using Mirage.Shared.Records;
using System.Collections.Generic;

namespace Mirage.Shared;

/// <summary>
/// The single source of truth for the two-layer ("layered world") movement rules, shared by server movement,
/// client prediction, and the editor.  All queries are in WORLD-tile space and read tiles through an
/// <see cref="IWorldTileView"/>, so a footprint that spans a seamless-map seam is handled uniformly (the
/// server backs the view with a MapGrid + ResolveWorldTile, the client with its NeighborMaps).
///
/// <para>Model: TWO FULL, UNIFORM logical planes — Ground and Fringe.  Each is walkable by default and shaped
/// the same way (place a Blocked attribute on that layer for a wall/railing).  An entity's layer is STICKY;
/// it flips ONLY across a <see cref="TileType.LayerRamp"/> tile — the sole connector between planes.  A ramp's
/// <c>FringeAttr.Data1</c> is its ground-side <see cref="Direction"/> (the side you mount from).  A ramp is:
/// (a) a mount-axis corridor — you enter/leave only along {groundSide, Opposite(groundSide)}; a perpendicular
/// step crossing a ramp's side edge (the "gap") is blocked on BOTH layers; and (b) depth-gated — your whole
/// SxS footprint must fit inside the contiguous ramp block, so a shallow ramp admits only small bodies.  While
/// on a ramp you are on the Fringe layer (occlusion); interaction across a ramp is 3-D adjacency, resolved by
/// the caller (combat), not here.</para>
///
/// <para>Walkability of the destination TILE (walls, closed doors, NpcAvoid, occupancy) stays with the
/// caller's existing checks, which read the correct layer's attribute via <see cref="AttrFor"/>.  LayerLogic
/// owns only the layer choice and the ramp corridor + fit geometry.</para>
/// </summary>
public static class LayerLogic
{
    /// <summary>Read-only world-tile accessor: the tile at a world coordinate, or null when the coordinate
    /// resolves off the loaded map grid (treated as not walkable).</summary>
    public interface IWorldTileView
    {
        TileRecord? At(int worldX, int worldY);
    }

    /// <summary>The gameplay attribute governing an entity at (<paramref name="t"/>, <paramref name="layer"/>):
    /// the tile's inline ground attribute on <see cref="WorldLayer.Ground"/>, or its
    /// <see cref="TileRecord.FringeAttr"/> on <see cref="WorldLayer.Fringe"/>.  The fringe plane is UNIFORM and
    /// walkable by default, so a Fringe query on a tile with no fringe attribute reads as
    /// <see cref="TileType.Walkable"/> (author a Blocked fringe attribute for a railing/wall up top).
    ///
    /// <para>A <see cref="TileType.LayerRamp"/> is a SOLID connector — the "hole" between the planes.  It is
    /// walkable ONLY on the Fringe layer (its ramp surface); on the Ground layer it reads <b>Blocked</b>, a solid
    /// understructure you can't walk under or through.  The only way onto a ramp is to mount it from its ground
    /// foot along the mount axis, which <see cref="ResolveLayer"/> resolves to Fringe (the walkable side) — so the
    /// gate sees Walkable there.  Any other step toward a ramp keeps you on Ground and hits this Blocked read.</para></summary>
    public static TileAttr AttrFor(TileRecord t, WorldLayer layer)
    {
        if (layer == WorldLayer.Fringe)
        {
            return t.FringeAttr is { } fa
                ? new TileAttr(fa.Type, fa.Data1, fa.Data2, fa.Data3)
                : TileAttr.Walkable;
        }

        // Ground layer: a ramp is solid from underneath (blocked); everything else reads its inline attribute.
        if (t.FringeAttr is { Type: TileType.LayerRamp })
            return TileAttr.Blocked;
        return new TileAttr(t.Type, t.Data1, t.Data2, t.Data3);
    }

