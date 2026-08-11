namespace Mirage.Shared.Records;

/// <summary>
/// The gameplay attribute of a tile's walkable FRINGE layer (the top of a bridge). Its PRESENCE on a
/// <see cref="TileRecord"/> means "a walkable fringe layer exists here" — distinct from the tile merely
/// carrying <see cref="TileRecord.Fringe"/> decor art (a treetop has Fringe[] art but no FringeAttr).
/// Mirrors the tile's inline ground attribute (Type + Data1..3) for the fringe layer; both are read
/// uniformly through LayerLogic.AttrFor(tile, WorldLayer).
/// </summary>
public sealed class FringeAttr
{
    public TileType Type { get; set; }
    public short Data1 { get; set; }
    public short Data2 { get; set; }
    public short Data3 { get; set; }
}
