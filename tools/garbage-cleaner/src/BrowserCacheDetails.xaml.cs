using System.Windows;
using System.Windows.Controls;

namespace XieBox.GarbageCleaner;

public partial class BrowserCacheDetails : UserControl
{
    private readonly Action<CategoryResult> _showFiles;
    public BrowserCacheDetails(CategoryResult group, Action<CategoryResult> showFiles)
    {
        InitializeComponent(); DataContext = group; _showFiles = showFiles;
    }
    private void Files_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is CategoryResult browser) _showFiles(browser);
    }
}
