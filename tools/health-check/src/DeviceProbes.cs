using System.Management;
using System.Globalization;

namespace XieBox.HealthCheck;

// Read-only Windows observations. A successful enumeration is not a stress test.
public static class DeviceProbes
{
    public static IReadOnlyList<HealthProbe> Create() => [
        new("系统设备状态", "hardware-check", "查看硬件", token => Task.Run(() => Devices(token), token)),
        new("存储健康状态", "hardware-check", "查看硬件", token => Task.Run(() => Storage(token), token)),
        new("内存基础读写测试", "hardware-check", "查看硬件", token => Task.Run(() => MemoryReadWriteCheck.Run(token), token))
    ];

    internal static List<Dictionary<string, object?>> Query(string scope, string query, CancellationToken token)
    {
        using var search = new ManagementObjectSearcher(scope, query);
        search.Options.Timeout = TimeSpan.FromSeconds(5);
        using var results = search.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementObject result in results)
        {
            using (result)
            {
                token.ThrowIfCancellationRequested();
                rows.Add(result.Properties.Cast<PropertyData>().ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.OrdinalIgnoreCase));
            }
        }
        token.ThrowIfCancellationRequested();
        return rows;
    }
    private static string Text(Dictionary<string, object?> row, string key) => Convert.ToString(row.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "";
    private static int? Code(Dictionary<string, object?> row, string key) => int.TryParse(Text(row, key), out var code) ? code : null;
    private static string Name(Dictionary<string, object?> row) => string.IsNullOrWhiteSpace(Text(row, "Name")) ? "未命名设备" : Text(row, "Name");

    private static HealthResult Devices(CancellationToken token)
    {
        var rows = Query(@"root\cimv2", "SELECT Name,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Present = TRUE", token);
        var errors = rows.Where(r => Code(r, "ConfigManagerErrorCode") is > 0 and not 22).ToArray();
        var disabled = rows.Count(r => Code(r, "ConfigManagerErrorCode") == 22);
        var unknown = rows.Count(r => Code(r, "ConfigManagerErrorCode") is null);
        var details = $"Windows 当前识别 {rows.Count} 个设备；已禁用 {disabled} 个（可能为用户主动设置）。";
        if (errors.Length > 0) details += "\n" + string.Join("\n", errors.Select(r => $"{Name(r)}：设备错误代码 {Code(r, "ConfigManagerErrorCode")}，需进一步排查驱动或连接。"));
        else details += "\n未发现系统报告的设备错误；不代表已验证设备全部功能。";
        if (unknown > 0) details += $"\n{unknown} 个设备未提供状态。";
        return new("系统设备状态", "hardware-check", details, "查看硬件", errors.Length > 0 ? HealthState.Issue : rows.Count == 0 || unknown > 0 ? HealthState.Incomplete : HealthState.Normal);
    }
    private static HealthResult Storage(CancellationToken token)
    {
        var rows = Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName,DeviceId,HealthStatus,Size FROM MSFT_PhysicalDisk", token);
        if (rows.Count == 0) return new("存储健康状态", "hardware-check", "系统没有返回可用的物理磁盘健康信息，不能据此判断磁盘正常。", "查看硬件", HealthState.Incomplete);
        var issue = rows.Any(r => Code(r, "HealthStatus") == 2);
        var warning = rows.Any(r => Code(r, "HealthStatus") == 1);
        var unknown = rows.Any(r => Code(r, "HealthStatus") is not (0 or 1 or 2));
        var detail = string.Join("\n", rows.Select(r => $"磁盘 {Text(r, "DeviceId")} · {Text(r, "FriendlyName")}：" + (Code(r, "HealthStatus") switch { 0 => "系统报告正常", 1 => "系统报告警告", 2 => "系统报告不健康", _ => "状态不可用" })));
        return new("存储健康状态", "hardware-check", detail + "\n依据：Windows 存储健康状态；未进行全盘读写或坏块检测，不能保证数据安全。", "查看硬件", issue ? HealthState.Issue : warning ? HealthState.Suggested : unknown ? HealthState.Incomplete : HealthState.Normal);
    }
}
