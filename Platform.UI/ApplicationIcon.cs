using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XieBox.Platform.UI;

public static class ApplicationIcon
{
    public static ImageSource Fallback { get; } = CreateFallback();

    // Read icon resources only; never execute the application or its command line.
    public static ImageSource Load(string? source, string? fallback = null)
        => TryLoad(source) ?? TryLoad(fallback) ?? Fallback;

    private static ImageSource? TryLoad(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        nint icon = 0;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(source.Trim());
            if (path.TrimStart('"').StartsWith(@"\\")) return null;
            var index = 0;
            var comma = path.LastIndexOf(',');
            if (comma >= 0 && int.TryParse(path[(comma + 1)..], out index)) path = path[..comma].Trim();
            else index = 0;
            if (path.StartsWith('"'))
            {
                var end = path.IndexOf('"', 1);
                if (end < 0) return null;
                path = path[1..end];
            }
            else if (!File.Exists(path))
            {
                var end = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (end >= 0) path = path[..(end + 4)];
            }
            // Avoid remote icon lookups while enumerating local software.
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || !File.Exists(path)) return null;
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var info = new ShellFileInfo();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100);
                icon = info.Icon;
            }
            else
            {
                var handles = new nint[1];
                ExtractIconEx(path, index, handles, null, 1);
                icon = handles[0];
            }
            if (icon == 0) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or NotSupportedException or System.ComponentModel.Win32Exception) { return null; }
        finally { if (icon != 0) DestroyIcon(icon); }
    }

    private static ImageSource CreateFallback()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(228, 239, 253)), null, new Rect(0, 0, 32, 32), 7, 7);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 125, 179)), 1.5);
            dc.DrawRoundedRectangle(Brushes.White, pen, new Rect(6, 7, 20, 18), 2, 2);
            dc.DrawLine(pen, new Point(6, 12), new Point(26, 12));
        }
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, [Out] nint[] large, [Out] nint[]? small, uint count);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
