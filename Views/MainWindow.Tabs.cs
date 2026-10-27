using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XieBox.Models;

namespace XieBox.Views;

public partial class MainWindow
{
    public static readonly DependencyProperty NavigationTabWidthProperty = DependencyProperty.Register(
        nameof(NavigationTabWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(180d));
    public double NavigationTabWidth { get => (double)GetValue(NavigationTabWidthProperty); private set => SetValue(NavigationTabWidthProperty, value); }
    private double TabStride => NavigationTabWidth + 6;
    private void NavigationStrip_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateNavigationTabWidth();
    private void UpdateNavigationTabWidth()
    {
        if (NavigationStrip is null || NavigationStrip.ActualWidth <= 0) return;
        NavigationTabWidth = OpenTabs.Count == 0 ? 180 : Math.Clamp(Math.Floor(NavigationStrip.ActualWidth / OpenTabs.Count) - 6, 64, 180);
    }
    private AppTab? _dragTab;
    private Point _tabPointerStart;
    private AppTab[]? _tabOriginalOrder;
    private bool _tabDragging;
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != sender; node = VisualTreeHelper.GetParent(node))
            if (node is Button { Name: "CloseTabButton" }) return;
        if (((Border)sender).Tag is not AppTab tab) return;
        _dragTab = tab; _tabPointerStart = e.GetPosition(NavigationItems); _tabOriginalOrder = OpenTabs.ToArray();
    }
    private void Tabs_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(NavigationItems);
        if (!_tabDragging)
        {
            if (Math.Abs(point.X - _tabPointerStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _tabDragging = true; NavigationItems.CaptureMouse(); OpenModule(_dragTab.Module);
        }
        var viewportPoint = e.GetPosition(NavigationScroll);
        if (viewportPoint.Y < -16 || viewportPoint.Y > NavigationScroll.ActualHeight + 16) { e.Handled = true; return; }
        if (viewportPoint.X < 24) NavigationScroll.ScrollToHorizontalOffset(NavigationScroll.HorizontalOffset - 12);
        else if (viewportPoint.X > NavigationScroll.ActualWidth - 24) NavigationScroll.ScrollToHorizontalOffset(NavigationScroll.HorizontalOffset + 12);
        // Use the current compact width, independent of visual animation offsets.
        var destination = Math.Clamp((int)(point.X / TabStride), 0, OpenTabs.Count - 1);
        MoveTab(_dragTab, destination); e.Handled = true;
    }
    public void MoveTab(AppTab tab, int destination)
    {
        var old = OpenTabs.IndexOf(tab);
        if (old < 0 || destination < 0 || destination >= OpenTabs.Count || old == destination) return;
        var positions = OpenTabs.ToDictionary(t => t, t => OpenTabs.IndexOf(t) * TabStride);
        OpenTabs.Move(old, destination); NavigationItems.UpdateLayout();
        if (!SystemParameters.ClientAreaAnimation) return;
        foreach (var item in OpenTabs)
        {
            if (NavigationItems.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container) continue;
            var transform = new TranslateTransform(); container.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(positions[item] - OpenTabs.IndexOf(item) * TabStride, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
    private void Tabs_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_tabDragging)
        {
            var point = e.GetPosition(NavigationScroll);
            FinishTabDrag(point.Y < -16 || point.Y > NavigationScroll.ActualHeight + 16 || point.X < 0 || point.X > NavigationScroll.ActualWidth);
            e.Handled = true;
        }
        else { _dragTab = null; _tabOriginalOrder = null; }
    }
    private void Tabs_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && _tabDragging) { FinishTabDrag(true); e.Handled = true; } }
    private void Tabs_LostCapture(object sender, MouseEventArgs e) { if (_tabDragging && ReferenceEquals(e.OriginalSource, NavigationItems) && !NavigationItems.IsMouseCaptured) FinishTabDrag(true); }
    private void FinishTabDrag(bool cancel)
    {
        _tabDragging = false;
        if (cancel && _tabOriginalOrder is not null)
            for (var i = 0; i < _tabOriginalOrder.Length; i++) MoveTab(_tabOriginalOrder[i], i);
        _dragTab = null; _tabOriginalOrder = null; NavigationItems.ReleaseMouseCapture();
    }
}


