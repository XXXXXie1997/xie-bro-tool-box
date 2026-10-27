using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XieBox.Contracts;
using XieBox.GarbageCleaner;

internal static class Program
{
    private static int _checks;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--scan-current-readonly"))
        {
            var engine = new CleanerEngine(CleanerPaths.Current, new WindowsRecycleBin());
            var snapshot = engine.Scan([], new InlineProgress(Console.WriteLine), default);
            foreach (var category in snapshot.Categories) Console.WriteLine($"{category.Id}: {category.CountLabel}, {category.Size}, {category.Status}");
            Console.WriteLine(snapshot.Summary); return 0;
        }
        // All writes and destructive tests stay in this new workspace fixture. Never use CleanerPaths.Current here.
        var root = Path.GetFullPath(Path.Combine("artifacts", "cleaner-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            RunEngine(root);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Path.GetFullPath("Resources/Theme.xaml")) });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var fixture = new Fixture(Path.Combine(root, "ui"));
            fixture.Old("Temp/old.tmp"); fixture.Old("CrashDumps/editor.dmp"); fixture.Browser("Google/Chrome/User Data", "Default"); fixture.Browser("Quark/User Data", "Default");
            var model = new CleanerViewModel(fixture.Engine(), new GuestContext(), Path.Combine(root, "ui-state"));
            var view = new CleanerView(model);
            Render(view, "cleaner-start.png");
            Check(!model.HasScanned && (((FrameworkElement)view.FindName("WelcomePanel")).Visibility == Visibility.Visible) && !(((FrameworkElement)view.FindName("ResultsPanel")).Visibility == Visibility.Visible), "first visit shows centered scan entry without results controls");
            var firstScan = model.ScanAsync();
            Check(model.IsBusy && !model.HasScanned && model.HasScanLayout && ((FrameworkElement)view.FindName("ResultsPanel")).Visibility == Visibility.Visible, "starting first scan immediately switches to scanning layout before results exist");
            Pump(firstScan);
            Render(view, "cleaner-results.png");
            Check(model.HasScanned && !(((FrameworkElement)view.FindName("WelcomePanel")).Visibility == Visibility.Visible) && (((FrameworkElement)view.FindName("ResultsPanel")).Visibility == Visibility.Visible), "completed scan switches from entry to results layout");
            Check(model.HasResults && !model.CanClean && model.Categories.All(c => !c.Selected), "guest offline scan starts with every category unchecked");
            var browsers = model.Categories.Single(c => c.Id == "browser-cache");
            Check(model.Categories.Count == 6 && browsers.Name == "浏览器缓存" && browsers.Children.Count == 8, "main results combine all browsers into one browser-cache category");
            Check(browsers.Bytes == browsers.Children.Sum(c => c.Bytes) && browsers.Files.Count == 4, "combined browser category totals all detected caches without duplicates");
            browsers.Selected = true;
            var browserPlan = model.CreatePlan()!;
            Check(model.SelectedCount == 4 && browserPlan.Files.Count == 4 && browserPlan.Files.All(f => f.Processes.Length > 0), "selecting the group keeps each browser's original execution checks");
            browsers.Children.Single(c => c.Id == "quark").Selected = false;
            Check(browsers.SelectionState is null && model.SelectedCount == 2 && model.CreatePlan()!.Files.All(f => f.CategoryId == "chrome"), "deselecting one browser updates group state and excludes it from the plan");
            browsers.Children.Single(c => c.Id == "chrome").Files[0].Selected = false;
            Check(model.SelectedCount == 1 && model.CreatePlan()!.Files.Count == 1, "individual file exclusions still apply within a selected browser");
            browsers.Selected = false;
            Check(!model.CanClean && browsers.Children.All(c => !c.Selected), "deselecting browser group clears every browser selection");
            var chromeCategory = browsers.Children.Single(c => c.Id == "chrome");
            Check(browsers.Files.All(f => !f.Selected), "deselecting group clears every detail checkbox");
            chromeCategory.Files[0].Selected = true;
            Check(chromeCategory.SelectionState is null && browsers.SelectionState is null && model.SelectedCount == 1 && model.CreatePlan()!.Files.Count == 1,
                "detail selection activates its category and updates both partial states and cleanup plan");
            Render(view, "cleaner-partial-selection.png");
            Render(new BrowserCacheDetails(browsers, _ => { }), "cleaner-browser-partial-selection.png");
            var selector = new TestSelectionCheckBox();
            selector.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("SelectionState") { Source = chromeCategory, Mode = System.Windows.Data.BindingMode.TwoWay });
            selector.Toggle();
            Check(chromeCategory.SelectionState == true && chromeCategory.Files.All(f => f.Selected) && browsers.SelectionState is null,
                "clicking partial category selects all its files but leaves other browsers untouched");
            selector.Toggle();
            Check(chromeCategory.SelectionState == false && browsers.SelectionState == false && !model.CanClean,
                "clicking selected category clears detail selection and disables cleanup");
            browsers.Selected = true;
            chromeCategory.Files[0].Selected = false;
            Check(browsers.SelectionState is null && chromeCategory.SelectionState is null, "one unchecked browser file makes both ancestors partial");
            browsers.SelectionState = true;
            Check(browsers.SelectionState == true && browsers.Files.All(f => f.Selected), "selecting parent fills missing checks in every browser");
            model.SelectNone();
            var temporary = model.Categories.Single(c => c.Id == "temporary");
            temporary.Files[0].Selected = true;
            Check(temporary.SelectionState == true && model.SelectedCount == 1 && model.SelectedBytes == temporary.Files[0].Bytes,
                "ordinary detail checkbox synchronizes category and exact selected totals");
            temporary.Files[0].Selected = false;
            Check(temporary.SelectionState == false && !model.CanClean, "clearing last detail checkbox clears category");
            Render(new BrowserCacheDetails(browsers, _ => { }), "cleaner-browser-details.png");
            model.SelectAll(); Check(model.CanClean && model.SelectedBytes == model.Categories.Sum(c => c.Bytes) && model.SelectedCount == 8, "select all includes selectable categories and restores individual file selection");
            model.SelectNone(); Check(!model.CanClean && model.SelectedCount == 0 && model.SelectedCategoriesLabel == "已选择（0）" && model.Categories.SelectMany(c => c.Files).All(f => !f.Selected), "cancel all clears every detail checkbox and category count");
            model.SelectAll(); Check(model.CanClean && model.SelectedCount == 8, "select all restores available results while empty categories stay unselected");
            model.SelectNone();
            Render(view, "cleaner-results.png");
            Check(model.AddExclusion(Path.Combine(fixture.Paths.Local, "Temp")) && !model.CanClean, "changing exclusions invalidates the existing cleanup plan");
            var restored = new CleanerViewModel(fixture.Engine(), new GuestContext(), Path.Combine(root, "ui-state")); restored.LoadSettings();
            Check(restored.Exclusions.Count == 1, "excluded directories persist locally");
            Pump(model.ScanAsync()); Check(!model.Categories.Single(c => c.Id == "temporary").CanSelect, "excluded directories disappear from the next scan");
            Check(fixture.Bin.EmptyCalls == 0, "preview and file tests never empty the real or simulated recycle bin implicitly");
            CheckMotion(root);
            Console.WriteLine($"PASS: {_checks} cleaner checks. Fixture: {root}"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void RunEngine(string root)
    {
        var fixture = new Fixture(Path.Combine(root, "selection"));
        var old = fixture.Old("Temp/old.tmp");
        var young = fixture.Old("Temp/recent.tmp", days: 0);
        var personal = fixture.Old("Temp/notes.txt");
        var excluded = fixture.Old("Temp/preserve/old.log");
        var dump = fixture.Old("CrashDumps/editor.dmp");
        var browser = fixture.Browser("Google/Chrome/User Data", "Default");
        fixture.Browser("Google/Chrome/User Data", "Profile 1");
        var cookies = fixture.Old("Google/Chrome/User Data/Default/Cache/Cache_Data/Cookies");
        var raw = fixture.Old("Microsoft/Edge/User Data/Default/Cache/Cache_Data/unknown"); // No Preferences or cache index: must not be inferred.
        var engine = fixture.Engine();
        var exclusions = new[] { Path.GetDirectoryName(excluded)! };
        var snapshot = engine.Scan(exclusions, null, default);
        Check(snapshot.Complete && snapshot.Categories.Count == 13, "system categories, eight browser adapters and recycle bin are listed separately");
        Check(snapshot.Categories.All(c => !c.Selected && !string.IsNullOrWhiteSpace(c.Rule.Consequence)), "all categories explain consequences and start unchecked");
        var temp = snapshot.Categories.Single(c => c.Id == "temporary");
        Check(temp.Files.Count == 1 && temp.Files[0].Path == old, "temporary rule respects age, extension and directory exclusions");
        var chrome = snapshot.Categories.Single(c => c.Id == "chrome");
        Check(chrome.Files.Count == 4 && chrome.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4, "multiple standard browser profiles are scanned without duplicate paths");
        Check(!chrome.Files.Any(f => f.Path == cookies) && !snapshot.Categories.Single(c => c.Id == "edge").CanSelect, "credentials and unrecognized cache layouts are protected");
        Check(new[] { old, young, personal, excluded, dump, browser, cookies, raw }.All(File.Exists), "scan is read-only");
        temp.Selected = true;
        var plan = engine.BuildPlan(snapshot, exclusions);
        temp.Selected = false;
        var result = engine.ExecuteFiles(plan, null, default);
        Check(result.DeletedCount == 1 && !File.Exists(old), "frozen plan deletes only the file selected at confirmation");
        Check(new[] { young, personal, excluded, dump, browser, cookies, raw }.All(File.Exists), "unselected categories and excluded or personal files remain intact");
        ExpectInvalid(() => engine.ExecuteFiles(plan, null, default), "same deletion plan cannot run twice");
        ExpectInvalid(() => engine.BuildPlan(snapshot, []), "consumed scan cannot create another plan");

        var changed = new Fixture(Path.Combine(root, "changed"));
        var changedPath = changed.Old("Temp/changed.tmp");
        var changedEngine = changed.Engine(); var changedScan = changedEngine.Scan([], null, default);
        changedScan.Categories.Single(c => c.Id == "temporary").Selected = true;
        var changedPlan = changedEngine.BuildPlan(changedScan, []);
        File.AppendAllText(changedPath, "new content");
        Check(changedEngine.ExecuteFiles(changedPlan, null, default).SkippedCount == 1 && File.Exists(changedPath), "modified file is preserved after scanning");

        var replaced = new Fixture(Path.Combine(root, "replaced"));
        var replacementPath = replaced.Old("Temp/same.tmp");
        var replaceEngine = replaced.Engine(); var replaceScan = replaceEngine.Scan([], null, default);
        replaceScan.Categories.Single(c => c.Id == "temporary").Selected = true;
        var replacePlan = replaceEngine.BuildPlan(replaceScan, []); var stamp = replacePlan.Files.Single().Stamp;
        File.Move(replacementPath, replacementPath + ".retained");
        File.WriteAllText(replacementPath, "fixture"); File.SetCreationTimeUtc(replacementPath, DateTime.FromFileTimeUtc(stamp.Creation)); File.SetLastWriteTimeUtc(replacementPath, DateTime.FromFileTimeUtc(stamp.Modified));
        Check(replaceEngine.ExecuteFiles(replacePlan, null, default).SkippedCount == 1 && File.Exists(replacementPath), "replacement file with identical size and times is rejected by file identity");

        var locked = new Fixture(Path.Combine(root, "locked")); var lockedPath = locked.Old("Temp/locked.tmp");
        var lockedEngine = locked.Engine(); var lockedScan = lockedEngine.Scan([], null, default); lockedScan.Categories.Single(c => c.Id == "temporary").Selected = true;
        var lockedPlan = lockedEngine.BuildPlan(lockedScan, []);
        using (var held = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(lockedEngine.ExecuteFiles(lockedPlan, null, default).SkippedCount == 1, "occupied file is skipped without forcing the process closed");
        Check(File.Exists(lockedPath), "occupied file remains untouched");

        var linked = new Fixture(Path.Combine(root, "hardlink")); var linkedPath = linked.Old("Temp/hard.tmp");
        var protectedPath = Path.Combine(linked.Paths.Local, "protected.dat");
        Check(CreateHardLinkW(protectedPath, linkedPath, IntPtr.Zero), "controlled hard-link fixture created");
        Check(!linked.Engine().Scan([], null, default).Categories.Single(c => c.Id == "temporary").CanSelect && File.Exists(protectedPath), "multiply linked files are not candidates");

        var parentSwap = new Fixture(Path.Combine(root, "junction")); var original = parentSwap.Old("Temp/swap/target.tmp");
        var swapEngine = parentSwap.Engine(); var swapScan = swapEngine.Scan([], null, default); swapScan.Categories.Single(c => c.Id == "temporary").Selected = true;
        var swapPlan = swapEngine.BuildPlan(swapScan, []);
        var parent = Path.GetDirectoryName(original)!; Directory.Move(parent, parent + "-old");
        var safe = Path.Combine(parentSwap.Paths.Local, "protected-folder"); Directory.CreateDirectory(safe); var safeFile = Path.Combine(safe, "target.tmp"); File.WriteAllText(safeFile, "do not delete");
        CreateJunction(parent, safe);
        Check(swapEngine.ExecuteFiles(swapPlan, null, default).SkippedCount == 1 && File.Exists(safeFile), "parent directory replaced with junction cannot redirect deletion");
        Check(!parentSwap.Engine().Scan([], null, default).Categories.Single(c => c.Id == "temporary").Files.Any(f => f.Path == original), "scanner does not traverse a directory junction");

        var cancel = new Fixture(Path.Combine(root, "cancel")); cancel.Old("Temp/a.tmp"); cancel.Old("Temp/b.tmp"); cancel.Old("Temp/c.tmp");
        var cancelEngine = cancel.Engine(); var cancelScan = cancelEngine.Scan([], null, default); cancelScan.Categories.Single(c => c.Id == "temporary").Selected = true;
        var cancelPlan = cancelEngine.BuildPlan(cancelScan, []); using var source = new CancellationTokenSource(); var reports = 0;
        var cancelled = cancelEngine.ExecuteFiles(cancelPlan, new InlineProgress(_ => { if (++reports == 2) source.Cancel(); }), source.Token);
        Check(cancelled.Cancelled && cancelled.DeletedCount == 1 && Directory.GetFiles(Path.Combine(cancel.Paths.Local, "Temp")).Length == 2, "cancellation stops before the next delete and retains completed counts");
        using var scanCancel = new CancellationTokenSource(); scanCancel.Cancel();
        try { cancelEngine.Scan([], null, scanCancel.Token); throw new Exception("Cancelled scan accepted"); } catch (OperationCanceledException) { Check(true, "scan cancellation returns no executable snapshot"); }

        var process = new Fixture(Path.Combine(root, "browser-running")); var cache = process.Browser("Quark/User Data", "Default"); var running = true;
        var processEngine = process.Engine(_ => running);
        Check(!processEngine.Scan([], null, default).Categories.Single(c => c.Id == "quark").CanSelect, "running browser is skipped at scan time");
        running = false; var processScan = processEngine.Scan([], null, default); processScan.Categories.Single(c => c.Id == "quark").Selected = true;
        var processPlan = processEngine.BuildPlan(processScan, []); running = true;
        Check(processEngine.ExecuteFiles(processPlan, null, default).DeletedCount == 0 && File.Exists(cache), "browser started after scanning prevents cache deletion");

        var firefox = new Fixture(Path.Combine(root, "firefox")); var ff = firefox.Old("Mozilla/Firefox/Profiles/a.default/cache2/entries/entry"); firefox.Old("Mozilla/Firefox/Profiles/a.default/cache2/index");
        Directory.CreateDirectory(Path.Combine(firefox.Paths.Roaming, "Mozilla/Firefox/Profiles/a.default")); File.WriteAllText(Path.Combine(firefox.Paths.Roaming, "Mozilla/Firefox/Profiles/a.default/prefs.js"), "");
        var ffScan = firefox.Engine().Scan([], null, default);
        Check(ffScan.Categories.Single(c => c.Id == "firefox").Files.Single().Path == ff, "Firefox adapter only includes cache entries with matching standard profile markers");
        var adapters = new Fixture(Path.Combine(root, "browser-adapters"));
        foreach (var relative in new[] { "Google/Chrome/User Data", "Microsoft/Edge/User Data", "360se6/User Data", "360Chrome/Chrome/User Data", "Tencent/QQBrowser/User Data", "Quark/User Data", "SogouExplorer/Webkit" }) adapters.Browser(relative, "Default");
        var adapterScan = adapters.Engine().Scan([], null, default);
        Check(new[] { "chrome", "edge", "360se", "360chrome", "qqbrowser", "quark", "sogou" }.All(id => adapterScan.Categories.Single(c => c.Id == id).Files.Count == 2), "seven Chromium-family adapters match only their own standard profile cache fixtures");

        var bin = new Fixture(Path.Combine(root, "recycle")); var binEngine = bin.Engine(); var binScan = binEngine.Scan([], null, default);
        var binCategory = binScan.Categories.Single(c => c.IsRecycleBin); Check(!binCategory.Selected && binCategory.RecycleCount == 2, "recycle bin is independently listed and never selected by default");
        binCategory.Selected = true; var binPlan = binEngine.BuildPlan(binScan, []); binEngine.ExecuteFiles(binPlan, null, default);
        bin.Bin.Snapshot = new(3, 3);
        Check(binEngine.EmptyRecycleBin(binPlan, IntPtr.Zero, default).Contains("已变化") && bin.Bin.EmptyCalls == 0, "changed recycle-bin snapshot requires a new scan instead of emptying");
        var bin2 = new Fixture(Path.Combine(root, "recycle-confirm")); var bin2Engine = bin2.Engine(); var bin2Scan = bin2Engine.Scan([], null, default); bin2Scan.Categories.Single(c => c.IsRecycleBin).Selected = true;
        var bin2Plan = bin2Engine.BuildPlan(bin2Scan, []); bin2Engine.ExecuteFiles(bin2Plan, null, default); bin2Engine.EmptyRecycleBin(bin2Plan, IntPtr.Zero, default);
        Check(bin2.Bin.EmptyCalls == 1, "selected recycle bin delegates to the system-confirmation abstraction");
        bin2Engine.EmptyRecycleBin(bin2Plan, IntPtr.Zero, default);
        Check(bin2.Bin.EmptyCalls == 1, "recycle-bin execution cannot be replayed");
        Check(!SafeFile.Within(Path.Combine(root, "safe-evil/file"), Path.Combine(root, "safe")), "path boundary check rejects a matching prefix in a sibling directory");
    }
    private sealed class Fixture
    {
        public CleanerPaths Paths { get; }
        public FakeBin Bin { get; } = new();
        public Fixture(string root) { Paths = new(Path.Combine(root, "local"), Path.Combine(root, "roaming")); Directory.CreateDirectory(Paths.Local); Directory.CreateDirectory(Paths.Roaming); }
        public CleanerEngine Engine(Func<string[], bool>? running = null) => new(Paths, Bin, names => names.Length != 0 && (running?.Invoke(names) ?? false));
        public string Old(string relative, int days = 9)
        {
            var path = Path.GetFullPath(Path.Combine(Paths.Local, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture");
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-days)); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-days)); return path;
        }
        public string Browser(string relative, string profile)
        {
            Old(relative + "/" + profile + "/Preferences"); Old(relative + "/" + profile + "/Cache/Cache_Data/index");
            return Old(relative + "/" + profile + "/Cache/Cache_Data/f_000001");
        }
    }
    private static void CheckMotion(string root)
    {
        var fixture = new Fixture(Path.Combine(root, "motion"));
        fixture.Browser("Google/Chrome/User Data", "Default");
        using var gate = new ManualResetEventSlim();
        var model = new CleanerViewModel(fixture.Engine(_ => { gate.Wait(TimeSpan.FromSeconds(5)); return false; }), new GuestContext(), Path.Combine(root, "motion-state"));
        var view = new CleanerView(model);
        var window = new Window { Content = view, Width = 1180, Height = 760, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        Task? scan = null;
        window.Show();
        try
        {
            window.UpdateLayout();
            scan = model.ScanAsync();
            Check(model.IsBusy && model.HasScanLayout && ((FrameworkElement)view.FindName("ResultsPanel")).Visibility == Visibility.Visible, "live window enters scan layout immediately while engine is still running");
            Pump(Task.Delay(180)); Render(view, "cleaner-transition.png");
            Pump(Task.Delay(400));
            var progress = (FrameworkElement)view.FindName("ScanProgress");
            var title = (FrameworkElement)view.FindName("ResultsTitle");
            var button = (FrameworkElement)view.FindName("RescanButton");
            var progressPoint = progress.TranslatePoint(new Point(), view);
            Check(progress.Visibility == Visibility.Visible && progress.ActualWidth > 100 && progressPoint.X > title.TranslatePoint(new Point(), view).X + title.ActualWidth && progressPoint.X + progress.ActualWidth < button.TranslatePoint(new Point(), view).X,
                "scan progress occupies the header space between title and rescan action");
            Render(view, "cleaner-scanning.png");
            gate.Set(); Pump(scan); Pump(Task.Delay(300));
            Check(model.HasResults && progress.Visibility == Visibility.Collapsed && ((FrameworkElement)view.FindName("WelcomePanel")).Visibility == Visibility.Collapsed, "transition settles with results shown and scan progress hidden");
            model.Categories.Single(c => c.Id == "browser-cache").Selected = true;
            var plan = model.CreatePlan()!;
            Pump(model.CleanAsync(plan, IntPtr.Zero));
            Pump(Task.Delay(500));
            Check(model.LastResult?.DeletedCount == 2 && !model.HasScanLayout && model.WelcomeDescription.Contains("已清理 2 个文件") && model.InitialStatus == "",
                "cleanup returns to a concise completion state with actual deleted file count");
            Check(((FrameworkElement)view.FindName("WelcomePanel")).Visibility == Visibility.Visible && ((FrameworkElement)view.FindName("ResultsPanel")).Visibility != Visibility.Visible && model.StartScanLabel == "再次扫描",
                "completion transition restores centered layout and rescan button");
            Render(view, "cleaner-complete.png");
            Pump(model.ScanAsync()); Pump(Task.Delay(800));
            Check(model.HasScanLayout && model.LastResult is null && ((FrameworkElement)view.FindName("WelcomePanel")).Visibility == Visibility.Collapsed && ((FrameworkElement)view.FindName("ResultsPanel")).IsHitTestVisible,
                "rescan after completion restores interactive results layout and clears previous summary");
        }
        finally { gate.Set(); if (scan is not null && !scan.IsCompleted) Pump(scan); window.Close(); }
    }
    private sealed class TestSelectionCheckBox : XieBox.Platform.UI.SelectionCheckBox { public void Toggle() => OnToggle(); }
    private sealed class FakeBin : IRecycleBin
    {
        public RecycleSnapshot Snapshot { get; set; } = new(20, 2);
        public int EmptyCalls { get; private set; }
        public RecycleSnapshot Query() => Snapshot;
        public string EmptyWithSystemConfirmation(IntPtr owner) { EmptyCalls++; return "fixture system confirmation"; }
    }
    private sealed class GuestContext : IPlatformContext
    {
        public UserContext CurrentUser => UserContext.Guest;
        public event EventHandler<UserContext>? UserContextChanged { add { } remove { } }
        public Task RequestSignInAsync() => throw new Exception("Free cleaner must not request sign-in");
        public Task<CapabilityResult> CheckCapabilityAsync(string capabilityId, CancellationToken cancellationToken = default) => Task.FromResult(new CapabilityResult(true, "ALLOWED", "offline free"));
    }
    private sealed class InlineProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
    private static void Check(bool success, string name) { if (!success) throw new Exception("FAIL: " + name); _checks++; Console.WriteLine("PASS: " + name); }
    private static void ExpectInvalid(Action run, string name) { try { run(); throw new Exception("Expected rejection"); } catch (InvalidOperationException) { Check(true, name); } }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame(); task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static void Render(FrameworkElement element, string name)
    {
        const int width = 1178, height = 706;
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var back = new DrawingVisual();
        using (var draw = back.RenderOpen()) draw.DrawRectangle((Brush)Application.Current.FindResource("CanvasBrush"), null, new Rect(0, 0, width, height));
        bitmap.Render(back); bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.GetFullPath(Path.Combine("artifacts", "previews", name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void CreateJunction(string path, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + path.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' -ErrorAction Stop | Out-Null");
        using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit(10000); if (process.ExitCode != 0) throw new Exception("Controlled junction fixture failed: " + process.StandardError.ReadToEnd());
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string link, string target, IntPtr security);
}
