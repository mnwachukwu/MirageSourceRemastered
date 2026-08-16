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

/// <summary>The contest zone as a place — setup walls, pushing non-participants out, NPC suppression,
/// the combat-integration queries CombatSystem asks of it, and the officer request queue.</summary>
public sealed partial class GuildTerritorySystem : GameSystem
{
    // ── Setup zone lifecycle (walls, push-out, NPC despawn/resume) ─────────────────
    // The GameWorld projection for a live contest (radius walls + NPC suppression + entry warnings).
    private ContestZone? ZoneFor(int territoryIndex)
    {
        foreach (var z in _world.ContestZones) if (z.TerritoryIndex == territoryIndex) return z;
        return null;
    }

    // Cooldown end: drop the projection (lifts the radius walls + NPC-spawn suppression), then resume the map
    // NPCs now that the whole war state is over (NPCs return only after the cooldown).
    private void EndContestZone(int territoryIndex)
    {
        var maps = ZoneFor(territoryIndex)?.Maps ?? TerritoryMaps(territoryIndex);
        _world.ContestZones.RemoveAll(z => z.TerritoryIndex == territoryIndex);
        foreach (int m in maps) _spawn.SpawnMapNpcs(m);
    }

    // Setup push-out: a non-defender caught inside a capture radius when setup begins is warped to a
    // walkable tile OUTSIDE every radius on that map (attackers + non-participants alike; the defending guild
    // may stay). Runs once at setup start; the radius walls then keep them out.
    private void PushNonDefendersOutOfRadii(TerritoryContest c, int defenderId)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            if (defenderId > 0 && sp.Guild == defenderId) continue;   // defenders may stand in the radii
            var ch = sp.Char;
            bool inside = false;
            foreach (var pt in c.Points)
            {
                if (ch.Map == pt.Map && ch.Layer == pt.Layer && WithinRadius(ch.X, ch.Y, pt.X, pt.Y))
                {
                    inside = true;
                    break;
                }
            }

