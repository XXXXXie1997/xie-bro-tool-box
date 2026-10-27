using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using XieBox.MemoryRelease;
using XieBox.Services;
using XieBox.StartupManager;
using XieBox.ViewModels;

internal static partial class Program
{
    private static void AwaitUi(Task task)
    {
        while (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static void RenderUtility(FrameworkElement view, string path)
    {
        var host = new Window { Content = view, Width = 1180, Height = 780, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        host.Show(); AwaitUi(Task.Delay(400));
        Render(view, path, 1178, 706); host.Content = null; host.Close();
    }
    private static void RunUi(Func<Task> operation)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { AwaitUi(operation()); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void CheckUtilityModules(string root, string preview, MainViewModel platform, AccountService accounts)
    {
        foreach (var definition in new[] { BuiltInModules.Startup, BuiltInModules.Memory })
        {
            var module = platform.Modules.Single(m => m.Id == definition.Id); platform.Selected = module;
            platform.InstallSelectedAsync(default).GetAwaiter().GetResult();
            Check(platform.Installer.IsInstalled(definition), definition.Id + " installs offline");
            platform.Uninstall(module); Check(module.IsInstalled, definition.Id + " remains bundled");
        }
        var keyPath = @"Software\XieBox.TestFixtures\" + Guid.NewGuid().ToString("N");
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        var source = new StartupSource(RegistryHive.CurrentUser, RegistryView.Registry64, keyPath + @"\Run", keyPath + @"\Approved", true, "当前用户 · 注册表");
        var folder = Path.Combine(root, "startup-fixture"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "示例应用.lnk"), "fixture only; not an executable shortcut");
        using var context = new ModuleContext(BuiltInModules.Startup, accounts, () => throw new Exception("must stay offline"));
        var engine = new StartupEngine([source], [(folder, true, "当前用户 · 启动文件夹")]);
        try
        {
            using (var key = hive.CreateSubKey(source.Key)) key.SetValue("示例启动项", @"%LOCALAPPDATA%\Example\app.exe --background", RegistryValueKind.ExpandString);
            var entry = engine.Scan().Entries.Single(e => e.Source is not null);
            Check(!entry.RequiresAdministrator, "current-user startup changes do not request administrator rights");
            Check((entry with { Source = source with { Hive = RegistryHive.LocalMachine } }).RequiresAdministrator, "machine startup changes require administrator authorization");
            Check(StartupElevation.RunWorker("invalid-pipe", Environment.ProcessId) == 2, "startup worker rejects unexpected pipe names before connecting");
            engine.Toggle(entry);
            var disabled = engine.Scan().Entries.Single(e => e.Source is not null);
            Check(!disabled.Enabled && disabled.Command == entry.Command, "disable preserves raw expandable registry command");
            using (var key = hive.OpenSubKey(source.Key + ".XieBoxDisabled")) Check(key!.GetValueKind(entry.Name) == RegistryValueKind.ExpandString, "disable preserves registry value kind");
            engine.Toggle(disabled);
            Check(engine.Scan().Entries.Single(e => e.Source is not null).Enabled, "registry startup is restorable");
            using (var key = hive.CreateSubKey(source.Key)) key.SetValue(entry.Name, "changed command");
            try { engine.Toggle(entry); throw new Exception("stale command accepted"); } catch (InvalidOperationException) { Check(true, "stale scan cannot overwrite a changed startup command"); }
            var current = engine.Scan().Entries.Single(e => e.Source is not null); engine.Toggle(current);
            using (var key = hive.CreateSubKey(source.Key)) key.SetValue(entry.Name, "new application");
            try { engine.Toggle(engine.Scan().Entries.Single(e => e.Source is not null && !e.Enabled)); throw new Exception("conflict accepted"); } catch (InvalidOperationException) { Check(true, "restore refuses existing same-name entry"); }
            using (var key = hive.CreateSubKey(source.Key)) key.DeleteValue(entry.Name);
            engine.Toggle(engine.Scan().Entries.Single(e => e.Source is not null));
            using (var key = hive.CreateSubKey(source.ApprovalKey!)) key.SetValue(entry.Name, new byte[] { 3, 0, 0, 0 }, RegistryValueKind.Binary);
            var external = engine.Scan().Entries.Single(e => e.Source is not null);
            Check(!external.Enabled && !external.CanChange, "Windows-disabled entry is read-only and correctly labelled");
            var file = engine.Scan().Entries.Single(e => e.Folder is not null); engine.Toggle(file);
            var moved = engine.Scan().Entries.Single(e => e.Folder is not null);
            Check(!moved.Enabled && !File.Exists(file.Command) && File.ReadAllText(moved.Command).StartsWith("fixture"), "startup file is preserved in a non-startup subdirectory");
            engine.Toggle(moved); Check(File.Exists(file.Command), "startup file restores to exact original path");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { engine.Scan(cancelled.Token); throw new Exception("cancel ignored"); } catch (OperationCanceledException) { Check(true, "startup scan honours cancellation"); }
            var model = new StartupModel(context, engine); var view = new StartupView(model);
            RenderUtility(view, Path.Combine(preview, "startup-intro.png"));
            RunUi(model.RefreshAsync); model.Filter = 2;
            Check(model.Entries.Count() == 1 && !model.Entries.First().Enabled, "disabled filter matches actual entries");
            model.Filter = 0; RenderUtility(view, Path.Combine(preview, "startup-results.png"));
        }
        finally { hive.DeleteSubKeyTree(keyPath, false); }
        using var memoryContext = new ModuleContext(BuiltInModules.Memory, accounts, () => throw new Exception("must stay offline"));
        var fake = new FakeMemory(); var memory = new MemoryModel(memoryContext, fake); var memoryView = new MemoryView(memory);
        memory.Refresh(); RunUi(memory.RefreshApplicationsAsync);
        Check(memory.Applications.Count == 2 && memory.Applications[0].ProcessCount == 3, "memory page exposes application working sets and process counts");
        RenderUtility(memoryView, Path.Combine(preview, "memory-start.png"));
        RunUi(memory.ReleaseAsync);
        Check(memory.Description.Contains("增加") && !memory.IsBusy && memory.ButtonLabel == "再次释放", "memory result shows measured positive delta and resets busy state");
        RenderUtility(memoryView, Path.Combine(preview, "memory-complete.png"));
        Check(new MemoryResult(new(100, 50), new(100, 40), 1).Summary.Contains("暂无"), "memory result never claims a release when available memory drops");
        var live = new MemoryEngine().Read(); Check(live.Total > 0 && live.Available <= live.Total, "native memory snapshot reads valid physical memory values");
        var apps = new MemoryEngine().ReadApplications(default);
        Check(apps.Count > 0 && apps.All(a => a.Bytes > 0 && a.ProcessCount > 0 && a.Icon.IsFrozen) && apps.Select(a => a.Bytes).SequenceEqual(apps.Select(a => a.Bytes).OrderDescending()), "live applications have frozen icons and positive working sets sorted descending");
        Check(apps.Select(a => a.Path.Length > 0 ? a.Path : a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == apps.Count, "live applications merge processes by executable identity");
        using var cancelledApps = new CancellationTokenSource(); cancelledApps.Cancel();
        try { new MemoryEngine().ReadApplications(cancelledApps.Token); throw new Exception("memory enumeration ignored cancellation"); }
        catch (OperationCanceledException) { Check(true, "application memory enumeration supports cancellation"); }
        using var self = Process.GetCurrentProcess(); Check(!MemoryEngine.TryTrimBackgroundProcess(self), "memory trim excludes host process");
        using var child = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "XieBox.Platform.Tests.exe"), "--memory-fixture") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true })!;
        try
        {
            Check(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() == "ready", "controlled background memory fixture is ready");
            child.Refresh(); var before = child.WorkingSet64;
            Check(MemoryEngine.TryTrimBackgroundProcess(child), "native working-set trim succeeds on the controlled child only");
            child.Refresh(); Check(!child.HasExited && child.WorkingSet64 < before, "trim reduces fixture working set without ending process");
        }
        finally { child.StandardInput.WriteLine("exit"); if (!child.WaitForExit(5000)) child.Kill(); }
    }
    private sealed class FakeMemory : IMemoryEngine
    {
        public IReadOnlyList<MemoryApplication> ReadApplications(CancellationToken token) => [new("浏览器", "browser.exe", 512L * 1024 * 1024, 3, XieBox.Platform.UI.ApplicationIcon.Fallback), new("文本编辑器", "editor.exe", 64L * 1024 * 1024, 1, XieBox.Platform.UI.ApplicationIcon.Fallback)];
        public MemorySnapshot Read() => new(16UL * 1024 * 1024 * 1024, 6UL * 1024 * 1024 * 1024);
        public MemoryResult Release(CancellationToken token) => new(Read(), new(16UL * 1024 * 1024 * 1024, 7UL * 1024 * 1024 * 1024), 3);
    }
}
