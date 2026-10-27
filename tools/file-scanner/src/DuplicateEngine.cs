using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;

namespace XieBox.FileScanner;
public sealed record DuplicateResult(IReadOnlyList<FileMatch> Files, int Groups, int Skipped, bool Limited)
{ public long BytesRead { get; init; } public int FullyHashedFiles { get; init; } }
public static class DuplicateEngine
{
    public static DuplicateResult Scan(string path, IProgress<string>? progress, CancellationToken token, int limit = 100_000)
    {
        var root = new DirectoryInfo(Path.GetFullPath(path));
        if (!root.Exists || (root.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new IOException("请选择可访问的实际目录。");
        var pending = new Stack<DirectoryInfo>(); pending.Push(root); var candidates = new List<FileInfo>(); var skipped = 0; var visited = 0; var limited = false;
        var timer = Stopwatch.StartNew(); long bytesRead = 0; int fullyHashed = 0;
        void Report(string message, bool force = false) { if (force || timer.ElapsedMilliseconds >= 200) { progress?.Report(message); timer.Restart(); } }
        Report("正在收集文件…", true);
        while (pending.TryPop(out var directory) && !limited)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    Report($"正在收集文件 · 已检查 {visited:N0} 项");
                    token.ThrowIfCancellationRequested(); if (++visited > limit) { limited = true; break; }
                    try
                    {
                        if ((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { skipped++; continue; }
                        if (entry is DirectoryInfo child) pending.Push(child); else if (entry is FileInfo file) { _ = file.Length; candidates.Add(file); }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
        }
        var matches = new List<FileMatch>(); var groupNumber = 0; var checkedFiles = 0;
        var groups = candidates.GroupBy(f => f.Length).Where(g => g.Count() > 1).OrderBy(g => g.Key).ToArray();
        var total = groups.Sum(g => g.Count());
        var buffer = new byte[128 * 1024];
        string Fingerprint(FileInfo file, bool sample)
        {
            token.ThrowIfCancellationRequested();
            var modified = file.LastWriteTimeUtc; var bytes = file.Length;
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1, sample ? FileOptions.RandomAccess : FileOptions.SequentialScan);
            if (stream.Length != bytes) throw new IOException("文件已变化");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (sample)
            {
                const int chunk = 16 * 1024;
                foreach (var offset in new[] { 0L, (bytes - chunk) / 2, bytes - chunk })
                {
                    token.ThrowIfCancellationRequested(); stream.Position = offset;
                    stream.ReadExactly(buffer, 0, chunk); bytesRead += chunk; hash.AppendData(buffer, 0, chunk);
                }
            }
            else
            {
                int read;
                while ((read = stream.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); bytesRead += read;
                    Report($"正在完整校验 · 已筛选 {checkedFiles:N0}/{total:N0} 个文件 · 已读取 {ScanEngine.FormatSize(bytesRead)}");
                }
                fullyHashed++;
            }
            file.Refresh(); if (file.Length != bytes || file.LastWriteTimeUtc != modified) throw new IOException("文件已变化");
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        foreach (var sizeGroup in groups)
        {
            var samples = new Dictionary<string, List<(FileInfo File, DateTime Modified)>>();
            bool large = sizeGroup.Key > 64 * 1024;
            foreach (var file in sizeGroup)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var key = Fingerprint(file, large);
                    if (!samples.TryGetValue(key, out var files)) samples[key] = files = [];
                    files.Add((file, file.LastWriteTimeUtc));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                checkedFiles++;
                Report($"正在快速筛选 · {checkedFiles:N0}/{total:N0} 个文件 · 已读取 {ScanEngine.FormatSize(bytesRead)}");
            }
            foreach (var possible in samples.Values.Where(g => g.Count > 1))
            {
                var hashes = new Dictionary<string, List<FileMatch>>();
                foreach (var candidate in possible)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var file = candidate.File; file.Refresh();
                        if (file.Length != sizeGroup.Key || file.LastWriteTimeUtc != candidate.Modified) { skipped++; continue; }
                        var key = large ? Fingerprint(file, false) : "already-fully-hashed";
                        if (!hashes.TryGetValue(key, out var files)) hashes[key] = files = [];
                        files.Add(new(file.FullName, file.Length));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                }
                foreach (var group in hashes.Values.Where(g => g.Count > 1))
                {
                    groupNumber++;
                    matches.AddRange(group.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(f => f with { Group = $"重复组 {groupNumber}" }));
                }
            }
        }
        token.ThrowIfCancellationRequested();
        Report($"比对完成 · {checkedFiles:N0} 个候选文件 · 已读取 {ScanEngine.FormatSize(bytesRead)}", true);
        return new(matches, groupNumber, skipped, limited) { BytesRead = bytesRead, FullyHashedFiles = fullyHashed };
    }
}