            if (!inside) continue;
            if (TryPickNearestTileOutsideRadii(ch.Map, ch.X, ch.Y, c.Points, out int x, out int y))
                _movement.PlayerWarp(i, ch.Map, x, y);
        }
    }

    // The NEAREST walkable tile outside every capture radius, found by BFS over walkable tiles out from the
    // player's tile — a gentle push (least-disruptive, path-reachable relocation) rather than a random
    // teleport across the map. False only if no such tile is reachable (every reachable tile is covered).
    private bool TryPickNearestTileOutsideRadii(int mapNum, int fromX, int fromY, List<ContestPoint> points, out int x, out int y)
    {
        var map = _world.Maps[mapNum];
        int w = Constants.MaxMapX + 1, h = Constants.MaxMapY + 1;
        x = y = 0;
        if (fromX < 0 || fromX >= w || fromY < 0 || fromY >= h) return false;
        var seen = new bool[w, h];
        var queue = new Queue<(int X, int Y)>();
        seen[fromX, fromY] = true;
        queue.Enqueue((fromX, fromY));  // start tile is walkable (the player stands on it)
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            if (!AnyRadiusCovers(cx, cy, mapNum, points))
            {
                x = cx;
                y = cy;
                return true;
            }  // first walkable tile clear of all radii
            foreach (var (nx, ny) in Neighbors4(cx, cy))
            {
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && !seen[nx, ny] && map.Tile[nx, ny].Type == TileType.Walkable)
                {
                    seen[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return false;
    }

    private static bool AnyRadiusCovers(int x, int y, int mapNum, List<ContestPoint> points)
    {
        foreach (var pt in points)
            if (pt.Map == mapNum && WithinRadius(x, y, pt.X, pt.Y)) return true;
        return false;
    }

    // Warn non-participants standing in the territory when setup begins — a courtesy so they can leave.
    // The System channel (not War) so a non-participant with the War channel muted still sees it.
    private void WarnNonParticipantsPresent(TerritoryContest c)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying || c.Participants.Contains(sp.Guild)) continue;
            if (!IsInTerritory(i, c.TerritoryIndex)) continue;
            _dispatcher.SendLocalizedChatTo(i, ServerStrings.GuildTerritory_NonParticipantWarning,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.System), ("Territory", TerritoryNameOf(c)));
        }
    }

    // ── Combat-integration queries (called by CombatSystem) ───────────────────
    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are opponents in a live contest — both
    /// participants of the same contest, in different guilds, both standing in the territory, during the
    /// CONTEST phase. The war-combat ruleset (durability-only) then applies to their fight.</summary>
    public bool AreContestOpponents(int a, int b)
    {
        int ga = _pm[a].Guild, gb = _pm[b].Guild;
        if (ga <= 0 || gb <= 0 || ga == gb) return false;
        foreach (var c in _contests)
        {
            if (c.Phase != ContestPhase.Contest) continue;
            if (!c.Participants.Contains(ga) || !c.Participants.Contains(gb)) continue;
            if (IsInTerritory(a, c.TerritoryIndex) && IsInTerritory(b, c.TerritoryIndex)) return true;
        }
        return false;
    }

    /// <summary>True if both are participants of the same contest standing in its territory during SETUP or
    /// COOLDOWN — PvP between participants is suppressed in those phases.</summary>
    public bool IsContestPvpSuppressed(int a, int b)
    {
        int ga = _pm[a].Guild, gb = _pm[b].Guild;
        if (ga <= 0 || gb <= 0) return false;
        foreach (var c in _contests)
        {
            if (c.Phase == ContestPhase.Contest) continue;   // combat is allowed during the contest itself
            if (!c.Participants.Contains(ga) || !c.Participants.Contains(gb)) continue;
            if (IsInTerritory(a, c.TerritoryIndex) && IsInTerritory(b, c.TerritoryIndex)) return true;
        }
        return false;
    }

    /// <summary>The territory index of the live contest this player is participating in AND standing in, else
    /// 0 — used to mark a territory war-death (its respawn area + readout).</summary>
    public int ContestTerritoryOf(int playerIndex)
    {
        int g = _pm[playerIndex].Guild;
        if (g <= 0) return 0;
        foreach (var c in _contests)
            if (c.Participants.Contains(g) && IsInTerritory(playerIndex, c.TerritoryIndex)) return c.TerritoryIndex;
        return 0;
    }

    /// <summary>True if this player is a participant of a live contest in its CONTEST phase AND standing in the
    /// territory — a war combatant with offensive license there, so striking anyone (a non-participant included)
    /// carries no aggressor/attack penalty. Scoped to the contest window: the setup/cooldown truce
    /// already bars participant-vs-participant PvP, and the no-penalty license does not apply then.</summary>
    public bool IsActiveContestParticipant(int playerIndex)
    {
        int g = _pm[playerIndex].Guild;
        if (g <= 0) return false;
        foreach (var c in _contests)
        {
            if (c.Phase == ContestPhase.Contest && c.Participants.Contains(g) && IsInTerritory(playerIndex, c.TerritoryIndex))
                return true;
        }

        return false;
    }

    /// <summary>Pick a random walkable tile in a territory's maps — the war-death respawn area.
    /// Returns false if the territory has no walkable tile.</summary>
    public bool TerritoryRespawnTile(int territoryIndex, out int map, out int x, out int y)
    {
        map = x = y = 0;
        var maps = TerritoryMaps(territoryIndex);
        for (int attempt = 0; attempt < maps.Count; attempt++)
        {
            int mapNum = maps[Rng.Next(maps.Count)];
            if (TryPickWalkable(mapNum, out x, out y))
            {
                map = mapNum;
                return true;
            }
        }
        return false;
    }

    private bool IsInTerritory(int playerIndex, int territoryIndex)
    {
        var sp = _pm[playerIndex];
        if (!sp.IsPlaying) return false;
        int m = sp.Char.Map;
        return m >= 1 && m <= Constants.MaxMaps && _world.Maps[m].MapGroup == territoryIndex;
    }

    private string TerritoryNameOf(TerritoryContest c) =>
        _world.MapGroups.GetValueOrDefault(c.TerritoryIndex) is { } g ? TerritoryName(g) : "";

    private void AnnounceResults(MapGroupRecord group, int oldOwner, int winner,
                                 List<int> challengers, bool retained, bool hadChallengers)
    {
        string terrName = TerritoryName(group);

        if (winner > 0 && winner != oldOwner)                       // captured (lone claimant on unclaimed/abandoned)
            GuildWarNotice(winner, ServerStrings.GuildTerritory_ResultWon, ("Territory", terrName));
        else if (retained && hadChallengers)                        // defended against challengers
            GuildWarNotice(winner, ServerStrings.GuildTerritory_ResultDefended, ("Territory", terrName));

        if (oldOwner > 0 && winner == 0)                            // abandoned territory fell unclaimed
            GuildWarNotice(oldOwner, ServerStrings.GuildTerritory_ResultAbandonedLost, ("Territory", terrName));

        foreach (int c in challengers)
        {
            if (c != winner)                                        // a losing challenger
                GuildWarNotice(c, ServerStrings.GuildTerritory_ResultLost, ("Territory", terrName));
        }
    }

    // ── Officer-request queue (mirrors GuildWarSystem's) ──────────────────────
    private void QueueChallengeRequest(GuildRecord guild, MapGroupRecord terr, ServerPlayer requester, int requesterIndex)
    {
        var result = GuildWarFormulas.TryQueueRequest(guild, GuildWarRequestKind.TerritoryChallenge, terr.Index,
            TerritoryName(terr), requester.Login, requester.Char.TrimmedName, UtcNow(), Constants.GuildWarMaxPendingRequests);
        switch (result)
        {
            case WarRequestQueueResult.AlreadyPending:
                Notify(requesterIndex, ServerStrings.GuildWar_RequestAlreadyPending);
                return;
            case WarRequestQueueResult.Full:
                Notify(requesterIndex, ServerStrings.GuildWar_RequestsFull);
                return;
        }
        _guilds.SaveGuild(guild);
        _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, ServerStrings.GuildTerritory_OfficerReqChallenge,
            new ChatMetadata(GameColor.BrightCyan, ChatChannel.GuildOfficer),
            ("Name", requester.Char.TrimmedName), ("Territory", TerritoryName(terr)));
        NotifyOk(requesterIndex, ServerStrings.GuildWar_RequestSent);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string TerritoryName(MapGroupRecord g) =>
        string.IsNullOrWhiteSpace(g.DisplayName) ? g.Name : g.DisplayName.Trim();

    private string OwnerName(MapGroupRecord g) => _guilds.GuildById(g.ControllingGuild)?.Name ?? "";

    private void SaveMapGroup(MapGroupRecord group) =>
        _bg.Run(_persistence.SaveMapGroupAsync(group.Index, group.Clone()), nameof(IPersistenceService.SaveMapGroupAsync));

    private void AnnounceWarPublic(string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToAll(key, new ChatMetadata(GameColor.BrightRed, ChatChannel.War), args);

    // Per-guild territory results on the private Guild War channel.
    private void GuildWarNotice(int guildId, string key, params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToGuild(guildId, key, new ChatMetadata(GameColor.BrightCyan, ChatChannel.GuildWar), args);
}
