using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal static class SetupActions
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--shortcut") return 1;
        try
        {
            var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            var link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { args[1] });
            var type = link.GetType();
            type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { args[2] });
            type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(args[2]) });
            type.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); return 0;
        }
        catch { MessageBox.Show("安装已完成，但桌面快捷方式创建失败。你仍可通过开始菜单打开蟹小箱。", "蟹小箱", MessageBoxButtons.OK, MessageBoxIcon.Information); return 1; }
    }
}
