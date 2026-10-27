using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace XieBox.MemoryRelease;
public sealed record MemoryApplication(string Name, string Path, long Bytes, int ProcessCount, System.Windows.Media.ImageSource Icon)
{
    public string MemoryText => MemorySnapshot.Size((ulong)Math.Max(0, Bytes));
}
public sealed record MemorySnapshot(ulong Total, ulong Available)
{
    public double UsedPercent => Total == 0 ? 0 : 100d * (Total - Math.Min(Total, Available)) / Total;
    public string TotalText => Size(Total);
    public string AvailableText => Size(Available);
    public string UsedText => Size(Total - Math.Min(Total, Available));
    public static string Size(ulong bytes) => bytes >= 1024UL * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.00} GB" : $"{bytes / (1024d * 1024):0.0} MB";
}
public sealed record MemoryResult(MemorySnapshot Before, MemorySnapshot After, int Trimmed)
{
    public string Summary => After.Available > Before.Available
        ? $"可用内存增加 {MemorySnapshot.Size(After.Available - Before.Available)}"
        : "可用内存暂无明显增加";
}
public interface IMemoryEngine
{
    MemorySnapshot Read();
    MemoryResult Release(CancellationToken cancellationToken);
    IReadOnlyList<MemoryApplication> ReadApplications(CancellationToken token) => [];
}
public sealed class MemoryEngine : IMemoryEngine
{
    private readonly Dictionary<string, (string Name, System.Windows.Media.ImageSource Icon)> _metadata = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MemoryApplication> ReadApplications(CancellationToken token)
    {
        using var current = Process.GetCurrentProcess();
        var entries = new List<MemoryApplication>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (process.SessionId != current.SessionId) continue;
                    var bytes = process.WorkingSet64;
                    if (bytes <= 0) continue;
                    var name = process.ProcessName;
                    string path;
                    try { path = process.MainModule?.FileName ?? ""; }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException) { path = ""; }
                    var key = path.Length > 0 ? path : name;
                    if (!_metadata.TryGetValue(key, out var metadata))
                    {
                        if (path.Length > 0)
                        {
                            try { name = FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description ? description : name; }
                            catch (Exception e) when (e is System.IO.IOException or Win32Exception or UnauthorizedAccessException) { }
                        }
                        metadata = (name, XieBox.Platform.UI.ApplicationIcon.Load(path));
                        if (_metadata.Count > 512) _metadata.Clear();
                        _metadata[key] = metadata;
                    }
                    entries.Add(new(metadata.Name, path, bytes, 1, metadata.Icon));
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return entries.GroupBy(e => e.Path.Length > 0 ? e.Path : e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First() with { Bytes = g.Sum(e => e.Bytes), ProcessCount = g.Count() })
            .OrderByDescending(e => e.Bytes).ThenBy(e => e.Name).ToArray();
    }
    public MemorySnapshot Read()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(status.TotalPhysical, status.AvailablePhysical);
    }
    public MemoryResult Release(CancellationToken cancellationToken)
    {
        var before = Read(); var trimmed = 0;
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryTrimBackgroundProcess(process)) trimmed++;
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return new(before, Read(), trimmed);
    }
    public static bool TryTrimBackgroundProcess(Process process)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var current = Process.GetCurrentProcess();
            GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
            if (process.SessionId != current.SessionId || process.MainWindowHandle != IntPtr.Zero || process.Id == foreground || process.Id == Environment.ProcessId || process.WorkingSet64 < 16 * 1024 * 1024) return false;
            using var handle = OpenProcess(0x1000 | 0x0100, false, process.Id); // query limited information + set quota; never terminate.
            if (handle.IsInvalid || !IsProcessCritical(handle, out var critical) || critical) return false;
            if (!OpenProcessToken(handle, 0x0008, out var token)) return false;
            using (token)
            using (var owner = new WindowsIdentity(token.DangerousGetHandle()))
            {
                if (owner.User != identity.User) return false;
                process.Refresh(); GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                if (process.MainWindowHandle != IntPtr.Zero || process.Id == foreground) return false;
                return K32EmptyWorkingSet(handle);
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessCritical(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool K32EmptyWorkingSet(SafeProcessHandle process);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
}
