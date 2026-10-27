using XieBox.Contracts;

namespace XieBox.Models;
public sealed class ModuleItem(ModuleDefinition definition) : ObservableObject
{
    private bool _visible = true;
    private bool _selected;
    private long _lastUsed;
    private bool _installed;
    private string _installStatus = "";
    public ModuleDefinition Definition { get; private set; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Icon => Definition.Icon;
    public string Description => Definition.Description;
    public string[] Features => Definition.Features;
    public string Version => Definition.Version;
    public bool IsVisible { get => _visible; set => Set(ref _visible, value); }
    public long LastUsed { get => _lastUsed; set => Set(ref _lastUsed, value); }
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    public bool IsInstalled { get => XieBox.Services.BuiltInModules.IsBuiltIn(Definition) || _installed; set { if (Set(ref _installed, value)) Refresh(); } }
    public bool CanInstall => Definition.Status == "available" && !IsInstalled;
    public string InstallStatus { get => _installStatus; set => Set(ref _installStatus, value); }
    public string StateLabel => XieBox.Services.BuiltInModules.IsBuiltIn(Definition) ? "内置" : IsInstalled ? "已安装" : "暂不可用";
    public string Availability => Definition.Offline ? "支持离线" : "需要联网";
    public void Update(ModuleDefinition definition) { Definition = definition; Refresh(); }
    private void Refresh() { Notify(nameof(StateLabel)); Notify(nameof(CanInstall)); Notify(nameof(Version)); Notify(nameof(Description)); }
}
public sealed record NavigationPreference(string Id, bool Visible, long LastUsed = 0);
public sealed record DesktopPreferences(List<NavigationPreference> Navigation, bool FavoritesExpanded = true, bool AllExpanded = true);

