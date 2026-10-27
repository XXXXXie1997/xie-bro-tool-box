using System.Windows;
using System.Windows.Controls;

namespace XieBox.Views;
public partial class AboutView : UserControl
{
    private readonly Action? _contact;
    private readonly Action? _cleanup;
    public AboutView(Action? contact = null, Action? cleanup = null)
    {
        InitializeComponent();
        _contact = contact;
        _cleanup = cleanup;
        VersionText.Text = "v" + (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AboutView).Assembly)?.InformationalVersion.Split('+')[0] ?? "—");
    }
    private void Contact_Click(object sender, RoutedEventArgs e) => _contact?.Invoke();
    private void Cleanup_Click(object sender, RoutedEventArgs e) => _cleanup?.Invoke();
}
