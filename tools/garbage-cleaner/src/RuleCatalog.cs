using System.IO;

namespace XieBox.GarbageCleaner;

public static class RuleCatalog
{
    public static CategoryRule[] Discover(CleanerPaths paths)
    {
        var rules = new List<CategoryRule>
        {
            new("temporary", "过期临时文件", "当前用户临时目录中超过 7 天的 .tmp、.temp 和 .log 文件。", "会永久删除这些临时文件；仍需保留的安装日志或恢复资料请取消勾选。", true,
                [new(Path.Combine(paths.Local, "Temp"), TimeSpan.FromDays(7), [".tmp", ".temp", ".log"])], []),
            new("crash-dumps", "程序崩溃转储", "超过 7 天的程序崩溃诊断文件（.dmp）。", "不影响程序正常启动，但无法再用这些文件分析当时的崩溃。", false,
                [new(Path.Combine(paths.Local, "CrashDumps"), TimeSpan.FromDays(7), [".dmp"])], []),
            new("error-reports", "Windows 错误报告", "当前用户超过 7 天的已归档或待发送错误报告。", "会丢失对应问题的诊断资料，尚未发送的报告也会被删除。", false,
                [new(Path.Combine(paths.Local, "Microsoft", "Windows", "WER", "ReportArchive"), TimeSpan.FromDays(7)), new(Path.Combine(paths.Local, "Microsoft", "Windows", "WER", "ReportQueue"), TimeSpan.FromDays(7))], []),
            new("shader-cache", "图形着色器缓存", "当前用户超过 1 天的 Direct3D 着色器缓存。", "游戏或图形应用首次运行可能变慢或短暂卡顿，系统会重新生成缓存。", false,
                [new(Path.Combine(paths.Local, "D3DSCache"), TimeSpan.FromDays(1))], [])
        };
        AddChromium(rules, paths, "chrome", "Google Chrome", ["chrome"], ["Google/Chrome/User Data"]);
        AddChromium(rules, paths, "edge", "Microsoft Edge", ["msedge"], ["Microsoft/Edge/User Data"]);
        AddChromium(rules, paths, "360se", "360 安全浏览器", ["360se", "360se64"], ["360se6/User Data", "360/360se6/User Data"], ["360se6/User Data"]);
        AddChromium(rules, paths, "360chrome", "360 极速浏览器", ["360chrome", "360chromex"], ["360Chrome/Chrome/User Data", "360ChromeX/Chrome/User Data"]);
        AddChromium(rules, paths, "qqbrowser", "QQ 浏览器", ["QQBrowser"], ["Tencent/QQBrowser/User Data", "Tencent/QQBrowser"]);
        AddChromium(rules, paths, "quark", "夸克浏览器", ["quark", "QuarkCloudDrive"], ["Quark/User Data"]);
        AddChromium(rules, paths, "sogou", "搜狗浏览器", ["SogouExplorer"], ["SogouExplorer/Webkit"], ["SogouExplorer/Webkit"]);
        var firefox = new List<RootRule>();
        foreach (var profile in Children(Path.Combine(paths.Local, "Mozilla", "Firefox", "Profiles")))
        {
            var cache = Path.Combine(profile, "cache2");
            if (File.Exists(Path.Combine(cache, "index")) && File.Exists(Path.Combine(paths.Roaming, "Mozilla", "Firefox", "Profiles", Path.GetFileName(profile), "prefs.js")))
                firefox.Add(new(Path.Combine(cache, "entries"), TimeSpan.Zero));
        }
        rules.Add(new("firefox", "Firefox", "只扫描标准配置下 cache2/entries 的网页缓存。", BrowserImpact, false, firefox.ToArray(), ["firefox"], BrowserNote));
        rules.Add(new("recycle-bin", "回收站", "当前用户所有本地磁盘的回收站，由 Windows 系统接口处理。", "清空后无法从回收站还原文件。执行时 Windows 会再次要求确认；也可能包含扫描后新移入的文件。", false, [], []));
        return rules.ToArray();
    }
    private const string BrowserImpact = "网页首次打开可能变慢并重新下载资源；不清理密码、Cookie、书签、历史记录和网盘下载。请先退出浏览器及其后台进程。";
    private const string BrowserNote = "仅识别标准缓存布局；自定义目录和未识别的版本不扫描。";
    private static void AddChromium(List<CategoryRule> rules, CleanerPaths paths, string id, string name, string[] processes, string[] localRoots, string[]? roamingRoots = null)
    {
        var roots = new List<RootRule>();
        var bases = localRoots.Select(p => Path.Combine(paths.Local, p.Replace('/', Path.DirectorySeparatorChar)))
            .Concat((roamingRoots ?? []).Select(p => Path.Combine(paths.Roaming, p.Replace('/', Path.DirectorySeparatorChar))));
        foreach (var directory in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var profile in Children(directory))
        {
            var namePart = Path.GetFileName(profile);
            if (namePart != "Default" && !namePart.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            if (!File.Exists(Path.Combine(profile, "Preferences"))) continue;
            var cache = Path.Combine(profile, "Cache", "Cache_Data");
            // Require both the profile marker and a cache index, never delete a whole profile.
            if (File.Exists(Path.Combine(cache, "index")) || File.Exists(Path.Combine(cache, "index-dir", "the-real-index")))
                roots.Add(new(cache, TimeSpan.Zero));
        }
        rules.Add(new(id, name, "仅扫描已识别配置文件中的 Cache/Cache_Data。", BrowserImpact, false, roots.ToArray(), processes, BrowserNote));
    }
    private static IEnumerable<string> Children(string root)
    {
        try
        {
            using var lease = SafeFile.PinDirectory(root);
            return Directory.GetDirectories(root).Where(p => !File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)).ToArray();
        }
        catch (Exception e) when (SafeFile.Expected(e)) { return []; }
    }
}
