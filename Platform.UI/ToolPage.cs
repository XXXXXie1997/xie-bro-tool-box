using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace XieBox.Platform.UI;

public abstract class ToolModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ActivityChanged;
    public void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private bool _busy, _results;
    private string _status = "";
    protected CancellationTokenSource? Cancellation;
    public bool IsBusy { get => _busy; private set { _busy = value; Notify(); Notify(nameof(IsIdle)); ActivityChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsIdle => !IsBusy;
    public bool ResultsLayout { get => _results; protected set { _results = value; Notify(); } }
    public string Status { get => _status; protected set { _status = value; Notify(); } }
    public void Cancel() => Cancellation?.Cancel();
    public void Edit() { if (IsIdle) { ResultsLayout = false; Status = ""; } }
    public void Message(string text) => Status = text;
    public void ClearInputFeedback()
    {
        if (IsIdle && StatusToneConverter.Classify(Status) != "Info") Message("");
    }
    protected async Task Run(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        Status = ""; Cancellation = new(); IsBusy = true;
        try { await action(Cancellation.Token); }
        catch (OperationCanceledException) { Status = "操作已停止，已完成的操作记录会保留。"; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        { Status = "未能完成：" + e.Message; }
        finally { Cancellation.Dispose(); Cancellation = null; IsBusy = false; }
    }
}

// A shared page frame keeps independent modules visually aligned with the host.
public sealed class ToolPage : UserControl
{
    private readonly Grid _results = new();
    private readonly ScrollViewer _setup = new() { VerticalAlignment = VerticalAlignment.Center, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    public ToolModel Model { get; }
    public DataGrid Table { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single };
    public StackPanel Actions { get; } = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    public StackPanel FooterActions { get; } = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    public ToolPage(ToolModel model, string title, string description, FrameworkElement options, string startLabel, Func<Task> start)
    {
        Model = model; DataContext = model;
        var root = new Grid { Margin = new Thickness(24, 16, 24, 12) }; Content = root;
        var setup = new StackPanel { Width = 560, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 24) };
        setup.Children.Add(Text(title, 34, true, true));
        var descriptionText = Text(description, 13, false, true); descriptionText.Margin = new Thickness(0, 12, 0, 28); descriptionText.SetResourceReference(ForegroundProperty, "MutedBrush"); setup.Children.Add(descriptionText);
        // Plain introductory copy follows the heading's center line; form fields
        // keep their own alignment inside their layout containers.
        if (options is TextBlock introText) introText.TextAlignment = TextAlignment.Center;
        setup.Children.Add(options);
        var startButton = Button(startLabel, async () => await start(), true); startButton.Width = 200; startButton.Height = 54; startButton.FontSize = 18; startButton.HorizontalAlignment = HorizontalAlignment.Center; startButton.Margin = new Thickness(0, 24, 0, 0); setup.Children.Add(startButton);
        var setupStop = Button("停止", model.Cancel); setupStop.HorizontalAlignment = HorizontalAlignment.Center; setupStop.Margin = new Thickness(0, 12, 0, 0);
        setupStop.SetBinding(IsEnabledProperty, new Binding("IsBusy")); setupStop.SetBinding(VisibilityProperty, new Binding("IsBusy") { Converter = new BooleanToVisibilityConverter() }); setup.Children.Add(setupStop);
        var setupStatus = Text("", 12, false, true); setupStatus.Margin = new Thickness(0, 12, 0, 0); setupStatus.SetResourceReference(StyleProperty, "StatusText"); setupStatus.SetBinding(TextBlock.TextProperty, new Binding("Status")); setup.Children.Add(setupStatus);
        _setup.Content = setup; root.Children.Add(_setup); root.Children.Add(_results);
        _results.RowDefinitions.Add(new() { Height = GridLength.Auto }); _results.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); _results.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text(title, 22, true));
        var progress = new ProgressBar { Height = 4, Margin = new Thickness(30, 0, 30, 0), IsIndeterminate = true, VerticalAlignment = VerticalAlignment.Center };
        progress.SetResourceReference(ForegroundProperty, "AccentInkBrush"); progress.SetResourceReference(BackgroundProperty, "AccentBrush"); progress.SetBinding(VisibilityProperty, new Binding("IsBusy") { Converter = new BooleanToVisibilityConverter() }); Grid.SetColumn(progress, 1); header.Children.Add(progress);
        Grid.SetColumn(Actions, 2); header.Children.Add(Actions); _results.Children.Add(header);
        var stop = Button("停止", model.Cancel); stop.SetBinding(IsEnabledProperty, new Binding("IsBusy")); stop.SetBinding(VisibilityProperty, new Binding("IsBusy") { Converter = new BooleanToVisibilityConverter() }); Actions.Children.Add(stop);
        Grid.SetRow(Table, 1); _results.Children.Add(Table);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; Grid.SetRow(footer, 2); DockPanel.SetDock(FooterActions, Dock.Right); footer.Children.Add(FooterActions);
        var status = Text("", 12); status.SetResourceReference(StyleProperty, "StatusText"); status.SetBinding(TextBlock.TextProperty, new Binding("Status")); footer.Children.Add(status); _results.Children.Add(footer);
        Loaded += (_, _) => { model.PropertyChanged -= Changed; model.PropertyChanged += Changed; Update(); };
        Unloaded += (_, _) => model.PropertyChanged -= Changed;
        Update();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Model.ResultsLayout)) Update(); }
    private void Update()
    {
        _setup.Visibility = Model.ResultsLayout ? Visibility.Collapsed : Visibility.Visible; _results.Visibility = Model.ResultsLayout ? Visibility.Visible : Visibility.Collapsed;
        var target = Model.ResultsLayout ? (FrameworkElement)_results : _setup;
        if (IsLoaded && SystemParameters.ClientAreaAnimation && TryFindResource("Motion.Enter") is Storyboard motion) { target.RenderTransform = new TranslateTransform(); motion.Clone().Begin(target); }
    }
    public static TextBlock Text(string text, double size = 13, bool bold = false, bool center = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6), MinHeight = 32 }; if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.SetBinding(IsEnabledProperty, new Binding("IsIdle")); button.Click += (_, _) => action(); return button;
    }
    public void Column(string label, string property, double width = 0)
    {
        var column = new DataGridTextColumn { Header = label, Binding = new Binding(property), Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star) };
        column.ElementStyle = (Style)FindResource("TableText"); Table.Columns.Add(column);
    }
    public void SetResultsContent(FrameworkElement content)
    {
        _results.Children.Remove(Table);
        Grid.SetRow(content, 1); _results.Children.Add(content);
    }
    public static StackPanel Field(string label, FrameworkElement control)
    { var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(new TextBlock { Text = label, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) }); panel.Children.Add(control); return panel; }
    public static TextBox Input(string property) { var box = new TextBox(); box.SetBinding(TextBox.TextProperty, new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); return box; }
    public static void Notice(Window owner, string title, string text) => new DialogWindow { Owner = owner, Title = title, Width = 520, Content = new ScrollViewer { Content = new TextBlock { Text = text, Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.FitContent().ShowDialog();
}


