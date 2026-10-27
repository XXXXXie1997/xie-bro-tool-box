using System.Windows.Controls;
using XieBox.ViewModels;
namespace XieBox.Views;
public partial class ModuleView : UserControl
{
    public ModuleView(MainViewModel model)
    {
        InitializeComponent(); DataContext = model;
    }
}
