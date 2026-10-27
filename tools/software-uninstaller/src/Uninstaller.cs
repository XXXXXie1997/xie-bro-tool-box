using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using XieBox.Contracts;
using XieBox.Platform.UI;

namespace XieBox.SoftwareUninstaller;

public sealed record SoftwareEntry(string Name, string Publisher, string Version, long Kilobytes, string Command, string Key, RegistryHive Hive, RegistryView View, bool Msi)
{
    public string Size => Kilobytes > 0 ? $"{Kilobytes / 1024d:0.#} MB" : "未提供";
    public string Scope => Hive == RegistryHive.CurrentUser ? "当前用户" : "所有用户";
    public System.Windows.Media.ImageSource Icon { get; init; } = ApplicationIcon.Fallback;
}
public static class SoftwareEngine
{
    private const string Branch = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public static IReadOnlyList<SoftwareEntry> Read(CancellationToken token)
    {
        var entries = new List<SoftwareEntry>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view); using var branch = root.OpenSubKey(Branch);
                if (branch is null) continue;
                foreach (var name in branch.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = branch.OpenSubKey(name); if (key is null) continue;
                        if (key.GetValue("NoRemove") is int noRemove && noRemove == 1) continue;
                        if (key.GetValue("SystemComponent") is int system && system == 1 || key.GetValue("ParentKeyName") is string) continue;
                        var title = key.GetValue("DisplayName") as string; if (string.IsNullOrWhiteSpace(title)) continue;
                        var command = key.GetValue("UninstallString") as string ?? "";
                        var size = key.GetValue("EstimatedSize") is int kb ? Math.Max(0L, kb) : 0;
                        entries.Add(new(title, key.GetValue("Publisher") as string ?? "—", key.GetValue("DisplayVersion") as string ?? "—", size, command, name, hive, view, key.GetValue("WindowsInstaller") is int msi && msi == 1)
                        { Icon = ApplicationIcon.Load(key.GetValue("DisplayIcon") as string, command) });
                    }
                    catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
                }
            }
            catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return entries.DistinctBy(e => (e.Hive, e.Key, e.Command)).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public static ProcessStartInfo LaunchInfo(SoftwareEntry entry)
    {
        if (entry.Msi && Guid.TryParse(entry.Key, out var product)) return new(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), "/x " + product.ToString("B")) { UseShellExecute = true };
        var command = Environment.ExpandEnvironmentVariables(entry.Command.Trim());
        string executable, arguments;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1); if (end < 0) throw new IOException("卸载命令格式无法识别，请通过系统设置卸载。");
            executable = command[1..end]; arguments = command[(end + 1)..].Trim();
        }
        else
        {
            var end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end < 0) throw new IOException("未提供可直接运行的卸载程序，请通过系统设置卸载。");
            executable = command[..(end + 4)]; arguments = command[(end + 4)..].Trim();
        }
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable)) throw new IOException("卸载程序不存在或路径不明确，请通过系统设置卸载。");
        if (new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe" }.Contains(Path.GetFileName(executable), StringComparer.OrdinalIgnoreCase)) throw new IOException("此软件需要系统卸载入口，请点击“系统应用设置”。");
        return new(executable, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
    }
    public static SoftwareEntry Revalidate(SoftwareEntry original, CancellationToken token)
    {
        var current = Read(token).FirstOrDefault(e => e.Hive == original.Hive && e.View == original.View && e.Key == original.Key);
        if (current is null || current.Command != original.Command || current.Msi != original.Msi || current.Name != original.Name) throw new IOException("软件信息已变化，请刷新后重新选择。");
        return current;
    }
}
public sealed class SoftwareModel(IPlatformContext context) : ToolModel
{
    private IReadOnlyList<SoftwareEntry> _entries = [];
    private string _search = "";
    public string Search { get => _search; set { _search = value; Notify(); Notify(nameof(Entries)); } }
    public IEnumerable<SoftwareEntry> Entries => _entries.Where(e => (e.Name + " " + e.Publisher).Contains(Search, StringComparison.OrdinalIgnoreCase));
    public Task RefreshAsync() => Run(async token =>
    {
        if (!(await context.CheckCapabilityAsync("scan", token)).Allowed) { Message("平台未允许软件查看能力。"); return; }
        ResultsLayout = true; Message("正在读取软件清单…");
        _entries = await Task.Run(() => SoftwareEngine.Read(token), token); Notify(nameof(Entries));
        Message($"发现 {_entries.Count} 个桌面软件。体积由软件提供；商店应用及未识别项目请使用系统应用设置。");
    });
    public Task UninstallAsync(SoftwareEntry entry) => Run(async token =>
    {
        if (!(await context.CheckCapabilityAsync("manage", token)).Allowed) { Message("平台未允许卸载能力。"); return; }
        var current = await Task.Run(() => SoftwareEngine.Revalidate(entry, token), token);
        Process? started;
        try
        {
            var start = SoftwareEngine.LaunchInfo(current);
            try { started = Process.Start(start); }
            catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 740)
            { start.Verb = "runas"; started = Process.Start(start); }
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        { Message("已取消管理员授权，未启动卸载程序。可再次点击卸载重试。"); return; }
        using var process = started;
        Message("已打开原卸载程序，请按其提示完成。关闭本页不会结束卸载程序。");
        if (process is not null) await process.WaitForExitAsync(token);
        _entries = await Task.Run(() => SoftwareEngine.Read(token), token); Notify(nameof(Entries));
        Message("卸载程序已退出，清单已刷新；是否卸载成功请以软件提示和最新清单为准。");
    });
}
public static class SoftwarePage
{
    public static ToolPage Create(IPlatformContext context)
    {
        var model = new SoftwareModel(context);
        var page = new ToolPage(model, "软件卸载", "查看已安装软件并运行卸载程序。", new Grid(), "查看软件", model.RefreshAsync);
        page.Table.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Entries"));
        page.Table.RowHeight = 58;
        var namePanel = new FrameworkElementFactory(typeof(DockPanel));
        namePanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var icon = new FrameworkElementFactory(typeof(Image));
        icon.SetBinding(Image.SourceProperty, new Binding("Icon"));
        icon.SetValue(FrameworkElement.WidthProperty, 32d); icon.SetValue(FrameworkElement.HeightProperty, 32d);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 12, 0)); namePanel.AppendChild(icon);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Name")); namePanel.AppendChild(name);
        page.Table.Columns.Add(new DataGridTemplateColumn { Header = "软件名称", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 200, CellTemplate = new DataTemplate { VisualTree = namePanel } });
        page.Column("发布者", "Publisher", 170); page.Column("版本", "Version", 110); page.Column("大小", "Size", 90); page.Column("范围", "Scope", 90);
        var search = ToolPage.Input("Search"); search.Width = 180; search.ToolTip = "搜索软件名称或发布者"; page.Actions.Children.Add(search);
        page.Actions.Children.Add(ToolPage.Button("刷新", async () => await model.RefreshAsync()));
        page.FooterActions.Children.Add(ToolPage.Button("系统应用设置", () => { try { Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception e) { model.Message(e.Message); } }));
        var uninstall = new FrameworkElementFactory(typeof(Button));
        uninstall.SetValue(ContentControl.ContentProperty, "卸载");
        var actionText = new FrameworkElementFactory(typeof(TextBlock));
        actionText.SetBinding(TextBlock.TextProperty, new Binding());
        actionText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        uninstall.SetValue(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = actionText });
        uninstall.SetResourceReference(Control.BorderBrushProperty, "DangerBorderBrush");
        uninstall.SetResourceReference(Control.BackgroundProperty, "DangerSurfaceBrush");
        uninstall.SetResourceReference(Control.ForegroundProperty, "DangerBrush");
        uninstall.SetValue(Control.PaddingProperty, new Thickness(14, 6, 14, 6));
        uninstall.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        uninstall.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        uninstall.SetBinding(UIElement.IsEnabledProperty, new Binding("DataContext.IsIdle") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1) });
        uninstall.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Name") { StringFormat = "卸载 {0}" });
        uninstall.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (sender, e) =>
        {
            e.Handled = true;
            if (sender is not Button { DataContext: SoftwareEntry entry } || !model.IsIdle) return;
            if (DialogWindow.Confirm(Window.GetWindow(page), $"即将打开“{entry.Name}”的原卸载程序。\n发布者：{entry.Publisher}\n卸载选项由该程序提供，请留意个人数据保留选项。", "卸载软件", "打开卸载程序")) await model.UninstallAsync(entry);
        }));
        page.Table.Columns.Add(new DataGridTemplateColumn { Header = "操作", Width = 90, CellTemplate = new DataTemplate { VisualTree = uninstall } });
        return page;
    }
}

