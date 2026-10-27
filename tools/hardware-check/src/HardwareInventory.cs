using System.Globalization;
using System.Management;
using System.Text;

namespace XieBox.HardwareCheck;

public sealed record HardwareField(string Name, string Value, bool Sensitive = false);
public sealed record HardwareSection(string Category, string Title, IReadOnlyList<HardwareField> Fields);
public sealed record HardwareSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<HardwareSection> Sections);

public static class HardwareInventory
{
    public const string Unknown = "暂未读取到";
    private sealed record Field(string Key, string Label, string Format = "", bool Sensitive = false);
    private sealed record Query(string Category, string Class, string Title, Field[] Fields, string Scope = @"root\cimv2");
    private static readonly Query[] Queries =
    [
        new("电脑概览", "Win32_ComputerSystem", "Name", [new("Manufacturer", "品牌"), new("Model", "整机型号"), new("SystemType", "系统架构"), new("TotalPhysicalMemory", "内存总容量", "bytes"), new("NumberOfLogicalProcessors", "逻辑处理器数量")]),
        new("电脑概览", "Win32_OperatingSystem", "Caption", [new("Version", "系统版本"), new("BuildNumber", "内部版本"), new("OSArchitecture", "系统位数"), new("InstallDate", "安装时间", "date"), new("LastBootUpTime", "本次启动", "date")]),
        new("处理器", "Win32_Processor", "Name", [new("Manufacturer", "厂商"), new("SocketDesignation", "插槽"), new("NumberOfCores", "物理核心"), new("NumberOfLogicalProcessors", "线程数"), new("MaxClockSpeed", "系统报告的最高频率", "mhz"), new("L2CacheSize", "二级缓存", "kb"), new("L3CacheSize", "三级缓存", "kb"), new("VirtualizationFirmwareEnabled", "固件虚拟化", "bool"), new("ProcessorId", "处理器标识", "", true)]),
        new("主板与 BIOS", "Win32_BaseBoard", "Product", [new("Manufacturer", "厂商"), new("Product", "型号"), new("Version", "版本"), new("SerialNumber", "序列号", "", true)]),
        new("主板与 BIOS", "Win32_BIOS", "Name", [new("Manufacturer", "厂商"), new("SMBIOSBIOSVersion", "BIOS 版本"), new("ReleaseDate", "发布日期", "date"), new("SMBIOSMajorVersion", "SMBIOS 主版本"), new("SMBIOSMinorVersion", "SMBIOS 次版本"), new("SerialNumber", "序列号", "", true)]),
        new("内存", "Win32_PhysicalMemory", "DeviceLocator", [new("Manufacturer", "厂商"), new("PartNumber", "内存型号"), new("Capacity", "容量", "bytes"), new("SMBIOSMemoryType", "规格", "memory"), new("Speed", "标称传输速率", "mts"), new("ConfiguredClockSpeed", "配置传输速率", "mts"), new("DataWidth", "数据位宽", "bits"), new("BankLabel", "通道 / 插槽标识"), new("ConfiguredVoltage", "配置电压", "mv"), new("SerialNumber", "序列号", "", true)]),
        new("内存", "Win32_PhysicalMemoryArray", "Caption", [new("MemoryDevices", "固件报告的插槽数"), new("MaxCapacityEx", "固件报告的最大容量", "kb")]),
        new("显卡", "Win32_VideoController", "Name", [new("AdapterCompatibility", "厂商"), new("VideoProcessor", "图形处理器"), new("DriverVersion", "驱动版本"), new("DriverDate", "驱动日期", "date"), new("CurrentHorizontalResolution", "当前水平分辨率", "px"), new("CurrentVerticalResolution", "当前垂直分辨率", "px"), new("CurrentRefreshRate", "当前刷新率", "hz"), new("PNPDeviceID", "设备标识", "", true)]),
        new("存储", "Win32_DiskDrive", "Model", [new("Size", "容量", "bytes"), new("FirmwareRevision", "固件"), new("InterfaceType", "系统接口类型"), new("MediaType", "介质描述"), new("BytesPerSector", "扇区字节数"), new("Partitions", "分区数"), new("SerialNumber", "序列号", "", true)]),
        new("存储", "MSFT_PhysicalDisk", "FriendlyName", [new("MediaType", "介质类型", "media"), new("BusType", "总线", "bus"), new("HealthStatus", "系统健康状态", "health"), new("Size", "容量", "bytes"), new("SpindleSpeed", "磁盘转速", "rpm"), new("FirmwareVersion", "固件"), new("SerialNumber", "序列号", "", true)], @"root\Microsoft\Windows\Storage"),
        new("存储", "MSFT_StorageReliabilityCounter", "DeviceId", [new("Temperature", "温度", "c"), new("TemperatureMax", "最高温度记录", "c"), new("Wear", "已消耗寿命（设备报告）", "percent"), new("PowerOnHours", "累计通电小时"), new("ReadErrorsUncorrected", "无法纠正的读取错误"), new("WriteErrorsUncorrected", "无法纠正的写入错误")], @"root\Microsoft\Windows\Storage"),
        new("显示器", "WmiMonitorID", "UserFriendlyName", [new("ManufacturerName", "厂商标识", "chars"), new("ProductCodeID", "产品代码", "chars"), new("UserFriendlyName", "型号", "chars"), new("WeekOfManufacture", "生产周"), new("YearOfManufacture", "生产年份"), new("SerialNumberID", "序列号", "chars", true)], @"root\wmi"),
        new("网络与声音", "Win32_NetworkAdapter", "Name", [new("Manufacturer", "厂商"), new("NetConnectionID", "连接名称"), new("Speed", "当前链路速率", "bps"), new("MACAddress", "物理地址", "", true)]),
        new("网络与声音", "Win32_SoundDevice", "Name", [new("Manufacturer", "厂商"), new("Status", "设备状态")]),
        new("电池", "Win32_Battery", "Name", [new("EstimatedChargeRemaining", "剩余电量", "percent"), new("DesignVoltage", "设计电压", "mv"), new("DesignCapacity", "设计容量", "mwh"), new("FullChargeCapacity", "满充容量", "mwh")]),
        new("电池", "BatteryStaticData", "InstanceName", [new("ManufactureName", "厂商"), new("DeviceName", "型号"), new("DesignedCapacity", "设计容量", "mwh"), new("SerialNumber", "序列号", "", true)], @"root\wmi"),
        new("电池", "BatteryFullChargedCapacity", "InstanceName", [new("FullChargedCapacity", "当前满充容量", "mwh")], @"root\wmi"),
        new("电池", "BatteryCycleCount", "InstanceName", [new("CycleCount", "循环次数")], @"root\wmi"),
        new("设备与驱动", "Win32_PnPSignedDriver", "DeviceName", [new("Manufacturer", "厂商"), new("DeviceClass", "设备类别"), new("DriverVersion", "驱动版本"), new("DriverDate", "驱动日期", "date"), new("DriverProviderName", "驱动提供商"), new("IsSigned", "数字签名", "bool")])
    ];

