using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XieBox.Platform.UI;

namespace XieBox.StartupManager;
public partial class StartupView : UserControl
{
    public StartupModel Model { get; }
    public StartupView(StartupModel model)
    {
        Model = model; InitializeComponent(); DataContext = model;
        Loaded += (_, _) => { Model.PropertyChanged += Changed; UpdatePage(); };
        Unloaded += (_, _) => Model.PropertyChanged -= Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Model.Opened)) UpdatePage(); }
    private void UpdatePage()
    {
        Intro.Visibility = Model.Opened ? Visibility.Collapsed : Visibility.Visible;
        Results.Visibility = Model.Opened ? Visibility.Visible : Visibility.Collapsed;
        var target = Model.Opened ? (FrameworkElement)Results : Intro;
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        target.RenderTransform = new TranslateTransform();
        ((Storyboard)FindResource("Motion.Enter")).Clone().Begin(target);
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Model.RefreshAsync();
    private void Filter_Checked(object sender, RoutedEventArgs e) { if (Model is not null && ((RadioButton)sender).Tag is string tag && int.TryParse(tag, out var filter)) Model.Filter = filter; }
    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        toggle.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        if (toggle.Tag is not StartupEntry entry || Model.IsBusy) return;
        if (DialogWindow.Confirm(Window.GetWindow(this), entry.Name + "\n\n" + (entry.Enabled ? "关闭后，此应用不会通过这一启动项在登录时自动打开，仍可手动启动。配套通知或后台功能可能需要手动打开应用后才会生效。\n\n可随时在已禁用列表恢复。" : "恢复后，此应用将通过这一启动项随登录自动启动。"), entry.Action, entry.Action))
            await Model.ToggleAsync(entry);
    }
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not StartupEntry entry) return;
        new DialogWindow { Owner = Window.GetWindow(this), Title = entry.Name + " · 启动详情", Width = 640,
            Content = new ScrollViewer { Margin = new Thickness(24), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock { Text = $"{entry.Publisher}\n{entry.Location} · {entry.State}\n\n启动命令 / 路径\n{entry.Command}\n\n{entry.Note}", TextWrapping = TextWrapping.Wrap, LineHeight = 25 } } }.FitContent().ShowDialog();
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (Win32Exception) { new DialogWindow { Owner = Window.GetWindow(this), Title = "系统启动设置", Width = 440, Content = new TextBlock { Text = "无法打开系统设置，请在任务管理器的启动应用中查看。", Margin = new Thickness(24) } }.FitContent().ShowDialog(); }
    }
}

