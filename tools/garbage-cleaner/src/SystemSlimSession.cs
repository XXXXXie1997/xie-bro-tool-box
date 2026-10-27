using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace XieBox.GarbageCleaner;

// Keeps the frozen cleanup plan inside the elevated worker. Only category IDs cross back.
public sealed class SystemSlimSession : IDisposable
{
    private NamedPipeServerStream? pipe;
    private StreamReader? reader;
    private StreamWriter? writer;
    private Process? worker;
    public async Task<SlimReply> Request(string command)
    {
        if (command is not ("scan" or "components" or "hibernate-off" or "hibernate-on" or "old-windows") && !command.StartsWith("cache:", StringComparison.Ordinal)) throw new ArgumentException("未知操作");
        try
        {
            if (pipe is null || !pipe.IsConnected || worker?.HasExited != false)
            {
                Dispose();
                if (command != "scan") throw new IOException("管理员分析会话已结束，请重新分析。");
                var name = "XieBox.Slim." + Guid.NewGuid().ToString("N");
                pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
                start.ArgumentList.Add("--slim-worker"); start.ArgumentList.Add(name); start.ArgumentList.Add(Environment.ProcessId.ToString());
                worker = Process.Start(start) ?? throw new IOException("无法启动管理员分析进程。");
                await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(45));
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var id) || id != worker.Id) throw new IOException("分析进程身份验证失败。");
                reader = new(pipe, leaveOpen: true); writer = new(pipe, leaveOpen: true) { AutoFlush = true };
            }
            await writer!.WriteLineAsync(command);
            return JsonSerializer.Deserialize<SlimReply>(await reader!.ReadLineAsync() ?? throw new IOException("分析进程已退出。")) ?? throw new IOException("未收到分析结果。");
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        try { writer?.Dispose(); } catch (IOException) { }
        reader?.Dispose(); pipe?.Dispose(); worker?.Dispose();
        writer = null; reader = null; pipe = null; worker = null;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
}
