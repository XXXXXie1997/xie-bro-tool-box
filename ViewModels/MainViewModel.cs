using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Data;
using XieBox.Contracts;
using XieBox.Models;
using XieBox.Services;

namespace XieBox.ViewModels;
public sealed class MainViewModel : ObservableObject
{
    private readonly LocalStore _store;
    private readonly PlatformApi _api;
    public AccountService Accounts { get; }
    public ModuleInstaller Installer { get; }
    private ModuleItem? _selected;
    private string _status = "本地功能随时可用";
    private string _connection = "本地模式";
    private bool _busy;
    private bool _toolBusy;
    public bool DemoEdition { get; }
    public bool ShowAccount => DesktopFeatures.AccountsEnabled && !DemoEdition;
    public string DataRoot => _store.Root;
    public string GetModuleStateDirectory(string id) => Path.Combine(_store.Root, "tools", id);
    public string ToolStateDirectory => Path.Combine(_store.Root, "tools", "garbage-cleaner");
    public event Action<string>? ModuleUninstalled;
    public ObservableCollection<ModuleItem> Modules { get; } = [];
    public ICollectionView Favorites { get; }
    private bool _favoritesExpanded = true, _allExpanded = true;
    public bool FavoritesExpanded { get => _favoritesExpanded; set { if (Set(ref _favoritesExpanded, value)) SaveNavigation(); } }
    public bool AllExpanded { get => _allExpanded; set { if (Set(ref _allExpanded, value)) SaveNavigation(); } }
    public ICollectionView VisibleModules { get; }
    public ModuleItem? Selected { get => _selected; set { if (Set(ref _selected, value)) foreach (var module in Modules) module.IsSelected = module == value; } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Connection { get => _connection; set => Set(ref _connection, value); }
    public bool IsBusy { get => _busy || _toolBusy; set { Set(ref _busy, value); Notify(nameof(IsIdle)); } }
    public void SetToolBusy(bool busy) { _toolBusy = busy; Notify(nameof(IsBusy)); Notify(nameof(IsIdle)); }
    public bool IsIdle => !IsBusy;
    public string AccountLabel => Accounts.Current.IsAuthenticated ? Accounts.Current.Username ?? "平台账号" : "登录 / 注册";
    public string IdentityLabel => Accounts.Current.IsAuthenticated ? "已登录平台账号" : "游客 · 免费工具无需登录";
    public int InstalledCount => Modules.Count(x => x.IsInstalled);
    public MainViewModel(LocalStore store, PlatformApi api, AccountService accounts, bool demoEdition = false)
    {
        _store = store; _api = api; Accounts = accounts; Installer = new(store); DemoEdition = demoEdition;
        var definitions = BuiltInModules.All;
        var preferences = store.LoadPreferences();
        var oldRenamer = preferences.Navigation.Find(p => p.Id == "batch-renamer");
        if (oldRenamer is not null)
        {
            var organizerIndex = preferences.Navigation.FindIndex(p => p.Id == "file-organizer");
            if (organizerIndex < 0) preferences.Navigation.Add(oldRenamer with { Id = "file-organizer" });
            else preferences.Navigation[organizerIndex] = preferences.Navigation[organizerIndex] with { LastUsed = Math.Max(preferences.Navigation[organizerIndex].LastUsed, oldRenamer.LastUsed) };
        }
        _favoritesExpanded = preferences.FavoritesExpanded; _allExpanded = preferences.AllExpanded;
        foreach (var definition in definitions.OrderBy(x => { var index = preferences.Navigation.FindIndex(p => p.Id == x.Id); return index < 0 ? int.MaxValue : index; }))
        {
            var item = new ModuleItem(definition) { IsVisible = preferences.Navigation.Find(x => x.Id == definition.Id)?.Visible ?? true, LastUsed = preferences.Navigation.Find(x => x.Id == definition.Id)?.LastUsed ?? 0, IsInstalled = Installer.IsInstalled(definition) };
            item.PropertyChanged += ModuleChanged;
            Modules.Add(item);
        }
        Favorites = new ListCollectionView(Modules) { Filter = value => value is ModuleItem { IsVisible: true, LastUsed: > 0 } };
        Favorites.SortDescriptions.Add(new SortDescription(nameof(ModuleItem.LastUsed), ListSortDirection.Descending));
        VisibleModules = CollectionViewSource.GetDefaultView(Modules);
        VisibleModules.Filter = value => ((ModuleItem)value).IsVisible;
        Accounts.Changed += (_, _) => { Notify(nameof(AccountLabel)); Notify(nameof(IdentityLabel)); };
    }
    private void ModuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModuleItem.LastUsed)) { Favorites.Refresh(); SaveNavigation(); }
        if (e.PropertyName == nameof(ModuleItem.IsVisible)) { VisibleModules.Refresh(); Favorites.Refresh(); SaveNavigation(); }
    }
    public void RecordUse(ModuleItem module)
    {
        module.LastUsed = Math.Max(DateTime.UtcNow.Ticks, Modules.Max(m => m.LastUsed) + 1);
    }
    public void SaveNavigation()
    {
        try { _store.SavePreferences(Modules, FavoritesExpanded, AllExpanded); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "暂时无法保存设置，请检查数据目录权限。"; }
    }
    public void Move(ModuleItem module, int offset)
    {
        var old = Modules.IndexOf(module); var next = old + offset;
        if (old < 0 || next < 0 || next >= Modules.Count) return;
        Modules.Move(old, next); SaveNavigation();
    }
    public void MoveToInsertion(ModuleItem module, int insertionIndex)
    {
        var old = Modules.IndexOf(module);
        if (IsBusy || old < 0 || insertionIndex < 0 || insertionIndex > Modules.Count) return;
        var next = insertionIndex > old ? insertionIndex - 1 : insertionIndex;
        if (next == old) return;
        Modules.Move(old, next); SaveNavigation();
    }
    public async Task InitializeAsync()
    {
        if (ShowAccount) await Accounts.InitializeAsync();
        Connection = "离线模式";
        Status = "所有应用已内置，点击即可使用";
    }
    public Task RefreshCatalogAsync()
    {
        Status = "所有应用随蟹小箱更新，无需单独下载";
        return Task.CompletedTask;
    }
    public async Task InstallSelectedAsync(CancellationToken cancellationToken)
    {
        var module = Selected;
        if (module is null || IsBusy || !module.CanInstall) return;
        IsBusy = true;
        try
        {
            module.InstallStatus = Status = $"正在安装 {module.Name}…";
            if (BuiltInModules.IsBuiltIn(module.Definition)) await Installer.InstallBuiltInAsync(module.Definition, cancellationToken);
            else await Installer.InstallFromUrlAsync(module.Definition, new Progress<double>(p => module.InstallStatus = Status = $"正在下载 {module.Name} · {p:0}%"), cancellationToken);
            module.IsInstalled = true; Notify(nameof(InstalledCount)); module.InstallStatus = ""; Status = $"{module.Name} 已安装，可以打开了";
        }
        catch (OperationCanceledException) { module.InstallStatus = Status = "安装已取消"; }
        catch (Exception e) when (e is IOException or HttpRequestException or InvalidDataException or InvalidOperationException or JsonException or UnauthorizedAccessException) { module.InstallStatus = Status = "安装未完成：" + e.Message; }
        finally { IsBusy = false; }
    }
    public void Uninstall(ModuleItem module)
    {
        if (BuiltInModules.IsBuiltIn(module.Definition) || IsBusy || !module.IsInstalled) return;
        try { Installer.Uninstall(module.Id); module.IsInstalled = false; Notify(nameof(InstalledCount)); ModuleUninstalled?.Invoke(module.Id); Status = $"{module.Name} 已卸载，导航设置保留"; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { Status = "卸载未完成：" + e.Message; }
    }
}