    /// <summary>The layer a mover ends on after stepping (top-left anchor) to world (aWX,aWY) from
    /// <paramref name="srcLayer"/> moving <paramref name="dir"/>.  Transitions happen only at the BOUNDARY of a
    /// contiguous ramp block (never tile-to-tile inside it), so a ramp any number of tiles deep behaves the same
    /// climbing up as coming down and you stay on one surface across the whole span:
    ///  * ASCEND (→ Fringe) when stepping from a NON-ramp tile onto a ramp, moving up-ramp (toward the fringe,
    ///    i.e. Opposite its ground side);
    ///  * DESCEND (→ Ground) when stepping OFF a ramp onto a NON-ramp tile, moving toward the ramp's ground side.
    /// Interior ramp→ramp steps (and plain non-ramp steps) keep the source layer — so once you have ascended you
    /// ride the ramp surface on Fringe until you step off its ground side, and a mover that entered a ramp tile
    /// from its high side on the Ground layer stays UNDER it on Ground.  Because a contiguous block resolves to a
    /// single surface with ground-connections only at its ground-side edges, mixed-direction blocks (humps,
    /// corners, multi-mount staircases) resolve coherently.  The corridor + fit gates live in
    /// <see cref="CanEnter"/>; interaction across a ramp is 3-D adjacency, resolved by the caller.</summary>
    public static WorldLayer ResolveLayer(IWorldTileView view, int aWX, int aWY, int size, WorldLayer srcLayer, Direction dir)
    {
        var (dx, dy) = WorldCoordHelper.DirDelta(dir);
        Direction destGround = default, srcGround = default;
        bool destRamp = view.At(aWX, aWY) is { } dest && IsRamp(dest, out destGround);
        bool srcRamp = view.At(aWX - dx, aWY - dy) is { } src && IsRamp(src, out srcGround);

        // Ascend only at the block's ground edge: entering a ramp FROM non-ramp ground, moving up-ramp.
        if (destRamp && !srcRamp && dir == Opposite(destGround))
            return WorldLayer.Fringe;

        // Descend only at the block's ground edge: leaving a ramp ONTO non-ramp ground, toward its ground side.
        if (srcRamp && !destRamp && dir == srcGround)
            return WorldLayer.Ground;

        // Interior (ramp→ramp) or plain (neither) step: keep the layer you were on.
        return srcLayer;
    }

    /// <summary>The ramp geometry gate for a step (top-left anchor moving to world (aWX,aWY)).  Computes the
    /// resulting <paramref name="newLayer"/> and returns false when the step is illegal ramp geometry:
    ///  * a PERPENDICULAR step across a ramp's side edge — exactly one of the src/dest anchor tiles is a ramp
    ///    and the direction is off the ramp's mount axis (crossing the gap) — blocked on both layers; OR
    ///  * MOUNTING a ramp whose contiguous block can't hold the SxS footprint (depth fit-gate).
    /// A plain within-plane step (no ramp at either anchor tile) is always geometry-legal here; the caller
    /// still layers its own tile-attribute (walls/doors) + occupancy checks on top, reading
    /// <see cref="AttrFor"/> at <paramref name="newLayer"/>.  Ramp corridor checks are anchor-based (a fair
    /// approximation for big bodies once the fit-gate has aligned them; the live per-step footprint walkability
    /// is the backstop).</summary>
    public static bool CanEnter(IWorldTileView view, int aWX, int aWY, int size, WorldLayer srcLayer, Direction dir, out WorldLayer newLayer)
    {
        newLayer = ResolveLayer(view, aWX, aWY, size, srcLayer, dir);

        var (dx, dy) = WorldCoordHelper.DirDelta(dir);
        Direction destGround = default, srcGround = default;
        bool destRamp = view.At(aWX, aWY) is { } dt && IsRamp(dt, out destGround);
        bool srcRamp = view.At(aWX - dx, aWY - dy) is { } st && IsRamp(st, out srcGround);

        // Perpendicular-across-the-gap: exactly one anchor tile is a ramp and the move is off the mount axis —
        // stepping off a ramp's long side, mounting from the side, or slipping under it perpendicular.  Blocked
        // on both layers.  Within-ramp and along-the-mount-axis steps pass.
        if (destRamp != srcRamp)
        {
            var ground = destRamp ? destGround : srcGround;
            if (dir != ground && dir != Opposite(ground))
                return false;
        }

        // Depth fit-gate: stepping ONTO a ramp requires the whole SxS footprint to fit inside the contiguous
        // ramp block — "doesn't fit on the ramp → can't use the layer."  A shallow ramp admits only small bodies.
        if (destRamp && !srcRamp && !RampBlockFits(view, aWX, aWY, size, destGround))
            return false;

        return true;
    }

