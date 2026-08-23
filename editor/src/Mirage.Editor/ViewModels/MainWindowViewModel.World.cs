using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;

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

    /// <summary>The world's record ceilings. A confirmed change is written to the folder and the world
    /// reread, since every list is sized from it.</summary>
    [RelayCommand]
    private async Task WorldSettingsAsync()
    {
        if (ShowWorldSettingsDialogAsync is null || !HasWorld) return;
        var dlg = new WorldSettingsDialogViewModel(_data.Limits, IsOnline);
        RecordLimits? chosen = null;
        dlg.Confirmed += limits => chosen = limits;
        await ShowWorldSettingsDialogAsync(dlg);
        if (chosen is null) return;

        await EditorDataService.SaveManifestAsync(EditorPaths.Data, chosen);
        EditorLog.Info("World record ceilings set on {Path}.", EditorPaths.Data);
        await OpenWorldAsync(EditorPaths.Data, remember: false);
    }

    private void NotifyWorldChanged()
    {
        OnPropertyChanged(nameof(HasWorld));
        OnPropertyChanged(nameof(ShowEmptyWorld));
        OnPropertyChanged(nameof(WorldPath));
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
