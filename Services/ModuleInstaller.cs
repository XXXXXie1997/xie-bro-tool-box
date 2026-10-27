using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using XieBox.Contracts;

namespace XieBox.Services;
public sealed class ModuleInstaller(LocalStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string ModulesRoot => Path.Combine(store.Root, "modules");
    public bool IsInstalled(ModuleDefinition module)
    {
        if (BuiltInModules.IsBuiltIn(module)) return true;
        try
        {
            var folder = Target(module.Id);
            if (!Directory.Exists(folder)) return false;
            RejectLinks(folder);
            var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(Path.Combine(folder, "module.json")), LocalStore.Json);
            return manifest is not null && manifest.Id == module.Id && manifest.ProtocolVersion == 1
                && (BuiltInModules.IsBuiltIn(module) ? manifest.Runtime == module.Runtime && manifest.Version == module.Version : manifest.Runtime == "content");
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }
    public Task InstallBuiltInAsync(ModuleDefinition module, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!BuiltInModules.IsBuiltIn(module)) throw new InvalidDataException("平台未内置此应用。");
        return Task.CompletedTask;
    }
    public async Task InstallFromUrlAsync(ModuleDefinition module, IProgress<double>? progress, CancellationToken token)
    {
        if (module.Status != "available" || module.PackageUrl is null) throw new InvalidOperationException("这个模块尚未发布。");
        var uri = new Uri(module.PackageUrl);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidDataException("下载地址必须使用 HTTPS。");
        Directory.CreateDirectory(ModulesRoot);
        var temporary = Path.Combine(ModulesRoot, Guid.NewGuid() + ".download");
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            const long limit = 100 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("模块包超过大小限制。");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > limit) throw new InvalidDataException("模块包超过大小限制。");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if (response.Content.Headers.ContentLength is long length && length > 0) progress?.Report(100d * total / length);
                }
            }
            await InstallPackageAsync(module, temporary, token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    // The expected release and digest must come from the host's trusted catalog, never from the package itself.
    public async Task InstallPackageAsync(ModuleDefinition expected, string packagePath, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        string? stage = null;
        try
        {
            if (expected.Status != "available" || expected.Sha256 is not { Length: 64 }) throw new InvalidDataException("缺少可信的发布校验信息。");
            Directory.CreateDirectory(ModulesRoot);
            RejectLinks(ModulesRoot);
            var target = Target(expected.Id);
            if (Directory.Exists(target)) throw new InvalidOperationException("模块已安装，请先卸载后安装其他版本。");
            await using var stream = File.OpenRead(packagePath);
            if (stream.Length > 100 * 1024 * 1024) throw new InvalidDataException("模块包过大。");
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
            if (!digest.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模块包校验失败，未安装。");
            stream.Position = 0;
            stage = Path.Combine(ModulesRoot, ".stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > 10000) throw new InvalidDataException("模块包文件数量过多。");
            long total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                total = checked(total + entry.Length);
                if (total > 500 * 1024 * 1024 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                    throw new InvalidDataException("模块包包含不支持的内容。");
                var destination = SafeEntry(stage, entry.FullName);
                if (!seen.Add(destination)) throw new InvalidDataException("模块包包含重复路径。");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                await source.CopyToAsync(output, token);
            }
            var manifestPath = Path.Combine(stage, "module.json");
            if (new FileInfo(manifestPath).Length > 64 * 1024) throw new InvalidDataException("模块描述过大。");
            var manifest = JsonSerializer.Deserialize<ModuleManifest>(await File.ReadAllTextAsync(manifestPath, token), LocalStore.Json);
            if (manifest is null || manifest.Id != expected.Id || manifest.Version != expected.Version || manifest.ProtocolVersion != 1 || manifest.Runtime != "content" || expected.Runtime != "content")
                throw new InvalidDataException("模块描述与发布信息不匹配，或当前平台尚不支持其运行方式。");
            if (!File.Exists(SafeEntry(stage, manifest.Entry))) throw new InvalidDataException("模块入口不存在。");
            token.ThrowIfCancellationRequested();
            Directory.Move(stage, target);
            stage = null;
        }
        finally
        {
            if (stage is not null && Directory.Exists(stage)) { RejectLinks(stage); Directory.Delete(stage, true); }
            _gate.Release();
        }
    }
    public ModuleContent LoadContent(ModuleDefinition module)
    {
        var target = Target(module.Id);
        RejectLinks(target);
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(Path.Combine(target, "module.json")), LocalStore.Json) ?? throw new InvalidDataException("模块描述无效。");
        if (manifest.Id != module.Id || manifest.ProtocolVersion != 1 || manifest.Runtime != "content") throw new InvalidDataException("模块协议不兼容。");
        var path = SafeEntry(target, manifest.Entry);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("模块页面过大。");
        return JsonSerializer.Deserialize<ModuleContent>(File.ReadAllText(path), LocalStore.Json) ?? throw new InvalidDataException("模块页面无效。");
    }
    public void Uninstall(string id)
    {
        if (BuiltInModules.All.Any(m => m.Id == id)) return;
        var target = Target(id);
        if (!Directory.Exists(target)) return;
        RejectLinks(target);
        Directory.Delete(target, true);
    }
    private string Target(string id)
    {
        if (!Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,63}$")) throw new InvalidDataException("模块 ID 无效。");
        return Path.Combine(ModulesRoot, id);
    }
    public static string SafeEntry(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        var parts = normalized.TrimEnd('/').Split('/');
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(normalized) || parts.Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(p, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("模块包路径无效。");
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模块包路径越界。");
        return path;
    }
    private static void RejectLinks(string root)
    {
        // Check ancestors too: a substituted modules root must not redirect installation or deletion.
        for (var parent = new DirectoryInfo(root); parent is not null; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("模块目录不能经过链接。");
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("模块目录包含链接。");
            if (entry is DirectoryInfo directory) RejectDescendants(directory);
        }
    }
    private static void RejectDescendants(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("模块目录包含链接。");
            if (entry is DirectoryInfo child) RejectDescendants(child);
        }
    }
}
