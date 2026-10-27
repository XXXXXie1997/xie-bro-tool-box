using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using XieBox.Contracts;
using XieBox.Platform.UI;

namespace XieBox.FileOperations;

public sealed class FileTaskModel(IPlatformContext context, string journalPath, bool organize, string? renameJournalPath = null) : ToolModel
{
    private bool _organizing = organize;
    public bool IsOrganizing
    {
        get => _organizing;
        set
        {
            if (IsBusy || _organizing == value) return;
            ChangeRule(ref _organizing, value);
            Message(""); Notify(nameof(CanUndo)); Notify(nameof(UndoLabel)); Edit();
        }
    }
    private bool _useFiles;
    public bool UseFiles { get => _useFiles; set => ChangeRule(ref _useFiles, value); }
    private IReadOnlyList<string> _selectedFiles = [];
    public IReadOnlyList<string> SelectedFiles { get => _selectedFiles; private set => ChangeRule(ref _selectedFiles, value); }
    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        try
        {
            var additions = paths.Select(Path.GetFullPath).ToArray();
            foreach (var path in additions)
            {
                FilePlan.ValidateRoot(Path.GetDirectoryName(path)!); FilePlan.CheckAncestors(path);
                if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline | FileAttributes.ReparsePoint)) != 0) throw new IOException("请选择可修改的普通文件。");
            }
            var result = SelectedFiles.Concat(additions).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (result.Length > 5000) throw new IOException("每批最多添加 5,000 个文件。");
            SelectedFiles = result; ClearInputFeedback();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Message(e.Message); }
    }
    public void RemoveFiles(IEnumerable<string> paths) { if (!IsBusy) SelectedFiles = SelectedFiles.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray(); }
    private string JournalPath => !IsOrganizing && renameJournalPath is not null ? renameJournalPath : journalPath;
    private string _directory = "", _prefix = "", _find = "", _replace = "";
    private bool _numbered;
    private int _rulesVersion;
    private RenameSettings? _nameRules;
    public RenameSettings NameRules
    {
        get
        {
            if (_nameRules is not null) return _nameRules;
            _nameRules = new RenameSettings();
            _nameRules.Changed += () => { _rulesVersion++; Entries = []; Notify(nameof(Entries)); Notify(nameof(CanApply)); ClearInputFeedback(); };
            return _nameRules;
        }
    }
    public string Directory { get => _directory; set => ChangeRule(ref _directory, value); }
    public string Prefix { get => _prefix; set => ChangeRule(ref _prefix, value); }
    public string Find { get => _find; set => ChangeRule(ref _find, value); }
    public string Replace { get => _replace; set => ChangeRule(ref _replace, value); }
    public bool Numbered { get => _numbered; set => ChangeRule(ref _numbered, value); }
    private void ChangeRule<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        ClearInputFeedback();
        field = value; _rulesVersion++; Notify(name);
        var hadPreview = Entries.Count > 0;
        Entries = []; Notify(nameof(Entries)); Notify(nameof(CanApply));
        if (hadPreview) Message("规则已调整，请重新生成预览。");
    }
    public IReadOnlyList<MoveItem> Entries { get; private set; } = [];
    private string _plannedRoot = "";
    private bool _plannedFiles;
    public bool CanApply => IsIdle && Entries.Count > 0;
    public string UndoLabel => IsOrganizing ? "撤销上次整理" : "撤销上次重命名";
    public bool CanUndo => IsIdle && HasUndoRecord();
    private bool HasUndoRecord()
    {
        if (!File.Exists(JournalPath)) return false;
        try { return FilePlan.Load(JournalPath).Items.Count > 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return true; }
    }
    public async Task PreviewAsync() => await Run(async token =>
    {
        Entries = []; Notify(nameof(Entries)); Notify(nameof(CanApply));
        if (!(await context.CheckCapabilityAsync("scan", token)).Allowed) { Message("平台未允许此预览能力。"); return; }
        if (UseFiles ? SelectedFiles.Count == 0 : string.IsNullOrWhiteSpace(Directory)) { Message("请先选择文件夹或添加文件。"); return; }
        var version = _rulesVersion;
        var explicitFiles = UseFiles; var paths = SelectedFiles.ToArray();
        var root = explicitFiles ? "" : Path.GetFullPath(Directory.Trim()); var organizeFiles = IsOrganizing;
        var rename = organizeFiles ? null : NameRules.Capture(DateTimeOffset.Now);
        Message("正在生成预览并校验文件内容…");
        string Destination(FileInfo file, int index) => organizeFiles
            ? Path.Combine(file.DirectoryName!, Category(file.Extension), file.Name)
            : Path.Combine(file.DirectoryName!, rename!(file.Name, index, new DateTimeOffset(file.CreationTimeUtc), new DateTimeOffset(file.LastWriteTimeUtc)));
        var preview = await Task.Run(() => explicitFiles ? FilePlan.PreviewFiles(paths, Destination, token) : FilePlan.Preview(root, Destination, token), token);
        if (version != _rulesVersion) { Message("规则已调整，请重新生成预览。"); return; }
        Entries = preview;
        _plannedRoot = root; _plannedFiles = explicitFiles; ResultsLayout = true; Notify(nameof(Entries)); Notify(nameof(CanApply));
        Message(Entries.Count == 0 ? "没有需要变更的文件。" : $"预览 {Entries.Count:N0} 个文件；仅处理所选范围，不覆盖重名文件。确认后才会执行。");
    });
    public async Task ApplyAsync() => await Run(async token =>
    {
        if (!(await context.CheckCapabilityAsync("manage", token)).Allowed) { Message("平台未允许文件修改能力。"); return; }
        if (Entries.Count == 0) return;
        var snapshot = Entries;
        try
        {
            var result = await Task.Run(() => FilePlan.Apply(_plannedRoot, snapshot, JournalPath, token, _plannedFiles), token);
            Message($"已完成 {result.Completed:N0} 个文件。可撤销本次操作；生成的空目录会保留。");
        }
        finally { Entries = []; Notify(nameof(Entries)); Notify(nameof(CanApply)); Notify(nameof(CanUndo)); }
    });
    public async Task UndoAsync() => await Run(async token =>
    {
        if (!(await context.CheckCapabilityAsync("manage", token)).Allowed) { Message("平台未允许文件修改能力。"); return; }
        var count = await Task.Run(() => FilePlan.Undo(JournalPath, token), token);
        Entries = []; Notify(nameof(Entries)); Notify(nameof(CanApply)); Notify(nameof(CanUndo)); Message($"已恢复 {count:N0} 个文件。空目录保留。");
    });
    public void ArchiveUndo()
    {
        if (IsBusy || !File.Exists(JournalPath)) return;
        try { File.Move(JournalPath, JournalPath + "." + Guid.NewGuid().ToString("N") + ".archive"); Notify(nameof(CanUndo)); Message("已保留历史记录，可以执行新的批次。历史批次需手动恢复。"); }
        catch (IOException e) { Message(e.Message); }
    }
    public static string Category(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".heic" or ".svg" or ".tif" or ".tiff" => "图片",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".flv" => "视频",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" => "音频",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "压缩包",
        ".doc" or ".docx" or ".pdf" or ".txt" or ".md" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" => "文档",
        _ => "其他"
    };
}

