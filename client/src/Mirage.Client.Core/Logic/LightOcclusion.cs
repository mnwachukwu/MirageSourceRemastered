using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Which tiles a light actually reaches.
///
/// <para>A blocked tile is a wall, and a wall stops light. Without this a torch inside a building lights
/// the street through its own wall, and a lantern on the far side of a cliff lights the path below it.</para>
///
/// <para>Reach is decided by the same straight tile-line the server uses for spell line-of-sight, so a
/// wall that stops a fireball stops the light from the torch beside it. Obstacles are read on the light's
/// own layer: a fringe wall shades the deck it stands on, not the ground beneath.</para>
///
/// <para>The wall itself is lit. A tile a light cannot see THROUGH it can still see, or every wall in the
/// world would be a black silhouette against the ground it encloses.</para>
/// </summary>
public static class LightOcclusion
{
    /// <summary>The 3x3 neighbourhood, in tiles. A light is culled long before it could reach past it.</summary>
    public const int GridW = WorldCoordHelper.MapTilesX * 3;
    public const int GridH = WorldCoordHelper.MapTilesY * 3;

    /// <summary>True when light from <paramref name="lightWX"/>,<paramref name="lightWY"/> reaches the tile
    /// at <paramref name="tileWX"/>,<paramref name="tileWY"/>.</summary>
    public static bool Reaches(ClientState state, int lightWX, int lightWY, WorldLayer layer,
                               int tileWX, int tileWY)
    {
        if (lightWX == tileWX && lightWY == tileWY) return true;
        var probe = new LightPredicate(state, layer);
        // The destination tile being a wall does not shade it: the line stops AT the wall, and the wall's
        // own face is what a torch in front of it lights up.
        if (probe.IsBlocked(tileWX, tileWY))
            return HasClearToWall(lightWX, lightWY, tileWX, tileWY, probe);
        return WorldCoordHelper.HasClearSpellLineOfSight(lightWX, lightWY, tileWX, tileWY, probe);
    }

    /// <summary>The side of the square a light of this radius reaches over, in tiles.</summary>
    public static int MaskSide(int radiusTiles) => Math.Max(0, radiusTiles) * 2 + 1;

    /// <summary>How many cells <see cref="Fill"/> needs for a light of this radius.</summary>
    public static int MaskCells(int radiusTiles) => MaskSide(radiusTiles) * MaskSide(radiusTiles);

    /// <summary>
    /// Fills <paramref name="mask"/> with one light's reach, indexed
    /// <c>(dy + r) * MaskSide(r) + (dx + r)</c> for an offset from the light's own tile.
    ///
    /// <para>The mask covers the light's square and nothing else, so its size follows the radius rather
    /// than the world around it. A torch reaching three tiles costs 49 cells whatever the maps are.</para>
    /// </summary>
    public static void Fill(ClientState state, int lightWX, int lightWY, WorldLayer layer,
                            int radiusTiles, bool[] mask)
    {
        int r = Math.Max(0, radiusTiles);
        int side = MaskSide(r);
        Array.Clear(mask, 0, side * side);
        for (int dy = -r; dy <= r; dy++)
        {
            int wy = lightWY + dy;
            if (wy < 0 || wy >= GridH) continue;
            for (int dx = -r; dx <= r; dx++)
            {
                int wx = lightWX + dx;
                if (wx < 0 || wx >= GridW) continue;
                mask[(dy + r) * side + (dx + r)] = Reaches(state, lightWX, lightWY, layer, wx, wy);
            }
        }
    }

    // A wall is lit from the side facing the light: the line up to (but excluding) the wall must be clear.
    // Stepping one tile back along the line and asking for a clear run to THAT is the cheapest way to say so.
    private static bool HasClearToWall(int lightWX, int lightWY, int wallWX, int wallWY, in LightPredicate probe)
    {
        int dx = wallWX - lightWX, dy = wallWY - lightWY;
        if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) return true;   // touching the light: always its own face
        int stepX = Math.Sign(dx), stepY = Math.Sign(dy);
        int beforeX = wallWX - (Math.Abs(dx) >= Math.Abs(dy) ? stepX : 0);
        int beforeY = wallWY - (Math.Abs(dy) >= Math.Abs(dx) ? stepY : 0);
        if (beforeX == lightWX && beforeY == lightWY) return true;
        // The tile in front of the wall has to be open ground the light already reaches. A wall standing
        // behind another wall is in its shadow like anything else.
        if (probe.IsBlocked(beforeX, beforeY)) return false;
        return WorldCoordHelper.HasClearSpellLineOfSight(lightWX, lightWY, beforeX, beforeY, probe);
    }

    // readonly struct so the generic line-of-sight helper specializes per call: no boxing, no closure, no
    // allocation per tile. Mirrors ClientLineOfSight's predicate minus the door and ramp rules — a closed
    // door stops a spell, and it stops light too, but a ramp is a slope rather than a wall.
    private readonly struct LightPredicate(ClientState state, WorldLayer layer) : ISpellLosPredicate
    {
        private readonly ClientState _state = state;
        private readonly WorldLayer _layer = layer;

        public bool IsBlocked(int worldX, int worldY)
        {
            int col = worldX / WorldCoordHelper.MapTilesX;
            int row = worldY / WorldCoordHelper.MapTilesY;
            if (col < 0 || col > 2 || row < 0 || row > 2) return true;
            var map = _state.NeighborMaps[col, row];
            if (map is null) return true;
            int lx = worldX - col * WorldCoordHelper.MapTilesX;
            int ly = worldY - row * WorldCoordHelper.MapTilesY;
            var attr = LayerLogic.AttrFor(map.Tile[lx, ly], _layer);
            var type = attr.Type;
            if (type == TileType.Blocked) return attr.BlocksLight;
            var doors = (col == 1 && row == 1) ? _state.TempTile : _state.NeighborTempTiles[col, row];
            return type == TileType.Key && !doors[lx, ly, (int)_layer];
        }
    }
}
