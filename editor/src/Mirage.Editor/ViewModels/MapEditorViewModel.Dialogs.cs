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
    [ObservableProperty] private short _itemTileQuantity;
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
    private short _retItemNum, _retItemQuantity, _retItemRespawn;

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
    public IEnumerable<FlickerStyle> FlickerStyles { get; } = Enum.GetValues<FlickerStyle>();

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
}
