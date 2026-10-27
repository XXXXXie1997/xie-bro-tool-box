using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using XieBox.Contracts;

namespace XieBox.FileScanner;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class FormatChoice(FormatSet set) : Bindable
{
    public string Name => set.Name;
    public string[] Extensions => set.Extensions;
    public string Hint => string.Join("、", Extensions.Select(e => e.TrimStart('.').ToUpperInvariant()));
    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; Notify(); } }
}
public sealed class ScannerModel(IPlatformContext context) : Bindable
{
    private string _directory = "", _threshold = "100", _status = "";
    private bool _largeFiles = true, _busy, _resultsLayout;
    private CancellationTokenSource? _cancellation;
    public event EventHandler? ActivityChanged;
    public string Directory { get => _directory; set { _directory = value; if (!IsBusy) Status = ""; Notify(); Notify(nameof(DuplicateHint)); } }
    public string DuplicateHint
    {
        get
        {
            var wholeDrive = false;
            try { var full = Path.GetFullPath(Directory.Trim()); wholeDrive = string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? ""), StringComparison.OrdinalIgnoreCase); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or System.Security.SecurityException) { }
            return (wholeDrive ? "扫描整个磁盘可能较慢，建议选择下载、照片或备份文件夹。" : "建议选择具体文件夹以缩短扫描时间。") + "扫描会核对文件内容，文件越多、越大，耗时越长。可随时停止。";
        }
    }
    public string Threshold { get => _threshold; set { _threshold = value; Notify(); } }
    private string _customExtensions = "";
    public string CustomExtensions { get => _customExtensions; set { _customExtensions = value; Notify(); } }
    private bool _duplicates;
    public bool Duplicates { get => _duplicates; set { _duplicates = value; if (value) _largeFiles = false; Notify(); Notify(nameof(LargeFiles)); Notify(nameof(FormatMode)); } }
    public bool LargeFiles { get => _largeFiles; set { _largeFiles = value; _duplicates = false; Notify(nameof(Duplicates)); Status = ""; Notify(); Notify(nameof(FormatMode)); } }
    public bool FormatMode => !LargeFiles && !Duplicates;
    public IReadOnlyList<FormatChoice> Formats { get; } = ScanEngine.Formats.Select(f => new FormatChoice(f)).ToArray();
    public bool IsBusy { get => _busy; private set { _busy = value; Notify(); Notify(nameof(IsIdle)); ActivityChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsIdle => !IsBusy;
    public bool ResultsLayout { get => _resultsLayout; private set { _resultsLayout = value; Notify(); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string Summary { get; private set; } = "";
    public string ResultCount => $"找到 {Files.Count:N0} 个文件 · {ScanEngine.FormatSize(Files.Sum(f => f.Bytes))}";
    public IReadOnlyList<FileMatch> Files { get; private set; } = [];
    public void EditConditions() { if (IsBusy) return; ResultsLayout = false; Status = ""; }
    public void Cancel() => _cancellation?.Cancel();
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Directory)) { Status = "请先选择要扫描的目录。"; return; }
        if (Duplicates) { await ScanDuplicatesAsync(); return; }
        var threshold = 100;
        if (LargeFiles && (!int.TryParse(Threshold, out threshold) || threshold < 50)) { Status = "请输入不低于 50 的整数阈值（MB）。"; return; }
        string[] custom = [];
        if (FormatMode)
        {
            try { custom = ScanEngine.ParseExtensions(CustomExtensions); }
            catch (ArgumentException) { Status = "格式填写不正确，请填写 jpg、.png 或 *.pdf，多个格式用空格分隔。"; return; }
        }
        var selectedFormats = Formats.Where(f => f.Selected).ToArray();
        var categoryExtensions = selectedFormats.SelectMany(f => f.Extensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additionalExtensions = custom.Where(extension => !categoryExtensions.Contains(extension)).ToArray();
        var extensions = categoryExtensions.Concat(additionalExtensions).ToArray();
        if (FormatMode && extensions.Length == 0) { Status = "请选择文件类型，或填写要查找的格式。"; return; }
        if (!(await context.CheckCapabilityAsync("scan")).Allowed) { Status = "平台未允许此扫描能力。"; return; }
        var options = new ScanOptions(Directory.Trim(), LargeFiles, threshold, extensions);
        Summary = (LargeFiles ? $"大于 {threshold:N0} MB" : string.Join("、", selectedFormats.Select(f => f.Name).Concat(additionalExtensions))) + " · 包含子目录";
        Notify(nameof(Summary));
        Files = []; Notify(nameof(Files)); Notify(nameof(ResultCount));
        _cancellation = new(); IsBusy = true; ResultsLayout = true; Status = "正在准备扫描…";
        try
        {
            var progress = new Progress<ScanProgress>(p => { if (IsBusy) Status = $"已检查 {p.Visited:N0} 项 · 找到 {p.Found:N0} 个文件"; });
            var result = await Task.Run(() => new ScanEngine().Scan(options, progress, _cancellation.Token));
            Files = result.Files; Notify(nameof(Files)); Notify(nameof(ResultCount));
            Status = result.Cancelled ? "扫描已停止，显示已找到的文件。" : result.Limited ? "已达到本次扫描上限，请缩小目录范围。" : Files.Count == 0 ? "扫描完成，没有找到符合条件的文件。" : "扫描完成。";
            if (result.Skipped > 0) Status += $" 已跳过 {result.Skipped:N0} 个无法访问、链接或离线项目。";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Status = "无法完成扫描，请检查目录与访问权限。"; }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; }
    }
    private async Task ScanDuplicatesAsync()
    {
        IsBusy = true; _cancellation = new();
        try
        {
            if (!(await context.CheckCapabilityAsync("scan", _cancellation.Token)).Allowed) { Status = "平台未允许此扫描能力。"; return; }
            Files = []; Notify(nameof(Files)); Notify(nameof(ResultCount));
            Summary = "重复文件 · 按文件内容比对"; Notify(nameof(Summary));
            ResultsLayout = true; Status = "正在收集文件并比对内容…";
            var token = _cancellation.Token;
            var progress = new Progress<string>(text => { if (IsBusy && !token.IsCancellationRequested) Status = text; });
            var result = await Task.Run(() => DuplicateEngine.Scan(Directory.Trim(), progress, token), token);
            Files = result.Files; Notify(nameof(Files)); Notify(nameof(ResultCount));
            Status = $"找到 {result.Groups} 组相同内容。仅展示，不自动删除；硬链接可能指向同一份数据。";
            if (result.Limited) Status += " 达到 100,000 项上限，请缩小范围。";
            if (result.Skipped > 0) Status += $" 跳过 {result.Skipped} 个链接、离线、变化中或无法访问的项目。";
        }
        catch (OperationCanceledException) { Status = "比对已停止，请重新扫描。"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Status = "无法完成内容比对，请检查目录与权限。"; }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; }
    }}


