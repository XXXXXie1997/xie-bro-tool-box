using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using XieBox.Contracts;

namespace XieBox.DiskAnalyzer;

public sealed class DiskModel(IPlatformContext context) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ActivityChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private string _directory = "", _status = "";
    private bool _busy, _results;
    private CancellationTokenSource? _cancellation;
    private DiskNode? _current;
    public string Directory { get => _directory; set { _directory = value; if (!IsBusy) Status = ""; Notify(); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public bool IsBusy { get => _busy; private set { _busy = value; Notify(); Notify(nameof(IsIdle)); Notify(nameof(CanGoUp)); ActivityChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsIdle => !IsBusy;
    public bool ResultsLayout { get => _results; private set { _results = value; Notify(); } }
    public IReadOnlyList<DiskNode> Entries => _current?.Children ?? (IReadOnlyList<DiskNode>)Array.Empty<DiskNode>();
    public string CurrentPath => _current?.Path ?? Directory;
    public string Summary => _current is null ? "正在分析目录…" : $"{_current.Size} · {_current.Files:N0} 个文件";
    public string MapHint => (_current?.IsComplete == false ? "部分统计：图块和占比仅代表已读取的数据，未知大小的项目请在列表查看。\n" : "") + "面积越大，占用越多 · 点击文件夹进入，悬浮查看详情 · 小项目合并为“其他”，零字节项目请在列表查看";
    public bool CanGoUp => IsIdle && _current?.Parent is not null;
    public DiskNode? CurrentNode => _current;
    public void GoRoot() { if (!IsIdle) return; var node = _current; while (node?.Parent is not null) node = node.Parent; SetCurrent(node); }
    public void EnterMap(DiskNode node)
    {
        if (!IsIdle || !node.IsFolder || node == _current) return;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (ReferenceEquals(parent, _current)) { SetCurrent(node); return; }
    }
    public void Enter(DiskNode node) { if (IsIdle && node.IsFolder && Entries.Contains(node)) SetCurrent(node); }
    public void GoUp() { if (CanGoUp) SetCurrent(_current!.Parent); }
    private void SetCurrent(DiskNode? node) { _current = node; Notify(nameof(Entries)); Notify(nameof(CurrentNode)); Notify(nameof(CurrentPath)); Notify(nameof(Summary)); Notify(nameof(MapHint)); Notify(nameof(CanGoUp)); }
    public void Edit() { if (IsIdle) { ResultsLayout = false; Status = ""; } }
    public void Cancel() => _cancellation?.Cancel();
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Directory)) { Status = "请先选择要分析的磁盘或目录。"; return; }
        // Busy includes capability checking, so repeated clicks cannot start concurrent scans.
        IsBusy = true;
        _cancellation = new();
        var token = _cancellation.Token;
        try
        {
            if (!(await context.CheckCapabilityAsync("scan", token)).Allowed) { Status = "平台未允许此扫描能力。"; return; }
            var path = Directory.Trim();
            SetCurrent(null); ResultsLayout = true; Status = "正在准备分析…";
            var progress = new Progress<long>(count => { if (IsBusy && !token.IsCancellationRequested) Status = $"已检查 {count:N0} 项"; });
            var result = await Task.Run(() => new DiskEngine().Scan(path, progress, token));
            SetCurrent(result.Root);
            Status = result.Cancelled ? "已停止，显示部分统计；未扫描目录不代表空目录。" : "分析完成。";
            if (result.Skipped > 0) Status += $" 跳过 {result.Skipped:N0} 个无法访问、链接或离线项目，统计可能不完整。";
        }
        catch (OperationCanceledException) { Status = "分析已停止。"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Status = "无法分析此目录，请检查路径与访问权限。"; ResultsLayout = false; }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; }
    }
}
