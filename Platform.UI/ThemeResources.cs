using System.Windows;
using System.Windows.Controls;

namespace XieBox.Platform.UI;

// Bind semantic resource keys, not resolved Brush instances. Both state changes
// and replacement of the active palette then update an already-open view.
public static class ThemeResources
{
    public static readonly DependencyProperty ForegroundKeyProperty = DependencyProperty.RegisterAttached(
        "ForegroundKey", typeof(string), typeof(ThemeResources), new PropertyMetadata(null, (d, e) => Apply(d, e, TextBlock.ForegroundProperty)));
    public static readonly DependencyProperty BorderKeyProperty = DependencyProperty.RegisterAttached(
        "BorderKey", typeof(string), typeof(ThemeResources), new PropertyMetadata(null, (d, e) => Apply(d, e, Border.BorderBrushProperty)));
    public static string? GetForegroundKey(DependencyObject target) => (string?)target.GetValue(ForegroundKeyProperty);
    public static void SetForegroundKey(DependencyObject target, string value) => target.SetValue(ForegroundKeyProperty, value);
    public static string? GetBorderKey(DependencyObject target) => (string?)target.GetValue(BorderKeyProperty);
    public static void SetBorderKey(DependencyObject target, string value) => target.SetValue(BorderKeyProperty, value);
    private static void Apply(DependencyObject target, DependencyPropertyChangedEventArgs args, DependencyProperty property)
    {
        if (target is FrameworkElement element && args.NewValue is string key) element.SetResourceReference(property, key);
    }
}
