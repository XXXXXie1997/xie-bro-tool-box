using System.Diagnostics;
using System.Runtime.InteropServices;
using XieBox.MemoryRelease;

namespace XieBox.HealthCheck;

public static class MemoryReadWriteCheck
{
    public static HealthResult Run(CancellationToken token)
    {
        const ulong mb = 1024 * 1024;
        token.ThrowIfCancellationRequested();
        var snapshot = new MemoryEngine().Read();
        if (snapshot.Available < 512 * mb)
            return Result(HealthState.Incomplete, "当前可用内存不足 512 MB，未开始读写测试。请关闭部分应用后重新体检；这不表示内存损坏。");
        var bytes = (int)(Math.Min(128UL, snapshot.Available / mb / 16) * mb);
        IntPtr buffer = IntPtr.Zero;
        var watch = Stopwatch.StartNew();
        try
        {
            buffer = Marshal.AllocHGlobal(bytes);
            for (var pass = 0; pass < 6; pass++)
            {
                for (var offset = 0; offset < bytes; offset += 4)
                {
                    if ((offset & 65535) == 0) token.ThrowIfCancellationRequested();
                    Marshal.WriteInt32(buffer, offset, Pattern(pass, offset));
                }
                // Separate write and read sweeps; verify every word, not a sample of the buffer.
                for (var offset = bytes - 4; offset >= 0; offset -= 4)
                {
                    if ((offset & 65535) == 0) token.ThrowIfCancellationRequested();
                    var expected = Pattern(pass, offset);
                    var actual = Marshal.ReadInt32(buffer, offset);
                    if (actual != expected)
                        return Result(HealthState.Issue, $"读写数据不一致：第 {pass + 1}/6 轮，测试区偏移 {offset} 字节，预期 {expected:X8}，实际 {actual:X8}。\n请保存工作并进一步诊断；本结果不能定位到具体内存条。");
                }
            }
            return Result(HealthState.Normal, $"已完成 {bytes / (1024 * 1024)} MB 测试区的 6 轮写入与逐项回读校验，未发现数据不一致。耗时 {watch.Elapsed.TotalSeconds:0.0} 秒。\n测试包含全零、全一、交替位及位置相关数据。仅验证本进程分配的测试区，不覆盖全部物理内存，不能排除间歇性故障。");
        }
        catch (OutOfMemoryException)
        { return Result(HealthState.Incomplete, "无法分配测试内存，本次读写测试未完成；请关闭部分应用后重试。"); }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }
    private static int Pattern(int pass, int offset) => pass switch
    {
        0 => 0, 1 => -1, 2 => unchecked((int)0xAAAAAAAA), 3 => 0x55555555,
        4 => unchecked(offset * 1664525 + 1013904223),
        _ => ~unchecked(offset * 1664525 + 1013904223)
    };
    private static HealthResult Result(HealthState state, string detail) => new("内存基础读写测试", "hardware-check", detail, "查看硬件", state);
}
