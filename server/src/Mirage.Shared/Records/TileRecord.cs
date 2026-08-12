using Mirage.Shared.Serialization;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// One map tile.  Three visual tile-art stacks, each a fixed-length stack of packed
/// <see cref="LayerCell"/> values (index 0 is the bottom; the editor labels them 1..N; a value of
/// <see cref="LayerCell.Empty"/> (0) means unused): <see cref="Ground"/> draws below all entities,
/// <see cref="Fringe"/> draws between the ground- and fringe-layer entity passes (the walkable top of
/// a bridge where a fringe layer exists), and <see cref="Canopy"/> draws on top of everything.
///
/// Gameplay attributes are per LOGICAL layer: the inline <see cref="Type"/> and the fields below govern
/// the ground layer, while <see cref="FringeAttr"/> (non-null iff a walkable fringe layer exists here)
/// governs the fringe layer.  Read both uniformly via LayerLogic.AttrFor.
///
/// The attribute fields are field-for-field identical to <see cref="FringeAttr"/> and
/// <see cref="TileAttr"/>; see <see cref="TileAttr"/> for what each one means, and
/// <see cref="TileAttrRules"/> for which apply to which <see cref="TileType"/>.
///
/// Serialized by <see cref="TileRecordConverter"/>.
/// </summary>
[JsonConverter(typeof(TileRecordConverter))]
public sealed class TileRecord
{
    public int[] Ground { get; set; } = new int[Constants.MaxGroundLayers];
    public int[] Fringe { get; set; } = new int[Constants.MaxFringeLayers];
    public int[] Canopy { get; set; } = new int[Constants.MaxCanopyLayers];

    public TileType Type { get; set; }

    // Warp — see TileAttr for the meaning of each field.
    public short WarpMap { get; set; }
    public short WarpX { get; set; }
    public short WarpY { get; set; }
    public WorldLayer WarpLayer { get; set; }

    // Item
    public short ItemNum { get; set; }
    public short ItemValue { get; set; }
    public short ItemRespawnSecs { get; set; }

    // Key (a locked door)
    public short KeyItemNum { get; set; }
    public bool KeyIsConsumed { get; set; }

    // KeyOpen (a plate that opens a door elsewhere)
    public short DoorX { get; set; }
    public short DoorY { get; set; }
    public WorldLayer DoorLayer { get; set; }

    // LayerRamp. Authored on the FRINGE attribute in practice — a ramp is a fringe-plane surface — but
    // carried here too so the two attribute sets stay identical and nothing has to special-case which
    // plane it was read from.
    public Direction RampGroundSide { get; set; }

    public FringeAttr? FringeAttr { get; set; }

    /// <summary>Zero every ground-attribute field this <see cref="Type"/> does not use. Does NOT touch
    /// <see cref="FringeAttr"/>, which normalizes itself.</summary>
    public void Normalize() => TileAttrRules.Normalize(this);

    /// <summary>Overwrite the ground-layer attribute wholesale, normalizing as it lands so the tile never
    /// keeps a previous type's fields. The editor's paint path writes through here.</summary>
    public void SetGroundAttr(TileAttr a)
    {
        Type = a.Type;
        WarpMap = a.WarpMap; WarpX = a.WarpX; WarpY = a.WarpY; WarpLayer = a.WarpLayer;
        ItemNum = a.ItemNum; ItemValue = a.ItemValue; ItemRespawnSecs = a.ItemRespawnSecs;
        KeyItemNum = a.KeyItemNum; KeyIsConsumed = a.KeyIsConsumed;
        DoorX = a.DoorX; DoorY = a.DoorY; DoorLayer = a.DoorLayer;
        RampGroundSide = a.RampGroundSide;
        Normalize();
    }

    /// <summary>The ground layer's resolved attribute.</summary>
    public TileAttr ToGroundAttr() => new()
    {
        Type = Type,
        WarpMap = WarpMap, WarpX = WarpX, WarpY = WarpY, WarpLayer = WarpLayer,
        ItemNum = ItemNum, ItemValue = ItemValue, ItemRespawnSecs = ItemRespawnSecs,
        KeyItemNum = KeyItemNum, KeyIsConsumed = KeyIsConsumed,
        DoorX = DoorX, DoorY = DoorY, DoorLayer = DoorLayer,
        RampGroundSide = RampGroundSide,
    };
}
