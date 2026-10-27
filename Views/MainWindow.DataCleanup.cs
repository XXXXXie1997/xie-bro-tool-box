using System.Windows;
using System.Windows.Controls;
using XieBox.Platform.UI;
using XieBox.Services;

namespace XieBox.Views;

public partial class MainWindow
{
    private void ShowDataCleanup(Window about)
    {
        var cleaner = new PersonalDataCleanup(_model.DataRoot);
        CleanupGroup[] groups;
        try { groups = cleaner.Preview(); }
        catch (Exception error) { MessageBox.Show(about, "无法读取应用数据：" + error.Message, "蟹小箱"); return; }
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "清除本机应用数据", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Description("仅清理当前数据目录中蟹小箱创建的设置和记录，不删除你的文件，不还原系统设置。清理后退出应用。"));
        var checks = groups.Select(group => new CheckBox
        {
            Content = $"{group.Name} · {group.Bytes / 1024d:N1} KB",
            IsChecked = !group.IsRecovery, Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center
        }).ToArray();
        foreach (var check in checks) panel.Children.Add(check);
        var warning = Description("清除撤销记录后，将无法撤销之前的文件整理和重命名操作。");
        warning.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush"); panel.Children.Add(warning);
        var path = Description(_model.DataRoot); path.TextWrapping = TextWrapping.Wrap; panel.Children.Add(path);
        var status = Description(""); status.TextWrapping = TextWrapping.Wrap; panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "取消", Padding = new Thickness(20, 9, 20, 9), Margin = new Thickness(0, 0, 10, 0) };
        var confirm = new Button { Content = "清理并退出", Padding = new Thickness(20, 9, 20, 9) };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons);
        var dialog = new DialogWindow { Owner = about, Title = "清除应用数据", Width = 580, Content = panel }.FitContent();
        var waiting = false;
        dialog.Closing += (_, e) => { if (waiting) e.Cancel = true; };
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += async (_, _) =>
        {
            var selected = checks.Select((check, i) => (check, i)).Where(x => x.check.IsChecked == true).Select(x => x.i).ToArray();
            if (selected.Length == 0) { status.Text = "请选择要清理的数据。"; return; }
            waiting = true; confirm.IsEnabled = cancel.IsEnabled = false;
            foreach (var check in checks) check.IsEnabled = false;
            status.Text = "正在停止后台任务，请稍候……";
            CancelTasks();
            while (_model.IsBusy) await Task.Delay(100);
            ((App)Application.Current).CleanupOnExit = () =>
            {
                try
                {
                    var failures = cleaner.Clear(selected);
                    if (failures.Length > 0) MessageBox.Show("部分数据未能清除：\n" + string.Join("\n", failures), "蟹小箱", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception error) { MessageBox.Show("应用已退出，但数据清理未完成：" + error.Message, "蟹小箱", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            waiting = false; dialog.Close(); about.Close(); RequestExit();
        };
        dialog.ShowDialog();
    }
}
