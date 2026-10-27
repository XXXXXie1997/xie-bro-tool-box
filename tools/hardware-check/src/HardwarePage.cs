using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XieBox.Contracts;
using XieBox.Platform.UI;

namespace XieBox.HardwareCheck;

public static class HardwarePage
{
    public static ToolPage Create(IPlatformContext context, IHardwareSource? source = null)
    {
        var model = new HardwareModel(context, source ?? new HardwareSource());
        var page = new ToolPage(model, "硬件信息", "查看硬件配置、温度与运行状态。", new Grid(), "查看硬件信息", () => { model.Start(); return Task.CompletedTask; });
        page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBox.HardwareCheck;component/HardwareStyles.xaml", UriKind.Relative) });
        var refresh = ToolPage.Button("重新读取", model.Refresh); refresh.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(model.CanRefresh))); page.Actions.Children.Add(refresh);
        var export = ToolPage.Button("导出报告", () => ReportExporter.Save(Window.GetWindow(page), model, model.Message)); export.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(model.CanExport))); page.Actions.Children.Add(export);
        var copy = ToolPage.Button("复制配置", () => { try { Clipboard.SetText(model.BuildReport()); model.Message("配置已复制。"); } catch (System.Runtime.InteropServices.COMException) { model.Message("剪贴板暂不可用，请重试。"); } }); copy.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(model.CanExport))); page.Actions.Children.Add(copy);
        var body = new Grid();
        var right = body;
        var expanded = new Dictionary<string, bool>();
        right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new());
        var summary = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var summaryLine = new WrapPanel(); summary.Children.Add(summaryLine);
        var state = ToolPage.Text(""); state.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.SamplingStatus))); summaryLine.Children.Add(state);
        var time = ToolPage.Text("", 12); time.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); time.Margin = new Thickness(16, 0, 0, 0); time.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.CaptureLabel))); summaryLine.Children.Add(time); right.Children.Add(summary);
        var loading = new StackPanel { Margin = new Thickness(0, 8, 0, 2) };
        loading.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(model.IsReading)) { Converter = new BooleanToVisibilityConverter() });
        var progress = new ProgressBar { Height = 4, IsIndeterminate = true, HorizontalAlignment = HorizontalAlignment.Stretch, BorderThickness = new Thickness(0) };
        progress.SetResourceReference(Control.ForegroundProperty, "AccentInkBrush"); progress.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); loading.Children.Add(progress);
        var reading = ToolPage.Text("", 12); reading.Margin = new Thickness(0, 6, 0, 0); reading.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.Status))); reading.SetResourceReference(FrameworkElement.StyleProperty, "StatusText"); loading.Children.Add(reading); summary.Children.Add(loading);
        var cards = new WrapPanel { ItemWidth = 250, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); right.Children.Add(scroll);
        scroll.SizeChanged += (_, _) => { var width = Math.Max(1, scroll.ActualWidth - 18); cards.ItemWidth = width / Math.Max(1, (int)(width / 240)); };
        DialogWindow? detailDialog = null;
        StackPanel? detailBody = null;
        string? activeCategory = null;
        Window? host = null;
        page.SetResultsContent(body);
        var sensitive = new CheckBox { Content = "显示 / 导出设备序列号", Margin = new Thickness(0, 8, 0, 0) }; sensitive.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(model.ShowSensitive)) { Mode = BindingMode.TwoWay }); sensitive.Checked += (_, _) => Render(); sensitive.Unchecked += (_, _) => Render(); page.FooterActions.Children.Add(sensitive);
        model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(model.VisibleSections) or nameof(model.DeviceLayoutVersion)) Render(); };
        void ActiveChanged(object? sender, EventArgs e) => model.SetActive(page.IsVisible && (host?.IsActive == true && host.WindowState != WindowState.Minimized || detailDialog?.IsActive == true && detailDialog.WindowState != WindowState.Minimized));
        Render();
        page.Loaded += (_, _) =>
        {
            host = Window.GetWindow(page);
            if (host is not null) { host.Activated += ActiveChanged; host.Deactivated += ActiveChanged; host.StateChanged += ActiveChanged; }
            ActiveChanged(null, EventArgs.Empty);
        };
        page.Unloaded += (_, _) =>
        {
            if (host is not null) { host.Activated -= ActiveChanged; host.Deactivated -= ActiveChanged; host.StateChanged -= ActiveChanged; }
            host = null; model.SetActive(false);
            detailDialog?.Close();
        };
        page.IsVisibleChanged += (_, _) => ActiveChanged(null, EventArgs.Empty);
        return page;

        Expander Expand(string key, object title, object content, bool initial = false)
        {
            var item = new Expander { Header = title, Content = content, IsExpanded = expanded.GetValueOrDefault(key, initial), HorizontalContentAlignment = HorizontalAlignment.Stretch, Style = (Style)page.FindResource("HardwareExpander") };
            item.Expanded += (_, e) => { if (ReferenceEquals(e.OriginalSource, item)) expanded[key] = true; };
            item.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, item)) expanded[key] = false; };
            return item;
        }
        void Render()
        {
            cards.Children.Clear();
            detailBody?.Children.Clear();
            if (model.Snapshot is null) return;
            foreach (var group in model.VisibleSections.GroupBy(s => s.Category))
            {
                var devices = HardwareDevices.Group(group.Key, group, model.Sensors);
                // Empty-query placeholders are not devices. Keep failures visible, and
                // retain a category if a sensor source did discover actual hardware.
                if (devices.All(d => d.Sensors.Count == 0 && d.Fields.Count > 0 &&
                    d.Fields.All(f => f.Name == "读取状态" &&
                        (f.Value == "未检测到电池。" || f.Value.Trim() == "未检测到设备或设备未提供信息。")))) continue;
                var content = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                if (activeCategory == group.Key) foreach (var device in devices)
                {
                    var details = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
                    foreach (var field in device.Fields)
                    {
                        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(180) }); row.ColumnDefinitions.Add(new());
                        var label = ToolPage.Text(field.Name, 12); label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); row.Children.Add(label);
                        var value = ToolPage.Text(field.Sensitive && !model.ShowSensitive ? "已隐藏" : field.Value); Grid.SetColumn(value, 1); row.Children.Add(value); details.Children.Add(row);
                    }
                    if (device.Sensors.Count > 0)
                    {
                        var label = ToolPage.Text("实时状态", 12, true); label.Margin = new Thickness(0, 8, 0, 6); details.Children.Add(label);
                        var table = new DataGrid { ItemsSource = device.Sensors, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, MaxHeight = 280, RowHeight = 32, ColumnHeaderHeight = 32 };
                        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), (Style)page.FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)));
                        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 6, 13, 6))); table.ColumnHeaderStyle = headerStyle;
                        var rowStyle = new Style(typeof(DataGridRow), (Style)page.FindResource(typeof(DataGridRow))); rowStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d)); table.RowStyle = rowStyle;
                        var cellStyle = new Style(typeof(DataGridCell), (Style)page.FindResource(typeof(DataGridCell))); cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 4, 13, 4))); table.CellStyle = cellStyle;
                        foreach (var (title, property, width) in new[] { ("状态项目", "Name", 2d), ("类型", "Kind", 1d), ("当前值", "Value", 1d) })
                            table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(width, DataGridLengthUnitType.Star), ElementStyle = (Style)page.FindResource("TableText") });
                        details.Children.Add(table);
                    }
                    var heading = new DockPanel { LastChildFill = true };
                    var live = device.Sensors.FirstOrDefault(s => s.Kind == "温度" && s.Value != HardwareInventory.Unknown);
                    if (live is not null)
                    {
                        var value = ToolPage.Text("", 12); value.Margin = new Thickness(16, 0, 8, 0); value.SetResourceReference(TextBlock.ForegroundProperty, "AccentInkBrush"); value.SetBinding(TextBlock.TextProperty, new Binding(nameof(SensorRow.Value)) { Source = live }); DockPanel.SetDock(value, Dock.Right); heading.Children.Add(value);
                    }
                    heading.Children.Add(ToolPage.Text(device.Title, 14, true));
                    var deviceCard = new Border { Child = Expand(group.Key + "/" + device.Key, heading, details, true), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10), ClipToBounds = true };
                    deviceCard.SetResourceReference(Border.BackgroundProperty, "CanvasBrush"); deviceCard.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                    content.Children.Add(deviceCard);
                }
                var icon = group.Key switch
                {
                    "处理器" => "memory-release", "主板与 BIOS" => "hardware-board",
                    "内存" => "hardware-memory", "显卡" => "hardware-gpu", "存储" => "disk-analyzer",
                    "显示器" => "hardware-monitor", "网络与声音" => "network-diagnostics",
                    "电池" => "hardware-battery", "设备与驱动" => "hardware-devices", _ => "hardware-check"
                };
                var tile = new Grid(); tile.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); tile.ColumnDefinitions.Add(new());
                tile.Children.Add(new ModuleIcon { ModuleId = icon, Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, 1); tile.Children.Add(text);
                text.Children.Add(ToolPage.Text(group.Key, 14, true));
                var hasOnlyStatus = devices.All(d => d.Sensors.Count == 0 && d.Fields.All(f => f.Name == "读取状态"));
                var categoryStatus = hasOnlyStatus
                    ? (devices.SelectMany(d => d.Fields).Any(f => f.Value == "未检测到电池。") ? "未检测到电池" : "暂无设备信息")
                    : $"{devices.Count} 个设备 / 项目";
                var subtitle = ToolPage.Text(categoryStatus, 12); subtitle.Margin = new Thickness(0, 4, 0, 0); subtitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); text.Children.Add(subtitle);
                var card = new Button { Content = tile, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 12, 14, 12), MinHeight = 80, ToolTip = "查看" + group.Key + "的配置与实时状态" };
                var category = group.Key;
                card.Click += (_, _) => ShowCategory(category);
                cards.Children.Add(card);
                if (activeCategory == category) detailBody?.Children.Add(content);
            }
        }
        void ShowCategory(string category)
        {
            if (detailDialog is not null) return;
            activeCategory = category;
            detailBody = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };
            var viewer = new ScrollViewer { Content = detailBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            detailDialog = new DialogWindow { Owner = Window.GetWindow(page), Title = category + " · 硬件信息", Width = Math.Min(820, SystemParameters.WorkArea.Width - 48), Height = Math.Min(640, SystemParameters.WorkArea.Height - 64), Content = viewer };
            detailDialog.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBox.HardwareCheck;component/HardwareStyles.xaml", UriKind.Relative) });
            detailDialog.Activated += ActiveChanged; detailDialog.Deactivated += ActiveChanged; detailDialog.StateChanged += ActiveChanged;
            Render();
            try { detailDialog.ShowDialog(); }
            finally { detailDialog = null; detailBody = null; activeCategory = null; ActiveChanged(null, EventArgs.Empty); }
        }
    }
}
