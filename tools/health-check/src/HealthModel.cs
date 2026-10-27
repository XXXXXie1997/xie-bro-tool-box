using System.IO;
using System.Text.Json;
using XieBox.Contracts;
using XieBox.Platform.UI;
using XieBox.GarbageCleaner;
using XieBox.StartupManager;
using XieBox.MemoryRelease;
using XieBox.NetworkDiagnostics;

namespace XieBox.HealthCheck;

public enum HealthState { Suggested, Normal, Incomplete, Issue, Information, Pending, Absent, Unavailable, Skipped }
public sealed record HealthResult(string Name, string ModuleId, string Description, string Action, HealthState State, object? Data = null, string NextStep = "");
public sealed record HealthProbe(string Name, string ModuleId, string Action, Func<CancellationToken, Task<HealthResult>> Check);
public sealed record CleanerSession(CleanerEngine Engine, ScanSnapshot Snapshot);
public sealed record NetworkSession(IReadOnlyList<NetworkCheck> Entries, DateTimeOffset Time);

public sealed class HealthModel(IPlatformContext context, IReadOnlyList<HealthProbe> probes, Func<CancellationToken, IReadOnlyDictionary<string, Presence>>? readDevices = null) : ToolModel, IReportable
{
    public IReadOnlyList<HealthResult> Results { get; private set; } = [];
    public DateTimeOffset? CheckedAt { get; private set; }
    public string Heading { get; private set; } = "";
    public double Progress { get; private set; }
    public string Summary => $"发现问题 {Results.Concat(ManualResults).Count(r => r.State == HealthState.Issue)} 项 · 建议关注 {Results.Count(r => r.State == HealthState.Suggested)} 项 · 自动检查未完成 {Results.Count(r => r.State is HealthState.Incomplete or HealthState.Pending)} 项 · 功能验证 {ManualResults.Count(r => r.State is HealthState.Normal or HealthState.Issue)}/{ManualResults.Count(r => r.State != HealthState.Absent)}";
    public IReadOnlyList<HealthResult> ManualResults { get; private set; } = GuidedChecks.Items.Select(i => new HealthResult(i.Title, "", "尚未进行功能验证", "", HealthState.Pending)).ToArray();
    private IReadOnlyDictionary<string, Presence> presence = new Dictionary<string, Presence>();
    public bool CanVerify(string kind) => presence.TryGetValue(kind, out var device) && device.Available == true;
    private async Task DetectDevices(CancellationToken token)
    {
        Message("正在检测已连接设备…");
        try { presence = await Task.Run(() => (readDevices ?? DevicePresence.Read)(token), token); }
        catch (OperationCanceledException) { presence = new Dictionary<string, Presence>(); }
        ManualResults = GuidedChecks.Items.Select(item =>
        {
            var device = presence.GetValueOrDefault(item.Kind) ?? new Presence(null, "设备检测未完成，请重新检测。");
            return new HealthResult(item.Title, "", device.Description, "", device.Available switch
            { true => HealthState.Pending, false => HealthState.Absent, null => HealthState.Unavailable });
        }).ToArray();
        Notify(nameof(ManualResults)); Notify(nameof(Summary));
    }
    public Task RefreshDevicesAsync() => Run(async token =>
    {
        await DetectDevices(token);
        Message("设备检测已更新，请按当前连接的设备进行功能验证。");
    });
    public void RecordManual(string title, bool? passed, string evidence)
        => RecordManualState(title, passed is null ? HealthState.Skipped : passed.Value ? HealthState.Normal : HealthState.Issue, evidence);
    public void RecordManualState(string title, HealthState state, string evidence, string nextStep = "")
    {
        if (state is not (HealthState.Normal or HealthState.Issue or HealthState.Skipped or HealthState.Incomplete)) throw new ArgumentOutOfRangeException(nameof(state));
        if (IsBusy || CheckedAt is null || !GuidedChecks.Items.Any(i => i.Title == title && CanVerify(i.Kind))) return;
        ManualResults = ManualResults.Select(r => r.Name == title ? r with { State = state, Description = $"{evidence}\n验证时间：{DateTimeOffset.Now:HH:mm:ss}", NextStep = nextStep } : r).ToArray();
        Notify(nameof(ManualResults)); Notify(nameof(Summary));
    }
    public bool CanExport => IsIdle && CheckedAt is not null;
    public string ReportTitle => "电脑体检";
    public Task CheckAsync() => Run(async token =>
    {
        if (!(await context.CheckCapabilityAsync("scan", token)).Allowed) { Message("暂时无法开始体检。"); return; }
        ResultsLayout = true; CheckedAt = DateTimeOffset.Now;
        Progress = 0; Notify(nameof(Progress));
        ManualResults = GuidedChecks.Items.Select(i => new HealthResult(i.Title, "", "尚未进行功能验证", "", HealthState.Pending)).ToArray(); Notify(nameof(ManualResults));
        Results = probes.Select(p => new HealthResult(p.Name, p.ModuleId, "等待检查", p.Action, HealthState.Pending)).ToArray();
        Heading = "正在检查"; Notify(nameof(Heading)); Notify(nameof(Results)); Notify(nameof(CanExport));
        presence = new Dictionary<string, Presence>();
        await DetectDevices(token);
        for (int i = 0; i < probes.Count; i++)
        {
            if (token.IsCancellationRequested) break;
            var probe = probes[i]; Message($"正在检查 {i + 1}/{probes.Count} · {probe.Name}");
            HealthResult result;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(20));
            try { result = await probe.Check(budget.Token); }
            catch (OperationCanceledException) { result = new(probe.Name, probe.ModuleId, token.IsCancellationRequested ? "检查已停止" : "本项耗时较长，可进入应用继续检查", probe.Action, HealthState.Incomplete); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException or InvalidOperationException or JsonException or System.Management.ManagementException or System.Runtime.InteropServices.COMException)
            { result = new(probe.Name, probe.ModuleId, "暂时无法完成检查，可进入应用重试", probe.Action, HealthState.Incomplete); }
            var updated = Results.ToArray(); updated[i] = result; Results = updated; Notify(nameof(Results));
            Progress = (i + 1d) / probes.Count * 100; Notify(nameof(Progress)); Notify(nameof(Summary));
        }
        Results = Results.Select(r => r.State == HealthState.Pending ? r with { Description = "本次未检查", State = HealthState.Incomplete } : r).ToArray();
        Heading = token.IsCancellationRequested ? "体检已停止" : Results.Any(r => r.State == HealthState.Incomplete) ? "体检结束，部分项目未完成" : "体检完成";
        Message($"检查时间 {CheckedAt:HH:mm} · 请继续进行功能验证；未发现异常不代表所有硬件均无故障。");
        Notify(nameof(Summary));
        Notify(nameof(Results)); Notify(nameof(Heading)); Notify(nameof(CanExport));
    });
    public string BuildReport()
    {
        if (!CanExport) throw new InvalidOperationException("请等待体检结束。");
        return ReportBuilder.Build(ReportTitle, CheckedAt!.Value, "自动检查与用户功能验证；不包含压力测试、完整内存测试和全盘坏块检测。未检查、无法读取及未连接的设备不视为正常。", Heading,
            Results.Concat(ManualResults).Select(r => $"{r.Name} · {StateName(r.State)}\n{r.Description}" + (r.NextStep.Length > 0 ? "\n下一步：" + r.NextStep : "") + (r.Data is NetworkSession network ? "\n网络技术记录：\n" + string.Join("\n", network.Entries.Select(e => $"{e.Name}：{e.Result}\n{e.Detail}")) : "")));
    }
    public static string StateName(HealthState state) => state switch { HealthState.Suggested => "建议关注", HealthState.Normal => "本项通过", HealthState.Issue => "发现问题", HealthState.Information => "已读取", HealthState.Pending => "待验证", HealthState.Absent => "未检测到", HealthState.Unavailable => "检测未完成 / 不可用", HealthState.Skipped => "已跳过", _ => "未完成" };
}

