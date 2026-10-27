using System.Runtime.InteropServices;

namespace XieBox.HealthCheck;

public sealed record Presence(bool? Available, string Description);

public static class DevicePresence
{
    public static IReadOnlyDictionary<string, Presence> Read(CancellationToken token)
    {
        var results = new Dictionary<string, Presence>();
        foreach (var item in GuidedChecks.Items)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                results[item.Kind] = item.Kind switch
                {
                    "microphone" => Audio(1),
                    "audio" => Audio(0),
                    "camera" => Camera(),
                    "screen" => Pnp("PNPClass = 'Monitor'", "显示设备", token),
                    "input" => Pnp("PNPClass = 'Keyboard' OR PNPClass = 'Mouse'", "键盘或鼠标", token),
                    _ => Pnp("PNPClass = 'USB'", "USB 控制器或设备", token)
                };
            }
            catch (Exception error) when (error is COMException or InvalidCastException or System.Management.ManagementException or UnauthorizedAccessException or InvalidOperationException or System.IO.IOException)
            { results[item.Kind] = new(null, "设备检测未完成，无法确认是否已连接。请重新检测，不能据此判断设备不存在。"); }
        }
        return results;
    }
    private static Presence Pnp(string condition, string label, CancellationToken token)
    {
        var rows = DeviceProbes.Query(@"root\cimv2", $"SELECT Name,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Present = TRUE AND ({condition})", token);
        if (rows.Count == 0) return new(false, $"未检测到{label}。连接设备后可点击重新检测设备。");
        if (!rows.Any(r => Convert.ToString(r.GetValueOrDefault("ConfigManagerErrorCode")) == "0"))
            return new(null, $"已识别{label}，但设备被禁用、状态异常或不可用。请先检查设备状态。");
        return new(true, $"已识别{label}；功能尚待验证。");
    }
    private static Presence Camera()
    {
        var enumerator = (ICreateDevEnum)new SystemDeviceEnumerator();
        System.Runtime.InteropServices.ComTypes.IEnumMoniker? devices = null;
        try
        {
            var category = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
            var hr = enumerator.CreateClassEnumerator(ref category, out devices, 0);
            if (hr == 1) return new(false, "未检测到摄像头。外接摄像头请连接后重新检测；无摄像头无需验证。");
            Marshal.ThrowExceptionForHR(hr);
            if (devices is null) return new(null, "未能读取摄像头设备列表，请重新检测。");
            var monikers = new System.Runtime.InteropServices.ComTypes.IMoniker[1];
            hr = devices.Next(1, monikers, IntPtr.Zero);
            if (hr == 1) return new(false, "未检测到摄像头。连接设备后可重新检测。");
            Marshal.ThrowExceptionForHR(hr);
            if (monikers[0] is not null) Marshal.ReleaseComObject(monikers[0]);
            return new(true, "已识别视频采集设备；画面及使用权限仍需验证。");
        }
        finally { if (devices is not null) Marshal.ReleaseComObject(devices); Marshal.ReleaseComObject(enumerator); }
    }
    private static Presence Audio(int flow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            var active = Count(1); // DEVICE_STATE_ACTIVE: excludes unplugged and disabled endpoints.
            var label = flow == 1 ? "麦克风/音频输入" : "扬声器/耳机输出";
            if (active > 0) return new(true, $"已识别 {active} 个可用{label}设备（可能包含虚拟设备）；实际功能仍需验证。");
            if (Count(2) > 0) return new(null, $"未找到可用{label}，但发现已禁用设备。请在系统声音设置中检查并启用后重新检测。");
            return new(false, $"未检测到已连接且可用的{label}设备。连接后可重新检测；无设备无需验证。");
            uint Count(uint state)
            {
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, state, out var collection));
                try { Marshal.ThrowExceptionForHR(collection.GetCount(out var count)); return count; }
                finally { Marshal.ReleaseComObject(collection); }
            }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection collection);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); }
    [ComImport, Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")] private class SystemDeviceEnumerator { }
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator(ref Guid category, out System.Runtime.InteropServices.ComTypes.IEnumMoniker? enumerator, int flags);
    }
}
