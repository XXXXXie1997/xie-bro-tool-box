using System.IO;
using XieBox.HardwareCheck;
using XieBox.Services;

internal static partial class Program
{
    private sealed class FixtureHardware : IHardwareSource
    {
        public int Reads, Inventories, Closes;
        public bool Disposed;
        public HardwareSnapshot ReadInventory(CancellationToken token, IProgress<string>? progress)
        {
            token.ThrowIfCancellationRequested(); Inventories++;
            return new(DateTimeOffset.Now, [new("电脑概览", "测试电脑", [new("型号", "Fixture PC"), new("序列号", "PRIVATE-SERIAL", true)]), new("处理器", "Intel Core i5-13600KF", [new("物理核心", "14"), new("线程数", "20"), new("三级缓存", "24 MB")]), new("内存", "插槽 1", [new("容量", "32 GB")])]);
        }
        public IReadOnlyList<SensorReading> ReadSensors(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Reads++;
            return [new("cpu/temp", "Intel Core i5-13600KF", "处理器封装", "温度", $"{40 + Reads} °C"), new("cpu/temp", "Intel Core i5-13600KF", "处理器封装", "温度", $"{40 + Reads} °C"), new("cpu/temp", "Intel Core i5-13600KF", "核心 1", "温度", "38 °C"), new("fan", "主板", "处理器风扇", "风扇转速", "900 RPM", "主板与 BIOS")];
        }
        public void CloseSensors() => Closes++;
        public void Dispose() => Disposed = true;
    }
    private static void CheckHardware(string preview, AccountService accounts)
    {
        using var context = new ModuleContext(BuiltInModules.Hardware, accounts, () => Task.CompletedTask);
        var source = new FixtureHardware();
        var model = new HardwareModel(context, source, TimeSpan.FromMilliseconds(30));
        AwaitUi(Task.Delay(60)); Check(source.Reads == 0, "hardware does not sample before activation");
        model.SetActive(true); AwaitUi(Task.Delay(80));
        Check(source.Inventories == 0 && source.Reads == 0 && !model.ResultsLayout, "active hardware landing page does not read or request elevation before start");
        model.SetActive(false); model.SetActive(true); AwaitUi(Task.Delay(60));
        Check(source.Inventories == 0, "returning to hardware landing page does not start collection");
        model.Start(); model.Start(); AwaitUi(Task.Delay(180));
        Check(source.Reads > 1 && source.Inventories == 1, "active hardware page refreshes sensors without rescanning inventory");
        Check(model.SensorSummaries["处理器"].Contains("13600KF") && model.Sensors.Single(s => s.Id == "fan").Category == "主板与 BIOS", "hardware summaries preserve model association and separate motherboard sensors");
        Check(model.Sensors.Count(s => s.Id == "cpu/temp") == 2 && model.Sensors.Count(s => s.Name == "处理器封装") == 1, "repeated samples are deduplicated while distinct channels sharing an identifier remain visible");
        var first = model.Sensors[0]; var firstValue = first.Value;
        AwaitUi(Task.Delay(90)); Check(ReferenceEquals(first, model.Sensors[0]) && first.Value != firstValue, "sensor updates retain row identity and selection");
        model.SetActive(false); AwaitUi(model.Completion); var reads = source.Reads; AwaitUi(Task.Delay(90));
        Check(source.Reads == reads && source.Closes > 0, "deactivation cancels polling and closes hardware session");
        Check(!model.BuildReport().Contains("PRIVATE-SERIAL") && model.BuildReport().Contains("已隐藏"), "hardware report hides serial numbers by default");
        model.ShowSensitive = true; Check(model.BuildReport().Contains("PRIVATE-SERIAL"), "serial numbers can be explicitly included");
        model.SetActive(true); AwaitUi(Task.Delay(100)); Check(source.Reads > reads && source.Inventories == 1, "return to active window resumes sensor session");
        model.Refresh(); AwaitUi(Task.Delay(150)); Check(source.Inventories == 2, "manual refresh rereads inventory once");
        model.Dispose(); AwaitUi(model.Completion); reads = source.Reads; model.SetActive(true); AwaitUi(Task.Delay(90));
        Check(source.Disposed && source.Reads == reads, "closed hardware tab cannot restart polling");
        Check(HardwareInventory.Format(null) == HardwareInventory.Unknown && HardwareInventory.Format(0, "bytes") == HardwareInventory.Unknown, "missing hardware capacity is not displayed as zero");
        Check(HardwareInventory.Format(0, "percent") == "0%" && HardwareInventory.Format(32768, "kb").Contains("32"), "zero percentage remains valid and cache units are readable");
        var diskSections = Enumerable.Range(0, 3).SelectMany(i => new[] {
            new HardwareSection("存储", $"SSD · 磁盘 {i}", [new("容量", "1 TB")]),
            new HardwareSection("存储", $"SSD · 磁盘 {i}", [new("总线", "NVMe")]),
            new HardwareSection("存储", $"磁盘 {i}", [new("累计通电小时", "100")]) }).ToArray();
        var diskSensors = Enumerable.Range(0, 3).Select(i => new SensorRow(new($"disk/{i}", $"SSD · 磁盘 {i}", "温度", "温度", $"{40+i} °C", "存储"))).ToArray();
        var repeated = new SensorReading("/gpu-nvidia/0/load/3", "RTX 3070", "GPU Video", "占用率", "12%", "显卡");
        var unique = SensorIdentity.Unique([repeated, repeated with { Value = HardwareInventory.Unknown }, repeated with { Device = "RTX 3070 #2" }, repeated with { Name = "GPU Bus" }]);
        Check(unique.Count == 3 && unique[0].Value == "12%", "duplicate GPU identifiers retain valid readings and do not merge separate devices or channels");
        var devices = HardwareDevices.Group("存储", diskSections, diskSensors);
        Check(devices.Count == 3 && devices.All(d => d.Fields.Count == 3 && d.Sensors.Count == 1), "disk sources merge into three physical devices with matching individual sensors");
        Check(devices[2].Sensors[0].Value == "42 °C", "same-model drives remain distinct by disk number");
        Check(!model.IsReading, "hardware loading indicator stops after disposal");
        Check(HardwareLabels.Sensor("Warning Temperature", "温度") == "Warning Temperature（警告温度阈值）" && HardwareLabels.Sensor("Critical Temperature", "温度") == "Critical Temperature（临界温度阈值）", "storage warning temperatures are labeled as thresholds rather than current temperatures");
        Check(HardwareLabels.Sensor("Data Written", "容量") == "Data Written（累计写入量）" && HardwareLabels.Sensor("Life", "水平") == "Life（剩余寿命）", "storage counters have readable Chinese names");
        Check(HardwareLabels.Sensor("CPU Cores", "功耗") == "CPU Cores（处理器核心总计）" && HardwareLabels.Sensor("CPU Core #1 Thread #2", "占用率").Contains("线程 #2"), "CPU plural and numbered thread labels translate without partial replacements");
        Check(HardwareLabels.Sensor("Vendor AUX7", "电压") == "Vendor AUX7（电压项目，厂商自定义名称）", "unknown vendor sensor names retain original text with Chinese context");
        var page = HardwarePage.Create(context, new FixtureHardware()); var ui = (HardwareModel)page.Model;
        RenderUtility(page, Path.Combine(preview, "hardware-start.png"));
        ui.SetActive(true); ui.Start(); AwaitUi(Task.Delay(120)); ui.SetActive(false); AwaitUi(ui.Completion);
        RenderUtility(page, Path.Combine(preview, "hardware-overview.png"));
        ui.SelectedCategory = "处理器"; RenderUtility(page, Path.Combine(preview, "hardware-sensors.png"));
        ui.Dispose();
        using var companion = new ElevatedHardwareSource(Environment.ProcessPath!, start =>
        {
            // Exercise the same IPC protocol without presenting a UAC prompt in automated tests.
            start.UseShellExecute = false; start.Verb = ""; start.CreateNoWindow = true;
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Insert(0, typeof(Program).Assembly.Location);
            return System.Diagnostics.Process.Start(start);
        });
        var actual = companion.ReadInventory(CancellationToken.None, null);
        Check(actual.Sections.Any(s => s.Category == "处理器"), "isolated hardware companion returns inventory over authenticated pipe");
        var actualSensors = companion.ReadSensors(CancellationToken.None);
        Check(actualSensors.All(s => !string.IsNullOrWhiteSpace(s.Device)), "companion sensors retain their hardware identity");
        companion.CloseSensors();
        Check(companion.ReadInventory(CancellationToken.None, null).Sections.Count > 0, "paused companion stays reusable without a second authorization");
    }
    private static void ProbeHardware()
    {
        using var source = new HardwareSource();
        var snapshot = source.ReadInventory(CancellationToken.None, null);
        File.WriteAllText("artifacts/hardware-probe.txt", string.Join("\n", snapshot.Sections.Select(s => $"[{s.Category}] {s.Title}\n" + string.Join("\n", s.Fields.Where(f => !f.Sensitive).Select(f => f.Name + ": " + f.Value)))));
        Console.WriteLine($"Inventory: {snapshot.Sections.Count} sections");
        var sensors = source.ReadSensors(CancellationToken.None);
        File.WriteAllText("artifacts/hardware-sensors.txt", string.Join("\n", sensors.Select(s => $"{s.Device} / {s.Name} / {s.Kind}: {s.Value}")));
        Console.WriteLine($"Sensors: {sensors.Count}, available: {sensors.Count(s => s.Value != HardwareInventory.Unknown)}");
        source.CloseSensors(); Console.WriteLine("Hardware session closed.");
    }
}
