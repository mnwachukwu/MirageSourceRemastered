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

/// <summary>Particles end to end: the generated textures, the spawns that emit them (melee swings,
/// spell casts), the seam-cross and warp bookkeeping, the weather and combat draw passes, and the
/// transient lights the glowing ones push into the frame.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Particle FX drawing ────────────────────────────────────────────────────
    private const int ParticleCullMargin = Constants.PicX * 2; // draw a little past the viewport edge
    private const float StreakThickness = 2.5f;                // rain/wind streak width (px)
    private const float ProjectileLightRadius = Constants.PicX * 2; // 64 px — 2-tile glow reach for spell FX
    private const int ParticleLightIdScale = 100000;               // stable-ish Pulse seed from Seed (0..1)
    private const float ProjectileGlowMinSize = 8f;
    private const float ProjectileGlowFactor = 1.6f;
    private const float CubeShadowOffset = 3f;
    private const float CubeShadowAlpha = 0.35f;
    private const float SwooshMidRadius = 0.72f;    // crescent band center (0..1 of the arc texture radius)
    private const float SwooshRadialWidth = 0.14f;  // crescent band thickness
    private const float SwooshAngularSpread = 1.15f; // half-arc angular span (radians)
    private const float SwooshSweepRad = 0.9f;      // rotation swept over the flash's life (motion cue)

    /// <summary>Draws the SPELL/COMBAT particles on <paramref name="layer"/> into the world batch — called from
    /// <see cref="DrawWorld"/> inside that layer's entity pass, so a burst occludes with the bridge (a ground
    /// burst under the deck, a fringe one on top). Weather is drawn separately + globally.</summary>
    private void DrawLayerParticles(SpriteBatch sb, WorldLayer layer)
    {
        var particles = _particles.Active;
        if (particles.Length == 0) return;
        EnsureParticleTextures(sb.GraphicsDevice);
        for (int i = 0; i < particles.Length; i++)
        {
            if (!ParticleSystem.IsWeatherKind(particles[i].Kind) && particles[i].Layer == layer)
                DrawParticle(sb, in particles[i]);
        }
    }

    /// <summary>Draws the GLOBAL weather particles (rain/snow/wind/debris) above everything, after the fringe +
    /// canopy passes — they aren't layer-occluded (weather falls over the whole scene).</summary>
    private void DrawWeatherParticles(SpriteBatch sb)
    {
        var particles = _particles.Active;
        if (particles.Length == 0) return;
        EnsureParticleTextures(sb.GraphicsDevice);
        for (int i = 0; i < particles.Length; i++)
        {
            if (ParticleSystem.IsWeatherKind(particles[i].Kind))
                DrawParticle(sb, in particles[i]);
        }
    }

    private void DrawParticle(SpriteBatch sb, in Particle p)
    {
        // World-anchored: particles scroll with the map (parallax) and dim at night with the world.
        float sx = p.X - _camera.CameraX;
        float sy = p.Y - _camera.CameraY;
        if (sx < -ParticleCullMargin || sx > Camera.ViewW + ParticleCullMargin
            || sy < -ParticleCullMargin || sy > Camera.ViewH + ParticleCullMargin)
        {
            return;
        }

        float a = ParticleSystem.AlphaOf(in p);
        if (a <= 0f) return;
        // The world batch uses premultiplied AlphaBlend and the dot texture is a premultiplied white gradient,
        // so scaling a full-alpha tint by `a` scales rgb + alpha together (correct premultiplied fade).
        var tint = new Color((int)((p.Rgb >> 16) & 0xFF), (int)((p.Rgb >> 8) & 0xFF), (int)(p.Rgb & 0xFF)) * a;
        var tex = _particleDotTex!;

        switch (p.Kind)
        {
            case ParticleKind.RainStreak:
            case ParticleKind.WindStreak:
                // Angle the streak along its true on-screen direction (world fall + subtle camera parallax),
                // so the droplet visibly tilts as you move rather than always pointing straight down.
                var (osvx, osvy) = ParticleSystem.OnScreenVelocity(in p, _camVelX, _camVelY);
                float rot = MathF.Atan2(osvy, osvx);
                sb.Draw(tex, new Vector2(sx, sy), null, tint, rot, new Vector2(0f, tex.Height / 2f),
                    new Vector2(p.Size / tex.Width, StreakThickness / tex.Height), SpriteEffects.None, 0f);
                break;
            case ParticleKind.Swoosh:
                // Crescent aimed at the attacker's facing (Vx/Vy), sweeping through a small arc over its life.
                float swT = p.Age / p.Life;
                float swRot = MathF.Atan2(p.Vy, p.Vx) + (swT - 0.5f) * SwooshSweepRad;
                var sw = _swooshTex!;
                sb.Draw(sw, new Vector2(sx, sy), null, tint, swRot,
                    new Vector2(sw.Width / 2f, sw.Height / 2f), p.Size / sw.Width, SpriteEffects.None, 0f);
                break;
            case ParticleKind.Cube:
                // Gray box with a soft drop shadow; p.Rgb (white) drives the light/glow, not the body.
                float half = p.Size / 2f;
                sb.Draw(_particlePixelTex!,
                    new Rectangle((int)(sx - half + CubeShadowOffset), (int)(sy - half + CubeShadowOffset), (int)p.Size, (int)p.Size),
                    new Color(0, 0, 0, (int)(CubeShadowAlpha * a * 255f)));
                sb.Draw(_particlePixelTex!,
                    new Rectangle((int)(sx - half), (int)(sy - half), (int)p.Size, (int)p.Size),
                    new Color(140, 140, 150) * a);
                break;
            default: // round particles: dot stretched to Size
                var dest = new Rectangle((int)(sx - p.Size / 2f), (int)(sy - p.Size / 2f), (int)p.Size, (int)p.Size);
                sb.Draw(tex, dest, tint);
                break;
        }
    }

    // Soft premultiplied white radial dot; round particles use it directly, streaks stretch it into a capsule.
    private void EnsureParticleTextures(GraphicsDevice gd)
    {
        if (_particleDotTex is not null) return;
        const int r = 8, size = r * 2;
        var px = new Color[size * size];
        float c = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy) / r;
                byte alpha = d >= 1f ? (byte)0 : (byte)((1f - d) * 255f);
                px[y * size + x] = new Color(alpha, alpha, alpha, alpha); // premultiplied
            }
        }

        _particleDotTex = new Texture2D(gd, size, size);
        _particleDotTex.SetData(px);
        _particlePixelTex = new Texture2D(gd, 1, 1);
        _particlePixelTex.SetData(new[] { Color.White });

        // Crescent blade-arc: an annulus segment — a soft radial bump around a mid-radius, tapered in angle,
        // centered on +x so a draw rotation aims it at the attacker's facing.
        const int asz = 48;
        var apx = new Color[asz * asz];
        float ac = asz / 2f;
        for (int y = 0; y < asz; y++)
        {
            for (int x = 0; x < asz; x++)
            {
                float dx = x - ac + 0.5f, dy = y - ac + 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy) / (asz / 2f); // 0..1
                float ang = MathF.Atan2(dy, dx);                      // -pi..pi, 0 = +x
                float radial = MathF.Exp(-((d - SwooshMidRadius) * (d - SwooshMidRadius)) / (2f * SwooshRadialWidth * SwooshRadialWidth));
                float angular = MathF.Max(0f, 1f - MathF.Abs(ang) / SwooshAngularSpread);
                byte a = (byte)(Math.Clamp(radial * angular, 0f, 1f) * 255f);
                apx[y * asz + x] = new Color(a, a, a, a); // premultiplied
            }
        }

        _swooshTex = new Texture2D(gd, asz, asz);
        _swooshTex.SetData(apx);
    }

    /// <summary>Re-anchor world-pixel particles + deferred hit FX on a seamless seam cross (mirrors
    /// <see cref="ShiftFloatingTexts"/>).</summary>
    public void ShiftParticles(int dx, int dy)
    {
        _particles.ShiftAll(dx, dy);
        // The camera re-anchors by the same (dx,dy) on this cross; rebase the velocity baseline so next frame's
        // (CameraX - _prevCamX) reflects only real player motion, not the seam jump (keeps weather from lurching).
        _prevCamX += dx;
        _prevCamY += dy;
        for (int i = 0; i < _deferredFloats.Count; i++) { var d = _deferredFloats[i]; d.WorldX += dx; d.WorldY += dy; _deferredFloats[i] = d; }
        for (int i = 0; i < _delayedDeaths.Count; i++) { var g = _delayedDeaths[i]; g.WorldX += dx; g.WorldY += dy; _delayedDeaths[i] = g; }
    }
    /// <summary>Drop all particles + deferred hit FX on a warp/teleport (mirrors <see cref="ClearFloatingTexts"/>).</summary>
    public void ClearParticles()
    {
        _particles.ClearAll();
        _pendingHits.Clear();
        _deferredFloats.Clear();
        _delayedDeaths.Clear();
    }

    /// <summary>Spawn a melee swing FX over the attacker's target tile (one step ahead in the facing dir),
    /// world-anchored. <paramref name="sparks"/> is true when the swing connected (crescent + sparks) and
    /// false on a whiff (crescent only). Driven by <see cref="ClientPacketHandler.MeleeSwing"/>.</summary>
    public void SpawnMeleeSwing(int map, int lx, int ly, float xoff, float yoff, Direction dir, bool sparks)
    {
        if (!TryEntityScreen(map, lx, ly, xoff, yoff, out float sx, out float sy)) return;
        var (dxT, dyT) = DirDelta(dir);
        // World-pixel CENTER of the target tile: attacker tile origin (screen + camera) + one tile ahead + half.
        float wx = sx + _camera.CameraX + (dxT + 0.5f) * Constants.PicX;
        float wy = sy + _camera.CameraY + (dyT + 0.5f) * Constants.PicY;
        _particles.EmitMeleeSwing(wx, wy, dxT, dyT, sparks, LayerAtTile(map, lx, ly));
    }

    /// <summary>The logical layer of an entity standing at (map,lx,ly) — used to anchor a combat/spell FX particle
    /// so it occludes with the bridge like the entity that emitted it (a ground burst under the deck, a fringe one
    /// on top). Defaults to Ground when no entity is found there (the FX origin is always some entity's tile).</summary>
    private WorldLayer LayerAtTile(int map, int lx, int ly)
    {
        var st = _ctx.State;
        foreach (var p in st.Players)
            if (p.Map == map && p.X == lx && p.Y == ly) return p.Layer;
        var npcs = st.NpcsForMap(map);
        if (npcs is not null)
        {
            foreach (var n in npcs)
                if (n.Num > 0 && n.X == lx && n.Y == ly) return n.Layer;
        }

        return WorldLayer.Ground;
    }

    private static (int dx, int dy) DirDelta(Direction dir) => dir switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        Direction.Right => (1, 0),
        _ => (0, 0),
    };

    /// <summary>Spawn typed spell FX for a cast: a projectile homing from the caster to the resolved target
    /// (or arriving in place if self-cast / the target isn't observable). Driven by
    /// <see cref="ClientPacketHandler.SpellCast"/>.</summary>
    public void SpawnSpellCast(SpellCastFx fx)
    {
        if (!TryEntityScreen(fx.CasterMap, fx.CasterX, fx.CasterY, fx.CasterXOff, fx.CasterYOff, out float csx, out float csy)) return;
        // Center the bolt on each body's FOOTPRINT (size*Pic/2) so an oversize NPC's cast leaves and arrives at its
        // center of mass, not its top-left anchor tile.
        int casterSize = fx.CasterSize < 1 ? 1 : fx.CasterSize;
        float sx = csx + _camera.CameraX + casterSize * Constants.PicX / 2f;
        float sy = csy + _camera.CameraY + casterSize * Constants.PicY / 2f;
        float ex = sx, ey = sy; // default: self / unresolved target → arrive in place (no travel)
        if (ResolveTargetTile(fx.Target, out int tMap, out int tX, out int tY)
            && TryEntityScreen(tMap, tX, tY, 0f, 0f, out float tsx, out float tsy))
        {
            int targetSize = TargetFootprintSize(fx.Target);
            ex = tsx + _camera.CameraX + targetSize * Constants.PicX / 2f;
            ey = tsy + _camera.CameraY + targetSize * Constants.PicY / 2f;
        }
        // Record a pending hit so the target's damage/heal number releases when the bolt would land (in sync).
        float dist = MathF.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
        if (dist > 1f)
            _pendingHits.Add(new PendingHit { Target = fx.Target, ReleaseMs = Environment.TickCount64 + (long)ParticleSystem.ProjectileFlightMs(dist) });
        _particles.EmitSpell(fx.Type, sx, sy, ex, ey, LayerAtTile(fx.CasterMap, fx.CasterX, fx.CasterY));
    }

    /// <summary>Pushes a transient light + glow core for each light-emitting particle (spell FX) into the
    /// frame so glowing projectiles illuminate the night (light map) and punch through darkness (glow seam).
    /// Runs after the frame is built and before the light pass, since it appends to Lights/Glows.</summary>
    private void EmitParticleLights()
    {
        float rawDark = _ctx.State.GetCurrentDarkness();
        int bleed = MirageGame.MapAreaBleed;
        var particles = _particles.Active;
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly var p = ref particles[i];
            if (!ParticleSystem.EmitsLight(p.Kind)) continue;
            float sx = p.X - _camera.CameraX;
            float sy = p.Y - _camera.CameraY;
            if (sx < -ParticleCullMargin || sx > Camera.ViewW + ParticleCullMargin
                || sy < -ParticleCullMargin || sy > Camera.ViewH + ParticleCullMargin)
            {
                continue;
            }

            int id = (int)(p.Seed * ParticleLightIdScale);
            bool inDark = false;
            foreach (var r in _renderFrame.AlwaysDarkMapLights)
            {
                if (sx >= r.ScreenX - bleed && sx < r.ScreenX + Camera.ViewW + bleed &&
                    sy >= r.ScreenY - bleed && sy < r.ScreenY + Camera.ViewH + bleed)
                {
                    inDark = true;
                    break;
                }
            }
            float effectiveDark = inDark ? 1f : rawDark;
            // LightSourceCmd centers via +HalfTile at draw, so pass center-minus-half-tile.
            _renderFrame.Lights.Add(new LightSourceCmd(sx - HalfTile, sy - HalfTile, 1f, p.Rgb,
                ProjectileLightRadius, FlickerStyle.Pulse, id, effectiveDark, p.Layer));
            _renderFrame.Glows.Add(new GlowCmd(sx, sy, p.Rgb, MathF.Max(p.Size, ProjectileGlowMinSize) * ProjectileGlowFactor));
        }
    }

    private static void DrawLightHalo(SpriteBatch sb, Texture2D outerTex, Texture2D innerTex,
        float cx, float cy, in LightSourceCmd cmd, float totalSec)
    {
        // intensity fades the whole halo out where a safe-zone town light already covers this emitter.
        float lit = cmd.EffectiveDarkness * cmd.Intensity;
        var core = UnpackRgb(cmd.Rgb);

        // Outer reach — static size (cmd.Radius), dim (core × OuterDimFactor).
        float outerR = cmd.Radius;
        var outerDest = new Rectangle(
            (int)(cx - outerR), (int)(cy - outerR), (int)(outerR * 2f), (int)(outerR * 2f));
        var outerTint = ScaleGlow(core * LightModel.OuterDimFactor, lit);
        if (cmd.Reach is null) sb.Draw(outerTex, outerDest, outerTint);
        else DrawOccludedHalo(sb, outerTex, outerDest, outerTint, cmd);

        // Inner core — brightness animates both ways per FlickerStyle; size only oscillates up from the base
        // (floored at MinInnerSizeFactor) so the core never shrinks small.
        float f = LightModel.FlickerFor(cmd.Flicker, totalSec, cmd.Id);
        float innerR = cmd.Radius * LightModel.InnerRadiusFactor;
        float sizeF = MathF.Max(f, LightModel.MinInnerSizeFactor);
        int innerSize = (int)(innerR * 2f * sizeF);
        var innerDest = new Rectangle(
            (int)(cx - innerSize / 2f), (int)(cy - innerSize / 2f), innerSize, innerSize);
        var innerTint = ScaleGlow(core, lit * f);
        if (cmd.Reach is null) sb.Draw(innerTex, innerDest, innerTint);
        else DrawOccludedHalo(sb, innerTex, innerDest, innerTint, cmd);
    }

    /// <summary>
    /// Draws a halo one tile at a time, skipping the tiles the light cannot reach.
    ///
    /// <para>Each tile takes the slice of the halo texture that would have covered it, so the gradient is
    /// the same one a single sprite would have drawn and the only new edges are at the walls. Additive
    /// accumulation is unchanged: two lights still sum on a tile they both reach.</para>
    /// </summary>
    private static void DrawOccludedHalo(SpriteBatch sb, Texture2D tex, Rectangle dest, Color tint,
                                         in LightSourceCmd cmd)
    {
        var reach = cmd.Reach!;
        // The halo's own bounds in tiles, relative to the emitter, so only tiles it covers are considered.
        int firstX = (int)MathF.Floor(dest.Left / (float)Constants.PicX);
        int lastX = (int)MathF.Ceiling(dest.Right / (float)Constants.PicX) - 1;
        int firstY = (int)MathF.Floor(dest.Top / (float)Constants.PicY);
        int lastY = (int)MathF.Ceiling(dest.Bottom / (float)Constants.PicY) - 1;

        for (int ty = firstY; ty <= lastY; ty++)
        {
            for (int tx = firstX; tx <= lastX; tx++)
            {
                // Screen tile -> world tile: the emitter's world tile is at its own screen tile.
                int wx = cmd.TileX + tx - (int)MathF.Floor(cmd.ScreenX / Constants.PicX);
                int wy = cmd.TileY + ty - (int)MathF.Floor(cmd.ScreenY / Constants.PicY);
                if (wx < 0 || wx >= LightOcclusion.GridW || wy < 0 || wy >= LightOcclusion.GridH) continue;
                if (!reach[wy * LightOcclusion.GridW + wx]) continue;

                var cell = new Rectangle(tx * Constants.PicX, ty * Constants.PicY, Constants.PicX, Constants.PicY);
                var slice = Rectangle.Intersect(cell, dest);
                if (slice.Width <= 0 || slice.Height <= 0) continue;

                // The matching slice of the halo texture, so every tile keeps the gradient it would have had.
                var src = new Rectangle(
                    (int)((slice.X - dest.X) / (float)dest.Width * tex.Width),
                    (int)((slice.Y - dest.Y) / (float)dest.Height * tex.Height),
                    Math.Max(1, (int)(slice.Width / (float)dest.Width * tex.Width)),
                    Math.Max(1, (int)(slice.Height / (float)dest.Height * tex.Height)));
                sb.Draw(tex, slice, src, tint);
            }
        }
    }

    // Scales a peak light color by an intensity factor, folding brightness into the RGB channels
    // (max-blended into the light map; the blend ignores source alpha for color).
    private static Color ScaleGlow(Vector3 peak, float k) => new(
        (byte)Math.Clamp(peak.X * k, 0f, 255f),
        (byte)Math.Clamp(peak.Y * k, 0f, 255f),
        (byte)Math.Clamp(peak.Z * k, 0f, 255f),
        (byte)255);
}
