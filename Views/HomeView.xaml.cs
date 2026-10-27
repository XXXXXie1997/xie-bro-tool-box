using System.Windows.Controls;
using System.Windows;
using XieBox.Models;
using XieBox.Platform.UI;
using XieBox.ViewModels;
namespace XieBox.Views;
public partial class HomeView : UserControl
{
    private readonly Action<ModuleItem>? _open;
    private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => AnimateCard(sender, -3);
    private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => AnimateCard(sender, 0);
    private static void AnimateCard(object sender, double offset)
    {
        if (sender is not FrameworkElement { RenderTransform: System.Windows.Media.TranslateTransform transform } card) return;
        if (!SystemParameters.ClientAreaAnimation) return;
        // Template transforms may be shared and frozen; animate an instance owned by this card.
        if (transform.IsFrozen)
        {
            transform = transform.CloneCurrentValue();
            card.RenderTransform = transform;
        }
        transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(offset, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }
    public HomeView(MainViewModel model, Action<ModuleItem>? open = null, FrameworkElement? health = null) { InitializeComponent(); HealthHost.Content = health; HealthHost.Visibility = health is null ? Visibility.Collapsed : Visibility.Visible; DataContext = model; ApplicationList.ItemsSource = model.VisibleModules; _open = open; }
    private void Section_Expanded(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation || sender is not Expander { Content: FrameworkElement content }) return;
        content.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ModuleItem module) _open?.Invoke(module);
    }
}

