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

public sealed partial class MapEditorViewModel : ObservableObject
{
    private readonly EditorDataService _data;
    private readonly EditorConnection _conn;

    // Palette/active-sheet bitmap = Tilesets[SelectedTileset]; kept in sync by UpdateTileBitmap().
    [ObservableProperty] private Bitmap? _tileBitmap;
    [ObservableProperty] private MapRowViewModel? _selectedMap;
    // Layer selection: which layer type, which 1-based layer index within it, and whether new tiles are
    // painted with the per-layer Anim (blink) flag set.
    [ObservableProperty] private LayerType _selectedLayerType = LayerType.Ground;
    [ObservableProperty] private int _selectedLayerIndex = 1;
    [ObservableProperty] private bool _selectedAnim;
    // Paint-default animation style for newly-flagged animated layers (matters once a stack has 2+ frames).
    [ObservableProperty] private AnimStyle _selectedAnimStyle;
    // Loaded tilesets and the 0-based sheet index newly painted tiles are tagged with.
    [ObservableProperty] private IReadOnlyList<Bitmap?> _tilesets = [];
    [ObservableProperty] private int _selectedTileset;
    [ObservableProperty] private AttributeTool _selectedAttributeTool = AttributeTool.Blocked;
    [ObservableProperty] private TileStamp? _selectedStamp;
    [ObservableProperty] private int _attributeBrushSizeX = 1;
    [ObservableProperty] private int _attributeBrushSizeY = 1;
    [ObservableProperty] private EditorMode _selectedMode = EditorMode.Tile;
    [ObservableProperty] private EditorAction _selectedAction = EditorAction.Place;
    [ObservableProperty] private SelectionBox? _selectionRect;
    [ObservableProperty] private ClipboardKind _clipboardKind = ClipboardKind.None;
    // MODE 2 transient NPC placement: the 0-based Npcs row currently being placed via
    // its "place" button, or -1 when idle. While active, the grid shows a live footprint brush and a click pins
    // the row (validated); Esc / right-click / map-switch / row-removal cancels.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlacingNpc))]
    [NotifyPropertyChangedFor(nameof(PlacingNpcSize))]
    private int _placingNpcRow = -1;
    [ObservableProperty] private int[,]? _clipboardTiles;
    [ObservableProperty] private TileAttr[,]? _clipboardAttrs;
    [ObservableProperty] private LightSpec?[,]? _clipboardLights;
    [ObservableProperty] private int _hoveredX = -1;
    [ObservableProperty] private int _hoveredY = -1;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _filterText = "";
    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMaps));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    [ObservableProperty] private bool _isAnimPreview;
    partial void OnIsAnimPreviewChanged(bool value) => OnPropertyChanged(nameof(AnimPreviewLabel));
    public string AnimPreviewLabel => EditorStrings.Get(
        IsAnimPreview ? EditorStrings.MapEditor_AnimPreviewStop : EditorStrings.MapEditor_AnimPreviewStart);

    [ObservableProperty] private bool _isDoorPreview;
    partial void OnIsDoorPreviewChanged(bool value) => OnPropertyChanged(nameof(DoorPreviewLabel));
    public string DoorPreviewLabel => EditorStrings.Get(
        IsDoorPreview ? EditorStrings.MapEditor_DoorPreviewOpen : EditorStrings.MapEditor_DoorPreviewClosed);

    [ObservableProperty] private bool _isNightPreview;
    partial void OnIsNightPreviewChanged(bool value) => OnPropertyChanged(nameof(NightPreviewLabel));
    public string NightPreviewLabel => EditorStrings.Get(
        IsNightPreview ? EditorStrings.MapEditor_NightPreviewStop : EditorStrings.MapEditor_NightPreviewStart);

    partial void OnHoveredXChanged(int value)
    {
        OnPropertyChanged(nameof(HoveredText));
        NotifyHoveredTile();
    }
    partial void OnHoveredYChanged(int value)
    {
        OnPropertyChanged(nameof(HoveredText));
        NotifyHoveredTile();
    }
    partial void OnSelectedAttributeToolChanged(AttributeTool value)
    {
        OnPropertyChanged(nameof(SelectedAttribute));
        OnPropertyChanged(nameof(IsNpcSpawnTool));
        OnPropertyChanged(nameof(IsLayerRampTool));
        OnPropertyChanged(nameof(SelectedAttributeDescription));
    }

    // The TileType the current attribute tool writes to a tile — drives the grid preview + the tile-writing
    // switch in TileClicked. NpcSpawn has no TileType (it writes a MapRecord.Npcs entry's pin), so it maps to Walkable.
    public TileType SelectedAttribute => SelectedAttributeTool switch
    {
        AttributeTool.Blocked => TileType.Blocked,
        AttributeTool.Warp => TileType.Warp,
        AttributeTool.Item => TileType.Item,
        AttributeTool.NpcAvoid => TileType.NpcAvoid,
        AttributeTool.Key => TileType.Key,
        AttributeTool.KeyOpen => TileType.KeyOpen,
        AttributeTool.LayerRamp => TileType.LayerRamp,
        _ => TileType.Walkable,
    };

    // True when the "NpcSpawn" attribute tool is active — routes clicks to the pin picker instead of tile.Type.
    public bool IsNpcSpawnTool => SelectedAttributeTool == AttributeTool.NpcSpawn;
    // True when the LayerRamp tool is active.  The ramp is the sole layer-connector; it OCCUPIES BOTH planes
    // (no other attribute may share its tile, and it can't be placed onto a tile that already has one), stored
    // on FringeAttr.Type = LayerRamp with Data1 = the ground-side Direction you mount from.
    public bool IsLayerRampTool => SelectedAttributeTool == AttributeTool.LayerRamp;
    // The ground-side Direction written into a placed LayerRamp's Data1 (which way you mount the ramp from).
    // Bound to a dropdown shown only while the LayerRamp tool is active.
    [ObservableProperty] private Direction _layerRampDirection = Direction.Down;
    public IEnumerable<Direction> Directions { get; } = Enum.GetValues<Direction>();
    public string LayerRampDirLabel => EditorStrings.Get(EditorStrings.MapEditor_LayerRampDir);

    // Uniform two-plane world: Attribute placement targets a logical layer.  Ground writes the tile's inline
    // attribute; Fringe writes its FringeAttr sub-record (the fringe plane is walkable by default, so a Fringe
    // attribute is a wall/warp/door "up top").  The LayerRamp tool ignores this — a ramp always occupies both.
    [ObservableProperty] private WorldLayer _selectedAttributeLayer = WorldLayer.Ground;
    public IEnumerable<WorldLayer> AttributeLayers { get; } = Enum.GetValues<WorldLayer>();
    public string AttrLayerLabel => EditorStrings.Get(EditorStrings.MapEditor_AttrLayer);
    public bool AttrLayerIsFringe => SelectedAttributeLayer == WorldLayer.Fringe;
    partial void OnSelectedAttributeLayerChanged(WorldLayer value)
    {
        OnPropertyChanged(nameof(AttrLayerIsFringe));
        InvalidateAllTiles?.Invoke();   // the attribute overlay shows the ACTIVE layer's attrs — repaint on switch
    }

    // ── Two-plane attribute helpers (uniform world) ────────────────────────────
    // A ramp occupies the whole tile on BOTH planes: nothing else may be authored there, and a ramp needs a
    // fully-clear tile to land on.
    private static bool TileHasRamp(TileRecord t) => t.FringeAttr is { Type: TileType.LayerRamp };
    private static bool TileAttrClear(TileRecord t) => t.Type == TileType.Walkable && t.FringeAttr is null;
    // The attribute currently on the ACTIVE logical layer (Ground inline vs FringeAttr; missing fringe = Walkable).
    private TileType ActiveAttrType(TileRecord t) =>
        AttrLayerIsFringe ? (t.FringeAttr?.Type ?? TileType.Walkable) : t.Type;
    // The full attribute on the ACTIVE logical layer — the read companion to SetActiveAttr, so the dialog
    // attributes (Warp/Item/Key/KeyOpen) seed their fields and eligibility from the right plane.
    private TileAttr ActiveAttrData(TileRecord t) =>
        AttrLayerIsFringe
            ? (t.FringeAttr?.ToAttr() ?? TileAttr.Walkable)
            : t.ToGroundAttr();
    // Write an attribute to the ACTIVE layer.  Ground -> inline fields; Fringe -> FringeAttr (Walkable
    // clears it back to the default walkable plane, since Walkable authors no fields).  Never used for the
    // ramp (which writes both-plane occupancy).
    private void SetActiveAttr(TileRecord t, TileAttr attr)
    {
        if (AttrLayerIsFringe)
            t.FringeAttr = attr.Type == TileType.Walkable ? null : FringeAttr.From(attr);
        else
            t.SetGroundAttr(attr);
    }
    // Overload for the many callers that only set a type and no fields.
    private void SetActiveAttr(TileRecord t, TileType type) => SetActiveAttr(t, new TileAttr { Type = type });

    public string HoveredText => HoveredX >= 0
        ? EditorStrings.Format(EditorStrings.MapEditor_TileCoords, ("X", HoveredX), ("Y", HoveredY))
        : EditorStrings.Get(EditorStrings.MapEditor_TileCoordsEmpty);
    public string StatusModeText =>
        EditorStrings.Format(EditorStrings.MapEditor_StatusMode, ("Mode", ModeLabel(SelectedMode)));
    public string StatusActionText =>
        EditorStrings.Format(EditorStrings.MapEditor_StatusAction, ("Action", ActionLabel(SelectedAction)));
    // Localized mode name — mirrors the mode-toggle labels so the footer shows the translated label
    // rather than the raw enum identifier (which .ToString() would give, unlocalized).
    private static string ModeLabel(EditorMode mode) => EditorStrings.Get(mode switch
    {
        EditorMode.Tile => EditorStrings.MapEditor_ModeTile,
        EditorMode.Attribute => EditorStrings.MapEditor_ModeAttribute,
        _ => EditorStrings.MapEditor_ModeLight,
    });
    // Localized action name — same rationale as ModeLabel, mirrors the action-toggle labels.
    private static string ActionLabel(EditorAction action) => EditorStrings.Get(action switch
    {
        EditorAction.Place => EditorStrings.MapEditor_ActionPlace,
        EditorAction.Delete => EditorStrings.MapEditor_ActionDelete,
        _ => EditorStrings.MapEditor_ActionSelect,
    });

    // ── Hovered-tile computed properties (for the tile preview overlay) ─────

    public bool IsHovering => HoveredX >= 0 && SelectedMap is not null;

    private TileRecord? HoveredTileRecord =>
        HoveredX >= 0 && HoveredX <= Constants.MaxMapX &&
        HoveredY >= 0 && HoveredY <= Constants.MaxMapY &&
        SelectedMap is not null
            ? SelectedMap.Record.Tile[HoveredX, HoveredY]
            : null;

    // Exploded preview of the hovered tile: every layer of each layer type, one column per type.
    public IReadOnlyList<HoveredLayerPreview> HoveredGroundLayers => BuildHoveredLayers(LayerType.Ground);
    public IReadOnlyList<HoveredLayerPreview> HoveredFringeLayers => BuildHoveredLayers(LayerType.Fringe);
    public IReadOnlyList<HoveredLayerPreview> HoveredCanopyLayers => BuildHoveredLayers(LayerType.Canopy);

    private IReadOnlyList<HoveredLayerPreview> BuildHoveredLayers(LayerType type)
    {
        var t = HoveredTileRecord;
        int count = MaxLayersOf(type);
        var list = new HoveredLayerPreview[count];
        for (int i = 0; i < count; i++)
        {
            int packed = LayerCell.Empty;
            if (t is not null)
            {
                var layers = StackOf(t, type);
                if (i < layers.Length) packed = layers[i];
            }
            int sheet = LayerCell.Sheet(packed);
            int tileIndex = LayerCell.Tile(packed);
            Bitmap? bmp = sheet >= 0 && sheet < Tilesets.Count ? Tilesets[sheet] : null;
            // Sheet index shown beside the tile so the source sheet is identifiable; blank when empty.
            string sheetText = tileIndex > 0 ? sheet.ToString() : "";
            // Star-mark animated layers so a tile's anim state is visible at a glance in the hover preview.
            string label = LayerCell.Anim(packed) ? $"{i + 1}*" : $"{i + 1}";
            list[i] = new HoveredLayerPreview(label, bmp, tileIndex, sheetText);
        }
        return list;
    }

    public TileType HoveredAttrType => HoveredTileRecord?.Type ?? TileType.Walkable;
    public TileAttr HoveredGroundAttr => HoveredTileRecord?.ToGroundAttr() ?? TileAttr.Walkable;

    // The Fringe plane's attribute (FringeAttr) — the walkable bridge-top layer's own Blocked/Warp/Item/etc.,
    // including LayerRamp (always authored here, never on the ground's inline Type). Read alongside the ground
    // attribute so the hover preview shows BOTH logical planes, not just Ground.
    public TileType HoveredFringeAttrType => HoveredTileRecord?.FringeAttr?.Type ?? TileType.Walkable;
    public TileAttr HoveredFringeAttr => HoveredTileRecord?.FringeAttr?.ToAttr() ?? TileAttr.Walkable;

    public string HoveredGroundAttributeText => EditorStrings.Format(EditorStrings.MapEditor_AttrLabel,
        ("Layer", EditorVocabulary.NameOf(WorldLayer.Ground)),
        ("Value", FormatAttributeText(HoveredGroundAttr)));
    public string HoveredFringeAttributeText => EditorStrings.Format(EditorStrings.MapEditor_AttrLabel,
        ("Layer", EditorVocabulary.NameOf(WorldLayer.Fringe)),
        ("Value", FormatAttributeText(HoveredFringeAttr)));

    // Placed-light info for the hovered tile, shown in the Shift exploded-tile preview so a tile's whole
    // definition — layers, attribute, AND any light — is visible in one place.
    private PlacedLight? HoveredLight =>
        HoveredX >= 0 && HoveredX <= Constants.MaxMapX && HoveredY >= 0 && HoveredY <= Constants.MaxMapY && SelectedMap is not null
            ? LightAt(SelectedMap.Record, HoveredX, HoveredY, SelectedAttributeLayer)
            : null;
    public string HoveredLightText => HoveredLight is { } pl
        ? EditorStrings.Format(EditorStrings.MapEditor_LightText,
            ("Color", (pl.Light.Rgb & 0xFFFFFF).ToString("X6")),
            ("Radius", pl.Light.Radius),
            ("Intensity", (int)Math.Round(pl.Light.Intensity * 100)),
            ("Flicker", pl.Light.Flicker))
        : EditorStrings.Get(EditorStrings.MapEditor_LightText_None);
    public bool HoveredHasLight => HoveredLight is not null;
    // Rendered swatch of the hovered light's color (null when the tile has no light).
    public IBrush? HoveredLightBrush => HoveredLight is { } pl ? new SolidColorBrush(ColorHex.ToColor(pl.Light.Rgb)) : null;

    // NpcSpawn has no TileType (it writes a MapRecord.Npcs entry's pin, not tile.Type/FringeAttr — see
    // AttributeTool), so the hover preview surfaces it separately from the Ground/Fringe attribute lines.
    private int? HoveredNpcSpawnIndex =>
        HoveredX >= 0 && HoveredX <= Constants.MaxMapX && HoveredY >= 0 && HoveredY <= Constants.MaxMapY && SelectedMap is not null
            ? EntryPinnedAt(SelectedMap.Record, HoveredX, HoveredY, SelectedAttributeLayer)
            : null;
    public bool HoveredHasNpcSpawn => HoveredNpcSpawnIndex is not null;
    public string HoveredNpcSpawnText => HoveredNpcSpawnIndex is int i
        ? EditorStrings.Format(EditorStrings.MapEditor_AttrText_NpcSpawn,
            ("Name", EditorVocabulary.NameOf(AttributeTool.NpcSpawn)),
            ("Npc", NpcEntryForRow(i)?.Name ?? $"#{SelectedMap!.Record.Npcs[i].Npc}"))
        : "";

    // The attribute's NAME comes from EditorVocabulary (English in every language, matching the tool
    // dropdown and the map files); only the surrounding phrasing and the value labels are translated.
    private string FormatAttributeText(TileAttr a) => FormatAttributeText(a.Type, a);

    private string FormatAttributeText(TileType type, TileAttr a) => type switch
    {
        // Not a name but a statement that the tile carries no attribute, so this one stays localized.
        TileType.Walkable => EditorStrings.Get(EditorStrings.MapEditor_AttrText_None),
        TileType.Blocked or TileType.NpcAvoid => EditorVocabulary.NameOf(type),
        TileType.Warp => EditorStrings.Format(EditorStrings.MapEditor_AttrText_Warp,
            ("Name", EditorVocabulary.NameOf(type)), ("Map", MapLabel(a.WarpMap)), ("X", a.WarpX), ("Y", a.WarpY)),
        TileType.Item => EditorStrings.Format(EditorStrings.MapEditor_AttrText_Item,
            ("Name", EditorVocabulary.NameOf(type)), ("Item", ItemLabel(a.ItemNum)), ("Qty", a.ItemQuantity),
            ("Respawn", a.ItemRespawnSecs == 0
                ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_RespawnDefault)
                : EditorStrings.Format(EditorStrings.MapEditor_AttrText_RespawnSeconds, ("Seconds", a.ItemRespawnSecs)))),
        TileType.Key => EditorStrings.Format(EditorStrings.MapEditor_AttrText_Key,
            ("Name", EditorVocabulary.NameOf(type)), ("Item", ItemLabel(a.KeyItemNum)),
            ("Action", a.KeyIsConsumed
                ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_KeyTake)
                : EditorStrings.Get(EditorStrings.MapEditor_AttrText_KeyKeep))),
        TileType.KeyOpen => EditorStrings.Format(EditorStrings.MapEditor_AttrText_KeyOpen,
            ("Name", EditorVocabulary.NameOf(type)), ("X", a.DoorX), ("Y", a.DoorY)),
        TileType.LayerRamp => EditorStrings.Format(EditorStrings.MapEditor_AttrText_LayerRamp,
            ("Name", EditorVocabulary.NameOf(type)), ("Direction", EditorVocabulary.NameOf(a.RampGroundSide))),
        _ => EditorVocabulary.NameOf(type),
    };

    private string ItemLabel(short id) => id <= 0
        ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_None)
        : (id < _data.LiveItemEntries.Length ? _data.LiveItemEntries[id].Name : $"#{id}");
    private string MapLabel(short id) => id <= 0
        ? EditorStrings.Get(EditorStrings.MapEditor_AttrText_None)
        : (id < _data.LiveMapEntries.Length ? _data.LiveMapEntries[id].Name : $"#{id}");

    private void NotifyHoveredTile()
    {
        OnPropertyChanged(nameof(IsHovering));
        OnPropertyChanged(nameof(HoveredGroundLayers));
        OnPropertyChanged(nameof(HoveredFringeLayers));
        OnPropertyChanged(nameof(HoveredCanopyLayers));
        OnPropertyChanged(nameof(HoveredAttrType));
        OnPropertyChanged(nameof(HoveredGroundAttr));
        OnPropertyChanged(nameof(HoveredFringeAttrType));
        OnPropertyChanged(nameof(HoveredFringeAttr));
        OnPropertyChanged(nameof(HoveredGroundAttributeText));
        OnPropertyChanged(nameof(HoveredFringeAttributeText));
        OnPropertyChanged(nameof(HoveredHasNpcSpawn));
        OnPropertyChanged(nameof(HoveredNpcSpawnText));
        OnPropertyChanged(nameof(HoveredLightText));
        OnPropertyChanged(nameof(HoveredHasLight));
        OnPropertyChanged(nameof(HoveredLightBrush));
    }
}
