using System.ComponentModel;
using System.Runtime.CompilerServices;
using XieBox.Contracts;

namespace XieBox.MemoryRelease;
public sealed class MemoryModel(IPlatformContext context, IMemoryEngine engine) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ActivityChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private string _status = "";
    private bool _readingApplications;
    public IReadOnlyList<MemoryApplication> Applications { get; private set; } = [];
    public string ApplicationsStatus { get; private set; } = "正在读取应用内存…";
    public async Task RefreshApplicationsAsync()
    {
        if (_readingApplications || IsBusy) return;
        _readingApplications = true;
        try
        {
            Applications = await Task.Run(() => engine.ReadApplications(CancellationToken.None));
            ApplicationsStatus = $"当前会话 · {Applications.Count} 个应用 · 按内存占用排序";
            Notify(nameof(Applications));
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
        { ApplicationsStatus = "暂时无法读取应用内存，下次刷新时重试。"; }
        finally { _readingApplications = false; Notify(nameof(ApplicationsStatus)); }
    }
    public bool IsBusy { get => _busy; private set { _busy = value; Notify(); Notify(nameof(CanRelease)); ActivityChanged?.Invoke(this, EventArgs.Empty); } }
    public bool CanRelease => !IsBusy && Snapshot.Total > 0;
    public MemorySnapshot Snapshot { get; private set; } = new(0, 0);
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string Title { get; private set; } = "内存释放";
    public string Description { get; private set; } = "查看各应用的内存占用，释放部分暂时不用的内存。";
    public string ButtonLabel { get; private set; } = "释放内存";
    public void Refresh()
    {
        if (IsBusy) return;
        try { Snapshot = engine.Read(); Notify(nameof(Snapshot)); Notify(nameof(CanRelease)); }
        catch (Win32Exception) { Snapshot = new(0, 0); Notify(nameof(Snapshot)); Notify(nameof(CanRelease)); Status = "暂时无法读取系统内存，请稍后重试。"; }
    }
    public void Cancel() => _cancellation?.Cancel();
    public async Task ReleaseAsync()
    {
        if (!CanRelease) return;
        if (!(await context.CheckCapabilityAsync("release")).Allowed) { Status = "平台未允许此释放能力。"; return; }
        IsBusy = true; Status = "正在释放后台应用暂时不用的内存…"; _cancellation = new();
        try
        {
            var result = await Task.Run(() => engine.Release(_cancellation.Token));
            Snapshot = result.After; Notify(nameof(Snapshot));
            Title = "内存整理完成"; Description = result.Summary;
            Status = $"可用内存：{result.Before.AvailableText} → {result.After.AvailableText} · 已整理 {result.Trimmed} 个后台进程。\n内存随应用运行动态变化，此差值不完全由整理操作产生。";
            ButtonLabel = "再次释放";
        }
        catch (OperationCanceledException) { Title = "内存整理已停止"; Description = "可以稍后再次尝试。"; Status = ""; }
        catch (Exception e) when (e is Win32Exception or UnauthorizedAccessException or InvalidOperationException)
        { Title = "暂未完成整理"; Description = "请稍后重试。"; Status = "未结束任何应用进程。"; }
        finally
        {
            _cancellation.Dispose(); _cancellation = null; IsBusy = false;
            Notify(nameof(Title)); Notify(nameof(Description)); Notify(nameof(ButtonLabel));
            await RefreshApplicationsAsync();
        }
    }
}
