using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.Views;

internal static partial class Program
{
    private static void CheckManageOrdering(string root, string preview, PlatformApi api)
    {
        var store = new LocalStore(Path.Combine(root, "manage-order"));
        var accounts = new AccountService(api, store);
        var model = new MainViewModel(store, api, accounts);
        var first = model.Modules[0]; var last = model.Modules[^1];
        first.IsVisible = false;
        model.MoveToInsertion(first, model.Modules.Count);
        Check(model.Modules[^1] == first, "drag insertion supports moving first item after the last item");
        model.MoveToInsertion(last, 0);
        Check(model.Modules[0] == last, "drag insertion supports moving last item before the first item");
        var unchanged = model.Modules.Select(m => m.Id).ToArray();
        model.MoveToInsertion(last, 1);
        model.MoveToInsertion(last, -1);
        model.MoveToInsertion(last, model.Modules.Count + 1);
        Check(model.Modules.Select(m => m.Id).SequenceEqual(unchanged), "self drops and out-of-range drops do not change order");
        model.SetToolBusy(true); model.MoveToInsertion(last, model.Modules.Count); model.SetToolBusy(false);
        Check(model.Modules[0] == last, "module activity blocks drag reorder");
        var restored = new MainViewModel(store, api, accounts);
        Check(restored.Modules.Select(m => m.Id).SequenceEqual(model.Modules.Select(m => m.Id)) && !restored.Modules.Single(m => m.Id == first.Id).IsVisible,
            "drag order persists without changing hidden navigation preferences");
        Check(restored.VisibleModules.Cast<XieBox.Models.ModuleItem>().First().Id == last.Id, "top navigation follows the new order immediately");
        var view = new ManageView(model, _ => { });
        RenderUtility(view, Path.Combine(preview, "manage-compact.png"));
        var list = (ItemsControl)view.FindName("ApplicationList");
        Check(((FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)).ActualHeight <= 66,
            "management rows use a compact single-line height");
        static IEnumerable<DependencyObject> Descendants(DependencyObject item)
        {
            yield return item;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(item, i))) yield return child;
        }
        Check(Descendants(list).OfType<Button>().All(b => b.Content is not "↑" and not "↓") &&
            Descendants(list).OfType<Border>().Count(b => b.Tag is XieBox.Models.ModuleItem && b.Focusable) == model.Modules.Count,
            "each row has a focusable drag handle and no arrow buttons");
        var host = new Window { Content = view, Width = 1180, Height = 1100, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        host.Show(); AwaitUi(Task.Delay(300));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Invoke(string name, params object[] values) => typeof(ManageView).GetMethod(name, flags)!.Invoke(view, values);
        var dragged = model.Modules[1];
        var sourceRow = (FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(dragged);
        var oldOrder = model.Modules.Select(m => m.Id).ToArray();
        typeof(ManageView).GetField("_dragged", flags)!.SetValue(view, dragged);
        Invoke("BeginPreview", dragged, new Point(20, sourceRow.TranslatePoint(new Point(), list).Y + 24));
        sourceRow.Opacity = 0;
        var target = new Point(28, list.ActualHeight - 24);
        Invoke("UpdatePreviewPosition", target); Invoke("UpdateInsertion", target.Y);
        AwaitUi(Task.Delay(320));
        var layer = (Canvas)view.FindName("DropLayer");
        var previewLayer = (Canvas)view.FindName("PreviewLayer");
        Check(previewLayer.Children.OfType<Image>().Single().Source is not null && layer.Children.OfType<Border>().Count() == 2,
            "drag preview contains a row snapshot and destination placeholder");
        var snapshot = (System.Windows.Media.Imaging.BitmapSource)previewLayer.Children.OfType<Image>().Single().Source;
        var pixels = new byte[snapshot.PixelWidth * snapshot.PixelHeight * 4]; snapshot.CopyPixels(pixels, snapshot.PixelWidth * 4, 0);
        Check(pixels[((snapshot.PixelHeight / 2) * snapshot.PixelWidth + snapshot.PixelWidth / 2) * 4 + 3] > 200,
            "drag snapshot contains visible card pixels instead of a transparent crop");
        Check(model.Modules.Select(m => m.Id).SequenceEqual(oldOrder) &&
            ((FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(2)).RenderTransform is TranslateTransform { Y: < 0 },
            "hover makes adjacent rows move aside without committing module order");
        Render(view, Path.Combine(preview, "manage-drag.png"), 1178, 706);
        Invoke("UpdateDragLocation", new Point(28, list.ActualHeight + 70)); AwaitUi(Task.Delay(320));
        Check(previewLayer.Children.OfType<Image>().Single().Visibility == Visibility.Visible &&
            (int)typeof(ManageView).GetField("_insertionIndex", flags)!.GetValue(view)! == -1 &&
            Math.Abs(((TranslateTransform)((FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(2)).RenderTransform).Y) < 0.1,
            "outside the list keeps a visible floating preview but resets the drop target and adjacent rows");
        Render(view, Path.Combine(preview, "manage-drag-outside.png"), 1178, 706);
        Invoke("UpdateDragLocation", target); AwaitUi(Task.Delay(320));
        Check((int)typeof(ManageView).GetField("_insertionIndex", flags)!.GetValue(view)! >= 0,
            "returning from an invalid region restores the insertion preview");
        Invoke("ResetPreviewOffsets"); Invoke("ClearPreview"); sourceRow.Opacity = 1;
        typeof(ManageView).GetField("_dragged", flags)!.SetValue(view, null);
        AwaitUi(Task.Delay(320));
        Check(previewLayer.Children.OfType<Image>().Count() == 0 && model.Modules.Select(m => m.Id).SequenceEqual(oldOrder) &&
            Math.Abs(((TranslateTransform)((FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(2)).RenderTransform).Y) < 0.1,
            "cancel removes the preview and restores row positions without saving a reorder");
        typeof(ManageView).GetField("_dragged", flags)!.SetValue(view, dragged);
        Invoke("BeginPreview", dragged, new Point(20, sourceRow.TranslatePoint(new Point(), list).Y + 24));
        sourceRow.Opacity = 0;
        Invoke("UpdateDragLocation", target); AwaitUi(Task.Delay(320));
        double RowY(XieBox.Models.ModuleItem item) => ((FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(item)).TranslatePoint(new Point(), list).Y;
        var settled = model.Modules.Where(m => m != dragged).ToDictionary(m => m, RowY);
        Invoke("CommitDrop", target);
        Invoke("UpdateDragLocation", new Point(28, list.ActualHeight + 70));
        Check(settled.All(pair => Math.Abs(RowY(pair.Key) - pair.Value) < 0.5),
            "drop preserves settled neighbouring row positions without replaying hover offsets");
        Invoke("ClearPreview"); sourceRow.Opacity = 1;
        typeof(ManageView).GetField("_dragged", flags)!.SetValue(view, null);
        AwaitUi(Task.Delay(100));
        Check(settled.All(pair => Math.Abs(RowY(pair.Key) - pair.Value) < 0.5) && model.Modules[^1] == dragged,
            "settled neighbours remain stationary during the dragged row landing animation");
        AwaitUi(Task.Delay(220));
        Check(settled.All(pair => Math.Abs(RowY(pair.Key) - pair.Value) < 0.5), "neighbours remain in place after drop animation completes");
        host.Content = null; host.Close();
    }
}

