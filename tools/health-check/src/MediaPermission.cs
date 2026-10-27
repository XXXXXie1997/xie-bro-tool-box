using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using XieBox.Platform.UI;

namespace XieBox.HealthCheck;

internal static class MediaPermission
{
    public static bool IsDenied(Exception error) => error is UnauthorizedAccessException || error.HResult == unchecked((int)0x80070005);

    public static bool NeedsConsent(bool microphone)
    {
        var path = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + (microphone ? "microphone" : "webcam");
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (var suffix in new[] { "", @"\NonPackaged" })
                {
                    using var key = hive.OpenSubKey(path + suffix);
                    if (string.Equals(key?.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase)) return true;
                }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { }
        // Unknown is not denied: attempt the actual device operation.
        return false;
    }

    public static string Request(Window? owner, bool microphone)
    {
        var device = microphone ? "麦克风" : "摄像头";
        if (owner is null) return $"需要允许访问{device}后重试。";
        if (!DialogWindow.Confirm(owner,
            $"测试需要访问{device}。请在 Windows 中开启“{device}访问”和“允许桌面应用访问{device}”，然后回到这里重试。此权限由 Windows 隐私设置控制。",
            $"允许访问{device}", "前往授权"))
            return $"暂未授权{device}访问，本项未完成。再次开始测试可重新授权。";
        try
        {
            Process.Start(new ProcessStartInfo(microphone ? "ms-settings:privacy-microphone" : "ms-settings:privacy-webcam") { UseShellExecute = true });
            return $"已打开{device}权限设置。允许访问后请重新开始测试。";
        }
        catch (System.ComponentModel.Win32Exception)
        { return $"无法打开设置，请在 Windows“隐私和安全性”中允许访问{device}后重试。"; }
    }
}
