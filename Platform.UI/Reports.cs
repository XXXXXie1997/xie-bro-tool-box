using System.IO;
using System.Text;
using System.Windows;
using System.Management;
using System.Reflection;
using Microsoft.Win32;

namespace XieBox.Platform.UI;

public interface IReportable
{
    string ReportTitle { get; }
    bool CanExport { get; }
    string BuildReport();
}

public static class ReportBuilder
{
    public static string ApplicationVersion => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "未知";

    public static string SystemDescription()
    {
        try
        {
            using var search = new ManagementObjectSearcher("SELECT Caption,Version,OSArchitecture FROM Win32_OperatingSystem");
            search.Options.Timeout = TimeSpan.FromSeconds(3);
            using var results = search.Get();
            foreach (ManagementObject row in results)
            {
                using (row)
                {
                    var caption = Convert.ToString(row["Caption"]);
                    if (string.IsNullOrWhiteSpace(caption)) continue;
                    var release = ""; var revision = "";
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                        release = Convert.ToString(key?.GetValue("DisplayVersion")) ?? "";
                        revision = Convert.ToString(key?.GetValue("UBR")) ?? "";
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
                    return $"{caption} {release} · {row["OSArchitecture"]}（内部版本 {row["Version"]}{(revision.Length > 0 ? "." + revision : "")}）";
                }
            }
        }
        catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { }
        return $"Windows（系统名称未能读取；内核版本 {Environment.OSVersion.Version}）";
    }
    public static string Build(string title, DateTimeOffset capturedAt, string scope, string conclusion, IEnumerable<string> details)
        => $"蟹小箱 · {title}报告\n记录时间：{capturedAt:O}\n生成时间：{DateTimeOffset.Now:O}\n版本：{ApplicationVersion}\n系统：{SystemDescription()}\n\n── 范围 ──\n{scope}\n\n── 结果 ──\n{conclusion}\n\n── 明细 ──\n{string.Join("\n", details)}\n\n── 说明 ──\n仅包含本次工具记录，不代表完整电脑体检。中断、跳过和失败情况以结果说明为准。\n报告可能包含文件路径、软件名称或网络地址；请仅提供给信任的人。不会自动上传，也不读取文件内容、密码或浏览历史。\n";
}

public static class ReportExporter
{
    public static void Save(Window? owner, IReportable report, Action<string> status)
    {
        if (!report.CanExport) return;
        var name = string.Concat(report.ReportTitle.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        // Let the actual write determine access; the shell's preliminary test can
        // reject a location before the application has attempted to save there.
        var dialog = new SaveFileDialog { Title = "保存排查报告", Filter = "文本报告 (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true, CheckPathExists = true, CreateTestFile = false, RestoreDirectory = true, FileName = $"蟹小箱-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dialog.ShowDialog(owner) != true) return;
        try { Write(dialog.FileName, report.BuildReport()); status("报告已保存。"); }
        catch (UnauthorizedAccessException) { status($"无法写入所选位置：{dialog.FileName}。请检查该文件夹的写入权限或 Windows 的文件夹保护设置。"); }
        catch (IOException e) { status($"报告保存失败：{e.Message}"); }
    }
    public static void Write(string path, string report) => File.WriteAllText(path, report, new UTF8Encoding(true));
}
