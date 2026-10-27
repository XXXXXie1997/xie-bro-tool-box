using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XieBox.Contracts;
using XieBox.Platform.UI;

namespace XieBox.HealthCheck;

public sealed class HealthPanel(HealthModel model) : UserControl
{ public HealthModel Model { get; } = model; }

public static class HealthPage
{
    public static HealthPanel Create(IPlatformContext context, string cleanerState, Action<HealthResult> navigate, IReadOnlyList<HealthProbe>? probes = null)
    {
        var model = new HealthModel(context, probes ?? HealthProbes.Create(cleanerState));
        var page = new HealthPanel(model) { DataContext = model };
        var root = new Grid { Margin = new Thickness(24, 16, 24, 12) }; page.Content = root;
        var intro = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(intro);
        intro.Children.Add(ToolPage.Text("电脑体检", 34, true, true));
        var scope = ToolPage.Text("检查硬件、磁盘空间和网络，验证屏幕、声音及 USB 接口。体检不会修改设置或清理文件。", 13, false, true);
        scope.Margin = new Thickness(0, 14, 0, 0);
        scope.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); intro.Children.Add(scope);
        var begin = ToolPage.Button("开始体检", async () => await model.CheckAsync(), true);
        begin.Width = 200; begin.MinHeight = 54; begin.FontSize = 18; begin.HorizontalAlignment = HorizontalAlignment.Center; begin.Margin = new Thickness(0, 28, 0, 0); intro.Children.Add(begin);
        var introStatus = ToolPage.Text("", 12, false, true); introStatus.SetResourceReference(FrameworkElement.StyleProperty, "StatusText"); introStatus.SetBinding(TextBlock.TextProperty, new Binding("Status")); introStatus.Margin = new Thickness(0, 12, 0, 0); intro.Children.Add(introStatus);

