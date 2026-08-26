using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Diagnostics;

namespace Mirage.Client.Core.Tests;

/// <summary>
/// What the occlusion masks actually cost. <b>[Explicit] — run manually.</b>
///
/// <para>Every light rebuilds its reach mask each frame: one line-of-sight trace per TILE it covers, then
/// one write per TEXEL, at <see cref="LightOcclusion.SubSamples"/> texels a tile in each direction. The
/// trace count grows with the radius squared; the texel count grows with the radius squared AND the
/// subdivision squared, so the two halves scale very differently and it is worth knowing which one is the
/// bill before reaching for either lever.</para>
///
/// <para>A moving emitter pays for both, twice — it traces the tile it is leaving and the one it is
/// entering so the shadows can blend across the step. A standing one pays once.</para>
/// </summary>
[TestFixture]
[Explicit, Category("Benchmark")]
public class LightPerfBaselineTests
{
    // A 3x3 of maps with walls scattered thickly enough that occlusion has real work to do — a clear field
    // measures the loop but not the tracing.
    private static ClientState WalledState()
    {
        var state = new ClientState { MyIndex = 1, CenterMapNum = 1 };
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                var map = new MapRecord();
                for (int x = 0; x <= Constants.MaxMapX; x++)
                {
                    for (int y = 0; y <= Constants.MaxMapY; y++)
                    {
                        bool wall = (x * 7 + y * 3) % 11 == 0;
                        map.EditTile(x, y, t => t with { Type = wall ? TileType.Blocked : TileType.Walkable });
                    }
                }

                state.NeighborMaps[col, row] = map;
                state.NeighborMapNums[col, row] = col * 3 + row + 1;
            }
        }

        state.CenterMapNum = state.NeighborMapNums[1, 1];
        return state;
    }

    // The BEST of several passes, not the mean. Frequency scaling and stray work only ever make a pass
    // slower, so the minimum is the closest estimate of what the code costs — and it is what stops a run
    // reporting a smaller radius as dearer than a larger one, which is how noise announces itself.
    private static double MicrosPer(int reps, Action body)
    {
        for (int i = 0; i < reps / 4 + 1; i++) body();   // JIT and warm the caches
        double best = double.MaxValue;
        for (int pass = 0; pass < 5; pass++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < reps; i++) body();
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMicroseconds / reps);
        }

        return best;
    }

    [Test]
    public void Benchmark_Fill_CostPerLightByRadius()
    {
        var state = WalledState();
        int cx = state.MapTilesX + state.MapTilesX / 2;
        int cy = state.MapTilesY + state.MapTilesY / 2;

        TestContext.Out.WriteLine($"SubSamples = {LightOcclusion.SubSamples}");
        TestContext.Out.WriteLine("  r   tiles  texels   KB      us/fill");
        foreach (int r in new[] { 2, 3, 4, 6, 8 })
        {
            var mask = new bool[LightOcclusion.MaskCells(r)];
            int side = LightOcclusion.MaskSide(r);
            int texels = LightOcclusion.MaskTexels(r);
            double us = MicrosPer(2000, () =>
                LightOcclusion.Fill(state, cx, cy, WorldLayer.Ground, r, mask));
            // What the shell then uploads: one 32-bit texel each, which is the GPU-side half of the bill.
            double kb = texels * texels / 1024.0;   // Alpha8: one byte a texel
            TestContext.Out.WriteLine(
                $"  {r,-3} {side * side,5}  {texels * texels,6}  {kb,6:F1}  {us,9:F1}");
        }
    }

    /// <summary>The frame cost of the whole builder with a crowd of torch-bearers, standing versus walking.
    /// The difference is the second trace the cross-fade needs, and it is the only thing movement adds.</summary>
    [Test]
    public void Benchmark_Build_StandingVersusWalking()
    {
        TestContext.Out.WriteLine("torches   standing us/frame   walking us/frame   bytes/frame");
        foreach (int torches in new[] { 1, 5, 10, 20 })
        {
            TestContext.Out.WriteLine(
                $"  {torches,-8}  {Measure(torches, false),15:F1}  {Measure(torches, true),17:F1}  {Bytes(torches),11:F0}");
        }

        static ClientState Crowd(int torches, bool walking)
        {
            var state = WalledState();
            for (int i = 1; i <= torches && i < state.Players.Length; i++)
            {
                var p = state.Players[i];
                p.Name = $"player{i}";
                p.Sprite = 1;
                p.Map = state.CenterMapNum;   // on screen, or the emitter is culled before it traces
                p.X = 1 + i % (Constants.MaxMapX - 1);
                p.Y = 1 + i % (Constants.MaxMapY - 1);
                p.XOffset = walking ? -Constants.PicX / 2f : 0f;
            }

            return state;
        }

        static double Measure(int torches, bool walking)
        {
            var state = Crowd(torches, walking);
            var frame = new RenderFrame();
            var camera = new Camera();
            camera.Update(8, 6, 0f, 0f, state.NeighborMapNums, state.MapTilesX, state.MapTilesY);
            return MicrosPer(400, () =>
            {
                frame.Clear();
                RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
            });
        }

        static double Bytes(int torches)
        {
            var state = Crowd(torches, true);
            var frame = new RenderFrame();
            var camera = new Camera();
            camera.Update(8, 6, 0f, 0f, state.NeighborMapNums, state.MapTilesX, state.MapTilesY);
            RenderCommandBuilder.Build(state, frame, camera, myIndex: state.MyIndex);
            double bytes = 0;
            foreach (var light in frame.Lights)
            {
                int texels = LightOcclusion.MaskTexels(light.ReachRadius);
                bytes += texels * texels;                           // Alpha8: one byte a texel
                if (light.ReachInto is not null) bytes += texels * texels;
            }

            return bytes;
        }
    }
}
