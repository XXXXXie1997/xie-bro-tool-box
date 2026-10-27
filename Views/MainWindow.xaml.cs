using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XieBox.Models;
using XieBox.Services;
using XieBox.ViewModels;
using XieBox.GarbageCleaner;
using XieBox.FileScanner;
using XieBox.StartupManager;
using XieBox.MemoryRelease;
using XieBox.DiskAnalyzer;
using XieBox.Platform.UI;
using XieBox.SoftwareUninstaller;
using XieBox.FileOrganizer;
using XieBox.NetworkDiagnostics;
using XieBox.HealthCheck;
using XieBox.HardwareCheck;

namespace XieBox.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private readonly PlatformApi _api;
    private readonly AppearanceSettings _appearance;
    public System.Collections.ObjectModel.ObservableCollection<AppTab> OpenTabs { get; } = [];
    private HomeView? _home;
    private HealthPanel? _health;
    private ModuleContext? _healthContext;


    private CleanerView? _cleaner;
    private ScannerView? _scanner;
    private StartupView? _startup;
    private MemoryView? _memory;
    private DiskView? _disk;
    private readonly Dictionary<string, (ToolPage Page, ModuleContext Context)> _utilityPages = new();
    private ModuleContext? _diskContext;
    private ModuleContext? _startupContext;
    private ModuleContext? _memoryContext;
    private ModuleContext? _scannerContext;
    private ModuleContext? _cleanerContext;
    private bool _exitRequested;
    private bool _sessionEnding;
    private WindowState _restoredState = WindowState.Normal;
    public Task Initialization { get; private set; } = Task.CompletedTask;
    public MainWindow(MainViewModel model, PlatformApi api, AppearanceSettings? appearance = null)
    {
        InitializeComponent(); _appearance = appearance ?? new AppearanceSettings(); UpdateThemeButton(); _model = model; _api = api; DataContext = model; NavigationItems.ItemsSource = OpenTabs;
        OpenTabs.CollectionChanged += (_, _) => UpdateNavigationTabWidth();
        ShowHome();
        _model.ModuleUninstalled += ModuleUninstalled;
        Closed += (_, _) => { _healthContext?.Dispose(); foreach (var utility in _utilityPages.Values) { (utility.Page.Model as IDisposable)?.Dispose(); utility.Context.Dispose(); } _diskContext?.Dispose(); _cleanerContext?.Dispose(); _scannerContext?.Dispose(); _startupContext?.Dispose(); _memoryContext?.Dispose(); };
        StateChanged += (_, _) => { DiagnosticLog.Write("WindowState", $"State={WindowState}; Visible={IsVisible}; Page={_model.Selected?.Id ?? "home"}; Busy={_model.IsBusy}"); if (WindowState != WindowState.Minimized) _restoredState = WindowState; };
        _model.PropertyChanged += Model_PropertyChanged;
        Closed += (_, _) => { _model.PropertyChanged -= Model_PropertyChanged; _model.ModuleUninstalled -= ModuleUninstalled; };
    }
    private void UpdateThemeButton()
    {
        ThemeButton.IsChecked = _appearance.IsDark;
        ThemeButton.ToolTip = _appearance.IsDark ? "深色模式 · 点击切换为浅色" : "浅色模式 · 点击切换为深色";
    }
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _appearance.Toggle();
        UpdateThemeButton();
    }
    public void RestoreWindow()
    {
        DiagnosticLog.Write("RestoreWindow");
        ShowActivated = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoredState;
        Activate();
    }
    public void RequestExit() => Close();
    public void PrepareForSessionEnd()
    {
        DiagnosticLog.Write("SessionEnding");
        _sessionEnding = true;
        CancelTasks();
    }
    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_exitRequested && e.PropertyName == nameof(MainViewModel.IsBusy) && !_model.IsBusy)
            Dispatcher.BeginInvoke(new Action(() => { if (_exitRequested && !_model.IsBusy) Close(); }));
    }
    private void CancelTasks() { _health?.Model.Cancel(); _cleaner?.Model.Cancel(); _scanner?.Model.Cancel(); _startup?.Model.Cancel(); _memory?.Model.Cancel(); _disk?.Model.Cancel(); foreach (var utility in _utilityPages.Values) utility.Page.Model.Cancel(); }
    private bool IsModuleBusy(string id) => (id switch
    {
        "health-check" => _health?.Model.IsBusy == true,
        "garbage-cleaner" => _cleaner?.Model.IsBusy == true,
        "file-scanner" => _scanner?.Model.IsBusy == true,
        "startup-manager" => _startup?.Model.IsBusy == true,
        "memory-release" => _memory?.Model.IsBusy == true,
        "disk-analyzer" => _disk?.Model.IsBusy == true,
        _ => _utilityPages.TryGetValue(id, out var utility) && utility.Page.Model.IsBusy
    });
    private void UpdateToolBusy()
    {
        foreach (var tab in OpenTabs) tab.IsBusy = IsModuleBusy(tab.Module.Id);
        _model.SetToolBusy(_health?.Model.IsBusy == true || OpenTabs.Any(t => t.IsBusy));
    }
    private void ReleaseModule(string id)
    {
        switch (id)
        {
            case "health-check": _health?.Model.Cancel(); _health = null; _healthContext?.Dispose(); _healthContext = null; break;
            case "garbage-cleaner": _cleaner = null; _cleanerContext?.Dispose(); _cleanerContext = null; break;
            case "file-scanner": _scanner = null; _scannerContext?.Dispose(); _scannerContext = null; break;
            case "startup-manager": _startup = null; _startupContext?.Dispose(); _startupContext = null; break;
            case "memory-release": _memory = null; _memoryContext?.Dispose(); _memoryContext = null; break;
            case "disk-analyzer": _disk = null; _diskContext?.Dispose(); _diskContext = null; break;
            default: if (_utilityPages.Remove(id, out var utility)) { (utility.Page.Model as IDisposable)?.Dispose(); utility.Context.Dispose(); } break;
        }
    }
    private void ModuleUninstalled(string id)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Module.Id == id);
        if (tab is not null) CloseTab(tab);
        else ReleaseModule(id);
    }
    public bool CloseTab(AppTab tab)
    {
        if (!OpenTabs.Contains(tab) || IsModuleBusy(tab.Module.Id) || tab.IsBusy) return false;
        var index = OpenTabs.IndexOf(tab); var active = _model.Selected == tab.Module;
        OpenTabs.Remove(tab);
        if (active)
        {
            if (OpenTabs.Count == 0) ShowHome();
            else OpenModule(OpenTabs[Math.Min(index, OpenTabs.Count - 1)].Module);
        }
        tab.Page = null; ReleaseModule(tab.Module.Id); UpdateToolBusy(); return true;
    }
    private void TabClose_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is AppTab tab) CloseTab(tab); e.Handled = true; }
    private void Tab_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is AppTab tab) OpenModule(tab.Module); }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Initialization = _model.InitializeAsync();
        await Initialization;
        if (Environment.GetCommandLineArgs().Contains("--open-slim") || Environment.GetCommandLineArgs().Contains("--open-health") || Environment.GetCommandLineArgs().Contains("--open-hardware") || Environment.GetCommandLineArgs().Contains("--open-disk") || Environment.GetCommandLineArgs().Contains("--open-cleaner") || Environment.GetCommandLineArgs().Contains("--open-scanner"))
        {
            var module = _model.Modules.Single(m => m.Id == (Environment.GetCommandLineArgs().Contains("--open-slim") ? BuiltInModules.Slim.Id : Environment.GetCommandLineArgs().Contains("--open-health") ? BuiltInModules.Health.Id : Environment.GetCommandLineArgs().Contains("--open-hardware") ? BuiltInModules.Hardware.Id : Environment.GetCommandLineArgs().Contains("--open-disk") ? BuiltInModules.Disk.Id : Environment.GetCommandLineArgs().Contains("--open-scanner") ? BuiltInModules.Scanner.Id : BuiltInModules.Cleaner.Id));
            _model.Selected = module;
            if (!module.IsInstalled) await _model.InstallSelectedAsync(CancellationToken.None);
            OpenModule(module);
        }
    }
    private void RevealSelectedNavigation(ModuleItem module)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (NavigationItems.ItemContainerGenerator.ContainerFromItem(OpenTabs.FirstOrDefault(t => t.Module == module)) is FrameworkElement item) item.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void SetPage(FrameworkElement page)
    {
        if (ReferenceEquals(PageHost.Content, page)) return;
        if (_model.Selected is { } selected && OpenTabs.FirstOrDefault(t => t.Module == selected) is { } tab) tab.Page = page;
        PageHost.Content = page;
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        PageHost.RenderTransform = new TranslateTransform();
        ((Storyboard)FindResource("Motion.Enter")).Clone().Begin(PageHost, HandoffBehavior.SnapshotAndReplace);
    }
    private void ShowHome()
    {
        _model.Selected = null;
        if (_home is null)
        {
            _home = new HomeView(_model, OpenModule);
        }
        SetPage(_home);
    }
    private void Home_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void Manage_Click(object sender, RoutedEventArgs e) { _model.Selected = null; SetPage(new ManageView(_model, OpenModule)); }
    private void Module_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is ModuleItem module) OpenModule(module); }
    private void OpenModule(ModuleItem module)
    {
        _model.RecordUse(module);
        _model.Selected = module;
        var tab = OpenTabs.FirstOrDefault(t => t.Module.Id == module.Id);
        if (tab is null) { tab = new AppTab(module); OpenTabs.Add(tab); }
        RevealSelectedNavigation(module);
        if (tab.Page is not null) { SetPage(tab.Page); return; }
        if (module.IsInstalled && module.Id == BuiltInModules.Health.Id)
        {
            if (_health is null)
            {
                _healthContext = new ModuleContext(BuiltInModules.Health, _model.Accounts, () => Task.CompletedTask);
                _health = HealthPage.Create(_healthContext, _model.ToolStateDirectory, OpenHealthResult);
                _health.Model.ActivityChanged += (_, _) => UpdateToolBusy();
            }
            SetPage(_health);
        }
        else if (module.IsInstalled && BuiltInModules.IsCleaner(module.Definition))
        {
            if (_cleaner is null)
            {
                _cleanerContext = new ModuleContext(module.Definition, _model.Accounts, () => { Account_Click(this, new()); return Task.CompletedTask; });
                var cleanerModel = new CleanerViewModel(new CleanerEngine(CleanerPaths.Current, new WindowsRecycleBin()), _cleanerContext, _model.ToolStateDirectory);
                cleanerModel.ActivityChanged += (_, _) => UpdateToolBusy();
                _cleaner = new(cleanerModel);
            }
            SetPage(_cleaner);
        }
        else if (module.IsInstalled && BuiltInModules.IsScanner(module.Definition))
        {
            if (_scanner is null)
            {
                _scannerContext = new ModuleContext(module.Definition, _model.Accounts, () => { Account_Click(this, new()); return Task.CompletedTask; });
                var scannerModel = new ScannerModel(_scannerContext);
                scannerModel.ActivityChanged += (_, _) => UpdateToolBusy();
                _scanner = new ScannerView(scannerModel);
            }
            SetPage(_scanner);
        }
        else if (module.IsInstalled && BuiltInModules.IsStartup(module.Definition))
        {
            if (_startup is null)
            {
                _startupContext = new ModuleContext(module.Definition, _model.Accounts, () => { Account_Click(this, new()); return Task.CompletedTask; });
                var startupModel = new StartupModel(_startupContext, new StartupEngine());
                startupModel.ActivityChanged += (_, _) => UpdateToolBusy();
                _startup = new StartupView(startupModel);
            }
            SetPage(_startup);
        }
        else if (module.IsInstalled && BuiltInModules.IsMemory(module.Definition))
        {
            if (_memory is null)
            {
                _memoryContext = new ModuleContext(module.Definition, _model.Accounts, () => { Account_Click(this, new()); return Task.CompletedTask; });
                var memoryModel = new MemoryModel(_memoryContext, new MemoryEngine());
                memoryModel.ActivityChanged += (_, _) => UpdateToolBusy();
                _memory = new MemoryView(memoryModel);
            }
            SetPage(_memory);
        }
        else if (module.IsInstalled && BuiltInModules.IsDisk(module.Definition))
        {
            if (_disk is null)
            {
                _diskContext = new ModuleContext(module.Definition, _model.Accounts, () => { Account_Click(this, new()); return Task.CompletedTask; });
                var diskModel = new DiskModel(_diskContext);
                diskModel.ActivityChanged += (_, _) => UpdateToolBusy();
                _disk = new DiskView(diskModel);
            }
            SetPage(_disk);
        }
        else if (module.IsInstalled && BuiltInModules.IsBuiltIn(module.Definition))
        {
            if (!_utilityPages.TryGetValue(module.Id, out var utility))
            {
                var context = new ModuleContext(module.Definition, _model.Accounts, () => { Account_Click(this, new()); return Task.CompletedTask; });
                var state = _model.GetModuleStateDirectory(module.Id);
                var page = module.Id switch
                {
                    "system-slim" => SystemSlimView.Create(context),
                    "software-uninstaller" => SoftwarePage.Create(context),
                    "file-organizer" => Organizer.Create(context, state),
                    "network-diagnostics" => NetworkPage.Create(context),
                    "hardware-check" => HardwarePage.Create(context, new ElevatedHardwareSource(Environment.ProcessPath!)),
                    _ => throw new InvalidOperationException("未注册的内置应用")
                };
                page.Model.ActivityChanged += (_, _) => UpdateToolBusy();
                utility = (page, context); _utilityPages.Add(module.Id, utility);
            }
            SetPage(utility.Page);
        }
        else SetPage(new ModuleView(_model));
    }
    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (!_model.ShowAccount) return;
        new AccountWindow(_model.Accounts, _api) { Owner = this }.ShowDialog();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        DiagnosticLog.Write("WindowClosing", $"SessionEnding={_sessionEnding}; Busy={_model.IsBusy}");
        if (_sessionEnding) return;
        if (!_exitRequested && _model.IsBusy &&
            !DialogWindow.Confirm(this, "有任务正在进行，是否退出？退出前会先停止当前任务。", "退出蟹小箱", "退出"))
        {
            e.Cancel = true;
            return;
        }
        _exitRequested = true;
        if (!_model.IsBusy) { CancelTasks(); return; }
        // Let an active operation finish cancelling before disposing its context.
        e.Cancel = true;
        CancelTasks();
        _model.Status = "正在停止任务，完成后自动退出";
    }
}









