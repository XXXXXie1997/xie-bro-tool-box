using System.Text.RegularExpressions;
namespace XieBox.HardwareCheck;

public static class HardwareLabels
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Composite Temperature"] = "综合温度", ["Warning Temperature"] = "警告温度阈值", ["Critical Temperature"] = "临界温度阈值",
        ["Life"] = "剩余寿命", ["Percentage Used"] = "寿命消耗比例", ["Available Spare"] = "可用备用空间", ["Available Spare Threshold"] = "备用空间警告阈值",
        ["Data Read"] = "累计读取量", ["Data Written"] = "累计写入量", ["Power On Count"] = "通电次数", ["Power On Hours"] = "累计通电时间", ["Power Cycles"] = "通电次数",
        ["Unsafe Shutdowns"] = "非正常关机次数", ["Media Errors"] = "介质错误次数", ["Used Space"] = "已用空间", ["Read Activity"] = "读取活动", ["Write Activity"] = "写入活动", ["Total Activity"] = "总活动",
        ["Read Rate"] = "读取速率", ["Write Rate"] = "写入速率", ["Read Throughput"] = "读取速率", ["Write Throughput"] = "写入速率",
        ["CPU Package"] = "处理器封装", ["CPU Total"] = "处理器总占用", ["CPU Cores"] = "处理器核心总计", ["CPU Core Max"] = "处理器核心最高值", ["CPU Memory"] = "处理器内存控制器", ["CPU Platform"] = "处理器平台",
        ["Core Max"] = "核心最高温度", ["Core Average"] = "核心平均温度", ["Bus Speed"] = "总线频率",
        ["Memory"] = "内存", ["Memory Used"] = "已用内存", ["Memory Available"] = "可用内存",
        ["GPU Core"] = "显卡核心", ["GPU Core Voltage"] = "显卡核心电压", ["GPU Memory Controller"] = "显存控制器", ["GPU Memory Total"] = "显存总容量", ["GPU Memory Free"] = "可用显存", ["GPU Memory Used"] = "已用显存", ["GPU Memory"] = "显存",
        ["GPU Video Engine"] = "显卡视频引擎", ["GPU Bus"] = "显卡总线", ["GPU Power"] = "显卡功率", ["GPU Board Power"] = "显卡整板功率", ["GPU Package"] = "显卡封装功耗", ["GPU Hot Spot"] = "显卡热点", ["GPU PCIe Rx"] = "显卡 PCIe 接收速率", ["GPU PCIe Tx"] = "显卡 PCIe 发送速率",
        ["D3D Dedicated Memory Used"] = "图形专用内存占用", ["D3D Shared Memory Used"] = "图形共享内存占用", ["D3D 3D"] = "三维渲染引擎", ["D3D Overlay"] = "图形叠加引擎", ["D3D Video Decode"] = "视频解码引擎", ["D3D Video Encode"] = "视频编码引擎", ["D3D Copy"] = "图形数据复制引擎", ["D3D Security"] = "图形安全引擎", ["D3D VR"] = "虚拟现实引擎",
        ["Charge Level"] = "剩余电量", ["Wear Level"] = "电池损耗比例", ["Remaining Capacity"] = "剩余容量", ["Full Charged Capacity"] = "满充容量", ["Designed Capacity"] = "设计容量", ["Charge Rate"] = "充电速率", ["Discharge Rate"] = "放电速率", ["Remaining Time"] = "剩余使用时间",
        ["Temperature"] = "温度", ["Fan"] = "风扇", ["Voltage"] = "电压", ["CPU VCore"] = "处理器核心电压", ["VCore"] = "核心电压", ["System"] = "系统", ["Motherboard"] = "主板", ["CPU"] = "处理器"
    };
    public static string Sensor(string name, string kind)
    {
        var meaning = Meaning(name, kind);
        return meaning == name ? name : $"{name}（{meaning}）";
    }
    private static string Meaning(string name, string kind)
    {
        if (Names.TryGetValue(name, out var label)) return label;
        var match = Regex.Match(name, @"^(CPU Core|P-Core|E-Core|GPU Fan|Temperature|Fan|Voltage|D3D Optical Flow Accelerator)\s*(#?\d+)(?:\s+Thread\s*(#?\d+))?(\s+Distance to TjMax)?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var prefix = match.Groups[1].Value.ToUpperInvariant() switch { "CPU CORE" => "处理器核心", "P-CORE" => "性能核心", "E-CORE" => "能效核心", "GPU FAN" => "显卡风扇", "FAN" => "风扇", "VOLTAGE" => "电压", "D3D OPTICAL FLOW ACCELERATOR" => "光流加速引擎", _ => "温度传感器" };
            return prefix + " " + match.Groups[2].Value + (match.Groups[3].Success ? " · 线程 " + match.Groups[3].Value : "") + (match.Groups[4].Success ? " · 距最高结温余量" : "");
        }
        if (Regex.IsMatch(name, @"[\u4e00-\u9fff]")) return name;
        // Do not invent meanings for vendor-specific channel names.
        return $"{kind}项目，厂商自定义名称";
    }
}
