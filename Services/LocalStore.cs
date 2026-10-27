using System.IO;
using System.Text.Json;
using XieBox.Models;

namespace XieBox.Services;
public sealed class LocalStore
{
    public string Root { get; }
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public LocalStore(string? directory = null)
    {
        Root = directory is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XieBox") : Path.GetFullPath(directory);
        Directory.CreateDirectory(Root);
    }
    public DesktopPreferences LoadPreferences()
    {
        try { return JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(Path.Combine(Root, "preferences.json")), Json) ?? new([]); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new([]); }
    }
    public void SavePreferences(IEnumerable<ModuleItem> modules, bool favoritesExpanded = true, bool allExpanded = true) => WriteAtomic("preferences.json", JsonSerializer.Serialize(new DesktopPreferences(modules.Select(m => new NavigationPreference(m.Id, m.IsVisible, m.LastUsed)).ToList(), favoritesExpanded, allExpanded), Json));
    public void WriteAtomic(string name, string content)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path + ".tmp", content);
        File.Move(path + ".tmp", path, true);
    }
}

