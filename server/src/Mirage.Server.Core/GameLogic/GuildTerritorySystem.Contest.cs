using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The live king-of-the-hill contest: capture points, the scoring tick, the participant-only
/// render state pushed to clients, and the finalization that hands the territory over.</summary>
public sealed partial class GuildTerritorySystem : GameSystem
{
    // ── Live contest (KotH) ───────────────────────────────────────────────────
    private void StartContest(MapGroupRecord group, int defenderId, List<int> challengers)
    {
        var maps = TerritoryMaps(group.Index);
        var contest = new TerritoryContest
        {
            TerritoryIndex = group.Index,
            DefenderGuild = defenderId,
            Phase = ContestPhase.Setup,
            PhaseEndUtc = UtcNow() + Constants.TerritoryContestSetupSeconds,
            Points = GenerateContestPoints(maps, defenderId),
            Participants = new HashSet<int>(challengers),
        };
        if (defenderId > 0) contest.Participants.Add(defenderId);
        _contests.Add(contest);

        // Publish the movement/spawn projection (setup radius walls + NPC suppression + entry warnings) so those
        // systems act on the war state without a back-reference to this one (see GameWorld.ContestZones).
        _world.ContestZones.Add(new ContestZone
        {
            TerritoryIndex = group.Index,
            Name = TerritoryName(group),
            DefenderGuild = defenderId,
            SetupPhase = true,
            Participants = new HashSet<int>(contest.Participants),
            Maps = maps,
            Points = contest.Points.Select(p => new ContestZone.CapturePoint(p.Map, p.X, p.Y, p.Layer)).ToList(),
        });

        // NPCs vanish for the whole war state (no PvE / no income mid-war); a non-defender caught in a capture
        // radius is pushed out; non-participants standing in the territory are warned to leave.
        foreach (int m in maps) _spawn.DespawnMapNpcs(m);
        PushNonDefendersOutOfRadii(contest, defenderId);
        WarnNonParticipantsPresent(contest);

        int mins = Constants.TerritoryContestSetupSeconds / 60;
        foreach (int g in contest.Participants)
            GuildWarNotice(g, ServerStrings.GuildTerritory_SetupBegun, ("Territory", TerritoryName(group)), ("Minutes", mins));
        BroadcastContest(contest);   // reveal the flags/circles to participants at setup start
        _logger.LogInformation("Territory {Terr} contest started with {N} participant(s).", group.Index, contest.Participants.Count);
    }

    // ── Client render state (participant-only flags/circles/names + HUD) ─────────────────
    // Push one contest's live render state (points + KotH scores + phase) to every online member of a
    // participating guild. Sent at setup start + on each 5s tick; the client colors per-viewer and gates the HUD.
    private void BroadcastContest(TerritoryContest c)
    {
        var group = _world.MapGroups.GetValueOrDefault(c.TerritoryIndex);
        var packet = new TerritoryContestPacket
        {
            Active = true,
            TerritoryIndex = c.TerritoryIndex,
            TerritoryName = group is not null ? TerritoryName(group) : "",
            Phase = (int)c.Phase,
            Points = c.Points.Select(p => new ContestPointView
            {
                Label = p.Label, Map = p.Map, X = p.X, Y = p.Y, Layer = p.Layer,
                OwnerGuild = p.OwnerGuild, ChallengerGuild = p.ChallengerGuild, Meter = p.Meter,
            }).ToList(),
            // Emit a row for EVERY participant (defender + challengers), defaulting to 0 — so the client's score
            // list shows all competitors and its territory-swords check can see the full participant set.
            Scores = c.Participants.Select(g => new ContestScoreView
            {
                GuildId = g, GuildName = _guilds.GuildById(g)?.Name ?? "", Score = c.Scores.GetValueOrDefault(g),
            }).ToList(),
        };
        foreach (int g in c.Participants) _dispatcher.SendToGuild(g, packet);
    }

    // Tear the contest render down on every participant (flags/circles/HUD vanish) at war end.
    private void BroadcastContestClear(TerritoryContest c)
    {
        var packet = new TerritoryContestPacket { Active = false, TerritoryIndex = c.TerritoryIndex };
        foreach (int g in c.Participants) _dispatcher.SendToGuild(g, packet);
    }

