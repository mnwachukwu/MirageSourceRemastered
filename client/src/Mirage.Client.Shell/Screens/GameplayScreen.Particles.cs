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

/// <summary>Weather and combat particle drawing — rain and wind streaks, melee swooshes, and the
/// spell-cast flashes — plus the generated particle textures.</summary>
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
}
