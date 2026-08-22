using Mirage.Shared;

namespace Mirage.Editor;

/// <summary>
/// Filesystem locations for the editor, resolved against the install dir or the per-user
/// config/data dirs — never the current working directory. Launched as a Linux AppImage or a
/// macOS .app (e.g. from Steam or a desktop shortcut), the CWD is the launcher's dir, not the
/// install dir, so CWD-relative paths land in the wrong place. The per-user config/data locations
/// are resolved by the shared <see cref="UserPaths"/>; the editor just supplies its own app name
/// so it gets its own per-user folder, distinct from the client's.
/// </summary>
internal static class EditorPaths
{
    private static readonly UserPaths Paths = new($"{Constants.GameName} Editor");

    /// <summary>
    /// Editable tile/sprite/item graphics. Operator-configurable via the AssetsDir setting; defaults
    /// to the per-user data dir so users can replace the shipped sheets or add their own. Populated
    /// from the bundled defaults by <see cref="SeedAssets"/>. A relative configured path is anchored
    /// to the install dir (never the CWD).
    /// </summary>
    public static string Assets
    {
        get
        {
            string? configured = AppSettings.Current.AssetsDir;
            return string.IsNullOrWhiteSpace(configured)
                ? Paths.Data("assets", "graphics")
                : Path.GetFullPath(configured, AppContext.BaseDirectory);
        }
    }

    /// <summary>The editor's per-user config dir (holds its appsettings.json).</summary>
    public static string Config => Paths.Config();

    /// <summary>The editor's log files. Under the DATA dir rather than config: logs are bulky and belong to
    /// the machine they were produced on, so on Windows they stay in %LocalAppData% and out of the roaming
    /// profile.</summary>
    public static string Logs => Paths.Data("logs");

    /// <summary>
    /// Authored game data (maps, items, npcs, …). Operator-configurable via the DataDir setting;
    /// defaults to the per-user data dir so writes succeed even from a read-only AppImage/.app. A
    /// relative configured path is anchored to the install dir (never the CWD).
    /// </summary>
    public static string Data
    {
        get
        {
            string? configured = AppSettings.Current.DataDir;
            return string.IsNullOrWhiteSpace(configured)
                ? Paths.Data()
                : Path.GetFullPath(configured, AppContext.BaseDirectory);
        }
    }

    // The bundled default graphics shipped next to the executable (read-only on AppImage/.app);
    // the seed source for the editable assets dir.
    private static string BundledAssets => Path.Combine(AppContext.BaseDirectory, "assets", "graphics");

    /// <summary>
    /// Ensures the editable assets dir holds the bundled defaults. Copies each bundled file that is
    /// missing at the destination (per-file, if-missing): populates on first run, fills in new
    /// defaults from an app update, and never overwrites a sheet the user has replaced or added.
    /// </summary>
    public static void SeedAssets()
    {
        string source = BundledAssets;
        if (!Directory.Exists(source)) return;
        string dest = Assets;
        foreach (string srcFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            try
            {
                string destFile = Path.Combine(dest, Path.GetRelativePath(source, srcFile));
                if (File.Exists(destFile)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(srcFile, destFile);
            }
            catch { /* best-effort seeding; a missing sheet just won't render */ }
        }
    }
}
