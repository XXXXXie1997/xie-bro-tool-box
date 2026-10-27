using System.Diagnostics;
using System.IO;

namespace XieBox.FileScanner;

public sealed record FormatSet(string Name, string[] Extensions);
public sealed record ScanOptions(string Directory, bool LargeFiles, int ThresholdMb, string[] Extensions);
public sealed record ScanProgress(long Visited, int Found, int Skipped);
public sealed record ScanResult(IReadOnlyList<FileMatch> Files, long Visited, int Skipped, bool Cancelled, bool Limited);
public sealed record FileMatch(string Path, long Bytes)
{
    public string Group { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public string Type => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant() is { Length: > 0 } type ? type : "无扩展名";
    public string Size => ScanEngine.FormatSize(Bytes);
}

public sealed class ScanEngine
{
    public static IReadOnlyList<FormatSet> Formats { get; } = [
        new("视频", [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".mpeg", ".mpg"]),
        new("音频", [".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma", ".aiff", ".opus"]),
        new("图片", [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".svg", ".ico"]),
        new("压缩包", [".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz"])
    ];
    public static string[] ParseExtensions(string text)
    {
        var values = System.Text.RegularExpressions.Regex.Split(text.Trim(), @"[\s,，;；、]+").Where(v => v.Length > 0);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var extension = value.StartsWith("*.") ? value[2..] : value.StartsWith('.') ? value[1..] : value;
            if (!System.Text.RegularExpressions.Regex.IsMatch(extension, @"^[\p{L}\p{N}_+-]+$"))
                throw new ArgumentException("请填写文件扩展名。");
            result.Add("." + extension.ToLowerInvariant());
        }
        return result.ToArray();
    }
    public static bool MatchesSize(long bytes, int thresholdMb) => bytes > thresholdMb * 1024L * 1024;
    public static string FormatSize(long bytes)
    {
        double value = bytes; string[] units = ["B", "KB", "MB", "GB", "TB"]; var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.##} {units[index]}";
    }
    public ScanResult Scan(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        if (options.LargeFiles && options.ThresholdMb < 50) throw new ArgumentException("大小阈值不能低于 50 MB。");
        if (!options.LargeFiles && options.Extensions.Length == 0) throw new ArgumentException("请至少指定一种文件格式。");
        var root = new DirectoryInfo(System.IO.Path.GetFullPath(options.Directory));
        if (!root.Exists) throw new DirectoryNotFoundException("所选目录不存在或无法访问。");
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("请选择实际目录，不扫描目录链接。");
        var extensions = new HashSet<string>(options.Extensions, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<DirectoryInfo>(); pending.Push(root);
        var matches = new List<FileMatch>(); long visited = 0; var skipped = 0; var cancelled = false; var limited = false;
        var clock = Stopwatch.StartNew();
        try
        {
            while (pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++visited > 500_000 || matches.Count >= 50_000) { limited = true; break; }
                        try
                        {
                            var attributes = entry.Attributes;
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { skipped++; continue; }
                            if (entry is DirectoryInfo child) pending.Push(child);
                            else if (entry is FileInfo file)
                            {
                                var size = file.Length;
                                if (options.LargeFiles ? MatchesSize(size, options.ThresholdMb) : extensions.Contains(file.Extension))
                                    matches.Add(new FileMatch(file.FullName, size));
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                        if (clock.ElapsedMilliseconds >= 100) { progress?.Report(new(visited, matches.Count, skipped)); clock.Restart(); }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                if (limited) break;
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        progress?.Report(new(visited, matches.Count, skipped));
        return new(matches.OrderByDescending(f => f.Bytes).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(), visited, skipped, cancelled, limited);
    }
}

