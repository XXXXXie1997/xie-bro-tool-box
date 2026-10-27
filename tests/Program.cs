using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using XieBox.Contracts;
using XieBox.Models;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.Views;

internal static partial class Program
{
    private static int _checks;
    private static int _failedGroups;
    private static void TestGroup(string name, Action action)
    {
        try { action(); } catch (Exception e) { _failedGroups++; Console.Error.WriteLine($"FAIL GROUP {name}: {e}"); }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--logs-check")) return CheckLogDirectory();
        if (args.Contains("--support-preview")) return CheckSupportPreview();
        if (args.Contains("--layout-regression")) return RunLayoutRegression();
        if (args.Contains("--spacemap-preview")) { RenderSpaceMapPreview(); return 0; }
        if (args.Contains("--cleanup-only")) { CheckPersonalDataCleanup(); return 0; }
        if (args.Length == 3 && args[0] == "--hardware-worker") return XieBox.HardwareCheck.HardwareWorker.Run(args[1], int.Parse(args[2]));
        if (args.Contains("--hardware-probe")) { ProbeHardware(); return 0; }
        if (args.Contains("--memory-fixture"))
        {
            var bytes = new byte[128 * 1024 * 1024];
            for (var i = 0; i < bytes.Length; i += 4096) bytes[i] = 42;
            Console.WriteLine("ready"); Console.ReadLine(); GC.KeepAlive(bytes); return 0;
        }
        var logIndex = Array.IndexOf(args, "--log");
        using var report = logIndex >= 0 ? new StreamWriter(args[logIndex + 1], false) { AutoFlush = true } : null;
        if (report is not null) { Console.SetOut(report); Console.SetError(report); args = args.Where((_, index) => index != logIndex && index != logIndex + 1).ToArray(); }
        var root = Path.GetFullPath(Path.Combine("artifacts", "platform-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        if (args.Contains("--account-only")) { RunAccountTests(root).GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--account-preview"))
        {
            var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            previewApp.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Theme.xaml", UriKind.Relative) });
            new AppearanceSettings(() => args.Contains("--dark")).Apply();
            using var previewApi = new PlatformApi(new Uri("http://127.0.0.1:1/"));
            var directory = Path.GetFullPath(args.Contains("--dark") ? "artifacts/account-email-preview-dark" : "artifacts/account-email-preview");
            Directory.CreateDirectory(directory);
            foreach (var mode in new[] { "login", "register", "reset", "profile" })
            {
                var previewService = new AccountService(previewApi, new LocalStore(root));
                if (mode == "profile") typeof(AccountService).GetProperty("Current")!.SetValue(previewService, new UserContext("fixture-internal-id", "hello@example.com", true, "hello@example.com", "AB12CD34"));
                var previewAccount = new AccountWindow(previewService, previewApi);
                if (mode == "register") typeof(AccountWindow).GetMethod("SetMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(previewAccount, ["register"]);
                if (mode == "reset")
                {
                    ((Button)previewAccount.FindName("ForgotPassword")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                previewAccount.ApplyTemplate();
                previewAccount.ShowActivated = false;
                previewAccount.Show();
                previewAccount.UpdateLayout();
                var previewHeight = previewAccount.ActualHeight;
                previewAccount.Arrange(new Rect(0, 0, 440, previewHeight));
                Render((FrameworkElement)VisualTreeHelper.GetChild(previewAccount, 0), Path.Combine(directory, mode + ".png"), 440, previewHeight);
                if (mode == "profile")
                {
                    Check(((TextBlock)previewAccount.FindName("PublicUid")).Text == "UID  AB12CD34", "profile shows the public UID instead of internal ID");
                    Check(previewHeight < 500 && ((Button)previewAccount.FindName("ChangePassword")).ActualHeight > 0, "profile fits content and exposes password change");
                }
                else
                {
                    Check(((TextBlock)previewAccount.FindName("IdentityLabel")).Text == (mode == "login" ? "邮箱或已有账号" : "邮箱"), mode + " uses email identity");
                    Check(((Button)previewAccount.FindName("Submit")).ActualHeight > 0, mode + " has a visible submit button");
                }
                previewAccount.Close();
            }
            Console.WriteLine("Account previews: " + directory);
            return 0;
        }
        try
        {
            DiagnosticLog.Initialize(root);
            try { throw new InvalidOperationException("diagnostic-fixture"); }
            catch (Exception error) { DiagnosticLog.Error("TestFailure", error); }
            var log = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "logs"), "*.log").Single());
            Check(log.Contains("diagnostic-fixture") && log.Contains("Program.Main") && log.Contains("Started"), "diagnostic log records version and exception stack locally");
            RunPackageTests(root).GetAwaiter().GetResult();
            RunAccountTests(root).GetAwaiter().GetResult();
            RunDownloadTests(root).GetAwaiter().GetResult();
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Theme.xaml", UriKind.Relative) });
            CheckThemeSwitch();
            CheckContentDialogSizing();
            if (args.Contains("--dark")) ThemePalette.Apply(new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Palette.Dark.xaml", UriKind.Relative) });
            var store = new LocalStore(Path.Combine(root, "desktop"));
            using var api = new PlatformApi(new Uri("http://127.0.0.1:1/"));
            var accounts = new AccountService(api, store);
            var model = new MainViewModel(store, api, accounts);
            File.WriteAllText(Path.Combine(store.Root, "session.dat"), "offline-session-sentinel");
            model.InitializeAsync().GetAwaiter().GetResult();
            Check(!model.ShowAccount && !accounts.Current.IsAuthenticated && model.Connection == "离线模式", "production desktop starts offline without an account entry");
            Check(File.ReadAllText(Path.Combine(store.Root, "session.dat")) == "offline-session-sentinel", "offline startup does not read, clear or refresh saved account sessions");
            using (var offlineContext = new ModuleContext(BuiltInModules.All.First(), accounts, () => throw new Exception("offline app must not open sign-in")))
                offlineContext.RequestSignInAsync().GetAwaiter().GetResult();
            Check(BuiltInModules.All.SelectMany(m => m.Capabilities).All(c => c.Policy == "free"), "all shipped capabilities remain available offline without membership");
            Check(model.Modules.All(m => m.IsInstalled && !m.CanInstall), "all shipped applications open without installation");
            Check(!Directory.Exists(Path.Combine(store.Root, "modules")), "bundled availability does not create installation manifests");
            File.WriteAllText(Path.Combine(store.Root, "catalog-cache.json"), "invalid old catalog");
            Check(new MainViewModel(store, api, accounts).Modules.Count == BuiltInModules.All.Count, "old catalog cannot block or add downloadable applications");
            var demoStore = new LocalStore(Path.Combine(root, "demo"));
            var demo = new MainViewModel(demoStore, api, new AccountService(api, demoStore), demoEdition: true);
            Check(!demo.ShowAccount && demo.Modules.Select(m => m.Id).Order().SequenceEqual(BuiltInModules.All.Select(m => m.Id).Order()),
                "demo hides account entry and includes all implemented modules");
            demo.RefreshCatalogAsync().GetAwaiter().GetResult();
            Check(demo.Modules.Count == BuiltInModules.All.Count, "demo catalog refresh remains offline and cannot add planned modules");
            model.Modules.Single(x => x.Id == "garbage-cleaner").IsVisible = false;
            model.Move(model.Modules.Single(m => m.Id == "network-diagnostics"), 1 - model.Modules.IndexOf(model.Modules.Single(m => m.Id == "network-diagnostics")));
            var restored = new MainViewModel(store, api, accounts);
            Check(!restored.Modules.Single(x => x.Id == "garbage-cleaner").IsVisible, "hidden navigation persisted");
            Check(restored.Modules[1].Id == "network-diagnostics", "navigation order persisted");
            var free = new ModuleContext(model.Modules.Single(x => x.Id == "garbage-cleaner").Definition, accounts, () => Task.CompletedTask);
            Check(free.CheckCapabilityAsync("scan").GetAwaiter().GetResult().Allowed, "guest free capability works without a server");
            Check(!free.CheckCapabilityAsync("unknown").GetAwaiter().GetResult().Allowed, "unknown capability denied");
            model.Move(model.Modules.Single(x => x.Id == "file-organizer"), -1);
            foreach (var module in model.Modules) module.IsVisible = true;
            var preview = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine("artifacts", args.Contains("--dark") ? "previews-dark" : "previews"));
            var builtIn = model.Modules.Single(x => x.Id == "garbage-cleaner");
            model.Selected = builtIn;
            model.InstallSelectedAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check(builtIn.IsInstalled && model.Installer.IsInstalled(builtIn.Definition), "bundled cleaner installs offline through platform module lifecycle");
            model.SetToolBusy(true); model.Uninstall(builtIn);
            Check(builtIn.IsInstalled && !model.IsIdle, "active cleaner task prevents uninstall and marks host busy");
            model.SetToolBusy(false); model.Uninstall(builtIn);
            Check(builtIn.IsInstalled, "bundled cleaner stays available without an uninstall lifecycle");
            Directory.CreateDirectory(preview);
            CheckFileScanner(root, preview, model, accounts);
            TestGroup("startup-memory", () => CheckUtilityModules(root, preview, model, accounts));
            TestGroup("disk", () => CheckDiskAnalyzer(root, preview, model, accounts));
            TestGroup("files-network-software", () => CheckExpandedTools(root, preview, model, accounts));
            RunUi(() => { CheckHardware(preview, accounts); return Task.CompletedTask; });
            TestGroup("health", () => CheckHealth(root, preview, api));
            TestGroup("manage", () => CheckManageOrdering(root, preview, api));
            TestGroup("tabs", () => CheckTabsAndFavorites(root, preview, api));
            TestGroup("reports", () => CheckUnifiedReports(root, api));
            Render(new HomeView(model), Path.Combine(preview, "home.png"), 1178, 676);
            Render(new ManageView(model, _ => { }), Path.Combine(preview, "manage.png"), 1178, 638);
            model.Selected = model.Modules.Single(x => x.Id == "garbage-cleaner");
            Render(new ModuleView(model), Path.Combine(preview, "module.png"), 1178, 638);
            var window = new MainWindow(model, api);
            Render(window.Content as FrameworkElement ?? throw new InvalidOperationException(), Path.Combine(preview, "platform.png"), 1180, 820);
            CheckWindowChrome(window);
            CheckCloseLifecycle(root, api);
            CheckDesktopInstance(root);
            var account = new AccountWindow(accounts, api);
            CheckDialogs(account, preview);
            CheckScrollBars(preview);
            CheckPixelScrolling();
            CheckStableGutter();
            Check(true, "all WPF views constructed and rendered");
            Console.WriteLine($"PASS: {_checks} platform checks. Previews: {preview}");
            Console.WriteLine($"Failed groups: {_failedGroups}");
            return _failedGroups == 0 ? 0 : 1;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void CheckFileScanner(string root, string preview, MainViewModel platform, AccountService accounts)
    {
        var folder = Path.Combine(root, "scanner-fixture");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllText(Path.Combine(folder, "nested", "image.JPG"), "image fixture");
        File.WriteAllText(Path.Combine(folder, "movie.MP4"), "video fixture longer");
        File.WriteAllText(Path.Combine(folder, "movie.mp4.txt"), "not a video");
        var large = Path.Combine(folder, "large.bin");
        using (var file = File.Create(large)) file.SetLength(50L * 1024 * 1024 + 1);
        var engine = new XieBox.FileScanner.ScanEngine();
        var scan = engine.Scan(new(folder, false, 100, [".jpg", ".mp4"]), null, default);
        Check(scan.Files.Count == 2 && scan.Files.Any(f => f.Name == "image.JPG") && !scan.Files.Any(f => f.Name.EndsWith(".txt")),
            "scanner recursively matches selected extensions case-insensitively and excludes misleading suffixes");
        Check(scan.Files[0].Bytes >= scan.Files[1].Bytes, "scanner results sort by numeric size descending");
        var largeScan = engine.Scan(new(folder, true, 50, []), null, default);
        Check(largeScan.Files.Count == 1 && largeScan.Files[0].Path == large && !XieBox.FileScanner.ScanEngine.MatchesSize(50L * 1024 * 1024, 50),
            "large-file scan enforces a strict greater-than threshold");
        try { engine.Scan(new(folder, true, 49, []), null, default); throw new Exception("threshold accepted"); }
        catch (ArgumentException) { Check(true, "engine rejects thresholds below 50 MB"); }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Check(engine.Scan(new(folder, true, 50, []), null, cancelled.Token).Cancelled, "cancelled scan returns a stopped result");
        Check(File.ReadAllText(Path.Combine(folder, "nested", "image.JPG")) == "image fixture" && new FileInfo(large).Length == 50L * 1024 * 1024 + 1,
            "file scanning leaves fixture contents unchanged");
        var module = platform.Modules.Single(m => m.Id == BuiltInModules.Scanner.Id);
        platform.Selected = module;
        platform.InstallSelectedAsync(default).GetAwaiter().GetResult();
        Check(module.IsInstalled && platform.Installer.IsInstalled(module.Definition), "file scanner installs through the offline module lifecycle");
        using var context = new ModuleContext(module.Definition, accounts, () => throw new Exception("Scanner must not require sign-in"));
        var model = new XieBox.FileScanner.ScannerModel(context);
        var view = new XieBox.FileScanner.ScannerView(model);
        Render(view, Path.Combine(preview, "scanner-start.png"), 1178, 706);
        model.Directory = folder; model.Threshold = "49";
        model.ScanAsync().GetAwaiter().GetResult();
        Check(!model.ResultsLayout && model.Status.Contains("50"), "invalid threshold stays on the setup page with feedback");
        model.LargeFiles = false;
        Render(view, Path.Combine(preview, "scanner-formats.png"), 1178, 706);
        void RunCustomScan()
        {
            var savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { AwaitUi(model.ScanAsync()); }
            finally { SynchronizationContext.SetSynchronizationContext(savedContext); }
        }
        Check(XieBox.FileScanner.ScanEngine.ParseExtensions("jpg .JPG，*.png; pdf；txt、webp").SequenceEqual(new[] { ".jpg", ".png", ".pdf", ".txt", ".webp" }), "custom formats normalize prefixes separators case and duplicates");
        model.CustomExtensions = "jpg";
        RunCustomScan();
        Check(model.Files.Count == 1 && model.Files[0].Name == "image.JPG" && model.Summary.Contains(".jpg"), "custom JPG alone scans recursively without selecting an entire category");
        model.EditConditions(); model.CustomExtensions = "*.mp4, jpg";
        RunCustomScan();
        Check(model.Files.Count == 2 && !model.Files.Any(f => f.Name.EndsWith(".txt")), "multiple custom formats match exact extensions");
        model.EditConditions(); model.CustomExtensions = "*";
        RunCustomScan();
        Check(!model.ResultsLayout && model.Status.Contains("格式填写不正确"), "invalid custom format stays on setup with actionable feedback");
        model.CustomExtensions = "jpg";
        model.Formats.Single(f => f.Name == "视频").Selected = true;
        RunCustomScan();
        Check(model.Files.Count == 2, "custom formats and selected categories combine without excluding either");
        model.EditConditions();
        File.WriteAllText(Path.Combine(folder, "archive.zip"), "zip fixture");
        File.WriteAllText(Path.Combine(folder, "photo.png"), "png fixture");
        model.Formats.Single(f => f.Name == "视频").Selected = false;
        model.Formats.Single(f => f.Name == "图片").Selected = true;
        model.CustomExtensions = "jpg png zip JPG zip";
        RunCustomScan();
        Check(model.Files.Count == 3 && model.Files.Select(f => f.Path).Distinct().Count() == 3 && model.Files.Any(f => f.Name == "archive.zip"), "image category plus overlapping JPG PNG and ZIP returns the unique union");
        Check(model.Summary == "图片、.zip · 包含子目录", "scan summary omits custom extensions already covered by a category");
        File.Delete(Path.Combine(folder, "archive.zip"));
        File.Delete(Path.Combine(folder, "photo.png"));
        model.Formats.Single(f => f.Name == "图片").Selected = false;
        model.EditConditions(); model.CustomExtensions = "";
        model.Formats.Single(f => f.Name == "视频").Selected = false;
        RunCustomScan();
        Check(!model.ResultsLayout && model.Status.Contains("填写"), "empty format selection does not start a scan");
        model.Formats.Single(f => f.Name == "图片").Selected = true;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            var task = model.ScanAsync();
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Check(model.Files.Count == 1 && model.ResultsLayout && !model.IsBusy, "offline scanner view model publishes filtered results and ends its busy state");
        var previewHost = new Window { Content = view, Width = 1180, Height = 760, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        previewHost.Show();
        var settle = new DispatcherFrame();
        var settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        settleTimer.Tick += (_, _) => { settleTimer.Stop(); settle.Continue = false; };
        settleTimer.Start(); Dispatcher.PushFrame(settle);
        Render(view, Path.Combine(preview, "scanner-results.png"), 1178, 706);
        previewHost.Content = null; previewHost.Close();
        model.EditConditions();
        Check(!model.ResultsLayout && model.Directory == folder && model.Formats.Any(f => f.Selected), "editing scan conditions retains directory and format choices");
        platform.Uninstall(module);
        Check(module.IsInstalled, "bundled scanner stays available");
    }
    private static void CheckPixelScrolling()
    {
        static T? Find<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T match) return match;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (Find<T>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
            return null;
        }
        static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        var rows = Enumerable.Range(0, 20000).Select(i => new { Name = "文件 " + i }).ToArray();
        foreach (var list in new ItemsControl[] { new ListBox { ItemsSource = rows, DisplayMemberPath = "Name" }, new DataGrid { ItemsSource = rows, IsReadOnly = true, CanUserAddRows = false } })
        {
            var host = new Window { Width = 640, Height = 400, Content = list, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            host.Show(); Pump(60);
            try
            {
                var viewer = Find<ScrollViewer>(list)!;
                viewer.ScrollToVerticalOffset(17); Pump(60);
                Check(Math.Abs(viewer.VerticalOffset - 17) < 0.1 && VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel,
                    "list and table accept a pixel offset between row boundaries");
                Check(list.ItemContainerGenerator.ContainerFromIndex(10000) is null,
                    "pixel scrolling preserves virtualization for long lists");
                var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                    { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                viewer.RaiseEvent(wheel); Pump(240);
                Check(wheel.Handled && viewer.VerticalOffset > 17, "smooth wheel handler scrolls both list and table content");
                Pump(180);
                var start = viewer.VerticalOffset;
                void Wheel(int delta) => viewer.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta)
                    { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                for (var i = 0; i < 5; i++) { Wheel(-120); Pump(30); }
                Pump(500);
                var step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16.0;
                Check(Math.Abs(viewer.VerticalOffset - (start + step * 5)) < 1,
                    "successive wheel events accumulate distance and settle without restarting or losing input");
                Wheel(120); Pump(400);
                Check(Math.Abs(viewer.VerticalOffset - (start + step * 4)) < 1,
                    "reverse wheel input settles at the expected pixel position");
                Wheel(-120); Pump(30);
                viewer.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(viewer)!, Environment.TickCount, System.Windows.Input.Key.Escape)
                    { RoutedEvent = UIElement.PreviewKeyDownEvent });
                Pump(30); var interrupted = viewer.VerticalOffset; Pump(300);
                Check(Math.Abs(viewer.VerticalOffset - interrupted) < 0.1, "direct keyboard interaction stops residual scroll motion");
            }
            finally { host.Close(); }
        }
    }
    private static void CheckScrollBars(string preview)
    {
        var viewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Visible, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Width = 160000, Height = 180000, Background = System.Windows.Media.Brushes.White } };
        var host = new Window { Width = 640, Height = 400, Content = viewer, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        host.Show(); host.UpdateLayout();
        try
        {
            foreach (var name in new[] { "PART_VerticalScrollBar", "PART_HorizontalScrollBar" })
            {
                var bar = (System.Windows.Controls.Primitives.ScrollBar)viewer.Template.FindName(name, viewer);
                bar.ApplyTemplate(); bar.UpdateLayout();
                var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
                Check(track.Thumb.Template.FindName("Slider", track.Thumb) is Border && track.Maximum > 0, "custom scrollbar has a visible thumb and scrollable range");
                var slider = (Border)track.Thumb.Template.FindName("Slider", track.Thumb);
                var vertical = bar.Orientation == Orientation.Vertical;
                var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(track.Thumb);
                Check(vertical ? track.Thumb.ActualHeight <= slot.Height && slider.ActualHeight >= 8 : track.Thumb.ActualWidth <= slot.Width && slider.ActualWidth >= 8,
                    "minimum scrollbar thumb fits its track allocation without clipping rounded ends");
                var before = bar.Orientation == Orientation.Vertical ? viewer.VerticalOffset : viewer.HorizontalOffset;
                track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(35, 35));
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var after = bar.Orientation == Orientation.Vertical ? viewer.VerticalOffset : viewer.HorizontalOffset;
                Check(after > before, "custom scrollbar thumb drag advances content in its orientation");
            }
            Render(viewer, Path.Combine(preview, "scrollbars.png"), 620, 360);
        }
        finally { host.Close(); }
    }
    private static void CheckDialogs(AccountWindow account, string preview)
    {
        static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        account.ShowActivated = false;
        account.Show();
        Pump(100);
        if (SystemParameters.ClientAreaAnimation) Check(account.Opacity > 0 && account.Opacity < 1, "account window fades in through intermediate opacity");
        Pump(300);
        Check(account.WindowStyle == WindowStyle.None && account.Title == "" && account.Opacity == 1, "account shares frameless dialog chrome without a title");
        CheckDialogDragging(account);
        var close = (Button)account.Template.FindName("DialogClose", account);
        Check(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(close), "dialog close control remains interactive inside draggable caption");
        Render((FrameworkElement)VisualTreeHelper.GetChild(account, 0), Path.Combine(preview, "account.png"), 440, 390);
        ((System.Windows.Input.RoutedCommand)close.Command).Execute(null, close.CommandTarget);
        if (SystemParameters.ClientAreaAnimation) Check(account.IsVisible, "close button keeps window alive until fade-out completes");
        Pump(240);
        Check(!account.IsVisible, "close button finishes fade-out and closes account window");

        var dialog = new XieBox.Platform.UI.DialogWindow { Title = "清理明细", Width = 640, Height = 360, ShowActivated = false,
            Content = new TextBlock { Text = "文件列表与清理说明", Margin = new Thickness(24) } };
        dialog.Show(); Pump(320);
        CheckDialogDragging(dialog);
        Check(((TextBlock)dialog.Template.FindName("DialogTitle", dialog)).Text == "清理明细", "other dialogs display their title in the shared caption");
        Render((FrameworkElement)VisualTreeHelper.GetChild(dialog, 0), Path.Combine(preview, "dialog.png"), 640, 360);
        System.Windows.Input.ApplicationCommands.Close.Execute(null, dialog);
        dialog.Close(); // Repeated close during a fade must not re-enter Close synchronously.
        Pump(240);
        Check(!dialog.IsVisible, "close command and repeated close complete without leaving an invisible modal window");

        var modal = new XieBox.Platform.UI.DialogWindow { Width = 300, Height = 180, ShowActivated = false };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        timer.Tick += (_, _) => { timer.Stop(); System.Windows.Input.ApplicationCommands.Close.Execute(null, modal); };
        timer.Start(); modal.ShowDialog();
        Check(!modal.IsVisible, "modal close command returns from ShowDialog after fading out");
    }
    private static void CheckDialogDragging(Window window)
    {
        int Hit(double x, double y)
        {
            var screen = window.PointToScreen(new Point(x, y));
            var coordinates = unchecked(((int)screen.Y << 16) | ((int)screen.X & 0xffff));
            return (int)SendMessage(new WindowInteropHelper(window).Handle, 0x0084, IntPtr.Zero, new IntPtr(coordinates));
        }
        Check(Hit(35, 24) == 2 && Hit(window.ActualWidth / 2, 24) == 2,
            "dialog title and empty caption support native dragging");
        Check(Hit(window.ActualWidth - 27, 24) == 1 && Hit(window.ActualWidth / 2, 100) == 1,
            "dialog close button and content remain interactive instead of dragging");
    }
    private static async Task RunPackageTests(string root)
    {
        var store = new LocalStore(Path.Combine(root, "packages"));
        var installer = new ModuleInstaller(store);
        var path = Path.Combine(root, "good.zip");
        CreatePackage(path, "page.json");
        var digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var module = new ModuleDefinition("test-module", "测试模块", "□", "测试", "0.1.0", "available", "content", true, [], [], Sha256: digest);
        await installer.InstallPackageAsync(module, path);
        Check(installer.IsInstalled(module), "trusted package installed");
        Check(installer.LoadContent(module).Title == "测试页面", "installed content loaded");
        await ExpectRejected(() => installer.InstallPackageAsync(module, path), "duplicate install rejected");
        installer.Uninstall(module.Id);
        Check(!installer.IsInstalled(module), "module uninstalled");
        await ExpectRejected(() => installer.InstallPackageAsync(module with { Sha256 = new string('0', 64) }, path), "tampered digest rejected");
        var malicious = Path.Combine(root, "evil.zip");
        CreatePackage(malicious, "../outside.json");
        var badHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(malicious)));
        await ExpectRejected(() => installer.InstallPackageAsync(module with { Sha256 = badHash }, malicious), "archive path traversal rejected");
        Check(!File.Exists(Path.Combine(store.Root, "modules", "outside.json")), "traversal did not write outside staging directory");
        foreach (var relative in new[] { "..\\evil", "C:\\evil", "/evil", "a:stream", "CON.json", "folder/../evil", "file. ", "folder//evil" })
        {
            try { ModuleInstaller.SafeEntry(root, relative); throw new Exception("Unsafe path accepted: " + relative); }
            catch (InvalidDataException) { Check(true, "unsafe path rejected: " + relative); }
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await installer.InstallPackageAsync(module, path, cancellation.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(!installer.IsInstalled(module), "cancelled install left no installed module"); }
    }
    private static async Task RunAccountTests(string root)
    {
        var url = Environment.GetEnvironmentVariable("XIEBOX_TEST_API_URL");
        if (url is null) return;
        using var api = new PlatformApi(new Uri(url));
        var store = new LocalStore(Path.Combine(root, "identity"));
        var account = new AccountService(api, store);
        account.SetAutoLogin(true);
        var continued = await account.ContinueAsync(Environment.GetEnvironmentVariable("XIEBOX_TEST_USERNAME")!, Environment.GetEnvironmentVariable("XIEBOX_TEST_PASSWORD")!);
        Check(continued.Step == "signed_in" && account.Current.IsAuthenticated, "desktop unified account service signs in against real API");
        var saved = await File.ReadAllBytesAsync(Path.Combine(store.Root, "session.dat"));
        Check(saved.Length > 0 && !System.Text.Encoding.UTF8.GetString(saved).Contains("accessToken"), "desktop credentials stored with Windows DPAPI");
        var second = new AccountService(api, store);
        await second.InitializeAsync();
        Check(second.Current.UserId == account.Current.UserId, "protected session restored and validated by API");
        var definition = new ModuleDefinition("identity-test", "身份测试", "□", "", "0.1.0", "available", "content", true, [], [new("local", "free")]);
        using var context1 = new ModuleContext(definition, second, () => Task.CompletedTask);
        using var context2 = new ModuleContext(definition with { Id = "other-module" }, second, () => Task.CompletedTask);
        var notifications = 0;
        context1.UserContextChanged += (_, _) => notifications++;
        context2.UserContextChanged += (_, _) => notifications++;
        await second.SignOutAsync();
        Check(notifications == 2 && !context1.CurrentUser.IsAuthenticated && !context2.CurrentUser.IsAuthenticated, "both modules receive platform sign-out without credentials");
        Check(!File.Exists(Path.Combine(store.Root, "session.dat")), "sign-out removes local protected credentials");
        Check((await context1.CheckCapabilityAsync("local")).Allowed, "free module continues after sign-out");
        var newUsername = "desktop-" + Guid.NewGuid().ToString("N") + "@example.test";
        var registration = new AccountService(api, new LocalStore(Path.Combine(root, "registration")));
        registration.SetAutoLogin(true);
        var registrationFlow = await registration.ContinueAsync(newUsername, "123456");
        Check(registrationFlow.Step == "verify_email" && registrationFlow.Challenge is not null, "unified entry requests verification for a new email");
        var pending = registrationFlow.Challenge!;
        Check(!registration.Current.IsAuthenticated, "desktop waits for email verification before accepting a session");
        var testData = Environment.GetEnvironmentVariable("XIEBOX_TEST_MAIL_DIRECTORY")!;
        using var mail = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(testData, pending.ChallengeId + ".json")));
        await registration.VerifyRegistrationAsync(pending.ChallengeId, mail.RootElement.GetProperty("code").GetString()!);
        Check(registration.Current.IsAuthenticated && registration.Current.Email == newUsername,
            "desktop registers verified email with six-character password");
        var registrationRestored = new AccountService(api, new LocalStore(Path.Combine(root, "registration")));
        await registrationRestored.InitializeAsync();
        Check(registrationRestored.Current.UserId == registration.Current.UserId && registrationRestored.Current.Username == newUsername,
            "new username and stable user ID survive protected session restore");
        await registration.SignOutAsync();
        var transientStore = new LocalStore(Path.Combine(root, "transient-login"));
        var transient = new AccountService(api, transientStore);
        await transient.SignInAsync(newUsername, "123456");
        Check(transient.Current.IsAuthenticated && !File.Exists(Path.Combine(transientStore.Root, "session.dat")), "login without auto-login writes no credentials");
        transient.SetAutoLogin(true);
        Check(File.Exists(Path.Combine(transientStore.Root, "session.dat")), "enabling auto-login persists protected session");
        transient.SetAutoLogin(false);
        Check(transient.Current.IsAuthenticated && !File.Exists(Path.Combine(transientStore.Root, "session.dat")), "disabling auto-login removes credentials without ending current use");
        Check(transient.Current.PublicId is { Length: 8 } && transient.Current.PublicId == registrationRestored.Current.PublicId, "eight-character public UID remains stable across login");
        await transient.SignOutAsync();
    }
    private static async Task RunDownloadTests(string root)
    {
        var url = Environment.GetEnvironmentVariable("XIEBOX_TEST_API_URL");
        var moduleId = Environment.GetEnvironmentVariable("XIEBOX_TEST_MODULE_ID");
        if (url is null || moduleId is null) return;
        using var api = new PlatformApi(new Uri(url));
        var catalog = await api.SendAsync<ModuleDefinition[]>("modules", HttpMethod.Get);
        var module = catalog.Single(x => x.Id == moduleId);
        var installer = new ModuleInstaller(new LocalStore(Path.Combine(root, "online-install")));
        await installer.InstallFromUrlAsync(module, null, CancellationToken.None);
        Check(installer.IsInstalled(module), "published module downloaded over real HTTP and installed");
        Check(installer.LoadContent(module).Title == "联网安装测试", "downloaded module opens through content runtime");
        installer.Uninstall(module.Id);
        Check(!installer.IsInstalled(module), "downloaded module can be uninstalled");
    }
    private static void CreatePackage(string path, string contentPath)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("module.json").Open())) writer.Write(JsonSerializer.Serialize(new ModuleManifest("test-module", "测试模块", "0.1.0", "content", "page.json"), LocalStore.Json));
        using (var writer = new StreamWriter(archive.CreateEntry(contentPath).Open())) writer.Write(JsonSerializer.Serialize(new ModuleContent("测试页面", "用于验证平台安装生命周期。", ["离线可用"]), LocalStore.Json));
    }
    private static async Task ExpectRejected(Func<Task> run, string name)
    {
        try { await run(); throw new Exception("Expected rejection: " + name); }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { Check(true, name); }
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); _checks++; Console.WriteLine("PASS: " + name); }
    private static void CheckWindowChrome(MainWindow window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.Show();
        try
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var handle = new WindowInteropHelper(window).Handle;
            int Hit(Point point)
            {
                var screen = window.PointToScreen(point);
                var coordinates = unchecked(((int)screen.Y << 16) | ((int)screen.X & 0xffff));
                return (int)SendMessage(handle, 0x0084, IntPtr.Zero, new IntPtr(coordinates));
            }
            Check(Hit(new Point(500, 28)) == 2, "empty title bar uses native window dragging");
            Check(Hit(new Point(12, 84)) == 2, "empty module navigation uses native window dragging");
            Check(Hit(new Point(75, 28)) == 2, "title text uses native window dragging");
            Check(Hit(new Point(52, 84)) == 1 && Hit(new Point(1110, 84)) == 1, "home and management buttons remain interactive");
            var aboutButton = TabDescendants(window).OfType<Button>().Single(b => Equals(b.Content, "关于"));
            var aboutCenter = aboutButton.TranslatePoint(new Point(aboutButton.ActualWidth / 2, aboutButton.ActualHeight / 2), window);
            Check(Hit(aboutCenter) == 1 && Hit(new Point(1148, 28)) == 1, "about and window control buttons remain interactive in offline layout");
            Check(!TabDescendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "登录 / 注册")), "offline title bar hides sign-in without leaving an interactive placeholder");
            Check(Hit(new Point(500, 300)) == 1, "body stays outside the draggable title area");
            foreach (var module in ((MainViewModel)window.DataContext).Modules) typeof(MainWindow).GetMethod("OpenModule", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [module]);
            window.UpdateLayout(); AwaitUi(Task.Delay(100));
            var navigation = (ScrollViewer)window.FindName("NavigationScroll");
            Check(navigation.ViewportHeight >= 36 && navigation.ScrollableWidth < 1 && window.NavigationTabWidth < 180, "many tabs shrink to fit without navigation controls");
            window.Width = 2400; window.UpdateLayout(); AwaitUi(Task.Delay(100));
            Check(window.NavigationTabWidth == 180 && navigation.HorizontalOffset < 1, "tabs expand to normal width when space is available");
            window.Width = 1000; window.UpdateLayout(); AwaitUi(Task.Delay(100));
            Check(window.NavigationTabWidth < 180 && navigation.ScrollableWidth < 1 && navigation.ViewportHeight >= 36, "narrow window keeps all current apps visible at compact widths");
            var moved = window.OpenTabs[0]; window.MoveTab(moved, window.OpenTabs.Count - 1);
            Check(window.OpenTabs.Last() == moved, "compressed tabs can be reordered");
        }
        finally { window.Close(); }
    }
    private static void CheckCloseLifecycle(string root, PlatformApi api)
    {
        var store = new LocalStore(Path.Combine(root, "close"));
        var model = new MainViewModel(store, api, new AccountService(api, store));
        var window = new MainWindow(model, api) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        window.Show();
        try
        {
            var content = ((ContentControl)window.FindName("PageHost")).Content;
            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Minimized;
            window.RestoreWindow();
            Check(window.WindowState == WindowState.Maximized && window.IsVisible, "relaunch restores a minimized window to its previous state");
            Check(ReferenceEquals(content, ((ContentControl)window.FindName("PageHost")).Content), "relaunch preserves the current page");
            window.Close();
            Check(!Application.Current.Windows.Cast<Window>().Contains(window), "close exits the idle desktop instead of hiding it");
        }
        finally { if (Application.Current.Windows.Cast<Window>().Contains(window)) window.Close(); }

        var busy = new MainWindow(model, api) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        busy.Show();
        try
        {
            void CloseWithChoice(string choice)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.Current.Windows.OfType<XieBox.Platform.UI.DialogWindow>().FirstOrDefault(w => w.Title == "退出蟹小箱");
                    if (dialog is null) return;
                    timer.Stop();
                    var panel = (DockPanel)dialog.Content;
                    var buttons = panel.Children.OfType<StackPanel>().Single();
                    buttons.Children.OfType<Button>().Single(b => Equals(b.Content, choice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                timer.Start();
                try { busy.Close(); } finally { timer.Stop(); }
            }
            model.SetToolBusy(true);
            CloseWithChoice("取消");
            Check(busy.IsVisible && model.IsBusy, "declining exit leaves the active task running");
            model.SetToolBusy(false);
            busy.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(busy.IsVisible, "declining exit does not schedule a later automatic exit");
            model.SetToolBusy(true);
            CloseWithChoice("退出");
            Check(busy.IsVisible && Application.Current.Windows.Cast<Window>().Contains(busy), "close keeps cancellation visible while an active operation stops");
            model.SetToolBusy(false);
            busy.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(!Application.Current.Windows.Cast<Window>().Contains(busy), "close automatically exits after cancellation without a second click");
        }
        finally { model.SetToolBusy(false); if (Application.Current.Windows.Cast<Window>().Contains(busy)) busy.Close(); }
    }
    private static void CheckDesktopInstance(string root)
    {
        var directory = Path.Combine(root, "instance");
        using var primary = new DesktopInstance(directory);
        using var activated = new ManualResetEventSlim();
        primary.Listen(() => activated.Set());
        using var second = new DesktopInstance(directory);
        Check(primary.IsPrimary && !second.IsPrimary, "relaunch shares one desktop instance per user and data directory");
        second.ActivateExisting();
        Check(activated.Wait(TimeSpan.FromSeconds(3)), "relaunch signals the background desktop to restore");
        using var isolated = new DesktopInstance(Path.Combine(root, "isolated-instance"));
        Check(isolated.IsPrimary, "separate test data directories keep independent desktop instances");
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    private static void Render(FrameworkElement element, string path, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle((Brush)Application.Current.FindResource("CanvasBrush"), null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}









