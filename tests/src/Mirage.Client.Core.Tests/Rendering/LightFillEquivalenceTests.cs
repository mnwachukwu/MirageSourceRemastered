using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests;

/// <summary>
/// <see cref="LightOcclusion.Fill"/> against the rule it is an optimisation of.
///
/// <para>The shipped fill exploits the fact that only a tile's two OUTWARD edges can ever be pulled back —
/// every texel further in has all three of its consulted neighbours inside its own tile. That is worth
/// sixty-four lookups a tile, and it is exactly the kind of reasoning that is right in the general case and
/// wrong in some corner nobody thought about.</para>
///
/// <para>So the naive rule is written out here, once, in the form it is easy to check by eye, and the two are
/// run against the same maps and compared texel for texel. Walls are laid down in patterns rather than at
/// random so a failure names a case rather than a seed.</para>
/// </summary>
[TestFixture]
public class LightFillEquivalenceTests
{
    private const int W = 24, H = 20;

    // The rule, stated plainly: a texel is lit if its tile is reached and none of the three neighbours on
    // the far side of it from the light blocks light.
    private static bool[] Naive(ClientState state, int lx, int ly, WorldLayer layer, int r)
    {
        int side = LightOcclusion.MaskSide(r);
        int texels = LightOcclusion.MaskTexels(r);
        int sub = LightOcclusion.SubSamples;
        var tiles = new bool[side * side];
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
                tiles[(dy + r) * side + (dx + r)] = LightOcclusion.Reaches(state, lx, ly, layer, lx + dx, ly + dy);
        }

        bool Blocks(int nx, int ny) =>
            nx >= 0 && nx < texels && ny >= 0 && ny < texels && !tiles[(ny / sub) * side + (nx / sub)];

        var mask = new bool[texels * texels];
        float lightAt = r * sub + (sub - 1) / 2f;
        for (int ty = 0; ty < texels; ty++)
        {
            int sy = ty > lightAt ? 1 : -1;
            for (int tx = 0; tx < texels; tx++)
            {
                int sx = tx > lightAt ? 1 : -1;
                mask[ty * texels + tx] = tiles[(ty / sub) * side + (tx / sub)]
                    && !Blocks(tx + sx, ty) && !Blocks(tx, ty + sy) && !Blocks(tx + sx, ty + sy);
            }
        }

        return mask;
    }

    private static ClientState StateWhere(Func<int, int, bool> wall)
    {
        var state = new ClientState();
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                var map = new MapRecord(W, H);
                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                        if (wall(x, y)) map.EditTile(x, y, t => t with { Type = TileType.Blocked });
                }

                state.NeighborMaps[col, row] = map;
            }
        }

        return state;
    }

    private static void AssertMatches(string what, Func<int, int, bool> wall, int r)
    {
        var state = StateWhere(wall);
        int lx = W + W / 2, ly = H + H / 2;                 // stand in the centre map
        var fast = new bool[LightOcclusion.MaskCells(r)];
        LightOcclusion.Fill(state, lx, ly, WorldLayer.Ground, r, fast);
        var naive = Naive(state, lx, ly, WorldLayer.Ground, r);

        int texels = LightOcclusion.MaskTexels(r);
        for (int i = 0; i < naive.Length; i++)
        {
            if (fast[i] == naive[i]) continue;
            Assert.Fail($"{what} (r={r}): texel ({i % texels},{i / texels}) is {fast[i]} but the rule says {naive[i]}");
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(5)]
    public void OpenGround(int r) => AssertMatches("open ground", (_, _) => false, r);

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(5)]
    public void SolidEverywhere(int r) => AssertMatches("nothing but wall", (_, _) => true, r);

    /// <summary>A wall on each side in turn — the four cases where a whole outward edge is pulled back.</summary>
    [TestCase(3)]
    public void WallsOnEachSide(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("wall east", (x, y) => x == cx + 2 && y == cy, r);
        AssertMatches("wall west", (x, y) => x == cx - 2 && y == cy, r);
        AssertMatches("wall south", (x, y) => x == cx && y == cy + 2, r);
        AssertMatches("wall north", (x, y) => x == cx && y == cy - 2, r);
    }

    /// <summary>Diagonal-only contact, which is the case the corner texel exists for.</summary>
    [TestCase(3)]
    public void WallTouchingOnlyAtACorner(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("wall on the diagonal", (x, y) => x == cx + 2 && y == cy + 2, r);
    }

    /// <summary>The light boxed in, so its own tile — the one row and column that straddle it — is pulled
    /// back on all four sides at once.</summary>
    [TestCase(3)]
    public void BoxedIn(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("boxed in", (x, y) =>
            (Math.Abs(x - cx) <= 1 && Math.Abs(y - cy) <= 1) && !(x == cx && y == cy), r);
    }

    /// <summary>A one-tile corridor and a one-tile doorway, where the pull-back meets itself.</summary>
    [TestCase(3)]
    public void CorridorsAndDoorways(int r)
    {
        int cx = W + W / 2, cy = H + H / 2;
        AssertMatches("east-west corridor", (x, y) => y == cy - 1 || y == cy + 1, r);
        AssertMatches("north-south corridor", (x, y) => x == cx - 1 || x == cx + 1, r);
        AssertMatches("doorway", (x, y) => x == cx + 2 && y != cy, r);
    }

    /// <summary>Scattered walls, which is what a real map looks like: every relative direction, every
    /// combination of the three neighbours, all at once.</summary>
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void ScatteredWalls(int r)
    {
        AssertMatches("scatter A", (x, y) => (x * 7 + y * 3) % 11 == 0, r);
        AssertMatches("scatter B", (x, y) => (x * 5 + y * 13) % 7 == 0, r);
        AssertMatches("scatter C", (x, y) => ((x / 2) + (y / 3)) % 3 == 0, r);
    }
}
