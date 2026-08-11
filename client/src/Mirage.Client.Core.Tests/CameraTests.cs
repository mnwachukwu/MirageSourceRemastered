using Mirage.Client.Core.Logic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests;

/// <summary>The seamless-world camera follows the local player on the center map but clamps to the loaded
/// 3x3 grid's extent, and its tile&lt;-&gt;screen transforms round-trip. With no neighbors it locks to the
/// center map exactly, reproducing the original single-map view (screenX == localX * PicX).</summary>
[TestFixture]
public class CameraTests
{
    // A 3x3 grid with only the center cell populated (the "no neighbors loaded" case).
    static MapRecord?[,] CenterOnly()
    {
        var g = new MapRecord?[3, 3];
        g[1, 1] = new MapRecord();
        return g;
    }

    static MapRecord?[,] FullGrid()
    {
        var g = new MapRecord?[3, 3];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                g[c, r] = new MapRecord();
        }

        return g;
    }

    // With no neighbors the camera locks to the center map regardless of player position: it pins to the
    // center map's top-left world pixel (512, 384) so the view never scrolls into the black void.
    [Test]
    public void Update_NoNeighbors_LocksToCenterMap()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, CenterOnly());
        Assert.Multiple(() =>
        {
            Assert.That(cam.CameraX, Is.EqualTo(512f));
            Assert.That(cam.CameraY, Is.EqualTo(384f));
        });

        // Player at the far corner of the center map: still locked (no neighbor to scroll toward).
        cam.Update(Constants.MaxMapX, Constants.MaxMapY, 0f, 0f, CenterOnly());
        Assert.Multiple(() =>
        {
            Assert.That(cam.CameraX, Is.EqualTo(512f));
            Assert.That(cam.CameraY, Is.EqualTo(384f));
        });
    }

    // The documented invariant: locked to center, a center-map local tile lands at screenX == localX*PicX.
    [Test]
    public void WorldTileToScreen_NoNeighbors_MatchesSingleMapView()
    {
        var cam = new Camera();
        cam.Update(8, 6, 0f, 0f, CenterOnly());
        for (int lx = 0; lx <= Constants.MaxMapX; lx++)
        {
            int worldX = WorldCoordHelper.MapTilesX + lx;
            var (sx, _) = cam.WorldTileToScreen(worldX, WorldCoordHelper.MapTilesY, 0f, 0f);
            Assert.That(sx, Is.EqualTo(lx * Constants.PicX), $"local x {lx}");
        }
    }

    // With every neighbor present the camera follows the player (centers them) and does not clamp mid-map.
    [Test]
    public void Update_FullGrid_FollowsPlayer()
    {
        var cam = new Camera();
        cam.Update(8, 6, 0f, 0f, FullGrid());
        float pwx = (WorldCoordHelper.MapTilesX + 8) * Constants.PicX;
        float pwy = (WorldCoordHelper.MapTilesY + 6) * Constants.PicY;
        Assert.Multiple(() =>
        {
            Assert.That(cam.CameraX, Is.EqualTo(pwx - Camera.ViewW / 2f));
            Assert.That(cam.CameraY, Is.EqualTo(pwy - Camera.ViewH / 2f));
        });
    }

    // Scrolling clamps to the grid edge: with only the center map, panning is locked; with a left column
    // present the camera can scroll one map-width further left than the center-only clamp.
    [Test]
    public void Update_LeftColumnPresent_AllowsScrollingLeft()
    {
        var withLeft = new MapRecord?[3, 3];
        withLeft[1, 1] = new MapRecord();
        withLeft[0, 1] = new MapRecord();   // a map exists to the left

        var cam = new Camera();
        cam.Update(0, 6, 0f, 0f, withLeft);   // player at the left edge of the center map
        // minCamX is now 0 (left column present), so the camera scrolls left of the center-only lock (512).
        Assert.That(cam.CameraX, Is.LessThan(512f));
    }

    // ScreenToGridTile is the inverse of the tile->screen map: the center-map origin round-trips to (1,1,0,0).
    [Test]
    public void ScreenToGridTile_InvertsWorldTileToScreen()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, CenterOnly());   // locked: CameraX=512, CameraY=384
        // Center map local (0,0) is world (16,12); its screen pos with the locked camera is (0,0).
        Assert.That(cam.ScreenToGridTile(0, 0), Is.EqualTo(new GridTileHit(Col: 1, Row: 1, LocalX: 0, LocalY: 0)));
    }

    // Pixels off the top/left of the world (negative world tile) are on no cell.
    [Test]
    public void ScreenToGridTile_OffWorld_ReturnsNull()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, FullGrid());
        Assert.That(cam.ScreenToGridTile(-300, -300), Is.Null);
    }

    [Test]
    public void IsWorldTileVisible_CenterTileVisible_FarTileNot()
    {
        var cam = new Camera();
        cam.Update(0, 0, 0f, 0f, CenterOnly());   // CameraX=512, CameraY=384
        Assert.Multiple(() =>
        {
            Assert.That(cam.IsWorldTileVisible(16, 12), Is.True, "center-map origin is on-screen");
            Assert.That(cam.IsWorldTileVisible(40, 12), Is.False, "a tile far to the right is culled");
        });
    }
}
