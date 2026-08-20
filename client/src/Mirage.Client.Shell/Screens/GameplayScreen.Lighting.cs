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

/// <summary>The night light map: ambient darkness, per-light halos, safe-zone and always-dark
/// regions, and the glow pass over it.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Light-source constants ─────────────────────────────────────────────────────────────────────

    // Halo geometry + flicker constants/curves now live in Mirage.Shared LightModel, shared verbatim with the
    // editor's night preview so the two can't drift (inner core = outer reach × LightModel.InnerRadiusFactor,
    // dim outer tint = core × LightModel.OuterDimFactor).
    private const int HalfTile = Constants.PicX / 2;
    // Per-light core color now travels on LightSourceCmd.Rgb (default torch = Core's TorchLightRgb); the halo
    // textures stay white-luminance and are tinted at draw. The dim outer tint = core × OuterDimFactor, the
    // inner core = the full color scaled by darkness and flicker — a single torch center still lands warm gold.
    // Safe-zone map light: a steady map-wide area light (no flicker), max-blended so contiguous safe maps
    // merge without seam glow. FULL WHITE = full daylight (×1.0 in the multiply). Because the town light map
    // is already at white, an entity's additive halo on top just clamps at 255 → invisible in town — so no
    // per-emitter suppression is needed; emitters only show where they spill into the darker wilderness.
    // Warm, slightly-dimmed daylight (below white) — a "lit town" look with a touch of evening warmth.
    // Because it's below white, an entity's additive halo could show through in town, so emitters instead
    // fade their own halo out by the safe-zone coverage (see TownLightCoverage) — full daylight would have
    // masked emitters for free, but the warm look is preferred. Raise toward white for a neutral daylit town.
    private static readonly Vector3 SafeZoneLightPeak = new(200f, 195f, 175f);

    // Flicker seeding + curves (Flame/Pulse) live in LightModel.FlickerFor, shared with the editor preview.

    // Unpack a packed 0xRRGGBB color to a straight-RGB 0..255 vector for tinting the white halo textures.
    private static Vector3 UnpackRgb(uint rgb) => new((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    // Same, as a Color (used for the additive glow-seam draw).
    private static Color UnpackColor(uint rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>
    /// Builds the light map into the currently-bound light RT. EVERYTHING composites with MAX blend so no
    /// two light sources bleed into each other: safe-zone area lights, then entity halos. Each entity halo
    /// is a single baked sprite (inner+outer already fused), so max between sources never flattens a light's
    /// own glow. Call while the light RT is bound, with <paramref name="transform"/> =
    /// <c>Matrix.CreateScale(_worldSS)</c>. Composited over the world by multiply in Pass 2.
    /// </summary>
    public void DrawLightMap(SpriteBatch sb, Matrix transform, Texture2D boxTex, Texture2D outerTex,
        Texture2D innerTex, float totalSec, WorldLayer? layerFilter = null)
    {
        // Safe-zone map area lights FIRST, MAX-blended: contiguous safe cells' flat interiors tile with no
        // seam, skirts spill. (Emitters are excluded from the Lights list inside safe cells, so no halo
        // sits over a town — only the wilderness-side skirt meets halos, where additive fills cleanly.)
        if (_renderFrame.AlwaysLitMapLights.Count > 0)
        {
            sb.Begin(SpriteSortMode.Deferred, MirageGame.MaxLightBlend,
                SamplerState.LinearClamp, null, null, null, transform);
            var safeColor = ScaleGlow(SafeZoneLightPeak, 1f);
            foreach (var m in _renderFrame.AlwaysLitMapLights)
            {
                var dest = new Rectangle(
                    (int)m.ScreenX - MirageGame.MapAreaBleed, (int)m.ScreenY - MirageGame.MapAreaBleed,
                    Camera.ViewW + MirageGame.MapAreaBleed * 2, Camera.ViewH + MirageGame.MapAreaBleed * 2);
                sb.Draw(boxTex, dest, safeColor);
            }
            sb.End();
        }

        // Per-map light overrides SECOND, ALPHA-BLENDED: indoor maps stay lit (White), AlwaysDark maps
        // stay dark (NightAmbient) regardless of time of day. Indoors drawn before AlwaysDark so darkness
        // wins on co-located maps. Deliberately AFTER the safe-zone MAX pass: an explicit per-map override
        // must win over a neighboring safe map's light skirt. Otherwise a safe map adjacent to an
        // AlwaysDark map would MAX-blend its bright spill ~2 tiles into the dark cell, lighting the edge,
        // pushing the visible seam inward, and delaying torch visibility. Alpha-blending the solid override
        // (alpha 1 over the cell) here paints back over any bled-in safe light; the override's own skirt
        // then spills the correct way — darkness OUT of the dark map, light OUT of the indoor map.
        if (_renderFrame.IndoorsMapLights.Count > 0 || _renderFrame.AlwaysDarkMapLights.Count > 0)
        {
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, null, null, null, transform);
            foreach (var m in _renderFrame.IndoorsMapLights)
            {
                var dest = new Rectangle(
                    (int)m.ScreenX - MirageGame.MapAreaBleed, (int)m.ScreenY - MirageGame.MapAreaBleed,
                    Camera.ViewW + MirageGame.MapAreaBleed * 2, Camera.ViewH + MirageGame.MapAreaBleed * 2);
                sb.Draw(boxTex, dest, Color.White);
            }
            foreach (var m in _renderFrame.AlwaysDarkMapLights)
            {
                var dest = new Rectangle(
                    (int)m.ScreenX - MirageGame.MapAreaBleed, (int)m.ScreenY - MirageGame.MapAreaBleed,
                    Camera.ViewW + MirageGame.MapAreaBleed * 2, Camera.ViewH + MirageGame.MapAreaBleed * 2);
                sb.Draw(boxTex, dest, MirageGame.NightAmbient);
            }
            sb.End();
        }

        // Entity halos SECOND, ADDITIVE: static outer reach + flickering inner core. Overlapping halos (and
        // the safe skirt at a town border) sum and blend smoothly — no seams, no dips — clamping at white
        // (= fully lit) in the 8-bit light map, so nothing floods brighter than "no lighting effect".
        sb.Begin(SpriteSortMode.Deferred, MirageGame.LightAccumBlend,
            SamplerState.LinearClamp, null, null, null, transform);
        foreach (var cmd in _renderFrame.Lights)
        {
            // Two light maps (occlusion split): a filtered pass builds only ONE layer's halos (ground vs fringe)
            // so ground-layer content is lit by the ground map and fringe content by the fringe map. Unfiltered
            // (layerFilter == null) is the single-map flat/daylight path — every halo, as before.
            if (layerFilter is not null && cmd.Layer != layerFilter) continue;
            DrawLightHalo(sb, outerTex, innerTex, cmd.ScreenX + HalfTile, cmd.ScreenY + HalfTile, cmd, totalSec);
        }
        sb.End();
    }

    /// <summary>
    /// Draws the additive FX glow cores (spell balls, sparkles) at the post-composite "glow seam" so they
    /// read through night darkness. Called by <see cref="MirageGame"/> after the light multiply with the
    /// composite's map rect + scale; positions are world-RT screen space (glow CENTER). No-op when empty.
    /// </summary>
    public void DrawGlows(SpriteBatch sb, Texture2D haloTex, Rectangle mapRect, float scaleX, float scaleY)
    {
        if (_renderFrame.Glows.Count == 0) return;
        var prevScissor = sb.GraphicsDevice.ScissorRectangle;
        sb.GraphicsDevice.ScissorRectangle = mapRect;
        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, null,
            MirageGame.WorldCompositeRaster);
        foreach (var g in _renderFrame.Glows)
        {
            float r = g.Radius * scaleX;
            float cx = mapRect.X + g.ScreenX * scaleX;
            float cy = mapRect.Y + g.ScreenY * scaleY;
            var dest = new Rectangle((int)(cx - r), (int)(cy - r), (int)(r * 2f), (int)(r * 2f));
            sb.Draw(haloTex, dest, UnpackColor(g.Rgb));
        }
        sb.End();
        sb.GraphicsDevice.ScissorRectangle = prevScissor;
    }
}
