using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace XieBox.MemoryRelease;
public partial class MemoryView : UserControl
{
    public MemoryModel Model { get; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    public MemoryView(MemoryModel model)
    {
        InitializeComponent(); Model = model; DataContext = model;
        _timer.Tick += async (_, _) => { Model.Refresh(); await Model.RefreshApplicationsAsync(); };
        Loaded += async (_, _) => { Model.PropertyChanged += Changed; Model.Refresh(); if (IsVisible) _timer.Start(); await Model.RefreshApplicationsAsync(); };
        Unloaded += (_, _) => { _timer.Stop(); Model.PropertyChanged -= Changed; };
        IsVisibleChanged += async (_, _) => { if (IsVisible && IsLoaded) { Model.Refresh(); _timer.Start(); await Model.RefreshApplicationsAsync(); } else _timer.Stop(); };
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Model.Title) || !IsVisible || !SystemParameters.ClientAreaAnimation) return;
        Panel.RenderTransform = new TranslateTransform();
        ((Storyboard)FindResource("Motion.Enter")).Clone().Begin(Panel);
    }
    private async void Release_Click(object sender, RoutedEventArgs e) => await Model.ReleaseAsync();
}
