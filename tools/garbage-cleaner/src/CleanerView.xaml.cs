using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using XieBox.Platform.UI;

namespace XieBox.GarbageCleaner;

public partial class CleanerView : UserControl
{
    private const double DetailsWidth = 880;
    private const double DetailsHeight = 620;
    public CleanerViewModel Model { get; }
    private bool _scanLayoutShown;
    private DateTime _layoutAnimationUntil;
    public CleanerView(CleanerViewModel model)
    {
        InitializeComponent(); Model = model; DataContext = model; model.LoadSettings();
        model.PropertyChanged += ModelChanged;
        Loaded += (_, _) => { model.PropertyChanged -= ModelChanged; model.PropertyChanged += ModelChanged; if (model.HasScanLayout) ShowScanLayout(); else ShowWelcomeLayout(); };
        Unloaded += (_, _) => model.PropertyChanged -= ModelChanged;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Model.HasScanLayout)) { if (Model.HasScanLayout) ShowScanLayout(); else ShowWelcomeLayout(); }
        if (e.PropertyName == nameof(Model.HasResults) && Model.HasResults && Model.HasScanLayout) Reveal(ResultsList);
    }
    private void ShowWelcomeLayout()
    {
        if (!_scanLayoutShown) return;
        _scanLayoutShown = false;
        _layoutAnimationUntil = DateTime.MinValue;
        ResultsPanel.IsHitTestVisible = false;
        void Finish()
        {
            if (Model.HasScanLayout) return;
            ResultsPanel.Visibility = Visibility.Hidden;
            ResultsPanel.BeginAnimation(OpacityProperty, null);
            WelcomePanel.BeginAnimation(OpacityProperty, null);
            WelcomeTitle.Opacity = StartScanButton.Opacity = WelcomePanel.Opacity = 1;
            WelcomePanel.Visibility = Visibility.Visible;
            WelcomePanel.IsHitTestVisible = true;
            Reveal(WelcomePanel);
        }
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) { Finish(); return; }
        var fade = new DoubleAnimation(0, (Duration)FindResource("Motion.FeedbackDuration"));
        fade.Completed += (_, _) => Finish();
        ResultsPanel.BeginAnimation(OpacityProperty, fade);
    }
    private void ShowScanLayout()
    {
        if (_scanLayoutShown) return;
        _scanLayoutShown = true;
        ResultsPanel.BeginAnimation(OpacityProperty, null);
        ResultsPanel.Opacity = 1;
        ResultsPanel.IsHitTestVisible = true;
        var canAnimate = IsLoaded && SystemParameters.ClientAreaAnimation && WelcomeTitle.ActualWidth > 0;
        var titleOrigin = canAnimate ? WelcomeTitle.TranslatePoint(new Point(), this) : new Point();
        var buttonOrigin = canAnimate ? StartScanButton.TranslatePoint(new Point(), this) : new Point();
        ResultsPanel.Visibility = Visibility.Visible;
        ResultsPanel.UpdateLayout();
        if (!canAnimate) { WelcomePanel.Visibility = Visibility.Collapsed; return; }
        var layoutDuration = ((Duration)FindResource("Motion.LayoutDuration")).TimeSpan;
        _layoutAnimationUntil = DateTime.UtcNow + layoutDuration;
        foreach (var element in new FrameworkElement[] { HeaderDescription, SelectionToolbar, ResultFooter })
        {
            var end = layoutDuration + ((Duration)FindResource("Motion.EnterDuration")).TimeSpan;
            var appear = new DoubleAnimationUsingKeyFrames { Duration = end, FillBehavior = FillBehavior.Stop };
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, TimeSpan.Zero));
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, layoutDuration));
            appear.KeyFrames.Add(new EasingDoubleKeyFrame(1, end, (IEasingFunction)FindResource("Motion.EaseOut")));
            element.BeginAnimation(OpacityProperty, appear);
        }
        WelcomePanel.IsHitTestVisible = false;
        WelcomeTitle.Opacity = 0; StartScanButton.Opacity = 0;
        var fade = new DoubleAnimation(1, 0, (Duration)FindResource("Motion.FeedbackDuration"));
        fade.Completed += (_, _) => { if (Model.HasScanLayout) WelcomePanel.Visibility = Visibility.Collapsed; };
        WelcomePanel.BeginAnimation(OpacityProperty, fade);
        MoveFrom(ResultsTitle, titleOrigin, 34.0 / 26, 34.0 / 26);
        MoveFrom(RescanButton, buttonOrigin, StartScanButton.ActualWidth / RescanButton.ActualWidth, StartScanButton.ActualHeight / RescanButton.ActualHeight);
    }
    private void MoveFrom(FrameworkElement target, Point origin, double scaleX, double scaleY)
    {
        var destination = target.TranslatePoint(new Point(), this);
        var scale = new ScaleTransform(); var offset = new TranslateTransform();
        target.RenderTransform = new TransformGroup { Children = { scale, offset } };
        DoubleAnimation Animation(double from, double to) => new(from, to, (Duration)FindResource("Motion.LayoutDuration"))
        { EasingFunction = (IEasingFunction)FindResource("Motion.EaseOut"), FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animation(scaleX, 1));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animation(scaleY, 1));
        offset.BeginAnimation(TranslateTransform.XProperty, Animation(origin.X - destination.X, 0));
        offset.BeginAnimation(TranslateTransform.YProperty, Animation(origin.Y - destination.Y, 0));
    }
    private void Reveal(FrameworkElement target)
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        target.RenderTransform = new TranslateTransform();
        var enter = ((Storyboard)FindResource("Motion.Enter")).Clone();
        var remaining = _layoutAnimationUntil - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero) { target.Opacity = 0; enter.BeginTime = remaining; }
        enter.Completed += (_, _) => target.Opacity = 1;
        enter.Begin(target, HandoffBehavior.SnapshotAndReplace);
    }
    private async void Scan_Click(object sender, RoutedEventArgs e) => await Model.ScanAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Model.Cancel();
    private void All_Click(object sender, RoutedEventArgs e) => Model.SelectAll();
    private void None_Click(object sender, RoutedEventArgs e) => Model.SelectNone();
    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var plan = Model.CreatePlan();
        if (plan is null) return;
        var names = Model.Categories.Where(c => c.Selected).Select(c => "• " + c.Name + "\n  " + c.Rule.Consequence);
        var message = $"即将永久删除所选文件，文件总大小 {Format.Bytes(plan.Bytes)}。不会移到回收站。\n\n" + string.Join("\n\n", names)
            + (plan.Recycle ? "\n\n回收站会由 Windows 再次确认。请留意其中是否有需要恢复的个人文件。" : "") + "\n\n确定开始清理？";
        if (!DialogWindow.Confirm(Window.GetWindow(this), message, "确认清理范围", "确认清理")) return;
        await Model.CleanAsync(plan, new WindowInteropHelper(Window.GetWindow(this)).Handle);
    }
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not CategoryResult category) return;
        ShowDetails(category, Window.GetWindow(this));
    }
    private void ShowDetails(CategoryResult category, Window owner)
    {
        if (category.IsGroup)
        {
            var window = new DialogWindow { Owner = owner, Title = "浏览器缓存 · 清理明细", Width = DetailsWidth, Height = DetailsHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            window.Content = new BrowserCacheDetails(category, browser => ShowDetails(browser, window));
            window.ShowDialog();
            return;
        }
        if (category.IsRecycleBin) { Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true }); return; }
        var grid = new DataGrid { ItemsSource = category.Files, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended, IsReadOnly = false, EnableRowVirtualization = true, Margin = new Thickness(20) };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "清理", Binding = new System.Windows.Data.Binding("Selected") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = 68, ElementStyle = (Style)FindResource("TableCheckBox"), EditingElementStyle = (Style)FindResource("TableCheckBox") });
        grid.Columns.Add(new DataGridTextColumn { Header = "文件路径", Binding = new System.Windows.Data.Binding("Path"), IsReadOnly = true, ElementStyle = (Style)FindResource("TableText"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "大小", Binding = new System.Windows.Data.Binding("Size"), IsReadOnly = true, ElementStyle = (Style)FindResource("TableSize"), Width = 120 });
        var panel = new DockPanel();
        var text = new TextBlock { Text = category.Consequence + "\n取消勾选的文件会保留；列表只包含此次扫描找到的文件。", Margin = new Thickness(20, 20, 20, 0), TextWrapping = TextWrapping.Wrap };
        var notice = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(24, 24, 24, 0), Child = text };
        notice.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
        text.Margin = new Thickness(0); text.FontSize = 13; text.LineHeight = 23;
        grid.Margin = new Thickness(24, 16, 24, 24);
        DockPanel.SetDock(notice, Dock.Top); panel.Children.Add(notice); panel.Children.Add(grid);
        var details = new DialogWindow { Owner = owner, Title = category.Name + " · 清理明细", Width = DetailsWidth, Height = DetailsHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        if (owner.Content is BrowserCacheDetails)
        {
            // Match the current parent bounds even after the user has moved or resized it.
            details.Width = owner.ActualWidth;
            details.Height = owner.ActualHeight;
            details.WindowStartupLocation = WindowStartupLocation.Manual;
            details.Left = owner.Left;
            details.Top = owner.Top;
            details.WindowState = owner.WindowState;
        }
        details.ShowDialog();
    }
    private void Exclude_Click(object sender, RoutedEventArgs e)
    {
        var list = new ListBox { ItemsSource = Model.Exclusions, Margin = new Thickness(20), MinHeight = 160 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 20, 20) };
        var drop = new DirectoryDropZone { Margin = new Thickness(20, 16, 20, 0) };
        drop.PathSelected += path => Model.AddExclusion(path);
        var remove = new Button { Content = "移除所选" };
        var panel = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); DockPanel.SetDock(drop, Dock.Top); panel.Children.Add(drop); panel.Children.Add(list);
        var window = new DialogWindow { Owner = Window.GetWindow(this), Title = "排除目录", Width = 660, Height = 490, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        remove.Click += (_, _) => { if (list.SelectedItem is string path) Model.RemoveExclusion(path); };
        buttons.Children.Add(remove); window.ShowDialog();
    }
    private void Result_Click(object sender, RoutedEventArgs e)
    {
        if (Model.LastResult is not { } result) return;
        var grid = new DataGrid { ItemsSource = result.Outcomes, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Margin = new Thickness(20), EnableRowVirtualization = true };
        grid.Columns.Add(new DataGridTextColumn { Header = "文件路径", Binding = new System.Windows.Data.Binding("Path"), ElementStyle = (Style)FindResource("TableText"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "处理结果", Binding = new System.Windows.Data.Binding("Reason"), ElementStyle = (Style)FindResource("TableText"), Width = 280 });
        var panel = new DockPanel(); var text = new TextBlock { Text = $"成功 {result.DeletedCount} · 跳过 {result.SkippedCount} · 未执行 {result.NotAttempted}\n" + result.RecycleStatus + "\n此记录不是文件备份，无法撤销永久删除。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 20, 20, 0) };
        var notice = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(24, 24, 24, 0), Child = text };
        notice.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
        text.Margin = new Thickness(0); text.FontSize = 13; text.LineHeight = 23;
        grid.Margin = new Thickness(24, 16, 24, 24);
        DockPanel.SetDock(notice, Dock.Top); panel.Children.Add(notice); panel.Children.Add(grid);
        new DialogWindow { Owner = Window.GetWindow(this), Title = "本次清理结果", Width = 900, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel }.ShowDialog();
    }
}
