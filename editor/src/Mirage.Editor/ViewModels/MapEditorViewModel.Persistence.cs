using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>Saving maps to the server and exporting clean map art to PNG.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // ── Map save ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        var dirty = Maps.Where(m => m.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_NoDirtyMaps);
            return;
        }
        int saved = 0;
        foreach (var vm in dirty)
        {
            try
            {
                // Bump before save in both modes — server is authoritative when online (ignores the
                // packet's Revision and does its own map.Revision++), so the local bump is just a UI
                // mirror; offline, the bump must land before disk write so the file carries the new
                // value. See MapRowViewModel.BumpRevision for the full rationale.
                vm.BumpRevision();
                if (_data.IsOnline)
                {
                    await _conn.SendSaveAsync(EditorDataService.BuildSaveMapPacket(vm.Index, vm.Record));
                    _data.PatchOnlineMapName(vm.Index, vm.Record.Name);
                }
                else
                {
                    await _data.SaveOfflineMapAsync(vm.Index, vm.Record);
                }
                vm.ClearDirty();
                saved++;
            }
            catch (Exception ex)
            {
                StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_SaveError,
                    ("Index", vm.Index), ("Error", ex.Message));
                return;
            }
        }
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_SavedCount,
            ("Count", saved));
    }

    [RelayCommand]
    private async Task SaveMapAsync()
    {
        if (SelectedMap is null) return;
        var vm = SelectedMap;
        var map = vm.Record;

        try
        {
            // Bump before save in both modes — see MapRowViewModel.BumpRevision for the rationale
            // (server is authoritative when online; offline must persist the new value to disk).
            vm.BumpRevision();
            if (_data.IsOnline)
            {
                await _conn.SendSaveAsync(EditorDataService.BuildSaveMapPacket(vm.Index, map));
                _data.PatchOnlineMapName(vm.Index, map.Name);
            }
            else
            {
                await _data.SaveOfflineMapAsync(vm.Index, map);
            }
            vm.ClearDirty();
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_MapSaved,
                ("Index", vm.Index));
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_SaveFailed,
                ("Error", ex.Message));
        }
    }

    // ── Auto-save ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public int DirtyCount => Maps.Count(m => m.IsDirty);

    /// <inheritdoc />
    public string OpenRecordName => SelectedMap?.Record.Name ?? "";

    /// <summary>Write dirty maps to disk on the auto-save schedule.
    ///
    /// <para>Skipped while a paint batch is open. A drag is one edit as far as undo is concerned, and a
    /// tick landing mid-stroke would persist half of it — recoverable, but the file on disk would hold a
    /// state the author never stopped at.</para>
    ///
    /// <para>The revision bump is the same one a manual save does, and it has to be: clients decide
    /// whether their cached tiles are stale by comparing it, so a save that did not bump would leave them
    /// showing the map as it was.</para></summary>
    public async Task<int> AutoSaveAsync(AutoSaveReach reach)
    {
        if (_batchOpen) return 0;

        var targets = reach == AutoSaveReach.OpenRecord
            ? (SelectedMap is { IsDirty: true } open ? [open] : new List<MapRowViewModel>())
            : Maps.Where(m => m.IsDirty).ToList();
        if (targets.Count == 0) return 0;

        int saved = 0;
        EditorLog.Info("Auto-save writing {Count} dirty map(s), reach {Reach}.", targets.Count, reach);
        foreach (var vm in targets)
        {
            vm.BumpRevision();
            await _data.SaveOfflineMapAsync(vm.Index, vm.Record);
            vm.ClearDirty();
            saved++;
        }
        NotifyMapDirtyState();
        return saved;
    }

    // ── Export to PNG (clean map art: base Ground+Fringe, no grid or overlays) ──

    public bool HasSelectedMap => SelectedMap is not null;

    // Full record for a map id with no side effects (no SelectedMap change, no dirtying): offline it is
    // already in memory; online it fetches once and caches via LoadRecord (reads links off the packet).
    private async Task<MapRecord?> FetchMapForExportAsync(int id)
    {
        if (id <= 0) return null;
        if (!_data.IsOnline)
            return id < _data.OfflineMaps.Length ? _data.OfflineMaps[id] : null;
        var row = RowFor(id);
        if (row is { IsLoaded: true }) return row.Record;
        var pkt = await _conn.RequestMapAsync(id);
        if (pkt is null) return null;
        var rec = EditorDataService.MapRecordFromPacket(pkt);
        row?.LoadRecord(rec);
        return rec;
    }

    // "map-0007-Town Square-world.png" — id-padded + sanitized map name; `suffix` distinguishes variants.
    private static string SuggestPngName(MapRowViewModel map, string suffix = "")
    {
        string name = string.IsNullOrWhiteSpace(map.Record.Name) ? "map" : map.Record.Name;
        string safe = PortableFileName.Sanitize(name);
        return $"map-{map.Index:0000}-{safe}{suffix}.png";
    }

    [RelayCommand]
    private async Task ExportMapAsync()
    {
        if (SelectedMap is null || SaveFilePngAsync is null) return;
        var map = SelectedMap;
        string? path = await SaveFilePngAsync(SuggestPngName(map));
        if (path is null) return;
        try
        {
            var rec = await FetchMapForExportAsync(map.Index);
            if (rec is null)
            {
                StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_ExportFailed_NoMap);
                return;
            }
            MapImageExport.SaveBitmap([(rec, 0, 0)], Tilesets,
                TileGridControl.MapPixelW(rec), TileGridControl.MapPixelH(rec), path);
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportedMap, ("Path", path));
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportFailed, ("Error", ex.Message));
        }
    }

    [RelayCommand]
    private async Task ExportObservableAreaAsync()
    {
        if (SelectedMap is null || SaveFilePngAsync is null) return;
        var map = SelectedMap;
        string? path = await SaveFilePngAsync(SuggestPngName(map, "-area"));
        if (path is null) return;
        try
        {
            if (_data.IsOnline) await EagerLoadNeighborsAsync(); // ensure the visible 3×3 ring is loaded
            int mw = TileGridControl.MapPixelW(SelectedMap?.Record), mh = TileGridControl.MapPixelH(SelectedMap?.Record);
            // 3×3 layout mirroring the on-screen observable area: the selected map is the center cell.
            (MapRecord? Map, int Cx, int Cy)[] cells =
            [
                (NeighborMapUpLeft,   0, 0), (NeighborMapUp,   1, 0), (NeighborMapUpRight,   2, 0),
                (NeighborMapLeft,     0, 1), (map.Record,      1, 1), (NeighborMapRight,     2, 1),
                (NeighborMapDownLeft, 0, 2), (NeighborMapDown, 1, 2), (NeighborMapDownRight, 2, 2),
            ];
            var placements = new List<(MapRecord, int, int)>(cells.Length);
            foreach (var (m, cx, cy) in cells)
                if (m is not null) placements.Add((m, cx * mw, cy * mh));
            MapImageExport.SaveBitmap(placements, Tilesets, mw * 3, mh * 3, path);
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportedMap, ("Path", path));
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportFailed, ("Error", ex.Message));
        }
    }

    [RelayCommand]
    private async Task ExportWorldAsync()
    {
        if (SelectedMap is null || SaveFilePngAsync is null) return;
        var origin = SelectedMap;
        int mw = TileGridControl.MapPixelW(SelectedMap?.Record), mh = TileGridControl.MapPixelH(SelectedMap?.Record);
        string? path = null;
        try
        {
            // Flood the neighbor graph onto an integer grid from the origin at (0,0); first-placement-wins
            // per map id and per cell (handles cycles and inconsistent links). Only side-effect-free reads
            // and non-dirtying LoadRecord (in FetchMapForExportAsync) are used — no SelectedMap change.
            var coordOf = new Dictionary<int, (int X, int Y)>();
            var cellUsed = new HashSet<(int, int)>();
            var recordOf = new Dictionary<int, MapRecord>();
            var queue = new Queue<int>();

            void Place(int id, int x, int y)
            {
                if (id <= 0 || coordOf.ContainsKey(id) || !cellUsed.Add((x, y))) return;
                coordOf[id] = (x, y);
                queue.Enqueue(id);
            }

            Place(origin.Index, 0, 0);
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                var rec = await FetchMapForExportAsync(id);
                if (rec is null) continue; // unreachable/failed → left as a black gap
                recordOf[id] = rec;
                var (x, y) = coordOf[id];
                Place(rec.Up, x, y - 1);
                Place(rec.Down, x, y + 1);
                Place(rec.Left, x - 1, y);
                Place(rec.Right, x + 1, y);
                StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportDiscovering,
                    ("Count", recordOf.Count));
            }
            if (recordOf.Count == 0) return;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var (x, y) in recordOf.Keys.Select(id => coordOf[id]))
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            int worldW = (maxX - minX + 1) * mw, worldH = (maxY - minY + 1) * mh;

            path = await SaveFilePngAsync(SuggestPngName(origin, "-world"));
            if (path is null) return;

            var placements = new List<(MapRecord, int, int)>(recordOf.Count);
            foreach (var (id, rec) in recordOf)
            {
                var (x, y) = coordOf[id];
                placements.Add((rec, (x - minX) * mw, (y - minY) * mh));
            }

            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportRendering,
                ("Width", worldW), ("Height", worldH));
            await Task.Yield(); // let the "Rendering..." status paint before the synchronous stream
            MapImageExport.ExportWorldPng(placements, Tilesets, worldW, worldH, path);
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportedWorld,
                ("Count", recordOf.Count), ("Path", path));
        }
        catch (Exception ex)
        {
            if (path is not null) { try { File.Delete(path); } catch { /* best-effort cleanup */ } }
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ExportFailed, ("Error", ex.Message));
        }
    }

    [RelayCommand]
    private async Task DiscardMapAsync()
    {
        if (SelectedMap is null || !SelectedMap.IsDirty) return;
        var vm = SelectedMap;
        if (_data.IsOnline)
        {
            await LoadMapAsync(vm);
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_MapDiscarded,
                ("Index", vm.Index));
        }
        else
        {
            var fresh = await _data.LoadSingleMapOfflineAsync(vm.Index);
            vm.LoadRecord(fresh);
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_MapDiscarded,
                ("Index", vm.Index));
        }
        vm.ClearDirty();
    }

    [RelayCommand]
    private async Task DiscardAllMapsAsync()
    {
        var dirty = Maps.Where(m => m.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_NoDirtyMaps);
            return;
        }
        foreach (var vm in dirty)
        {
            try
            {
                if (_data.IsOnline)
                {
                    await LoadMapAsync(vm);
                }
                else
                {
                    var fresh = await _data.LoadSingleMapOfflineAsync(vm.Index);
                    vm.LoadRecord(fresh);
                }
                vm.ClearDirty();
            }
            catch (Exception ex)
            {
                StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_DiscardError,
                    ("Index", vm.Index), ("Error", ex.Message));
                return;
            }
        }
        StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_AllDiscarded);
    }

    public IEnumerable<MapRowViewModel> GetDirty() => Maps.Where(m => m.IsDirty);

    public async Task SaveAllOfflineAsync()
    {
        foreach (var vm in Maps.Where(m => m.IsDirty).ToList())
        {
            vm.BumpRevision();
            await _data.SaveOfflineMapAsync(vm.Index, vm.Record);
            vm.ClearDirty();
        }
    }
}
