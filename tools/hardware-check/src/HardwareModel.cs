using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using XieBox.Contracts;
using XieBox.Platform.UI;

namespace XieBox.HardwareCheck;

public sealed class SensorRow(SensorReading reading) : INotifyPropertyChanged
{
    public string Id { get; } = reading.Id;
    public (string Id, string Device, string Name, string Kind, string Category) Key { get; } = SensorIdentity.Key(reading);
    public string Device { get; } = reading.Device;
    public string Name { get; } = HardwareLabels.Sensor(reading.Name, reading.Kind);
    public string Kind { get; } = reading.Kind;
    public string Category { get; } = reading.Category;
    public string Value { get; private set; } = reading.Value;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(string value) { if (Value == value) return; Value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); }
}

public sealed class HardwareModel(IPlatformContext context, IHardwareSource source, TimeSpan? interval = null) : ToolModel, IDisposable, IReportable
{
    public static IReadOnlyList<string> Categories { get; } = ["电脑概览", "处理器", "主板与 BIOS", "内存", "显卡", "存储", "显示器", "网络与声音", "电池", "设备与驱动"];
    private string _category = "电脑概览";
    private bool _active, _disposed, _started, _refresh = true;
    private CancellationTokenSource? _session;
    private Task? _loop;
    public HardwareSnapshot? Snapshot { get; private set; }
    public DateTimeOffset? SensorTime { get; private set; }
    public ObservableCollection<SensorRow> Sensors { get; } = [];
    public IReadOnlyDictionary<string, string> SensorSummaries { get; private set; } = Categories.ToDictionary(c => c, _ => "");
    public bool ShowSensitive { get; set; }
    public string SelectedCategory { get => _category; set { if (_category == value) return; _category = value; Notify(); Notify(nameof(VisibleSections)); Notify(nameof(IsSensorPage)); } }
    public bool IsSensorPage => SelectedCategory == "硬件状态";
    public IEnumerable<HardwareSection> VisibleSections => Snapshot?.Sections.Where(s => SelectedCategory == "电脑概览" || s.Category == SelectedCategory) ?? [];
    public string SamplingStatus { get; private set; } = "窗口处于前台时采集，离开后暂停。";
    public string SensorNote { get; private set; } = "正在准备读取温度、转速、频率和功耗。";
    public string CaptureLabel => Snapshot is null ? "尚未读取配置" : $"配置读取于 {Snapshot.CapturedAt:HH:mm:ss} · 状态更新于 {(SensorTime is { } time ? time.ToString("HH:mm:ss") : "—")}";
    public bool IsReading { get; private set; }
    public int DeviceLayoutVersion { get; private set; }
    private void Reading(bool value) { IsReading = value; Notify(nameof(IsReading)); Notify(nameof(CanRefresh)); }
    public bool CanRefresh => _started && !_disposed && _active && !IsReading;
    public bool CanExport => Snapshot is not null;
    public string ReportTitle => "硬件信息";
    public Task Completion => _loop ?? Task.CompletedTask;
    public void Start()
    {
        if (_disposed || _started) return;
        _started = true; ResultsLayout = true; Notify(nameof(CanRefresh));
        SamplingStatus = _active ? "正在读取硬件信息" : "已暂停 · 返回活动窗口后继续"; Notify(nameof(SamplingStatus));
        StartLoop();
    }

