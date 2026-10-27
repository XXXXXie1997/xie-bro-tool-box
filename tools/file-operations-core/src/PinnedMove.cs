using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XieBox.FileOperations;

// Hash and rename the same open file, denying concurrent writes/deletes until completion.
internal static class PinnedMove
{
    public static void Move(MoveItem item, bool reverse, CancellationToken token)
    {
        var source = reverse ? item.Destination : item.Source; var target = reverse ? item.Source : item.Destination;
        ProtectedDirectories.Validate(source); ProtectedDirectories.Validate(target);
        var directories = new List<SafeFileHandle>();
        try
        {
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in new[] { source, target })
                for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent)) parents.Add(parent);
            foreach (var path in parents.OrderBy(p => p.Length))
            {
                ProtectedDirectories.Validate(path);
                var handle = Open(path, 0x80, 3, 0x02000000); directories.Add(handle);
                if ((Info(handle).Attributes & (0x400 | 0x1000)) != 0) throw new IOException("目录包含链接或离线位置，操作已停止。");
            }
            using var file = Open(source, 0x80000000 | 0x10000, 1, 0);
            var info = Info(file);
            if ((info.Attributes & (0x400 | 0x1000 | 0x10 | 0x4)) != 0 || ((long)info.SizeHigh << 32 | info.SizeLow) != item.Bytes) throw new IOException("文件已变化或属于系统文件，请重新预览。");
            using var stream = new FileStream(file, FileAccess.Read, 128 * 1024, false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024]; int read;
            while ((read = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
            if (Convert.ToHexString(hash.GetHashAndReset()) != item.Hash) throw new IOException("文件内容已变化，请重新预览。");
            token.ThrowIfCancellationRequested();
            var bytes = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
            var rootOffset = IntPtr.Size == 8 ? 8 : 4; var lengthOffset = rootOffset + IntPtr.Size; var nameOffset = lengthOffset + 4;
            var size = nameOffset + bytes.Length + 2; var memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(new byte[size], 0, memory, size); // ReplaceIfExists = false, RootDirectory = null.
                Marshal.WriteInt32(memory, lengthOffset, bytes.Length); Marshal.Copy(bytes, 0, memory + nameOffset, bytes.Length);
                if (!SetFileInformationByHandle(file, 3, memory, (uint)size)) throw new IOException("无法移动文件：" + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        finally { foreach (var directory in directories) directory.Dispose(); }
    }
    private static SafeFileHandle Open(string path, uint access, uint share, uint flags)
    {
        var full = Path.GetFullPath(path); var native = full.StartsWith(@"\\") ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
        var handle = CreateFileW(native, access, share, IntPtr.Zero, 3, flags | 0x00200000 | 0x00100000, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("文件被占用或无法访问：" + new Win32Exception(error).Message);
    }
    private static HandleInfo Info(SafeFileHandle handle) { if (!GetFileInformationByHandle(handle, out var info)) throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message); return info; }
    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int type, IntPtr data, uint size);
}
