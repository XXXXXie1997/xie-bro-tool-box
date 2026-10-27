using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace XieBox.Platform.UI;

public sealed class DirectoryDropZone : UserControl
{
    public static readonly DependencyProperty SelectedPathProperty = DependencyProperty.Register(nameof(SelectedPath), typeof(string), typeof(DirectoryDropZone), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DirectoryDropZone)d).UpdatePath()));
    public string SelectedPath { get => (string)GetValue(SelectedPathProperty); set => SetValue(SelectedPathProperty, value); }
    public Func<string, string>? ValidatePath { get; set; }
    public event Action<string>? PathSelected;
    private readonly TextBlock _path = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center, FontSize = 12, Margin = new Thickness(12, 6, 12, 0) };
    private readonly TextBlock _error = new() { Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Border _surface = new() { CornerRadius = new CornerRadius(10), Background = Brushes.Transparent };
    private readonly Rectangle _outline = new() { RadiusX = 10, RadiusY = 10, StrokeThickness = 1.3, StrokeDashArray = new DoubleCollection { 5, 4 }, IsHitTestVisible = false };
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _dragging;
    private readonly bool _files;
    public event Action<string[]>? FilesSelected;
    public DirectoryDropZone() : this(false) { }
    public DirectoryDropZone(bool files)
    {
        _files = files;
        var caption = files ? "拖拽/选择多个文件" : "拖拽/选择磁盘或目录";
        AllowDrop = true;
        var root = new StackPanel(); Content = root;
        var button = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 116, Cursor = Cursors.Hand, FocusVisualStyle = null };
        // Keep the hit area stationary; animate only its inner artwork.
        var template = new ControlTemplate(typeof(Button)); var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); template.VisualTree = presenter; button.Template = template;
        System.Windows.Automation.AutomationProperties.SetName(button, caption);
        var canvas = new Grid { Background = Brushes.Transparent };
        canvas.Children.Add(_surface); canvas.Children.Add(_outline);
        var contents = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 12, 12, 12), IsHitTestVisible = false };
        var icon = new ModuleIcon { ModuleId = files ? "file-document" : "file-organizer", Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, RenderTransform = _scale, RenderTransformOrigin = new Point(.5, .5) }; contents.Children.Add(icon);
        contents.Children.Add(new TextBlock { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0) });
        _path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); contents.Children.Add(_path); canvas.Children.Add(contents);
        button.Content = canvas; root.Children.Add(button); _error.SetResourceReference(StyleProperty, "StatusText"); root.Children.Add(_error);
        _outline.SetResourceReference(Shape.StrokeProperty, "InputBorderBrush");
        button.Click += (_, _) =>
        {
            if (_files)
            {
                var picker = new OpenFileDialog { Title = "选择要处理的文件", Multiselect = true, CheckFileExists = true };
                if (picker.ShowDialog(Window.GetWindow(this)) == true) AcceptFiles(picker.FileNames);
            }
            else { var picker = new OpenFolderDialog { Title = "选择磁盘或目录" }; if (picker.ShowDialog(Window.GetWindow(this)) == true) Accept(picker.FolderName); }
        };
        button.MouseEnter += (_, _) => Hover(true); button.MouseLeave += (_, _) => { if (!_dragging) Hover(false); };
        button.GotKeyboardFocus += (_, _) => Hover(true); button.LostKeyboardFocus += (_, _) => { if (!_dragging) Hover(false); };
        PreviewDragEnter += Drag; PreviewDragOver += Drag;
        PreviewDragLeave += (_, e) => { _dragging = false; Hover(false); e.Handled = true; };
        PreviewDrop += (_, e) =>
        {
            e.Handled = true; _dragging = false; Hover(false); e.Effects = DragDropEffects.None;
            if (!IsEnabled) return;
            var paths = Paths(e);
            if (_files) { if (AcceptFiles(paths)) e.Effects = DragDropEffects.Copy; return; }
            if (paths is not { Length: 1 }) { Error("请一次拖入一个磁盘或目录。"); return; }
            if (Accept(paths[0])) e.Effects = DragDropEffects.Copy;
        };
        Unloaded += (_, _) => { _dragging = false; Hover(false); };
        IsEnabledChanged += (_, _) => { Opacity = IsEnabled ? 1 : .55; if (!IsEnabled) { _dragging = false; Hover(false); } };
        UpdatePath(); Hover(false);
    }
    private static string[]? Paths(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
    private void Drag(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = Paths(e);
        var valid = IsEnabled && (_files ? paths is { Length: > 0 } && paths.All(File.Exists) : paths is { Length: 1 } && Directory.Exists(paths[0]));
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        if (valid == _dragging) return;
        _dragging = valid; Hover(valid);
    }
    private bool AcceptFiles(string[]? paths)
    {
        if (paths is not { Length: > 0 } || !paths.All(File.Exists)) { Error("请拖入文件，文件夹请使用“按文件夹”方式。"); return false; }
        _error.Text = ""; _error.Visibility = Visibility.Collapsed;
        FilesSelected?.Invoke(paths); return true;
    }
    private bool Accept(string path)
    {
        try
        {
            if (!Directory.Exists(path)) { Error("请选择有效目录，不支持拖入文件。"); return false; }
            path = ValidatePath?.Invoke(path) ?? System.IO.Path.GetFullPath(path);
            SetCurrentValue(SelectedPathProperty, path); _error.Text = ""; _error.Visibility = Visibility.Collapsed;
            PathSelected?.Invoke(path); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Error(e.Message); return false; }
    }
    private void Error(string message) { _error.Text = message; _error.Visibility = Visibility.Visible; }
    private void UpdatePath() { _path.Text = SelectedPath; _path.ToolTip = SelectedPath; _path.Visibility = string.IsNullOrWhiteSpace(SelectedPath) ? Visibility.Collapsed : Visibility.Visible; }
    private void Hover(bool active)
    {
        _surface.SetResourceReference(Border.BackgroundProperty, active ? "SoftBrush" : "SurfaceBrush");
        _outline.SetResourceReference(Shape.StrokeProperty, active ? "AccentInkBrush" : "InputBorderBrush");
        _outline.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _scale.ScaleX = _scale.ScaleY = 1;
        if (!SystemParameters.ClientAreaAnimation || !active) return;
        var animation = new DoubleAnimation(1, 1.1, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = _dragging ? RepeatBehavior.Forever : new RepeatBehavior(1) };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        if (_dragging) _outline.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -9, TimeSpan.FromMilliseconds(750)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
