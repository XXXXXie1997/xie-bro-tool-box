using System.ComponentModel;
using XieBox.Platform.UI;
using XieBox.GarbageCleaner;
using XieBox.FileScanner;
using XieBox.StartupManager;
using XieBox.MemoryRelease;
using XieBox.DiskAnalyzer;
using XieBox.SoftwareUninstaller;
using XieBox.FileOperations;

namespace XieBox.Services;

// Capture completed operation data, so editing the next input cannot relabel an old result.
public sealed class ModuleReport : IReportable
{
    private readonly object _model;
    private readonly Func<bool> _busy;
    private readonly Func<string> _scope, _status;
    private readonly Func<IEnumerable<string>> _rows;
    private string? _body;
    private string _operationScope = "";
    private string[] _before = [];
    private bool _running;
    public string ReportTitle { get; }
    public bool CanExport => !_busy() && _body is not null;
    public event Action? Changed;
    public ModuleReport(string title, INotifyPropertyChanged model, Func<bool> busy, Func<string> scope, Func<string> status, Func<IEnumerable<string>> rows)
    {
        ReportTitle = title; _model = model; _busy = busy; _scope = scope; _status = status; _rows = rows;
        model.PropertyChanged += (_, e) =>
        {
            if (_busy())
            {
                if (!_running) { _running = true; _operationScope = _scope(); _before = _rows().ToArray(); }
            }
            else if (_running)
            {
                _running = false;
                var lines = _rows().ToArray();
                if (_model is FileTaskModel && lines.Length == 0 && _before.Length > 0)
                    lines = ["以下为操作前的预览清单，不代表每项均已执行成功：", .. _before];
                _body = ReportBuilder.Build(title, DateTimeOffset.Now, _operationScope, _status(), lines);
            }
            else if (_model is MemoryModel memory && e.PropertyName == nameof(MemoryModel.Applications) && memory.Snapshot.Total > 0)
                _body = ReportBuilder.Build(title, DateTimeOffset.Now, _scope(), _status(), _rows());
            Changed?.Invoke();
        };
    }
    public string BuildReport() => CanExport ? _body! : throw new InvalidOperationException("没有可导出的结果，或任务尚未结束。");

    public static ModuleReport? Create(string title, object? model) => model switch
    {
        CleanerViewModel m => new(title, m, () => m.IsBusy, () => "当前用户清理规则；排除目录：" + string.Join("、", m.Exclusions), () => m.ScanLabel + "\n" + m.Status + (m.LastResult is null ? "" : "\n" + m.WelcomeDescription), () => m.Categories.Select(c => $"{c.Name}：{c.Bytes} 字节；{c.Status}；{c.Consequence}")),
        ScannerModel m => new(title, m, () => m.IsBusy, () => "扫描目录：" + m.Directory, () => m.Summary + "\n" + m.ResultCount + "\n" + m.Status, () => m.Files.Select(f => $"{f.Path}\t{f.Bytes} 字节")),
        StartupModel m => new(title, m, () => m.IsBusy, () => "已识别的登录启动项；当前筛选：" + m.Filter, () => m.Count + "\n" + m.Status, () => m.Entries.Select(e => $"{e.Name}\t{e.State}\t{e.Publisher}\t{e.Location}\n{e.Note}")),
        MemoryModel m => new(title, m, () => m.IsBusy, () => "当前会话应用；工作集包含共享内存，合计不等于系统已用内存。", () => $"总内存 {m.Snapshot.TotalText}；可用 {m.Snapshot.AvailableText}\n{m.Status}", () => m.Applications.Select(a => $"{a.Name}\t{a.MemoryText}\t{a.ProcessCount} 个进程")),
        DiskModel m => new(title, m, () => m.IsBusy, () => "分析目录：" + m.Directory, () => m.Summary + "\n" + m.Status + "\n以下为分析结束时所在层级，不包含完整递归树；大小不等于可释放空间。", () => m.Entries.Select(e => $"{e.Path}\t{e.Size}\t{e.Files} 个文件")),
        SoftwareModel m => new(title, m, () => m.IsBusy, () => "桌面软件注册清单；搜索条件：" + m.Search, () => m.Status + "\n大小由软件提供，未知大小不等于没有占用。", () => m.Entries.Select(e => $"{e.Name}\t{e.Version}\t{e.Publisher}\t{e.Size}\t{e.Scope}")),
        FileTaskModel m => new(title, m, () => m.IsBusy, () => "目录：" + m.Directory + "；仅处理当前层。", () => m.Status, () => m.Entries.Select(e => $"{e.Source} → {e.Destination}\t{e.Bytes} 字节")),
        _ => null
    };
}
