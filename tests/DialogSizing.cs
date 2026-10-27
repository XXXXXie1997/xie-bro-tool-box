using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XieBox.Platform.UI;

internal static partial class Program
{
    private static void CheckContentDialogSizing()
    {
        var owner = new Window { Width = 900, Height = 700, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        owner.Show();
        try
        {
            double Measure(string message, string preview)
            {
                double height = 0;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.Current.Windows.OfType<DialogWindow>().FirstOrDefault(w => w.Title == "确认尺寸验证");
                    if (dialog is null) return;
                    timer.Stop(); dialog.UpdateLayout(); height = dialog.ActualHeight;
                    Render((FrameworkElement)dialog.Content, System.IO.Path.Combine("artifacts", "previews", preview), dialog.ActualWidth - 2, dialog.ActualHeight - 42);
                    dialog.Close();
                };
                timer.Start();
                try { DialogWindow.Confirm(owner, message, "确认尺寸验证", "确认"); }
                finally { timer.Stop(); }
                return height;
            }
            var shortHeight = Measure("将按上次操作记录恢复文件名称和位置。若文件已变化或原位置已被占用，会停止并保留文件。", "confirm-compact.png");
            var longHeight = Measure(string.Join("\n", Enumerable.Repeat("较长的确认说明，内容超过可用空间时应滚动显示。", 60)), "confirm-long.png");
            Check(shortHeight < 260 && shortHeight > 140, "short confirmations size to their content without large blank space");
            Check(longHeight > shortHeight && longHeight <= Math.Min(600, SystemParameters.WorkArea.Height - 80) + 1, "long confirmations grow within the screen limit");
        }
        finally { owner.Close(); }
    }
}
