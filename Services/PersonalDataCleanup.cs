using System.IO;

namespace XieBox.Services;

public sealed record CleanupGroup(string Name, string[] Files, bool IsRecovery = false)
{
    public long Bytes => Files.Sum(path => new FileInfo(path).Length);
}

// Only named application files are eligible; never recursively delete the data root.
public sealed class PersonalDataCleanup(string root)
{
    private readonly string _root = Path.GetFullPath(root);
    private bool Safe(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
    private string[] Named(params string[] names) => names.Select(n => Path.Combine(_root, n)).Where(p => Safe(p) && File.Exists(p)).ToArray();
    private string[] Logs(string name, string pattern)
    {
        var folder = Path.Combine(_root, name);
        if (!Safe(folder) || !Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly).Where(Safe).ToArray();
    }
    public CleanupGroup[] Preview() =>
    [
        new("设置、首页布局与登录状态", Named("preferences.json", "preferences.json.tmp", "session.dat", "session.dat.tmp", "tools/garbage-cleaner/exclusions.json")),
        new("缓存与日志", Named("catalog-cache.json", "tools/garbage-cleaner/last-result.json").Concat(Logs("logs", "session-*.log")).Concat(Logs("InstallerLogs", "*.log")).ToArray()),
        new("文件整理、重命名撤销记录", Named("tools/file-organizer/last-operation.json", "tools/batch-renamer/last-operation.json"), true)
    ];
    public string[] Clear(IEnumerable<int> selected)
    {
        var groups = Preview();
        var failures = new List<string>();
        foreach (var path in selected.Distinct().SelectMany(i => groups[i].Files))
        {
            try
            {
                if (!Safe(path)) throw new IOException("目录已变为链接，跳过清理。");
                File.Delete(path);
                // Remove only empty parent directories, up to the exact data root.
                for (var dir = Path.GetDirectoryName(path); dir is not null && (dir.Equals(_root, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)); dir = Path.GetDirectoryName(dir))
                {
                    if (!Safe(Path.Combine(dir, ".cleanup-check")) || !Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) break;
                    Directory.Delete(dir);
                    if (dir.Equals(_root, StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failures.Add(path + "：" + e.Message); }
        }
        return failures.ToArray();
    }
}
