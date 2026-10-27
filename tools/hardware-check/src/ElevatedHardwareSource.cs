using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace XieBox.HardwareCheck;

// The elevated companion only accepts fixed, read-only hardware requests. The host
// and all file tools keep their original token. A private pipe binds it to its parent.
public sealed class ElevatedHardwareSource(string executable, Func<ProcessStartInfo, Process?>? launch = null) : IHardwareSource
{
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _worker;
    private bool _disposed;
    public HardwareSnapshot ReadInventory(CancellationToken token, IProgress<string>? progress)
    {
        progress?.Report("正在请求管理员权限并读取硬件…");
        return Request<HardwareSnapshot>("inventory", token);
    }
    public IReadOnlyList<SensorReading> ReadSensors(CancellationToken token) => Request<SensorReading[]>("sensors", token);
    private void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pipe?.IsConnected == true) return;
        Disconnect();
        var name = "XieBox.Hardware." + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add("--hardware-worker"); start.ArgumentList.Add(name); start.ArgumentList.Add(Environment.ProcessId.ToString());
            _worker = (launch ?? Process.Start)(start) ?? throw new IOException("未能启动硬件读取进程。");
            _pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(_pipe.SafePipeHandle, out var client) || client != _worker.Id) throw new IOException("硬件读取进程身份验证失败。");
            _reader = new StreamReader(_pipe, leaveOpen: true);
            _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { Disconnect(); throw new IOException("已取消管理员授权。点击重新读取可再次授权。"); }
        catch { Disconnect(); throw; }
    }
    private T Request<T>(string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Connect(); token.ThrowIfCancellationRequested();
        try
        {
            _writer!.WriteLine(command);
            var line = _reader!.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90)).GetAwaiter().GetResult() ?? throw new IOException("硬件读取进程已退出。");
            var reply = JsonSerializer.Deserialize<HardwareReply>(line) ?? throw new IOException("硬件读取响应为空。");
            if (reply.Error is not null) throw new IOException(reply.Error);
            token.ThrowIfCancellationRequested();
            return reply.Data.Deserialize<T>() ?? throw new IOException("硬件读取结果为空。");
        }
        catch (TimeoutException) { Disconnect(); throw new IOException("硬件读取超时，请重试。"); }
    }
    public void CloseSensors()
    {
        if (_pipe?.IsConnected != true) return;
        try { Request<bool>("pause", CancellationToken.None); } catch { Disconnect(); }
    }
    private void Disconnect()
    {
        try { _writer?.Dispose(); } catch (IOException) { } finally { _writer = null; }
        _reader?.Dispose(); _reader = null; _pipe?.Dispose(); _pipe = null;
        if (_worker is not null)
        {
            try { if (!_worker.WaitForExit(1500)) _worker.Kill(); } catch (InvalidOperationException) { } catch (Win32Exception) { }
            _worker.Dispose(); _worker = null;
        }
    }
    public void Dispose() { _disposed = true; Disconnect(); }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}

public sealed record HardwareReply(JsonElement Data, string? Error = null);
public static class HardwareWorker
{
    public static int Run(string pipeName, int parentId)
    {
        if (!pipeName.StartsWith("XieBox.Hardware.", StringComparison.Ordinal) || !Guid.TryParseExact(pipeName["XieBox.Hardware.".Length..], "N", out _)) return 2;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        pipe.Connect(30000);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != parentId) return 3;
        using var parent = Process.GetProcessById(parentId);
        // Refuse a pipe owned by a different executable, even if the name was supplied manually.
        if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 4;
        using var source = new HardwareSource();
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        while (reader.ReadLine() is { } command)
        {
            try
            {
                object result = command switch
                {
                    "inventory" => source.ReadInventory(CancellationToken.None, null),
                    "sensors" => source.ReadSensors(CancellationToken.None),
                    "pause" => Pause(),
                    _ => throw new IOException("未知的硬件读取请求。")
                };
                writer.WriteLine(JsonSerializer.Serialize(new HardwareReply(JsonSerializer.SerializeToElement(result))));
            }
            catch (Exception e) { writer.WriteLine(JsonSerializer.Serialize(new HardwareReply(JsonSerializer.SerializeToElement(false), e.Message))); }
        }
        return 0;
        bool Pause() { source.CloseSensors(); return true; }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
