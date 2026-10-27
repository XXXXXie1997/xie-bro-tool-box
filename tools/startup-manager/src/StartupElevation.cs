using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace XieBox.StartupManager;

// One operation per helper. Paths and registry keys are never supplied by the host:
// the helper re-reads its fixed startup sources and checks the displayed snapshot.
public static class StartupElevation
{
    private const string Prefix = "XieBox.Startup.";
    private sealed record Request(string Name, string Command, string Location, bool Enabled);

    public static void Toggle(StartupEntry entry)
    {
        var name = Prefix + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--startup-worker"); start.ArgumentList.Add(name); start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process? worker = null;
        try
        {
            worker = Process.Start(start) ?? throw new IOException("未能启动管理员进程。");
            pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var client) || client != worker.Id) throw new IOException("管理员进程身份验证失败。");
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(new Request(entry.Name, entry.Command, entry.Location, entry.Enabled)));
            var result = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            if (result != "ok") throw new IOException(result ?? "管理员进程已退出，请刷新列表确认结果。");
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { throw new IOException("已取消管理员授权。再次操作时可重新授权。"); }
        catch (TimeoutException) { throw new IOException("操作超时，请刷新列表确认结果。"); }
        finally
        {
            pipe.Dispose();
            if (worker is not null)
            {
                try { if (!worker.WaitForExit(2000)) worker.Kill(); } catch (InvalidOperationException) { } catch (Win32Exception) { }
                worker.Dispose();
            }
        }
    }

    public static int RunWorker(string name, int parentId)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[Prefix.Length..], "N", out _)) return 2;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(30000);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != parentId) return 3;
        using var parent = Process.GetProcessById(parentId);
        if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 4;
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        try
        {
            var line = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            var request = JsonSerializer.Deserialize<Request>(line ?? "null") ?? throw new IOException("操作请求为空。");
            var engine = new StartupEngine();
            var entry = engine.Scan().Entries.SingleOrDefault(e => e.RequiresAdministrator && e.CanChange &&
                e.Name == request.Name && e.Command == request.Command && e.Location == request.Location && e.Enabled == request.Enabled)
                ?? throw new IOException("启动项已变化，请刷新后重试。");
            engine.Toggle(entry);
            writer.WriteLine("ok");
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or JsonException or TimeoutException)
        { writer.WriteLine(error.Message.Replace('\r', ' ').Replace('\n', ' ')); return 1; }
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
