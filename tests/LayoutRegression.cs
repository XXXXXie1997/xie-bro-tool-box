using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XieBox.HardwareCheck;
using XieBox.Platform.UI;
using XieBox.Services;

internal static partial class Program
{
    private sealed class WaitingHardware : IHardwareSource
    {
        public readonly ManualResetEventSlim Ready = new(false);
        private readonly FixtureHardware _fixture = new();
        public HardwareSnapshot ReadInventory(CancellationToken token, IProgress<string>? progress)
        {
            progress?.Report("正在读取硬件配置…"); Ready.Wait(token);
            var snapshot = _fixture.ReadInventory(token, progress);
            return snapshot with { Sections = snapshot.Sections.Concat(new[] {
                new HardwareSection("电池", "电池", new[] { new HardwareField("读取状态", "未检测到电池。") }),
                new HardwareSection("显示器", "显示器", new[] { new HardwareField("读取状态", "未检测到设备或设备未提供信息。 ") }),
                new HardwareSection("设备与驱动", "设备与驱动 · 补充信息", new[] { new HardwareField("读取状态", "此数据源暂不可用，可能需要管理员权限或设备支持。") })
            }).ToArray() };
        }
        public IReadOnlyList<SensorReading> ReadSensors(CancellationToken token) => _fixture.ReadSensors(token);
        public void CloseSensors() => _fixture.CloseSensors();
        public void Dispose() { Ready.Set(); _fixture.Dispose(); }
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T found) yield return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static int RunLayoutRegression()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "layout-regression", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(root);
        using var log = new StreamWriter(Path.Combine(root, "results.txt")) { AutoFlush = true };
        Console.SetOut(log); Console.SetError(log);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Theme.xaml", UriKind.Relative) });
            using var api = new PlatformApi(new Uri("http://127.0.0.1:1/"));
            var accounts = new AccountService(api, new LocalStore(Path.Combine(root, "data")));
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ThemePalette.Apply(new ResourceDictionary { Source = new Uri($"/XieBroToolBox;component/Resources/Palette.{theme}.xaml", UriKind.Relative) });
                using var context = new ModuleContext(BuiltInModules.Hardware, accounts, () => Task.CompletedTask);
                using var source = new WaitingHardware();
                var page = HardwarePage.Create(context, source);
                using var model = (HardwareModel)page.Model;
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                RunUi(async () => { model.SetActive(true); model.Start(); await Task.Delay(100); });
                Render(page, Path.Combine(root, theme + "-loading.png"), 1180, 700);
                var bar = Visuals<ProgressBar>(page).Single(b => b.ActualWidth > 500);
                Check(model.IsReading && bar.ActualWidth >= 1000 && bar.ActualHeight >= 4, theme + ": hardware reading progress spans available width");
                Check(bar.Parent is StackPanel stack && stack.Parent is StackPanel, theme + ": progress has a separate vertical row");
                source.Ready.Set();
                RunUi(async () => { for (var i = 0; i < 100 && model.IsReading; i++) await Task.Delay(20); });
                model.SetActive(false); RunUi(() => model.Completion);
                Render(page, Path.Combine(root, theme + "-cards.png"), 1180, 700);
                var cards = Visuals<Button>(page).Where(b => b.ToolTip is string text && text.EndsWith("的配置与实时状态")).ToArray();
                Check(cards.Length == 4, theme + ": inventory categories rendered as cards");
                Check(!cards.Any(c => c.ToolTip is "查看电池的配置与实时状态" or "查看显示器的配置与实时状态"), theme + ": absent device categories are hidden");
                Check(cards.Any(c => c.ToolTip is "查看设备与驱动的配置与实时状态"), theme + ": failed queries remain visible");
                foreach (var card in cards)
                {
                    var grid = (Grid)card.Content;
                    var icon = grid.Children.OfType<ModuleIcon>().Single();
                    var labels = grid.Children.OfType<StackPanel>().Single();
                    Check(icon.ActualWidth == 44 && Grid.GetColumn(labels) == 1 && card.ActualHeight <= 90, theme + ": large left icon, right labels, compact card height");
                }
                Render(page, Path.Combine(root, theme + "-narrow.png"), 680, 700);
                Check(cards.All(c => c.ActualWidth > 200 && c.ActualWidth < 330), theme + ": cards wrap at narrow width");
                var details = new Expander { Header = new TextBlock { Text = "DDR5-A1 · 示例设备", FontWeight = FontWeights.SemiBold }, Content = new TextBlock { Text = "容量    16 GB\n厂商    示例厂商", Margin = new Thickness(16) }, IsExpanded = true, Style = (Style)page.FindResource("HardwareExpander") };
                Render(details, Path.Combine(root, theme + "-detail.png"), 720, 140);
                var surface = Visuals<Border>(details).Single(b => b.Name == "HeaderSurface");
                Check(surface.Background is SolidColorBrush && surface.BorderThickness.Bottom == 1, theme + ": device heading has theme background and divider");
                page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            }
            RunUi(() => { CheckHardware(root, accounts); return Task.CompletedTask; });
            Console.WriteLine($"PASS: {_checks} checks; no interactive test windows opened.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