    public static HardwareSnapshot Read(CancellationToken token, IProgress<string>? progress = null)
    {
        var sections = new List<HardwareSection>();
        var batteryDesign = new Dictionary<string, double>();
        var batteryFull = new Dictionary<string, double>();
        bool? batteryPresent = null;
        foreach (var query in Queries)
        {
            token.ThrowIfCancellationRequested(); progress?.Report($"正在读取{query.Category}…");
            // Supplementary battery providers may be absent on desktops; that is not a read failure.
            if (query.Class.StartsWith("Battery", StringComparison.Ordinal) && batteryPresent == false) continue;
            try
            {
                var keys = query.Fields.Select(f => f.Key).Append(query.Title).ToList();
                if (query.Class == "Win32_DiskDrive") keys.Add("Index");
                if (query.Class == "MSFT_PhysicalDisk") keys.Add("DeviceId");
                var filter = query.Class == "Win32_NetworkAdapter" ? " WHERE PhysicalAdapter = TRUE" : "";
                using var search = new ManagementObjectSearcher(query.Scope, $"SELECT {string.Join(",", keys.Distinct())} FROM {query.Class}{filter}");
                search.Options.Timeout = TimeSpan.FromSeconds(5); search.Options.ReturnImmediately = true;
                using var values = search.Get();
                var foundBattery = false;
                foreach (ManagementObject value in values)
                {
                    using (value)
                    {
                        token.ThrowIfCancellationRequested();
                        if (query.Class == "Win32_Battery") foundBattery = true;
                        var title = Format(value[query.Title], value[query.Title] is ushort[] ? "chars" : "");
                        if (query.Class == "Win32_ComputerSystem") title = "电脑配置";
                        if (query.Class == "Win32_DiskDrive") title += " · 磁盘 " + value["Index"];
                        if (query.Class == "MSFT_PhysicalDisk") title += " · 磁盘 " + value["DeviceId"];
                        if (query.Class == "MSFT_StorageReliabilityCounter") title = "磁盘 " + title;
                        if (query.Class.StartsWith("Battery", StringComparison.Ordinal)) title = query.Class switch { "BatteryStaticData" => "电池设计信息", "BatteryFullChargedCapacity" => "电池满充容量", _ => "电池循环记录" };
                        if (query.Class == "BatteryStaticData" && value["DesignedCapacity"] is uint design && design > 0) batteryDesign[Convert.ToString(value["InstanceName"]) ?? ""] = design;
                        if (query.Class == "BatteryFullChargedCapacity" && value["FullChargedCapacity"] is uint full && full > 0) batteryFull[Convert.ToString(value["InstanceName"]) ?? ""] = full;
                        sections.Add(new(query.Category, title == Unknown ? query.Category : title, query.Fields.Select(f => new HardwareField(f.Label, Format(value[f.Key], f.Format), f.Sensitive)).ToArray()));
                    }
                }
                if (query.Class == "Win32_Battery") batteryPresent = foundBattery;
            }
            catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.IO.IOException)
            {
                sections.Add(new(query.Category, query.Category + " · 补充信息", [new("读取状态", query.Class == "Win32_Battery"
                    ? "电池设备查询失败，无法确认是否存在电池。"
                    : "此数据源读取失败，设备或驱动可能不支持。")]));
            }
        }
        token.ThrowIfCancellationRequested();
        foreach (var (id, design) in batteryDesign)
            if (batteryFull.TryGetValue(id, out var full)) sections.Add(new("电池", "电池容量状况", [new("满充容量 / 设计容量", $"{full / design:P1}"), new("估算容量损耗", $"{Math.Max(0, 1 - full / design):P1}"), new("说明", "根据设备报告容量计算，会受电池校准与充电策略影响。")]));
        try { sections.AddRange(GraphicsInventory.Read()); }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or DllNotFoundException) { sections.Add(new("显卡", "显存信息", [new("读取状态", Unknown)])); }
        foreach (var category in Queries.Select(q => q.Category).Distinct())
            if (!sections.Any(s => s.Category == category)) sections.Add(new(category, category, [new("读取状态", category == "电池" ? "未检测到电池。" : "未检测到设备或设备未提供信息。 ")]));
        return new(DateTimeOffset.Now, sections);
    }

    public static string Format(object? value, string format = "")
    {
        if (value is null) return Unknown;
        if (format == "chars" && value is ushort[] chars) return Clean(new string(chars.Where(c => c != 0).Select(c => (char)c).ToArray()));
        if (format == "bool") return value is bool b ? b ? "已启用" : "未启用" : Unknown;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (format == "date") { try { return ManagementDateTimeConverter.ToDateTime(text).ToString("yyyy-MM-dd HH:mm"); } catch (ArgumentException) { return Unknown; } }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return Clean(text);
        if (format == "bytes") return n > 0 ? $"{n / 1073741824d:N2} GB" : Unknown;
        if (format == "kb") return n > 0 ? n >= 1048576 ? $"{n / 1048576:N2} GB" : $"{n / 1024:N2} MB" : Unknown;
        if (format == "memory") return (int)n switch { 0 or 2 => Unknown, 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "规格代码 " + text };
        if (format == "media") return (int)n switch { 3 => "机械硬盘", 4 => "固态硬盘", 5 => "存储级内存", _ => Unknown };
        if (format == "bus") return (int)n switch { 7 => "USB", 8 => "RAID", 11 => "SATA", 17 => "NVMe", 1 => "SCSI", 3 => "ATA", 15 => "虚拟磁盘", _ => "类型代码 " + text };
        if (format == "health") return (int)n switch { 0 => "正常", 1 => "警告", 2 => "异常", _ => Unknown };
        if (format is "mhz" or "mts" or "mv" or "mwh" or "px" or "hz" or "bps" && n <= 0) return Unknown;
        return format switch { "mhz" => $"{n:N0} MHz", "mts" => $"{n:N0} MT/s", "kb" => $"{n:N0} KB", "bits" => $"{n:N0} 位", "mv" => $"{n / 1000:N2} V", "mwh" => $"{n:N0} mWh", "px" => $"{n:N0} px", "hz" => $"{n:N0} Hz", "bps" => $"{n / 1000000:N0} Mbps", "percent" => $"{n:N0}%", "rpm" => $"{n:N0} RPM", "c" => $"{n:N0} °C", _ => Clean(text) };
    }
    private static string Clean(string value) => string.IsNullOrWhiteSpace(value) || value.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) || value.Equals("Default string", StringComparison.OrdinalIgnoreCase) ? Unknown : value;
}
