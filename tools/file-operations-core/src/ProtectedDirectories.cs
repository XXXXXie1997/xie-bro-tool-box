using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace XieBox.FileOperations;

public static class ProtectedDirectories
{
    public static string Validate(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) throw new IOException("不支持设备路径，请选择个人文件夹。");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var drive = Path.GetPathRoot(full)!;
        if (full[drive.Length..].Split(Path.DirectorySeparatorChar).Any(s => s.EndsWith(' ') || s.EndsWith('.') || s.Contains(':'))) throw new IOException("路径格式不支持，请选择个人文件夹。");
        var buffer = new StringBuilder(32768);
        if (GetLongPathName(full, buffer, buffer.Capacity) > 0) full = Path.TrimEndingDirectorySeparator(buffer.ToString());
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory
        };
        // Also protect offline Windows installations and reserved volume directories.
        foreach (var name in new[] { "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "$WINDOWS.~BT", "$WINDOWS.~WS", "Config.Msi" }) roots.Add(Path.Combine(drive, name));
        if (roots.Where(r => !string.IsNullOrWhiteSpace(r)).Any(r => Within(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))))
            throw new IOException("系统目录和程序目录禁止修改，请选择文档、图片等个人文件夹。");
        // Protect every user's AppData, not only the account running this process.
        var segments = full[drive.Length..].Split(Path.DirectorySeparatorChar);
        if (segments.Length >= 3 && segments[0].Equals("Users", StringComparison.OrdinalIgnoreCase) && segments[2].Equals("AppData", StringComparison.OrdinalIgnoreCase))
            throw new IOException("应用数据目录禁止修改，请选择个人文件夹。");
        return full;
    }
    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, int length);
}
