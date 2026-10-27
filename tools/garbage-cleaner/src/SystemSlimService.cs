using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace XieBox.GarbageCleaner;

public sealed record SlimSnapshot(long FreeBytes, long? HibernateBytes, long OldBytes, bool OldPresent, bool OldPartial, string ComponentAnalysis, bool ComponentReady);
public sealed record SlimFile(string Path, RootRule Root, FileStamp Stamp, string Reason);
public sealed record SlimCategory(CategoryRule Rule, SlimFile[] Files, string Status, long RecycleBytes, long RecycleCount, string[] Notes);
public sealed record SlimCacheData(SlimCategory[] Categories, DateTime CreatedUtc, bool Complete, string Summary)
{
    public static SlimCacheData From(ScanSnapshot scan) => new(scan.Categories.Select(c => new SlimCategory(c.Rule, c.Files.Select(f => new SlimFile(f.Path, f.Root, f.Stamp, f.Reason)).ToArray(), c.Status, c.RecycleBytes, c.RecycleCount, c.ScanNotes.ToArray())).ToArray(), scan.CreatedUtc, scan.Complete, scan.Summary);
    public ScanSnapshot ToSnapshot() => new(Categories.Select(c => new CategoryResult(c.Rule, c.Files.Select(f => new Candidate(f.Path, f.Root, f.Stamp, f.Reason)).ToArray(), c.Status, recycleBytes: c.RecycleBytes, recycleCount: c.RecycleCount, scanNotes: c.Notes)).ToArray(), CreatedUtc, Complete, Summary);
}
public sealed record SlimReply(SlimSnapshot? Snapshot, string Message, bool Success, SlimCacheData? Caches = null);

// A single, fixed request per elevated process. No caller-supplied paths or command lines.
public static class SystemSlimService
{
    public static string SystemRoot => Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
    public static int RunWorker(string name, int parentId)
    {
        if (!name.StartsWith("XieBox.Slim.", StringComparison.Ordinal) || !Guid.TryParseExact(name[12..], "N", out _)) return 2;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        pipe.Connect(30000);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id) || id != parentId) return 3;
        using var parent = Process.GetProcessById(parentId);
        if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 4;
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var engine = new CleanerEngine(CleanerPaths.Current, new WindowsRecycleBin(SystemRoot), discover: SlimCacheRules.Discover, scanRunning: true);
        ScanSnapshot? caches = null;
        while (reader.ReadLine() is { } command)
        {
        SlimReply reply;
        try
        {
            // Serialize across instances; never run Windows servicing tasks concurrently here.
            using var mutex = new Mutex(false, @"Global\XieBroToolBox.SystemSlim");
            bool locked;
            try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
            if (!locked) reply = new(null, "另一个系统盘瘦身任务正在运行，请稍后重试。", false);
            else
            {
                try
                {
                    if (command == "scan")
                    {
                        caches = null;
                        caches = engine.Scan([], null, CancellationToken.None);
                        var system = Execute(command);
                        reply = system with { Caches = SlimCacheData.From(caches) };
                    }
                    else if (command.StartsWith("cache:", StringComparison.Ordinal))
                    {
                        if (caches is null || !caches.Complete || !caches.Categories.Any(c => c.Id == command[6..])) throw new InvalidOperationException("扫描结果无效，请重新分析。");
                        foreach (var item in caches.Categories) item.Selected = item.Id == command[6..];
                        var plan = engine.BuildPlan(caches, []); caches = null;
                        var result = engine.ExecuteFiles(plan, null, CancellationToken.None);
                        var recycle = engine.EmptyRecycleBin(plan, IntPtr.Zero, CancellationToken.None);
                        reply = new(null, $"已删除 {result.DeletedCount:N0} 个文件（{Format.Bytes(result.DeletedBytes)}），跳过 {result.SkippedCount:N0} 个。" + (plan.Recycle ? " " + recycle : ""), true);
                    }
                    else { caches = null; reply = Execute(command); }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        catch (Exception error) { reply = new(null, "未能完成：" + error.Message, false); }
        writer.WriteLine(JsonSerializer.Serialize(reply));
        }
        return 0;
    }

    private static SlimReply Execute(string? command)
    {
        if (command == "scan") return new(Scan(), "分析完成。请选择项目查看影响并单独确认。", true);
        if (command is "components" or "old-windows")
        {
            using var servicing = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var update = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            if (servicing is not null || update is not null)
                return new(null, "Windows 有等待重启完成的更新，请重启电脑后重新分析。本次未执行清理。", false);
        }
        var before = new DriveInfo(SystemRoot).AvailableFreeSpace;
        var result = command switch
        {
            "components" => Run("dism.exe", "/Online", "/English", "/Cleanup-Image", "/StartComponentCleanup", "/NoRestart"),
            "hibernate-off" => Run("powercfg.exe", "/hibernate", "off"),
            "hibernate-on" => Run("powercfg.exe", "/hibernate", "on"),
            "old-windows" => Run("cleanmgr.exe", "/autoclean", "/d", SystemRoot.TrimEnd('\\')),
            _ => throw new InvalidOperationException("未知的系统盘操作。")
        };
        var success = result.Code is 0 or 3010;
        var after = new DriveInfo(SystemRoot).AvailableFreeSpace;
        var change = after - before;
        var message = success ? "Windows 已完成本次命令。" : $"Windows 返回错误代码 {result.Code}，本项未确认完成。";
        if (result.Code == 3010) message += "需要重启电脑以完成处理，本应用不会自动重启。";
        if (command == "old-windows" && Directory.Exists(Path.Combine(SystemRoot, "Windows.old")))
            message += "旧系统目录仍存在，可能有未处理内容；请重新分析，不能视为全部清空。";
        message += $"\n系统盘可用空间变化：{(change >= 0 ? "+" : "-")}{Format.Bytes(Math.Abs(change))}（包含同期其他程序的影响）。";
        if (!success) message += "\n" + result.Output;
        return new(null, message, success);
    }

    private static SlimSnapshot Scan()
    {
        var component = Run("dism.exe", "/Online", "/English", "/Cleanup-Image", "/AnalyzeComponentStore");
        long? hibernate = null;
        try { var file = new FileInfo(Path.Combine(SystemRoot, "hiberfil.sys")); hibernate = file.Exists ? file.Length : 0; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var old = Path.Combine(SystemRoot, "Windows.old");
        bool present = Directory.Exists(old), partial = false; long bytes = 0;
        var pending = new Stack<string>(); if (present) pending.Push(old);
        var budget = Stopwatch.StartNew();
        while (pending.Count > 0)
        {
            if (budget.Elapsed > TimeSpan.FromSeconds(20)) { partial = true; break; }
            var directory = pending.Pop();
            try
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) { partial = true; continue; }
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (budget.Elapsed > TimeSpan.FromSeconds(20)) { partial = true; pending.Clear(); break; }
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if (attributes.HasFlag(FileAttributes.ReparsePoint)) { partial = true; continue; }
                        if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(path);
                        else bytes += new FileInfo(path).Length;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { partial = true; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { partial = true; }
        }
        return new(new DriveInfo(SystemRoot).AvailableFreeSpace, hibernate, bytes, present, partial, component.Output, component.Code == 0);
    }
    private static (int Code, string Output) Run(string file, params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, file)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法启动 Windows 系统工具。");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        return (process.ExitCode, output.Length > 24000 ? output[..24000] : output);
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
}
