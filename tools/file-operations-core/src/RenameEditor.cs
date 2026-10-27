using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XieBox.Platform.UI;
namespace XieBox.FileOperations;

public static class RenameEditor
{
    public static void Open(Window? owner, RenameSettings rules)
    {
        var root = new Grid { Margin = new Thickness(24), DataContext = rules };
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBox.FileOperations;component/RenameEditorStyles.xaml", UriKind.Relative) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var modes = new Grid { Margin = new Thickness(0, 0, 0, 16) }; modes.ColumnDefinitions.Add(new()); modes.ColumnDefinitions.Add(new());
        var replaceMode = Button("查找替换", () => rules.Combine = false); var combineMode = Button("自定义组合", () => rules.Combine = true); Grid.SetColumn(combineMode, 1); replaceMode.Margin = new Thickness(0, 0, 6, 0); combineMode.Margin = new Thickness(6, 0, 0, 0); modes.Children.Add(replaceMode); modes.Children.Add(combineMode); root.Children.Add(modes); modes.Visibility = Visibility.Collapsed;
        var content = new StackPanel(); var scroll = new ScrollViewer { Content = content, MaxHeight = Math.Max(160, Math.Min(420, SystemParameters.WorkArea.Height - 280)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var replace = new StackPanel();
        replace.Children.Add(ToolPage.Field("查找文字", ToolPage.Input(nameof(rules.Find)))); replace.Children.Add(ToolPage.Field("替换为（可留空，表示删除匹配内容）", ToolPage.Input(nameof(rules.Replacement))));
        var regex = new CheckBox { Content = "使用正则表达式", Margin = new Thickness(0, 0, 0, 8) }; regex.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(rules.UseRegex)) { Mode = BindingMode.TwoWay }); replace.Children.Add(regex);
        replace.Children.Add(Note("只修改文件名，保留扩展名。启用正则后可用 \\d+ 匹配数字，替换内容支持 $1、$2 等分组引用。")); content.Children.Add(replace);
        var combine = new StackPanel();
        combine.Children.Add(Note("从上到下依次拼接，可重复添加文字、流水号或日期。扩展名自动保留。"));
        var toolbar = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        foreach (var (label, kind, value) in new[] { ("＋文字", "text", ""), ("＋流水号", "number", ""), ("＋日期", "date", "yyyyMMdd"), ("＋原文件名", "original", "") })
        {
            var add = Button(label, () => { if (rules.Parts.Count < 16) rules.Parts.Add(new(kind, value)); }); add.Margin = new Thickness(0, 0, 8, 4); toolbar.Children.Add(add);
        }
        combine.Children.Add(toolbar);
        var parts = new StackPanel(); combine.Children.Add(parts);
        combine.Children.Add(Note("流水号按原文件名排序递增。当前日期使用生成预览时的时间；创建和修改日期按各文件读取，以本地时间显示。时间戳为 Unix 时间。最多 16 个片段。")); content.Children.Add(combine);
        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetRow(footer, 2);
        var footerFrame = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0) }; footerFrame.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); Grid.SetRow(footerFrame, 2); root.Children.Add(footerFrame);
        var example = new StackPanel { Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center }; footer.Children.Add(example);
        example.Children.Add(ToolPage.Text("文件名示例（创建与修改日期使用示例值）", 12, true)); var sample = ToolPage.Text(""); sample.Margin = new Thickness(0, 6, 0, 0); sample.SetResourceReference(FrameworkElement.StyleProperty, "StatusText"); sample.SetBinding(TextBlock.TextProperty, new Binding(nameof(rules.Example))); example.Children.Add(sample);
        var dialog = new DialogWindow { Owner = owner, Title = "文件名编辑器", Width = 760, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 48, MaxWidth = SystemParameters.WorkArea.Width - 32, Content = root, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        // Reserve space using measured header/footer heights instead of assuming a fixed font size.
        root.LayoutUpdated += (_, _) =>
        {
            var available = Math.Max(80, dialog.MaxHeight - root.Margin.Top - root.Margin.Bottom - 48 - modes.ActualHeight - modes.Margin.Top - modes.Margin.Bottom - footerFrame.ActualHeight - footerFrame.Margin.Top - footerFrame.Margin.Bottom);
            var limit = Math.Min(420, available);
            if (Math.Abs(scroll.MaxHeight - limit) > 1) scroll.MaxHeight = limit;
        };
        var done = Button("完成", () => dialog.Close()); done.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton"); done.HorizontalAlignment = HorizontalAlignment.Right; done.MinWidth = 96; done.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(done, 1); footer.Children.Add(done);
        using var drag = new NamePartDrag(root, parts, scroll, rules); drag.Initialize();
        void Modes()
        {
            replace.Visibility = Visibility.Collapsed; combine.Visibility = Visibility.Visible;
        }
        void Parts()
        {
            parts.Children.Clear();
            foreach (var part in rules.Parts)
            {
                var row = new Grid { Margin = new Thickness(0), MinHeight = 36, DataContext = part };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(32) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(92) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var handle = new Border { Width = 26, MinHeight = 32, VerticalAlignment = VerticalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent, CornerRadius = new CornerRadius(6), FocusVisualStyle = null };
                var bars = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                for (var line = 0; line < 3; line++) { var bar = new Border { Width = 14, Height = 1.5, Margin = new Thickness(0, 2, 0, 0), CornerRadius = new CornerRadius(1) }; bar.SetResourceReference(Border.BackgroundProperty, "MutedBrush"); bars.Children.Add(bar); }
                handle.Child = bars; row.Children.Add(handle);
                var partTitle = ToolPage.Text(part.Label, 12); Grid.SetColumn(partTitle, 1); row.Children.Add(partTitle);
                FrameworkElement input;
                if (part.Kind == "text") input = ToolPage.Input(nameof(part.Value));
                else if (part.Kind is "date" or "created" or "modified")
                {
                    var dateOptions = new Grid(); dateOptions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); dateOptions.ColumnDefinitions.Add(new() { Width = new GridLength(1.5, GridUnitType.Star) });
                    var source = new ComboBox { ItemsSource = RenameSettings.Sources, DisplayMemberPath = "Label", SelectedValuePath = "Format", Margin = new Thickness(0, 0, 8, 0) };
                    source.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(part.DateSource)) { Mode = BindingMode.TwoWay }); dateOptions.Children.Add(source);
                    var format = new ComboBox { ItemsSource = RenameSettings.Dates, DisplayMemberPath = "Label", SelectedValuePath = "Format" };
                    format.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(part.Value)) { Mode = BindingMode.TwoWay }); Grid.SetColumn(format, 1); dateOptions.Children.Add(format); input = dateOptions;
                }
                else if (part.Kind == "number")
                {
                    var fields = new Grid();
                    var definitions = new[] { ("起始值", nameof(part.Start)), ("步长", nameof(part.Step)), ("补零位数", nameof(part.Digits)) };
                    for (var i = 0; i < definitions.Length; i++)
                    {
                        fields.ColumnDefinitions.Add(new());
                        var field = ToolPage.Field(definitions[i].Item1, ToolPage.Input(definitions[i].Item2));
                        field.Margin = new Thickness(i == 0 ? 0 : 5, 0, i == 2 ? 0 : 5, 0); Grid.SetColumn(field, i); fields.Children.Add(field);
                    }
                    var settings = new Expander { Content = fields, Margin = new Thickness(0, 4, 0, 4) };
                    settings.SetBinding(HeaderedContentControl.HeaderProperty, new Binding(nameof(part.NumberPreview)) { StringFormat = "{0} · 编号设置" }); input = settings;
                }
                else input = Note("保留原文件名，不含扩展名");
                Grid.SetColumn(input, 2); row.Children.Add(input);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                foreach (var (label, action) in new (string, Action)[] { ("×", () => rules.Parts.Remove(part)) }) { var b = Button(label, action); b.MinWidth = 32; b.MinHeight = 32; b.Padding = new Thickness(6, 4, 6, 4); b.Margin = new Thickness(4, 0, 0, 0); b.IsEnabled = label == "×" || (label == "↑" ? rules.Parts.IndexOf(part) > 0 : rules.Parts.IndexOf(part) < rules.Parts.Count - 1); b.ToolTip = label == "×" ? "移除此片段" : label == "↑" ? "向前移动" : "向后移动"; actions.Children.Add(b); }
                Grid.SetColumn(actions, 3); row.Children.Add(actions);
                var frame = new Border { Child = row, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) }; frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); parts.Children.Add(frame); drag.Attach(handle, frame, part);
            }
        }
        System.ComponentModel.PropertyChangedEventHandler modeChanged = (_, e) => { if (e.PropertyName == nameof(rules.Combine)) Modes(); };
        System.Collections.Specialized.NotifyCollectionChangedEventHandler partsChanged = (_, _) => Parts();
        rules.PropertyChanged += modeChanged; rules.Parts.CollectionChanged += partsChanged;
        try { Modes(); Parts(); dialog.ShowDialog(); }
        finally { rules.PropertyChanged -= modeChanged; rules.Parts.CollectionChanged -= partsChanged; }
    }
    private static Button Button(string text, Action action) { var b = new Button { Content = text, MinHeight = 36, FocusVisualStyle = null }; b.Click += (_, _) => action(); return b; }
    private static TextBlock Note(string text) { var note = ToolPage.Text(text, 12); note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); return note; }
}
