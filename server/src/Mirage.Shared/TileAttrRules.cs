using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Which tile fields apply to which <see cref="TileType"/>, stated once. The editor asks it what to
/// show, <c>Normalize</c> asks it what to clear, and the serializer asks it what to write — so a field
/// hidden in the form is exactly a field absent from the file.
///
/// <para>This is the half that makes named fields honest rather than merely readable. Repaint a Warp
/// tile as a Key and its destination map would otherwise sit on the record forever: invisible in the
/// editor, still in the file, and live again the moment anything set it back to Warp. Items and spells
/// carry the same rule for the same reason.</para>
/// </summary>
public static class TileAttrRules
{
    public static bool UsesWarp(TileType type) => type is TileType.Warp;
    public static bool UsesItem(TileType type) => type is TileType.Item;
    public static bool UsesKey(TileType type) => type is TileType.Key;
    public static bool UsesDoor(TileType type) => type is TileType.KeyOpen;
    public static bool UsesRamp(TileType type) => type is TileType.LayerRamp;

    /// <summary>Whether this kind of tile carries any authored field at all — Walkable, Blocked and
    /// NpcAvoid are pure attributes with nothing to configure.</summary>
    public static bool UsesAnyField(TileType type) =>
        UsesWarp(type) || UsesItem(type) || UsesKey(type) || UsesDoor(type) || UsesRamp(type);

    public static void Normalize(TileRecord t)
    {
        if (!UsesWarp(t.Type)) { t.WarpMap = 0; t.WarpX = 0; t.WarpY = 0; t.WarpLayer = default; }
        if (!UsesItem(t.Type)) { t.ItemNum = 0; t.ItemQuantity = 0; t.ItemRespawnSecs = 0; }
        if (!UsesKey(t.Type)) { t.KeyItemNum = 0; t.KeyIsConsumed = false; }
        if (!UsesDoor(t.Type)) { t.DoorX = 0; t.DoorY = 0; t.DoorLayer = default; }
        if (!UsesRamp(t.Type)) t.RampGroundSide = default;
    }

    public static void Normalize(FringeAttr a)
    {
        if (!UsesWarp(a.Type)) { a.WarpMap = 0; a.WarpX = 0; a.WarpY = 0; a.WarpLayer = default; }
        if (!UsesItem(a.Type)) { a.ItemNum = 0; a.ItemQuantity = 0; a.ItemRespawnSecs = 0; }
        if (!UsesKey(a.Type)) { a.KeyItemNum = 0; a.KeyIsConsumed = false; }
        if (!UsesDoor(a.Type)) { a.DoorX = 0; a.DoorY = 0; a.DoorLayer = default; }
        if (!UsesRamp(a.Type)) a.RampGroundSide = default;
    }
}
