using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XieBox.Platform.UI;
namespace XieBox.FileOperations;

internal sealed class NamePartDrag(Grid root, StackPanel list, ScrollViewer scroll, RenameSettings rules) : IDisposable
{
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Dictionary<FrameworkElement, double> _tops = [];
    private FrameworkElement? _handle, _row;
    private NamePart? _part;
    private Border? _ghost;
    private Point _press, _grab;
    private bool _dragging, _finishing;
    private int _target = -1;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    public void Initialize()
    {
        Grid.SetRowSpan(_overlay, 3); Panel.SetZIndex(_overlay, 100); root.Children.Add(_overlay);
        _timer.Tick += (_, _) => { if (!_dragging || _finishing) return; var p = Mouse.GetPosition(scroll); if (p.X >= 0 && p.X <= scroll.ActualWidth) { if (p.Y < 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 12); else if (p.Y > scroll.ActualHeight - 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 12); } Update(); };
    }
    public void Attach(FrameworkElement handle, FrameworkElement row, NamePart part)
    {
        handle.Cursor = Cursors.SizeNS; handle.Focusable = true; handle.ToolTip = "拖动调整顺序；Alt + ↑ / ↓ 也可移动";
        System.Windows.Automation.AutomationProperties.SetName(handle, "调整顺序：" + part.Label);
        handle.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _handle is not null) { Finish(false); e.Handled = true; } else if (!_dragging && Keyboard.Modifiers == ModifierKeys.Alt && e.Key is Key.Up or Key.Down) { rules.Move(part, e.Key == Key.Up ? -1 : 1); e.Handled = true; } };
        handle.PreviewMouseLeftButtonDown += (_, e) => { if (_handle is not null) return; _handle = handle; _row = row; _part = part; _press = e.GetPosition(root); _grab = e.GetPosition(row); handle.Focus(); handle.CaptureMouse(); e.Handled = true; };
        handle.PreviewMouseMove += (_, e) =>
        {
            if (_handle != handle || _finishing) return;
            if (e.LeftButton != MouseButtonState.Pressed) { Finish(false); return; }
            var p = e.GetPosition(root);
            if (!_dragging && (Math.Abs(p.X - _press.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _press.Y) > SystemParameters.MinimumVerticalDragDistance)) Begin();
            if (_dragging) Update();
        };
        handle.PreviewMouseLeftButtonUp += (_, e) => { if (_handle == handle) { if (_dragging) Update(); Finish(_target >= 0); e.Handled = true; } };
        handle.LostMouseCapture += (_, _) => { if (_handle == handle && !_finishing) Finish(false); };
    }
    private void Begin()
    {
        if (_row is null || _part is null) return;
        _dragging = true; _tops.Clear();
        foreach (FrameworkElement child in list.Children) { _tops[child] = child.TranslatePoint(new Point(), list).Y; child.RenderTransform = new TranslateTransform(); }
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(ToolPage.Text("☰", 16)); var text = ToolPage.Text(_part.Label + (_part.Kind == "text" ? " · " + _part.Value : "")); text.Margin = new Thickness(14, 0, 0, 0); label.Children.Add(text);
        _ghost = new Border { Child = label, Width = _row.ActualWidth, Height = _row.ActualHeight, Padding = new Thickness(16, 8, 16, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5), Opacity = .96 };
        _ghost.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); _ghost.SetResourceReference(Border.BorderBrushProperty, "AccentInkBrush"); _overlay.Children.Add(_ghost); _row.Opacity = .25; _timer.Start();
    }
    private void Update()
    {
        if (_ghost is null || _row is null || _part is null || _finishing) return;
        var p = Mouse.GetPosition(root); Canvas.SetLeft(_ghost, Math.Clamp(p.X - _grab.X, 0, Math.Max(0, root.ActualWidth - _ghost.Width))); Canvas.SetTop(_ghost, p.Y - _grab.Y);
        var point = Mouse.GetPosition(list); var viewport = Mouse.GetPosition(scroll);
        var old = rules.Parts.IndexOf(_part); var next = -1;
        if (new Rect(list.RenderSize).Contains(point) && new Rect(scroll.RenderSize).Contains(viewport))
        {
            var insertion = list.Children.Count;
            for (var i = 0; i < list.Children.Count; i++) { var child = (FrameworkElement)list.Children[i]; if (point.Y < _tops[child] + child.ActualHeight / 2) { insertion = i; break; } }
            next = insertion > old ? insertion - 1 : insertion;
        }
        if (_target == next) return; _target = next;
        var height = _row.ActualHeight + _row.Margin.Top + _row.Margin.Bottom;
        for (var i = 0; i < list.Children.Count; i++)
        {
            var child = (FrameworkElement)list.Children[i];
            var y = next < 0 || i == old ? 0 : old < next && i > old && i <= next ? -height : old > next && i >= next && i < old ? height : 0;
            var transform = (TranslateTransform)child.RenderTransform;
            if (SystemParameters.ClientAreaAnimation) transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }); else transform.Y = y;
        }
    }
    private void Finish(bool commit)
    {
        if (_handle is null || _finishing) return;
        _finishing = true; _timer.Stop();
        var part = _part; var target = _target; var wasDragging = _dragging;
        _handle.ReleaseMouseCapture(); if (_row is not null) _row.Opacity = 1;
        foreach (var child in _tops.Keys) child.RenderTransform = Transform.Identity;
        _overlay.Children.Clear(); _ghost = null; _handle = null; _row = null; _part = null; _dragging = false; _target = -1; _tops.Clear();
        if (commit && wasDragging && part is not null && target >= 0 && target < rules.Parts.Count) { var old = rules.Parts.IndexOf(part); if (old >= 0 && old != target) rules.Parts.Move(old, target); }
        _finishing = false;
    }
    public void Dispose() { Finish(false); _timer.Stop(); root.Children.Remove(_overlay); }
}