    // Advance every live contest one 5s tick: score the running ones, then roll phases whose timer elapsed.
    private void TickContests(long now)
    {
        for (int i = _contests.Count - 1; i >= 0; i--)
        {
            var c = _contests[i];
            if (c.Phase == ContestPhase.Contest) ScoreContestTick(c);
            if (now >= c.PhaseEndUtc)
            {
                switch (c.Phase)
                {
                    case ContestPhase.Setup:
                        c.Phase = ContestPhase.Contest;
                        c.PhaseEndUtc = now + Constants.TerritoryContestSeconds;
                        if (ZoneFor(c.TerritoryIndex) is { } startZone) startZone.SetupPhase = false;   // radius walls lift
                        foreach (int g in c.Participants) GuildWarNotice(g, ServerStrings.GuildTerritory_ContestBegun, ("Territory", TerritoryNameOf(c)));
                        break;
                    case ContestPhase.Contest:
                        FinalizeContest(c);
                        c.Phase = ContestPhase.Cooldown;
                        c.PhaseEndUtc = now + Constants.TerritoryContestCooldownSeconds;
                        int mins = Constants.TerritoryContestCooldownSeconds / 60;
                        foreach (int g in c.Participants) GuildWarNotice(g, ServerStrings.GuildTerritory_CooldownBegun, ("Territory", TerritoryNameOf(c)), ("Minutes", mins));
                        break;
                    case ContestPhase.Cooldown:
                        _contests.RemoveAt(i);
                        BroadcastContestClear(c);           // flags + HUD vanish instantly at war end
                        EndContestZone(c.TerritoryIndex);   // lift suppression/walls, then resume the map NPCs
                        if (_contests.Count == 0) AnnounceWarPublic(ServerStrings.GuildTerritory_WarNightEnd);
                        continue;                           // removed — skip the live broadcast below
                }
            }
            BroadcastContest(c);   // push the live render state (flags/meters/scores) to participants each tick
        }
    }

    // One 5s scoring tick: push each point's meter by the majority in its radius, then award the owned points.
    private void ScoreContestTick(TerritoryContest c)
    {
        foreach (var pt in c.Points)
        {
            int majority = MajorityGuildInRadius(pt, c.Participants);
            var r = TerritoryContestFormulas.AdvanceMeter(pt.Meter, pt.OwnerGuild, pt.ChallengerGuild, majority);
            pt.Meter = r.Meter;
            pt.OwnerGuild = r.Owner;
            pt.ChallengerGuild = r.Challenger;
            int delta = TerritoryContestFormulas.ScoreDelta(TerritoryContestFormulas.ScorerThisTick(pt.Meter, pt.OwnerGuild), c.DefenderGuild);
            if (delta > 0) c.Scores[pt.OwnerGuild] = c.Scores.GetValueOrDefault(pt.OwnerGuild) + delta;
        }
    }

    private void FinalizeContest(TerritoryContest c)
    {
        if (_world.MapGroups.GetValueOrDefault(c.TerritoryIndex) is not { } group) return;
        int winner = TerritoryContestFormulas.DetermineWinner(c.Scores, c.DefenderGuild);
        var challengers = c.Participants.Where(p => p != c.DefenderGuild).ToList();
        ApplyOutcome(group, winner, challengers);
    }

