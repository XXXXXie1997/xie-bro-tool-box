using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using XieBox.Contracts;

namespace XieBox.StartupManager;
public sealed class StartupModel(IPlatformContext context, StartupEngine engine) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ActivityChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private bool _busy, _opened;
    private int _filter;
    private string _status = "";
    private IReadOnlyList<StartupEntry> _entries = [];
    private CancellationTokenSource? _cancellation;
    public bool IsBusy { get => _busy; private set { _busy = value; Notify(); Notify(nameof(IsIdle)); ActivityChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsIdle => !IsBusy;
    public bool Opened { get => _opened; private set { _opened = value; Notify(); } }
    public int Filter { get => _filter; set { _filter = value; Notify(); Notify(nameof(Entries)); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string Count => $"共 {_entries.Count} 项 · 已启用 {_entries.Count(e => e.Enabled)} 项";
    public IEnumerable<StartupEntry> Entries => _entries.Where(e => Filter == 0 || (Filter == 1 ? e.Enabled : !e.Enabled));
    public void Cancel() => _cancellation?.Cancel();
    public void UseSnapshot(StartupScan snapshot)
    {
        if (IsBusy) return;
        _entries = snapshot.Entries; Filter = 0; Opened = true; Notify(nameof(Entries)); Notify(nameof(Count));
        Status = "显示本次体检读取的启动项，可刷新获取最新状态。";
    }
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        if (!(await context.CheckCapabilityAsync("scan")).Allowed) { Status = "平台未允许此查看能力。"; return; }
        IsBusy = true; Opened = true; Status = "正在读取启动项…"; _cancellation = new();
        try { await ReadAsync(_cancellation.Token); }
        catch (OperationCanceledException) { Status = "已停止读取，可重新刷新。"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { Status = "暂时无法读取启动项，请检查访问权限。"; }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; }
    }
    private async Task ReadAsync(CancellationToken token)
    {
        var result = await Task.Run(() => engine.Scan(token), token);
        _entries = result.Entries; Notify(nameof(Entries)); Notify(nameof(Count));
        Status = _entries.Count == 0 ? "未发现普通应用启动项。" : "启用或禁用应用自动启动，不影响手动运行。";
        if (result.Unavailable > 0) Status += $" 有 {result.Unavailable} 个位置无法读取。";
    }
    public async Task ToggleAsync(StartupEntry entry)
    {
        if (IsBusy || !entry.CanChange) return;
        if (!(await context.CheckCapabilityAsync("manage")).Allowed) { Status = "平台未允许此管理能力。"; return; }
        IsBusy = true;
        try
        {
            Status = entry.RequiresAdministrator ? "正在请求管理员权限…" : "正在修改启动项…";
            await Task.Run(() => { if (entry.RequiresAdministrator) StartupElevation.Toggle(entry); else engine.Toggle(entry); });
            await ReadAsync(default);
            Status = entry.Enabled ? $"已关闭 {entry.Name} 的自动启动，可在已禁用列表恢复。" : $"已恢复 {entry.Name} 的自动启动。";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException or Win32Exception)
        { Status = "操作未完成：" + e.Message + " 请刷新列表检查状态。"; }
        finally { IsBusy = false; }
    }
}
