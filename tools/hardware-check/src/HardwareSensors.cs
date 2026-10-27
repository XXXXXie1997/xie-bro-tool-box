using LibreHardwareMonitor.Hardware;

namespace XieBox.HardwareCheck;

public sealed record SensorReading(string Id, string Device, string Name, string Kind, string Value, string Category = "处理器");
public static class SensorIdentity
{
    // Some drivers reuse numeric identifiers for different named channels.
    // Include device and channel metadata so only the same reading is deduplicated.
    public static (string Id, string Device, string Name, string Kind, string Category) Key(SensorReading r) => (r.Id, r.Device, r.Name, r.Kind, r.Category);
    public static IReadOnlyList<SensorReading> Unique(IEnumerable<SensorReading> readings) => readings.GroupBy(Key)
        .Select(g => g.LastOrDefault(r => r.Value != HardwareInventory.Unknown) ?? g.Last()).ToArray();
}
public interface IHardwareSource : IDisposable
{
    HardwareSnapshot ReadInventory(CancellationToken token, IProgress<string>? progress);
    IReadOnlyList<SensorReading> ReadSensors(CancellationToken token);
    void CloseSensors();
}

// All calls are serialized by HardwareModel. No sensor controls are written.
public sealed class HardwareSource : IHardwareSource
{
    private Computer? _computer;
    public HardwareSnapshot ReadInventory(CancellationToken token, IProgress<string>? progress) => HardwareInventory.Read(token, progress);
    public IReadOnlyList<SensorReading> ReadSensors(CancellationToken token)
    {
        if (_computer is null)
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = true, IsBatteryEnabled = true, IsNetworkEnabled = false, IsControllerEnabled = false };
            try { _computer.Open(); } catch { CloseSensors(); throw; }
        }
        var result = new List<SensorReading>();
        var visited = new HashSet<IHardware>(ReferenceEqualityComparer.Instance);
        foreach (var hardware in _computer.Hardware) Visit(hardware);
        return SensorIdentity.Unique(result);

        void Visit(IHardware hardware)
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(hardware)) return;
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                var (kind, unit) = sensor.SensorType.ToString() switch
                {
                    "Temperature" => ("温度", "°C"), "Fan" => ("风扇转速", "RPM"), "Voltage" => ("电压", "V"),
                    "Clock" => ("频率", "MHz"), "Load" => ("占用率", "%"), "Power" => ("功耗", "W"),
                    "Data" => ("容量", "GB"), "SmallData" => ("容量", "MB"), "Level" => ("水平", "%"),
                    "Energy" => ("能量", "mWh"), "Throughput" => ("传输速率", "B/s"), "Factor" => ("系数", ""),
                    "Current" => ("电流", "A"), _ => ("其他", "")
                };
                if (kind == "其他") continue;
                var value = sensor.Value;
                var category = hardware.HardwareType.ToString() switch { "Cpu" => "处理器", "GpuNvidia" or "GpuAmd" or "GpuIntel" => "显卡", "Storage" => "存储", "Memory" => "内存", "Battery" => "电池", _ => "主板与 BIOS" };
                var device = category == "存储" ? hardware.Name + " · 磁盘 " + hardware.Identifier.ToString().Split('/').Last() : hardware.Name;
                result.Add(new(sensor.Identifier.ToString(), device, sensor.Name, kind,
                    value.HasValue && float.IsFinite(value.Value) ? $"{value.Value:N1} {unit}".TrimEnd() : HardwareInventory.Unknown, category));
            }
            foreach (var child in hardware.SubHardware) Visit(child);
        }
    }
    public void CloseSensors() { var computer = _computer; _computer = null; computer?.Close(); }
    public void Dispose() => CloseSensors();
}
