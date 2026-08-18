using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

public sealed class MovementSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ShopSystem _shop;
    private readonly BloodSystem _blood;

    public MovementSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ShopSystem shop, BloodSystem blood,
                          IClock? clock = null)
        : base(dispatcher, clock: clock)
    {
        _world = world;
        _pm = pm;
        _shop = shop;
        _blood = blood;
    }

    // ── The pace gate ─────────────────────────────────────────────────────────

    /// <summary>How much banked movement a player may hold, in milliseconds. A step costs its own
    /// ms-per-tile and the budget accrues in real time, so the SUSTAINED rate any client can reach is
    /// exactly the pace <see cref="MovementFormulas"/> intends. This window is the slack on top of it: a
    /// network stall that bunches a second's worth of steps into one arrival is paid out of the bank
    /// rather than refused, which is what keeps an honest player off the rubber band.</summary>
    public const long MoveCreditWindowMs = 1500;

    /// <summary>Charges one step against <paramref name="sp"/>'s movement budget. False means the step
    /// arrived too early to pay for, and the caller must refuse it.
    ///
    /// <para>The budget is a single deadline (<see cref="ServerPlayer.MoveAllowedAt"/>) rather than a
    /// counter: it may sit up to <see cref="MoveCreditWindowMs"/> BEHIND <paramref name="now"/>, and that
    /// gap is the bank. Clamping it forward on every call is what refills it, which is also why an idle
    /// player is restored to exactly one window and never more.</para></summary>
    public static bool TryConsumeMoveCredit(ServerPlayer sp, MovementType movement, int spd, long now)
    {
        long floor = now - MoveCreditWindowMs;
        if (sp.MoveAllowedAt < floor) sp.MoveAllowedAt = floor;
        if (now < sp.MoveAllowedAt) return false;

        sp.MoveAllowedAt += (long)(movement == MovementType.Running
            ? MovementFormulas.RunMsPerTile(spd)
            : MovementFormulas.BaseWalkMsPerTile);
        return true;
    }

    public void PlayerMove(int index, Direction dir, MovementType movement)
    {
        if (!_pm[index].IsPlaying) return;
        if (dir > Direction.Right || movement < MovementType.Walking || movement > MovementType.Running) return;

        var p = _pm[index].Char;
        p.Dir = dir;

        // No SP left — force walking pace.
        if (movement == MovementType.Running && p.Sp <= 0)
            movement = MovementType.Walking;
        // Heavy Wind blocks running entirely.
        if (movement == MovementType.Running && _world.WeatherOn(p.Map) == WeatherType.HeavyWind)
            movement = MovementType.Walking;

        // WHEN, not only where. Everything below decides whether the destination is legal; this decides
        // whether it is legal YET. Charged AFTER both downgrades above, so a client that keeps claiming
        // Running on an empty SP bar is billed the walking pace the server is actually moving it at.
        //
        // A step refused further down for a WALL still costs its credit, which is deliberate: charging on
        // attempt keeps the budget one atomic operation, and an honest client cannot spend that way — it
        // predicts with the same collision rules and simply does not send a move it expects to fail.
        if (!TryConsumeMoveCredit(_pm[index], movement, p.Spd, Environment.TickCount64))
        {
            // The same correction a wall refusal sends below — the client predicted this step locally.
            _dispatcher.SendTo(index, PacketBuilder.PlayerMove(index, p.X, p.Y, p.Dir, MovementType.Walking, p.Layer));
            return;
        }

        bool moved = false;
        bool stepped = false;   // true only for normal tile steps (not edge-of-map warps)
        WorldLayer newLayer;    // the logical layer the in-map step lands on (committed to p.Layer)

        switch (dir)
        {
            case Direction.Up:
                if (p.Y > 0 && CanPlayerWalkOnTile(index, p.Map, p.X, p.Y - 1, dir, out newLayer))
                {
                    p.Y--;
                    p.Layer = newLayer;
                    BroadcastMove(index, movement);
                    moved = true;
                    stepped = true;
                }
                else if (p.Y == 0 && _world.Maps[p.Map].Up > 0
                    && CanPlayerWalkOnTile(index, _world.Maps[p.Map].Up, p.X, Constants.MaxMapY, dir, out newLayer))
                {
                    PlayerWarp(index, _world.Maps[p.Map].Up, p.X, Constants.MaxMapY, Direction.Up, movement, destLayer: newLayer);
                    moved = true;
                }
                break;

            case Direction.Down:
                if (p.Y < Constants.MaxMapY && CanPlayerWalkOnTile(index, p.Map, p.X, p.Y + 1, dir, out newLayer))
                {
                    p.Y++;
                    p.Layer = newLayer;
                    BroadcastMove(index, movement);
                    moved = true;
                    stepped = true;
                }
                else if (p.Y == Constants.MaxMapY && _world.Maps[p.Map].Down > 0
                    && CanPlayerWalkOnTile(index, _world.Maps[p.Map].Down, p.X, 0, dir, out newLayer))
                {
                    PlayerWarp(index, _world.Maps[p.Map].Down, p.X, 0, Direction.Down, movement, destLayer: newLayer);
                    moved = true;
                }
                break;

            case Direction.Left:
                if (p.X > 0 && CanPlayerWalkOnTile(index, p.Map, p.X - 1, p.Y, dir, out newLayer))
                {
                    p.X--;
                    p.Layer = newLayer;
                    BroadcastMove(index, movement);
                    moved = true;
                    stepped = true;
                }
                else if (p.X == 0 && _world.Maps[p.Map].Left > 0
                    && CanPlayerWalkOnTile(index, _world.Maps[p.Map].Left, Constants.MaxMapX, p.Y, dir, out newLayer))
                {
                    PlayerWarp(index, _world.Maps[p.Map].Left, Constants.MaxMapX, p.Y, Direction.Left, movement, destLayer: newLayer);
                    moved = true;
                }
                break;

            case Direction.Right:
                if (p.X < Constants.MaxMapX && CanPlayerWalkOnTile(index, p.Map, p.X + 1, p.Y, dir, out newLayer))
                {
                    p.X++;
                    p.Layer = newLayer;
                    BroadcastMove(index, movement);
                    moved = true;
                    stepped = true;
                }
                else if (p.X == Constants.MaxMapX && _world.Maps[p.Map].Right > 0
                    && CanPlayerWalkOnTile(index, _world.Maps[p.Map].Right, 0, p.Y, dir, out newLayer))
                {
                    PlayerWarp(index, _world.Maps[p.Map].Right, 0, p.Y, Direction.Right, movement, destLayer: newLayer);
                    moved = true;
                }
                break;
        }

        if (stepped && movement == MovementType.Running)
        {
            // Shield doubles run-stamina drain — wearing a shield trades mobility for the
            // magic-mit chip + physical block. Positional tradeoff: shield up = better defense
            // (especially against magic for non-Int builds), but slower to close gaps.
            int drain = (p.ShieldSlot > 0) ? 2 : 1;
            // Heat Wave doubles all stamina costs, including run drain.
            if (_world.WeatherOn(p.Map) == WeatherType.HeatWave)
                drain *= Constants.WeatherHeatWaveSpCostMultiplier;
            p.Sp = Math.Max(p.Sp - drain, 0);
            SendToMap(_world, p.Map, PacketBuilder.SendSp(index, p.Sp, p.MaxSp));
        }

        // Blood trail: a badly wounded player (<= BloodTrailHpThreshold of max HP) drips onto each fresh tile it
        // moves to — an in-map step OR a walk across a map edge (both set `moved`; a teleport isn't a PlayerMove).
        if (moved && p.Hp <= p.MaxHp * Constants.BloodTrailHpThreshold)
            _blood.DepositTrail(p.Map, p.X, p.Y, layer: p.Layer);

        if (!moved)
        {
            // Client may have predicted this move locally; correct their position (and layer).
            _dispatcher.SendTo(index, PacketBuilder.PlayerMove(index, p.X, p.Y, p.Dir, MovementType.Walking, p.Layer));
            return;
        }

        var destTile = _world.Maps[p.Map].Tile[p.X, p.Y];
        // Two-plane world: the post-step attribute is read on the mover's OWN layer — a Warp/door authored on the
        // bridge deck (FringeAttr) fires for a fringe walker, while the ground attribute at the same (x,y) is inert
        // to them (and vice-versa: a ground Warp does not fire for someone crossing the deck above it).
        var dest = LayerLogic.AttrFor(destTile, p.Layer);

        if (dest.Type == TileType.Warp)
        {
            // A warp can deliver onto the fringe deck; WarpLayer says which plane, and defaults to Ground.
            // Record the doorway taken so a chasing NPC can follow this exact warp (see ServerPlayer
            // warp-mark fields). p.X/p.Y are the warp tile the player just stepped onto.
            var sp = _pm[index];
            sp.WarpFromMap = p.Map;
            sp.WarpFromX = p.X;
            sp.WarpFromY = p.Y;
            sp.WarpToMap = dest.WarpMap;
            sp.WarpToX = dest.WarpX;
            sp.WarpToY = dest.WarpY;
            PlayerWarp(index, dest.WarpMap, dest.WarpX, dest.WarpY, destLayer: dest.WarpLayer);
        }
        else if (dest.Type == TileType.KeyOpen)
        {
            int kx = dest.DoorX;
            int ky = dest.DoorY;
            // The door's layer is authored on the KeyOpen, so a plate can open a Key door on EITHER plane —
            // a ground plate can open a fringe-deck gate, or a fringe plate the ground door beneath.
            var doorLayer = dest.DoorLayer;
            if (LayerLogic.AttrFor(_world.Maps[p.Map].Tile[kx, ky], doorLayer).Type == TileType.Key &&
                !_world.TempTiles[p.Map].IsDoorOpen(kx, ky, doorLayer))
            {
                _world.TempTiles[p.Map].OpenDoor(kx, ky, doorLayer, Environment.TickCount64);
                // Door state syncs to everyone rendering the area (observers); the notice is local chat (viewport).
                SendToMap(_world, p.Map, new MapKeyPacket { MapNum = p.Map, X = kx, Y = ky, Open = true, Layer = doorLayer });
                _dispatcher.SendLocalizedChatToViewport(index, ServerStrings.Common_DoorUnlocked, new ChatMetadata(GameColor.White, ChatChannel.System));
            }
        }
    }

    public void PlayerDir(int index, Direction dir)
    {
        if (!_pm[index].IsPlaying || dir > Direction.Right) return;
        var p = _pm[index].Char;
        p.Dir = dir;
        SendToMapBut(_world, p.Map, index, new SendPlayerDirPacket { Index = index, Dir = dir });
    }

    /// <summary>
    /// Moves a player to a new map.  <paramref name="edgeDir"/> non-null means the player simply walked
    /// off a map edge into an already-loaded neighbor — a <b>seamless</b> crossing: the client shifts its
    /// 3×3 grid (no reload, no input block) and asks for a region re-sync.  Null = a true warp/teleport,
    /// which uses the blocking reload handshake.
    /// </summary>
    public void PlayerWarp(int index, int mapNum, int x, int y, Direction? edgeDir = null,
                           MovementType movement = MovementType.Walking,
                           bool suppressShopGreeting = false,
                           WorldLayer destLayer = WorldLayer.Ground)
    {
        if (!_pm[index].IsPlaying || mapNum <= 0 || mapNum > _world.Limits.Maps) return;

        var p = _pm[index].Char;
        var sp = _pm[index];

        int oldMap = p.Map;
        var oldMoral = _world.MoralOf(oldMap);
        var oldGreeting = _world.GreetingOf(oldMap);
        var newGreeting = _world.GreetingOf(mapNum);
        // Walking between contiguous map tiles that share the same greeting (e.g. two maps of one building that
        // inherit it from their group) stays silent — the player hasn't entered or left anything.
        bool greetingChanged = oldGreeting != newGreeting;

        // Shops/inns hang off a keeper NPC now, not the map, so leaving the map ends any open shop session
        // (the r=5 re-check would catch it too, but a clean reset avoids a stale keeper reference lingering).
        if (oldMap != mapNum)
            sp.ClearActiveShop();

        if (greetingChanged)
            _shop.OnLeaveMap(index);

        p.Map = mapNum;
        p.X = x;
        p.Y = y;
        // Two-layer world: land on destLayer VERBATIM. A SEAMLESS edge cross passes the layer CanPlayerWalkOnTile
        // computed for the landing (so a bridge continues across the seam — already CanEnter-fit-validated); a true
        // warp/teleport defaults to Ground, an authored fringe warp target passes Fringe (§1b), and a relog passes
        // the PERSISTED layer so the player restores onto the bridge (standing on a ramp ⟹ Fringe, which is walkable
        // on the ramp, so persistence alone restores it correctly). No arrival re-fit: a target that lands you in a
        // wall is an AUTHORING bug for playtesting to catch, not something the engine papers over — consistent with
        // a bad persisted (X,Y) against an edited map, which isn't rescued either (the layer is just a coordinate).
        p.Layer = destLayer;

        // JoinGame passes suppressShopGreeting:true so the greeting can be re-issued after SendWelcome,
        // landing the map chatter last in the joining player's chat instead of before the welcome lines.
        if (!suppressShopGreeting && greetingChanged)
            _shop.OnJoinMap(index);

        if (sp.InGame && oldMap != mapNum)
        {
            var newMoral = _world.MoralOf(mapNum);
            bool isPk = p.IsPk(NowUtc);
            if (newMoral == MapMoral.Safe && oldMoral != MapMoral.Safe)
            {
                // Base line first (green), then the PvP-implications note on its own gray line for level 10+.
                // PvP is asymmetric in safe zones: non-PKers can strike PKers without retaliation,
                // PKers can only attack other PKers, and sub-level-10 players are outside PvP entirely.
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.MovementSystem_EnterSafeBase, new ChatMetadata(GameColor.BrightGreen, ChatChannel.System));
                if (p.Level >= 10)
                    _dispatcher.SendLocalizedChatTo(index, isPk ? ServerStrings.MovementSystem_EnterSafePk : ServerStrings.MovementSystem_EnterSafeNonPk, new ChatMetadata(GameColor.Gray, ChatChannel.System));
            }
            else if (oldMoral == MapMoral.Safe && newMoral != MapMoral.Safe)
            {
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.MovementSystem_LeaveSafeBase, new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
                if (!isPk && p.Level >= 10)
                    _dispatcher.SendLocalizedChatTo(index, ServerStrings.MovementSystem_LeaveSafeNonPk, new ChatMetadata(GameColor.Gray, ChatChannel.System));
            }

            // Arena transition — a separate if/else (not chained to the safe block above) so an
            // Arena↔Safe crossing correctly announces both "exit arena" and "enter safe".
            if (newMoral == MapMoral.Arena && oldMoral != MapMoral.Arena)
            {
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.MovementSystem_EnterArenaBase, new ChatMetadata(GameColor.Yellow, ChatChannel.System));
                if (p.Level >= 10)
                    _dispatcher.SendLocalizedChatTo(index, ServerStrings.MovementSystem_EnterArenaPvp, new ChatMetadata(GameColor.Gray, ChatChannel.System));
            }
            else if (oldMoral == MapMoral.Arena && newMoral != MapMoral.Arena)
            {
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.MovementSystem_LeaveArena, new ChatMetadata(GameColor.Yellow, ChatChannel.System));
            }

            // Territory war: warn a non-participant crossing INTO a territory that has a live
            // contest (any phase) — a courtesy so they can clear the area; participants already got the notice.
            if (_world.ContestZones.Count > 0)
                WarnIfEnteringContestZone(index, oldMap, mapNum);
        }

        if (_pm.GetTotalMapPlayers(oldMap) == 0)
            _world.PlayersOnMap[oldMap] = false;
        _world.PlayersOnMap[mapNum] = true;

        // Seamless world: this player now observes the new map's 3×3 region (and stops
        // observing maps that fell out of view).  Drives NPC AI ticking and entity broadcasts.
        _world.RemoveObserver(index, oldMap);
        _world.AddObserver(index, mapNum);

        // Membership: tell players who could see this one on the old map but can no longer
        // see it on the new map to drop its sprite.  Players who still observe it (and new
        // observers) are (re)synced by SendJoinData once this player's client is ready.
        foreach (int q in _world.MapObservers[oldMap])
        {
            if (q != index && !_world.IsObserving(q, mapNum))
                _dispatcher.SendTo(q, PacketBuilder.LeaveMap(index));
        }

        if (edgeDir is { } d)
        {
            // Seamless edge crossing: the new center map is already loaded as a neighbor.  Tell the
            // client to shift its grid and re-center — no GettingMap, no reload.  It then requests a
            // region re-sync to fill in the newly-revealed edge maps/entities.
            _dispatcher.SendTo(index, new SeamlessCrossPacket { MapNum = mapNum, Dir = d, X = x, Y = y, Layer = p.Layer, Revision = _world.Maps[mapNum].Revision });

            // Observers who can see this player on BOTH the old and new maps get a seam-cross MOVE so
            // they slide the sprite over the border (MapNum set = cross).  Those who just lost sight got
            // LeaveMap above; those who just gained it get the player fresh via the region re-sync — so
            // we'd only make those snap/flicker, hence the both-maps intersection.
            foreach (int q in _world.MapObservers[mapNum])
            {
                if (q != index && _world.IsObserving(q, oldMap))
                {
                    _dispatcher.SendTo(q, new SendPlayerMovePacket
                    {
                        Index = index, MapNum = mapNum, X = x, Y = y, Dir = d, Movement = movement, Layer = p.Layer,
                    });
                }
            }
        }
        else
        {
            sp.GettingMap = true;
            _dispatcher.SendTo(index, new CheckForMapPacket { MapNum = mapNum, Revision = _world.Maps[mapNum].Revision });
        }
    }

    // ── NPC movement ──────────────────────────────────────────────────────────

    // Guards ignore the NpcAvoid ("npc block") map attribute — they path and step across those
    // tiles as if walkable, so a guard can cut straight through an npc-block barrier when chasing a
    // PK or sweeping litter.  Every other behavior still treats NpcAvoid as a wall (its normal use:
    // shaping wander zones / fencing wild mobs out of an area).
    public static bool NpcIgnoresNpcAvoid(NpcBehavior behavior) => behavior == NpcBehavior.Guard;

    // Tile-type landing test for an NPC: Walkable, Item, and a LayerRamp surface are always legal; NpcAvoid
    // is legal only when the mover ignores it (guards).  Blocked / Warp / Key stay impassable for every NPC.
    // LayerRamp is the walkable connector between the two layers; it only ever appears as a fringe attribute
    // (read via LayerLogic.AttrFor at WorldLayer.Fringe), so treating it as walkable lets a fringe-layer NPC
    // climb onto / descend a ramp exactly as a player can (a player's gate is "not Blocked", which ramps pass).
    public static bool IsNpcWalkableTileType(TileType type, bool ignoreNpcAvoid)
        => type == TileType.Walkable
           || type == TileType.Item
           || type == TileType.LayerRamp
           || (ignoreNpcAvoid && type == TileType.NpcAvoid);

    public bool CanNpcMove(int mapNum, int npcSlot, Direction dir)
    {
        if (npcSlot <= 0 || npcSlot > Constants.MaxMapNpcs) return false;
        return CanNpcMoveFrom(mapNum, _world.MapNpcs[mapNum, npcSlot], dir);
    }

    /// <summary>
    /// Movement validity for any NPC record (native slot or traversal guest), excluding itself
    /// from the tile-occupancy check by reference.  Bounds-only — edge crossings are handled by
    /// the AI's cross-map chase logic, not here.
    /// </summary>
    public bool CanNpcMoveFrom(int mapNum, MapNpcRecord npc, Direction dir)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return false;

        var npcRec = _world.Npcs[npc.Num];
        int size = npcRec.EffectiveSize;
        bool ignoreNpcAvoid = NpcIgnoresNpcAvoid(npcRec.Behavior);

        // Resolve the destination anchor in world space and let LayerLogic pick the resulting layer
        // (sticky, ramp-gated) and reject an illegal deck-edge walk-off.  Covers cross-seam bridges.
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = WorldCoordHelper.ToWorld(1, 1, npc.X, npc.Y);
        var (adx, ady) = WorldCoordHelper.DirDelta(dir);
        if (!LayerLogic.CanEnter(view, aWX + adx, aWY + ady, size, npc.Layer, dir, out var newLayer)) return false;

        if (size <= 1)
        {
            // Single tile in front, on this map.  An off-map front tile fails the bounds test in
            // IsNpcTileFree; the actual edge cross is handled separately (StepLeavesMap / cross-border).
            var (nx, ny) = WorldCoordHelper.LeadingEdgeTiles(npc.X, npc.Y, 1, dir)[0];
            return IsNpcTileFree(mapNum, nx, ny, newLayer, ignoreNpcAvoid, npc);
        }

        // The anchor itself must stay on this map: the footprint may spill across a seam, but a WITHIN-map
        // step never walks the anchor off the edge (a real seam cross is handled by StepLeavesMap /
        // TryNativeStep).  Without this, a WANDERING big NPC - which steps via CanNpcMove, bypassing
        // StepLeavesMap - would push its anchor off-map, leaving its footprint on no on-map tile at all and
        // so invisible to collision.
        if ((uint)(npc.X + adx) > Constants.MaxMapX || (uint)(npc.Y + ady) > Constants.MaxMapY) return false;

        // A large NPC's leading edge can already spill across a seam on a within-map anchor step (the body
        // straddles), so resolve each leading-edge tile in world space and validate it (at the resulting
        // layer) on whichever map it lands on.  Excludes the mover itself so its own body never blocks it.
        var edge = WorldCoordHelper.LeadingEdgeTiles(aWX, aWY, size, dir);
        for (int e = 0; e < edge.Count; e++)
        {
            var (ewx, ewy) = edge[e];
            var (m, lx, ly) = WorldCoordHelper.ResolveWorldTile(in grid, ewx, ewy);
            if (m <= 0) return false;
            if (!IsNpcTileFree(m, lx, ly, newLayer, ignoreNpcAvoid, npc)) return false;
        }
        return true;
    }

    /// <summary>
    /// True when a tile on a map is a legal landing spot for an NPC — walkable type and free of
    /// players and other NPCs (native or traversal).  Used to validate a border-cross destination.
    /// <paramref name="ignoreNpcAvoid"/> is the mover's guard exception (see <see cref="NpcIgnoresNpcAvoid"/>).
    /// </summary>
    public bool IsNpcDestFree(int mapNum, int x, int y, bool ignoreNpcAvoid)
        => IsNpcTileFree(mapNum, x, y, WorldLayer.Ground, ignoreNpcAvoid, null);

    /// <summary>
    /// Bounds + walkable-type + no-player + no-other-NPC test for a single tile on a map, excluding the
    /// mover itself (by reference) from the NPC-occupancy check.  Shared by <see cref="CanNpcMoveFrom"/>'s
    /// footprint leading-edge loop and by <see cref="IsNpcDestFree"/> (border-cross landings).  The
    /// NPC-occupancy half is footprint-aware via <see cref="GameWorld.IsTileOccupiedByNpc"/>, so a single
    /// tile inside a big NPC's body counts as occupied.
    /// </summary>
    private bool IsNpcTileFree(int mapNum, int x, int y, WorldLayer layer, bool ignoreNpcAvoid, MapNpcRecord? exclude)
    {
        if (mapNum <= 0 || mapNum > _world.Limits.Maps) return false;
        if (x < 0 || x > Constants.MaxMapX || y < 0 || y > Constants.MaxMapY) return false;
        // Walkable type at the mover's layer: a fringe surface / ramp is walkable up top, a fringe wall is not.
        var type = LayerLogic.AttrFor(_world.Maps[mapNum].Tile[x, y], layer).Type;
        if (!IsNpcWalkableTileType(type, ignoreNpcAvoid)) return false;

        // Block on any player occupying the tile on the SAME layer.  Iterate the pre-maintained observable-
        // area set instead of the whole roster; players on neighboring maps are filtered out by pc.Map.
        foreach (int i in _world.MapObservers[mapNum])
        {
            if (!_pm[i].IsPlaying) continue;
            var pc = _pm[i].Char;
            if (pc.Map == mapNum && pc.X == x && pc.Y == y && pc.Layer == layer) return false;
        }
        return !_world.IsTileOccupiedByNpc(mapNum, x, y, exclude, layer);
    }

    /// <summary>
    /// Footprint-aware landing test for a border cross or warp-follow: the whole SxS block anchored (top-left)
    /// at (x,y) on <paramref name="destMap"/> must be walkable and free, resolved across seams, EXCLUDING the
    /// crossing NPC itself (its pre-cross body may still spill onto the landing).  For size 1 this is exactly
    /// <see cref="IsNpcDestFree"/>.
    /// </summary>
    public bool IsNpcFootprintLandingFree(int destMap, int x, int y, int size, bool ignoreNpcAvoid, MapNpcRecord mover,
        WorldLayer? layerOverride = null)
    {
        // Validate the landing at the mover's OWN layer (a fringe walker lands on the fringe surface; a ground
        // walker on the ground — where a ramp reads Blocked), or an explicit override the caller resolved (e.g.
        // a cross-seam step that ascends/descends).  A hardcoded Ground here let a fringe-layer guest's seam
        // cross be judged against the ground beneath the deck.
        var layer = layerOverride ?? mover.Layer;
        if (size <= 1) return IsNpcTileFree(destMap, x, y, layer, ignoreNpcAvoid, mover);
        if (destMap <= 0 || destMap > _world.Limits.Maps) return false;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, destMap);
        var (aWX, aWY) = WorldCoordHelper.ToWorld(1, 1, x, y);
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                var (m, lx, ly) = WorldCoordHelper.ResolveWorldTile(in grid, aWX + i, aWY + j);
                if (m <= 0) return false;
                if (!IsNpcTileFree(m, lx, ly, layer, ignoreNpcAvoid, mover)) return false;
            }
        }

        return true;
    }

    /// <summary>The ramp corridor + fit + layer gate for an NPC's would-be step in <paramref name="dir"/>,
    /// evaluated in WORLD space so it reads a ramp that sits across a seam — the SAME
    /// <see cref="LayerLogic.CanEnter"/> a within-map step gets in <see cref="CanNpcMoveFrom"/>, but WITHOUT the
    /// occupancy / off-map-anchor checks, so the cross-border step can reuse it.  Outputs the resulting layer
    /// (ascend/descend across a ramp, else the source layer).</summary>
    public bool NpcStepPassesRampGate(int mapNum, MapNpcRecord npc, Direction dir, out WorldLayer newLayer)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var view = new ServerTileView(_world, grid);
        var (aWX, aWY) = WorldCoordHelper.ToWorld(1, 1, npc.X, npc.Y);
        var (adx, ady) = WorldCoordHelper.DirDelta(dir);
        int size = _world.Npcs[npc.Num].EffectiveSize;
        return LayerLogic.CanEnter(view, aWX + adx, aWY + ady, size, npc.Layer, dir, out newLayer);
    }

    public void NpcMove(int mapNum, int npcSlot, Direction dir, MovementType movement)
    {
        if (!CanNpcMove(mapNum, npcSlot, dir)) return;
        var npc = _world.MapNpcs[mapNum, npcSlot];

        // Commit the resulting logical layer (sticky / ramp-gated) BEFORE mutating X/Y — LayerLogic reads
        // the src/dest tiles around the anchor's pre-move position.
        int size = _world.Npcs[npc.Num].EffectiveSize;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mapNum);
        var (aWX, aWY) = WorldCoordHelper.ToWorld(1, 1, npc.X, npc.Y);
        var (dx, dy) = WorldCoordHelper.DirDelta(dir);
        npc.Layer = LayerLogic.ResolveLayer(new ServerTileView(_world, grid), aWX + dx, aWY + dy, size, npc.Layer, dir);

        npc.Dir = dir;
        switch (dir)
        {
            case Direction.Up:
                npc.Y--;
                break;
            case Direction.Down:
                npc.Y++;
                break;
            case Direction.Left:
                npc.X--;
                break;
            case Direction.Right:
                npc.X++;
                break;
        }
        SendToMap(_world, mapNum, new NpcMovePacket
        {
            MapNum = mapNum,
            NpcSlot = npcSlot,
            X = npc.X,
            Y = npc.Y,
            Dir = npc.Dir,
            Movement = movement,
            Layer = npc.Layer,
        });

        // Blood trail: a badly wounded NPC (<= BloodTrailHpThreshold of max HP) drips onto each fresh tile it steps to.
        var npcRec = _world.Npcs[npc.Num];
        if (npc.Hp <= _world.EffectiveNpcMaxHp(npcRec) * Constants.BloodTrailHpThreshold)
            _blood.DepositTrail(mapNum, npc.X, npc.Y, npcRec.EffectiveSize, npc.Layer);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private bool CanPlayerWalkOnTile(int index, int destMapNum, int x, int y, Direction dir, out WorldLayer newLayer)
    {
        newLayer = WorldLayer.Ground;
        var mover = _pm[index].Char;

        // Resolve the destination in world space over the mover's 3x3 grid, then let LayerLogic pick the
        // resulting logical layer (sticky, ramp-gated) and reject an illegal deck-edge walk-off.  Players
        // are always size 1.  This also covers cross-seam bridges: the view reads neighbor tiles too.
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, mover.Map);
        var gp = WorldCoordHelper.GridPosition(in grid, destMapNum);
        if (gp is null) return false;
        var (destWX, destWY) = WorldCoordHelper.ToWorld(gp.Value.col, gp.Value.row, x, y);
        if (!LayerLogic.CanEnter(new ServerTileView(_world, grid), destWX, destWY, 1, mover.Layer, dir, out newLayer))
            return false;

        // Tile attribute at the RESULTING layer: a fringe railing (Blocked on FringeAttr) stops a
        // fringe-layer walker but not someone underneath.  Door state is per (tile, layer) — open flag AND
        // auto-close clock alike — so a deck door and the ground door beneath it are fully independent
        // (see TempTileState.DoorOpenedAt).
        var tile = _world.Maps[destMapNum].Tile[x, y];
        var attrType = LayerLogic.AttrFor(tile, newLayer).Type;
        if (attrType == TileType.Blocked) return false;
        if (attrType == TileType.Key && !_world.TempTiles[destMapNum].IsDoorOpen(x, y, newLayer)) return false;

        // Territory contest setup: during the 10-min ramp-up only defenders may enter a capture
        // radius — attackers + non-participants hit an invisible wall. Cheap: ContestZones is empty off war night.
        if (_world.ContestZones.Count > 0 && IsBlockedBySetupWall(index, destMapNum, x, y, newLayer)) return false;

        // Block on any live NPC on the SAME layer — native slot or visiting traversal NPC.
        if (_world.IsTileOccupiedByNpc(destMapNum, x, y, null, newLayer)) return false;

        // Block on other players on the destination tile AND layer, mirroring the client's prediction so a
        // tampered client can't cheat through.  Pass-through applies when either the source or destination
        // map is safe AND the mover isn't PK-flagged.  A grace-period PKer counts as effectively non-PK.
        var srcMoral = _world.MoralOf(mover.Map);
        var destMoral = _world.MoralOf(destMapNum);
        long nowUtc = NowUtc;
        bool effectivelyPk = mover.IsPk(nowUtc) && _pm[index].PkGraceUntilUtc <= nowUtc;
        bool playersPassThrough = !effectivelyPk && (srcMoral == MapMoral.Safe || destMoral == MapMoral.Safe);
        if (!playersPassThrough)
        {
            foreach (int i in _world.MapObservers[destMapNum])
            {
                if (i == index || !_pm[i].IsPlaying) continue;
                var pc = _pm[i].Char;
                if (pc.Map == destMapNum && pc.X == x && pc.Y == y && pc.Layer == newLayer) return false;
            }
        }
        return true;
    }

    // Setup radius wall: a non-defender may not step into a capture-point radius while a contest is
    // in its setup phase. The defending guild (DefenderGuild > 0 and it's the mover's guild) may enter freely.
    private bool IsBlockedBySetupWall(int index, int mapNum, int x, int y, WorldLayer layer)
    {
        int guild = _pm[index].Guild;
        foreach (var z in _world.ContestZones)
        {
            if (!z.SetupPhase) continue;
            if (z.DefenderGuild > 0 && guild == z.DefenderGuild) continue;   // the defending guild may enter
            foreach (var pt in z.Points)
            {
                if (pt.Map == mapNum && pt.Layer == layer &&   // the wall is on the point's own layer
                    TerritoryContestFormulas.WithinRadius(x, y, pt.X, pt.Y, Constants.TerritoryCapturePointRadius))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Non-participant entry warning: fired on a map change into a contested territory from outside
    // it, so a bystander is told to clear the area. Participants already received the ramp-up/phase notices.
    private void WarnIfEnteringContestZone(int index, int oldMap, int newMap)
    {
        int guild = _pm[index].Guild;
        foreach (var z in _world.ContestZones)
        {
            if (!z.Maps.Contains(newMap) || z.Maps.Contains(oldMap)) continue;   // only when crossing IN from outside
            if (z.Participants.Contains(guild)) return;                          // a participant — no warning
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.GuildTerritory_NonParticipantWarning,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System), ("Territory", z.Name));
            return;
        }
    }

    private void BroadcastMove(int index, MovementType movement)
    {
        var p = _pm[index].Char;
        // Exclude the mover — they already applied the move client-side (prediction).
        SendToMapBut(_world, p.Map, index, PacketBuilder.PlayerMove(index, p.X, p.Y, p.Dir, movement, p.Layer));
    }
}
