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

/// <summary>The attribute dialogs' bound state: input fields, the per-dialog retain checkboxes and
/// the values they carry between placements, plus the light-source and tile-animation dialogs.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // ── Attribute dialog input fields (bound to the open dialog) ─────────────
    // Always zeroed for a blank tile click, pre-filled from tile data for an
    // existing tile.  Cancel is safe: these never hold retained values.

    [ObservableProperty] private bool _showWarpDialog;
    [ObservableProperty] private short _warpMapNum;
    [ObservableProperty] private short _warpX;
    [ObservableProperty] private short _warpY;
    // Two-plane world (§1b): the logical layer the warp delivers you onto — Ground (default) or the Fringe deck.
    // Packed into the warp's Data3 alongside WarpY via WorldTarget (dest coords are well under a byte).
    [ObservableProperty] private WorldLayer _warpDestLayer = WorldLayer.Ground;

    [ObservableProperty] private bool _showItemDialog;
    [ObservableProperty] private short _itemTileNum;
    [ObservableProperty] private short _itemTileValue;
    [ObservableProperty] private short _itemTileRespawnSeconds;

    [ObservableProperty] private bool _showKeyDialog;
    [ObservableProperty] private short _keyItemNum;
    // Data2 = take flag (1 = consume key on use, 0 = keep).  Data3 = 0 (unused).
    [ObservableProperty] private bool _keyTake;

    [ObservableProperty] private bool _showKeyOpenDialog;
    // Data1/2 = coordinates of the Key (door) tile on the same map.
    [ObservableProperty] private short _keyOpenDoorX;
    [ObservableProperty] private short _keyOpenDoorY;
    // Data3 = the target door's WorldLayer (0 Ground / 1 Fringe) — a KeyOpen can open a Key door on EITHER plane,
    // independent of the plane the KeyOpen plate itself sits on (e.g. a ground plate opening a fringe-deck gate).
    [ObservableProperty] private WorldLayer _keyOpenDoorLayer = WorldLayer.Ground;

    [ObservableProperty] private bool _showNpcSpawnDialog;
    // The chosen eligible slot in the NPC-spawn pin picker (null until one is picked).
    [ObservableProperty] private NpcSpawnChoice? _npcSpawnChoice;
    // Eligible slots offered by the picker (non-empty NPC types not already pinned). Rebuilt each time it opens.
    public ObservableCollection<NpcSpawnChoice> NpcSpawnChoices { get; } = new();

    [ObservableProperty] private string _dialogError = "";

    // ── Per-dialog "retain values" checkboxes ────────────────────────────────
    [ObservableProperty] private bool _warpRetain;
    [ObservableProperty] private bool _itemRetain;
    [ObservableProperty] private bool _keyRetain;
    [ObservableProperty] private bool _keyOpenRetain;

    // ── Retained values (set only by Confirm when *Retain is true; Alt+Click) ──
    // Completely separate from the dialog fields so cancel never corrupts them.
    private bool _hasRetainedWarp;
    private short _retWarpMapNum, _retWarpX, _retWarpY;
    private WorldLayer _retWarpDestLayer;

    private bool _hasRetainedItem;
    private short _retItemNum, _retItemValue, _retItemRespawn;

    private bool _hasRetainedKey;
    private short _retKeyItemNum;
    private bool _retKeyTake;

    private bool _hasRetainedKeyOpen;
    private short _retKeyOpenDoorX, _retKeyOpenDoorY;
    private WorldLayer _retKeyOpenDoorLayer;

    // ── Light Sources dialog (Light mode) ─────────────────────────────────────
    [ObservableProperty] private bool _showLightDialog;
    [ObservableProperty] private Color _lightColor = ColorHex.ToColor(LightSpec.Torch.Rgb);
    partial void OnLightColorChanged(Color value) => OnPropertyChanged(nameof(LightColorHex));
    [ObservableProperty] private double _lightRadius = LightSpec.Torch.Radius;   // tiles
    [ObservableProperty] private FlickerStyle _lightFlicker = LightSpec.Torch.Flicker;
    [ObservableProperty] private int _lightIntensity = 100;                      // percent, 0..100
    [ObservableProperty] private bool _lightRetain;

    // Hex form of LightColor, kept two-way in sync with the color picker (edit either, both update).
    public string LightColorHex
    {
        get => $"{LightColor.R:X2}{LightColor.G:X2}{LightColor.B:X2}";
        set
        {
            if (ColorHex.TryParse(value, out var c))
            {
                DialogError = "";
                LightColor = c;
            }
            else
            {
                DialogError = EditorStrings.Get(EditorStrings.AttrDialog_InvalidColor);
            }
        }
    }

    // Retained light for Alt+Click quick-place (separate from dialog fields so Cancel never corrupts it).
    private bool _hasRetainedLight;
    private LightSpec _retLight = LightSpec.Torch;

    // pending tile footprint while a dialog is open (brush × 1 or N)
    private readonly List<(int X, int Y)> _pendingTiles = [];

    // ── Tile-animation dialog (Tile mode: click a placed tile whose selected layer is occupied) ──
    [ObservableProperty] private bool _showAnimDialog;
    public ObservableCollection<AnimLayerRow> AnimLayers { get; } = [];
    private int _animDialogX, _animDialogY;
    // One style per animated stack; the render helper reads each stack's style from its lowest anim layer.
    [ObservableProperty] private AnimStyle _groundAnimStyle;
    [ObservableProperty] private AnimStyle _fringeAnimStyle;
    // A stack's style picker only matters once it has 2+ animated layers (a lone anim layer just blinks).
    public bool GroundStyleEnabled => AnimLayers.Count(r => r.IsAnim && r.Type == LayerType.Ground) >= 2;
    public bool FringeStyleEnabled => AnimLayers.Count(r => r.IsAnim && r.Type == LayerType.Fringe) >= 2;
    public IReadOnlyList<AnimStyle> AnimStyles { get; } = Enum.GetValues<AnimStyle>();

    // Tracks the map row we've subscribed to for Record changes
    private MapRowViewModel? _subscribedMap;
    private readonly List<MapRowViewModel> _subscribedMapRows = [];
    private readonly HashSet<int> _loadingNeighborIds = [];

    public bool IsSelectedMapDirty => SelectedMap is not null && SelectedMap.IsDirty;
    public bool HasAnyDirtyMap => Maps.Any(m => m.IsDirty);

    private void NotifyMapDirtyState()
    {
        OnPropertyChanged(nameof(IsSelectedMapDirty));
        OnPropertyChanged(nameof(HasAnyDirtyMap));
    }

    private void HookMaps()
    {
        Maps.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _subscribedMapRows)
                    row.PropertyChanged -= OnMapItemPropertyChanged;
                _subscribedMapRows.Clear();
                NotifyMapDirtyState();
                OnPropertyChanged(nameof(FilteredMaps));
                OnPropertyChanged(nameof(FilterStatus));
                return;
            }
            if (e.NewItems is not null)
            {
                foreach (MapRowViewModel row in e.NewItems.Cast<MapRowViewModel>())
                {
                    row.PropertyChanged += OnMapItemPropertyChanged;
                    _subscribedMapRows.Add(row);
                }
            }

            if (e.OldItems is not null)
            {
                foreach (MapRowViewModel row in e.OldItems.Cast<MapRowViewModel>())
                {
                    row.PropertyChanged -= OnMapItemPropertyChanged;
                    _subscribedMapRows.Remove(row);
                }
            }

            NotifyMapDirtyState();
            OnPropertyChanged(nameof(FilteredMaps));
            OnPropertyChanged(nameof(FilterStatus));
        };
    }

    private void OnMapItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsDirty")
            NotifyMapDirtyState();
    }

    public ObservableCollection<MapRowViewModel> Maps { get; } = [];
    public IEnumerable<MapRowViewModel> FilteredMaps =>
        string.IsNullOrEmpty(FilterText)
            ? Maps
            : Maps.Where(m => m.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
    public bool IsFilterActive => !string.IsNullOrEmpty(FilterText);
    public string FilterStatus => EditorStrings.Format(EditorStrings.Status_FilterCount,
        ("Filtered", FilteredMaps.Count()), ("Total", Maps.Count));
    [RelayCommand] private void ClearFilter() => FilterText = "";

    public IEnumerable<AttributeTool> Attributes { get; } = Enum.GetValues<AttributeTool>();
    // Tri-state Moral choices for the map's inherit/override ComboBox. Rebuilt on language change.
    public IReadOnlyList<MoralChoice> MoralOptions { get; private set; } = MoralChoices.Build();
    public IEnumerable<FlickerStyle> FlickerStyles { get; } = Enum.GetValues<FlickerStyle>();

    // Convenience bool properties for RadioButton bindings
    public bool IsTileMode
    {
        get => SelectedMode == EditorMode.Tile;
        set { if (value) SelectedMode = EditorMode.Tile; }
    }

    public bool IsAttributeMode
    {
        get => SelectedMode == EditorMode.Attribute;
        set { if (value) SelectedMode = EditorMode.Attribute; }
    }

    public bool IsLightMode
    {
        get => SelectedMode == EditorMode.Light;
        set { if (value) SelectedMode = EditorMode.Light; }
    }

    // The Ground/Fringe logical-layer selector is shown in BOTH Attribute and Light modes (each authors on a
    // chosen plane — attributes / lights); Tile mode uses the visual-stack (LayerType) selector instead.
    public bool IsLayerAuthoringMode => IsAttributeMode || IsLightMode;

    partial void OnSelectedModeChanged(EditorMode value)
    {
        OnPropertyChanged(nameof(IsTileMode));
        OnPropertyChanged(nameof(IsAttributeMode));
        OnPropertyChanged(nameof(IsLightMode));
        OnPropertyChanged(nameof(IsLayerAuthoringMode));
        OnPropertyChanged(nameof(BrushSizeVisible));
        OnPropertyChanged(nameof(StatusModeText));
    }

    public bool IsPlaceAction
    {
        get => SelectedAction == EditorAction.Place;
        set { if (value) SelectedAction = EditorAction.Place; }
    }

    public bool IsSelectAction
    {
        get => SelectedAction == EditorAction.Select;
        set { if (value) SelectedAction = EditorAction.Select; }
    }

    public bool IsDeleteAction
    {
        get => SelectedAction == EditorAction.Delete;
        set { if (value) SelectedAction = EditorAction.Delete; }
    }

    // The brush-size control shows in Attribute mode (attribute brush) AND whenever the Delete action is active
    // (its erase brush works in every mode).
    public bool BrushSizeVisible => IsAttributeMode || IsDeleteAction;

    partial void OnSelectedActionChanged(EditorAction value)
    {
        OnPropertyChanged(nameof(IsPlaceAction));
        OnPropertyChanged(nameof(IsSelectAction));
        OnPropertyChanged(nameof(IsDeleteAction));
        OnPropertyChanged(nameof(BrushSizeVisible));
        OnPropertyChanged(nameof(StatusActionText));
    }

    // Routes map name edits through the ViewModel so the list item DisplayName updates live.
    public string MapName
    {
        get => SelectedMap?.Record.Name ?? "";
        set
        {
            if (SelectedMap is null) return;
            SelectedMap.Record.Name = value;
            SelectedMap.NotifyDisplayName();
        }
    }

    // Player-facing name. Routed through NotifyDisplayName so the list row's parenthetical updates live.
    public string MapDisplayName
    {
        get => SelectedMap?.Record.DisplayName ?? "";
        set
        {
            if (SelectedMap is null) return;
            SelectedMap.Record.DisplayName = value;
            SelectedMap.NotifyDisplayName();
        }
    }

    // Pass-through properties for map record fields — each setter marks the map dirty
    // so that edits to numeric fields and NPC slots register correctly.

    // Tri-state Moral: "(Inherit)" (null) or an explicit MapMoral. The map's own value overrides
    // its group; null inherits the group (else the hard default None).
    public MoralChoice? SelectedMapMoral
    {
        get => MoralOptions.FirstOrDefault(c => c.Value == SelectedMap?.Record.Moral) ?? MoralOptions[0];
        set
        {
            if (SelectedMap is null || value is null || SelectedMap.Record.Moral == value.Value) return;
            SelectedMap.Record.Moral = value.Value;
            SelectedMap.MarkDirty();
            OnPropertyChanged(nameof(SelectedMapMoral));
        }
    }
    public int MapUp
    {
        get => SelectedMap?.Record.Up ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Up == value) return;
            SelectedMap.Record.Up = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapDown
    {
        get => SelectedMap?.Record.Down ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Down == value) return;
            SelectedMap.Record.Down = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapLeft
    {
        get => SelectedMap?.Record.Left ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Left == value) return;
            SelectedMap.Record.Left = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapRight
    {
        get => SelectedMap?.Record.Right ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Right == value) return;
            SelectedMap.Record.Right = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapMusic
    {
        get => SelectedMap?.Record.Music ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Music == value) return;
            SelectedMap.Record.Music = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapBootMap
    {
        get => SelectedMap?.Record.BootMap ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.BootMap == value) return;
            SelectedMap.Record.BootMap = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapBootX
    {
        get => SelectedMap?.Record.BootX ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.BootX == value) return;
            SelectedMap.Record.BootX = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapBootY
    {
        get => SelectedMap?.Record.BootY ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.BootY == value) return;
            SelectedMap.Record.BootY = value;
            SelectedMap.MarkDirty();
        }
    }
    // Map-enter/leave greeting, authored per map (shops are not map-bound).
    // Blank on the map inherits the field from its MapGroup; blank everywhere = no greeting.
    public string MapGreetingSpeaker
    {
        get => SelectedMap?.Record.GreetingSpeaker ?? "";
        set
        {
            if (SelectedMap is null || SelectedMap.Record.GreetingSpeaker == value) return;
            SelectedMap.Record.GreetingSpeaker = value;
            SelectedMap.MarkDirty();
        }
    }
    public string MapJoinSay
    {
        get => SelectedMap?.Record.JoinSay ?? "";
        set
        {
            if (SelectedMap is null || SelectedMap.Record.JoinSay == value) return;
            SelectedMap.Record.JoinSay = value;
            SelectedMap.MarkDirty();
        }
    }
    public string MapLeaveSay
    {
        get => SelectedMap?.Record.LeaveSay ?? "";
        set
        {
            if (SelectedMap is null || SelectedMap.Record.LeaveSay == value) return;
            SelectedMap.Record.LeaveSay = value;
            SelectedMap.MarkDirty();
        }
    }
    // Tri-state (null = inherit from the map group) — bound to IsThreeState CheckBoxes.
    public bool? MapIndoors
    {
        get => SelectedMap?.Record.Indoors;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Indoors == value) return;
            SelectedMap.Record.Indoors = value;
            SelectedMap.MarkDirty();
        }
    }
    public bool? MapAlwaysDark
    {
        get => SelectedMap?.Record.AlwaysDark;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.AlwaysDark == value) return;
            SelectedMap.Record.AlwaysDark = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapGroup
    {
        get => SelectedMap?.Record.MapGroup ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.MapGroup == value) return;
            SelectedMap.Record.MapGroup = value;
            SelectedMap.MarkDirty();
        }
    }
    // Server-bumped revision.  Display-only — surfaces what the live server has (or will have after
    // the next push); editing it client-side would just be cosmetic since the server ignores it.
    public int MapRevision => SelectedMap?.Record.Revision ?? 0;
    // Status-bar readout: "Revision: N" (built like SelectedLayerLabel, reusing the localized label).
    public string MapRevisionText =>
        $"{EditorStrings.Get(EditorStrings.MapEditor_RevisionLabel)} {MapRevision}";
    // Per-map dynamic NPC list: one facade row per Record.Npcs entry. "+" adds a row (up to
    // MaxMapNpcs), each row's "−" removes it, and only non-empty rows are saved — so a reload restores exactly the
    // authored rows. Rebuilt on map switch; refreshed on entry-list change. Only authored rows are listed,
    // never the full 1..MaxMapNpcs slot range.
    public ObservableCollection<MapNpcRowViewModel> MapNpcRows { get; } = new();

    /// <summary>True when the map has no NPC rows yet — drives the editor's empty-state hint.</summary>
    public bool HasNoNpcRows => MapNpcRows.Count == 0;

    // Localized placeholder for every NPC-row autocomplete box (rows delegate here so it stays one source).
    public string NpcSlotPlaceholder => EditorStrings.Get(EditorStrings.MapEditor_SearchNpcsPlaceholder);

    // Localized tooltip for each row's "place on map" button (rows delegate here).
    public string NpcPlaceTooltip => EditorStrings.Get(EditorStrings.MapEditor_PlaceNpcTooltip);

    // NPC footprint size (EffectiveSize) for the size-aware placement overlay + validation.
    // A method group so it binds to TileGridControl.NpcSizeLookup and MapNpcPlacement.ValidatePin's size Func.
    public int NpcSize(int npcNum) => _data.NpcSize(npcNum);

    // True when (x,y) lies under any pinned NPC entry's SxS footprint on this map. A placed NPC reserves its
    // whole footprint (top-left anchor), so tile attributes can't be written under it — collision is
    // bidirectional (MapNpcPlacement.ValidatePin already blocks the reverse: pinning onto a non-Walkable
    // attribute tile, since it requires the whole footprint to be Walkable).
    private bool TileCoveredByPinnedFootprint(MapRecord map, int x, int y)
    {
        foreach (var e in map.Npcs)
        {
            if (e.HasPin && WorldCoordHelper.FootprintContains(e.PinX!.Value, e.PinY!.Value, NpcSize(e.Npc), x, y))
                return true;
        }

        return false;
    }

    // True when any pinned entry on this map spawns NPC #npcNum — used to scope the resize re-prompt.
    private static bool MapPinsNpc(MapRecord map, int npcNum)
    {
        foreach (var e in map.Npcs)
            if (e.HasPin && e.Npc == npcNum) return true;
        return false;
    }

    // A live NPC-size change arrived (any editor's NPC save → server SendToAll(UpdateNpc); marshaled to the UI
    // thread by MainWindowViewModel). OnlineNpcSizes is otherwise a connect-time snapshot, so refresh the size the
    // footprint overlay + placement validation read, redraw the active map, and flag every LOADED map that pins
    // this NPC dirty — a resize can turn a prior-valid pin into an off-map/overlap, so the author is re-prompted
    // to re-validate + re-save (re-saving runs the HandleEditorSaveMap backstop, which drops any now-invalid pin).
    public void OnNpcLiveUpdated(int npcNum, int newSize)
    {
        _data.UpdateOnlineNpcSize(npcNum, newSize);

        if (SelectedMap is not null && MapPinsNpc(SelectedMap.Record, npcNum))
            InvalidateAllTiles?.Invoke();   // full render-cache rebuild so footprints redraw at the new size

        foreach (var m in Maps)
        {
            if (m.IsLoaded && MapPinsNpc(m.Record, npcNum))
                m.MarkDirty();
        }
    }

    // Localized "can't place here" reason for a failed footprint validation.
    private static string PlacementErrorText(NpcPlacementError err) => err switch
    {
        NpcPlacementError.OffMap => EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceOffMap),
        NpcPlacementError.OnBlocked => EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceOnBlocked),
        NpcPlacementError.Overlap => EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceOverlap),
        _ => "",
    };

    // The NPC-type entry at row `index` (0-based), or null if empty / out of range.
    public NamedEntry? NpcEntryForRow(int index)
    {
        var npcs = SelectedMap?.Record.Npcs;
        return npcs is not null && index >= 0 && index < npcs.Count
            ? EntryFor(_data.LiveNpcEntries, npcs[index].Npc) : null;
    }

    // Write the NPC TYPE into row `index`. Clearing it (id 0) also drops the row's fixed-spawn pin (an empty row
    // can't stay pinned). Marks the map dirty + refreshes the row; a dropped pin is cleared from the grid.
    public void SetRowNpc(int index, NamedEntry? value)
    {
        if (SelectedMap is null) return;
        var npcs = SelectedMap.Record.Npcs;
        if (index < 0 || index >= npcs.Count) return;
        int id = value?.Id ?? 0;
        var cur = npcs[index];
        if (cur.Npc == id) return;
        if (id == 0)
        {
            npcs[index] = cur with { Npc = 0, PinX = null, PinY = null };
            if (cur.HasPin) InvalidateTileGrid?.Invoke(cur.PinX!.Value, cur.PinY!.Value);
        }
        else
        {
            npcs[index] = cur with { Npc = id };
        }

        SelectedMap.MarkDirty();
        RowAt(index)?.Refresh();
    }

    // "@ x,y" when row `index` is pinned to a fixed spawn tile (else "" = spawns randomly) — the row read-out.
    public string NpcPlacementLabel(int index)
    {
        var npcs = SelectedMap?.Record.Npcs;
        if (npcs is null || index < 0 || index >= npcs.Count) return "";
        var e = npcs[index];
        return e.HasPin ? $"@ {e.PinX},{e.PinY}" : "";
    }

    private MapNpcRowViewModel? RowAt(int index)
        => index >= 0 && index < MapNpcRows.Count ? MapNpcRows[index] : null;

    // Add an empty NPC row (appended, so existing indices don't shift). Capped at MaxMapNpcs runtime posts.
    [RelayCommand(CanExecute = nameof(CanAddNpcRow))]
    private void AddNpcRow()
    {
        if (SelectedMap is null) return;
        SelectedMap.Record.Npcs.Add(new MapNpcEntry(0, null, null));
        MapNpcRows.Add(new MapNpcRowViewModel(this, MapNpcRows.Count));
        SelectedMap.MarkDirty();
        OnPropertyChanged(nameof(HasNoNpcRows));
        AddNpcRowCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddNpcRow() => SelectedMap is not null && MapNpcRows.Count < Constants.MaxMapNpcs;

    // Remove a row (and its entry). Later rows slide down a post, so any tile-undo pin ops keyed by the old
    // indices are cleared — a deliberate structural edit, like the shop's non-undoable trade-row removal.
    [RelayCommand]
    private void RemoveNpcRow(MapNpcRowViewModel row)
    {
        if (SelectedMap is null) return;
        int index = row.Index;
        var npcs = SelectedMap.Record.Npcs;
        if (index < 0 || index >= npcs.Count) return;
        PlacingNpcRow = -1;   // a row removal shifts indices — abandon any in-progress placement
        npcs.RemoveAt(index);
        MapNpcRows.RemoveAt(index);
        ReindexNpcRows();
        AdjustPinOpsAfterRemoval(index);   // shift/void queued pin ops in place so the tile-undo history survives
        SelectedMap.MarkDirty();
        OnPropertyChanged(nameof(HasNoNpcRows));
        AddNpcRowCommand.NotifyCanExecuteChanged();
        // The reindex shifted every later pin's post-number label (and dropped the removed row's pin), so redraw
        // all markers rather than a single tile.
        InvalidateAllTiles?.Invoke();
    }

    // Re-stamp each row's Index to its list position after a removal so the facades track the shifted entries.
    private void ReindexNpcRows()
    {
        for (int i = 0; i < MapNpcRows.Count; i++)
        {
            MapNpcRows[i].Index = i;
            MapNpcRows[i].Refresh();
        }
    }

    // Rebuild the row facades from the selected map's entries (called on map switch).
    private void RebuildMapNpcRows()
    {
        PlacingNpcRow = -1;   // switching maps invalidates a row-bound placement in progress
        MapNpcRows.Clear();
        var npcs = SelectedMap?.Record.Npcs;
        if (npcs is not null)
        {
            for (int i = 0; i < npcs.Count; i++)
                MapNpcRows.Add(new MapNpcRowViewModel(this, i));
        }

        OnPropertyChanged(nameof(HasNoNpcRows));
        AddNpcRowCommand.NotifyCanExecuteChanged();
    }

    // A row removal shifts entry indices, so fix up the entry-index keys in every queued NPC-spawn pin op (undo
    // AND redo) IN PLACE — preserving the whole undo history instead of clearing it. Before/After are the entry
    // index pinned at the op's tile: the removed index → null (that entry, and its pin, are gone, so the op
    // degrades to a harmless "clear this tile" no-op), an index past the removed one shifts down by one.
    private void AdjustPinOpsAfterRemoval(int removedIndex)
    {
        ShiftPinOps(_undoStack, removedIndex);
        ShiftPinOps(_redoStack, removedIndex);
    }

    private static void ShiftPinOps(Stack<List<UndoOp>> stack, int removedIndex)
    {
        // The stack's batches are mutable List references; editing an op in place persists without disturbing the
        // stack order, so history depth and CanUndo/CanRedo are untouched.
        foreach (var batch in stack)
        {
            for (int i = 0; i < batch.Count; i++)
            {
                if (batch[i] is NpcSpawnOp op)
                {
                    batch[i] = op with
                    {
                        Before = ShiftPinIndex(op.Before, removedIndex),
                        After = ShiftPinIndex(op.After, removedIndex)
                    };
                }
            }
        }
    }

    private static int? ShiftPinIndex(int? entryIndex, int removedIndex)
    {
        if (entryIndex is not int i) return null;
        if (i == removedIndex) return null;      // the pinned entry was removed — no target remains
        return i > removedIndex ? i - 1 : i;     // entries after the removed one slid down a post
    }
}
