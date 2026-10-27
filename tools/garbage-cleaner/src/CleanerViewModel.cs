using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using XieBox.Contracts;

namespace XieBox.GarbageCleaner;

public sealed class CleanerViewModel(CleanerEngine engine, IPlatformContext context, string stateDirectory) : Bindable
{
    private bool _busy;
    private ScanSnapshot? _snapshot;
    private CancellationTokenSource? _cancellation;
    private string _status = "扫描后可选择要清理的内容。所有操作都在本机完成。";
    private bool _canClean;
    private bool _hasScanned;
    private bool _hasAttemptedScan;
    private bool _showCompletion;
    public ObservableCollection<CategoryResult> Categories { get; } = [];
    public ObservableCollection<string> Exclusions { get; } = [];
    public event EventHandler? ActivityChanged;
    public bool IsBusy { get => _busy; private set { _busy = value; Notify(); Notify(nameof(IsIdle)); ActivityChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsIdle => !IsBusy;
    public bool CanClean => IsIdle && _canClean && (SelectedCount > 0 || Categories.Any(c => c.Selected && c.IsRecycleBin));
    public bool HasResults => Categories.Count != 0;
    public bool HasScanned => _hasScanned;
    public bool HasScanLayout => _hasAttemptedScan && !_showCompletion;
    public string StartScanLabel => IsBusy ? "正在扫描…" : _hasAttemptedScan ? "再次扫描" : "开始扫描";
    public string WelcomeHeading => !_showCompletion ? "垃圾清理" : LastResult?.Cancelled == true ? "已停止清理" : "清理完成";
    public string WelcomeDescription => !_showCompletion || LastResult is not { } result
        ? "扫描临时文件、故障日志和浏览器缓存，按需选择清理。"
        : $"已清理 {result.DeletedCount:N0} 个文件\n" + (result.SpaceChange is > 0
            ? $"释放 {Format.Bytes(result.SpaceChange.Value)} 可用空间"
            : result.SpaceChange is null ? $"已清理文件大小 {Format.Bytes(result.DeletedBytes)}，可用空间暂无法读取"
            : $"已清理文件大小 {Format.Bytes(result.DeletedBytes)}，磁盘可用空间暂未增加");
    public string InitialStatus => _showCompletion ? "" : _hasAttemptedScan ? Status : "";
    public bool CanSelectResults => IsIdle && _canClean && HasResults;
    public string SelectedCategoriesLabel => $"已选择（{Categories.Count(c => c.Selected)}）";
    public string Status { get => _status; private set { _status = value; Notify(); Notify(nameof(InitialStatus)); } }
    public long SelectedBytes => Categories.Sum(c => c.SelectedBytes);
    public long SelectedCount => Categories.Sum(c => c.SelectedCount);
    public string SelectionLabel => $"已选 {SelectedCount:N0} 个文件 · {Format.Bytes(SelectedBytes)}";
    public string ScanLabel => _snapshot is null ? "当前用户 · 本地临时文件、诊断资料与浏览器缓存" : $"发现 {Format.Bytes(Categories.Sum(c => c.Bytes))} · 扫描时间 {_snapshot.CreatedUtc.ToLocalTime():HH:mm}";
    public string DiskLabel => engine.AvailableSpace() is long space ? "所在磁盘可用 " + Format.Bytes(space) : "磁盘空间暂不可用";
    public string ExclusionLabel => $"排除目录（{Exclusions.Count}）";
    public CleanupResult? LastResult { get; private set; }
    public bool HasCleanupResult => LastResult is not null;
    public void LoadSettings()
    {
        try
        {
            var file = Path.Combine(stateDirectory, "exclusions.json");
            if (File.Exists(file)) foreach (var path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? []) Exclusions.Add(SafeFile.Normalize(path));
        }
        catch (Exception e) when (SafeFile.Expected(e) || e is JsonException) { Status = "排除目录设置无法读取，请重新添加需要保留的目录。"; }
    }
    public bool AddExclusion(string path)
    {
        if (IsBusy) return false;
        try
        {
            path = SafeFile.Normalize(path);
            if (Exclusions.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
            Exclusions.Add(path); SaveSettings(); Invalidate(); Notify(nameof(ExclusionLabel));
            Status = "已排除此目录及其子目录，请重新扫描。"; return true;
        }
        catch (Exception e) when (SafeFile.Expected(e)) { Status = "无法保存排除目录，请检查位置和本地存储权限。"; return false; }
    }
    public void RemoveExclusion(string path)
    {
        if (IsBusy) return;
        Exclusions.Remove(path);
        try { SaveSettings(); Invalidate(); Notify(nameof(ExclusionLabel)); Status = "排除设置已更新，请重新扫描。"; }
        catch (Exception e) when (SafeFile.Expected(e)) { Status = "排除设置未能保存，请检查本地存储权限。"; }
    }
    private void SaveSettings()
    {
        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, "exclusions.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Exclusions)); File.Move(path + ".tmp", path, true);
    }
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        if (!(await context.CheckCapabilityAsync("scan")).Allowed) { Status = "平台未允许此扫描能力。"; return; }
        foreach (var category in Categories) Detach(category);
        Categories.Clear(); Invalidate(); LastResult = null; _showCompletion = false; _snapshot = null; _hasAttemptedScan = true;
        IsBusy = true; _cancellation = new(); Status = "正在准备扫描…"; NotifyState();
        try
        {
            var exclusions = Exclusions.ToArray();
            var progress = new Progress<string>(s => Status = s);
            _snapshot = await Task.Run(() => engine.Scan(exclusions, progress, _cancellation.Token));
            PresentSnapshot(_snapshot);
        }
        catch (OperationCanceledException) { Status = "扫描已取消，没有删除任何文件。"; }
        catch (Exception e) when (SafeFile.Expected(e)) { Status = "部分位置无法访问，扫描未完成，请检查权限或添加排除目录后重试。"; }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; NotifyState(); }
    }
    public void UseSnapshot(ScanSnapshot snapshot)
    {
        if (IsBusy) return;
        foreach (var category in Categories) Detach(category);
        Categories.Clear(); LastResult = null; _showCompletion = false; _hasAttemptedScan = true;
        PresentSnapshot(snapshot); NotifyState();
    }
    private void PresentSnapshot(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;
            var browsers = _snapshot.Categories.Where(c => c.Rule.Processes.Length > 0).ToArray();
            var browserGroup = CategoryResult.GroupBrowsers(browsers);
            var visibleCategories = new List<CategoryResult>();
            foreach (var category in _snapshot.Categories)
            {
                if (browsers.Contains(category))
                {
                    if (category != browsers[0]) continue;
                    visibleCategories.Add(browserGroup);
                }
                else visibleCategories.Add(category);
            }
            // Keep the original order within each group, including zero-byte files with candidates.
            foreach (var category in visibleCategories.OrderByDescending(c => c.CanSelect))
            { Categories.Add(category); Attach(category); }
            // Start unchecked: the user decides after reading the consequences.
            _canClean = _snapshot.Complete; _hasScanned = true; Status = _snapshot.Summary;
    }
    public void SelectAll()
    {
        if (IsBusy || !_canClean) return;
        foreach (var category in Categories) category.Selected = true;
    }
    public void SelectNone() { if (IsBusy) return; foreach (var category in Categories) category.Selected = false; }
    public CleanupPlan? CreatePlan()
    {
        if (!CanClean || _snapshot is null) return null;
        try { return engine.BuildPlan(_snapshot, Exclusions); }
        catch (InvalidOperationException e) { Status = e.Message; Invalidate(); return null; }
    }
    public async Task CleanAsync(CleanupPlan plan, IntPtr owner)
    {
        if (!CanClean || IsBusy) return;
        if (!(await context.CheckCapabilityAsync("cleanup")).Allowed) { Status = "平台未允许此清理能力。"; return; }
        IsBusy = true; _cancellation = new(); NotifyState();
        try
        {
            var before = engine.AvailableSpace();
            var progress = new Progress<string>(s => Status = s);
            var result = await Task.Run(() => engine.ExecuteFiles(plan, progress, _cancellation.Token));
            var recycleStatus = engine.EmptyRecycleBin(plan, owner, _cancellation.Token);
            var after = engine.AvailableSpace();
            result = result with { RecycleStatus = recycleStatus, SpaceChange = before is not null && after is not null ? after - before : null };
            LastResult = result;
            Status = (result.Cancelled ? "已取消后续清理。" : "本次清理已结束。") + $"已永久删除 {result.DeletedCount:N0} 个文件（文件大小 {Format.Bytes(result.DeletedBytes)}），跳过 {result.SkippedCount:N0} 个，未执行 {result.NotAttempted:N0} 个。"
                + (result.SpaceChange is long delta ? $" 所在磁盘可用空间变化 {Format.Bytes(delta)}（会受其他程序影响）。" : "")
                + (plan.Recycle ? " " + recycleStatus : "") + " 请重新扫描获取最新结果。";
            SaveSummary(result, recycleStatus);
        }
        catch (Exception e) when (SafeFile.Expected(e) || e is InvalidOperationException) { Status = "清理未全部完成，请重新扫描检查；已执行的删除不会撤销。"; }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; Invalidate(); _showCompletion = LastResult is not null; NotifyState(); }
    }
    private void SaveSummary(CleanupResult result, string recycleStatus)
    {
        try
        {
            Directory.CreateDirectory(stateDirectory);
            File.WriteAllText(Path.Combine(stateDirectory, "last-result.json"), JsonSerializer.Serialize(new
            { Time = DateTimeOffset.Now, result.DeletedCount, result.DeletedBytes, result.SkippedCount, result.NotAttempted, result.Cancelled, RecycleStatus = recycleStatus,
                Categories = result.Outcomes.GroupBy(o => o.CategoryId).Select(g => new { Id = g.Key, Deleted = g.Count(x => x.Deleted), Skipped = g.Count(x => !x.Deleted) }) }));
        }
        catch (Exception e) when (SafeFile.Expected(e)) { Status += " 本地结果摘要未能保存。"; }
    }
    public void Cancel() => _cancellation?.Cancel();
    private void Attach(CategoryResult category) { category.PropertyChanged += SelectionChanged; }
    private void Detach(CategoryResult category) { category.PropertyChanged -= SelectionChanged; }
    private void SelectionChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName != nameof(CategoryResult.SelectionState)) return; Notify(nameof(SelectionLabel)); Notify(nameof(SelectedCategoriesLabel)); Notify(nameof(CanClean)); }
    private void Invalidate() { _canClean = false; Notify(nameof(CanClean)); Notify(nameof(CanSelectResults)); }
    private void NotifyState() { Notify(nameof(WelcomeHeading)); Notify(nameof(WelcomeDescription)); Notify(nameof(CanClean)); Notify(nameof(HasResults)); Notify(nameof(HasScanned)); Notify(nameof(HasScanLayout)); Notify(nameof(StartScanLabel)); Notify(nameof(InitialStatus)); Notify(nameof(CanSelectResults)); Notify(nameof(ScanLabel)); Notify(nameof(SelectionLabel)); Notify(nameof(SelectedCategoriesLabel)); Notify(nameof(DiskLabel)); Notify(nameof(HasCleanupResult)); }
}
