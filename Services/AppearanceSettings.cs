using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace XieBox.Services;

public sealed class AppearanceSettings
{
    public bool IsDark { get; private set; }
    // Read once per launch. Manual changes last for this session only.
    public AppearanceSettings(Func<bool>? readSystemDark = null)
    {
        try { IsDark = (readSystemDark ?? ReadSystemDark)(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        { IsDark = false; }
    }
    private static bool ReadSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
    public void Apply(bool animate = false)
    {
        ThemePalette.Apply(new ResourceDictionary
        {
            Source = new Uri($"/XieBroToolBox;component/Resources/Palette.{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        }, animate);
        var logo = new System.Windows.Media.Imaging.BitmapImage(new Uri(
            $"pack://application:,,,/XieBroToolBox;component/Resources/{(IsDark ? "logo-dark.png" : "logo.png")}"));
        logo.Freeze();
        Application.Current.Resources["AppLogo"] = logo;
    }
    public void Toggle() { IsDark = !IsDark; Apply(animate: true); }
}

