using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;

namespace Mirage.Server.Tests;

/// <summary>Server integration coverage for variable-size NPCs: footprint occupancy (within-map and across a
/// seam) and the large-NPC melee strip (one swing strikes every player on the leading edge), plus the size-1
/// regression guards (single-tile occupancy, omnidirectional adjacency).</summary>
[TestFixture]
public class NpcFootprintCombatTests
{
    const int Map = 1;
    const int StartHp = 9999;
    // These pin REACH, not cadence. Every NPC here starts with AttackTimer at 0, so any live clock reading
    // clears the cooldown and the gate answers the question the test is asking.
    static long Now => Environment.TickCount64;

    // ── Footprint occupancy ───────────────────────────────────────────────────
    [Test]
    public void Size2Npc_OccupiesItsWholeFootprint()
    {
        var world = new GameWorld();
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[Map, 1];
        n.Num = 1;
        n.X = 5;
        n.Y = 5;

        foreach (var (x, y) in new[] { (5, 5), (6, 5), (5, 6), (6, 6) })
            Assert.That(world.IsTileOccupiedByNpc(Map, x, y), Is.True, $"footprint tile ({x},{y})");
        foreach (var (x, y) in new[] { (4, 5), (7, 5), (5, 4), (5, 7) })
            Assert.That(world.IsTileOccupiedByNpc(Map, x, y), Is.False, $"tile just outside ({x},{y})");
    }

    [Test]
    public void Size1Npc_OccupiesOnlyItsTile()
    {
        var world = new GameWorld();
        world.Npcs[1].Size = 1;
        var n = world.MapNpcs[Map, 1];
        n.Num = 1;
        n.X = 5;
        n.Y = 5;
        Assert.That(world.IsTileOccupiedByNpc(Map, 5, 5), Is.True);
        Assert.That(world.IsTileOccupiedByNpc(Map, 6, 5), Is.False);
        Assert.That(world.IsTileOccupiedByNpc(Map, 5, 6), Is.False);
    }

    [Test]
    public void Size2Npc_AtRightEdge_SpillsOntoRightNeighbor()
    {
        var world = new GameWorld();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = Constants.MaxMapX;
        n.Y = 5;  // footprint {15,16}x{5,6}

        // On the home map the on-map column (15) is occupied.
        Assert.That(world.IsTileOccupiedByNpc(1, Constants.MaxMapX, 5), Is.True);
        // The spilled column resolves onto the right neighbor's local col 0, rows 5 and 6.
        Assert.That(world.IsTileOccupiedByNpc(2, 0, 5), Is.True, "spilled body blocks on the neighbor map");
        Assert.That(world.IsTileOccupiedByNpc(2, 0, 6), Is.True);
        Assert.That(world.IsTileOccupiedByNpc(2, 0, 4), Is.False, "outside the spilled rows");
        Assert.That(world.IsTileOccupiedByNpc(2, 1, 5), Is.False, "the body only spills one column");
    }

    [Test]
    public void Size2Guest_OccupiesItsWholeFootprint()
    {
        // A chasing NPC that crossed a seam lives in MapTraversalNpcs; its footprint must block just like a native's.
        var world = new GameWorld();
        world.Npcs[1].Size = 2;
        world.MapTraversalNpcs[Map].Add(new TraversalNpcRecord
        {
            Num = 1, CurrentMapNum = Map, X = 5, Y = 5, SpawnMapNum = Map, SpawnSlot = 1,
        });
        foreach (var (x, y) in new[] { (5, 5), (6, 5), (5, 6), (6, 6) })
            Assert.That(world.IsTileOccupiedByNpc(Map, x, y), Is.True, $"guest footprint tile ({x},{y})");
        Assert.That(world.IsTileOccupiedByNpc(Map, 7, 5), Is.False, "just outside the guest footprint");
    }

