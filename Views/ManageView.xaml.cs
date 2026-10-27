using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using XieBox.Models;
using XieBox.ViewModels;
namespace XieBox.Views;
public partial class ManageView : UserControl
{
    private readonly MainViewModel _model;
    private readonly Action<ModuleItem> _open;
    private Point _pressPoint;
    private Border? _pressedHandle;
    private ModuleItem? _dragged;
    private int _insertionIndex = -1;
    private readonly Dictionary<ModuleItem, double> _rowTops = [];
    private readonly Dictionary<ModuleItem, double> _previewOffsets = [];
    private Image? _dragPreview;
    private Border? _dropPlaceholder;
    private Point _grabOffset;
    private double _dragHeight;
    private bool _dropCommitted;
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    public ManageView(MainViewModel model, Action<ModuleItem> open)
    {
        InitializeComponent(); DataContext = model; _model = model; _open = open;
        // Navigation's default collection view filters hidden items; management must always keep every item accessible.
        ApplicationList.ItemsSource = new ListCollectionView(model.Modules);
        _scrollTimer.Tick += (_, _) => AutoScroll();
        Unloaded += (_, _) => { _scrollTimer.Stop(); _pressedHandle?.ReleaseMouseCapture(); _pressedHandle = null; ClearPreview(); };
    }
    private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_model.IsIdle) return;
        _pressedHandle = (Border)sender; _pressedHandle.Focus();
        _pressedHandle.CaptureMouse();
        _pressPoint = e.GetPosition(this); e.Handled = true;
    }
    private void Handle_MouseUp(object sender, MouseButtonEventArgs e) { _pressedHandle?.ReleaseMouseCapture(); _pressedHandle = null; }
    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _pressedHandle?.ReleaseMouseCapture(); _pressedHandle = null; return; }
        if (_dragged is not null || _pressedHandle is null || !ReferenceEquals(sender, _pressedHandle) || !_model.IsIdle || _pressedHandle.Tag is not ModuleItem module) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var container = ApplicationList.ItemContainerGenerator.ContainerFromItem(module) as FrameworkElement;
        try
        {
            _dragged = module;
            _dropCommitted = false;
            BeginPreview(module, e.GetPosition(ApplicationList));
            _pressedHandle.ReleaseMouseCapture();
            if (container is not null) container.Opacity = 0;
            _scrollTimer.Start();
            DragDrop.DoDragDrop(_pressedHandle, new DataObject(typeof(ModuleItem), module), DragDropEffects.Move);
        }
        finally
        {
            _scrollTimer.Stop();
            if (container is not null) container.Opacity = 1;
            if (!_dropCommitted) AnimateReorder(() => { });
            ClearPreview();
            _dragged = null; _pressedHandle = null; _insertionIndex = -1;
            InsertionLine.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }
    private bool IsOwnDrag(DragEventArgs e) => !_dropCommitted && _model.IsIdle && _dragged is not null && ReferenceEquals(e.Data.GetData(typeof(ModuleItem)), _dragged);
    protected override void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
        base.OnGiveFeedback(e);
        // Native drag feedback continues even when the cursor is over a non-drop target.
        if (_dropCommitted || _dragged is null || _dragPreview is null || !IsLoaded || !GetCursorPos(out var cursor)) return;
        UpdateDragLocation(ApplicationList.PointFromScreen(new Point(cursor.X, cursor.Y)));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
    private bool IsInsideList(Point point)
        => new Rect(ApplicationList.RenderSize).Contains(point)
        && new Rect(ManagementScroll.RenderSize).Contains(ApplicationList.TranslatePoint(point, ManagementScroll));
    private void UpdateDragLocation(Point point)
    {
        if (_dropCommitted) return;
        UpdatePreviewPosition(point);
        if (_model.IsIdle && IsInsideList(point)) UpdateInsertion(point.Y);
        else LeaveDropTarget();
    }
    private void LeaveDropTarget()
    {
        _insertionIndex = -1;
        InsertionLine.Visibility = Visibility.Collapsed;
        if (_dropPlaceholder is not null) _dropPlaceholder.Visibility = Visibility.Collapsed;
        ResetPreviewOffsets();
    }
    private void List_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (!IsOwnDrag(e)) { InsertionLine.Visibility = Visibility.Collapsed; return; }
        var point = e.GetPosition(ApplicationList);
        UpdateDragLocation(point);
        if (IsInsideList(point)) e.Effects = DragDropEffects.Move;
    }
    private void UpdateInsertion(double y)
    {
        _insertionIndex = _model.Modules.Count;
        for (var i = 0; i < _model.Modules.Count; i++)
        {
            if (ApplicationList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) continue;
            var top = _rowTops.GetValueOrDefault(_model.Modules[i], row.TranslatePoint(new Point(), ApplicationList).Y);
            if (y >= top + row.ActualHeight / 2) continue;
            _insertionIndex = i; break;
        }
        var old = _dragged is null ? -1 : _model.Modules.IndexOf(_dragged);
        if (old < 0) return;
        var target = _insertionIndex > old ? _insertionIndex - 1 : _insertionIndex;
        var lineY = _rowTops.GetValueOrDefault(_model.Modules[target]);
        if (target > old && ApplicationList.ItemContainerGenerator.ContainerFromIndex(target) is FrameworkElement targetRow)
            lineY += targetRow.ActualHeight - _dragHeight;
        for (var i = 0; i < _model.Modules.Count; i++)
        {
            if (i == old) continue;
            var offset = i > old && i <= target ? -_dragHeight : i < old && i >= target ? _dragHeight : 0;
            SetPreviewOffset(_model.Modules[i], offset);
        }
        if (_dropPlaceholder is not null) { Canvas.SetTop(_dropPlaceholder, lineY); _dropPlaceholder.Visibility = Visibility.Visible; }
        InsertionLine.Width = ApplicationList.ActualWidth;
        Canvas.SetTop(InsertionLine, Math.Max(0, lineY - 3));
        InsertionLine.Visibility = _insertionIndex == old || _insertionIndex == old + 1 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void List_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (!IsOwnDrag(e) || !IsInsideList(e.GetPosition(ApplicationList))) return;
        CommitDrop(e.GetPosition(ApplicationList));
        e.Effects = DragDropEffects.Move;
        InsertionLine.Visibility = Visibility.Collapsed;
    }
    private void CommitDrop(Point point)
    {
        if (_dropCommitted || _dragged is null || !_model.IsIdle || !IsInsideList(point)) return;
        UpdateInsertion(point.Y);
        // End hover handling before Move/UpdateLayout can raise drag-leave or feedback events.
        _dropCommitted = true;
        _scrollTimer.Stop();
        AnimateReorder(() => _model.MoveToInsertion(_dragged, _insertionIndex));
    }
    private void List_DragLeave(object sender, DragEventArgs e)
    {
        if (_dropCommitted) return;
        var point = e.GetPosition((IInputElement)sender);
        var bounds = new Rect(((FrameworkElement)sender).RenderSize);
        if (!bounds.Contains(point))
        {
            UpdatePreviewPosition(e.GetPosition(ApplicationList));
            LeaveDropTarget();
        }
    }
    private void AutoScroll()
    {
        if (_dropCommitted || _dragged is null || !_model.IsIdle) return;
        var point = Mouse.GetPosition(ManagementScroll);
        if (point.X < 0 || point.X > ManagementScroll.ActualWidth || point.Y < 0 || point.Y > ManagementScroll.ActualHeight) return;
        var delta = point.Y < 40 ? -12 : point.Y > ManagementScroll.ActualHeight - 40 ? 12 : 0;
        if (delta == 0) return;
        ManagementScroll.ScrollToVerticalOffset(ManagementScroll.VerticalOffset + delta);
        ManagementScroll.UpdateLayout();
        var listPoint = Mouse.GetPosition(ApplicationList);
        UpdateDragLocation(listPoint);
    }
    private void BeginPreview(ModuleItem module, Point point)
    {
        _rowTops.Clear(); _previewOffsets.Clear();
        foreach (var item in _model.Modules)
        {
            if (ApplicationList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row) continue;
            row.RenderTransform = Transform.Identity;
            _rowTops[item] = row.TranslatePoint(new Point(), ApplicationList).Y;
        }
        if (ApplicationList.ItemContainerGenerator.ContainerFromItem(module) is not FrameworkElement source) return;
        _dragHeight = source.ActualHeight;
        _grabOffset = new Point(point.X, point.Y - _rowTops[module]);
        var dpi = VisualTreeHelper.GetDpi(source);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        var card = VisualTreeHelper.GetChildrenCount(source) > 0 && VisualTreeHelper.GetChild(source, 0) is FrameworkElement child ? child : source;
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.Fill }, null, new Rect(0, 0, card.ActualWidth, card.ActualHeight));
        bitmap.Render(drawing); bitmap.Freeze();
        _dropPlaceholder = new Border { Width = source.ActualWidth, Height = Math.Max(1, _dragHeight - 8), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), IsHitTestVisible = false };
        _dropPlaceholder.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
        _dropPlaceholder.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        DropLayer.Children.Insert(0, _dropPlaceholder);
        _dragPreview = new Image { Source = bitmap, Width = source.ActualWidth, Height = source.ActualHeight, Opacity = 0.94, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.22 } };
        System.Windows.Data.BindingOperations.SetBinding(_dragPreview.Effect, DropShadowEffect.ColorProperty,
            new System.Windows.Data.Binding("Foreground.Color") { Source = this });
        PreviewLayer.Children.Add(_dragPreview);
        UpdatePreviewPosition(point); UpdateInsertion(point.Y);
        if (SystemParameters.ClientAreaAnimation)
            _dragPreview.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 0.94, (Duration)FindResource("Motion.FeedbackDuration")));
    }
    private void UpdatePreviewPosition(Point point)
    {
        if (_dragPreview is null) return;
        _dragPreview.Visibility = Visibility.Visible;
        var location = ApplicationList.TranslatePoint(new Point(point.X - _grabOffset.X, point.Y - _grabOffset.Y), PreviewLayer);
        Canvas.SetLeft(_dragPreview, location.X);
        Canvas.SetTop(_dragPreview, location.Y);
    }
    private void SetPreviewOffset(ModuleItem module, double offset)
    {
        if (_previewOffsets.TryGetValue(module, out var previous) && previous == offset) return;
        _previewOffsets[module] = offset;
        if (ApplicationList.ItemContainerGenerator.ContainerFromItem(module) is not FrameworkElement row) return;
        var transform = row.RenderTransform as TranslateTransform ?? new TranslateTransform();
        row.RenderTransform = transform;
        if (SystemParameters.ClientAreaAnimation)
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(transform.Y, offset, (Duration)FindResource("Motion.EnterDuration"))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { transform.BeginAnimation(TranslateTransform.YProperty, null); transform.Y = offset; }
    }
    private void ResetPreviewOffsets()
    {
        foreach (var item in _model.Modules) SetPreviewOffset(item, 0);
    }
    private void ClearPreview()
    {
        if (_dragPreview is not null) PreviewLayer.Children.Remove(_dragPreview);
        if (_dropPlaceholder is not null) DropLayer.Children.Remove(_dropPlaceholder);
        _dragPreview = null; _dropPlaceholder = null;
        _rowTops.Clear(); _previewOffsets.Clear();
        InsertionLine.Visibility = Visibility.Collapsed;
    }
    private void Handle_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt || !_model.IsIdle || ((Border)sender).Tag is not ModuleItem module) return;
        if (e.SystemKey is not (Key.Up or Key.Down)) return;
        AnimateReorder(() => _model.Move(module, e.SystemKey == Key.Up ? -1 : 1)); e.Handled = true;
    }
    private void AnimateReorder(Action reorder)
    {
        var positions = new Dictionary<ModuleItem, double>();
        foreach (var item in _model.Modules)
            if (ApplicationList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row)
                positions[item] = row.TranslatePoint(new Point(), ApplicationList).Y;
        if (_dragged is not null && _dragPreview is not null)
            positions[_dragged] = PreviewLayer.TranslatePoint(new Point(Canvas.GetLeft(_dragPreview), Canvas.GetTop(_dragPreview)), ApplicationList).Y;
        // Retain current visual positions, then remove preview offsets before calculating the new layout.
        foreach (var item in _model.Modules)
            if (ApplicationList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row)
                row.RenderTransform = Transform.Identity;
        reorder(); ApplicationList.UpdateLayout();
        foreach (var item in _model.Modules)
        {
            if (!positions.TryGetValue(item, out var before) || ApplicationList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row) continue;
            row.RenderTransform = Transform.Identity;
            if (!SystemParameters.ClientAreaAnimation) continue;
            var delta = before - row.TranslatePoint(new Point(), ApplicationList).Y;
            if (Math.Abs(delta) < 0.1) continue;
            var transform = new TranslateTransform(); row.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(delta, 0, (Duration)FindResource("Motion.EnterDuration"))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
    private void Open_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is ModuleItem module) _open(module); }
}