    /// <summary>The cross-layer LoS / targeting connect rule (used by ranged line-of-sight AND melee, which adds
    /// its own 2-D adjacency check). Two points on the SAME logical layer always connect — normal obstacle /
    /// adjacency rules then decide. Across the two layers (Ground vs Fringe) they connect ONLY when the FRINGE
    /// endpoint stands ON A RAMP <b>and the Ground endpoint lies toward that ramp's ground (mount) side</b>: a ramp
    /// bridges the planes only down its mount axis — the way you climb on/off it — NOT off its high (lift) end or
    /// across its sides. So a person on a ramp reaches the ground at its foot (and is reachable from there), but not
    /// a ground target behind/above the ramp; a plain ground point and a plain fringe point never connect. (The
    /// Ground endpoint is never itself on a ramp — ramps read Blocked on Ground — so only the Fringe side carries a
    /// ramp.) Directional via the dominant step direction from the ramp toward the ground endpoint; range-agnostic.</summary>
    public static bool LayerConnects(IWorldTileView view, int aWX, int aWY, WorldLayer aLayer, int bWX, int bWY, WorldLayer bLayer)
    {
        if (aLayer == bLayer) return true;
        // The Fringe endpoint carries the ramp; connect only when the Ground endpoint is toward its ground side.
        if (aLayer == WorldLayer.Fringe && IsRampWithGroundSide(view, aWX, aWY, out var ag))
            return WorldCoordHelper.WorldDirectionFrom(aWX, aWY, bWX, bWY) == ag;
        if (bLayer == WorldLayer.Fringe && IsRampWithGroundSide(view, bWX, bWY, out var bg))
            return WorldCoordHelper.WorldDirectionFrom(bWX, bWY, aWX, aWY) == bg;
        return false;
    }

    private static bool IsRampWithGroundSide(IWorldTileView view, int wx, int wy, out Direction groundSide)
    {
        if (view.At(wx, wy) is { } t) return IsRamp(t, out groundSide);
        groundSide = default;
        return false;
    }

    private static readonly (int dx, int dy)[] _adj = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    // True iff the contiguous same-ground-side ramp block reachable from (wx,wy) contains an SxS all-ramp
    // square — the "does the footprint fit on the ramp" depth-gate.  The flood is bounded (ramps are small) and
    // only runs on a mount step, so the per-call set/stack allocation is off the movement hot path.
    private static bool RampBlockFits(IWorldTileView view, int wx, int wy, int size, Direction groundSide)
    {
        if (size <= 1) return true;
        var block = new HashSet<(int, int)>();
        var stack = new Stack<(int, int)>();
        stack.Push((wx, wy));
        while (stack.Count > 0)
        {
            var cell = stack.Pop();
            if (!block.Add(cell)) continue;
            foreach (var (dx, dy) in _adj)
            {
                var n = (cell.Item1 + dx, cell.Item2 + dy);
                if (!block.Contains(n) && view.At(n.Item1, n.Item2) is { } t && IsRamp(t, out var g) && g == groundSide)
                    stack.Push(n);
            }
        }
        foreach (var (bx, by) in block)
        {
            bool fits = true;
            for (int j = 0; j < size && fits; j++)
            {
                for (int i = 0; i < size && fits; i++)
                    if (!block.Contains((bx + i, by + j))) fits = false;
            }

            if (fits) return true;
        }
        return false;
    }

    private static bool IsRamp(TileRecord t, out Direction groundSide)
    {
        if (t.FringeAttr is { Type: TileType.LayerRamp } fa)
        {
            groundSide = (Direction)fa.Data1;
            return true;
        }
        groundSide = default;
        return false;
    }

    private static Direction Opposite(Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => Direction.Left,
    };
}

/// <summary>
/// Packs a <see cref="WorldLayer"/> into a world-target Y coordinate, for attributes whose Data encodes a
/// TARGET (x,y) that must also specify a layer: <see cref="TileType.Warp"/> (dest map/x/y + layer) and
/// <see cref="TileType.KeyOpen"/> (door x/y + layer).  Coordinates are &lt;= <see cref="Constants.MaxMapY"/>
/// (well under a byte), so the low byte carries the Y and bit 8 carries the layer — no schema growth, and
/// every call site reads the target layer through this one accessor (no raw bit math elsewhere).
/// </summary>
public static class WorldTarget
{
    public static short Pack(int y, WorldLayer layer) => (short)((y & 0xFF) | ((int)layer << 8));
    public static short Y(short packed) => (short)(packed & 0xFF);
    public static WorldLayer Layer(short packed) => (WorldLayer)((packed >> 8) & 1);
}
