using System.IO;
using System.Windows;
using System.Windows.Controls;
using XieBox.Views;
using XieBox.Platform.UI;

internal static partial class Program
{
    private static int CheckSupportPreview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Theme.xaml", UriKind.Relative) });
        var output = Path.GetFullPath("artifacts/support-preview"); Directory.CreateDirectory(output);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            new XieBox.Services.AppearanceSettings(() => theme == "Dark").Apply();
            using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/XieBroToolBox;component/Resources/Support/wechat.png"))!.Stream)
            {
                var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.StreamSource = resource; image.EndInit();
                Check(image.PixelWidth > 0, theme + " bundled QR image decodes");
            }
            var view = new SupportView();
            var window = new DialogWindow { Content = view, Width = 590, ShowActivated = false, ShowInTaskbar = false }.FitContent();
            window.Show(); window.UpdateLayout();
            try
            {
                Check(((StackPanel)view.FindName("WechatPanel")).IsVisible, theme + " shows WeChat directly");
                Check(!TabDescendants(view).OfType<RadioButton>().Any(), theme + " has no redundant payment or amount choices");
                Check(TabDescendants((StackPanel)view.FindName("WechatPanel")).OfType<Image>().Count(i => i.Source is not null) == 1 && !TabDescendants(view).OfType<TextBlock>().Any(t => t.Text == "二维码待添加"), theme + " displays the bundled WeChat code without a placeholder");
                Render((FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(window, 0), Path.Combine(output, "wechat-" + theme + ".png"), window.ActualWidth, window.ActualHeight);
                var about = new AboutView(); Render(about, Path.Combine(output, "about-" + theme + ".png"), 598, 236);
                Check(!TabDescendants(about).OfType<Button>().Any(b => Equals(b.Content, "给蟹兄买杯奶茶") || Equals(b.Content, "支持作者")), theme + " about no longer duplicates the support entry");
            }
            finally { window.Close(); }
        }
        return 0;
    }
}
