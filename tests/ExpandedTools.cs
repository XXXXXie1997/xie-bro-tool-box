using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XieBox.FileOperations;
using XieBox.FileScanner;
using XieBox.BatchRenamer;
using XieBox.FileOrganizer;
using XieBox.NetworkDiagnostics;
using XieBox.SoftwareUninstaller;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.Views;

internal static partial class Program
{
    private static void CheckExpandedTools(string root, string preview, MainViewModel platform, AccountService accounts)
    {
        var fixture = Path.Combine(root, "file-workflows"); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "甲.txt"), "first original content");
        File.WriteAllText(Path.Combine(fixture, "乙.jpg"), "second original content");
        var journal = Path.Combine(root, "move-journal.json");
        foreach (var blocked in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Path.Combine(Path.GetPathRoot(fixture)!, "Windows.old"), Path.Combine(Path.GetPathRoot(fixture)!, "Users", "OtherUser", "AppData", "Local"), Path.Combine(Path.GetPathRoot(fixture)!, "Recovery") })
        {
            try { ProtectedDirectories.Validate(blocked); throw new Exception("protected path accepted"); }
            catch (IOException) { Check(true, "file mutation rejects system/application directory: " + Path.GetFileName(blocked)); }
        }
        Check(ProtectedDirectories.Validate(fixture) == Path.GetFullPath(fixture), "personal file fixtures remain editable");
        foreach (var blocked in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "..", "Windows", "System32"), Path.Combine(Path.GetPathRoot(fixture)!, "Windows.", "System32"), @"\\?\C:\Windows\System32" })
        {
            try { ProtectedDirectories.Validate(blocked); throw new Exception("path alias accepted"); }
            catch (IOException) { Check(true, "file mutation rejects normalized and device path aliases"); }
        }
        var plan = FilePlan.Preview(fixture, (f, i) => Path.Combine(fixture, "前缀_" + f.Name), default);
        Check(plan.Count == 2 && File.Exists(Path.Combine(fixture, "甲.txt")), "rename preview is read-only");
        FilePlan.Apply(fixture, plan, journal, default);
        Check(plan.All(p => !File.Exists(p.Source) && File.Exists(p.Destination)), "rename executes exact preview without overwriting");
        Check(FilePlan.Undo(journal, default) == 2 && File.ReadAllText(Path.Combine(fixture, "甲.txt")) == "first original content", "rename durable undo restores names and content");
        Check(FilePlan.Undo(journal, default) == 0, "undo is idempotent after successful recovery");
        try { FilePlan.Preview(fixture, (f, i) => Path.Combine(fixture, "collision.txt"), default); throw new Exception("collision accepted"); }
        catch (IOException) { Check(true, "duplicate destination names stop preview"); }
        try { FilePlan.Preview(fixture, (f, i) => Path.Combine(root, f.Name), default); throw new Exception("escape accepted"); }
        catch (IOException) { Check(true, "file plan cannot escape selected directory"); }
        Check(!FilePlan.ValidName("CON.txt") && !FilePlan.ValidName("bad.") && !FilePlan.ValidName("bad/name") && FilePlan.ValidName("旅行照片_0001.jpg"), "Windows filename rules include reserved device names");
        File.AppendAllText(plan[0].Source, "changed");
        try { FilePlan.Apply(fixture, plan, journal, default); throw new Exception("stale preview accepted"); }
        catch (IOException) { Check(File.Exists(plan[0].Source), "changed file blocks stale preview execution"); }
        var organize = FilePlan.Preview(fixture, (f, i) => Path.Combine(fixture, FileTaskModel.Category(f.Extension), f.Name), default);
        FilePlan.Apply(fixture, organize, journal, default);
        Check(organize.All(p => File.Exists(p.Destination)), "organizer creates category folders and moves files");
        File.WriteAllText(organize[^1].Source, "new occupant");
        try { FilePlan.Undo(journal, default); throw new Exception("undo overwrote file"); }
        catch (IOException) { Check(File.ReadAllText(organize[^1].Source) == "new occupant", "undo never overwrites a new occupant"); }
        File.Delete(organize[^1].Source); // Only this test-created occupant.
        Check(FilePlan.Undo(journal, default) == 2, "undo can retry after conflict is resolved");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { FilePlan.Preview(fixture, (f, i) => f.FullName, cancellation.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { Check(true, "file preview cancellation interrupts work"); }
        // An interrupted write-ahead intent without a completed move is safely recoverable.
        var intent = new MoveJournal(fixture, [FilePlan.Preview(fixture, (f, i) => Path.Combine(fixture, "new_" + f.Name), default)[0]]);
        File.WriteAllText(journal, System.Text.Json.JsonSerializer.Serialize(intent));
        Check(FilePlan.Undo(journal, default) == 0 && FilePlan.Load(journal).Items.Count == 0, "unexecuted durable intent can be recovered");
        var lockedPlan = FilePlan.Preview(fixture, (f, i) => Path.Combine(fixture, "locked_" + f.Name), default);
        using (var writer = new FileStream(lockedPlan[0].Source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            try { FilePlan.Apply(fixture, lockedPlan, journal, default); throw new Exception("active writer accepted"); }
            catch (IOException) { Check(File.Exists(lockedPlan[0].Source) && !File.Exists(lockedPlan[0].Destination), "pinned rename refuses concurrent file writers"); }
        }
        FilePlan.Undo(journal, default);
        var preoccupied = FilePlan.Preview(fixture, (f, i) => Path.Combine(fixture, "occupied_" + f.Name), default);
        File.WriteAllText(preoccupied[0].Destination, "keep this");
        try { FilePlan.Apply(fixture, preoccupied, journal, default); throw new Exception("new target overwritten"); }
        catch (IOException) { Check(File.ReadAllText(preoccupied[0].Destination) == "keep this", "execution rechecks destinations created after preview"); }
        File.Delete(preoccupied[0].Destination);

        var duplicates = Path.Combine(root, "duplicates"); Directory.CreateDirectory(duplicates);
        File.WriteAllText(Path.Combine(duplicates, "one.txt"), "same content"); File.WriteAllText(Path.Combine(duplicates, "two.bin"), "same content"); File.WriteAllText(Path.Combine(duplicates, "other.txt"), "diff content");
        var duplicateResult = DuplicateEngine.Scan(duplicates, null, default);
        Check(duplicateResult.Groups == 1 && duplicateResult.Files.Count == 2 && duplicateResult.Files.All(f => f.Group == "重复组 1"), "duplicate scanner compares content across extensions and rejects equal-size different content");
        var fastFolder = Path.Combine(root, "duplicate-fast"); Directory.CreateDirectory(fastFolder);
        for (int i = 0; i < 12; i++) { var data = new byte[256 * 1024]; Array.Fill(data, (byte)i); File.WriteAllBytes(Path.Combine(fastFolder, $"{i}.bin"), data); }
        var fastResult = DuplicateEngine.Scan(fastFolder, null, default);
        Check(fastResult.Groups == 0 && fastResult.FullyHashedFiles == 0 && fastResult.BytesRead == 12L * 48 * 1024, "sample filtering avoids full reads for same-size distinct files");
        var collisionFolder = Path.Combine(root, "duplicate-samples"); Directory.CreateDirectory(collisionFolder);
        var identical = new byte[256 * 1024]; File.WriteAllBytes(Path.Combine(collisionFolder, "a.bin"), identical); File.WriteAllBytes(Path.Combine(collisionFolder, "b.bin"), identical);
        identical[40 * 1024] = 1; File.WriteAllBytes(Path.Combine(collisionFolder, "different.bin"), identical);
        var collisionResult = DuplicateEngine.Scan(collisionFolder, null, default);
        Check(collisionResult.Groups == 1 && collisionResult.Files.Count == 2 && collisionResult.Files.All(f => !f.Path.EndsWith("different.bin")), "matching samples still require full hash and reject differences outside sampled ranges");
        Check(DuplicateEngine.Scan(duplicates, null, default, 1).Limited, "duplicate enumeration has an explicit upper bound");
        try { DuplicateEngine.Scan(duplicates, null, cancellation.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { Check(true, "duplicate scan cancellation is honored"); }
        Check(NetworkEngine.ValidateHost("example.com") == "example.com", "network target accepts domain");
        try { NetworkEngine.ValidateHost("https://example.com/path"); throw new Exception("invalid target accepted"); }
        catch (ArgumentException) { Check(true, "network target rejects URL paths and protocols"); }
        Check(!NetworkEngine.WebsiteSucceeded(new("HTTPS 连接", "已收到响应", "", 502)), "HTTP 502 is a failed website test");
        Check(NetworkEngine.WebsiteUri("http://example.com/page?q=1").AbsoluteUri == "http://example.com/page?q=1", "website test preserves scheme path and query");
        Check(NetworkEngine.ExplainNetwork([new("HTTPS 连接", "未完成", ""), new("对照网站", "已收到响应", "", 200)], true).Title == "网络连接正常", "general network conclusion is separate from target website failure");
        Check(NetworkEngine.Local().Count > 0, "network adapter snapshot is available offline");
        Check(NetworkMap.Evaluate([new("Wi-Fi", "未连接", "")], true) == new NetworkLinks(LinkState.Failed, LinkState.Pending), "offline marks only the local link as failed");
        Check(NetworkMap.Evaluate([new("Wi-Fi", "已连接", ""), new("网关 1", "未响应", ""), new("HTTPS 连接", "已收到响应", "", 200)], true) == new NetworkLinks(LinkState.Good, LinkState.Good), "HTTPS proves both links despite blocked ping");
        Check(NetworkMap.Evaluate([new("Wi-Fi", "已连接", ""), new("网关 1", "可达", "")], false) == new NetworkLinks(LinkState.Good, LinkState.Pending), "unfinished internet check stays pending");
        Check(NetworkMap.Evaluate([new("Wi-Fi", "已连接", ""), new("网关 1", "未响应", "")], true).Local == LinkState.Uncertain, "unanswered gateway is not a proven broken link");
        Check(NetworkEngine.Explain([new("Wi-Fi", "未连接", "")], true).Title.Contains("未检测到"), "offline conclusion explains missing connection");
        Check(NetworkEngine.Explain([new("Wi-Fi", "已连接", ""), new("HTTPS 连接", "未完成", ""), new("对照网站", "已收到响应", "", 200)], true).Title.Contains("其他网站可以连接"), "reachable comparison website avoids claiming total internet failure");
        Check(NetworkEngine.Explain([new("HTTPS 连接", "已收到响应", "", 503)], true).Title.Contains("异常"), "HTTP server error is not reported as a healthy website");
        Check(NetworkEngine.Explain([new("HTTPS 连接", "已收到响应", "", 200)], false).Title.Contains("尚未完成"), "cancelled diagnostic does not present a completed conclusion");
        Check(NetworkEngine.Explain([new("Wi-Fi", "已连接", ""), new("网关", "未响应", ""), new("HTTPS 连接", "已收到响应", "", 200)], true).Title.Contains("网络连接正常"), "blocked ping does not override successful website access");
        var msi = new SoftwareEntry("Fixture", "Fixture", "1", 0, "ignored", "{12345678-1234-1234-1234-123456789ABC}", RegistryHive.CurrentUser, RegistryView.Registry64, true);
        Check(SoftwareEngine.LaunchInfo(msi).Arguments.StartsWith("/x ") && SoftwareEngine.LaunchInfo(msi).FileName.EndsWith("msiexec.exe"), "MSI uninstall invokes native uninstall with product identity");
        try { SoftwareEngine.LaunchInfo(msi with { Msi = false, Command = "cmd.exe /c anything" }); throw new Exception("shell accepted"); }
        catch (IOException) { Check(true, "ambiguous shell-based uninstall commands are rejected"); }
        Check(SoftwareEngine.Read(default).All(e => !string.IsNullOrWhiteSpace(e.Name)), "software registry inventory uses named entries only");
        var executableIcon = XieBox.Platform.UI.ApplicationIcon.Load("\"" + Path.Combine(Environment.SystemDirectory, "notepad.exe") + "\",0");
        Check(executableIcon.IsFrozen && !ReferenceEquals(executableIcon, XieBox.Platform.UI.ApplicationIcon.Fallback), "software icon reads executable resource and is safe across threads");
        Check(ReferenceEquals(XieBox.Platform.UI.ApplicationIcon.Load(Path.Combine(root, "missing.exe")), XieBox.Platform.UI.ApplicationIcon.Fallback), "missing application icon uses shared fallback");

        foreach (var definition in new[] { BuiltInModules.Uninstaller, BuiltInModules.Organizer, BuiltInModules.Network })
        {
            var module = platform.Modules.Single(m => m.Id == definition.Id); platform.Selected = module;
            platform.InstallSelectedAsync(default).GetAwaiter().GetResult(); Check(platform.Installer.IsInstalled(definition), definition.Id + " installs offline");
            using var context = new ModuleContext(definition, accounts, () => throw new Exception("guest modules must not require sign in"));
            var state = Path.Combine(root, definition.Id);
            var page = definition.Id switch { "software-uninstaller" => SoftwarePage.Create(context), "batch-renamer" => Renamer.Create(context, state), "file-organizer" => Organizer.Create(context, state), _ => NetworkPage.Create(context) };
            if (page.Model is SoftwareModel)
            {
                var actionTemplate = ((DataGridTemplateColumn)page.Table.Columns.Last()).CellTemplate;
                actionTemplate.Seal();
                var action = (Button)actionTemplate.LoadContent();
                action.DataContext = msi;
                Check(Equals(action.Content, "卸载") && ((System.Windows.Media.SolidColorBrush)action.Foreground).Color.R > 150 && !page.FooterActions.Children.OfType<Button>().Any(b => b.Content?.ToString()?.Contains("卸载") == true), "software uninstall action is a red per-row button with no footer selection step");
            }
            RenderUtility(page, Path.Combine(preview, definition.Id + "-start.png"));
            if (page.Model is FileTaskModel model)
            {
                model.Directory = fixture; model.Prefix = "新_"; RunUi(model.PreviewAsync);
                Check(model.Entries.Count == 2 && model.CanApply, definition.Id + " preview model exposes exact plan");
                Check(model.Entries.All(e => File.Exists(e.Source) && !File.Exists(e.Destination)), definition.Id + " model preview leaves originals untouched");
                model.Prefix = "调整_";
                Check(model.Entries.Count == 0 && !model.CanApply, definition.Id + " changed rules invalidate preview");
                RunUi(model.ApplyAsync);
                Check(File.Exists(Path.Combine(fixture, "甲.txt")), definition.Id + " cannot execute without a current preview");
                RunUi(model.PreviewAsync);
                model.Directory = fixture + Path.DirectorySeparatorChar;
                Check(!model.CanApply && model.Entries.Count == 0, definition.Id + " changed directory invalidates preview");
                RunUi(model.PreviewAsync);
                RenderUtility(page, Path.Combine(preview, definition.Id + "-results.png"));
                RunUi(model.ApplyAsync); Check(model.Entries.Count == 0 && model.CanUndo, definition.Id + " successful execution clears stale preview"); RunUi(model.UndoAsync);
                Check(!model.CanUndo && File.Exists(Path.Combine(fixture, "甲.txt")), definition.Id + " undo restores files and disables exhausted undo action");
            }
            if (page.Model is SoftwareModel software) { RunUi(software.RefreshAsync); RenderUtility(page, Path.Combine(preview, "software-results.png")); software.Search = "no-match-" + Guid.NewGuid(); Check(!software.Entries.Any(), "software search filters inventory"); }
            if (page.Model is NetworkModel network)
            {
                network.Host = "https://"; RunUi(network.TestWebsiteAsync); Check(!network.IsBusy && !network.ResultsLayout, "invalid network input returns to idle without sending request");
                network.Host = "www.microsoft.com"; RunUi(network.TestWebsiteAsync);
                Check(!network.IsBusy && network.ResultsLayout && network.Entries.Any(e => e.Name == "DNS 解析") && network.Entries.Any(e => e.Name == "HTTPS 连接"), "network diagnostic reports success or bounded failure for each remote check");
                var report = network.BuildReport(); network.Host = "different.example";
                Check(network.CanExport && report.Contains("技术记录") && report.Contains("www.microsoft.com") && network.BuildReport() == report, "report preserves tested target and evidence after input changes");
                Check(!TabDescendants(page).OfType<System.Windows.Controls.DataGrid>().Any(), "network results show guidance instead of raw diagnostic table");
                var reportFile = Path.Combine(root, "network-report.txt"); File.WriteAllText(reportFile, report, new System.Text.UTF8Encoding(true));
                Check(File.ReadAllText(reportFile) == report && report.Contains('\n'), "diagnostic report round trips as readable UTF-8 text");
                RenderUtility(page, Path.Combine(preview, "network-results.png"));
                var details = NetworkPage.CreateDetails(network);
                var detailContent = (FrameworkElement)details.Content; details.Content = null;
                RenderUtility(detailContent, Path.Combine(preview, "network-details.png"));
            }
            platform.Uninstall(module); Check(module.IsInstalled, definition.Id + " remains bundled");
        }
        using var scannerContext = new ModuleContext(BuiltInModules.Scanner, accounts, () => Task.CompletedTask);
        var scanner = new ScannerModel(scannerContext) { Directory = duplicates, Duplicates = true }; var scannerView = new ScannerView(scanner);
        RenderUtility(scannerView, Path.Combine(preview, "duplicates-start.png")); RunUi(scanner.ScanAsync);
        Check(scanner.Files.Count == 2 && !scanner.IsBusy && scanner.ResultsLayout, "duplicate mode integrates with scanner lifecycle"); RenderUtility(scannerView, Path.Combine(preview, "duplicates-results.png"));
        var hidden = platform.Modules[0]; hidden.IsVisible = false; var home = new HomeView(platform);
        Check(!((ItemsControl)home.FindName("ApplicationList")).Items.Contains(hidden), "all-apps home respects homepage visibility setting"); hidden.IsVisible = true;
    }
}
