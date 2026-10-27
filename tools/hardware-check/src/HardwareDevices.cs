using System.Text.RegularExpressions;
namespace XieBox.HardwareCheck;

public sealed record HardwareDevice(string Key, string Title, IReadOnlyList<HardwareField> Fields, IReadOnlyList<SensorRow> Sensors);
public static class HardwareDevices
{
    public static string Key(string category, string title)
    {
        if (category == "存储")
        {
            var match = Regex.Match(title, @"(?:磁盘|设备)\s+(\d+)\s*$");
            if (match.Success) return "disk:" + match.Groups[1].Value;
        }
        return title.Replace(" · 显存", "").Trim().ToUpperInvariant();
    }
    public static IReadOnlyList<HardwareDevice> Group(string category, IEnumerable<HardwareSection> sections, IEnumerable<SensorRow> sensors)
    {
        var devices = sections.GroupBy(s => Key(category, s.Title)).Select(g => new HardwareDevice(g.Key, g.First().Title.Replace(" · 显存", ""), g.SelectMany(s => s.Fields).Distinct().ToArray(), Array.Empty<SensorRow>())).ToList();
        foreach (var group in sensors.Where(s => s.Category == category).GroupBy(s => Key(category, s.Device)))
        {
            var index = devices.FindIndex(d => d.Key == group.Key);
            // A single CPU is unambiguous even when WMI and firmware use different marketing names.
            if (index < 0 && category == "处理器" && devices.Count == 1 && sensors.Where(s => s.Category == category).Select(s => s.Device).Distinct().Count() == 1) index = 0;
            if (index >= 0) devices[index] = devices[index] with { Sensors = group.ToArray() };
            else devices.Add(new(group.Key, group.First().Device, Array.Empty<HardwareField>(), group.ToArray()));
        }
        return devices;
    }
}