    public void SetActive(bool active)
    {
        if (_disposed || _active == active) return;
        _active = active; Notify(nameof(CanRefresh));
        if (!_started) return;
        SamplingStatus = active ? "正在采集 · 每 3 秒更新" : "已暂停 · 返回活动窗口后继续"; Notify(nameof(SamplingStatus));
        if (!active) _session?.Cancel();
        else StartLoop();
    }
    public void Refresh()
    {
        if (!CanRefresh) return;
        _refresh = true; _session?.Cancel();
        if (_loop is null || _loop.IsCompleted) StartLoop();
    }
    private void StartLoop()
    {
        if (_disposed || !_started || !_active || _loop is { IsCompleted: false }) return;
        _session = new(); _loop = LoopAsync(_session);
    }
    private async Task LoopAsync(CancellationTokenSource session)
    {
        // Yield before work so _loop is assigned even when capability checking completes synchronously.
        await Task.Yield();
        var token = session.Token;
        var retry = true;
        try
        {
            if (!(await context.CheckCapabilityAsync("scan", token)).Allowed) { Message("平台未允许硬件读取。"); retry = false; return; }
            if (_refresh || Snapshot is null)
            {
                Reading(true); Message("正在读取硬件配置…");
                var progress = new Progress<string>(s => { if (!token.IsCancellationRequested) Message(s); });
                var snapshot = await Task.Run(() => source.ReadInventory(token, progress), token);
                token.ThrowIfCancellationRequested(); Snapshot = snapshot; _refresh = false;
                Notify(nameof(VisibleSections)); Notify(nameof(CaptureLabel)); Notify(nameof(CanExport)); Message("正在读取硬件状态…");
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var readings = await Task.Run(() => source.ReadSensors(token), token);
                token.ThrowIfCancellationRequested();
                ApplyReadings(readings); if (IsReading) { Reading(false); Message("读取完成"); } SensorTime = DateTimeOffset.Now; Notify(nameof(CaptureLabel));
                SensorNote = readings.Any(r => r.Value != HardwareInventory.Unknown)
                    ? "显示设备实际提供的数据，未提供的温度或转速不会显示。"
                    : "暂未读取到传感器数据，设备或驱动可能不支持。配置信息仍可查看。";
                Notify(nameof(SensorNote));
                await Task.Delay(interval ?? TimeSpan.FromSeconds(3), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            retry = false;
            SensorNote = "硬件状态读取未完成，可点击重新读取重试。"; Notify(nameof(SensorNote));
            Message("读取未完成：" + e.Message);
        }
        finally
        {
            Reading(false);
            try { await Task.Run(source.CloseSensors); }
            catch (Exception) { /* The session is already stopped; no more reads are scheduled. */ }
            session.Dispose(); _session = null; _loop = null;
            if (_disposed) source.Dispose();
            else if (_active && retry) StartLoop();
            else if (_active) { SamplingStatus = "采集已停止 · 可重新读取"; Notify(nameof(SamplingStatus)); }
        }
    }
    private void ApplyReadings(IReadOnlyList<SensorReading> readings)
    {
        readings = SensorIdentity.Unique(readings);
        var ids = readings.Select(SensorIdentity.Key).ToHashSet();
        var layoutChanged = !ids.SetEquals(Sensors.Select(s => s.Key));
        for (var i = Sensors.Count - 1; i >= 0; i--) if (!ids.Contains(Sensors[i].Key)) Sensors.RemoveAt(i);
        var existing = Sensors.ToDictionary(s => s.Key);
        foreach (var reading in readings)
            if (existing.TryGetValue(SensorIdentity.Key(reading), out var row)) row.Update(reading.Value);
            else Sensors.Add(new(reading));
        var summaries = readings.GroupBy(r => r.Category).ToDictionary(g => g.Key, g =>
            string.Join("   ·   ", g.Where(r => r.Kind is "温度" or "风扇转速" && r.Value != HardwareInventory.Unknown)
                .GroupBy(r => r.Device).Select(device => device.First()).Take(3).Select(r => $"{r.Device}：{r.Value}")));
        SensorSummaries = Categories.ToDictionary(c => c, c => summaries.GetValueOrDefault(c, ""));
        Notify(nameof(SensorSummaries));
        if (layoutChanged) { DeviceLayoutVersion++; Notify(nameof(DeviceLayoutVersion)); }
    }
    public string BuildReport()
    {
        var text = new StringBuilder($"蟹小箱 · 硬件信息\n{CaptureLabel}\n{SamplingStatus}\n\n");
        foreach (var section in Snapshot?.Sections ?? [])
        {
            text.AppendLine($"【{section.Category} · {section.Title}】");
            foreach (var field in section.Fields) text.AppendLine($"{field.Name}：{(field.Sensitive && !ShowSensitive ? "已隐藏" : field.Value)}");
            text.AppendLine();
        }
        text.AppendLine("【硬件状态快照】");
        foreach (var sensor in Sensors) text.AppendLine($"{sensor.Device} / {sensor.Name} / {sensor.Kind}：{sensor.Value}");
        text.AppendLine("\n未读取到信息不代表硬件故障。温度和转速为记录时的快照。数据来源：Windows、设备固件、LibreHardwareMonitor。");
        return text.ToString();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _active = false; _session?.Cancel();
        if (_loop is null) source.Dispose();
    }
}
