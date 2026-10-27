using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using XieBox.Platform.UI;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.Views;
using XieBox.FileScanner;

internal static partial class Program
{
    private static void CheckUnifiedReports(string root, PlatformApi api)
    {
        var retired = BuiltInModules.Scanner with { Id = "app-mover", Status = "planned", Runtime = "content" };
        Check(BuiltInModules.Include([retired]).All(m => m.Id != "app-mover"), "unfinished modules from cached or server catalogs are hidden");
        var store = new LocalStore(Path.Combine(root, "reports"));
        var model = new MainViewModel(store, api, new AccountService(api, store), true);
        var window = new MainWindow(model, api) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); AwaitUi(window.Initialization);
        var open = typeof(MainWindow).GetMethod("OpenModule", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(window.FindName("ExportReportButton") is null, "no universal report entry for ordinary tools");
        open.Invoke(window, [model.Modules.Single(m => m.Id == "file-scanner")]);
        var scanner = ((ScannerView)((ContentControl)window.FindName("PageHost")).Content).Model;
        var reports = ModuleReport.Create("文件扫描", scanner)!;
        Check(!reports.CanExport, "no report is available before an operation");
        var directory = Path.Combine(root, "report-input"); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "test.png"), "fixture");
        scanner.Directory = directory; scanner.LargeFiles = false; scanner.Formats.Single(f => f.Name == "图片").Selected = true;
        RunUi(scanner.ScanAsync);
        Check(reports.CanExport, "completed scan enables the shared report action");
        var report = reports.BuildReport();
        scanner.Directory = "another-directory";
        Check(reports.BuildReport() == report && report.Contains(directory) && !report.Contains("another-directory"), "report freezes original scan scope and results");
        var file = Path.Combine(root, "unified-report.txt"); ReportExporter.Write(file, report);
        Check(File.ReadAllText(file) == report && File.ReadAllBytes(file).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "shared exporter writes UTF-8 BOM with complete report text");
        scanner.Directory = directory;
        RunUi(async () => { var scan = scanner.ScanAsync(); Check(!reports.CanExport, "running task disables export of stale result"); scanner.Cancel(); await scan; });
        Check(reports.CanExport && reports.BuildReport().Contains("── 范围 ──"), "stopped scan has a scoped report with operation status");
        window.Close();
    }
}
