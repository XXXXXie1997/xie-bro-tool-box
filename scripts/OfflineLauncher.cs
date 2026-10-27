using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// Compiled with the Windows .NET Framework compiler; the application itself
// carries its own modern .NET runtime under app/.
internal static class OfflineLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool smoke = args.Contains("--smoke-test");
        try
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
            string executable = Path.Combine(folder, "XieBox.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("缺少 app 文件夹，请完整解压压缩包后再运行。");
            var start = new ProcessStartInfo(executable, string.Join(" ", args.Select(Quote))) { WorkingDirectory = folder, UseShellExecute = false };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new IOException("应用未能启动。");
                if (!smoke) return 0;
                if (!process.WaitForExit(60000)) { process.Kill(); return 2; }
                return process.ExitCode;
            }
        }
        catch (Exception error)
        {
            if (!smoke) MessageBox.Show(error.Message, "蟹小箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