        var results = new Grid { Visibility = Visibility.Collapsed }; root.Children.Add(results);
        results.RowDefinitions.Add(new() { Height = GridLength.Auto }); results.RowDefinitions.Add(new() { Height = GridLength.Auto }); results.RowDefinitions.Add(new()); results.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; results.Children.Add(header);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var stop = ToolPage.Button("停止", model.Cancel); stop.SetBinding(UIElement.IsEnabledProperty, new Binding("IsBusy")); stop.SetBinding(UIElement.VisibilityProperty, new Binding("IsBusy") { Converter = new BooleanToVisibilityConverter() }); actions.Children.Add(stop);
        actions.Children.Add(ToolPage.Button("硬件信息", () => navigate(new HealthResult("硬件信息", "hardware-check", "", "", HealthState.Information))));
        actions.Children.Add(ToolPage.Button("重新体检", async () => await model.CheckAsync()));
        var export = ToolPage.Button("导出报告", () => ReportExporter.Save(Window.GetWindow(page), model, model.Message)); export.SetBinding(UIElement.IsEnabledProperty, new Binding("CanExport")); actions.Children.Add(export);
        header.Children.Add(ToolPage.Text("电脑体检", 22, true));
        var summary = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(summary, 1); results.Children.Add(summary);
        var heading = ToolPage.Text("", 14, true); heading.SetBinding(TextBlock.TextProperty, new Binding("Heading")); summary.Children.Add(heading);
        var counts = ToolPage.Text("", 12); counts.Margin = new Thickness(0, 6, 0, 0); counts.SetBinding(TextBlock.TextProperty, new Binding("Summary")); counts.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); summary.Children.Add(counts);
        var progressRow = new Grid { Margin = new Thickness(0, 10, 0, 0) }; progressRow.ColumnDefinitions.Add(new()); progressRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        progressRow.SetBinding(UIElement.VisibilityProperty, new Binding("IsBusy") { Converter = new BooleanToVisibilityConverter() }); summary.Children.Add(progressRow);
        var progress = new ProgressBar { Height = 5, Maximum = 100, VerticalAlignment = VerticalAlignment.Center }; progress.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding("Progress") { Mode = BindingMode.OneWay }); progressRow.Children.Add(progress);
        var percent = ToolPage.Text("", 12); percent.Margin = new Thickness(12, 0, 0, 0); percent.SetBinding(TextBlock.TextProperty, new Binding("Progress") { StringFormat = "{0:0}%" }); Grid.SetColumn(percent, 1); progressRow.Children.Add(percent);
        var list = new StackPanel(); var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 2); results.Children.Add(scroll);
        var status = ToolPage.Text("", 12); status.Margin = new Thickness(0, 10, 0, 0); status.SetResourceReference(FrameworkElement.StyleProperty, "StatusText"); status.SetBinding(TextBlock.TextProperty, new Binding("Status")); Grid.SetRow(status, 3); results.Children.Add(status);
        void Render()
        {
            list.Children.Clear();
            Section("自动检查", "依据系统当前返回的信息判断；未进行压力测试、完整内存读写或全盘坏块检测。");
            foreach (var result in model.Results) AddRow(result);
            Section("功能验证", "选择已连接的设备进行测试。连接新设备后可重新检测。");
            foreach (var item in GuidedChecks.Items)
            {
                var result = model.ManualResults.Single(r => r.Name == item.Title);
                if (model.CanVerify(item.Kind))
                    AddRow(result with { Description = result.State == HealthState.Pending ? item.Description : result.Description }, () => GuidedChecks.Show(Window.GetWindow(page), item, model), result.State == HealthState.Pending ? "开始验证" : "重新验证");
                else
                    AddRow(result, async () => await model.RefreshDevicesAsync(), "重新检测设备");
            }
        }
        void Section(string title, string description)
        {
            var text = ToolPage.Text(title, 16, true); text.Margin = new Thickness(0, 12, 0, 6); list.Children.Add(text);
            var note = ToolPage.Text(description, 12); note.Margin = new Thickness(0, 0, 0, 10); note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); list.Children.Add(note);
        }
        void AddRow(HealthResult result, Action? action = null, string actionLabel = "")
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; row.Children.Add(text);
            var title = new WrapPanel(); title.Children.Add(ToolPage.Text(result.Name, 14, true));
            var state = ToolPage.Text(HealthModel.StateName(result.State), 12); state.Margin = new Thickness(12, 0, 0, 0);
            state.SetResourceReference(TextBlock.ForegroundProperty, result.State switch { HealthState.Issue => "ErrorBrush", HealthState.Suggested => "WarningBrush", HealthState.Normal => "SuccessBrush", _ => "MutedBrush" }); title.Children.Add(state); text.Children.Add(title);
            var lines = result.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var description = ToolPage.Text(lines.FirstOrDefault() ?? "", 12); description.Margin = new Thickness(0, 6, 0, 0); description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); text.Children.Add(description);
            if (!string.IsNullOrWhiteSpace(result.NextStep))
            {
                var next = ToolPage.Text("下一步：" + result.NextStep, 12); next.Margin = new Thickness(0, 6, 0, 0); text.Children.Add(next);
            }
            if (lines.Length > 1)
            {
                var detail = ToolPage.Text(string.Join("\n", lines.Skip(1)), 12);
                detail.Margin = new Thickness(0, 6, 0, 0); detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                text.Children.Add(new Expander { Header = "查看依据与详情", Content = detail, Margin = new Thickness(0, 6, 0, 0) });
            }
            if (action is not null)
            {
                var button = ToolPage.Button(actionLabel, action); button.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(button, 1); row.Children.Add(button);
            }
            var card = new Border { Child = row, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); card.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); list.Children.Add(card);
        }
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(model.Results) or nameof(model.ManualResults)) Render();
            if (e.PropertyName == nameof(model.ResultsLayout)) { intro.Visibility = model.ResultsLayout ? Visibility.Collapsed : Visibility.Visible; results.Visibility = model.ResultsLayout ? Visibility.Visible : Visibility.Collapsed; }
            if (e.PropertyName == nameof(model.IsIdle)) model.Notify(nameof(model.CanExport));
        };
        page.Unloaded += (_, _) => model.Cancel();
        return page;
    }
}
