using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XieBox.GarbageCleaner;

public sealed record RecycleSnapshot(long Bytes, long Count);
public interface IRecycleBin
{
    RecycleSnapshot Query();
    string EmptyWithSystemConfirmation(IntPtr owner);
}
public sealed class WindowsRecycleBin(string? root = null) : IRecycleBin
{
    public RecycleSnapshot Query()
    {
        var info = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        var result = SHQueryRecycleBinW(root, ref info);
        if (result < 0) throw new Win32Exception(result);
        return new(Math.Max(0, info.Bytes), Math.Max(0, info.Count));
    }
    public string EmptyWithSystemConfirmation(IntPtr owner)
    {
        // Keep the native confirmation. Never use SHERB_NOCONFIRMATION or erase $Recycle.Bin directly.
        var result = SHEmptyRecycleBinW(owner, root, 4);
        if (result == unchecked((int)0x800704C7)) return "回收站操作已由用户取消。";
        if (result < 0) return "回收站未能全部清空，请打开回收站检查。";
        return Query().Count == 0 ? "回收站已清空（由 Windows 确认并执行）。" : "回收站仍有文件，请检查占用或权限。";
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct QueryInfo { public uint Size; public long Bytes; public long Count; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref QueryInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr owner, string? root, uint flags);
}