public static class FileTaskPage
{
    public static ToolPage Create(FileTaskModel model, bool organize)
    {
        var options = new StackPanel();
        ToolPage? page = null;
        var directory = new DirectoryDropZone { ValidatePath = FilePlan.ValidateRoot };
        directory.SetBinding(DirectoryDropZone.SelectedPathProperty, new Binding(nameof(model.Directory)) { Mode = BindingMode.TwoWay });
        directory.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(model.IsIdle)));
        directory.PathSelected += _ => model.ClearInputFeedback();
        var directoryField = ToolPage.Field("个人文件夹（仅处理当前层，系统与程序目录禁止修改）", directory);
        var sourceModes = new Grid { Margin = new Thickness(0, 0, 0, 12) }; sourceModes.ColumnDefinitions.Add(new()); sourceModes.ColumnDefinitions.Add(new());
        var folderMode = ToolPage.Button("按文件夹", () => model.UseFiles = false);
        var filesMode = ToolPage.Button("指定文件", () => model.UseFiles = true); Grid.SetColumn(filesMode, 1);
        sourceModes.Children.Add(folderMode); sourceModes.Children.Add(filesMode); options.Children.Add(sourceModes); options.Children.Add(directoryField);
        var filePicker = new FileSelectionPanel(model); options.Children.Add(filePicker);
        void UpdateSource()
        {
            directoryField.Visibility = model.UseFiles ? Visibility.Collapsed : Visibility.Visible;
            filePicker.Visibility = model.UseFiles ? Visibility.Visible : Visibility.Collapsed;
            folderMode.SetResourceReference(FrameworkElement.StyleProperty, model.UseFiles ? typeof(Button) : "PrimaryButton");
            filesMode.SetResourceReference(FrameworkElement.StyleProperty, model.UseFiles ? "PrimaryButton" : typeof(Button));
        }
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.UseFiles)) UpdateSource(); }; UpdateSource();
        var modes = new Grid { Margin = new Thickness(0, 2, 0, 16) }; modes.ColumnDefinitions.Add(new()); modes.ColumnDefinitions.Add(new());
        var sortMode = ToolPage.Button("分类整理", () => model.IsOrganizing = true);
        var renameMode = ToolPage.Button("批量重命名", () => model.IsOrganizing = false);
        sortMode.Margin = new Thickness(0, 0, 6, 0); renameMode.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(renameMode, 1); modes.Children.Add(sortMode); modes.Children.Add(renameMode); options.Children.Add(modes);
        var renameOptions = new RenameQuickOptions(model);
        var ruleArea = new StackPanel(); options.Children.Add(ruleArea);
        void Configure()
        {
            if (model.IsBusy) return;
            var body = new StackPanel { Margin = new Thickness(24), DataContext = model };
            var dialog = new DialogWindow { Owner = Window.GetWindow(page), Title = "配置规则", Width = 680, MaxWidth = SystemParameters.WorkArea.Width - 48, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 48, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var content = model.IsOrganizing ? (FrameworkElement)ToolPage.Text("按类型归入图片、视频、音频、文档、压缩包和其他文件夹。\n\n每个文件放入其原目录下的分类文件夹；不会覆盖已有文件。") : renameOptions;
            body.Children.Add(new ScrollViewer { Content = content, MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height - 210), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var done = ToolPage.Button("完成", () => dialog.Close(), true); done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(0, 16, 0, 0); body.Children.Add(done); dialog.Content = body;
            try { dialog.ShowDialog(); } finally { ((ScrollViewer)body.Children[0]).Content = null; }
        }
        ruleArea.Children.Add(ToolPage.Button("配置规则", Configure));
        var ruleExample = ToolPage.Text("", 12); ruleExample.Margin = new Thickness(0, 8, 0, 0);
        ruleExample.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = renameOptions.ExampleDisplay }); ruleArea.Children.Add(ruleExample);
        var sortOptions = ToolPage.Text("按类型归入各自原目录下的分类文件夹。\n不进入子目录，不处理隐藏文件和系统文件。"); options.Children.Add(sortOptions);
        var undo = UndoButton(); undo.Margin = new Thickness(0, 18, 0, 0); options.Children.Add(undo);
        page = new ToolPage(model, "文件整理", "按规则整理文件或批量重命名，执行前可预览结果。", options, "生成预览", model.PreviewAsync);
        var previewHost = new Grid();
        previewHost.RowDefinitions.Add(new() { Height = GridLength.Auto }); previewHost.RowDefinitions.Add(new());
        var previewExample = ToolPage.Text("", 12); previewExample.Margin = new Thickness(0, 0, 0, 12);
        previewExample.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = renameOptions.ExampleDisplay }); previewHost.Children.Add(previewExample);
        var sortPreview = new OrganizerPreview(model); var renamePreview = new RenamePreview { DataContext = model };
        Grid.SetRow(sortPreview, 1); Grid.SetRow(renamePreview, 1);
        previewHost.Children.Add(sortPreview); previewHost.Children.Add(renamePreview); page.SetResultsContent(previewHost);
        void UpdateMode()
        {
            sortOptions.Visibility = sortPreview.Visibility = model.IsOrganizing ? Visibility.Visible : Visibility.Collapsed;
            previewExample.Visibility = ruleExample.Visibility = renamePreview.Visibility = model.IsOrganizing ? Visibility.Collapsed : Visibility.Visible;
            sortMode.SetResourceReference(FrameworkElement.StyleProperty, model.IsOrganizing ? "PrimaryButton" : typeof(Button));
            renameMode.SetResourceReference(FrameworkElement.StyleProperty, model.IsOrganizing ? typeof(Button) : "PrimaryButton");
        }
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.IsOrganizing)) UpdateMode(); };
        UpdateMode();
        page.Actions.Children.Add(ToolPage.Button("选择文件", model.Edit)); page.Actions.Children.Add(ToolPage.Button("配置规则", Configure)); page.Actions.Children.Add(ToolPage.Button("重新预览", async () => await model.PreviewAsync()));
        page.FooterActions.Children.Add(UndoButton());
        page.FooterActions.Children.Add(ToolPage.Button("结束上次批次", () => { if (DialogWindow.Confirm(Window.GetWindow(page), "保留历史记录，但不再提供该批次的一键撤销，以便开始新的批次。是否继续？", "结束批次", "结束批次")) model.ArchiveUndo(); }));
        var apply = ToolPage.Button("执行预览内容", async () => { if (model.Entries.Count > 0 && DialogWindow.Confirm(Window.GetWindow(page), $"将修改预览中的 {model.Entries.Count} 个文件位置或名称。文件内容不变，不覆盖重名文件；完成后可撤销。", "确认文件操作", "执行")) await model.ApplyAsync(); }, true);
        apply.SetBinding(UIElement.IsEnabledProperty, new Binding("CanApply")); page.FooterActions.Children.Add(apply);
        model.ActivityChanged += (_, _) => { model.Notify(nameof(model.CanApply)); model.Notify(nameof(model.CanUndo)); };
        Button UndoButton() { var button = ToolPage.Button("撤销上次操作", () => Undo()); button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(model.UndoLabel))); button.SetBinding(UIElement.IsEnabledProperty, new Binding("CanUndo")); return button; }
        async void Undo() { if (page is not null && DialogWindow.Confirm(Window.GetWindow(page), "将按上次操作记录恢复文件名称和位置。若文件已变化或原位置已被占用，会停止并保留文件。", "撤销文件操作", "撤销")) await model.UndoAsync(); }
        return page;
    }
}

