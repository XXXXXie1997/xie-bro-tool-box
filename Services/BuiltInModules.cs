using XieBox.Contracts;

namespace XieBox.Services;

// Only code shipped and built with this host can register an executable first-party module.
public static class BuiltInModules
{
    public static ModuleDefinition Cleaner { get; } = new("garbage-cleaner", "垃圾清理", "◌", "分类扫描临时文件与浏览器缓存，支持选择清理。", "0.2.0", "available", "builtin-cleaner", true,
        ["分类扫描", "按需勾选", "清理影响说明", "本地清理"], [new("scan", "free"), new("cleanup", "free")]);
    public static ModuleDefinition Slim { get; } = new("system-slim", "系统盘瘦身", "▥", "查看系统盘中可清理的内容及其占用。", "0.1.0", "available", "builtin-system-slim", true,
        ["占用分析", "清理影响说明", "逐项确认", "功能预览"], [new("scan", "free"), new("system-slim", "free")]);
    public static bool IsCleaner(ModuleDefinition module) => module.Id == Cleaner.Id && module.Runtime == Cleaner.Runtime && module.Version == Cleaner.Version;
    public static ModuleDefinition Scanner { get; } = new("file-scanner", "文件扫描", "⌕", "按大小或格式查找目录中的文件。", "0.1.0", "available", "builtin-scanner", true,
        ["大文件扫描", "格式筛选", "重复文件扫描", "包含子目录", "离线扫描"], [new("scan", "free")]);
    public static bool IsScanner(ModuleDefinition module) => module.Id == Scanner.Id && module.Runtime == Scanner.Runtime && module.Version == Scanner.Version;
    public static ModuleDefinition Startup { get; } = new("startup-manager", "启动项管理", "↗", "管理应用的开机自动启动。", "0.1.0", "available", "builtin-startup", true,
        ["查看启动项", "关闭与恢复", "离线管理"], [new("scan", "free"), new("manage", "free")]);
    public static ModuleDefinition Memory { get; } = new("memory-release", "内存释放", "▤", "了解内存占用，按需整理后台应用内存。", "0.1.0", "available", "builtin-memory", true,
        ["实时内存", "按需释放", "不结束进程"], [new("release", "free")]);
    public static bool IsStartup(ModuleDefinition module) => module.Id == Startup.Id && module.Runtime == Startup.Runtime && module.Version == Startup.Version;
    public static bool IsMemory(ModuleDefinition module) => module.Id == Memory.Id && module.Runtime == Memory.Runtime && module.Version == Memory.Version;
    public static ModuleDefinition Disk { get; } = new("disk-analyzer", "磁盘空间分析", "▥", "逐层查看目录大小，找出空间用在哪里。", "0.1.0", "available", "builtin-disk", true, ["目录排行", "空间占比", "逐层查看", "离线分析"], [new("scan", "free")]);
    public static bool IsDisk(ModuleDefinition module) => module.Id == Disk.Id && module.Runtime == Disk.Runtime && module.Version == Disk.Version;
    public static ModuleDefinition Uninstaller { get; } = new("software-uninstaller", "软件卸载", "⊟", "查看已安装的软件并运行卸载程序。", "0.1.0", "available", "builtin-uninstaller", true, ["软件清单", "搜索筛选", "原生卸载"], [new("scan", "free"), new("manage", "free")]);
    public static ModuleDefinition Renamer { get; } = new("batch-renamer", "批量重命名", "Aa", "预览一批新名字，确认后再修改。", "0.1.0", "available", "builtin-renamer", true, ["前缀与替换", "自动编号", "操作预览", "撤销记录"], [new("scan", "free"), new("manage", "free")]);
    public static ModuleDefinition Organizer { get; } = new("file-organizer", "文件整理", "▤", "分类整理与批量重命名，支持预览和撤销。", "0.1.0", "available", "builtin-organizer", true, ["类型分类", "批量重命名", "操作预览", "重名保护", "撤销记录"], [new("scan", "free"), new("manage", "free")]);
    public static ModuleDefinition Network { get; } = new("network-diagnostics", "网络检查", "⇆", "检查上网问题，提供处理建议和排查报告。", "0.1.0", "available", "builtin-network", false, ["连接检查", "网站检测", "处理建议", "报告导出"], [new("scan", "free"), new("manage", "free")]);
    public static ModuleDefinition Health { get; } = new("health-check", "电脑体检", "♡", "检查硬件状态并验证常用设备功能。", "0.2.0", "available", "builtin-health", true, ["自动检查", "功能验证", "检查依据", "报告导出"], [new("scan", "free")]);
    public static ModuleDefinition Hardware { get; } = new("hardware-check", "硬件信息", "▣", "查看硬件配置、温度、风扇转速与健康信息。", "0.1.0", "available", "builtin-hardware", true, ["详细配置", "硬件状态", "前台采集", "报告导出"], [new("scan", "free")]);
    public static IReadOnlyList<ModuleDefinition> All { get; } = [Health, Cleaner, Slim, Scanner, Startup, Memory, Disk, Uninstaller, Organizer, Network, Hardware];
    public static bool IsBuiltIn(ModuleDefinition module) => All.Any(d => d.Id == module.Id && d.Runtime == module.Runtime && d.Version == module.Version);
    public static ModuleDefinition[] Include(IEnumerable<ModuleDefinition> definitions)
        => All.Concat(definitions.Where(d => d.Status == "available" && d.Runtime != "placeholder" && All.All(b => b.Id != d.Id))).ToArray();
}

