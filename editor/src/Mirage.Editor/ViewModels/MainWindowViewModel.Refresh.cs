using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol.Packets;
using System.Text;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Rereading whatever the editor is pointed at — the offline store on disk, or the server's name indexes
/// over the live connection — and reporting what arrived, so a change made outside the editor does not need
/// a restart to be seen.
///
/// <para>A section holding unsaved edits is left alone. Reloading it would drop that work with no way back,
/// and the report names it so a section that did not move is never mistaken for one that had nothing to
/// take.</para>
///
/// <para>Online the same payload login hands over is asked for again with <c>EditorRequestDataPacket</c>.
/// The offline side compares folders on disk; the online side compares the packet, which moves when a record
/// is added, removed or renamed.</para>
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The offline store folder behind each section. A section absent from here keeps no folder.</summary>
    private static readonly Dictionary<string, string> SectionFolder = new()
    {
        ["Maps"] = "maps", ["MapGroups"] = "map_groups", ["Items"] = "items", ["NPCs"] = "npcs",
        ["Shops"] = "shops", ["Spells"] = "spells", ["Classes"] = "classes", ["Quests"] = "quests",
        ["Conversations"] = "conversations", ["Accounts"] = "accounts",
    };

    /// <summary>What each section's folder looked like at the last load, so the next refresh can say whether
    /// anything actually arrived.</summary>
    private readonly Dictionary<string, string> _lastSeen = [];

    private (string Key, Action Online, Action Offline, Func<bool> Dirty, Func<int> Count)[] Refreshable =>
    [
        ("Maps",          MapEditor.LoadOnline, MapEditor.LoadOffline,          () => MapEditor.HasAnyDirtyMap,       () => MapEditor.Maps.Count),
        ("MapGroups",     MapGroupEditor.LoadOnline, MapGroupEditor.LoadOffline,     () => MapGroupEditor.HasAnyDirty,     () => MapGroupEditor.MapGroups.Count),
        ("Items",         ItemEditor.LoadOnline, ItemEditor.LoadOffline,         () => ItemEditor.HasAnyDirty,         () => ItemEditor.Items.Count),
        ("NPCs",          NpcEditor.LoadOnline, NpcEditor.LoadOffline,          () => NpcEditor.HasAnyDirty,          () => NpcEditor.Npcs.Count),
        ("Shops",         ShopEditor.LoadOnline, ShopEditor.LoadOffline,         () => ShopEditor.HasAnyDirty,         () => ShopEditor.Shops.Count),
        ("Spells",        SpellEditor.LoadOnline, SpellEditor.LoadOffline,        () => SpellEditor.HasAnyDirty,        () => SpellEditor.Spells.Count),
        ("Classes",       ClassEditor.LoadOnline, ClassEditor.LoadOffline,        () => ClassEditor.HasAnyDirty,        () => ClassEditor.Classes.Count),
        ("Quests",        QuestEditor.LoadOnline, QuestEditor.LoadOffline,        () => QuestEditor.HasAnyDirty,        () => QuestEditor.Quests.Count),
        ("Conversations", ConversationEditor.LoadOnline, ConversationEditor.LoadOffline, () => ConversationEditor.HasAnyDirty, () => ConversationEditor.Conversations.Count),
        ("Accounts",      AccountEditor.LoadOnline, AccountEditor.LoadOffline,      () => false,                          () => AccountEditor.Accounts.Count),
    ];

    public string RefreshMenuItemLabel => EditorStrings.Get(EditorStrings.MainWindow_DataRefresh);

    /// <summary>The server's answer, reduced to something comparable: every name index it sent, in order.
    /// It moves when a record is added, removed or renamed on the server, which is the online equivalent of
    /// a file changing on disk.</summary>
    private static string ServerStamp(EditorDataPacket p)
    {
        var sb = new StringBuilder();
        void band(string label, IReadOnlyList<EditorDataPacket.NameEntry>? xs)
        {
            sb.Append(label).Append('=');
            foreach (var e in xs ?? []) sb.Append(e.Num).Append(':').Append(e.Name).Append(',');
            sb.Append('|');
        }
        band("i", p.Items); band("n", p.Npcs); band("s", p.Shops); band("p", p.Spells);
        band("m", p.Maps); band("c", p.Classes); band("g", p.MapGroups); band("q", p.Quests);
        band("v", p.Conversations);
        return sb.ToString();
    }

    /// <summary>The stamp of the last server payload seen, for the same before/after question the offline
    /// folders answer. One value rather than one per section: the server sends every index in one packet.</summary>
    private string _lastServerSeen = "";

    /// <summary>Takes the baseline from a payload the session already has, so the first refresh after a
    /// connect compares against what login handed over rather than against nothing.</summary>
    public void MarkServerSeen(EditorDataPacket pkt) => _lastServerSeen = ServerStamp(pkt);

    /// <summary>
    /// A cheap stand-in for the content of a section's folder: every file's name, length and write time. It
    /// moves whenever a record is added, removed OR rewritten, which a record count does not — a folder whose
    /// files were all replaced in place still holds the same number of them.
    /// </summary>
    private static string SourceStamp(string section)
    {
        if (!SectionFolder.TryGetValue(section, out string? folder)) return "";
        string dir = Path.Combine(EditorPaths.Data, folder);
        if (!Directory.Exists(dir)) return "";
        try
        {
            var sb = new StringBuilder();
            foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var fi = new FileInfo(f);
                sb.Append(fi.Name).Append(':').Append(fi.Length).Append(':')
                  .Append(fi.LastWriteTimeUtc.Ticks).Append('|');
            }
            return sb.ToString();
        }
        catch
        {
            return "";   // a folder that cannot be read reports as unknown rather than as unchanged
        }
    }

    /// <summary>Records what every section's folder looks like now, so the next refresh has a baseline.
    /// Called once the startup load has finished.</summary>
    public void MarkSourceSeen()
    {
        foreach (var (key, _, _, _, _) in Refreshable) _lastSeen[key] = SourceStamp(key);
    }

    [RelayCommand]
    private async Task RefreshFromSourceAsync()
    {
        EditorLog.Info("Refresh requested ({Source}).", IsOnline ? "server" : "disk");

        // The whole world is replaced under the open editors, so the window is covered while it happens.
        // Editing a row that is about to be thrown away reads as the editor discarding the work.
        IsLoading = true;
        LoadingStatus = EditorStrings.Get(EditorStrings.MainWindow_LoadingData);
        string? report;
        try
        {
            report = await RereadAsync();
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }

        // Reported once the cover is down, so the result is read against the world it describes.
        if (report is not null && ShowAlertAsync is not null) await ShowAlertAsync(report);
    }

    /// <summary>Rereads the world and returns what moved, for reporting once the cover is down.</summary>
    private async Task<string?> RereadAsync()
    {
        // THE reread, and the whole point of the command. Each editor's LoadOffline()/LoadOnline() refills
        // its rows from a cache filled once at startup or at login. Calling those alone rebuilds the view
        // from the same records and reports, correctly and uselessly, that nothing changed.
        bool serverMoved = false;
        if (IsOnline)
        {
            var pkt = await _conn.RequestDataAsync();
            if (pkt is null)
            {
                EditorLog.Warn("Refresh: the server did not answer the data request.");
                return EditorStrings.Get(EditorStrings.Refresh_ServerSilent);
            }
            string stamp = ServerStamp(pkt);
            serverMoved = stamp != _lastServerSeen;
            _lastServerSeen = stamp;
            _data.LoadOnline(pkt, _conn.Hello?.Records);
        }
        else
        {
            await _data.LoadOfflineAsync();
        }

        var changed = new List<string>();
        var same = new List<string>();
        var skipped = new List<string>();

        foreach (var (key, online, offline, dirty, count) in Refreshable)
        {
            string label = EditorStrings.Get(SectionLabelKey(key));
            if (dirty()) { skipped.Add(label); continue; }

            string before = _lastSeen.GetValueOrDefault(key, "");
            string now = IsOnline ? "" : SourceStamp(key);
            int countBefore = count();

            if (IsOnline) online(); else offline();

            _lastSeen[key] = now;
            int countAfter = count();

            // Offline each folder answers for itself. Online there is one packet for every section, so the
            // section moved if the packet did or if its own count changed — a rename moves the packet, and
            // that is as fine-grained as the server's answer gets.
            bool moved = IsOnline ? serverMoved || countAfter != countBefore : now != before;
            if (!moved) { same.Add(label); continue; }
            changed.Add(countAfter == countBefore
                ? label
                : EditorStrings.Format(EditorStrings.Refresh_SectionMoved,
                    ("Section", label), ("Before", countBefore), ("After", countAfter)));
        }

        var sb = new StringBuilder();
        sb.AppendLine(EditorStrings.Get(IsOnline ? EditorStrings.Refresh_FromServer : EditorStrings.Refresh_FromDisk));
        sb.AppendLine();
        sb.AppendLine(changed.Count > 0
            ? EditorStrings.Format(EditorStrings.Refresh_Changed, ("Sections", string.Join(", ", changed)))
            : EditorStrings.Get(EditorStrings.Refresh_NothingMoved));
        if (same.Count > 0)
            sb.AppendLine(EditorStrings.Format(EditorStrings.Refresh_SameCount, ("Sections", string.Join(", ", same))));
        if (skipped.Count > 0)
            sb.AppendLine(EditorStrings.Format(EditorStrings.Refresh_Skipped, ("Sections", string.Join(", ", skipped))));

        EditorLog.Info("Refresh done: {Changed} changed, {Same} unchanged, {Skipped} skipped.",
                       changed.Count, same.Count, skipped.Count);

        return sb.ToString().TrimEnd();
    }
}
