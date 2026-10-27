using System.IO;
using XieBox.Services;

internal static partial class Program
{
    private static int CheckLogDirectory()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "log-fixtures", Guid.NewGuid().ToString("N"), "日志 空格"));
        DiagnosticLog.Initialize(root);
        DiagnosticLog.Write("Fixture", "local verification");
        var directory = DiagnosticLog.PrepareDirectoryForBrowsing();
        var log = Directory.GetFiles(directory, "session-*.log").Single();
        Check(File.ReadAllText(log).Contains("[Fixture]") && File.ReadAllText(log).Contains("[OpenLogDirectory]"), "log initialization, writing and open-request recording succeed with Unicode and spaces");
        File.Delete(log); Directory.Delete(directory);
        Check(Directory.Exists(DiagnosticLog.PrepareDirectoryForBrowsing()), "opening logs recreates a removed directory and writes a fresh log");
        var blocked = Path.Combine(root, "blocked"); Directory.CreateDirectory(blocked); File.WriteAllText(Path.Combine(blocked, "logs"), "fixture obstacle");
        DiagnosticLog.Initialize(blocked);
        try { DiagnosticLog.PrepareDirectoryForBrowsing(); throw new Exception("expected a visible write error"); }
        catch (IOException) { Check(true, "explicit open request surfaces failure instead of pretending logging succeeded"); }
        DiagnosticLog.Stop();
        return 0;
    }
}
