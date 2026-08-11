using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>Pure footprint math for variable-size NPCs: FootprintContains, LeadingEdgeTiles (the melee/
/// movement strip), TileRun membership, and cross-seam resolution. No world state needed.</summary>
[TestFixture]
public class WorldCoordHelperFootprintTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void FootprintContains_CoversExactlySxS(int size)
    {
        const int ax = 5, ay = 7;
        for (int j = -1; j <= size; j++)
        {
            for (int i = -1; i <= size; i++)
            {
                bool inside = i >= 0 && i < size && j >= 0 && j < size;
                Assert.That(WorldCoordHelper.FootprintContains(ax, ay, size, ax + i, ay + j), Is.EqualTo(inside),
                    $"offset ({i},{j}) for size {size}");
            }
        }
    }

    [Test]
    public void LeadingEdgeTiles_Right_IsColumnPastRightEdge()
    {
        const int ax = 4, ay = 6, size = 3;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Right);
        Assert.That(run.Count, Is.EqualTo(size));
        for (int j = 0; j < size; j++)
            Assert.That(run[j], Is.EqualTo((ax + size, ay + j)));
    }

    [Test]
    public void LeadingEdgeTiles_Left_IsColumnBeforeAnchor()
    {
        const int ax = 4, ay = 6, size = 2;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Left);
        for (int j = 0; j < size; j++)
            Assert.That(run[j], Is.EqualTo((ax - 1, ay + j)));
    }

    [Test]
    public void LeadingEdgeTiles_Down_IsRowPastBottomEdge()
    {
        const int ax = 4, ay = 6, size = 3;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Down);
        for (int i = 0; i < size; i++)
            Assert.That(run[i], Is.EqualTo((ax + i, ay + size)));
    }

    [Test]
    public void LeadingEdgeTiles_Up_IsRowAboveAnchor()
    {
        const int ax = 4, ay = 6, size = 2;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Up);
        for (int i = 0; i < size; i++)
            Assert.That(run[i], Is.EqualTo((ax + i, ay - 1)));
    }

    [Test]
    public void LeadingEdgeTiles_Size1_IsSingleFacedTile()
    {
        Assert.That(WorldCoordHelper.LeadingEdgeTiles(4, 6, 1, Direction.Right)[0], Is.EqualTo((5, 6)));
        Assert.That(WorldCoordHelper.LeadingEdgeTiles(4, 6, 1, Direction.Up)[0], Is.EqualTo((4, 5)));
    }

    [Test]
    public void TileRun_Contains_MatchesTiles()
    {
        var run = WorldCoordHelper.LeadingEdgeTiles(4, 6, 3, Direction.Right);
        for (int k = 0; k < run.Count; k++)
        {
            var (wx, wy) = run[k];
            Assert.That(run.Contains(wx, wy), Is.True);
        }
        Assert.That(run.Contains(4, 6), Is.False);          // the anchor itself is not on the strike strip
        Assert.That(run.Contains(4 + 3, 6 + 3), Is.False);  // one past the end of the strip
    }

    [Test]
    public void Footprint_StraddlesSeam_RightNeighborResolves()
    {
        // Center map = 1 at grid [1,1], right neighbor = 2. Anchor local (15,0) on center = world (31,12).
        var grid = new MapGrid(0, 0, 0, 0, 1, 2, 0, 0, 0);
        var (aWX, aWY) = WorldCoordHelper.ToWorld(1, 1, 15, 0);
        Assert.That((aWX, aWY), Is.EqualTo((31, 12)));

        // Size-2 footprint {(31,12),(32,12)}: col 31 = center local 15; col 32 = right neighbor local 0.
        Assert.That(WorldCoordHelper.FootprintContains(aWX, aWY, 2, 32, 12), Is.True);
        Assert.That(WorldCoordHelper.ResolveWorldTile(in grid, 31, 12), Is.EqualTo((1, 15, 0)));
        Assert.That(WorldCoordHelper.ResolveWorldTile(in grid, 32, 12), Is.EqualTo((2, 0, 0)));
    }

    // ── Spell-circle range (footprint-aware, r = 5) ──────────────────────────

    [Test]
    public void IsInSpellRange_Size1_MatchesPointCheck_AtTheBoundary()
    {
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 5, 0), Is.True, "exactly r=5 away is in range");
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 6, 0), Is.False, "6 away is out");
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 1, 5, 0, 1),
            Is.EqualTo(WorldCoordHelper.IsInSpellRange(0, 0, 5, 0)), "the 6-arg with size 1/1 is the plain point check");
    }

    [Test]
    public void IsInSpellRange_OversizeTarget_NearEdgeInCircle_IsInRange()
    {
        // Caster (9,4); a size-3 NPC anchored (2,2) has footprint [2,4]x[2,4].  Its near corner (4,4) is exactly
        // r=5 away (IN), but its anchor (2,2) is ~7.3 away (OUT) - the fix targets the body, not the corner.
        Assert.That(WorldCoordHelper.IsInSpellRange(9, 4, 1, 2, 2, 3), Is.True, "the near edge is inside the circle");
        Assert.That(WorldCoordHelper.IsInSpellRange(9, 4, 2, 2), Is.False, "the anchor alone is out of the circle");
    }

    [Test]
    public void IsInSpellRange_IsSymmetric_ForMixedSizes()
    {
        // Two-way: swapping caster/target roles agrees, so an oversize NPC casting at a player is fair both ways.
        Assert.That(WorldCoordHelper.IsInSpellRange(9, 4, 1, 2, 2, 3),
            Is.EqualTo(WorldCoordHelper.IsInSpellRange(2, 2, 3, 9, 4, 1)));
    }

    [Test]
    public void IsInSpellRange_NpcVsNpc_BothOversize_UsesNearestEdges()
    {
        // Two size-3 footprints on the same rows: A [0,2], B [7,9] on X -> nearest edges col 2<->7 -> gap 5 = IN.
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 3, 7, 0, 3), Is.True);
        // Shift B one further (anchor 8 -> near edge col 8) -> gap 6 -> OUT.
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 3, 8, 0, 3), Is.False);
    }
}
