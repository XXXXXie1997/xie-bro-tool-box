using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using XieBox.Contracts;
using XieBox.Platform.UI;

namespace XieBox.GarbageCleaner;

public sealed class SystemSlimModel(IPlatformContext context) : ToolModel, IDisposable
{
    private readonly SystemSlimSession session = new();
    public ScanSnapshot? Caches { get; private set; }
    public Task CleanCache(string id, IntPtr owner) => Execute("cache:" + id);
    public void Dispose() => session.Dispose();
    public SlimSnapshot? Snapshot { get; private set; }
    public DateTime AnalyzedAt { get; private set; }
    public Task Analyze() => Execute("scan");
    public Task Execute(string command) => Run(async token =>
    {
        var access = await context.CheckCapabilityAsync(command == "scan" ? "scan" : "system-slim");
        if (!access.Allowed) { Message(access.Message); return; }
        ResultsLayout = true;
        // Invalidate the previous plan before attempting a new scan or action.
        Snapshot = null; Caches = null; Notify(nameof(Snapshot)); Notify(nameof(Caches));
        Message(command == "scan" ? "正在分析系统盘，请完成 Windows 管理员授权。分析期间不会删除文件…" : "Windows 正在处理，请勿关机。完成后会显示结果…");
        try
        {
            var reply = await session.Request(command);
            Caches = reply.Caches?.ToSnapshot(); Notify(nameof(Caches));
            Snapshot = reply.Snapshot;
            if (Snapshot is not null) AnalyzedAt = DateTime.Now;
            Notify(nameof(Snapshot));
            Message(reply.Message + (command == "scan" ? "" : "\n请再次分析，查看当前剩余内容。"));
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { Message("已取消管理员授权，本次扫描未开始。点击再次分析可重新授权。"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException or System.Text.Json.JsonException)
        { Message("未能完成：" + e.Message + " 请再次分析确认当前状态。"); }
    });
}

public static class SystemSlimView
{
    public static ToolPage Create(IPlatformContext context)
    {
        var model = new SystemSlimModel(context);
        var page = new ToolPage(model, "系统盘瘦身", "查看系统盘中可清理的内容、占用空间和清理影响。", new Grid(), "开始分析", model.Analyze);
        page.Actions.Children.Clear();
        page.Actions.Children.Add(ToolPage.Button("再次分析", async () => await model.Analyze(), true));
        var list = new StackPanel();
        page.SetResultsContent(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        async Task Act(string command, string title, string impact)
        {
            if (command.StartsWith("cache:", StringComparison.Ordinal))
            {
                if (model.Caches?.Complete != true) { model.Message("扫描未完成，请重新分析。"); return; }
                var category = model.Caches.Categories.Single(c => c.Id == command[6..]);
                if (!DialogWindow.Confirm(Window.GetWindow(page), $"将处理 {category.Name}：{category.CountLabel}，{category.Size}。\n\n{impact}\n\n仅处理本次扫描结果，文件删除不可撤销。", title, "确认清理")) return;
                await model.CleanCache(category.Id, new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(page)).Handle); return;
            }
            if (model.Snapshot is null || DateTime.Now - model.AnalyzedAt > TimeSpan.FromMinutes(10)) { model.Message("结果已过期，请再次分析后操作。"); return; }
            if (!DialogWindow.Confirm(Window.GetWindow(page), impact + "\n\n仅处理这一项。将在已授权的管理员会话中执行。", title, "确认执行")) return;
            await model.Execute(command);
        }
        void Render()
        {
            list.Children.Clear();
            if (model.Caches is { } cache)
            {
                if (!cache.Complete) list.Children.Add(ToolPage.Text(cache.Summary, 12));
                var hidden = cache.Categories.Count(c => c.Bytes == 0);

                var issues = cache.Categories.Where(c => c.ScanNotes.Count > 0 || c.Status.Contains("无法查询")).ToArray();
                if (issues.Length > 0)
                    list.Children.Add(new Expander { Header = $"{issues.Length} 项未读完整 · 查看原因", Margin = new Thickness(0, 8, 0, 12), Content = ToolPage.Text(string.Join("\n\n", issues.Select(c => c.Name + "\n" + (c.ScanNotes.Count > 0 ? string.Join("\n", c.ScanNotes) : c.Status))), 12) });
                foreach (var category in cache.Categories.Where(c => c.Bytes > 0).OrderByDescending(c => c.Bytes))
                {
                    var paths = category.Rule.Roots.Select(r => r.Path).ToArray();
                    Card(category.Name, category.Status, category.Size, category.CountLabel + (category.ScanNotes.Count > 0 ? " · 已统计部分" : ""),
                        category.Description, category.IsRecycleBin ? "仅处理当前用户的系统盘回收站。" : paths.Length == 0 ? "未发现标准目录，或目录位于其他磁盘。" : string.Join("\n", paths),
                        category.Rule.Consequence, category.Rule.Recommended ? "可按需清理，请保留仍需使用的资料。" : "请确认不再需要这些文件后清理。",
                        cache.Complete && category.CanSelect ? "清理这一项" : null, "cache:" + category.Id);
                }
            }
            if (model.Snapshot is not { } value)
            {
                var waiting = ToolPage.Text("分析完成后，显示可以整理的内容。", 14, false, true);
                waiting.Margin = new Thickness(0, 40, 0, 0); list.Children.Add(waiting); return;
            }
            var summary = ToolPage.Text($"系统盘 {SystemSlimService.SystemRoot} · 当前可用 {Format.Bytes(value.FreeBytes)} · 分析于 {model.AnalyzedAt:HH:mm}", 14, true);
            summary.Margin = new Thickness(0, 4, 0, 8); list.Children.Insert(0, summary);
            var note = ToolPage.Text("按需清理，实际释放空间以完成后的结果为准。", 12);
            note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); note.Margin = new Thickness(0, 0, 0, 16); list.Children.Insert(1, note);
            var store = Field(value.ComponentAnalysis, "Actual Size of Component Store");
            var backups = Field(value.ComponentAnalysis, "Backups and Disabled Features");
            var temporary = Field(value.ComponentAnalysis, "Cache and Temporary Data");
            var packages = Field(value.ComponentAnalysis, "Number of Reclaimable Packages");
            var recommendation = Field(value.ComponentAnalysis, "Component Store Cleanup Recommended");
            bool recommended = recommendation.Equals("Yes", StringComparison.OrdinalIgnoreCase);
            if (!value.ComponentReady || packages != "0" || recommended) Card("旧更新文件", value.ComponentReady ? recommended ? "Windows 建议清理" : "已完成分析" : "未能完成分析",
                value.ComponentReady && store.Length > 0 ? store : "大小暂不可用", "组件库占用，非可清理量",
                "Windows 更新时保留的旧组件，以及维护系统需要的文件。它们存放在 Windows\\WinSxS 中，不能把整个文件夹都删掉。",
                value.ComponentReady ? $"可回收的更新包：{Display(packages)}\n备份和已禁用功能：{Display(backups)}；缓存及临时数据：{Display(temporary)}。这些内容不一定都能删除。" : "本次未获取到完整结果，不会把读取失败当成没有占用。",
                "Windows 会移除已被新版替代的部分旧组件。不会删除整个组件库；被清除的旧文件不能由本应用恢复。实际释放量要等清理完成后查看。",
                value.ComponentReady ? recommended ? "适合清理更新遗留；不急用空间也可以保留。" : "Windows 未明确建议清理，可先保留。" : "请重新分析后再决定。",
                value.ComponentReady ? "清理旧组件" : null, "components", value.ComponentAnalysis);
            var hasHibernation = value.HibernateBytes is > 0;
            if (value.HibernateBytes != 0) Card("休眠文件", hasHibernation ? "已找到" : value.HibernateBytes == 0 ? "未发现文件" : "无法读取",
                value.HibernateBytes is long size ? Format.Bytes(size) : "大小暂不可用", "文件占用",
                "休眠会把当前打开的程序状态保存到系统盘，下次开机可以接着用。快速启动也可能使用这个文件。",
                "位置：" + SystemSlimService.SystemRoot + "hiberfil.sys",
                "关闭后不能休眠，混合睡眠和快速启动也会受到影响。可以重新启用，但文件会重新占用空间。",
                "经常用休眠、合盖后继续工作的用户，建议保留。",
                hasHibernation ? "关闭休眠并释放空间" : value.HibernateBytes == 0 ? "启用休眠（会占空间）" : null,
                hasHibernation ? "hibernate-off" : "hibernate-on");
            if (value.OldPresent) Card("旧版 Windows", value.OldPresent ? "已找到旧系统文件" : "未发现旧系统文件",
                value.OldPresent ? Format.Bytes(value.OldBytes) : "0 B", value.OldPartial ? "仅统计到部分文件" : "已统计大小",
                "升级 Windows 后留下的旧系统，用于退回升级前的版本。里面也可能有旧用户目录中的个人文件。",
                "位置：" + SystemSlimService.SystemRoot + "Windows.old" + (value.OldPartial ? "\n部分内容无法读取或未统计完，实际占用可能更多。" : ""),
                "清理后无法退回上一版 Windows。Windows 会处理旧系统及升级遗留文件；执行前请确认旧用户目录中需要的文件已经取回。",
                "刚升级、还没确认新系统稳定，建议保留。",
                value.OldPresent ? "清理旧系统文件" : null, "old-windows");
        }
        void Card(string title, string found, string size, string sizeLabel, string what, string detail, string impact, string suggestion, string? action, string command, string? raw = null)
        {
            var body = new Grid(); body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = new GridLength(210) });
            var text = new StackPanel { Margin = new Thickness(0, 0, 24, 0) }; body.Children.Add(text);
            text.Children.Add(ToolPage.Text(title, 16, true));
            var shortWhat = command switch
            {
                "components" => "Windows 更新后留下的旧文件。",
                "hibernate-off" or "hibernate-on" => "保存休眠状态，方便下次开机继续工作。",
                "old-windows" => "升级前的 Windows，用来退回旧版本。",
                "cache:recycle-bin" => "已删除、还可以从回收站找回的文件。",
                "cache:temporary" or "cache:windows-temp" => "超过 7 天的临时文件和日志。",
                "cache:crash-dumps" or "cache:blue-screen-dumps" => "用于排查崩溃或蓝屏的记录。",
                "cache:error-reports" or "cache:system-error-reports" => "Windows 保存的故障报告。",
                "cache:shader-cache" or "cache:nvidia-cache" or "cache:amd-cache" => "帮助游戏和图形程序加快加载的缓存。",
                _ => "浏览器保存的网页图片和资源。"
            };
            var shortImpact = command switch
            {
                "components" => "删除后无法恢复这些旧文件。",
                "hibernate-off" or "hibernate-on" => "关闭后不能休眠，混合睡眠和快速启动也受影响；可重新启用。",
                "old-windows" => "删除后不能回退旧系统，请先取回需要的个人文件。",
                "cache:recycle-bin" => "清空后不能从回收站还原。",
                "cache:temporary" or "cache:windows-temp" => "删除后不能找回这些临时文件和日志。",
                "cache:crash-dumps" or "cache:blue-screen-dumps" or "cache:error-reports" or "cache:system-error-reports" => "删除后无法用这些记录排查故障。",
                "cache:shader-cache" or "cache:nvidia-cache" or "cache:amd-cache" => "下次游戏加载可能变慢或短暂卡顿。",
                _ => "网页会重新下载资源；密码、书签和登录信息保留。清理前请退出浏览器。"
            };
            var description = ToolPage.Text(shortWhat, 13); description.Margin = new Thickness(0, 7, 0, 5);
            description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); text.Children.Add(description);
            var consequence = ToolPage.Text(shortImpact, 12); consequence.Margin = new Thickness(0, 0, 0, 6); text.Children.Add(consequence);
            var extra = new StackPanel();
            extra.Children.Add(ToolPage.Text(found + "\n" + what + "\n" + detail + "\n\n" + impact + "\n" + suggestion, 12));
            if (command.StartsWith("cache:", StringComparison.Ordinal) && model.Caches?.Categories.Single(c => c.Id == command[6..]) is { IsRecycleBin: false } category && category.Files.Count > 0)
            {
                var details = ToolPage.Button("查看文件明细", () =>
                {
                    var table = new DataGrid { ItemsSource = category.Files, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 320, EnableRowVirtualization = true };
                    table.Columns.Add(new DataGridTextColumn { Header = "文件", Binding = new System.Windows.Data.Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                    table.Columns.Add(new DataGridTextColumn { Header = "大小", Binding = new System.Windows.Data.Binding("Size"), Width = 100 });
                    new DialogWindow { Owner = Window.GetWindow(page), Title = category.Name + " · 本次扫描明细", Width = Math.Min(850, SystemParameters.WorkArea.Width - 48), Content = table }.FitContent().ShowDialog();
                });
                details.HorizontalAlignment = HorizontalAlignment.Left; extra.Children.Add(details);
            }
            if (raw is not null) extra.Children.Add(new Expander { Header = "技术信息", Content = ToolPage.Text(raw, 11) });
            text.Children.Add(new Expander { Header = "查看详情", Content = extra });
            var right = new StackPanel(); Grid.SetColumn(right, 1); body.Children.Add(right);
            right.Children.Add(ToolPage.Text(size, 22, true, true));
            var hint = ToolPage.Text(sizeLabel, 12, false, true); hint.Margin = new Thickness(0, 4, 0, 10); hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); right.Children.Add(hint);
            if (action is not null)
            {
                var button = ToolPage.Button(action, async () => await Act(command, title, command == "hibernate-on" ? "启用休眠会重新创建休眠文件，占用系统盘空间。" : impact));
                button.HorizontalAlignment = HorizontalAlignment.Center; right.Children.Add(button);
            }
            var border = new Border { Child = body, Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); list.Children.Add(border);
        }
        model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(model.Snapshot) or nameof(model.Caches)) Render(); };
        return page;
    }
    public static string Field(string output, string label)
        => Regex.Match(output, @"^\s*" + Regex.Escape(label) + @"\s*:\s*([^\r\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
    private static string Display(string text) => string.IsNullOrWhiteSpace(text) ? "系统未提供" : text;
}
