using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using XieBox.Platform.UI;

namespace XieBox.FileScanner;

public partial class ScannerView : UserControl
{
    public ScannerModel Model { get; }
    public ScannerView(ScannerModel model)
    {
        InitializeComponent(); Model = model; DataContext = model;
        DirectoryPicker.PathSelected += path => Model.Directory = path;
        model.PropertyChanged += Changed;
        Unloaded += (_, _) => model.PropertyChanged -= Changed;
        Loaded += (_, _) => { model.PropertyChanged -= Changed; model.PropertyChanged += Changed; UpdateLayoutState(); };
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Model.ResultsLayout)) UpdateLayoutState();
        if (e.PropertyName == nameof(Model.Files))
        {
            FilesTable.Items.SortDescriptions.Clear();
            foreach (var column in FilesTable.Columns) column.SortDirection = null;
            FilesTable.Columns[3].SortDirection = ListSortDirection.Descending;
            Animate(FilesTable);
        }
    }
    private void UpdateLayoutState()
    {
        GroupColumn.Visibility = Model.Duplicates ? Visibility.Visible : Visibility.Collapsed;
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
    private void Duplicates_Click(object sender, RoutedEventArgs e) => Model.Duplicates = true;
    private void Large_Click(object sender, RoutedEventArgs e) => Model.LargeFiles = true;
    private void Formats_Click(object sender, RoutedEventArgs e) => Model.LargeFiles = false;
    private async void Scan_Click(object sender, RoutedEventArgs e) => await Model.ScanAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => Model.Cancel();
    private void Edit_Click(object sender, RoutedEventArgs e) => Model.EditConditions();
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string path) return;
        try
        {
            if (!File.Exists(path)) throw new IOException();
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            new DialogWindow { Owner = Window.GetWindow(this), Title = "打开位置", Width = 400,
                Content = new TextBlock { Text = "文件可能已移动或无法访问，请重新扫描。", Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap } }.FitContent().ShowDialog();
        }
    }
}


