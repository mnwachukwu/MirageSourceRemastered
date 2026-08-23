namespace Mirage.Shared.Records;

/// <summary>
/// The gameplay attribute of a tile's walkable FRINGE layer (the top of a bridge). Its PRESENCE on a
/// <see cref="TileRecord"/> means "a walkable fringe layer exists here" — distinct from the tile merely
/// carrying <see cref="TileRecord.Fringe"/> decor art (a treetop has Fringe[] art but no FringeAttr).
/// Mirrors the tile's inline ground attribute for the fringe layer; both are read uniformly through
/// LayerLogic.AttrFor, which resolves either into a <see cref="TileAttr"/>.
///
/// <para>Field-for-field identical to <see cref="TileRecord"/>'s inline attribute and to
/// <see cref="TileAttr"/> — see the latter for what each field means and why they are named rather than
/// numbered. Kept a separate type because storage and the resolved value have different lifetimes: this
/// one is mutated by the editor and persisted, that one is a snapshot handed to a movement check.</para>
/// </summary>
public sealed class FringeAttr
{
    public TileType Type { get; set; }

    // Warp — see TileAttr for the meaning of each field.
    public short WarpMap { get; set; }
    public short WarpX { get; set; }
    public short WarpY { get; set; }
    public WorldLayer WarpLayer { get; set; }

    // Item
    public short ItemNum { get; set; }
    public short ItemQuantity { get; set; }
    public short ItemRespawnSecs { get; set; }

    // Key (a locked door)
    public short KeyItemNum { get; set; }
    public bool KeyIsConsumed { get; set; }

    // KeyOpen (a plate that opens a door elsewhere)
    public short DoorX { get; set; }
    public short DoorY { get; set; }
    public WorldLayer DoorLayer { get; set; }

    // Blocked — what the wall stops. Both default TRUE: a wall stops everything unless it says otherwise,
    // which is also what a map authored without these fields means.
    public bool BlocksLight { get; set; } = true;
    public bool BlocksSight { get; set; } = true;

    // LayerRamp — the side you mount from. The one field the fringe plane uses that the ground never does.
    public Direction RampGroundSide { get; set; }

    /// <summary>Zero every field this <see cref="Type"/> does not use, so a retyped tile cannot keep the
    /// previous kind's numbers — the same rule items and spells follow on save. Called by the editor's
    /// paint and save paths and by the server when it applies an editor map.</summary>
    public void Normalize() => TileAttrRules.Normalize(this);

    /// <summary>Build stored fringe state from a resolved attribute, normalized on the way in. The
    /// editor's paint path writes through here.</summary>
    public static FringeAttr From(TileAttr a)
    {
        var fa = new FringeAttr
        {
            Type = a.Type,
            WarpMap = a.WarpMap, WarpX = a.WarpX, WarpY = a.WarpY, WarpLayer = a.WarpLayer,
            ItemNum = a.ItemNum, ItemQuantity = a.ItemQuantity, ItemRespawnSecs = a.ItemRespawnSecs,
            KeyItemNum = a.KeyItemNum, KeyIsConsumed = a.KeyIsConsumed,
            DoorX = a.DoorX, DoorY = a.DoorY, DoorLayer = a.DoorLayer,
            RampGroundSide = a.RampGroundSide,
            BlocksLight = a.BlocksLight, BlocksSight = a.BlocksSight,
        };
        fa.Normalize();
        return fa;
    }

    /// <summary>The resolved value a movement or interaction check reads.</summary>
    public TileAttr ToAttr() => new()
    {
        Type = Type,
        WarpMap = WarpMap, WarpX = WarpX, WarpY = WarpY, WarpLayer = WarpLayer,
        ItemNum = ItemNum, ItemQuantity = ItemQuantity, ItemRespawnSecs = ItemRespawnSecs,
        KeyItemNum = KeyItemNum, KeyIsConsumed = KeyIsConsumed,
        DoorX = DoorX, DoorY = DoorY, DoorLayer = DoorLayer,
        RampGroundSide = RampGroundSide,
        BlocksLight = BlocksLight, BlocksSight = BlocksSight,
    };

    public FringeAttr Clone() => (FringeAttr)MemberwiseClone();
}
