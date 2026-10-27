using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using XieBox.HealthCheck;
using XieBox.GarbageCleaner;
using XieBox.NetworkDiagnostics;
using XieBox.StartupManager;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.Views;

internal static partial class Program
{
    private sealed class HealthRecycleBin : IRecycleBin
    {
        public RecycleSnapshot Query() => new(0, 0);
        public string EmptyWithSystemConfirmation(IntPtr owner) => throw new Exception("Health must never empty the recycle bin");
    }
    private static void CheckHealth(string root, string preview, PlatformApi api)
    {
        var store = new LocalStore(Path.Combine(root, "health-host"));
        var platform = new MainViewModel(store, api, new AccountService(api, store), true);
        using var context = new ModuleContext(BuiltInModules.Health, platform.Accounts, () => throw new Exception("Health must work without login"));
        CheckMediaVerification(context, preview);
        HealthProbe[] probes = [
            new("磁盘空间", "disk-analyzer", "查看空间占用", _ => Task.FromResult(new HealthResult("磁盘空间", "disk-analyzer", "C:\\ 剩余空间不足 10%", "查看空间占用", HealthState.Suggested, "C:\\"))),
            new("内存占用", "memory-release", "查看内存占用", _ => Task.FromResult(new HealthResult("内存占用", "memory-release", "当前使用 42%", "查看内存占用", HealthState.Normal))),
            new("启动项", "startup-manager", "管理启动项", _ => throw new IOException("fixture unavailable")),
            new("网络连接", "network-diagnostics", "查看排查结果", _ => Task.FromResult(new HealthResult("网络连接", "network-diagnostics", "网络连接正常", "查看排查结果", HealthState.Normal)))
        ];
        HealthResult? destination = null;
        var page = HealthPage.Create(context, root, result => destination = result, probes);
        var model = (HealthModel)page.Model;
        RunUi(model.CheckAsync);
        Check(model.Results.Count == 4 && model.Results[2].State == HealthState.Incomplete && model.Results[3].State == HealthState.Normal, "health isolates failed probes and continues remaining checks");
        Check(model.CanExport && model.BuildReport().Contains("未完成") && model.BuildReport().Contains("内存占用"), "health report includes completed and incomplete results");
        RenderUtility(page, Path.Combine(preview, "health-results.png"));
        Check(!TabDescendants(page).OfType<Button>().Any(b => Equals(b.Content, "查看空间占用")), "health results omit individual module navigation buttons");
        var action = TabDescendants(page).OfType<Button>().Single(b => Equals(b.Content, "硬件信息"));
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(destination?.ModuleId == "hardware-check", "health header opens the unified hardware information entry");
        RenderUtility(page, Path.Combine(preview, "health-results.png"));
        RenderUtility(new HomeView(platform, health: page), Path.Combine(preview, "home-health-results.png"));
        bool laterRan = false;
        var cancelled = new HealthModel(context, [new("等待", "network-diagnostics", "查看", async token => { await Task.Delay(10000, token); throw new Exception("not cancelled"); }), new("后续", "disk-analyzer", "查看", _ => { laterRan = true; return Task.FromResult(model.Results[0]); })]);
        RunUi(async () => { var task = cancelled.CheckAsync(); Check(cancelled.IsBusy && !cancelled.CanExport, "health task is busy and export stays disabled"); cancelled.Cancel(); await task; });
        Check(!laterRan && cancelled.Results.All(r => r.State == HealthState.Incomplete) && cancelled.Heading == "体检已停止", "cancellation stops later probes and does not mark unchecked items healthy");

        var window = new MainWindow(platform, api) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        window.Show(); AwaitUi(window.Initialization);
        var open = typeof(MainWindow).GetMethod("OpenModule", BindingFlags.NonPublic | BindingFlags.Instance)!;
        typeof(MainWindow).GetMethod("ShowHome", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        var healthPage = ((ContentControl)window.FindName("PageHost")).Content;
        Check(healthPage is HomeView && platform.Modules.Any(m => m.Id == "health-check") && window.OpenTabs.Count == 0, "health appears in applications without starting on home");
        open.Invoke(window, [platform.Modules.Single(m => m.Id == "health-check")]);
        var homePanel = (HealthPanel)((ContentControl)window.FindName("PageHost")).Content;
        Check(!homePanel.Model.ResultsLayout && homePanel.Model.CheckedAt is null, "health opens on an idle landing page");
        RunUi(async () => { var task = homePanel.Model.CheckAsync(); Check(platform.IsBusy, "health tab participates in global busy state"); homePanel.Model.Cancel(); await task; });
        Check(!platform.IsBusy, "health cancellation clears global busy state");
        Check(BuiltInModules.Include([BuiltInModules.Health]).Count(m => m.Id == "health-check") == 1, "health catalog entries are deduplicated");
        var navigate = typeof(MainWindow).GetMethod("OpenHealthResult", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var network = new NetworkSession([new("HTTPS 连接", "已收到响应", "fixture", 200)], DateTimeOffset.Now);
        var networkResult = new HealthResult("网络", "network-diagnostics", "正常", "查看", HealthState.Normal, network);
        navigate.Invoke(window, [networkResult]);
        var networkPage = (XieBox.Platform.UI.ToolPage)((ContentControl)window.FindName("PageHost")).Content;
        Check(networkPage.Model is NetworkModel n && ReferenceEquals(n.Entries, network.Entries) && n.ResultsLayout && !n.IsBusy, "network handoff reuses the health snapshot without another request");
        navigate.Invoke(window, [networkResult]);
        Check(window.OpenTabs.Count(t => t.Module.Id == "network-diagnostics") == 1 && ReferenceEquals(networkPage, ((ContentControl)window.FindName("PageHost")).Content), "health links reuse existing tabs");
        var engine = new CleanerEngine(new CleanerPaths(Path.Combine(root, "health-local"), Path.Combine(root, "health-roaming")), new HealthRecycleBin(), _ => false);
        var snapshot = engine.Scan([], null, default);
        navigate.Invoke(window, [new HealthResult("清理", "garbage-cleaner", "fixture", "查看", HealthState.Normal, new CleanerSession(engine, snapshot))]);
        var cleaner = (CleanerView)((ContentControl)window.FindName("PageHost")).Content;
        Check(cleaner.Model.HasScanLayout && cleaner.Model.HasScanned && !cleaner.Model.IsBusy && cleaner.Model.SelectedCount == 0, "cleaner handoff exposes results without automatic selection or deletion");
        Check(engine.BuildPlan(snapshot, []).Files.Count == 0, "cleaner snapshot retains its originating engine validation");
        navigate.Invoke(window, [new HealthResult("启动项", "startup-manager", "fixture", "查看", HealthState.Normal, new StartupScan([], 0))]);
        Check(((StartupView)((ContentControl)window.FindName("PageHost")).Content).Model.Opened, "startup handoff opens results without rescanning");
        typeof(MainWindow).GetMethod("ShowHome", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        Check(ReferenceEquals(healthPage, ((ContentControl)window.FindName("PageHost")).Content), "health page is preserved after visiting sub-applications");
        window.Close();
    }
}
