using System.Windows;
using System.Windows.Controls;
using XieBox.Platform.UI;

namespace XieBox.Views;
public partial class MainWindow
{
    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DialogWindow
        {
            Owner = this, Title = "关于蟹小箱", Width = 600,
            ResizeMode = ResizeMode.NoResize
        };
        dialog.Content = new AboutView(() => ShowContact(dialog), () => ShowDataCleanup(dialog));
        dialog.FitContent().ShowDialog();
    }
    private void Support_Click(object sender, RoutedEventArgs e)
    {
        new DialogWindow { Owner = this, Title = "支持作者", Width = 590, Content = new SupportView() }.FitContent().ShowDialog();
    }
    private void ShowContact(Window owner)
    {
        var content = new StackPanel { Margin = new Thickness(32, 24, 32, 32) };
        content.Children.Add(new TextBlock { Text = "期待听到你的想法", FontSize = 20, FontWeight = FontWeights.SemiBold });
        content.Children.Add(Description("使用反馈、建议，或只是打个招呼。"));
        content.Children.Add(Description("邮箱　　1416849251@qq.com"));
        new DialogWindow { Owner = owner, Title = "联系我们", Width = 460, Content = content }.FitContent().ShowDialog();
    }
    private TextBlock Description(string text) => new() { Text = text, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 16, 0, 0) };
}


