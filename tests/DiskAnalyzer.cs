using System.IO;
using XieBox.DiskAnalyzer;
using XieBox.Services;
using XieBox.ViewModels;

internal static partial class Program
{
    private static void CheckDiskAnalyzer(string root, string preview, MainViewModel platform, AccountService accounts)
    {
        var fixture = Path.Combine(root, "disk-fixture");
        Directory.CreateDirectory(Path.Combine(fixture, "资料", "子目录"));
        Directory.CreateDirectory(Path.Combine(fixture, "空目录"));
        File.WriteAllBytes(Path.Combine(fixture, "资料", "子目录", "sample.bin"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(fixture, "小文件.txt"), new byte[1024]);
        var engine = new DiskEngine();
        var result = engine.Scan(fixture, null, default);
        Check(result.Root.Bytes == 5120 && result.Root.Files == 2, "disk recursive totals include nested files exactly once");
        Check(result.Root.Children[0].Name == "资料" && result.Root.Children[0].Share == 80, "disk ranking and current-directory shares");
        Check(result.Root.Children.Single(n => n.Name == "空目录").Bytes == 0, "empty directory retained");
        Check(!result.Cancelled && result.Root.IsComplete && result.Skipped == 0, "complete disk scan status");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Check(engine.Scan(fixture, null, cancel.Token).Cancelled, "disk scan honors cancellation");
        Check(engine.Scan(fixture, null, cancel.Token).Root.Size == "未扫描", "unscanned root is not shown as an empty directory");
        try { engine.Scan(Path.Combine(root, "missing-disk-directory"), null, default); throw new Exception("missing path accepted"); }
        catch (DirectoryNotFoundException) { Check(true, "missing disk root rejected"); }
        var module = platform.Modules.Single(m => m.Id == BuiltInModules.Disk.Id); platform.Selected = module;
        platform.InstallSelectedAsync(default).GetAwaiter().GetResult();
        Check(platform.Installer.IsInstalled(module.Definition), "disk module installs offline");
        using var context = new ModuleContext(BuiltInModules.Disk, accounts, () => throw new Exception("must stay offline"));
        var model = new DiskModel(context);
        var view = new DiskView(model);
        RenderUtility(view, Path.Combine(preview, "disk-start.png"));
        RunUi(model.ScanAsync);
        Check(!model.ResultsLayout && model.Status.Contains("选择"), "disk empty input stays on setup");
        model.Directory = fixture;
        RunUi(model.ScanAsync);
        Check(model.ResultsLayout && !model.IsBusy && model.Entries.Count == 3, "disk model completes and releases busy state");
        RenderUtility(view, Path.Combine(preview, "disk-results.png"));
        model.Enter(model.Entries[0]);
        Check(model.CanGoUp && model.Entries.Single().Name == "子目录", "disk drilldown uses cached subtree");
        model.GoUp(); Check(!model.CanGoUp && model.Entries.Count == 3, "disk parent navigation stops at scan root");
        model.Edit(); Check(!model.ResultsLayout && model.Directory == fixture, "disk change directory preserves input");
        Check(File.ReadAllBytes(Path.Combine(fixture, "小文件.txt")).Length == 1024, "disk analysis does not modify fixture files");
        platform.Uninstall(module); Check(module.IsInstalled, "bundled disk analyzer stays available");
    }
}