public static class HealthProbes
{
    public static IReadOnlyList<HealthProbe> Create(string cleanerState) => [
        .. DeviceProbes.Create(),
        new("磁盘空间", "disk-analyzer", "查看空间占用", token => Task.Run(() =>
        {
            var disks = new List<(string Path, long Free, long Total)>(); int unavailable = 0;
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            {
                token.ThrowIfCancellationRequested();
                try { if (drive.IsReady) disks.Add((drive.Name, drive.AvailableFreeSpace, drive.TotalSize)); else unavailable++; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { unavailable++; }
            }
            var low = disks.Where(d => d.Total > 0 && d.Free / (double)d.Total < .1).ToArray();
            var description = string.Join("；", disks.Select(d => $"{d.Path} 可用 {Format.Bytes(d.Free)}"));
            if (unavailable > 0) description += $"；{unavailable} 个磁盘暂时无法读取";
            return new HealthResult("磁盘空间", "disk-analyzer", description.Length == 0 ? "未能读取本地磁盘" : (low.Length > 0 ? "部分磁盘剩余空间不足 10%。" : "") + description, "查看空间占用", low.Length > 0 ? HealthState.Suggested : unavailable > 0 || disks.Count == 0 ? HealthState.Incomplete : HealthState.Normal, (low.FirstOrDefault().Path ?? disks.FirstOrDefault().Path));
        }, token)),
        new("网络连接", "network-diagnostics", "查看排查结果", async token =>
        {
            var entries = await Task.Run(() => NetworkEngine.Diagnose("https://www.baidu.com/", null, token), token);
            var conclusion = NetworkEngine.ExplainNetwork(entries, true);
            return new HealthResult("网络连接", "network-diagnostics", conclusion.Title, "查看排查结果", entries.Any(r => (r.Name == "HTTPS 连接" || r.Name == "对照网站") && NetworkEngine.WebsiteSucceeded(r)) ? HealthState.Normal : HealthState.Suggested, new NetworkSession(entries, DateTimeOffset.Now));
        }),
    ];
}
