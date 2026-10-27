using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace XieBox.Services;

// Appearance selection/persistence can call this when theme settings are added.
// All visual consumers use dynamic references to these semantic brush keys.
public static class ThemePalette
{
    public static void Apply(ResourceDictionary palette, bool animate = false)
    {
        var app = Application.Current ?? throw new InvalidOperationException("主题需要在应用启动后切换。");
        app.Dispatcher.VerifyAccess();
        var (owner, index) = Find(app.Resources) ?? throw new InvalidOperationException("未加载全局调色板。");
        var current = owner.MergedDictionaries[index];
        var replacement = new ResourceDictionary();
        foreach (var key in current.Keys.Cast<object>())
        {
            if (palette[key] is not SolidColorBrush brush) throw new ArgumentException($"主题缺少颜色：{key}");
                        var copy = brush.CloneCurrentValue();
            if (animate && SystemParameters.ClientAreaAnimation && current[key] is SolidColorBrush previous)
            {
                var target = copy.Color;
                copy.Color = previous.Color;
                var transition = new ColorAnimation(copy.Color, target, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.HoldEnd
                };
                transition.Completed += (_, _) => { copy.Color = target; copy.BeginAnimation(SolidColorBrush.ColorProperty, null); copy.Freeze(); };
                copy.BeginAnimation(SolidColorBrush.ColorProperty, transition);
            }
            else copy.Freeze();
            replacement[key] = copy;
        }
        foreach (var key in replacement.Keys.Cast<object>()) current[key] = replacement[key];
    }
    private static (ResourceDictionary Owner, int Index)? Find(ResourceDictionary parent)
    {
        for (int i = 0; i < parent.MergedDictionaries.Count; i++)
        {
            var child = parent.MergedDictionaries[i];
            if (child.Keys.Cast<object>().Contains("CanvasBrush") && child.Keys.Cast<object>().Contains("DangerBrush")) return (parent, i);
            if (Find(child) is { } nested) return nested;
        }
        return null;
    }
}



