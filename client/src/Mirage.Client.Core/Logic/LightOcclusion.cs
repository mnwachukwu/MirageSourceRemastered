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
/// <para><c>BlocksLight</c> means what it says: light stops AT such a tile, and the tile is not lit. A tile
/// light should reach and stand on — open water, ground cover — is authored with the flag off rather than
/// exempted here. The mask is sampled with linear filtering, so an unlit occluder is not a silhouette: the
/// light ramps down across the last open tile and lands near zero at the occluder's edge.</para>
/// </summary>
public static class LightOcclusion
{
    /// <summary>The 3x3 neighbourhood in tiles, measured in the center map's size. A light is culled long
    /// before it could reach past it.</summary>
    public static int GridW(ClientState state) => state.MapTilesX * 3;

    /// <inheritdoc cref="GridW"/>
    public static int GridH(ClientState state) => state.MapTilesY * 3;

    /// <summary>True when light from <paramref name="lightWX"/>,<paramref name="lightWY"/> reaches the tile
    /// at <paramref name="tileWX"/>,<paramref name="tileWY"/>.</summary>
    public static bool Reaches(ClientState state, int lightWX, int lightWY, WorldLayer layer,
                               int tileWX, int tileWY)
    {
        if (lightWX == tileWX && lightWY == tileWY) return true;
        var probe = new LightPredicate(state, layer);
        if (probe.IsBlocked(tileWX, tileWY)) return false;
        return WorldCoordHelper.HasClearSpellLineOfSight(lightWX, lightWY, tileWX, tileWY, probe);
    }

    /// <summary>
    /// Mask texels per tile.
    ///
    /// <para>A linear sampler ramps between texel CENTRES, so the transition at a wall is one texel wide.
    /// Where it SITS is <see cref="Fill"/>'s pull-back to choose: centred on the border leaves a hairline of
    /// light along every blocked edge, so it goes just clear of it instead. Either way the width is a texel,
    /// and the only lever on it is how big a texel is.</para>
    ///
    /// <para>So: EIGHT. That is four pixels — enough to read as an edge rather than a stair, small enough
    /// that the ground given up in front of a wall does not read as a gap. Costs texels, not traces; reach
    /// is still answered once per tile.</para>
    /// </summary>
    public const int SubSamples = 8;

    /// <summary>The side of the square a light of this radius reaches over, in tiles. This is what the mask
    /// COVERS, and so what <see cref="MaskUv"/> maps onto — independent of how finely it is sampled.</summary>
    public static int MaskSide(int radiusTiles) => Math.Max(0, radiusTiles) * 2 + 1;

    /// <summary>The mask texture's side, in texels.</summary>
    public static int MaskTexels(int radiusTiles) => MaskSide(radiusTiles) * SubSamples;

    /// <summary>How many cells <see cref="Fill"/> needs for a light of this radius.</summary>
    public static int MaskCells(int radiusTiles) => MaskTexels(radiusTiles) * MaskTexels(radiusTiles);

    /// <summary>
    /// Where a halo's own 0..1 quad coordinates land in its reach mask: <c>maskUv = uv * Scale + Offset</c>.
    ///
    /// <para>The mask spans <see cref="MaskSide"/> tiles around <paramref name="tileScreenX"/>,<paramref
    /// name="tileScreenY"/> — the tile the occlusion was traced from — while the halo spans its own radius
    /// around wherever it is being drawn. Both are axis-aligned rectangles in the same space, so the mapping
    /// between them is one scale and one offset, and a halo sliding sub-tile slides across a mask that stays
    /// put.</para>
    ///
    /// <para>Texel <c>i</c> of the mask covers the whole of tile <c>i - r</c>, so mapping the mask's rectangle
    /// onto 0..1 puts each texel's CENTRE on its tile's centre — which is what makes a linear sample between
    /// two texels a ramp across the boundary between their tiles.</para>
    /// </summary>
    public static (float ScaleX, float ScaleY, float OffsetX, float OffsetY) MaskUv(
        float destLeft, float destTop, float destW, float destH,
        float tileScreenX, float tileScreenY, int reachRadius)
    {
        int r = Math.Max(0, reachRadius);
        int side = MaskSide(r);
        float spanX = side * (float)Constants.PicX, spanY = side * (float)Constants.PicY;
        float maskX = tileScreenX - r * Constants.PicX;
        float maskY = tileScreenY - r * Constants.PicY;
        return (destW / spanX, destH / spanY, (destLeft - maskX) / spanX, (destTop - maskY) / spanY);
    }

