using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XieBox.FileScanner;
using XieBox.Models;
using XieBox.Platform.UI;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.Views;

internal static partial class Program
{
    private static void CheckTabsAndFavorites(string root, string preview, PlatformApi api)
    {
        var store = new LocalStore(Path.Combine(root, "tabs")); var accounts = new AccountService(api, store);
        var model = new MainViewModel(store, api, accounts, true);
        var window = new MainWindow(model, api) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        window.Show(); AwaitUi(window.Initialization);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Open(ModuleItem module) => typeof(MainWindow).GetMethod("OpenModule", flags)!.Invoke(window, [module]);
        void Home() => typeof(MainWindow).GetMethod("ShowHome", flags)!.Invoke(window, null);
        var host = (ContentControl)window.FindName("PageHost"); var originalHome = host.Content;
        Check(window.OpenTabs.Count == 0 && host.Content is HomeView, "startup shows fixed home without preopening module tabs");
        var scannerModule = model.Modules.Single(m => m.Id == "file-scanner"); Open(scannerModule);
        var scannerTab = window.OpenTabs.Single(); var scannerPage = (ScannerView)host.Content;
        scannerPage.Model.Threshold = "321";
        Open(scannerModule); Check(window.OpenTabs.Count == 1 && ReferenceEquals(scannerPage, host.Content), "opening an existing module activates the same page");
        var disk = model.Modules.Single(m => m.Id == "disk-analyzer"); Open(disk); Open(scannerModule);
        Check(scannerPage.Model.Threshold == "321" && ReferenceEquals(scannerPage, host.Content), "switching tabs preserves form state");
        window.MoveTab(scannerTab, 1); Check(window.OpenTabs[1] == scannerTab, "tab order is independent and draggable");
        var diskTab = window.OpenTabs.Single(t => t.Module == disk);
        Check(window.CloseTab(diskTab) && ReferenceEquals(scannerPage, host.Content), "closing an inactive tab does not change active content");
        var scanRoot = Path.Combine(root, "tab-scan"); Directory.CreateDirectory(scanRoot); File.WriteAllText(Path.Combine(scanRoot, "sample.txt"), "fixture");
        scannerPage.Model.Directory = scanRoot;
        RunUi(async () =>
        {
            var task = scannerPage.Model.ScanAsync();
            Check(scannerTab.IsBusy && !scannerTab.CanClose && !window.CloseTab(scannerTab), "running scanner locks its tab against closing");
            Home(); Check(ReferenceEquals(host.Content, originalHome) && scannerTab.IsBusy, "busy task may continue while home is visible");
            scannerPage.Model.Cancel(); await task;
            Check(!scannerTab.IsBusy && scannerTab.CanClose, "tab unlocks only after cancellation finishes");
        });
        Open(scannerModule);
        for (var i = 0; i < 48; i++) File.WriteAllText(Path.Combine(scanRoot, $"image-{i:D2}.png"), "fixture");
        scannerPage.Model.LargeFiles = false; scannerPage.Model.Formats.Single(f => f.Name == "图片").Selected = true;
        RunUi(scannerPage.Model.ScanAsync); window.UpdateLayout(); AwaitUi(Task.Delay(300));
        var table = (DataGrid)scannerPage.FindName("FilesTable"); table.SelectedIndex = 20;
        var scroll = TabDescendants(table).OfType<ScrollViewer>().First(); scroll.ScrollToVerticalOffset(250); window.UpdateLayout();
        var offset = scroll.VerticalOffset; Home(); Open(scannerModule); window.UpdateLayout(); AwaitUi(Task.Delay(300));
        Check(table.SelectedIndex == 20 && Math.Abs(scroll.VerticalOffset - offset) < 1 && offset > 0, "tab switching preserves result selection and scroll position");
        Open(scannerModule); Check(window.CloseTab(scannerTab) && window.OpenTabs.Count == 0 && host.Content is HomeView, "closing the last tab returns to home");
        Open(scannerModule); Check(!ReferenceEquals(scannerPage, host.Content) && ((ScannerView)host.Content).Model.Directory == "", "reopening a closed module starts a fresh page");
        Open(disk); var currentDisk = host.Content;
        model.Uninstall(scannerModule); Check(window.OpenTabs.Count == 2 && scannerModule.IsInstalled && ReferenceEquals(currentDisk, host.Content), "bundled applications cannot be uninstalled or lose their open tabs");
        Open(model.Modules.Single(m => m.Id == "memory-release"));
        var memory = window.OpenTabs.Single(t => t.Module.Id == "memory-release");
        Check(!memory.IsBusy && window.CloseTab(memory) && model.Selected == disk, "memory monitoring is closable and close selects an adjacent tab");
        Open(scannerModule); Open(disk); Open(scannerModule);
        Check(model.Favorites.Cast<ModuleItem>().Take(2).SequenceEqual([scannerModule, disk]), "opening an existing app moves it to the front of recent apps without duplicates");
        var recentIds = model.Favorites.Cast<ModuleItem>().Select(m => m.Id).ToArray();
        model.FavoritesExpanded = false; model.AllExpanded = false;
        var restored = new MainViewModel(store, api, accounts, true);
        Check(restored.Favorites.Cast<ModuleItem>().Select(m => m.Id).SequenceEqual(recentIds) && !restored.FavoritesExpanded && !restored.AllExpanded, "recent use order and independent section expansion persist across restart");
        Home(); var home = (HomeView)host.Content;
        Check(((ItemsControl)home.FindName("ApplicationList")).Items.Count == model.Modules.Count && ((ItemsControl)home.FindName("FavoritesList")).Items.Count == recentIds.Length, "home exposes all apps and recently used apps as separate groups");
        scannerModule.IsVisible = false;
        Check(!((ItemsControl)home.FindName("ApplicationList")).Items.Contains(scannerModule) && !((ItemsControl)home.FindName("FavoritesList")).Items.Contains(scannerModule), "hidden app disappears from both home sections");
        var hiddenRestored = new MainViewModel(store, api, accounts, true);
        Check(!hiddenRestored.Modules.Single(m => m.Id == scannerModule.Id).IsVisible && scannerModule.IsInstalled && window.OpenTabs.Any(t => t.Module == scannerModule), "home visibility persists without removing bundled app or open tab");
        scannerModule.IsVisible = true;
        Check(((ItemsControl)home.FindName("ApplicationList")).Items.Contains(scannerModule) && model.Favorites.Cast<ModuleItem>().First() == scannerModule, "showing app restores home entry and existing recent order");
        model.FavoritesExpanded = model.AllExpanded = true;
        foreach (var module in model.Modules) Open(module);
        Home(); AwaitUi(Task.Delay(300));
        var cards = TabDescendants(home).OfType<Grid>().Where(g => g.RenderTransform is TranslateTransform && g.Height == 96).ToArray();
        Check(cards.Length > 1, "home creates compact interactive app cards");
        var card = cards[0]; var otherCard = cards[1];
        var otherTransform = otherCard.RenderTransform;
        // Reproduce WPF's template freezing even if its optimization changes in a future runtime.
        var frozenTransform = new TranslateTransform(); frozenTransform.Freeze(); card.RenderTransform = frozenTransform;
        card.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        AwaitUi(Task.Delay(240));
        Check(!SystemParameters.ClientAreaAnimation || (!card.RenderTransform.IsFrozen && Math.Abs(((TranslateTransform)card.RenderTransform).Y + 3) < 0.01), "hover safely animates a frozen template transform");
        Check(ReferenceEquals(otherCard.RenderTransform, otherTransform) && ((TranslateTransform)otherTransform).Y == 0, "hover leaves other card transforms unchanged");
        for (var i = 0; i < 5; i++)
        {
            card.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
            card.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        }
        card.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
        AwaitUi(Task.Delay(240));
        Check(Math.Abs(((TranslateTransform)card.RenderTransform).Y) < 0.01, "rapid hover transitions settle back to the original position");
        Render((FrameworkElement)window.Content, Path.Combine(preview, "tabs-home.png"), 1180, 820);
        var recentOrder = model.Favorites.Cast<ModuleItem>().ToArray();
        model.Move(model.Modules[0], 1);
        Check(model.Favorites.Cast<ModuleItem>().SequenceEqual(recentOrder), "manual all-app ordering does not change recent use ordering");
        window.Close();

        var taskModel = new CancellablePreviewModel(); var page = new ToolPage(taskModel, "预览测试", "", new StackPanel(), "生成预览", taskModel.Start);
        var taskHost = new Window { Content = page, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 }; taskHost.Show();
        RunUi(async () =>
        {
            var task = taskModel.Start(); taskHost.UpdateLayout();
            var stop = TabDescendants(page).OfType<Button>().Single(b => Equals(b.Content, "停止") && b.IsVisible);
            Check(stop.IsEnabled && !taskModel.ResultsLayout, "first-run preview exposes a visible enabled stop button");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await task;
            Check(!taskModel.IsBusy, "first-run stop cancels preview work");
        });
        taskHost.Close();
    }
    private sealed class CancellablePreviewModel : ToolModel { public Task Start() => Run(token => Task.Delay(Timeout.Infinite, token)); }
    private static IEnumerable<DependencyObject> TabDescendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in TabDescendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
