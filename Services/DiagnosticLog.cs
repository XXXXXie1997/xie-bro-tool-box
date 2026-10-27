using System.IO;
using System.Text;

namespace XieBox.Services;

public static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static string? _file;
    public static string DirectoryPath { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XieBox", "logs");
    public static void Initialize(string dataDirectory)
    {
        lock (Gate)
        {
            DirectoryPath = Path.Combine(dataDirectory, "logs");
            _file = Path.Combine(DirectoryPath, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                foreach (var file in new DirectoryInfo(DirectoryPath).GetFiles("session-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(29)) file.Delete();
            }
            catch { /* Diagnostics must not prevent startup or error reporting. */ }
            Write("Started", $"Version={typeof(DiagnosticLog).Assembly.GetName().Version}; Runtime={Environment.Version}; OS={Environment.OSVersion}");
        }
    }
    public static void Write(string stage, string? detail = null)
    {
        lock (Gate)
        {
            if (_file is null) return;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(_file, $"{DateTimeOffset.Now:O} [{stage}] {detail}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { /* Never throw from an exception handler. */ }
        }
    }
    public static string PrepareDirectoryForBrowsing()
    {
        lock (Gate)
        {
            if (_file is null) throw new IOException("日志尚未初始化，请重新启动应用后重试。");
            // Unlike crash logging, an explicit user request must surface write errors.
            var directory = Path.GetFullPath(DirectoryPath);
            Directory.CreateDirectory(directory);
            File.AppendAllText(_file, $"{DateTimeOffset.Now:O} [OpenLogDirectory]{Environment.NewLine}", Encoding.UTF8);
            return directory;
        }
    }
    public static void Error(string stage, Exception error) => Write(stage, error.ToString());
    public static void Stop() { lock (Gate) _file = null; }
}