    /// <summary>
    /// Fills <paramref name="mask"/> with one light's reach, at <see cref="SubSamples"/> texels per tile,
    /// row-major over <see cref="MaskTexels"/> — texel <c>(tx, ty)</c> sits in the tile at offset
    /// <c>(tx / SubSamples - r, ty / SubSamples - r)</c> from the light's own.
    ///
    /// <para>The mask covers the light's square and nothing else, so its size follows the radius rather
    /// than the world around it.</para>
    ///
    /// <para>Reach is answered once per TILE. The mask is then pulled back ONE TEXEL from anything that
    /// blocks light, which lands the sampler's ramp just clear of the border rather than across it — a wall
    /// takes nothing, and the hairline of light that otherwise runs along a blocked edge goes with it. At
    /// <see cref="SubSamples"/> a tile that costs four pixels of ground in front of the wall.</para>
    ///
    /// <para>The pull-back is DIRECTIONAL — only the three neighbours on the far side of a texel FROM the
    /// light are consulted. Applied evenly it also dims the open tiles beside a wall, tiles nothing is
    /// standing between, which reads as shadow leaking sideways out of the thing casting it.</para>
    /// </summary>
    public static void Fill(ClientState state, int lightWX, int lightWY, WorldLayer layer,
                            int radiusTiles, bool[] mask)
    {
        int r = Math.Max(0, radiusTiles);
        int side = MaskSide(r);
        int texels = side * SubSamples;

        Span<bool> tiles = side * side <= 1024 ? stackalloc bool[1024] : new bool[side * side];
        for (int dy = -r; dy <= r; dy++)
        {
            int wy = lightWY + dy;
            for (int dx = -r; dx <= r; dx++)
            {
                int wx = lightWX + dx;
                tiles[(dy + r) * side + (dx + r)] =
                    wy >= 0 && wy < GridH(state) && wx >= 0 && wx < GridW(state)
                    && Reaches(state, lightWX, lightWY, layer, wx, wy);
            }
        }

        // Only the texels on a tile's two OUTWARD edges can be pulled back: for any texel further in, all
        // three neighbours the rule consults are inside the tile itself, and a lit tile does not shade
        // itself. So the whole answer for a tile is three lookups and a fill, rather than three lookups per
        // texel — sixty-four times fewer at SubSamples of eight.
        //
        // A tile is uniform in this only where it lies wholly to one side of the light. The row and column
        // the light sits in straddle it, so those split in half and each half is handled on its own.
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                bool lit = tiles[(dy + r) * side + (dx + r)];
                int x0 = (dx + r) * SubSamples, y0 = (dy + r) * SubSamples;
                if (!lit)
                {
                    for (int sy = 0; sy < SubSamples; sy++)
                        Array.Clear(mask, (y0 + sy) * texels + x0, SubSamples);
                    continue;
                }

                int half = SubSamples / 2;
                for (int hx = 0, hxs = dx == 0 ? 2 : 1; hx < hxs; hx++)
                {
                    int xFrom = dx == 0 ? hx * half : 0;
                    int xTo = dx == 0 ? xFrom + half : SubSamples;
                    int sx = dx == 0 ? (hx == 0 ? -1 : 1) : Math.Sign(dx);
                    for (int hy = 0, hys = dy == 0 ? 2 : 1; hy < hys; hy++)
                    {
                        int yFrom = dy == 0 ? hy * half : 0;
                        int yTo = dy == 0 ? yFrom + half : SubSamples;
                        int sy = dy == 0 ? (hy == 0 ? -1 : 1) : Math.Sign(dy);
                        bool blockX = Blocked(tiles, side, r, dx + sx, dy);
                        bool blockY = Blocked(tiles, side, r, dx, dy + sy);
                        bool blockD = Blocked(tiles, side, r, dx + sx, dy + sy);
                        int edgeX = sx > 0 ? xTo - 1 : xFrom;
                        int edgeY = sy > 0 ? yTo - 1 : yFrom;
                        for (int ly = yFrom; ly < yTo; ly++)
                        {
                            int row = (y0 + ly) * texels + x0;
                            for (int lx = xFrom; lx < xTo; lx++)
                            {
                                bool outX = lx == edgeX, outY = ly == edgeY;
                                mask[row + lx] = !((outX && blockX) || (outY && blockY) || (outX && outY && blockD));
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Whether the tile at that offset stops light. Outside the mask is not a wall.</summary>
    private static bool Blocked(ReadOnlySpan<bool> tiles, int side, int r, int dx, int dy) =>
        dx >= -r && dx <= r && dy >= -r && dy <= r && !tiles[(dy + r) * side + (dx + r)];

    // readonly struct so the generic line-of-sight helper specializes per call: no boxing, no closure, no
    // allocation per tile. Mirrors ClientLineOfSight's predicate minus the door and ramp rules — a closed
    // door stops a spell, and it stops light too, but a ramp is a slope rather than a wall.
    private readonly struct LightPredicate(ClientState state, WorldLayer layer) : ISpellLosPredicate
    {
        private readonly ClientState _state = state;
        private readonly WorldLayer _layer = layer;

        public bool IsBlocked(int worldX, int worldY)
        {
            int col = worldX / _state.MapTilesX;
            int row = worldY / _state.MapTilesY;
            if (col < 0 || col > 2 || row < 0 || row > 2) return true;
            var map = _state.NeighborMaps[col, row];
            if (map is null) return true;
            int lx = worldX - col * _state.MapTilesX;
            int ly = worldY - row * _state.MapTilesY;
            var attr = LayerLogic.AttrFor(map.Tile[lx, ly], _layer);
            var type = attr.Type;
            if (type == TileType.Blocked) return attr.BlocksLight;
            var doors = (col == 1 && row == 1) ? _state.TempTile : _state.NeighborTempTiles[col, row];
            return type == TileType.Key && !doors[lx, ly, (int)_layer];
        }
    }
}
