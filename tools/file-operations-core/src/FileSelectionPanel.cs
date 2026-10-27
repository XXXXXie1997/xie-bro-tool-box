using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using XieBox.Platform.UI;

namespace XieBox.FileOperations;

public sealed class FileSelectionPanel : StackPanel
{
    public FileSelectionPanel(FileTaskModel model)
    {
        SetBinding(IsEnabledProperty, new Binding(nameof(model.IsIdle)) { Source = model });
        var drop = new DirectoryDropZone(files: true);
        drop.FilesSelected += model.AddFiles;
        Children.Add(drop);
        var count = ToolPage.Text("", 12); count.Margin = new Thickness(0, 8, 0, 8); Children.Add(count);
        var list = new ListBox { MaxHeight = 160, SelectionMode = SelectionMode.Extended, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var template = new DataTemplate();
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding()); label.SetBinding(ToolTipProperty, new Binding()); label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); label.SetValue(MarginProperty, new Thickness(4, 6, 4, 6)); template.VisualTree = label;
        list.ItemTemplate = template; Children.Add(list);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 8) };
        actions.Children.Add(ToolPage.Button("移除选中", () => model.RemoveFiles(list.SelectedItems.Cast<string>().ToArray())));
        actions.Children.Add(ToolPage.Button("清空", () => model.RemoveFiles(model.SelectedFiles.ToArray()))); Children.Add(actions);
        void Update() { list.ItemsSource = model.SelectedFiles; count.Text = $"已选择 {model.SelectedFiles.Count} 个文件 · 可继续追加，重复文件自动忽略"; list.Visibility = model.SelectedFiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible; }
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.SelectedFiles)) Update(); }; Update();
    }
}
