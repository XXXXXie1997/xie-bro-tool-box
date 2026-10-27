using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using XieBox.Platform.UI;

namespace XieBox.DiskAnalyzer;

public partial class DiskView : UserControl
{
    public DiskModel Model { get; }
    private bool _mapMode;
    public DiskView(DiskModel model)
    {
        InitializeComponent(); Model = model; DataContext = model;
        SpaceChart.EnterFolder += model.EnterMap;
        UpdateMode();
        UpdateLayoutState();
        DirectoryPicker.PathSelected += path => Model.Directory = path;
        model.PropertyChanged += Changed;
        Unloaded += (_, _) => model.PropertyChanged -= Changed;
        Loaded += (_, _) => { model.PropertyChanged -= Changed; model.PropertyChanged += Changed; UpdateLayoutState(); };
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Model.ResultsLayout)) UpdateLayoutState();
        if (e.PropertyName == nameof(Model.Entries))
        {
            FilesTable.Items.SortDescriptions.Clear();
            foreach (var column in FilesTable.Columns) column.SortDirection = null;
            FilesTable.Columns[2].SortDirection = ListSortDirection.Descending;
            Animate(FilesTable);
        }
    }
    private void UpdateLayoutState()
    {
        Setup.Visibility = Model.ResultsLayout ? Visibility.Collapsed : Visibility.Visible;
        Results.Visibility = Model.ResultsLayout ? Visibility.Visible : Visibility.Collapsed;
        Animate(Model.ResultsLayout ? Results : Setup);
    }
    private void Animate(FrameworkElement target)
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        target.RenderTransform = new TranslateTransform();
        ((Storyboard)FindResource("Motion.Enter")).Clone().Begin(target, HandoffBehavior.SnapshotAndReplace);
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Model.GoUp();
    private void Root_Click(object sender, RoutedEventArgs e) => Model.GoRoot();
    private void ListMode_Click(object sender, RoutedEventArgs e) { _mapMode = false; UpdateMode(); }
    private void MapMode_Click(object sender, RoutedEventArgs e) { _mapMode = true; UpdateMode(); }
    private void UpdateMode()
    {
        MapPanel.Visibility = _mapMode ? Visibility.Visible : Visibility.Collapsed;
        FilesTable.Visibility = _mapMode ? Visibility.Collapsed : Visibility.Visible;
        MapMode.SetResourceReference(StyleProperty, _mapMode ? "PrimaryButton" : "FlatButton");
        ListMode.SetResourceReference(StyleProperty, _mapMode ? "FlatButton" : "PrimaryButton");
    }
    private void Enter_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is DiskNode node) Model.Enter(node); }
    private async void Scan_Click(object sender, RoutedEventArgs e) => await Model.ScanAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => Model.Cancel();
    private void Edit_Click(object sender, RoutedEventArgs e) => Model.Edit();
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string path) return;
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException();
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            new DialogWindow { Owner = Window.GetWindow(this), Title = "打开位置", Width = 400,
                Content = new TextBlock { Text = "文件可能已移动或无法访问，请重新扫描。", Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap } }.FitContent().ShowDialog();
        }
    }
}


