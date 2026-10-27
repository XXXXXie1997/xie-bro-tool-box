using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using XieBox.Platform.UI;

namespace XieBox.FileOperations;

public sealed class OrganizerPreview : UserControl
{
    private readonly StackPanel _groups = new();
    private readonly FileTaskModel _model;
    public OrganizerPreview(FileTaskModel model)
    {
        _model = model;
        Content = new ScrollViewer { Content = _groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Entries)) Render(); };
        Render();
    }
    private void Render()
    {
        _groups.Children.Clear();
        if (_model.Entries.Count == 0) { _groups.Children.Add(ToolPage.Text("暂无待整理文件。")); return; }
        foreach (var group in _model.Entries.GroupBy(e => Path.GetDirectoryName(e.Destination)!, StringComparer.OrdinalIgnoreCase))
        {
            var files = group.ToArray();
            var folder = Path.GetFileName(group.Key);
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(3, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(88) });
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
            var source = new StackPanel(); layout.Children.Add(source);
            var title = ToolPage.Text($"{folder}文件 · {files.Length:N0} 个", 14, true); title.Margin = new Thickness(0, 0, 0, 8); source.Children.Add(title);
            var list = new ListBox { ItemsSource = files.Take(4), BorderThickness = new Thickness(0), Background = Brushes.Transparent, MaxHeight = 256, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), ItemTemplate = FileTemplate(folder) };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            source.Children.Add(list);
            if (files.Length > 4)
            {
                var expanded = false;
                var more = new Button { Content = $"查看全部 {files.Length:N0} 个文件", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                more.SetResourceReference(StyleProperty, "FlatButton");
                more.Click += (_, _) => { expanded = !expanded; list.ItemsSource = expanded ? files : files.Take(4); more.Content = expanded ? "收起列表" : $"查看全部 {files.Length:N0} 个文件"; };
                source.Children.Add(more);
            }
            var arrow = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var line = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,10 L48,10 L39,2"), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 50, Height = 16 };
            line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentInkBrush"); arrow.Children.Add(line);
            var caption = ToolPage.Text("归入", 12, false, true); caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); arrow.Children.Add(caption); Grid.SetColumn(arrow, 1); layout.Children.Add(arrow);
            var destination = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 12, 4, 12), ToolTip = group.Key };
            destination.Children.Add(new ModuleIcon { ModuleId = "file-organizer", Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) });
            destination.Children.Add(ToolPage.Text(folder, 17, true));
            var state = ToolPage.Text(Directory.Exists(group.Key) ? "现有文件夹" : "将新建文件夹", 12); state.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); state.Margin = new Thickness(0, 5, 0, 8); destination.Children.Add(state);
            destination.Children.Add(ToolPage.Text($"将移入 {files.Length:N0} 个文件", 13));
            var path = ToolPage.Text(group.Key, 12); path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); path.Margin = new Thickness(0, 6, 0, 0); destination.Children.Add(path);
            Grid.SetColumn(destination, 2); layout.Children.Add(destination);
            var card = new Border { Child = layout, Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); card.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); _groups.Children.Add(card);
        }
    }
    private static DataTemplate FileTemplate(string category)
    {
        var row = new FrameworkElementFactory(typeof(DockPanel)); row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4)); row.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(MoveItem.Source)));
        var icon = new FrameworkElementFactory(typeof(ModuleIcon)); icon.SetValue(ModuleIcon.ModuleIdProperty, category switch { "图片" => "file-image", "视频" => "file-video", "音频" => "file-audio", "压缩包" => "file-archive", _ => "file-document" }); icon.SetValue(FrameworkElement.WidthProperty, 22d); icon.SetValue(FrameworkElement.HeightProperty, 22d); icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)); icon.SetValue(DockPanel.DockProperty, Dock.Left); row.AppendChild(icon);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(nameof(MoveItem.Name))); text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); row.AppendChild(text);
        return new DataTemplate { VisualTree = row };
    }
}
