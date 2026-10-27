using System.IO;
using System.Windows;
using XieBox.DiskAnalyzer;
using XieBox.Services;

internal static partial class Program
{
    private static void RenderSpaceMapPreview()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "spacemap-preview", "fixture"));
        string[] names = ["图片素材", "开发项目", "视频", "文档", "下载"];
        for (var i = 0; i < names.Length; i++)
        {
            var dir = Path.Combine(folder, names[i]); Directory.CreateDirectory(dir);
            for (var j = 0; j < 4; j++)
            { using var file = File.Create(Path.Combine(dir, $"示例文件-{j + 1}.bin")); file.SetLength((5 - i) * (4 - j) * 1024); }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Theme.xaml", UriKind.Relative) });
        using var api = new PlatformApi(new Uri("http://127.0.0.1:1/"));
        var store = new LocalStore(Path.Combine(folder, "..", "state"));
        using var context = new ModuleContext(BuiltInModules.Disk, new AccountService(api, store), () => Task.CompletedTask);
        var model = new DiskModel(context) { Directory = folder };
        RunUi(model.ScanAsync);
        var view = new DiskView(model);
        Render(view, Path.Combine(folder, "..", "light.png"), 1180, 720);
        ThemePalette.Apply(new ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Palette.Dark.xaml", UriKind.Relative) });
        Render(new DiskView(model), Path.Combine(folder, "..", "dark.png"), 1180, 720);
        Console.WriteLine("Space map previews rendered without showing windows.");
    }
}
