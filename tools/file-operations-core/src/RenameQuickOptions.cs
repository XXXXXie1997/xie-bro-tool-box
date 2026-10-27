using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XieBox.Platform.UI;

namespace XieBox.FileOperations;

public sealed class RenameQuickOptions : StackPanel
{
    private readonly FileTaskModel model;
    private readonly TextBlock examples;
    public TextBlock ExampleDisplay => examples;
    private Sample[] samples = [];
    private int readVersion, renderVersion;
    private record Sample(string Name, DateTimeOffset Created, DateTimeOffset Modified);

    public RenameQuickOptions(FileTaskModel model)
    {
        this.model = model;
        var rules = model.NameRules;
        DataContext = rules;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBox.FileOperations;component/RenameEditorStyles.xaml", UriKind.Relative) });
        SetBinding(IsEnabledProperty, new Binding(nameof(model.IsIdle)) { Source = model });
        var modes = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        var labels = new[] { "统一名称＋编号", "查找替换", "添加文字", "自定义组合" };
        var buttons = new Button[4];
        for (var i = 0; i < 4; i++)
        {
            modes.ColumnDefinitions.Add(new()); var mode = i;
            var button = ToolPage.Button(labels[i], () => rules.Mode = mode);
            button.Padding = new Thickness(6, 8, 6, 8); button.MinHeight = 36;
            button.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 3 ? 0 : 4, 0);
            Grid.SetColumn(button, i); modes.Children.Add(button); buttons[i] = button;
        }
        Children.Add(modes);
        var panels = Enumerable.Range(0, 4).Select(_ => new StackPanel()).ToArray();
        panels[0].Children.Add(ToolPage.Field("名称", ToolPage.Input(nameof(rules.BaseName))));
        var numbering = new Grid { DataContext = rules.UniformNumber, Margin = new Thickness(0, 8, 0, 0) };
        var fields = new[] { ("起始值", "Start"), ("步长", "Step"), ("补零位数", "Digits") };
        for (var i = 0; i < fields.Length; i++)
        {
            numbering.ColumnDefinitions.Add(new());
            var field = ToolPage.Field(fields[i].Item1, ToolPage.Input(fields[i].Item2));
            field.Margin = new Thickness(0, 0, i == 2 ? 0 : 10, 0); Grid.SetColumn(field, i); numbering.Children.Add(field);
        }
        panels[0].Children.Add(new Expander { Header = "编号设置", Content = numbering });
        panels[1].Children.Add(ToolPage.Field("查找文字", ToolPage.Input(nameof(rules.Find))));
        panels[1].Children.Add(ToolPage.Field("替换为（留空则删除）", ToolPage.Input(nameof(rules.Replacement))));
        var advanced = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var regex = new CheckBox { Content = "使用正则表达式" };
        regex.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(rules.UseRegex)) { Mode = BindingMode.TwoWay }); advanced.Children.Add(regex);
        advanced.Children.Add(Note("支持正则，例如 \\d+ 匹配数字；$1 可引用匹配分组。"));
        panels[1].Children.Add(new Expander { Header = "高级选项 · 正则表达式", Content = advanced });
        panels[2].Children.Add(ToolPage.Field("添加的文字", ToolPage.Input(nameof(rules.AddedText))));
        var append = new CheckBox { Content = "放在原文件名后面（默认放在前面）" };
        append.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(rules.Append)) { Mode = BindingMode.TwoWay }); panels[2].Children.Add(append);
        panels[3].Children.Add(Note("自由组合文字、编号、日期与原文件名，支持拖动排序。"));
        panels[3].Children.Add(ToolPage.Button("编辑自定义组合", () => RenameEditor.Open(Window.GetWindow(this), rules)));
        foreach (var panel in panels) Children.Add(panel);
        examples = Note("选择文件后显示文件名示例。");
        var frame = new Border { Child = examples, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 0) };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); Children.Add(frame);
        Children.Add(Note("扩展名自动保留。示例仅供参考，生成完整预览并确认后才会修改文件。"));
        void UpdateMode()
        {
            for (var i = 0; i < 4; i++)
            {
                panels[i].Visibility = rules.Mode == i ? Visibility.Visible : Visibility.Collapsed;
                buttons[i].SetResourceReference(StyleProperty, rules.Mode == i ? "PrimaryButton" : typeof(Button));
            }
        }
        rules.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(rules.Mode)) UpdateMode(); };
        rules.Changed += RenderExamples;
        model.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(model.Directory) or nameof(model.SelectedFiles) or nameof(model.UseFiles)) ReadExamples(); };
        UpdateMode(); ReadExamples();
    }

    private async void ReadExamples()
    {
        if (!Dispatcher.CheckAccess()) { await Dispatcher.InvokeAsync(ReadExamples); return; }
        var version = ++readVersion; ++renderVersion;
        samples = []; var path = model.Directory; var useFiles = model.UseFiles; var selected = model.SelectedFiles.ToArray();
        if (useFiles ? selected.Length == 0 : string.IsNullOrWhiteSpace(path)) { examples.Text = "选择文件后显示文件名示例。"; return; }
        examples.Text = "正在读取文件名示例…";
        try
        {
            var result = await Task.Run(() =>
            {
                if (!useFiles) FilePlan.ValidateRoot(path);
                return (useFiles ? selected.Select(p => new FileInfo(p)) : new DirectoryInfo(path).EnumerateFiles()).Take(5001)
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline | FileAttributes.ReparsePoint)) == 0)
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).Take(3)
                    .Select(f => new Sample(f.Name, new DateTimeOffset(f.CreationTimeUtc), new DateTimeOffset(f.LastWriteTimeUtc))).ToArray();
            });
            await Dispatcher.InvokeAsync(() =>
            {
                if (version != readVersion) return;
                samples = result; RenderExamples();
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { await Dispatcher.InvokeAsync(() => { if (version == readVersion) examples.Text = "暂时无法读取示例，请检查所选目录。"; }); }
    }

    private async void RenderExamples()
    {
        if (!Dispatcher.CheckAccess()) { await Dispatcher.InvokeAsync(RenderExamples); return; }
        var version = ++renderVersion;
        if (samples.Length == 0) { examples.Text = string.IsNullOrWhiteSpace(model.Directory) ? "选择文件后显示文件名示例。" : "此目录暂无可展示的文件名示例。"; return; }
        try
        {
            var rename = model.NameRules.Capture(DateTimeOffset.Now); var snapshot = samples;
            var text = await Task.Run(() => string.Join("\n", snapshot.Select((s, i) => s.Name + " → " + rename(s.Name, i + 1, s.Created, s.Modified))));
            await Dispatcher.InvokeAsync(() => { if (version == renderVersion) examples.Text = text; });
        }
        catch (IOException e) { await Dispatcher.InvokeAsync(() => { if (version == renderVersion) examples.Text = e.Message; }); }
    }
    private static TextBlock Note(string text)
    {
        var note = ToolPage.Text(text, 12); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 6, 0, 6);
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); return note;
    }
}
