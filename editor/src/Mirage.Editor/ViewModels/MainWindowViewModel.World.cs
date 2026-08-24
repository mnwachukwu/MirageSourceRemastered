using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Opening and closing a world offline.
///
/// <para>A world is a directory holding maps/, npcs/, items/ and the rest — the numbered set the records
/// address each other through. The editor keeps none of its own: it opens one wherever it lives, and several
/// can sit side by side. Only settings, logs and the editable graphics belong to the editor.</para>
///
/// <para>Nothing is open until something is opened, so the window starts empty and says so.</para>
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Set by the View: shows a folder picker starting at <paramref name="startAt"/>, or null if
    /// the person cancelled.</summary>
    public Func<string, Task<string?>>? PickWorldFolderAsync { get; set; }

    /// <summary>Whether a world is open. Everything that lists or edits records hangs off this.</summary>
    public bool HasWorld => EditorPaths.HasWorld;

    /// <summary>Whether the window has nothing to show. Connected, the world is the server's and there is
    /// always one, so the prompt to open a folder belongs to the offline case alone.</summary>
    public bool ShowEmptyWorld => !IsOnline && !HasWorld;

    /// <summary>The open world's path, for the title bar and the empty-state prompt.</summary>
    public string WorldPath => EditorPaths.Data;

    /// <summary>What to call the open world in the window title: its own name, or "Untitled World" where
    /// it has none, or nothing at all when none is open. Telling a live world from a test copy of it is the
    /// whole reason a world carries a name, and the title bar is where that has to be legible.</summary>
    public string WorldLabel =>
        !HasWorld ? ""
        : _data.Manifest.IsNamed ? _data.Manifest.Name.Trim()
        : EditorStrings.Get(EditorStrings.World_Untitled);

    /// <summary>Worlds opened before, most recent first.</summary>
    public IReadOnlyList<RecentWorldViewModel> RecentWorlds =>
        [.. AppSettings.Current.RecentWorlds.Select(p => new RecentWorldViewModel(p, OpenRecentWorldAsync))];

    [RelayCommand]
    private async Task OpenWorldAsync()
    {
        if (PickWorldFolderAsync is null) return;
        // The picker starts at the shipped world, which is the one thing a first run is guaranteed to have.
        string start = EditorPaths.HasWorld ? EditorPaths.Data
            : Directory.Exists(EditorPaths.BundledWorld) ? EditorPaths.BundledWorld
            : AppContext.BaseDirectory;
        string? picked = await PickWorldFolderAsync(start);
        if (picked is not null) await OpenWorldAsync(picked, remember: true);
    }

    /// <summary>Points the editor at a world and reads it. Anything unsaved is offered first, since opening
    /// another world replaces everything in memory.</summary>
    public async Task OpenWorldAsync(string path, bool remember)
    {
        if (!Directory.Exists(path))
        {
            if (ShowAlertAsync is not null)
                await ShowAlertAsync(EditorStrings.Format(EditorStrings.World_NotFound, ("Path", (object?)path)));
            Forget(path);
            return;
        }
        if (!await ConfirmDiscardIfDirtyAsync()) return;

        EditorPaths.OpenWorld(path);
        EditorLog.Info("Opening world {Path}.", path);

        IsLoading = true;
        LoadingStatus = EditorStrings.Get(EditorStrings.MainWindow_LoadingData);
        try
        {
            await _data.LoadOfflineAsync();
            RefreshEditors(online: false);
            MarkSourceSeen();
            SelectedSection = _sectionMap["Maps"];
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }

        if (remember) Remember(path);
        NotifyWorldChanged();
    }

    [RelayCommand]
    private async Task CloseWorldAsync()
    {
        if (!HasWorld) return;
        if (!await ConfirmDiscardIfDirtyAsync()) return;
        EditorLog.Info("Closing world {Path}.", EditorPaths.Data);
        EditorPaths.OpenWorld("");
        // The records go with it: the lists refill from these arrays, so they have to be empty too.
        _data.ClearOffline();
        RefreshEditors(online: false);
        SelectedSection = null;
        CurrentEditor = null;
        NotifyWorldChanged();
    }

    /// <summary>Opens one from the recent list.</summary>
    private Task OpenRecentWorldAsync(string path) =>
        string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : OpenWorldAsync(path, remember: true);

    /// <summary>Set by the View: shows the world's record ceilings and answers with the new ones.</summary>
    public Func<WorldSettingsDialogViewModel, Task>? ShowWorldSettingsDialogAsync { get; set; }

    /// <summary>The world's name, its default map size and its record ceilings. A confirmed change is
    /// written to the folder and the world reread, since every list is sized from it.</summary>
    [RelayCommand]
    private async Task WorldSettingsAsync()
    {
        if (ShowWorldSettingsDialogAsync is null || !HasWorld) return;
        var dlg = new WorldSettingsDialogViewModel(_data.Manifest, IsOnline);
        WorldManifest? chosen = null;
        dlg.Confirmed += manifest => chosen = manifest;
        await ShowWorldSettingsDialogAsync(dlg);
        if (chosen is null) return;

        await EditorDataService.SaveManifestAsync(EditorPaths.Data, chosen);
        EditorLog.Info("World settings written to {Path}.", EditorPaths.Data);
        await OpenWorldAsync(EditorPaths.Data, remember: false);
    }

    /// <summary>Set by the View: shows the world check's findings.</summary>
    public Func<WorldCheckDialogViewModel, Task>? ShowWorldCheckDialogAsync { get; set; }

    /// <summary>Reads every map against every other and reports what does not agree — a link joining two
    /// sizes, a warp naming a tile its destination has not got, a group index no group backs. Records only,
    /// so it needs no server and changes nothing.</summary>
    [RelayCommand]
    private async Task CheckWorldAsync()
    {
        if (ShowWorldCheckDialogAsync is null || (!HasWorld && !IsOnline)) return;

        var groups = MapGroupEditor.MapGroups.Select(g => g.Index).ToHashSet();
        var world = new WorldContent
        {
            Maps = Slots(MapEditor.Maps, r => r.Index, r => r.Record),
            Items = Slots(ItemEditor.Items, r => r.Index, r => r.ToRecord()),
            Npcs = Slots(NpcEditor.Items, r => r.Index, r => r.ToRecord()),
            Shops = Slots(ShopEditor.Items, r => r.Index, r => r.ToRecord()),
            Spells = Slots(SpellEditor.Items, r => r.Index, r => r.ToRecord()),
            Quests = Slots(QuestEditor.Items, r => r.Index, r => r.ToRecord()),
            Conversations = Slots(ConversationEditor.Items, r => r.Index, r => r.ToRecord()),
            Classes = Slots(ClassEditor.Items, r => r.Index, r => r.ToRecord()),
            GroupExists = groups.Contains,
        };

        var dlg = new WorldCheckDialogViewModel(WorldCheck.Run(world), NameOf);
        dlg.Navigate += GoTo;
        await ShowWorldCheckDialogAsync(dlg);
    }

    /// <summary>An editor's rows as the 1-based array every record family is held in, index 0 unused.</summary>
    private static TRecord?[] Slots<TRow, TRecord>(IReadOnlyList<TRow> rows, Func<TRow, int> numOf,
                                                   Func<TRow, TRecord> recordOf) where TRecord : class
    {
        int max = 0;
        foreach (var row in rows) max = Math.Max(max, numOf(row));
        var all = new TRecord?[max + 1];
        foreach (var row in rows)
        {
            int n = numOf(row);
            if (n > 0) all[n] = recordOf(row);
        }

        return all;
    }

    /// <summary>Follows a finding to the record it is on. A tile-scoped one goes to its map and no further:
    /// selecting a tile belongs to the canvas control rather than here, and the row names the coordinates.</summary>
    private void GoTo(WorldRecordKind kind, int num)
    {
        switch (kind)
        {
            case WorldRecordKind.Map: OpenMap(num); break;
            case WorldRecordKind.Item: Open("Items", ItemEditor, num); break;
            case WorldRecordKind.Npc: Open("NPCs", NpcEditor, num); break;
            case WorldRecordKind.Shop: Open("Shops", ShopEditor, num); break;
            case WorldRecordKind.Spell: Open("Spells", SpellEditor, num); break;
            case WorldRecordKind.Quest: Open("Quests", QuestEditor, num); break;
            case WorldRecordKind.Conversation: Open("Conversations", ConversationEditor, num); break;
            case WorldRecordKind.Class: Open("Classes", ClassEditor, num); break;
        }
    }

    /// <summary>What to call the record a finding is on, for the row that names it.</summary>
    private string NameOf(WorldRecordKind kind, int num) => kind switch
    {
        WorldRecordKind.Map => MapEditor.Maps.FirstOrDefault(m => m.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Item => ItemEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Npc => NpcEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Shop => ShopEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Spell => SpellEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Quest => QuestEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        WorldRecordKind.Conversation => ConversationEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
        _ => ClassEditor.Items.FirstOrDefault(r => r.Index == num)?.DisplayName ?? "",
    };

    private void NotifyWorldChanged()
    {
        OnPropertyChanged(nameof(HasWorld));
        OnPropertyChanged(nameof(ShowEmptyWorld));
        OnPropertyChanged(nameof(WorldPath));
        OnPropertyChanged(nameof(WorldLabel));
        OnPropertyChanged(nameof(RecentWorlds));
    }

    private static void Remember(string path)
    {
        var s = AppSettings.Current;
        s.RecentWorlds.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        s.RecentWorlds.Insert(0, path);
        if (s.RecentWorlds.Count > 8) s.RecentWorlds.RemoveRange(8, s.RecentWorlds.Count - 8);
        s.LastWorldPath = path;
        s.Save();
    }

    /// <summary>Removes a missing path from the recent list.</summary>
    private void Forget(string path)
    {
        var s = AppSettings.Current;
        if (s.RecentWorlds.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) == 0) return;
        if (string.Equals(s.LastWorldPath, path, StringComparison.OrdinalIgnoreCase)) s.LastWorldPath = null;
        s.Save();
        OnPropertyChanged(nameof(RecentWorlds));
    }

    /// <summary>Offers to keep unsaved work before something replaces it. False means the person backed out
    /// and whatever was about to happen must not.</summary>
    private async Task<bool> ConfirmDiscardIfDirtyAsync()
    {
        var dirty = GetAllDirty().ToList();
        if (dirty.Count == 0 || ShowPushChangesDialogAsync is null) return true;
        var vm = new PushChangesDialogViewModel(dirty, _conn, _data, isConnecting: true);
        bool go = false;
        vm.ProceedConfirmed += () => go = true;
        await ShowPushChangesDialogAsync(vm);
        return go;
    }
}
