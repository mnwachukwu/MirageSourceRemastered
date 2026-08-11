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

/// <summary>Blood decals: the generated stain atlas, the accumulation pass into an offscreen field,
/// and the composite that lays it under the entities.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Blood-pool decals ──────────────────────────────────────────────────────
    private Texture2D? _bloodTex;
    private const int BloodVariants = 3;    // lumpy CORE variants in the atlas (the streak + droplet cells follow these)
    private const int BloodBlobSize = 96;   // px per atlas cell (resolution headroom for big draws)
    private const float BloodCoreRadius = 0.52f;   // central blob radius (normalized; 1 = cell half) — bigger core so pools read solid + merge
    // Droplets are stamped SEPARATELY from the core (not baked in): their COUNT ramps with the pool's amount and
    // they sit at FIXED positions so they don't slide as the pool grows/shrinks (see the draw loop).
    private const int BloodSatelliteMax = 30;         // droplet count at a FULL tile — ramps in lockstep with the blob size (shares BloodSizeFullAmount), so droplets ring the pool at every size, birth-anchored
    private const float BloodSatelliteMinPx = 1f;     // droplet diameter range (px) — per-droplet, FIXED (independent of pool size)
    private const float BloodSatelliteMaxPx = 4f;
    private const float BloodSatelliteDistMinFrac = 0.30f; // droplet distance as a FRACTION of the pool size (min) — so droplets sit just past the rim at ANY size, not a fixed px that's too far on a small pool / swallowed by a big one
    private const float BloodSatelliteDistMaxFrac = 0.48f; // + up to here, per-droplet — a tight cluster hugging the rim
    private const float BloodDropletFadeFloor = 0.6f;  // droplets are fully GONE once the stain's freshness falls to this — so the spatter clears while the pool is still dark (raise toward 1 to clear it even sooner)
    private const float BloodDropletFadePower = 2.5f;  // fade curve WITHIN the [floor..1] window — >1 so the spatter thins out faster than the pool, cueing a stain's freshness

    // One atlas texture holding, side by side: BloodVariants lumpy CORE blobs then a single DROPLET cell (a soft
    // dot), stamped as separate quads at draw time (see DrawBloodInfluence) and welded into one mass by the
    // MAX-blend accumulation buffer.  EVERY cell fades to fully transparent at its edges, so linear sampling at a
    // source-rect boundary can't bleed one cell's ink into the next (that bleed was showing as stray red lines at
    // blob edges).  Premultiplied white, tinted dark red at composite.  Deterministic (hashed shape, no RNG).
    private void EnsureBloodTextures(GraphicsDevice gd)
    {
        if (_bloodTex is not null) return;
        int sz = BloodBlobSize, cells = BloodVariants + 1, w = sz * cells;
        var px = new Color[w * sz];
        float c = sz / 2f;

        // Core variants: solid center, soft irregular rim (the lumpy blob that GROWS with the pool amount).
        for (int v = 0; v < BloodVariants; v++)
        {
            int ox = v * sz;
            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float dx = (x - c + 0.5f) / c;   // -1..1 (1 = cell half)
                    float dy = (y - c + 0.5f) / c;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float ang = MathF.Atan2(dy, dx);
                    float coreEdge = BloodCoreRadius * (1f + 0.16f * MathF.Sin(ang * 3f + v * 1.7f) + 0.10f * MathF.Sin(ang * 5f - v * 2.3f));
                    float a01;
                    // Solid out to 70% of the radius with a tight soft rim (outer 30%).  A mostly-solid core means a
                    // streak (drawn at full opacity) matches the core across most of its overlap instead of reading
                    // as a darker spoke punching through a wide faded rim — so streaks blend in.  Also reads bold.
                    if (dist >= coreEdge)
                    {
                        a01 = 0f;
                    }
                    else if (dist <= coreEdge * 0.70f)
                    {
                        a01 = 1f;
                    }
                    else
                    {
                        float r = (dist - coreEdge * 0.70f) / (coreEdge * 0.30f);
                        a01 = 1f - r * r * (3f - 2f * r);
                    }
                    byte a = (byte)(Math.Clamp(a01, 0f, 1f) * 255f);
                    px[y * w + ox + x] = new Color(a, a, a, a);   // premultiplied
                }
            }
        }

        // Droplet cell: a small soft round dot (solid inner half, soft rim), centered.  Stamped at a FIXED px
        // size for the detached satellite spatter.
        {
            int ox = BloodVariants * sz;
            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float dx = (x - c + 0.5f) / c, dy = (y - c + 0.5f) / c;
                    float d = MathF.Sqrt(dx * dx + dy * dy);           // 0 center .. 1 cell edge
                    float a01 = d >= 1f ? 0f : MathF.Min(1f, (1f - d) / 0.5f);   // solid inner half, soft rim
                    byte a = (byte)(Math.Clamp(a01, 0f, 1f) * 255f);
                    px[y * w + ox + x] = new Color(a, a, a, a);
                }
            }
        }

        _bloodTex = new Texture2D(gd, w, sz);
        _bloodTex.SetData(px);
    }

    // Draws the frame's blood-pool decals — a few stamps (core + streaks + droplets) per bloodied tile, all
    // from the single atlas (one texture bind) inside the existing world AlphaBlend batch, so they scroll with
    // the map and dim at night.  A stable per-tile hash picks the core variant, rotation, and scale jitter so a
    // spread multi-tile pool reads as one organic mass rather than a grid of circles.
    // Accumulate every blood pool into the offscreen bloodRT with MAX blend: overlapping blobs take the
    // higher influence (a smooth UNION), so adjacent pools merge into one contiguous mass with no additive
    // darkening at the seams.  Runs before the world batch because it swaps render targets; the world target
    // is re-bound + re-cleared afterward (nothing was drawn to it yet, so the re-clear is loss-free).
    // Two-layer world: accumulate only the pools on `layer` into `bloodRT`, so ground blood and fringe (bridge-
    // top) blood build separate fields that composite in their own passes (fringe over ground).  Skips the whole
    // render-target swap when this layer has no blood, so a flat map pays nothing for the second field.
    private bool AccumulateBloodField(SpriteBatch sb, RenderTarget2D bloodRT, Matrix transform, WorldLayer layer)
    {
        var blood = _renderFrame.Blood;
        bool any = false;
        for (int i = 0; i < blood.Count && !any; i++) if (blood[i].Layer == layer) any = true;
        if (!any) return false;

        EnsureBloodTextures(sb.GraphicsDevice);
        var gd = sb.GraphicsDevice;
        var saved = gd.GetRenderTargets();
        if (saved.Length == 0) return false;   // no world target bound (shouldn't happen) — skip rather than risk binding the backbuffer
        gd.SetRenderTarget(bloodRT);
        gd.Clear(Color.Transparent);
        sb.Begin(SpriteSortMode.Deferred, MirageGame.MaxLightBlend, SamplerState.LinearClamp, null, null, null, transform);
        for (int i = 0; i < blood.Count; i++) if (blood[i].Layer == layer) DrawBloodInfluence(sb, blood[i]);
        sb.End();
        gd.SetRenderTargets(saved);
        gd.Clear(Color.Black);   // the re-bind discarded the world target (nothing drawn to it yet); re-establish its clear
        return true;
    }

    // Stamps one tile's blood into the MAX-blend accumulation buffer.  Every blood tile gets a CORE POOL BLOB
    // (small at first, grows SLOWLY toward max under accumulation) plus DROPLETS (count ramps with the amount) for
    // a splatter look.  Streaks removed.  Opacity = freshness.  Seed drives a stable per-tile variant/rotation/
    // jitter so the look never shimmers frame-to-frame.
    private static float BloodHash(float n)
    {
        float s = MathF.Sin(n * 12.9898f) * 43758.5453f;
        return s - MathF.Floor(s);
    }
    private void DrawBloodInfluence(SpriteBatch sb, in BloodDrawCmd cmd)
    {
        var tex = _bloodTex!;
        int sz = BloodBlobSize;
        float sat = Math.Clamp(cmd.Amount / Constants.BloodSizeFullAmount, 0f, 1f);
        float influence = cmd.Freshness;
        uint h = (uint)cmd.Seed;
        int variant = (int)(h % BloodVariants);
        float rot = ((h >> 3) % 360) * (MathF.PI / 180f);
        float jitter = 0.85f + ((h >> 12) % 31) / 100f;   // 0.85..1.15
        var tint = Color.White * influence;               // MAX-accumulate the influence; the blood color is applied at composite
        // A large NPC (Size>1) leaves ONE decal scaled to its whole body, CENTERED on the footprint (Size*32 px)
        // instead of its top-left anchor tile - so its splat reads as one pool, not Size*Size discrete tile blobs.
        float scale = cmd.Size < 1 ? 1f : cmd.Size;
        var pos = new Vector2(cmd.ScreenX + scale * Constants.PicX / 2f, cmd.ScreenY + scale * Constants.PicY / 2f);
        var center = new Vector2(sz / 2f, sz / 2f);

        // Core pool blob — grows with the amount, shrinks back as it dries; scaled up for a large NPC's body.
        float coreSize = MathHelper.Lerp(Constants.BloodDecalMinSizePx, Constants.BloodDecalMaxSizePx, sat) * jitter * scale;
        sb.Draw(tex, pos, new Rectangle(variant * sz, 0, sz, sz), tint, rot, center, coreSize / sz, SpriteEffects.None, 0f);

        // Droplets — always.  FIXED px size AND fixed absolute distance (both hashed per droplet, NOT scaled by the
        // pool), so a droplet never slides as blood accumulates or dries.  The COUNT ramps with saturation, so new
        // droplets appear on top of the ones already placed; the newest fades in with `dPartial` (no pop).
        var dropSrc = new Rectangle(BloodVariants * sz, 0, sz, sz);
        // Droplets fade to NOTHING by the time the stain's freshness drops to BloodDropletFadeFloor — well before
        // the pool itself "dries" — so a fresh stain shows its spray and an aged one is just the pool left.  Remap
        // freshness from [floor..1] onto [0..1] so it hits a hard 0 at the floor (not the asymptotic speck a bare
        // power curve leaves), then the power shapes the fade within that window.
        float dropFade = Math.Clamp((influence - BloodDropletFadeFloor) / (1f - BloodDropletFadeFloor), 0f, 1f);
        var dropTint = Color.White * MathF.Pow(dropFade, BloodDropletFadePower);
        float dCountF = BloodSatelliteMax * sat;   // droplet count ramps in lockstep with the blob SIZE (shared `sat` = amount / BloodSizeFullAmount)
        int dWhole = (int)MathF.Floor(dCountF);
        float dPartial = dCountF - dWhole;
        for (int k = 0; k <= dWhole; k++)
        {
            float dMul = k < dWhole ? 1f : dPartial;
            if (dMul <= 0.01f) break;
            float baseN = variant * 9.7f + k * 7.31f;
            float phi = rot + BloodHash(baseN) * MathF.Tau;
            // Distance is a fraction of the blob size AT THIS DROPLET'S BIRTH — the size-saturation the pool had
            // when the count first reached index k.  With the droplet ramp matched to the size ramp, that's simply
            // k/Max, a function of k alone, so the position is CONSTANT: as the pool grows, higher-index droplets
            // appear further out at the bigger rim while every droplet already placed stays put (and the growing
            // pool eventually laps over the innermost).
            float birthSize = MathHelper.Lerp(Constants.BloodDecalMinSizePx, Constants.BloodDecalMaxSizePx, k / (float)BloodSatelliteMax) * jitter * scale;
            float dist = birthSize * (BloodSatelliteDistMinFrac + BloodHash(baseN + 2.1f) * (BloodSatelliteDistMaxFrac - BloodSatelliteDistMinFrac));
            float dropPx = (BloodSatelliteMinPx + BloodHash(baseN + 4.3f) * (BloodSatelliteMaxPx - BloodSatelliteMinPx)) * scale;  // 1..4 px, per-droplet (scaled up for a big NPC)
            var sp = pos + new Vector2(MathF.Cos(phi), MathF.Sin(phi)) * dist;
            sb.Draw(tex, sp, dropSrc, dropTint * dMul, 0f, center, dropPx / sz, SpriteEffects.None, 0f);
        }
    }

    // Composite the merged blood field over the ground as one tinted 1:1 quad.  The viewport-sized dest rect
    // maps under the world transform to the full supersampled target, matching bloodRT 1:1.  Premultiplied
    // blood tint * MaxAlpha, so the union reads as solid dark red (with a soft rim from the blob falloff).
    private void CompositeBloodField(SpriteBatch sb, RenderTarget2D bloodRT)
    {
        var rgb = Constants.BloodTintRgb;
        var tint = new Color((int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF)) * Constants.BloodMaxAlpha;
        sb.Draw(bloodRT, new Rectangle(0, 0, Camera.ViewW, Camera.ViewH), tint);
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

    /// <summary>The camera's world-pixel Y — the heat shader uses it to anchor its wave to the world so the
    /// shimmer doesn't appear to speed up while moving vertically.</summary>
    public float CameraWorldY => _camera.CameraY;

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

    /// <summary>VitalDelta path (slotted NPC or player) — build the target ref and defer-or-float.</summary>
    public void SpawnOrDeferVitalFloat(bool isNpc, int idx, int npcMap, int mapNum, int lx, int ly, float xoff, float yoff, string? text, Color color, float bloodIntensity)
        => DeferOrFloat(isNpc ? new TargetRef(TargetKind.Npc, idx, npcMap) : new TargetRef(TargetKind.Player, idx, 0),
            mapNum, lx, ly, xoff, yoff, text, color, bloodIntensity);

    /// <summary>Traversal-NPC path (positioned by world tile) — build the traversal ref and defer-or-float.</summary>
    public void SpawnOrDeferTraversalFloat(int spawnMap, int spawnSlot, int mapNum, int x, int y, string? text, Color color, float bloodIntensity)
        => DeferOrFloat(new TargetRef(TargetKind.Traversal, spawnMap, spawnSlot), mapNum, x, y, 0f, 0f, text, color, bloodIntensity);

    /// <summary>Float a vital number now, OR — when it belongs to an in-flight spell projectile — defer it
    /// until the bolt would land, so the number appears in sync with the visible impact. The world position
    /// is captured now, since the entity may die or despawn before release.</summary>
    private void DeferOrFloat(TargetRef target, int mapNum, int lx, int ly, float xoff, float yoff, string? text, Color color, float bloodIntensity)
    {
        long release = ClaimRelease(target);
        bool onScreen = TryEntityScreen(mapNum, lx, ly, xoff, yoff, out float sx, out float sy);
        int tsize = TargetFootprintSize(target);   // center the number/blood on an oversize NPC's body, not its anchor
        if (release > 0 && onScreen)
        {
            HoldBarFor(target, release); // hold the HP bar too, so it drops in sync with the bolt
            float cx = sx + tsize * Constants.PicX / 2f;
            float cy = sy - FloatTextGapAbove;
            // Defer BOTH the number and the blood burst to the bolt's arrival, so they land with the impact.
            _deferredFloats.Add(new DeferredFloat
            {
                WorldX = cx + _camera.CameraX, WorldY = cy + _camera.CameraY, Text = text, Color = color,
                ReleaseMs = release, BloodIntensity = bloodIntensity, Layer = LayerAtTile(mapNum, lx, ly),
            });
            return;
        }
        // Immediate (melee, or an unresolved/instant bolt): number now, blood burst now.
        if (text is not null) SpawnFloatingTextAtEntity(mapNum, lx, ly, xoff, yoff, text, color, tsize);
        if (bloodIntensity > 0f && _showBlood && onScreen)
            _particles.EmitBloodSplatter(sx + tsize * Constants.PicX / 2f + _camera.CameraX, sy - FloatTextGapAbove + _camera.CameraY, bloodIntensity, LayerAtTile(mapNum, lx, ly));
    }

    // Hold the target's HP bar (display) until the bolt lands, matching the deferred number. NPC + traversal
    // route through their ClientMapNpc; players are handled with the player-death work (server death signal).
    private void HoldBarFor(TargetRef target, long until)
    {
        switch (target.Kind)
        {
            case TargetKind.Npc:
                var npcs = _ctx.State.NpcsForMap(target.B);
                if (npcs is not null && target.A >= 1 && target.A <= Constants.MaxMapNpcs)
                    npcs[target.A].BarHoldUntilMs = Math.Max(npcs[target.A].BarHoldUntilMs, until);
                break;
            case TargetKind.Traversal:
                if (_ctx.State.TraversalNpcs.TryGetValue((target.A, target.B), out var tn))
                    tn.BarHoldUntilMs = Math.Max(tn.BarHoldUntilMs, until);
                break;
            case TargetKind.Player:
                if (target.A >= 1 && target.A <= Constants.MaxPlayers)
                    _ctx.State.Players[target.A].BarHoldUntilMs = Math.Max(_ctx.State.Players[target.A].BarHoldUntilMs, until);
                break;
        }
    }

    /// <summary>Delayed death: hold a killed entity's sprite in place until its killing spell bolt lands, so the
    /// body doesn't vanish before the visible projectile arrives. Works for NPCs, traversal guests, and other
    /// players. No-op for the LOCAL player (its own death must not lag) or a death with no in-flight hit.
    /// (Distinct from the combat-logoff "ghost" feature — different concept, different vocabulary.)</summary>
    public void OnEntityDied(EntityDeathFx fx)
    {
        if (fx.Target.Kind == TargetKind.Player && fx.Target.A == _ctx.State.MyIndex) return;
        long release = ClaimOrReuseRelease(fx.Target);
        if (release <= 0 || fx.SpriteRow < 0) return;
        if (!TryEntityScreen(fx.Map, fx.X, fx.Y, fx.XOff, fx.YOff, out float sx, out float sy)) return;
        _delayedDeaths.Add(new DelayedDeath
        {
            WorldX = sx + _camera.CameraX, WorldY = sy + _camera.CameraY, SpriteRow = fx.SpriteRow, Dir = fx.Dir, ReleaseMs = release, Size = fx.Size,
        });
    }

    private static bool SameTarget(TargetRef a, TargetRef b) => a.Kind == b.Kind && a.A == b.A && a.B == b.B;

    // Claim the EARLIEST unclaimed in-flight hit for this target (FIFO), returning its release time (0 if none).
    // Claiming marks it so N bolts on one target stagger their numbers/deaths across their distinct arrivals.
    private long ClaimRelease(TargetRef target)
    {
        long now = Environment.TickCount64;
        int best = -1;
        for (int i = 0; i < _pendingHits.Count; i++)
        {
            var h = _pendingHits[i];
            if (!h.Claimed && h.ReleaseMs > now && SameTarget(h.Target, target)
                && (best < 0 || h.ReleaseMs < _pendingHits[best].ReleaseMs))
            {
                best = i;
            }
        }
        if (best < 0) return 0;
        var claimed = _pendingHits[best];
        claimed.Claimed = true;
        _pendingHits[best] = claimed;
        _lastClaimTarget = target;
        _lastClaimRelease = claimed.ReleaseMs;
        _lastClaimTick = now;
        _lastClaimConsumed = false;
        return claimed.ReleaseMs;
    }

    // A death and its damage number are the SAME hit: the death reuses the number's just-claimed bolt (same
    // batch) rather than consuming a second one; otherwise it claims its own.
    private long ClaimOrReuseRelease(TargetRef target)
    {
        long now = Environment.TickCount64;
        if (!_lastClaimConsumed && _lastClaimRelease > now && SameTarget(_lastClaimTarget, target)
            && now - _lastClaimTick <= ClaimReuseWindowMs)
        {
            _lastClaimConsumed = true;
            return _lastClaimRelease;
        }
        return ClaimRelease(target);
    }

    // Release deferred hit numbers whose projectile has landed; expire stale pending hits. Called each frame.
    private void ReleaseDeferredHits()
    {
        long now = Environment.TickCount64;
        for (int i = _deferredFloats.Count - 1; i >= 0; i--)
        {
            if (now >= _deferredFloats[i].ReleaseMs)
            {
                var d = _deferredFloats[i];
                if (d.Text is not null) SpawnFloatingText(d.WorldX - _camera.CameraX, d.WorldY - _camera.CameraY, d.Text, d.Color);
                if (d.BloodIntensity > 0f && _showBlood) _particles.EmitBloodSplatter(d.WorldX, d.WorldY, d.BloodIntensity, d.Layer);
                _deferredFloats.RemoveAt(i);
            }
        }

        for (int i = _delayedDeaths.Count - 1; i >= 0; i--)
            if (now >= _delayedDeaths[i].ReleaseMs) _delayedDeaths.RemoveAt(i);
        for (int i = _pendingHits.Count - 1; i >= 0; i--)
            if (now >= _pendingHits[i].ReleaseMs + PendingHitGraceMs) _pendingHits.RemoveAt(i);
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
        sb.Draw(outerTex, outerDest, ScaleGlow(core * LightModel.OuterDimFactor, lit));

        // Inner core — brightness animates both ways per FlickerStyle; size only oscillates up from the base
        // (floored at MinInnerSizeFactor) so the core never shrinks small.
        float f = LightModel.FlickerFor(cmd.Flicker, totalSec, cmd.Id);
        float innerR = cmd.Radius * LightModel.InnerRadiusFactor;
        float sizeF = MathF.Max(f, LightModel.MinInnerSizeFactor);
        int innerSize = (int)(innerR * 2f * sizeF);
        var innerDest = new Rectangle(
            (int)(cx - innerSize / 2f), (int)(cy - innerSize / 2f), innerSize, innerSize);
        sb.Draw(innerTex, innerDest, ScaleGlow(core, lit * f));
    }

    // Scales a peak light color by an intensity factor, folding brightness into the RGB channels
    // (max-blended into the light map; the blend ignores source alpha for color).
    private static Color ScaleGlow(Vector3 peak, float k) => new(
        (byte)Math.Clamp(peak.X * k, 0f, 255f),
        (byte)Math.Clamp(peak.Y * k, 0f, 255f),
        (byte)Math.Clamp(peak.Z * k, 0f, 255f),
        (byte)255);

    /// <summary>
    /// Draws the UI (background, sidebar, HUD, chat, panels) into the main reference target, inside the
    /// batch MirageGame already opened.  The scrolling world is drawn separately by
    /// <see cref="DrawWorld"/> into its own target and composited over the (black) map area afterward.
    /// </summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        long nowMs = Environment.TickCount64;
        // Black background for the UI region ONLY — leave the map viewport (0,0,ViewW,ViewH) TRANSPARENT
        // so the world composite shows through underneath it, with panels drawn on top (these two rects
        // are the whole reference frame minus the map area).
        UiHelper.DrawFilledRect(sb, new Rectangle(0, Camera.ViewH, UiHelper.RefW, UiHelper.RefH - Camera.ViewH), Color.Black);
        UiHelper.DrawFilledRect(sb, new Rectangle(Camera.ViewW, 0, UiHelper.RefW - Camera.ViewW, Camera.ViewH), Color.Black);

        // Sidebar background (right column, separated from the map viewport area).
        UiHelper.DrawFilledRect(sb, new Rectangle(Camera.ViewW, 0, UiHelper.RefW - Camera.ViewW, UiHelper.RefH), UiHelper.BarBg);

        // Find the topmost open panel under the mouse BEFORE any UI draws. Widgets below it
        // (HUD buttons, vital bars, sidebar links, chat hyperlinks, lower-z panels) must not
        // hover-highlight or request a cursor — the mouse is visually over the panel, not them.
        // The topmost-under-mouse panel resets hover around its own Draw so its widgets work.
        int topUnderMouse = -1;
        var mpos = _lastInput.MousePosition;
        for (int zi = _zOrder.Count - 1; zi >= 0; zi--)
        {
            int idx = _zOrder[zi];
            if (PanelIsOpen(idx) && PanelContainsMouse(idx, mpos))
            {
                topUnderMouse = idx;
                break;
            }
        }
        bool mouseOverPanel = topUnderMouse >= 0;
        if (mouseOverPanel) _lastInput.ConsumeMouseHover();

        _hud.Draw(sb, font, _ctx.TitleFont ?? font, _ctx.State, _lastInput);
        _partyOverlay.Draw(sb, font, _ctx.State, _lastInput, _tabTarget, nowMs);
        _contestHud.Draw(sb, font, _ctx.State);
        _chat.Draw(sb, font, nowMs);

        // Sidebar [Options (O)] / [Help (H)] links — drawn BEFORE the panel z-order so any
        // open panel (or tooltip) overlapping the bottom-right link strip renders on top of them,
        // matching the user expectation that floating panels always win over background UI.
        // Mail is drawn first (leftmost) and tinted gold while there is unread mail, so the inbox
        // announces itself without a separate badge; Options/Help keep their default gray idle color.
        HudPanel.MailLink.IdleColor = _ctx.State.UnreadMailCount() > 0 ? Color.Gold : Color.Gray;
        HudPanel.MailLink.Draw(sb, font, _lastInput);
        HudPanel.OptionsLinkInGame.Draw(sb, font, _lastInput);
        HudPanel.HelpLink.Draw(sb, font, _lastInput);

        // The single tooltip is fed by panel Draws; only the topmost open panel under the mouse
        // may notify it this frame, so a hovered row in a panel hidden behind another panel
        // doesn't leak its tooltip through the occluding window above it.
        foreach (int idx in _zOrder)
        {
            if (idx == topUnderMouse) _lastInput.ResetMouseHover();
            DrawPanel(idx, sb, font, nowMs, idx == _activePanel, idx == topUnderMouse);
            if (idx == topUnderMouse) _lastInput.ConsumeMouseHover();
        }

        // Tooltip floats above every panel — panels call Tooltip.NotifyHover* during their draws
        // and this single global tick decides whether to render, where (pinned), and when to hide.
        Tooltip.TickAndDraw(sb, font, nowMs, _lastInput.MousePosition);

        // Chat tab options panel draws above the chat panel but below the context menu.
        _chatOptions.Draw(sb, font, _lastInput);

        // Context menu draws LAST so it overlays every panel, the tooltip, and the world.
        _contextMenu.Draw(sb, font);

        // Death overlay is the true top layer while the local player is dead (a full-screen modal).
        _death.Draw(sb, font, _lastInput, _ctx.State);
    }

    /// <summary>Grid cell (col,row) currently holding <paramref name="mapNum"/>, or null if off-grid.</summary>
    private (int col, int row)? GridCellForMap(int mapNum)
    {
        if (mapNum <= 0) return null;
        var nums = _ctx.State.NeighborMapNums;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                if (nums[c, r] == mapNum) return (c, r);
        }

        return null;
    }

    /// <summary>
    /// Top-left screen pixel of an entity given its map + local tile + sub-tile offset, via the
    /// camera.  Returns false when that map isn't part of the current 3×3 grid.  Shared by hover
    /// hit-testing and floating combat text so both track the scrolling camera.
    /// </summary>
    public bool TryEntityScreen(int mapNum, int localX, int localY, float xOff, float yOff, out float sx, out float sy)
    {
        sx = sy = 0f;
        var cell = GridCellForMap(mapNum);
        if (cell is null) return false;
        (sx, sy) = _camera.WorldTileToScreen(
            cell.Value.col * WorldCoordHelper.MapTilesX + localX,
            cell.Value.row * WorldCoordHelper.MapTilesY + localY, xOff, yOff);
        return true;
    }

    private TargetRef ComputeHoveredEntity()
    {
        var mp = _lastInput.MousePosition;
        if (mp.X < 0 || mp.X >= Camera.ViewW || mp.Y < 0 || mp.Y >= Camera.ViewH) return default;
        for (int zi = 0; zi < _zOrder.Count; zi++)
            if (PanelIsOpen(_zOrder[zi]) && PanelContainsMouse(_zOrder[zi], mp)) return default;
        // Hover spans the full 3x3 region: the client doesn't distinguish "your map" from "next
        // map over" for bars/tooltips. Reuse the click-targeting hit-test so a mouseover on a
        // neighbor-map NPC (or a chasing traversal guest) gets the same identity a click would.
        return FindEntityAtPixel(mp.X + _camera.CameraX, mp.Y + _camera.CameraY);
    }
}
