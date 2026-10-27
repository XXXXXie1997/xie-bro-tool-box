using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

internal static partial class Program
{
    private static void CheckStableGutter()
    {
        foreach (var kind in new[] { "page", "list", "table" })
        {
            var rows = new System.Collections.ObjectModel.ObservableCollection<string> { "first" };
            var content = new Border { Height = 40 };
            FrameworkElement control = kind switch
            {
                "page" => new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                "list" => new ListBox { ItemsSource = rows },
                _ => new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, Columns = { new DataGridTextColumn { Binding = new Binding(), Width = new DataGridLength(1, DataGridLengthUnitType.Star) } } }
            };
            var window = new Window { Width = 600, Height = 300, Content = control, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show(); window.UpdateLayout(); AwaitUi(Task.Delay(50));
            var viewer = TabDescendants(control).OfType<ScrollViewer>().First();
            var bar = TabDescendants(viewer).OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);
            var presenter = TabDescendants(viewer).OfType<ScrollContentPresenter>().First();
            var width = presenter.ActualWidth;
            Check(bar.Visibility == Visibility.Hidden && bar.ActualWidth > 0, kind + " reserves hidden vertical scrollbar slot");
            content.Height = 1800; for (var i = 0; i < 80; i++) rows.Add("row " + i);
            window.UpdateLayout(); AwaitUi(Task.Delay(50));
            Check(bar.Visibility == Visibility.Visible && Math.Abs(presenter.ActualWidth - width) < 0.1, kind + " keeps content width when scrollbar appears");
            content.Height = 40; rows.Clear(); rows.Add("first");
            window.UpdateLayout(); AwaitUi(Task.Delay(50));
            Check(bar.Visibility == Visibility.Hidden && Math.Abs(presenter.ActualWidth - width) < 0.1, kind + " keeps content width when scrollbar disappears");
            window.Close();
        }
    }
}
