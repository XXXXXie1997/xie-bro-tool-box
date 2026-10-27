using System.IO;

namespace XieBox.GarbageCleaner;

public static class SlimCacheRules
{
    public static CategoryRule[] Discover()
    {
        var paths = CleanerPaths.Current;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var rules = RuleCatalog.Discover(paths).Select(r => r.IsRecycle() ? r with
        {
            Name = "系统盘回收站", Description = "当前用户在系统盘上删除、仍可还原的文件。", Consequence = "清空后不能从回收站还原。Windows 会再次确认；若扫描后内容变化，本次将跳过。"
        } : r with { Name = r.Processes.Length > 0 ? r.Name + " 网页缓存" : r.Name }).ToList();
        rules.Add(new("windows-temp", "Windows 过期临时文件", "Windows 临时目录中超过 7 天的临时文件和日志。", "删除后无法再查看这些日志；正在使用的文件会保留。", false,
            [new(Path.Combine(windows, "Temp"), TimeSpan.FromDays(7), [".tmp", ".temp", ".log"])], []));
        rules.Add(new("system-error-reports", "系统错误报告", "系统保存的崩溃和故障报告，超过 7 天才纳入。", "不影响正常启动，但会丢失这些故障的诊断资料。正在排查故障时建议保留。", false,
            [new(Path.Combine(data, "Microsoft", "Windows", "WER", "ReportArchive"), TimeSpan.FromDays(7)), new(Path.Combine(data, "Microsoft", "Windows", "WER", "ReportQueue"), TimeSpan.FromDays(7))], []));
        rules.Add(new("blue-screen-dumps", "蓝屏诊断文件", "Windows Minidump 中超过 7 天的小型内存转储。", "清理后无法利用这些文件分析过去的蓝屏；经常蓝屏时请保留。", false,
            [new(Path.Combine(windows, "Minidump"), TimeSpan.FromDays(7), [".dmp"])], []));
        AddGpu("nvidia-cache", "NVIDIA 图形缓存", ["NVIDIA/DXCache", "NVIDIA/GLCache", "NVIDIA/DXCache2"]);
        AddGpu("amd-cache", "AMD 图形缓存", ["AMD/DxCache", "AMD/DxcCache", "AMD/GLCache", "AMD/VkCache"]);
        return rules.Select(r => r with { Roots = r.Roots.Where(root => string.Equals(Path.GetPathRoot(Path.GetFullPath(root.Path)), SystemSlimService.SystemRoot, StringComparison.OrdinalIgnoreCase)).ToArray() }).ToArray();

        void AddGpu(string id, string name, string[] folders) => rules.Add(new(id, name, "显卡驱动生成的图形缓存，仅纳入超过 7 天的文件。", "游戏或图形应用下次启动可能需要重新编译，加载变慢或短暂卡顿。建议先退出游戏。", false,
            folders.Select(p => new RootRule(Path.Combine(paths.Local, p.Replace('/', Path.DirectorySeparatorChar)), TimeSpan.FromDays(7))).ToArray(), []));
    }
    private static bool IsRecycle(this CategoryRule rule) => rule.Id == "recycle-bin";
}
