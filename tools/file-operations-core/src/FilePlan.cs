using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace XieBox.FileOperations;

public sealed record MoveItem(string Source, string Destination, long Bytes, long Modified, string Hash)
{
    public string Name => Path.GetFileName(Source);
    public string NewName => Path.GetFileName(Destination);
    public string Target => Destination;
}
public sealed record MoveJournal(string Root, List<MoveItem> Items, bool ExplicitFiles = false);
public sealed record MoveOutcome(int Completed, int Remaining);

public static class FilePlan
{
    public static string ValidateRoot(string root)
    {
        root = ProtectedDirectories.Validate(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("所选目录不存在。");
        if (Path.GetPathRoot(root)!.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择具体的个人文件夹，不直接整理整个磁盘。");
        CheckAncestors(root);
        return root;
    }
    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void CheckAncestors(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            ProtectedDirectories.Validate(current);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new IOException("目录或文件包含链接、离线项目，未执行操作。");
        }
    }
    public static string Fingerprint(string path, CancellationToken token)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024]; int read;
        while ((read = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 240 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ') && !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<MoveItem> Preview(string directory, Func<FileInfo, int, string> destination, CancellationToken token)
    {
        var root = ValidateRoot(directory);
        var files = new List<FileInfo>();
        foreach (var file in new DirectoryInfo(root).EnumerateFiles())
        {
            token.ThrowIfCancellationRequested();
            if (files.Count >= 5000) throw new IOException("此目录超过 5,000 个文件，请缩小范围。");
            if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.System | FileAttributes.Hidden)) == 0) files.Add(file);
        }
        return PreviewFiles(files.Select(f => f.FullName), destination, token);
    }
    public static IReadOnlyList<MoveItem> PreviewFiles(IEnumerable<string> paths, Func<FileInfo, int, string> destination, CancellationToken token)
    {
        var files = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(5001).Select(p => new FileInfo(p)).ToList();
        if (files.Count > 5000) throw new IOException("每批最多处理 5,000 个文件，请缩小范围。");
        files.Sort((a, b) => { var order = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName); });
        var output = new List<MoveItem>(); var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var index = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var root = ValidateRoot(file.DirectoryName!);
            CheckAncestors(file.FullName);
            if (!file.Exists || (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.System | FileAttributes.Hidden)) != 0) throw new IOException("文件不存在或不允许修改：" + file.Name);
            ProtectedDirectories.Validate(file.FullName);
            var target = ProtectedDirectories.Validate(destination(file, ++index));
            if (!Within(target, root) || !ValidName(Path.GetFileName(target))) throw new IOException("生成的名称无效或超出选定目录。");
            if (target.Equals(file.FullName, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(target) || Directory.Exists(target) || !targets.Add(target)) throw new IOException("目标重名：" + Path.GetFileName(target) + "。请修改规则，现有文件不会被覆盖。");
            CheckAncestors(target);
            var length = file.Length; var modified = file.LastWriteTimeUtc.Ticks;
            var hash = Fingerprint(file.FullName, token); file.Refresh();
            if (length != file.Length || modified != file.LastWriteTimeUtc.Ticks) throw new IOException("文件正在变化，请稍后重新预览。");
            output.Add(new(file.FullName, target, length, modified, hash));
        }
        return output;
    }
    private static void Save(string path, MoveJournal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, journal); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
    public static MoveOutcome Apply(string root, IReadOnlyList<MoveItem> items, string journalPath, CancellationToken token, bool explicitFiles = false)
    {
        if (!explicitFiles) root = ValidateRoot(root);
        if (File.Exists(journalPath) && Load(journalPath).Items.Count > 0) throw new IOException("上次操作仍有恢复记录，请先撤销或明确放弃撤销。");
        var journal = new MoveJournal(root, [], explicitFiles); var completed = 0;
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            ValidateItem(explicitFiles ? ValidateRoot(Path.GetDirectoryName(item.Source)!) : root, item, false, token);
            // Write intent durably before moving, so a crash between move and bookkeeping is recoverable.
            journal.Items.Add(item); Save(journalPath, journal);
            Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
            CheckAncestors(item.Destination);
            PinnedMove.Move(item, false, token); completed++;
        }
        return new(completed, 0);
    }
    private static void ValidateItem(string root, MoveItem item, bool reverse, CancellationToken token)
    {
        if (!Within(Path.GetFullPath(item.Source), root) || !Within(Path.GetFullPath(item.Destination), root)) throw new IOException("操作记录超出原目录。");
        var source = reverse ? item.Destination : item.Source; var target = reverse ? item.Source : item.Destination;
        ProtectedDirectories.Validate(source); ProtectedDirectories.Validate(target);
        CheckAncestors(source); CheckAncestors(target);
        var file = new FileInfo(source);
        if (!file.Exists || file.Length != item.Bytes || file.LastWriteTimeUtc.Ticks != item.Modified) throw new IOException("文件已变化，保留当前文件并停止：" + Path.GetFileName(source));
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("目标已存在，未覆盖：" + Path.GetFileName(target));
    }
    public static MoveJournal Load(string journalPath)
    {
        try { return JsonSerializer.Deserialize<MoveJournal>(File.ReadAllText(journalPath)) ?? throw new IOException("操作记录为空。"); }
        catch (JsonException) { throw new IOException("操作记录损坏，请保留记录并手动检查文件位置。"); }
    }
    public static int Undo(string journalPath, CancellationToken token)
    {
        var journal = Load(journalPath); if (!journal.ExplicitFiles) ValidateRoot(journal.Root); var restored = 0;
        foreach (var item in journal.Items.ToArray().Reverse())
        {
            token.ThrowIfCancellationRequested();
            ProtectedDirectories.Validate(item.Source); ProtectedDirectories.Validate(item.Destination);
            if (File.Exists(item.Source) && !File.Exists(item.Destination))
            {
                // An intent may have been saved before a move which never happened.
                var original = new FileInfo(item.Source);
                if (original.Length != item.Bytes || Fingerprint(item.Source, token) != item.Hash) throw new IOException("原位置文件已变化，请检查操作记录。");
            }
            else { ValidateItem(journal.ExplicitFiles ? ValidateRoot(Path.GetDirectoryName(item.Source)!) : journal.Root, item, true, token); PinnedMove.Move(item, true, token); restored++; }
            journal.Items.Remove(item); Save(journalPath, journal);
        }
        return restored;
    }
}

