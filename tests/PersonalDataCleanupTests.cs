using System.IO;
using XieBox.Services;

internal static partial class Program
{
    private static void CheckPersonalDataCleanup()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "cleanup-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(root, "tools", "file-organizer"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        var recovery = Path.Combine(root, "tools", "file-organizer", "last-operation.json");
        File.WriteAllText(recovery, "recovery");
        File.WriteAllText(Path.Combine(root, "preferences.json"), "settings");
        File.WriteAllText(Path.Combine(root, "session.dat"), "session");
        File.WriteAllText(Path.Combine(root, "logs", "session-test.log"), "log");
        File.WriteAllText(Path.Combine(root, "user-photo.jpg"), "user data");
        var cleanup = new PersonalDataCleanup(root);
        Check(cleanup.Preview()[2].IsRecovery && cleanup.Preview()[2].Bytes == 8, "recovery separately listed with size");
        Check(cleanup.Clear([0, 1]).Length == 0, "settings/cache cleanup succeeds");
        Check(File.Exists(recovery), "default cleanup preserves undo records");
        Check(!File.Exists(Path.Combine(root, "preferences.json")) && !File.Exists(Path.Combine(root, "session.dat")), "preferences and login removed");
        Check(!Directory.Exists(Path.Combine(root, "logs")), "empty log directory removed");
        Check(File.Exists(Path.Combine(root, "user-photo.jpg")), "unknown user file preserved");
        Check(cleanup.Clear([2]).Length == 0 && !File.Exists(recovery), "explicit cleanup removes undo records");
        Check(cleanup.Clear([0, 1, 2]).Length == 0, "cleanup is repeatable");
        var linked = Path.Combine(root, "linked");
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "preferences.json"), "keep");
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linked}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
        { process.WaitForExit(); if (process.ExitCode != 0) throw new IOException(process.StandardError.ReadToEnd()); }
        Check(new PersonalDataCleanup(linked).Preview().All(g => g.Files.Length == 0), "linked root is excluded");
        Check(new PersonalDataCleanup(linked).Clear([0]).Length == 0 && File.Exists(Path.Combine(outside, "preferences.json")), "link target remains intact");
        var empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "preferences.json"), "settings");
        new PersonalDataCleanup(empty).Clear([0]);
        Check(!Directory.Exists(empty), "empty app data root removed");
        DiagnosticLog.Initialize(Path.Combine(root, "logging")); DiagnosticLog.Stop();
        new PersonalDataCleanup(Path.Combine(root, "logging")).Clear([1]); DiagnosticLog.Write("must not recreate");
        Check(!Directory.Exists(Path.Combine(root, "logging")), "shutdown cannot recreate cleared logs");
        var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Theme.xaml", UriKind.Relative) });
        Render(new XieBox.Views.AboutView(), Path.Combine(root, "about.png"), 600, 238);
        Console.WriteLine("About preview: " + Path.Combine(root, "about.png"));
        Console.WriteLine($"Personal data cleanup: {_checks} checks passed.");
    }
}
