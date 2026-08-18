using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>Attacks, casts, damage numbers, deaths and target assignment, plus the world events
/// (weather, time of day, map keys) that arrive alongside them.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── Combat ────────────────────────────────────────────────────────────────

    /// <summary>Fired on a melee attack packet: (mapNum, tileX, tileY, xOffset, yOffset, dir, sparks) of the
    /// attacker, so the shell can spawn a swing FX over the target tile in the facing direction. sparks is
    /// true when the swing connected (crescent + sparks) and false on a whiff (crescent only).</summary>
    public event Action<int, int, int, float, float, Direction, bool>? MeleeSwing;

    /// <summary>Fired on a cast packet — the shell spawns the typed projectile FX (see <see cref="SpellCastFx"/>).</summary>
    public event Action<SpellCastFx>? SpellCast;

    /// <summary>Fired just before a killed entity vanishes — the shell can hold its sprite until a killing bolt lands.</summary>
    public event Action<EntityDeathFx>? EntityDied;

    private void HandlePlayerAttack(PlayerAttackPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        player.Attacking = true;
        player.AttackTimer = Environment.TickCount64;
        if (p.InCombat) player.LastCombatMs = Environment.TickCount64;
        // Sparks fire only when the swing connected. InCombat is set exactly when the attacker engaged a
        // target (player or NPC); an empty swing (whiff) arrives with InCombat=false → crescent, no sparks.
        MeleeSwing?.Invoke(player.Map, player.X, player.Y, player.XOffset, player.YOffset, player.Dir, p.InCombat);
    }

    private void HandlePlayerCast(PlayerCastPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        player.Attacking = true;
        player.AttackTimer = Environment.TickCount64;
        // Heals on a peaceful target don't flip combat status — server gates this with InCombat.
        if (p.InCombat) player.LastCombatMs = Environment.TickCount64;
        SpellCast?.Invoke(new SpellCastFx(player.Map, player.X, player.Y, player.XOffset, player.YOffset, 1,
            SpellTypeFor(p.SpellNum), TargetRefFrom(p.TargetType, p.Target, p.TargetMap, p.SpawnMap, p.SpawnSlot)));
    }

    private void HandlePlayerDeath(PlayerDeathPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        if (player.Sprite < 0) return;
        // Hand the shell the death render state so it can hold a delayed-death sprite until a killing bolt lands
        // (the shell excludes the LOCAL player, so your own death never lags).
        EntityDied?.Invoke(new EntityDeathFx(new TargetRef(TargetKind.Player, p.Index, 0),
            player.Sprite, p.MapNum, p.X, p.Y, 0f, 0f, p.Dir));
    }

    private void HandleSetTarget(SetTargetPacket p)
        => TargetAssigned?.Invoke(TargetRefFrom(p.TargetType, p.Target, p.TargetMap, p.SpawnMap, p.SpawnSlot));

    // Translates the server's TargetType convention (0=player, 1=npc, 2=self, 3=traversal) into the client's
    // TargetRef shape. Self collapses to a Player ref on the local index, matching the click-own-tile path in
    // HandleSearch. Shared by target assignment and the cast-FX projectile homing.
    private TargetRef TargetRefFrom(byte targetType, int target, int targetMap, int spawnMap, int spawnSlot)
        => targetType switch
        {
            0 => new TargetRef(TargetKind.Player, target, 0),
            1 => new TargetRef(TargetKind.Npc, target, targetMap),
            2 => new TargetRef(TargetKind.Player, _state.MyIndex, 0),
            3 => new TargetRef(TargetKind.Traversal, spawnMap, spawnSlot),
            _ => default,
        };

    // Cached spell type for a spell number (client has all spell defs); falls back to SubHp for a stray num.
    private SpellType SpellTypeFor(int spellNum)
        => spellNum >= 1 && spellNum < _state.SpellDefs.Length && _state.SpellDefs[spellNum] is { } rec
            ? rec.Type : SpellType.SubHp;

    // Server rejected a client-proposed target (entity gone, slot mismatch, not observable).
    // Drop the local guess so the arrow disappears and the tab/click target state matches the
    // server's authoritative "no target" state.
    private void HandleClearTarget() => TargetAssigned?.Invoke(default);

    // ── World events ──────────────────────────────────────────────────────────

    private void HandleWeather(WeatherPacket p) => _state.Weather = p.Weather;

    private void HandleTimeOfDay(TimeOfDayPacket p)
    {
        _state.TimePhase = p.Phase;
        _state.TimeProgress = p.Progress;
        _state.TimePhaseReceivedMs = Environment.TickCount64;
    }

    private void HandleMapKey(MapKeyPacket p)
    {
        if (p.X > Constants.MaxMapX || p.Y > Constants.MaxMapY) return;
        var doors = _state.TempTilesForMap(p.MapNum);
        if (doors is null) return;
        doors[p.X, p.Y, (int)p.Layer] = p.Open;   // per-layer: a fringe-deck door is tracked apart from the ground one
    }
}