    // The strict-plurality participant guild standing in a point's radius (0 = contested tie or empty).
    private int MajorityGuildInRadius(ContestPoint pt, HashSet<int> participants)
    {
        var counts = new Dictionary<int, int>();
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            int g = sp.Guild;
            if (g <= 0 || !participants.Contains(g)) continue;
            var ch = sp.Char;
            // Two-layer world: only players on the point's own layer hold it — standing on the ground UNDER a
            // bridge-top point earns no credit; you must be up on the deck (and vice-versa).
            if (ch.Map != pt.Map || ch.Layer != pt.Layer || !WithinRadius(ch.X, ch.Y, pt.X, pt.Y)) continue;
            counts[g] = counts.GetValueOrDefault(g) + 1;
        }
        int max = 0;
        foreach (int v in counts.Values) if (v > max) max = v;
        if (max == 0) return 0;
        int top = 0, topCount = 0;
        foreach (var kv in counts) if (kv.Value == max) { top = kv.Key; topCount++; }
        return topCount == 1 ? top : 0;
    }

    private static bool WithinRadius(int x, int y, int cx, int cy) =>
        TerritoryContestFormulas.WithinRadius(x, y, cx, cy, Constants.TerritoryCapturePointRadius);

    // Every map that belongs to a territory (its MapGroup index) — the contest's arena for point placement,
    // NPC despawn, and the war-death respawn area.
    private List<int> TerritoryMaps(int territoryIndex)
    {
        var maps = new List<int>();
        for (int m = 1; m <= _world.Limits.Maps; m++)
            if (_world.Maps[m].MapGroup == territoryIndex) maps.Add(m);
        return maps;
    }

    // Place the capture points: 1 per N maps (clamped), distributed across the territory's maps round-robin,
    // and within each map spread by farthest-point sampling over the map's LARGEST connected walkable region
    // (so a point is always stand-able + path-reachable, never on an isolated pocket, and points stay evenly
    // spread). The defender (if any) starts securely owning every point.
    private List<ContestPoint> GenerateContestPoints(List<int> maps, int defenderId)
    {
        int count = TerritoryContestFormulas.PointCount(maps.Count);
        var points = new List<ContestPoint>();
        var components = new Dictionary<int, List<(int X, int Y)>>();   // cached largest walkable region per map
        var placedOnMap = new Dictionary<int, List<(int X, int Y)>>();  // points already placed on each map
        for (int i = 0; i < count && maps.Count > 0; i++)
        {
            int mapNum = maps[i % maps.Count];
            var region = components.TryGetValue(mapNum, out var r) ? r : (components[mapNum] = LargestWalkableComponent(mapNum));
            if (region.Count == 0) continue;
            var placed = placedOnMap.TryGetValue(mapNum, out var p) ? p : (placedOnMap[mapNum] = new List<(int X, int Y)>());
            var pick = FarthestPoint(region, placed);
            placed.Add(pick);
            points.Add(new ContestPoint
            {
                Label = TerritoryContestFormulas.PointLabels[i], Map = mapNum, X = pick.X, Y = pick.Y,
                // Auto-placed points stay on the GROUND — auto-placing onto a bridge deck is too fiddly to handle
                // reliably (user call).  The Layer field + credit gate remain so a hand-authored fringe point could
                // work later; a ground point is still held from the ground, not from a bridge above it.
                Layer = WorldLayer.Ground,
                OwnerGuild = defenderId,
                Meter = defenderId > 0 ? -Constants.TerritoryCaptureFull : 0,   // defender secure, else neutral
            });
        }
        return points;
    }

    // The biggest 4-connected region of walkable tiles on a map (BFS flood-fill) — a capture point is only
    // ever placed inside it, so it's guaranteed reachable (never on an isolated one-tile walkable pocket).
    private List<(int X, int Y)> LargestWalkableComponent(int mapNum)
    {
        var map = _world.Maps[mapNum];
        int w = map.Width, h = map.Height;
        var seen = new bool[w, h];
        var best = new List<(int X, int Y)>();
        var queue = new Queue<(int X, int Y)>();
        for (int sx = 0; sx < w; sx++)
        {
            for (int sy = 0; sy < h; sy++)
            {
                if (seen[sx, sy] || map.Tile[sx, sy].Type != TileType.Walkable) continue;
                var comp = new List<(int X, int Y)>();
                seen[sx, sy] = true;
                queue.Enqueue((sx, sy));
                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();
                    comp.Add((x, y));
                    foreach (var (nx, ny) in Neighbors4(x, y))
                    {
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && !seen[nx, ny] && map.Tile[nx, ny].Type == TileType.Walkable)
                        {
                            seen[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
                if (comp.Count > best.Count) best = comp;
            }
        }

        return best;
    }

    // Greedy farthest-point pick: the candidate maximizing its min straight-line distance to the points
    // already placed (a random candidate when none are placed yet), keeping a contest's points spread out.
    private (int X, int Y) FarthestPoint(List<(int X, int Y)> candidates, List<(int X, int Y)> placed)
    {
        if (placed.Count == 0) return candidates[Rng.Next(candidates.Count)];
        (int X, int Y) best = candidates[0];
        long bestDist = -1;
        foreach (var c in candidates)
        {
            long minD = long.MaxValue;
            foreach (var pt in placed)
            {
                long dx = c.X - pt.X, dy = c.Y - pt.Y;
                minD = Math.Min(minD, dx * dx + dy * dy);
            }
            if (minD > bestDist)
            {
                bestDist = minD;
                best = c;
            }
        }
        return best;
    }

    private static IEnumerable<(int, int)> Neighbors4(int x, int y)
    {
        yield return (x - 1, y);
        yield return (x + 1, y);
        yield return (x, y - 1);
        yield return (x, y + 1);
    }

    private bool TryPickWalkable(int mapNum, out int x, out int y)
    {
        var map = _world.Maps[mapNum];
        // Counted, then walked to the winning tile. A list of every walkable tile is 65,000 entries
        // on a large map; the draw is one number either way.
        int walkable = 0;
        for (int tx = 0; tx < map.Width; tx++)
        {
            for (int ty = 0; ty < map.Height; ty++)
                if (map.Tile[tx, ty].Type == TileType.Walkable) walkable++;
        }

        if (walkable == 0)
        {
            x = y = 0;
            return false;
        }

        int pick = Rng.Next(walkable);
        for (int tx = 0; tx < map.Width; tx++)
        {
            for (int ty = 0; ty < map.Height; ty++)
            {
                if (map.Tile[tx, ty].Type != TileType.Walkable) continue;
                if (pick-- > 0) continue;
                x = tx;
                y = ty;
                return true;
            }
        }

        x = y = 0;
        return false;
    }

    private bool HasActiveContest(int territoryIndex)
    {
        foreach (var c in _contests) if (c.TerritoryIndex == territoryIndex) return true;
        return false;
    }
}
