using Mirage.Editor.Services;
using Mirage.Shared.Protocol.Packets;
using System.ComponentModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The map editor's half of record locking and live sync.
///
/// <para>It is written separately from <see cref="EditorViewModelBase{TRow}"/>, which the other eight
/// editors share, because a map row is not one of those: it holds a whole <see cref="Mirage.Shared.Records.MapRecord"/>
/// rather than mirroring fields, and announces its own changes. The rules are the same ones — claim on dirty,
/// give back on clean, take whatever the server pushes — only the plumbing differs.</para>
/// </summary>
public sealed partial class MapEditorViewModel
{
    private const string LockSection = "Maps";

    /// <summary>The shared table, assigned by the shell. Null offline.</summary>
    public EditorLockState? Locks { get; set; }

    private readonly HashSet<int> _lockWatched = [];

    /// <summary>Starts watching a row's dirty flag, so going dirty claims the map and coming clean gives it
    /// back. Called as rows are built; watching twice is harmless.</summary>
    public void WatchForLocks(MapRowViewModel row)
    {
        if (!_lockWatched.Add(row.Index)) return;
        row.PropertyChanged += OnMapRowChanged;
    }

    private void OnMapRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MapRowViewModel.IsDirty)) return;
        if (sender is not MapRowViewModel row || Locks is null || !_conn.IsConnected) return;
        _ = row.IsDirty
            ? _conn.SendLockAsync(LockSection, row.Index)
            : _conn.SendUnlockAsync(LockSection, row.Index);
    }

    /// <summary>Re-reads every row's indicator from the table.</summary>
    public void RefreshLockState()
    {
        if (Locks is null) return;
        foreach (var row in Maps)
        {
            string? holder = Locks.HolderOf(LockSection, row.Index);
            row.LockHolder = holder ?? "";
            row.LockedByOther = Locks.IsHeldByOther(LockSection, row.Index);
        }
        OnPropertyChanged(nameof(IsSelectedLocked));
    }

    /// <summary>Whether the open map is held by somebody else — the canvas and the panel both go dead.</summary>
    public bool IsSelectedLocked => SelectedMap?.LockedByOther == true;

    /// <summary>Takes a map the server pushed after somebody else saved it. A row this session has dirtied is
    /// one it holds the lock on, so a push and local edits cannot both exist — but the check stays, because
    /// silently discarding somebody's work on a wrong assumption is the one outcome worth ruling out.</summary>
    public void OnMapLivePushed(SendMapPacket p)
    {
        var row = Maps.FirstOrDefault(m => m.Index == p.MapNum);
        if (row is null || row.IsDirty) return;
        // LoadRecord announces Record, and the selected row is already subscribed to that — so the open
        // map redraws from this without a second call.
        row.LoadRecord(EditorDataService.MapRecordFromPacket(p));
    }
}
