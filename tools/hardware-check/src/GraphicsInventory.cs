using System.Runtime.InteropServices;

namespace XieBox.HardwareCheck;

internal static class GraphicsInventory
{
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter, out Description description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Description
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
        public uint Vendor, Device, Subsystem, Revision;
        public UIntPtr DedicatedVideo, DedicatedSystem, SharedSystem;
        public long Luid;
        public uint Flags;
    }
    private static T Method<T>(IntPtr instance, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    public static IEnumerable<HardwareSection> Read()
    {
        var result = new List<HardwareSection>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out var factory) < 0) return result;
        try
        {
            for (uint i = 0; i < 32; i++)
            {
                if (Method<EnumAdapters>(factory, 12)(factory, i, out var adapter) < 0) break;
                try
                {
                    if (Method<GetDescription>(adapter, 10)(adapter, out var info) < 0 || (info.Flags & 2) != 0) continue;
                    result.Add(new("显卡", info.Name + " · 显存", [new("专用显存", info.DedicatedVideo.ToUInt64() == 0 ? "无独立显存，使用共享内存" : HardwareInventory.Format(info.DedicatedVideo.ToUInt64(), "bytes")), new("可共享系统内存上限", HardwareInventory.Format(info.SharedSystem.ToUInt64(), "bytes")), new("厂商 / 设备编号", $"{info.Vendor:X4} / {info.Device:X4}")]));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }
}
