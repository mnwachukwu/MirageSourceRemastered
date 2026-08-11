using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text;

namespace Mirage.Client.Shell.Screens;

/// <summary>The surface the network layer drives — chat lines, panel opens, floating text and death
/// effects pushed in from <c>IClientEvents</c>.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Public access for IClientEvents wiring ────────────────────────────────

    /// <summary>Spawn floating text anchored over an entity on (mapNum, lx, ly) with its
    /// current interp offset (xoff, yoff). Returns false if the entity isn't on screen.
    /// Centralizes the screen-coord conversion + below-sprite flip + pic-center offset that
    /// the half-dozen damage/heal/exp/levelup spawn sites otherwise repeat.</summary>
    public bool SpawnFloatingTextAtEntity(int mapNum, int lx, int ly, float xoff, float yoff,
                                          string text, Color color, int size = 1)
    {
        if (!TryEntityScreen(mapNum, lx, ly, xoff, yoff, out float sx, out float sy)) return false;
        bool floatDown = sy < RenderCommandBuilder.BelowSpriteThreshold;
        // Center on the footprint and, when floating below, clear the whole body (size*PicY) - size 1 is unchanged.
        float cx = sx + size * Constants.PicX / 2f;
        float cy = floatDown ? sy + size * Constants.PicY + FloatTextGapBelow : sy - FloatTextGapAbove;
        SpawnFloatingText(cx, cy, text, color, floatDown);
        return true;
    }

    /// <summary>Slide every live floating text by (dx, dy) world pixels. Called on a seamless
    /// border crossing (see <see cref="ClientState.GridShifted"/>) — the loaded grid data slides
    /// one cell in the opposite direction of the cross, so any world-pixel coord we cached at
    /// spawn time becomes stale by that same amount. Without this, floats spawned just before a
    /// seam (e.g. the "Enter Combat" tag on a pursuing player) drift off-screen the instant the
    /// camera re-frames around the new center map.</summary>
    public void ShiftFloatingTexts(int dx, int dy)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_floatingTexts);
        for (int i = 0; i < span.Length; i++)
        {
            span[i].X += dx;
            span[i].Y += dy;
        }
    }

    /// <summary>Drop every live floating text. Called on a warp/teleport/full reload (see
    /// <see cref="ClientState.MapStateCleared"/>): floats are anchored in the old map's world-pixel
    /// frame, so without this they hang over the destination map until they age out.</summary>
    public void ClearFloatingTexts() => _floatingTexts.Clear();

    public void SpawnFloatingText(float x, float y, string text, Color color, bool floatDown = false)
    {
        // Callers pass a SCREEN position (computed from the entity at spawn time via WorldTileToScreen).
        // Add the camera back to recover the exact world pixel so it stays anchored to the gameworld as
        // the camera scrolls; the same-frame stacking check below is then consistently in world space too.
        x += _camera.CameraX;
        y += _camera.CameraY;
        const float xThreshold = 50f;
        const float yThreshold = 20f;
        const float yStep = 16f;
        // Only floats that pop in the SAME frame over the SAME spot are stacked: they would otherwise render
        // exactly on top of one another. Frame-mates are the ones still at Age == 0 (aging runs once per
        // frame in Update, so anything spawned this frame hasn't aged yet); earlier-frame floats have
        // Age > 0 and are already drift-separated, so each of those simply starts at the origin.
        int stackIndex = 0;
        foreach (var ft in _floatingTexts)
        {
            if (ft.Age == 0f && ft.FloatDown == floatDown
                && Math.Abs(ft.X - x) < xThreshold && Math.Abs(ft.Y - y) < yThreshold)
            {
                stackIndex++;
            }
        }
        // Fan the column opposite the drift (toward the sprite): down (+y) for normal above-sprite text,
        // up (-y) when the text is flipped below the sprite.
        float stackOffset = (floatDown ? -1f : 1f) * stackIndex * yStep;
        _floatingTexts.Add(new FloatingText(x, y, text, color, stackOffset, floatDown));
    }
}
