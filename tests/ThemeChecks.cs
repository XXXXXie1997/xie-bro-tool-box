using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XieBox.Platform.UI;
using XieBox.Services;

internal static partial class Program
{
    private static void CheckThemeSwitch()
    {
        var light = new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Palette.Light.xaml", UriKind.Relative) };
        var alternate = new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Palette.Dark.xaml", UriKind.Relative) };
        Check(light.Keys.Cast<object>().OrderBy(k => k).SequenceEqual(alternate.Keys.Cast<object>().OrderBy(k => k)), "dark and light palettes have identical semantic keys");
        var settingsStore = new LocalStore(System.IO.Path.Combine("artifacts", "platform-tests", Guid.NewGuid().ToString("N")));
        var settings = new AppearanceSettings(() => true);
        Check(settings.IsDark, "startup follows system dark mode");
        settings.Toggle();
        Check(!settings.IsDark, "manual switch overrides system mode for current session");
        Check(((System.Windows.Media.Imaging.BitmapImage)Application.Current.Resources["AppLogo"]).UriSource.ToString().EndsWith("/logo.png"), "light theme selects purple logo");
        settings.Toggle();
        var goldLogo = (System.Windows.Media.Imaging.BitmapImage)Application.Current.Resources["AppLogo"];
        Check(goldLogo.UriSource.ToString().EndsWith("/logo-dark.png") && goldLogo.PixelWidth > 0 && goldLogo.IsFrozen, "dark theme loads the gold logo resource");
        settings.Toggle();
        Check(new AppearanceSettings(() => true).IsDark, "next launch follows system again");
        Check(!new AppearanceSettings(() => false).IsDark, "startup follows system light mode");
        Check(!new AppearanceSettings(() => throw new System.UnauthorizedAccessException()).IsDark, "unreadable system preference falls back to light");
        ThemePalette.Apply(light);
        ThemePalette.Apply(alternate, animate: true);
        var transitioning = (SolidColorBrush)Application.Current.FindResource("CanvasBrush");
        Check(!SystemParameters.ClientAreaAnimation || transitioning.HasAnimatedProperties, "theme switch animates semantic palette brushes when motion is enabled");
        AwaitUi(Task.Delay(70));
        var beforeReverse = transitioning.Color;
        ThemePalette.Apply(light, animate: true);
        var reversed = (SolidColorBrush)Application.Current.FindResource("CanvasBrush");
        Check(!SystemParameters.ClientAreaAnimation || reversed.Color == beforeReverse, "rapid theme reversal starts from the current displayed color");
        AwaitUi(Task.Delay(350));
        Check(reversed.Color == ((SolidColorBrush)light["CanvasBrush"]).Color, "theme animation settles on the final palette");
        var window = new Window { Width = 480, Height = 320, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        var panel = new StackPanel(); window.Content = panel;
        var input = new TextBox { Text = "主题切换验证" }; panel.Children.Add(input);
        var password = new PasswordBox { Password = "test-value" }; panel.Children.Add(password);
        var multiline = new TextBox { Text = "第一行\n第二行", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(multiline);
        var label = new TextBlock { Text = "状态" }; ThemeResources.SetForegroundKey(label, "SuccessBrush"); panel.Children.Add(label);
        var border = new Border { Height = 24, BorderThickness = new Thickness(1) }; ThemeResources.SetBorderKey(border, "SuccessBrush"); panel.Children.Add(border);
        window.Show(); window.UpdateLayout();
        Check(input.Template.FindName("InputFrame", input) is Border { CornerRadius.TopLeft: 8 } && password.Template.FindName("InputFrame", password) is Border { CornerRadius.TopLeft: 8 }, "shared text and password inputs use rounded borders");
        input.Select(0, 2); input.SelectedText = "输入";
        Check(input.Text.StartsWith("输入") && password.Password == "test-value" && multiline.LineCount >= 2, "rounded templates retain text editing password storage and multiline layout");
        Render(panel, System.IO.Path.Combine("artifacts", "previews", "inputs-light.png"), 460, 260);
        var dialog = new DialogWindow { Owner = window, Width = 300, Height = 160, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        dialog.Show(); dialog.UpdateLayout();
        try
        {
            ThemePalette.Apply(alternate); AwaitUi(Task.Delay(50)); window.UpdateLayout(); dialog.UpdateLayout();
            Check(((SolidColorBrush)window.Background).Color == ((SolidColorBrush)alternate["CanvasBrush"]).Color && ((SolidColorBrush)input.Background).Color == ((SolidColorBrush)alternate["SurfaceBrush"]).Color, "palette replacement updates existing windows and inputs without recreating them");
            Check(((SolidColorBrush)input.Foreground).Color == ((SolidColorBrush)alternate["InkBrush"]).Color && ((SolidColorBrush)dialog.Foreground).Color == ((SolidColorBrush)alternate["InkBrush"]).Color, "palette replacement updates foreground in open dialogs and controls");
            Check(((SolidColorBrush)label.Foreground).Color == ((SolidColorBrush)alternate["SuccessBrush"]).Color && ((SolidColorBrush)border.BorderBrush).Color == ((SolidColorBrush)alternate["SuccessBrush"]).Color, "semantic status resources update after palette replacement");
                        using (var api = new PlatformApi(new Uri("http://127.0.0.1:1/")))
            {
                var model = new XieBox.ViewModels.MainViewModel(settingsStore, api, new AccountService(api, settingsStore));
                var appearance = new AppearanceSettings(() => true);
                var shell = new XieBox.Views.MainWindow(model, api, appearance) { ShowActivated = false, ShowInTaskbar = false };
                shell.Show(); shell.UpdateLayout();
                var toggle = (System.Windows.Controls.Primitives.ToggleButton)shell.FindName("ThemeButton");
                Check(toggle.IsChecked == true && System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(toggle), "titlebar switch reflects dark state and accepts clicks");
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                AwaitUi(Task.Delay(350));
                Check(!appearance.IsDark && toggle.IsChecked == false && ((SolidColorBrush)((Border)shell.Content).Background).Color == ((SolidColorBrush)light["CanvasBrush"]).Color, "titlebar switch changes open window to light");
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                AwaitUi(Task.Delay(350));
                Check(appearance.IsDark && toggle.IsChecked == true && ((SolidColorBrush)((Border)shell.Content).Background).Color == ((SolidColorBrush)alternate["CanvasBrush"]).Color, "titlebar switch changes open window back to dark");
                shell.RequestExit();
                RenderUtility(new XieBox.Views.HomeView(model), System.IO.Path.Combine("artifacts", "previews", "home-dark.png"));
            }
            Render(new XieBox.Views.AboutView(), System.IO.Path.Combine("artifacts", "previews", "about-dark.png"), 598, 236);
            Render(panel, System.IO.Path.Combine("artifacts", "previews", "inputs-dark.png"), 460, 260);
            ThemeResources.SetForegroundKey(label, "ErrorBrush");
            Check(((SolidColorBrush)label.Foreground).Color == ((SolidColorBrush)alternate["ErrorBrush"]).Color, "state changes can switch semantic keys after theme changes");
            try { ThemePalette.Apply(new ResourceDictionary()); throw new Exception("incomplete palette accepted"); }
            catch (ArgumentException) { Check(((SolidColorBrush)window.Background).Color == ((SolidColorBrush)alternate["CanvasBrush"]).Color, "invalid palette is rejected without changing the active theme"); }
        }
        finally { ThemePalette.Apply(light); dialog.Close(); window.Close(); }
        Render(new XieBox.Views.AboutView(), System.IO.Path.Combine("artifacts", "previews", "about-light.png"), 598, 236);
        Check(((SolidColorBrush)Application.Current.FindResource("CanvasBrush")).Color == ((SolidColorBrush)light["CanvasBrush"]).Color, "light palette restores cleanly after runtime switching");
    }
}









