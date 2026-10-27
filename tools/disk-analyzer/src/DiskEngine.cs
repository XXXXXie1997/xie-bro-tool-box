using System.Diagnostics;
using System.IO;

namespace XieBox.DiskAnalyzer;

public enum DiskScanState { NotScanned, Complete, Partial, Unavailable }

public sealed class DiskNode(string path, bool folder, DiskNode? parent)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
    public bool IsFolder { get; } = folder;
    public string Kind => IsFolder ? "文件夹" : "文件";
    public DiskNode? Parent { get; } = parent;
    private List<DiskNode>? _children;
    // Files do not allocate empty child lists; only directories need them.
    public List<DiskNode> Children => _children ??= [];
    public DiskScanState ScanState { get; internal set; } = folder ? DiskScanState.NotScanned : DiskScanState.Complete;
    public bool IsComplete => ScanState == DiskScanState.Complete;
    public long Bytes { get; internal set; }
    public long Files { get; internal set; }
    public double Share => Parent?.Bytes > 0 ? 100d * Bytes / Parent.Bytes : 0;
    public string ShareText => !IsComplete && Bytes == 0 ? "—" : $"{Share:0.0}%" + (Parent?.IsComplete == false ? "*" : "");
    public string Size => ScanState switch
    {
        DiskScanState.NotScanned => "未扫描",
        DiskScanState.Unavailable => "无法读取",
        DiskScanState.Partial => Bytes == 0 ? "未完整读取" : DiskEngine.FormatSize(Bytes) + "（部分）",
        _ => DiskEngine.FormatSize(Bytes)
    };
}
public sealed record DiskResult(DiskNode Root, long Skipped, bool Cancelled);
public sealed class DiskEngine
{
    public DiskResult Scan(string path, IProgress<long>? progress, CancellationToken token)
    {
        var info = new DirectoryInfo(System.IO.Path.GetFullPath(path));
        if (!info.Exists) throw new DirectoryNotFoundException();
        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new IOException("请选择本地实际目录。");
        var root = new DiskNode(info.FullName, true, null);
        var pending = new Stack<DiskNode>(); pending.Push(root);
        var folders = new List<DiskNode> { root };
        long visited = 0, skipped = 0;
        var clock = Stopwatch.StartNew();
        while (pending.Count > 0 && !token.IsCancellationRequested)
        {
            var folder = pending.Pop();
            folder.ScanState = DiskScanState.Complete;
            try
            {
                // Enumerate lazily without a file-count cutoff; retain cancellation and throttled progress.
                foreach (var entry in new DirectoryInfo(folder.Path).EnumerateFileSystemInfos())
                {
                    if (token.IsCancellationRequested) { folder.ScanState = DiskScanState.Partial; break; }
                    visited++;
                    try
                    {
                        var attributes = entry.Attributes;
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { skipped++; folder.ScanState = DiskScanState.Partial; continue; }
                        var isFolder = (attributes & FileAttributes.Directory) != 0;
                        var child = new DiskNode(entry.FullName, isFolder, folder);
                        if (isFolder) { folders.Add(child); pending.Push(child); }
                        else { child.Bytes = ((FileInfo)entry).Length; child.Files = 1; }
                        folder.Children.Add(child);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; folder.ScanState = DiskScanState.Partial; }
                    if (clock.ElapsedMilliseconds >= 100) { progress?.Report(visited); clock.Restart(); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { skipped++; folder.ScanState = folder.Children.Count == 0 ? DiskScanState.Unavailable : DiskScanState.Partial; }
        }
        for (var i = folders.Count - 1; i >= 0; i--)
        {
            var folder = folders[i];
            folder.Bytes = folder.Children.Sum(c => c.Bytes);
            folder.Files = folder.Children.Sum(c => c.Files);
            if (folder.ScanState == DiskScanState.Complete && folder.Children.Any(c => !c.IsComplete)) folder.ScanState = DiskScanState.Partial;
            folder.Children.Sort((a, b) => { var order = b.Bytes.CompareTo(a.Bytes); return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); });
        }
        progress?.Report(visited);
        return new(root, skipped, token.IsCancellationRequested);
    }
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes; var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
