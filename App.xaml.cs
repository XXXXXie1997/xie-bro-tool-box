using System.IO;
using System.Text.Json;
using System.Windows;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.Views;

namespace XieBox;
public partial class App : Application
{
    private PlatformApi? _api;
    private DesktopInstance? _instance;
    public Action? CleanupOnExit { get; set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) => DiagnosticLog.Error("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => DiagnosticLog.Write("UnhandledException", args.ExceptionObject.ToString());
        TaskScheduler.UnobservedTaskException += (_, args) => DiagnosticLog.Error("UnobservedTaskException", args.Exception);
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--startup-worker" && int.TryParse(e.Args[2], out var startupParent))
        {
            try { Shutdown(XieBox.StartupManager.StartupElevation.RunWorker(e.Args[1], startupParent)); }
            catch (Exception error) { DiagnosticLog.Error("StartupWorker", error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--slim-worker" && int.TryParse(e.Args[2], out var slimParent))
        {
            try { Shutdown(XieBox.GarbageCleaner.SystemSlimService.RunWorker(e.Args[1], slimParent)); }
            catch (Exception error) { DiagnosticLog.Error("SystemSlimWorker", error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--hardware-worker" && int.TryParse(e.Args[2], out var parentId))
        {
            DiagnosticLog.Initialize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XieBox"));
            try { Shutdown(XieBox.HardwareCheck.HardwareWorker.Run(e.Args[1], parentId)); }
            catch (Exception error) { DiagnosticLog.Error("HardwareWorker", error); Shutdown(1); }
            return;
        }
        try
        {
#if PORTABLE_RELEASE
            // Preserve the portable data location; desktop account access is disabled by DesktopFeatures.
            var settings = new DesktopSettings("https://www.xie-bro.top/xie-bro-tool-box/",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XieBoxDemo"));
#else
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "Resources", "platformsettings.json");
            if (!File.Exists(settingsPath)) settingsPath = Path.Combine(AppContext.BaseDirectory, "platformsettings.json");
            var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(settingsPath), LocalStore.Json)!;
#endif
            var store = new LocalStore(Environment.GetEnvironmentVariable("XIEBOX_DATA_DIR") ?? settings.DataDirectory
                ?? (settings.DemoEdition ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XieBoxDemo") : null));
            DiagnosticLog.Initialize(store.Root);
            if (!e.Args.Contains("--smoke-test"))
            {
                _instance = new DesktopInstance(store.Root);
                if (!_instance.IsPrimary) { DiagnosticLog.Write("ActivateExistingInstance"); _instance.ActivateExisting(); Shutdown(0); return; }
            }
            _api = new(new Uri(Environment.GetEnvironmentVariable("XIEBOX_API_URL") ?? settings.ApiBaseUrl));
            var accounts = new AccountService(_api, store);
            var appearance = new AppearanceSettings(); appearance.Apply();
            var window = new MainWindow(new MainViewModel(store, _api, accounts, settings.DemoEdition), _api, appearance);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (e.Args.Contains("--smoke-test") && e.Args.Contains("--headless"))
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1180, 820));
                content.Arrange(new Rect(0, 0, 1180, 820));
                Shutdown(0); return;
            }
            if (e.Args.Contains("--smoke-test"))
                window.ContentRendered += async (_, _) => { await window.Initialization; Shutdown(0); };
            else
            {
                _instance!.Listen(() =>
                {
                    if (!Dispatcher.HasShutdownStarted)
                        Dispatcher.BeginInvoke(new Action(() => { if (!Dispatcher.HasShutdownStarted) window.RestoreWindow(); }));
                });
                SessionEnding += (_, _) => window.PrepareForSessionEnd();
            }
            window.Show();
        }
        catch (Exception error)
        {
            DiagnosticLog.Error("StartupFailure", error);
            if (e.Args.Contains("--smoke-test")) Console.Error.WriteLine(error);
            else MessageBox.Show("蟹小箱暂时无法启动，请检查配置文件和数据目录。\n" + error.Message, "蟹小箱", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Write("Exit", $"Code={e.ApplicationExitCode}");
        _api?.Dispose(); DiagnosticLog.Stop();
        try { CleanupOnExit?.Invoke(); }
        finally { _instance?.Dispose(); base.OnExit(e); }
    }
    private sealed record DesktopSettings(string ApiBaseUrl, string? DataDirectory, bool DemoEdition = false);
}



