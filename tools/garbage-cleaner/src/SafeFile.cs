using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XieBox.GarbageCleaner;

// Pin every ancestor against rename while resolving a file, then delete through its checked handle.
public static class SafeFile
{
    private const uint ReadAttributes = 0x80, DeleteAccess = 0x10000;
    private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000, OpenNoRecall = 0x00100000;
    private const uint UnsafeAttributes = 0x400 | 0x1000 | 0x40000 | 0x400000;
    public static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or NotSupportedException;
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0) throw new IOException("不支持网络路径或特殊设备路径。");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Path.IsPathFullyQualified(full) || Path.GetPathRoot(full)?.Length != 3) throw new IOException("只支持本地磁盘路径。");
        if (new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed) throw new IOException("只支持本地固定磁盘。");
        return full;
    }
    public static bool Within(string path, string directory) => path.Equals(directory, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static bool Excluded(string path, IEnumerable<string> excludes) => excludes.Any(root => Within(path, root));
    public static DirectoryLease PinDirectory(string path) => new(Normalize(path));
    public sealed class DirectoryLease : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];
        internal DirectoryLease(string path)
        {
            try
            {
                var root = Path.GetPathRoot(path)!;
                var current = root;
                Pin(current);
                foreach (var segment in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) { current = Path.Combine(current, segment); Pin(current); }
            }
            catch { Dispose(); throw; }
        }
        private void Pin(string path)
        {
            var handle = Open(path, ReadAttributes, 1, true);
            _handles.Add(handle);
            var info = Information(handle);
            if ((info.Attributes & UnsafeAttributes) != 0 || (info.Attributes & 0x10) == 0) throw new IOException("目录为链接、云端占位或不支持的对象。");
            RequireFinalPath(handle, path);
        }
        public void Dispose() { for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); _handles.Clear(); }
    }
    public static FileStamp Inspect(string path)
    {
        path = Normalize(path);
        using var parents = PinDirectory(Path.GetDirectoryName(path)!);
        using var handle = Open(path, ReadAttributes, 7, false);
        RequireFinalPath(handle, path);
        return Stamp(Information(handle));
    }
    public static bool Eligible(string path, RootRule rule, FileStamp stamp, DateTime now)
    {
        if (!Within(path, Normalize(rule.Path)) || path.Equals(Normalize(rule.Path), StringComparison.OrdinalIgnoreCase)) return false;
        string[] protectedNames = ["Cookies", "Login Data", "Bookmarks", "History", "Preferences", "Web Data", "Local State", "places.sqlite", "logins.json", "key4.db", "cookies.sqlite"];
        if (protectedNames.Any(n => Path.GetFileName(path).Equals(n, StringComparison.OrdinalIgnoreCase))) return false;
        if (rule.Extensions is not null && !rule.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return false;
        return stamp.Links == 1 && stamp.Modified <= now.Subtract(rule.MinimumAge).ToFileTimeUtc() && stamp.Creation <= now.Subtract(rule.MinimumAge).ToFileTimeUtc();
    }
    public static void Delete(PlannedFile file, IEnumerable<string> excluded, DateTime now)
    {
        var path = Normalize(file.Path);
        if (Excluded(path, excluded) || !Eligible(path, file.Root, file.Stamp, now)) throw new IOException("文件已排除或不符合规则。");
        using var parents = PinDirectory(Path.GetDirectoryName(path)!);
        using var handle = Open(path, ReadAttributes | DeleteAccess, 1, false);
        RequireFinalPath(handle, path);
        if (Stamp(Information(handle)) != file.Stamp) throw new IOException("文件自扫描后已改变，已保留。");
        var disposition = new Disposition { Delete = true };
        if (!SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf<Disposition>())) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private static SafeFileHandle Open(string path, uint access, uint share, bool directory)
    {
        var handle = CreateFileW("\\\\?\\" + path, access, share, IntPtr.Zero, 3, OpenReparsePoint | OpenNoRecall | (directory ? BackupSemantics : 0), IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error);
    }
    private static HandleInfo Information(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }
    private static FileStamp Stamp(HandleInfo info)
    {
        if ((info.Attributes & (UnsafeAttributes | 0x10 | 0x1 | 0x4)) != 0 || info.Links != 1) throw new IOException("文件为链接、占位、只读、系统文件或有多个硬链接。");
        return new(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow, ((long)info.CreatedHigh << 32) | info.CreatedLow,
            ((long)info.ModifiedHigh << 32) | info.ModifiedLow, ((long)info.SizeHigh << 32) | info.SizeLow, info.Links);
    }
    private static void RequireFinalPath(SafeFileHandle handle, string expected)
    {
        var text = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, text, (uint)text.Capacity, 0);
        if (length == 0 || length >= text.Capacity || !text.ToString().Equals("\\\\?\\" + expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("文件实际路径发生变化，已跳过。");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.Bool)] public bool Delete; }
    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, ModifiedLow, ModifiedHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref Disposition info, uint size);
}