    // ── Large-NPC melee strip ─────────────────────────────────────────────────
    [Test]
    public void Size3Npc_FacingRight_CanAttackAnyTileOnLeadingEdge()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 5);
        n.Dir = Direction.Right;  // footprint {5,6,7}; right edge = col 8, rows {5,6,7}

        RegisterPlayer(world, pm, 10, 8, 5);
        RegisterPlayer(world, pm, 11, 8, 6);
        RegisterPlayer(world, pm, 12, 8, 7);
        RegisterPlayer(world, pm, 13, 8, 8);    // one row past the strip
        RegisterPlayer(world, pm, 14, 10, 5);   // two tiles from the footprint

        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True, "strike tile (8,5)");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 11, Environment.TickCount64), Is.True, "strike tile (8,6)");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 12, Environment.TickCount64), Is.True, "strike tile (8,7)");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 13, Environment.TickCount64), Is.False, "off the leading-edge strip");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 14, Environment.TickCount64), Is.False, "two tiles from the footprint");
    }

    [Test]
    public void Size3Npc_OneSwing_StrikesEveryPlayerOnLeadingEdge()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 60;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 5);
        n.Dir = Direction.Right;
        n.AttackTimer = 0;

        var onStrip = new[]
        {
            RegisterPlayer(world, pm, 10, 8, 5),
            RegisterPlayer(world, pm, 11, 8, 6),
            RegisterPlayer(world, pm, 12, 8, 7),
        };
        var offStrip = RegisterPlayer(world, pm, 13, 8, 8);   // one row past the strip

        combat.NpcAttackPlayer(Map, 1, 10, Environment.TickCount64);   // primary target = the player at (8,5)

        foreach (var pc in onStrip)
            Assert.That(pc.Hp, Is.LessThan(StartHp), "every player on the leading edge takes the one swing");
        Assert.That(offStrip.Hp, Is.EqualTo(StartHp), "a player off the strip is untouched by the same swing");
    }

    // The whole body is the NPC, so every tile touching it is in reach — including the ones beyond a corner
    // column or row, where the direction read off the ANCHOR points along the wrong axis entirely. A player
    // standing above a size-3 body's right-hand column is one tile from three tons of monster; the anchor sits
    // two columns to its left, so an anchor-derived facing calls that "to the right" and swings at empty air.
    [TestCase(7, 4, TestName = "Footprint_AboveTheRightColumn")]
    [TestCase(5, 4, TestName = "Footprint_AboveTheLeftColumn")]
    [TestCase(4, 7, TestName = "Footprint_LeftOfTheBottomRow")]
    [TestCase(4, 5, TestName = "Footprint_LeftOfTheTopRow")]
    [TestCase(8, 7, TestName = "Footprint_RightOfTheBottomRow")]
    [TestCase(7, 8, TestName = "Footprint_BelowTheRightColumn")]
    public void Size3Npc_ReachesEveryTileTouchingItsBody(int px, int py)
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        PlaceNpc(world, 1, 1, 5, 5);   // body {5,6,7} x {5,6,7}
        RegisterPlayer(world, pm, 10, px, py);

        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Now), Is.True,
            $"({px},{py}) touches the body, so it is in reach");
    }

    [TestCase(8, 8, TestName = "Footprint_DiagonalPastTheCorner")]
    [TestCase(4, 4, TestName = "Footprint_DiagonalPastTheOtherCorner")]
    [TestCase(9, 6, TestName = "Footprint_TwoColumnsOut")]
    public void Size3Npc_DoesNotReachPastItsBody(int px, int py)
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        PlaceNpc(world, 1, 1, 5, 5);
        RegisterPlayer(world, pm, 10, px, py);

        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Now), Is.False,
            $"({px},{py}) only touches the body at a corner or not at all");
    }

    [Test]
    public void Size1Npc_MeleeStaysOmnidirectional()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 1;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 5);
        n.Dir = Direction.Right;  // faces right...
        RegisterPlayer(world, pm, 10, 5, 4);                            // ...but the player is directly ABOVE
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True,
            "a size-1 NPC still melees any Manhattan-1 neighbor regardless of facing");
    }

    // ── NPC vs NPC reach (both sides can be oversize) ─────────────────────────
    // Unlike the player gate, neither side here is guaranteed size 1. Two size-3 bodies whose edges touch sit
    // three tiles apart anchor to anchor, so an anchor-distance gate refuses the swing while the pathing keeps
    // trying to close — which is what made them shuffle around each other instead of fighting.

    [Test]
    public void Size3Npcs_TouchingEdges_CanAttackEachOther()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[2].Size = 3;
        var a = PlaceNpc(world, 1, 1, 5, 5);   // x 5..7, y 5..7
        var b = PlaceNpc(world, 2, 2, 8, 5);   // x 8..10, y 5..7 — its left edge against a's right edge

        Assert.Multiple(() =>
        {
            Assert.That(WorldCoordHelper.WorldManhattan(5, 5, 8, 5), Is.EqualTo(3),
                "a touching pair is 3 apart by anchor — the distance the old gate measured");
            Assert.That(combat.CanNpcAttackNpc(Map, a, Map, b, Now), Is.True, "touching edges are in reach");
            Assert.That(combat.CanNpcAttackNpc(Map, b, Map, a, Now), Is.True, "and reach reads the same from either body");
        });
    }

    [Test]
    public void Size3Npcs_WithAClearTileBetween_StillCannotAttack()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[2].Size = 3;
        var a = PlaceNpc(world, 1, 1, 5, 5);   // x 5..7
        var b = PlaceNpc(world, 2, 2, 9, 5);   // x 9..11 — column 8 is empty between them

        Assert.That(combat.CanNpcAttackNpc(Map, a, Map, b, Now), Is.False, "one clear tile of gap is out of reach");
    }

    [Test]
    public void Size3Npcs_MeetingOnlyAtACorner_CannotAttack()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[2].Size = 3;
        var a = PlaceNpc(world, 1, 1, 5, 5);   // x 5..7, y 5..7
        var b = PlaceNpc(world, 2, 2, 8, 8);   // x 8..10, y 8..10 — corner to corner only

        Assert.That(combat.CanNpcAttackNpc(Map, a, Map, b, Now), Is.False, "melee is cardinal — a diagonal corner is not reach");
    }

    [Test]
    public void Size1Npcs_ReachIsUnchanged()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 1;
        world.Npcs[2].Size = 1;
        world.Npcs[3].Size = 1;
        var a = PlaceNpc(world, 1, 1, 5, 5);
        var neighbor = PlaceNpc(world, 2, 2, 5, 6);
        var twoAway = PlaceNpc(world, 3, 3, 5, 7);

        Assert.Multiple(() =>
        {
            Assert.That(combat.CanNpcAttackNpc(Map, a, Map, neighbor, Now), Is.True, "the classic one-tile neighbor");
            Assert.That(combat.CanNpcAttackNpc(Map, a, Map, twoAway, Now), Is.False, "two tiles is still out of reach");
        });
    }

    // ── Cross-seam movement (the straddle enabler) ────────────────────────────
    [Test]
    public void Size2Npc_CanStepTowardSeam_LeadingEdgeValidatedOnNeighbor()
    {
        var (world, movement) = NewMovement();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = 14;
        n.Y = 5;  // footprint {14,15} on map 1
        Assert.That(movement.CanNpcMoveFrom(1, n, Direction.Right), Is.True,
            "the leading edge (col 16) resolves onto the right neighbor and is clear, so the body may straddle the seam");
    }

    [Test]
    public void Size2Npc_BlockedFromSeam_WhenNeighborLandingTileOccupied()
    {
        var (world, movement) = NewMovement();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = 14;
        n.Y = 5;
        // A blocker on the neighbor tile the leading edge would enter (map 2, local (0,5)).
        world.Npcs[2].Size = 1;
        var blocker = world.MapNpcs[2, 1];
        blocker.Num = 2;
        blocker.X = 0;
        blocker.Y = 5;
        Assert.That(movement.CanNpcMoveFrom(1, n, Direction.Right), Is.False,
            "a blocker on the neighbor-side leading-edge tile stops the straddle step");
    }

    [Test]
    public void Size2Npc_WithinMapStep_NeverWalksAnchorOffMap()
    {
        // The wander bug: a big NPC steps via CanNpcMove (no StepLeavesMap), so a within-map step must refuse
        // to push the anchor off the edge - otherwise its footprint lands on no on-map tile and stops blocking.
        var (world, movement) = NewMovement();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = Constants.MaxMapX;
        n.Y = 5;  // anchor at the right edge (footprint straddling)
        Assert.That(movement.CanNpcMoveFrom(1, n, Direction.Right), Is.False,
            "a within-map step must not move the anchor off-map; a genuine cross is handled by StepLeavesMap/TryNativeStep");
    }

    // ── Harness (per-file convention: local combat build + no-op dispatcher) ───
    // Two-layer world: an NPC can't melee another NPC across layers — only where a ramp connects them ("layer 1.5").
    [Test]
    public void CanNpcAttackNpc_GatedByLayer_ExceptAtARampFoot()
    {
        var (combat, world, _) = NewCombat();
        var attacker = PlaceNpc(world, 1, 1, 5, 6);   // ground foot
        var victim = PlaceNpc(world, 2, 1, 5, 5);     // one tile up (orthogonally adjacent)

        Assert.Multiple(() =>
        {
            // Same layer, adjacent → connects.
            attacker.Layer = WorldLayer.Ground;
            victim.Layer = WorldLayer.Ground;
            Assert.That(combat.CanNpcAttackNpc(Map, attacker, Map, victim, Now), Is.True, "same-layer neighbors connect");

            // Victim on the fringe over a plain tile → no reach from the ground.
            victim.Layer = WorldLayer.Fringe;
            Assert.That(combat.CanNpcAttackNpc(Map, attacker, Map, victim, Now), Is.False, "no cross-layer melee on a plain tile");

            // A ramp on the victim's tile (mounts from below) → the ground attacker at its foot connects.
            world.Maps[Map].EditTile(5, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });
            Assert.That(combat.CanNpcAttackNpc(Map, attacker, Map, victim, Now), Is.True, "reaches a victim on the adjacent ramp");
        });
    }

    // The reported asymmetry: an NPC on the ground can't melee a player up on the fringe (or vice-versa) unless the
    // fringe endpoint is on a ramp — CanNpcAttackPlayer must gate on layer exactly like the player's own melee.
    [Test]
    public void CanNpcAttackPlayer_GatedByLayer_ExceptAtARampFoot()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 1;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 6);            // NPC on the ground foot
        var pc = RegisterPlayer(world, pm, 10, 5, 5);   // player one tile up, adjacent

        Assert.Multiple(() =>
        {
            // Same layer, adjacent → the NPC can swing.
            n.Layer = WorldLayer.Ground;
            pc.Layer = WorldLayer.Ground;
            Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True, "same-layer neighbors connect");

            // Player up on the fringe over a plain tile → the NPC beneath can't reach it.
            pc.Layer = WorldLayer.Fringe;
            Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.False, "no cross-layer melee on a plain tile");

            // A ramp on the player's tile → the ground NPC at its foot connects.
            world.Maps[Map].EditTile(5, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });
            Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True, "reaches a player on the adjacent ramp");
        });
    }

    static (CombatSystem combat, GameWorld world, PlayerManager pm) NewCombat()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        return (combat, world, pm);
    }

    static (GameWorld world, MovementSystem movement) NewMovement()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return (world, movement);
    }

    static MapNpcRecord PlaceNpc(GameWorld world, int slot, int num, int x, int y)
    {
        var mn = world.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
        mn.Hp = 100;
        return mn;
    }

    static PlayerRecord RegisterPlayer(GameWorld world, PlayerManager pm, int index, int x, int y)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = x;
        pc.Y = y;
        pc.Level = 1;
        pc.MaxHp = StartHp;
        pc.Hp = StartHp;
        pc.Sp = 0;   // no SP → no block/dodge, so a landed swing always deals damage (deterministic)
        pc.Access = AdminLevel.Player;
        world.MapObservers[Map].Add(index);
        return pc;
    }

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
