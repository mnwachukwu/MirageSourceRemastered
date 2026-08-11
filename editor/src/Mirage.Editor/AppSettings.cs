using System.Text.Json;

namespace Mirage.Editor;

public sealed class AppSettings
{
    public string DefaultServerHost { get; set; } = "localhost";
    public int DefaultServerPort { get; set; } = 4000;
    public string Language { get; set; } = "en";

    /// <summary>Optional path to the game data dir (maps, items, npcs, …). Null/empty = the per-user
    /// data dir. A relative path is resolved against the install dir, never the CWD.</summary>
    public string? DataDir { get; set; }

    /// <summary>Optional path to the editable graphics dir (tilesets/sprites/items). Null/empty = the
    /// per-user data dir. A relative path is resolved against the install dir, never the CWD.</summary>
    public string? AssetsDir { get; set; }

    // Window state
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // Map editor panel widths/heights
    public double MapEditorLeftWidth { get; set; } = 256;
    public double MapEditorRightWidth { get; set; } = 300;
    public double MapEditorRightBottomHeight { get; set; } = 220;
    // Whether the map editor's animated-tile preview starts on. Default on so mis-flagged tiles are visible.
    public bool MapEditorAnimPreview { get; set; } = true;

    // Other editor left-panel widths
    public double ItemEditorLeftWidth { get; set; } = 200;
    public double NpcEditorLeftWidth { get; set; } = 200;
    public double SpellEditorLeftWidth { get; set; } = 200;
    public double ShopEditorLeftWidth { get; set; } = 200;
    public double ClassEditorLeftWidth { get; set; } = 200;
    public double QuestEditorLeftWidth { get; set; } = 200;
    public double ConversationEditorLeftWidth { get; set; } = 200;
    public double MapGroupEditorLeftWidth { get; set; } = 200;

    private static AppSettings? _instance;
    public static AppSettings Current => _instance ??= Load();

    // Editor settings persist in the per-user config dir. appsettings.json is not bundled; until the
    // user changes a setting the app falls back to the in-code defaults above.
    private static string SettingsFile => Path.Combine(EditorPaths.Config, "appsettings.json");

    private static AppSettings Load()
    {
        string path = SettingsFile;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            string path = SettingsFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
