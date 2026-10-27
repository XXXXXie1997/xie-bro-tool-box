using System.Diagnostics;
using System.IO;

namespace XieBox.GarbageCleaner;

public sealed class CleanerEngine(CleanerPaths paths, IRecycleBin recycleBin, Func<string[], bool>? isRunning = null, Func<CategoryRule[]>? discover = null, bool scanRunning = false)
{
    private readonly Func<string[], bool> _isRunning = isRunning ?? Running;
    private ScanSnapshot? _lastScan;
    public static bool Running(string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                var any = processes.Length != 0;
                foreach (var process in processes) process.Dispose();
                if (any) return true;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
        }
        return false;
    }
    public ScanSnapshot Scan(IEnumerable<string> exclusions, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        _lastScan = null;
        var excludes = exclusions.Select(SafeFile.Normalize).ToArray();
        var results = new List<CategoryResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var visited = 0; var complete = true;
        foreach (var rule in (discover?.Invoke() ?? RuleCatalog.Discover(paths)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("正在扫描 · " + rule.Name);
            if (rule.Id == "recycle-bin")
            {
                try { var bin = recycleBin.Query(); results.Add(new(rule, [], "由 Windows 查询；请先查看再决定是否清空", recycleBytes: bin.Bytes, recycleCount: bin.Count)); }
                catch (Exception e) when (SafeFile.Expected(e)) { results.Add(new(rule, [], "无法查询回收站，本次不清理")); }
                continue;
            }
            var running = _isRunning(rule.Processes);
            if (running && !scanRunning) { results.Add(new(rule, [], "浏览器仍在运行，已跳过；退出后台进程后重新扫描")); continue; }
            var files = new List<Candidate>(); var skipped = 0; var foundRoot = false;
            var reasons = new Dictionary<string, (int Count, string Example)>();
            void Skip(string path, Exception? error = null)
            {
                skipped++;
                var code = error is System.ComponentModel.Win32Exception win ? win.NativeErrorCode : error?.HResult & 65535;
                var reason = error is UnauthorizedAccessException || code == 5 ? "权限不足" : code is 32 or 33 ? "文件被占用" : code is 2 or 3 ? "扫描时文件已消失" : error is null ? "链接或云端占位文件，按保护规则保留" : error.Message;
                var previous = reasons.GetValueOrDefault(reason);
                reasons[reason] = (previous.Count + 1, previous.Example ?? path);
            }
            foreach (var root in rule.Roots)
            {
                if (!Directory.Exists(root.Path)) continue;
                foundRoot = true;
                var stack = new Stack<string>(); stack.Push(root.Path);
                while (stack.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var directory = stack.Pop();
                    if (SafeFile.Excluded(directory, excludes)) continue;
                    try
                    {
                        using var lease = SafeFile.PinDirectory(directory);
                        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (++visited > 250_000 || seen.Count >= 50_000) { complete = false; stack.Clear(); break; }
                            if (SafeFile.Excluded(entry, excludes)) continue;
                            try
                            {
                                var attributes = File.GetAttributes(entry);
                                if (((uint)attributes & (0x400u | 0x1000u | 0x40000u | 0x400000u)) != 0) { Skip(entry); continue; }
                                if (attributes.HasFlag(FileAttributes.Directory)) { stack.Push(entry); continue; }
                                var normalized = SafeFile.Normalize(entry);
                                var stamp = SafeFile.Inspect(normalized);
                                if (!SafeFile.Eligible(normalized, root, stamp, now) || !seen.Add(normalized)) continue;
                                files.Add(new(normalized, root, stamp, rule.Description));
                            }
                            catch (Exception e) when (SafeFile.Expected(e)) { Skip(entry, e); }
                        }
                    }
                    catch (Exception e) when (SafeFile.Expected(e)) { Skip(directory, e); }
                }
                if (!complete) break;
            }
            var status = files.Count > 0 ? "扫描完成" : foundRoot ? "未发现符合规则的文件" : "未发现可识别的目录";
            if (rule.Processes.Length > 0) status += " · " + rule.Note;
            if (running) status += " · 浏览器正在运行，仅统计可读取的缓存；清理前请退出浏览器及后台进程";
            if (!complete) status = "达到扫描上限，请排除不需要的目录后重新扫描";
            results.Add(new(rule, files.AsReadOnly(), status, skipped, scanNotes: reasons.Select(r => $"{r.Key}：{r.Value.Count} 项（例如：{r.Value.Example}）").ToArray()));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return _lastScan = new(results.AsReadOnly(), now, complete, complete ? "扫描完成。选择要清理的类目，或展开明细逐项保留。" : "扫描未完整完成，本次结果不能执行清理。请添加排除目录后重新扫描。");
    }
    public CleanupPlan BuildPlan(ScanSnapshot snapshot, IEnumerable<string> exclusions)
    {
        if (!ReferenceEquals(snapshot, _lastScan) || !snapshot.Complete || DateTime.UtcNow - snapshot.CreatedUtc > TimeSpan.FromMinutes(30))
            throw new InvalidOperationException("扫描结果已失效，请重新扫描。");
        var excluded = exclusions.Select(SafeFile.Normalize).ToArray();
        var files = snapshot.Categories.Where(c => c.Selected && !c.IsRecycleBin)
            .SelectMany(c => c.Files.Where(f => f.Selected && !SafeFile.Excluded(f.Path, excluded))
                .Select(f => new PlannedFile(c.Id, f.Path, f.Root, f.Stamp, c.Rule.Processes.ToArray()))).ToArray();
        var recycle = snapshot.Categories.Single(c => c.IsRecycleBin);
        return new(files, recycle.Selected, recycle.RecycleBytes, recycle.RecycleCount, excluded);
    }
    // File deletion never enumerates afresh: only the frozen plan can supply candidates.
    public CleanupResult ExecuteFiles(CleanupPlan plan, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        plan.Start();
        _lastScan = null;
        var before = AvailableSpace();
        var outcomes = new List<FileOutcome>();
        foreach (var file in plan.Files)
        {
            if (cancellationToken.IsCancellationRequested) break;
            progress?.Report($"正在清理 · {outcomes.Count + 1:N0} / {plan.Files.Count:N0}");
            try
            {
                if (_isRunning(file.Processes)) throw new IOException("浏览器正在运行，已保留缓存。");
                cancellationToken.ThrowIfCancellationRequested();
                SafeFile.Delete(file, plan.Excluded, DateTime.UtcNow);
                outcomes.Add(new(file.CategoryId, file.Path, file.Stamp.Length, true, "已永久删除"));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) when (SafeFile.Expected(e)) { outcomes.Add(new(file.CategoryId, file.Path, 0, false, e is IOException ? e.Message : "文件占用、权限不足或状态变化，已保留。")); }
        }
        var after = AvailableSpace();
        return new(outcomes.AsReadOnly(), cancellationToken.IsCancellationRequested, "未选择清空回收站。", before is not null && after is not null ? after - before : null, plan.Files.Count - outcomes.Count);
    }
    public string EmptyRecycleBin(CleanupPlan plan, IntPtr owner, CancellationToken cancellationToken)
    {
        if (!plan.Recycle || cancellationToken.IsCancellationRequested) return "未执行回收站清空。";
        if (!plan.StartRecycle()) return "此计划的回收站操作无效或已处理，请重新扫描。";
        try
        {
            var current = recycleBin.Query();
            if (current.Bytes != plan.RecycleBytes || current.Count != plan.RecycleCount) return "回收站内容自扫描后已变化，已跳过；请重新扫描并确认。";
            return recycleBin.EmptyWithSystemConfirmation(owner);
        }
        catch (Exception e) when (SafeFile.Expected(e)) { return "回收站处理失败，请在 Windows 中检查权限和占用。"; }
    }
    public long? AvailableSpace()
    {
        try { return new DriveInfo(Path.GetPathRoot(paths.Local)!).AvailableFreeSpace; }
        catch (Exception e) when (SafeFile.Expected(e)) { return null; }
    }
}
