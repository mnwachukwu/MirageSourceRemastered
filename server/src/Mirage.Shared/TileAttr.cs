namespace Mirage.Shared;

/// <summary>
/// A tile's gameplay attribute on one layer: what the tile IS plus its three data slots, whose meaning
/// depends on <see cref="Type"/> (a Warp's destination map/x/y, a Key's key number, a ramp's ground side,
/// and so on — see <see cref="TileType"/>).
///
/// <para>What <see cref="LayerLogic.AttrFor"/> returns. It used to return a bare
/// <c>(TileType, short, short, short)</c> tuple, and the editor declared that same shape three more times
/// under different element names (<c>T/D1/D2/D3</c> rather than <c>Type/Data1/Data2/Data3</c>) — four
/// independent spellings of one concept, with three consecutive same-typed <c>short</c>s that positional
/// destructuring gave no protection against transposing.</para>
///
/// <para>Distinct from <see cref="Records.FringeAttr"/> and the wire-format <c>FringeData</c>, which carry
/// the same four values but are STORAGE types with their own persistence contracts. This is the resolved,
/// read-only answer to "what governs an entity standing here, on this layer" — a value, not a record on
/// a map.</para>
/// </summary>
public readonly record struct TileAttr(TileType Type, short Data1, short Data2, short Data3)
{
    /// <summary>The fringe plane is uniform and open by default, so a tile with no fringe attribute
    /// reads as this rather than as "no attribute".</summary>
    public static readonly TileAttr Walkable = new(TileType.Walkable, 0, 0, 0);

    /// <summary>What a ramp reads as from the ground layer: a solid understructure, not a hole.</summary>
    public static readonly TileAttr Blocked = new(TileType.Blocked, 0, 0, 0);
}
