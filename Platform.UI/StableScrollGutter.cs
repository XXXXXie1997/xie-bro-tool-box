using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace XieBox.Platform.UI;

// Keep Auto scrollbar slots in layout, while hiding their visuals and hit targets.
public static class StableScrollGutter
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(StableScrollGutter), new PropertyMetadata(false, Changed));
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not ScrollBar bar) return;
        bar.Loaded -= Loaded;
        if ((bool)e.NewValue) { bar.Loaded += Loaded; if (bar.IsLoaded) Apply(bar); }
    }
    private static void Loaded(object sender, RoutedEventArgs e) => Apply((ScrollBar)sender);
    private static void Apply(ScrollBar bar)
    {
        if (bar.TemplatedParent is not ScrollViewer viewer) return;
        var vertical = bar.Orientation == Orientation.Vertical;
        var binding = new MultiBinding { Converter = new GutterVisibility() };
        binding.Bindings.Add(new Binding(vertical ? "ComputedVerticalScrollBarVisibility" : "ComputedHorizontalScrollBarVisibility") { Source = viewer });
        binding.Bindings.Add(new Binding(vertical ? "VerticalScrollBarVisibility" : "HorizontalScrollBarVisibility") { Source = viewer });
        BindingOperations.SetBinding(bar, UIElement.VisibilityProperty, binding);
    }
    private sealed class GutterVisibility : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[1] is ScrollBarVisibility.Hidden or ScrollBarVisibility.Disabled) return Visibility.Collapsed;
            return values[0] is Visibility.Visible ? Visibility.Visible : Visibility.Hidden;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
