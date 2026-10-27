using System.IO;
using System.Windows;
using XieBox.GarbageCleaner;
using XieBox.HealthCheck;
using XieBox.NetworkDiagnostics;
using XieBox.StartupManager;
using XieBox.Services;

namespace XieBox.Views;

public partial class MainWindow
{
    private void OpenHealthResult(HealthResult result)
    {
        var module = _model.Modules.FirstOrDefault(m => m.Id == result.ModuleId);
        if (module is null) return;
        if (_health?.Model.IsBusy == true) return;
        if (IsModuleBusy(module.Id)) { _health?.Model.Message("该应用正在执行任务，结束后可查看体检结果。"); return; }
        try
        {
            bool existing = OpenTabs.Any(t => t.Module.Id == module.Id && t.Page is not null);
            if (!existing && result.Data is CleanerSession cleaner)
            {
                _cleanerContext = new ModuleContext(module.Definition, _model.Accounts, () => Task.CompletedTask);
                var model = new CleanerViewModel(cleaner.Engine, _cleanerContext, _model.ToolStateDirectory);
                model.ActivityChanged += (_, _) => UpdateToolBusy();
                _cleaner = new(model); model.UseSnapshot(cleaner.Snapshot);
            }
            OpenModule(module);
            // Existing pages retain their selection and task results. Fresh pages receive
            // the actual health-check snapshot rather than silently starting another scan.
            if (!existing)
            {
                if (result.Data is StartupScan startup) _startup?.Model.UseSnapshot(startup);
                if (result.Data is NetworkSession network && _utilityPages.TryGetValue(module.Id, out var target) && target.Page.Model is NetworkModel model)
                    model.UseSnapshot(network.Entries, network.Time);
                if (module.Id == "disk-analyzer" && result.Data is string directory && _disk is not null) _disk.Model.Directory = directory;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { _health?.Model.Message("暂时无法打开应用，请重新启动蟹小箱后重试。"); }
    }
}
