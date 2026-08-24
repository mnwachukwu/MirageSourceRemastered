using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests;

/// <summary>
/// A wall stops light.
///
/// <para>Without this a torch inside a building lights the street through its own wall. The rule is the
/// same straight tile-line the server uses for spell line-of-sight, so a wall that stops a fireball stops
/// the light beside it — and these pin that the two agree.</para>
/// </summary>
[TestFixture]
public class LightOcclusionTests
{
    private const int W = WorldCoordHelper.MapTilesX;   // one map's width, so the centre cell starts here

    private static ClientState StateWithWalls(params (int X, int Y)[] walls)
    {
        var state = new ClientState();
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 3; r++)
                state.NeighborMaps[c, r] = new MapRecord();
        foreach (var (x, y) in walls)
            state.NeighborMaps[1, 1]!.EditTile(x, y, t => t with { Type = TileType.Blocked });
        return state;
    }

    // World coordinates of a tile in the centre map.
    private static (int X, int Y) At(int x, int y) => (W + x, WorldCoordHelper.MapTilesY + y);

    [Test]
    public void OpenGround_IsReached()
    {
        var state = StateWithWalls();
        var (lx, ly) = At(5, 5);
        var (tx, ty) = At(9, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, tx, ty), Is.True);
    }

    /// <summary>The case that started this: a light with a wall between it and a tile does not reach it.</summary>
    [Test]
    public void ATileBehindAWall_IsNotReached()
    {
        var state = StateWithWalls((7, 5));
        var (lx, ly) = At(5, 5);
        var (tx, ty) = At(9, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, tx, ty), Is.False);
    }

    /// <summary>The wall itself is lit. A torch in front of a wall has to show the wall, or every wall in
    /// the world is a black silhouette against the ground it encloses.</summary>
    [Test]
    public void TheWallItself_IsLitFromTheSideFacingTheLight()
    {
        var state = StateWithWalls((7, 5));
        var (lx, ly) = At(5, 5);
        var (wx, wy) = At(7, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, wx, wy), Is.True);
    }

    [Test]
    public void AWallBehindAWall_IsNotLit()
    {
        var state = StateWithWalls((7, 5), (8, 5));
        var (lx, ly) = At(5, 5);
        var (farX, farY) = At(8, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, farX, farY), Is.False);
    }

    [Test]
    public void ALightStandsOnItsOwnTile()
    {
        var state = StateWithWalls((5, 5));
        var (lx, ly) = At(5, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, lx, ly), Is.True);
    }

    /// <summary>Obstacles are read on the light's own layer: a fringe wall shades the deck it stands on,
    /// not the ground beneath it.</summary>
    [Test]
    public void AFringeWall_DoesNotShadeTheGroundBeneath()
    {
        var state = new ClientState();
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 3; r++)
                state.NeighborMaps[c, r] = new MapRecord();
        state.NeighborMaps[1, 1]!.EditTile(7, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.Blocked } });

        var (lx, ly) = At(5, 5);
        var (tx, ty) = At(9, 5);

        Assert.Multiple(() =>
        {
            Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, tx, ty), Is.True,
                "a fringe wall is not on the ground plane");
            Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Fringe, tx, ty), Is.False,
                "on the fringe plane it is a wall like any other");
        });
    }

    /// <summary>Fill answers over the light's own square, indexed from its tile.</summary>
    [Test]
    public void Fill_MarksTheReachableTilesOverItsOwnSquare()
    {
        const int r = 3;
        var state = StateWithWalls((7, 5));
        var (lx, ly) = At(5, 5);
        var mask = new bool[LightOcclusion.MaskCells(r)];

        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, mask);

        int side = LightOcclusion.MaskSide(r);
        bool Reached(int dx, int dy) => mask[(dy + r) * side + (dx + r)];

        Assert.Multiple(() =>
        {
            Assert.That(Reached(0, 0), Is.True, "its own tile");
            Assert.That(Reached(0, 2), Is.True, "open ground inside the radius");
            Assert.That(Reached(3, 0), Is.False, "behind the wall");
        });
    }

    /// <summary>The mask covers the light's square and nothing more, so its cost follows the radius
    /// rather than the size of the world around it.</summary>
    [TestCase(0, 1)]
    [TestCase(1, 9)]
    [TestCase(3, 49)]
    [TestCase(8, 289)]
    public void TheMask_IsSizedByTheRadiusAlone(int radius, int cells)
    {
        Assert.Multiple(() =>
        {
            Assert.That(LightOcclusion.MaskSide(radius), Is.EqualTo(radius * 2 + 1));
            Assert.That(LightOcclusion.MaskCells(radius), Is.EqualTo(cells));
        });
    }

    /// <summary>A map that has not arrived is opaque: light does not spill into a cell nothing is known
    /// about, which is the same answer spell line-of-sight gives.</summary>
    [Test]
    public void AnUnloadedNeighbour_StopsLight()
    {
        var state = StateWithWalls();
        state.NeighborMaps[0, 1] = null;
        var (lx, ly) = At(1, 5);

        Assert.That(LightOcclusion.Reaches(state, lx, ly, WorldLayer.Ground, W - 3, ly), Is.False);
    }
}
