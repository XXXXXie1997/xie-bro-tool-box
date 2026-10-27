using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace XieBox.StartupManager;

public sealed record StartupSource(RegistryHive Hive, RegistryView View, string Key, string? ApprovalKey, bool Writable, string Label);
public sealed record StartupEntry(string Name, string Command, string Location, bool Enabled, bool CanChange,
    string Note, StartupSource? Source = null, string? Folder = null)
{
    public bool RequiresAdministrator => Source?.Hive == RegistryHive.LocalMachine || (Folder is not null && string.Equals(Folder, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StringComparison.OrdinalIgnoreCase));
    public string State => Enabled ? "已启用" : "已禁用";
    public string Action => Enabled ? "关闭启动" : "恢复启动";
    public string Initial => string.IsNullOrEmpty(Name) ? "↗" : Name[..1].ToUpperInvariant();
    public string Publisher { get; init; } = "发布者未知";
    public System.Windows.Media.ImageSource Icon { get; init; } = XieBox.Platform.UI.ApplicationIcon.Fallback;
}
public sealed record StartupScan(IReadOnlyList<StartupEntry> Entries, int Unavailable);

// Disabled values keep their original registry type and data. No RunOnce, services or drivers are edited.
public sealed class StartupEngine
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    public const string BackupFolder = "XieBox.DisabledStartup";
    private readonly StartupSource[] _sources;
    private readonly (string Path, bool Writable, string Label)[] _folders;
    public StartupEngine() : this([
        new(RegistryHive.CurrentUser, RegistryView.Registry64, Run, Approved + "Run", true, "当前用户 · 注册表"),
        new(RegistryHive.LocalMachine, RegistryView.Registry64, Run, Approved + "Run", true, "所有用户 · 注册表"),
        new(RegistryHive.LocalMachine, RegistryView.Registry32, Run, Approved + "Run32", true, "所有用户 · 32 位注册表")],
        [(Environment.GetFolderPath(Environment.SpecialFolder.Startup), true, "当前用户 · 启动文件夹"),
         (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true, "所有用户 · 启动文件夹")]) { }
    public StartupEngine(StartupSource[] sources, (string Path, bool Writable, string Label)[] folders)
    { _sources = sources; _folders = folders; }

    public StartupScan Scan(CancellationToken token = default)
    {
        var entries = new List<StartupEntry>(); var unavailable = 0;
        foreach (var source in _sources)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var hive = RegistryKey.OpenBaseKey(source.Hive, source.View);
                using var approval = source.ApprovalKey is null ? null : hive.OpenSubKey(source.ApprovalKey);
                foreach (var enabled in new[] { true, false })
                {
                    using var key = hive.OpenSubKey(enabled ? source.Key : source.Key + ".XieBoxDisabled");
                    if (key is null) continue;
                    foreach (var name in key.GetValueNames())
                    {
                        token.ThrowIfCancellationRequested();
                        if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command) continue;
                        var externallyDisabled = IsExternallyDisabled(approval?.GetValue(name));
                        var writable = source.Writable && !externallyDisabled;
                        entries.Add(new(name, command, source.Label, enabled && !externallyDisabled, writable,
                            externallyDisabled ? "此项由 Windows 或其他工具禁用，请在系统启动应用设置中恢复。" :
                            !source.Writable ? "此项影响所有用户，本版仅展示；可前往系统启动应用设置管理。" : source.Hive == RegistryHive.LocalMachine ? "影响所有用户，修改时会请求管理员授权。" : "仅控制登录时自动启动，不影响手动打开应用。用途未知，请按需操作。", source)
                            { Publisher = Publisher(command), Icon = XieBox.Platform.UI.ApplicationIcon.Load(command) });
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { unavailable++; }
        }
        foreach (var folder in _folders)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var enabled in new[] { true, false })
                {
                    var path = enabled ? folder.Path : Path.Combine(folder.Path, BackupFolder);
                    if (!Directory.Exists(path)) continue;
                    EnsureNormalDirectory(path);
                    foreach (var file in Directory.EnumerateFiles(path))
                    {
                        token.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(file) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0 || Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        using var hive = RegistryKey.OpenBaseKey(string.Equals(folder.Path, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
                        using var approval = hive.OpenSubKey(Approved + "StartupFolder");
                        var externallyDisabled = IsExternallyDisabled(approval?.GetValue(Path.GetFileName(file)));
                        entries.Add(new(Path.GetFileName(file), file, folder.Label, enabled && !externallyDisabled, folder.Writable && !externallyDisabled,
                            externallyDisabled ? "此项由系统禁用，请在系统启动应用设置中恢复。" : folder.Writable ? "关闭后移入启动文件夹内的备份子目录，恢复时移回；不会删除文件。" : "所有用户的启动项，本版仅展示。", Folder: folder.Path) { Icon = XieBox.Platform.UI.ApplicationIcon.Load(file) });
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { unavailable++; }
        }
        return new(entries.OrderByDescending(e => e.Enabled).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), unavailable);
    }
    public static bool IsExternallyDisabled(object? value) => value is byte[] { Length: > 0 } bytes && bytes[0] != 2 && bytes[0] != 6;

    public void Toggle(StartupEntry entry)
    {
        if (!entry.CanChange) throw new InvalidOperationException("此启动项暂不支持直接修改。");
        if (entry.Source is { } source)
        {
            if (!_sources.Contains(source) || !source.Writable) throw new InvalidOperationException("启动来源不匹配。");
            using var hive = RegistryKey.OpenBaseKey(source.Hive, source.View);
            using var approval = source.ApprovalKey is null ? null : hive.OpenSubKey(source.ApprovalKey);
            if (IsExternallyDisabled(approval?.GetValue(entry.Name))) throw new InvalidOperationException("系统状态已变化，请刷新列表。");
            using var from = hive.OpenSubKey(entry.Enabled ? source.Key : source.Key + ".XieBoxDisabled", true);
            if (from is null || from.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string data || data != entry.Command)
                throw new InvalidOperationException("启动项已变化，请刷新后重试。");
            using var to = hive.CreateSubKey(entry.Enabled ? source.Key + ".XieBoxDisabled" : source.Key);
            if (to.GetValueNames().Contains(entry.Name, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("目标位置已有同名启动项，未覆盖，请先检查。");
            to.SetValue(entry.Name, data, from.GetValueKind(entry.Name)); to.Flush();
            // Backup is durable before deleting the original. A failure leaves recoverable data at both locations.
            from.DeleteValue(entry.Name, true); from.Flush();
        }
        else if (entry.Folder is { } folder)
        {
            if (!_folders.Any(f => f.Path == folder && f.Writable) || Path.GetFileName(entry.Name) != entry.Name) throw new InvalidOperationException("启动位置不匹配。");
            EnsureNormalDirectory(folder);
            var backup = Path.Combine(folder, BackupFolder);
            if (!Directory.Exists(backup)) Directory.CreateDirectory(backup);
            EnsureNormalDirectory(backup);
            var from = Path.Combine(entry.Enabled ? folder : backup, entry.Name);
            var to = Path.Combine(entry.Enabled ? backup : folder, entry.Name);
            if (from != entry.Command || (File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("文件已变化，请刷新后重试。");
            using var hive = RegistryKey.OpenBaseKey(entry.RequiresAdministrator ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
            using var approval = hive.OpenSubKey(Approved + "StartupFolder");
            if (IsExternallyDisabled(approval?.GetValue(entry.Name))) throw new InvalidOperationException("系统状态已变化，请刷新列表。");
            File.Move(from, to, false);
        }
        else throw new InvalidOperationException("无法识别启动来源。");
    }
    private static void EnsureNormalDirectory(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("不修改链接目录中的启动项。");
    }
    private static string Publisher(string command)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(command).Trim();
            var path = expanded.StartsWith('"') ? expanded.Split('"').ElementAtOrDefault(1) : expanded.Split(' ')[0];
            return path is not null && Path.IsPathFullyQualified(path) && File.Exists(path)
                ? FileVersionInfo.GetVersionInfo(path).CompanyName is { Length: > 0 } company ? company + "（文件声明）" : "发布者未知" : "发布者未知";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return "发布者未知"; }
    }
}
