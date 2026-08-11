using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Traversal-guest NPC-versus-NPC stepping, plus the direction helper and neighbor
/// tables the pathing flood is built on.</summary>
public sealed partial class NpcAiSystem : GameSystem
{
    private void RunGuestNpcVsNpcStep(int mapNum, int listIndex, TraversalNpcRecord t, long now)
    {
        var npc = _world.Npcs[t.Num];
        var resolved = _combat.ResolveNpcByIdentity(t.NpcTargetSpawnMap, t.NpcTargetSpawnSlot);
        if (resolved is null)
        {
            // Victim died/despawned — drop target and revert to idle scan/wander.  Combat keeps
            // ticking; the expire gate in RunTraversalAi sends the guest home when time runs out.
            t.NpcTargetSpawnMap = 0;
            t.NpcTargetSpawnSlot = 0;
            BroadcastTraversalState(t);
            RunGuestIdle(mapNum, listIndex, t, now);
            return;
        }
        var (victimMap, victimSlot, victimMn) = resolved.Value;

        if (WorldCoordHelper.GridPosition(_world.Maps, mapNum, victimMap) is null)
        {
            // Victim slipped out of observable area — same drop-and-idle as victim-gone.
            t.NpcTargetSpawnMap = 0;
            t.NpcTargetSpawnSlot = 0;
            BroadcastTraversalState(t);
            RunGuestIdle(mapNum, listIndex, t, now);
            return;
        }

        // AoS guest unreachable give-up — same 10s rule as the player-target guest path.  A guest
        // give-up unifies into ReturnTraversalHome (drop targets + relocate to spawn + vitals
        // refill); we never linger as an idle guest after the AoS chase fails.
        if (ShouldGiveUpUnreachableAosTarget(t, now))
        {
            ReturnTraversalHome(mapNum, listIndex, t);
            return;
        }

        // Cast decision first (Int>0 guests) — mirrors RunNpcVsNpcStep's magic→melee order. Consumes the tick
        // on a cast/kite/hold; a non-caster or out-of-range guest falls through to the melee + chase below.
        if (TryNpcMagicActionVsNpc(mapNum, listIndex, t, victimMap, victimSlot, victimMn, now))
        {
            if (!t.WantsKite) t.NextMoveMs = now + Constants.AiTickIntervalMs;
            return;
        }

        if (_combat.CanNpcAttackNpc(mapNum, t, victimMap, victimMn))
        {
            // Turn to face BEFORE the swing (so the client applies the new Dir before the swoosh spawns) — the
            // legs pass does this on arrival; brain fallback here, never mid-slide, no deliberate beat.
            var faceDir = FaceTargetDir(mapNum, t.X, t.Y, victimMap, victimMn.X, victimMn.Y, t.Dir);
            if (t.Dir != faceDir)
            {
                if (now < t.NextMoveMs) return;                   // still sliding into place — finish the move first
                FaceNpcToward(mapNum, 0, t, victimMap, victimMn.X, victimMn.Y);
                return;
            }
            _combat.NpcAttackNpc(mapNum, 0, t, victimMap, victimSlot, victimMn);
            t.AttackTimer = now;
            BroadcastTraversalState(t);
            return;
        }

        // Refresh combat each chase step for relentless behaviors (AoS, Guard) — AWA guests stay
        // yield-able so the combat-expire gate at the top of RunTraversalAi can eventually fire.
        if (npc.Behavior is NpcBehavior.AttackOnSight or NpcBehavior.Guard)
            _combat.MarkNpcCombat(t, now);
        // On an OBSERVED map the fast legs pass (AdvanceGuestChaseStep) runs the WHOLE chase-STEP — same-map
        // AND cross-seam — at run/walk pace.  The legs skip UNWATCHED maps (RunMovement bails on observers==0)
        // and guests (unlike natives) are ticked everywhere, so on an unwatched map the brain still steps here
        // (both a same-map victim and a seam cross), keeping a guest closing on an NPC while nobody watches.
        if (_world.MapObservers[mapNum].Count == 0)
            StepGuestTowardObservableArea(mapNum, listIndex, t, victimMap, victimMn.X, victimMn.Y, victimMn.Layer);
    }

    private static Direction DirectionToward(MapPos from, MapPos to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx > 0 ? Direction.Right : Direction.Left;
        return dy > 0 ? Direction.Down : Direction.Up;
    }

    // Direction deltas for BFS neighbor expansion (Up/Down/Left/Right) and the corresponding
    // direction the NPC steps to go FROM that neighbor back to the current tile.  Static so they
    // never allocate per call — the BFS hot path runs once per chasing NPC per tick.
    private static readonly int[] _bfsDx = { 0, 0, -1, 1 };
    private static readonly int[] _bfsDy = { -1, 1, 0, 0 };
    private static readonly Direction[] _bfsStepFromNeighbor = { Direction.Down, Direction.Up, Direction.Right, Direction.Left };

    // BFS for the shortest walkable path from (fromX,fromY) on `mapNum` to (toX,toY) on `targetMap`,
    // across the WHOLE 3×3 observable area (48×36 world tiles).  Returns the first step direction
    // the NPC should take — the actual move for this tick — letting it route around walls, U-shapes,
    // and any solid map geometry AND follow linked borders into neighbor maps when that's shorter.
    // Cell crossings run the same entry gate as the live cross step, so a planned route can never reach
    // somewhere the NPC could not actually step.  Tiles currently held by other players
    // and NPCs are pinned as walls via a one-shot occupancy snapshot — so the planned route matches
    // what the NPC can actually step to THIS tick, and the plan stays consistent across ticks while
    // a blocker persists (no oscillation against a stationary mob standing between NPC and target).
    // Source and target tiles are special-cased: source is the BFS termination so its own occupancy
    // is never checked; target is the BFS root so the same is true.  Returns null when no walkable
    // path connects source and target in the observable area.
    //
    // Two-layer world: the BFS state is (cell, WorldLayer), 2*N states.  It roots at the target's layer
    // (targetLayer) and terminates on the chaser's layer (fromLayer); a ramp is the only place a step
    // crosses between layers (LayerLogic.CanEnter decides, matching live movement).  So an NPC under a
    // bridge routes to a ramp to reach a target on top, and one that can't reach the target's layer at all
    // returns null (the acquire gate then skips it / the give-up timer drops the lock).
}
