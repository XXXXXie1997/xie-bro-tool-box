using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XieBox.GarbageCleaner;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed record CleanerPaths(string Local, string Roaming)
{
    public static CleanerPaths Current => new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
}
public sealed record RootRule(string Path, TimeSpan MinimumAge, string[]? Extensions = null);
public sealed record CategoryRule(string Id, string Name, string Description, string Consequence, bool Recommended, RootRule[] Roots, string[] Processes, string Note = "");
public sealed record FileStamp(uint Volume, ulong Index, long Creation, long Modified, long Length, uint Links);
public sealed class Candidate(string path, RootRule root, FileStamp stamp, string reason) : Bindable
{
    private bool _selected;
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public RootRule Root { get; } = root;
    public FileStamp Stamp { get; } = stamp;
    public string Reason { get; } = reason;
    public long Bytes => Stamp.Length;
    public string Size => Format.Bytes(Bytes);
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Notify(); } }
}
public sealed class CategoryResult : Bindable
{
    private bool _selected;
    private readonly string _status;
    private long _selectedCount;
    private long _selectedBytes;
    private bool _updatingSelection;
    public CategoryResult(CategoryRule rule, IReadOnlyList<Candidate> files, string status, int skipped = 0, long recycleBytes = 0, long recycleCount = 0, IReadOnlyList<CategoryResult>? children = null, IReadOnlyList<string>? scanNotes = null)
    {
        Rule = rule; Files = files; _status = status + (skipped > 0 ? $" · 跳过 {skipped} 项" : "");
        ScanNotes = scanNotes ?? [];
        RecycleBytes = recycleBytes; RecycleCount = recycleCount;
        Children = children?.OrderByDescending(c => c.CanSelect).ToArray() ?? [];
        if (IsGroup)
        {
            foreach (var child in Children) child.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectionState)) NotifySelection();
            };
        }
        else
        {
            _selectedCount = files.Count(f => f.Selected);
            _selectedBytes = files.Where(f => f.Selected).Sum(f => f.Bytes);
            foreach (var file in files) file.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(Candidate.Selected)) return;
                _selectedCount += file.Selected ? 1 : -1;
                _selectedBytes += file.Selected ? file.Bytes : -file.Bytes;
                NotifySelection();
            };
        }
    }
    private void NotifySelection()
    {
        if (_updatingSelection) return;
        Notify(nameof(Selected)); Notify(nameof(SelectedCount)); Notify(nameof(SelectedBytes)); Notify(nameof(SelectionState));
    }
    public static CategoryResult GroupBrowsers(IReadOnlyList<CategoryResult> browsers) => new(
        new("browser-cache", "浏览器缓存", "Chrome、Edge、360、QQ、Firefox、夸克和搜狗的已识别网页缓存。",
            "网页首次打开可能变慢并重新下载资源；不清理密码、Cookie、书签、历史记录和网盘下载。请先退出需要清理的浏览器及其后台进程。", false, [], []),
        browsers.SelectMany(c => c.Files).ToArray(),
        $"发现 {browsers.Count(c => c.CanSelect)} 种浏览器缓存 · 展开明细可分别选择并查看跳过原因", children: browsers);
    public CategoryRule Rule { get; }
    public IReadOnlyList<string> ScanNotes { get; }
    public IReadOnlyList<CategoryResult> Children { get; }
    public bool IsGroup => Children.Count != 0;
    public string Id => Rule.Id;
    public string Name => Rule.Name;
    public string Description => Rule.Description;
    public string Consequence => "清理后：" + Rule.Consequence;
    public string Status => _status;
    public IReadOnlyList<Candidate> Files { get; }
    public long RecycleCount { get; }
    public long RecycleBytes { get; }
    public bool IsRecycleBin => Id == "recycle-bin";
    public bool CanSelect => IsRecycleBin ? RecycleCount > 0 : Files.Count > 0;
    public bool Selected
    {
        get => IsGroup ? Children.Any(c => c.Selected) : IsRecycleBin ? _selected : _selectedCount > 0;
        set
        {
            _updatingSelection = true;
            try
            {
                if (IsGroup) { foreach (var child in Children) child.Selected = value; }
                else if (IsRecycleBin) _selected = value && CanSelect;
                else foreach (var file in Files) file.Selected = value;
            }
            finally { _updatingSelection = false; }
            NotifySelection();
        }
    }
    public bool? SelectionState
    {
        get => !Selected ? false : IsGroup ? Children.Where(c => c.CanSelect).All(c => c.SelectionState == true) ? true : null
            : IsRecycleBin || _selectedCount == Files.Count ? true : null;
        set => Selected = value == true;
    }
    public long SelectedBytes => IsGroup ? Children.Sum(c => c.SelectedBytes) : IsRecycleBin ? Selected ? RecycleBytes : 0 : _selectedBytes;
    public long SelectedCount => IsGroup ? Children.Sum(c => c.SelectedCount) : IsRecycleBin ? Selected ? RecycleCount : 0 : _selectedCount;
    public long Bytes => IsRecycleBin ? RecycleBytes : Files.Sum(f => f.Bytes);
    public string Size => Format.Bytes(Bytes);
    public string CountLabel => $"{(IsRecycleBin ? RecycleCount : Files.Count):N0} 个文件";
    public string Recommendation => IsRecycleBin ? "请仔细确认" : Rule.Recommended ? "可按需选择" : "默认保留";
}
public sealed record ScanSnapshot(IReadOnlyList<CategoryResult> Categories, DateTime CreatedUtc, bool Complete, string Summary);
public sealed record PlannedFile(string CategoryId, string Path, RootRule Root, FileStamp Stamp, string[] Processes);
public sealed class CleanupPlan
{
    private int _started;
    private int _recycleStarted;
    internal CleanupPlan(PlannedFile[] files, bool recycle, long recycleBytes, long recycleCount, string[] excluded)
    { Files = Array.AsReadOnly(files); Recycle = recycle; RecycleBytes = recycleBytes; RecycleCount = recycleCount; Excluded = Array.AsReadOnly(excluded); }
    public IReadOnlyList<PlannedFile> Files { get; }
    public IReadOnlyList<string> Excluded { get; }
    public bool Recycle { get; }
    public long RecycleBytes { get; }
    public long RecycleCount { get; }
    public long Bytes => Files.Sum(f => f.Stamp.Length) + (Recycle ? RecycleBytes : 0);
    internal void Start() { if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("此清理计划已执行，请重新扫描。"); }
    internal bool StartRecycle() => Volatile.Read(ref _started) == 1 && Interlocked.Exchange(ref _recycleStarted, 1) == 0;
}
public sealed record FileOutcome(string CategoryId, string Path, long Bytes, bool Deleted, string Reason);
public sealed record CleanupResult(IReadOnlyList<FileOutcome> Outcomes, bool Cancelled, string RecycleStatus, long? SpaceChange, int NotAttempted = 0)
{
    public long DeletedBytes => Outcomes.Where(o => o.Deleted).Sum(o => o.Bytes);
    public int DeletedCount => Outcomes.Count(o => o.Deleted);
    public int SkippedCount => Outcomes.Count(o => !o.Deleted);
}
public static class Format
{
    public static string Bytes(long size)
    {
        var number = (double)Math.Abs(size); var unit = 0; string[] units = ["B", "KB", "MB", "GB", "TB"];
        while (number >= 1024 && unit < units.Length - 1) { number /= 1024; unit++; }
        return (size < 0 ? "−" : "") + $"{number:0.##} {units[unit]}";
    }
}
