using System.Windows;

namespace XieBox.Models;

public sealed class AppTab(ModuleItem module) : ObservableObject
{
    public ModuleItem Module { get; } = module;
    public FrameworkElement? Page { get; set; }
    private bool _busy;
    public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) { Notify(nameof(CanClose)); Notify(nameof(CloseHint)); } } }
    public bool CanClose => !IsBusy;
    public string CloseHint => IsBusy ? "任务进行中，请先停止任务" : "关闭应用页签";
}
