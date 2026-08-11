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

/// <summary>Creating, disbanding, and configuring a guild — name, MOTD, labels, colour, and the
/// open-for-membership and overhead-rank toggles — plus the serialized per-guild file writes
/// every mutation lands through.</summary>
public sealed partial class GuildSystem : GameSystem
{
    // ── Guild file writes ─────────────────────────────────────────────────────────

    /// <summary>Delete a guild's file, serialized after any pending saves for that id — so a still-in-
    /// flight save can't resurrect a just-disbanded guild.</summary>
    private void DeleteGuild(int index) =>
        ChainGuildWrite(index, () => _persistence.DeleteGuildAsync(index));

    // Chain an off-thread file op after any prior write to this guild id (save or delete).
    private void ChainGuildWrite(int index, Func<Task> op)
    {
        Task prior = _guildWriteChains.TryGetValue(index, out var t) ? t : Task.CompletedTask;

        async Task Run()
        {
            try { await prior.ConfigureAwait(false); }
            catch { /* prior write failed and logged; keep the chain going */ }
            try { await op().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Guild write failed for guild {Index}", index); }
        }

        _guildWriteChains[index] = Task.Run(Run);
    }

    /// <summary>Await all pending guild writes. Call at shutdown, after the game loop has stopped.</summary>
    public Task DrainAsync() => Task.WhenAll(_guildWriteChains.Values.ToArray());

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Found a new guild named <paramref name="name"/> for the player at slot
    /// <paramref name="index"/>. Server-authoritative: re-checks eligibility, name uniqueness, and
    /// funds, deducts the creation cost (a sink), then creates + persists the guild with the founder
    /// as Leader. The name is assumed already format-validated by the caller (HandleGuildCreate).</summary>
    public void CreateGuild(int index, string name)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;

        // Only Player-access accounts may be in a guild (Monitor+ cannot). Access is per-account,
        // mirrored onto the active char's runtime Access at login, so this reads account-wide.
        if (sp.Char.Access > AdminLevel.Player)
        {
            Notify(index, ServerStrings.Guild_AdminCannotJoin);
            return;
        }
        if (sp.Guild != 0)
        {
            Notify(index, ServerStrings.Guild_AlreadyInOne);
            return;
        }
        if (GuildByName(name) is not null)
        {
            Notify(index, ServerStrings.Guild_NameTaken);
            return;
        }
        if (ItemSystem.HasItem(sp.Char, _world.Items, Constants.GoldItemIndex) < Constants.GuildCreationCost)
        {
            Notify(index, ServerStrings.Guild_NeedGold, ("Cost", Constants.GuildCreationCost));
            return;
        }

        // Charge the creation cost (consumed — a sink; the new guild's vault starts empty).
        _items.TakeItem(index, Constants.GoldItemIndex, Constants.GuildCreationCost);

        int id = AllocateGuildIndex();
        var guild = new GuildRecord { Index = id, Name = name, FoundingWeekday = Clock.LocalNow.DayOfWeek };
        guild.Members.Add(new GuildMember
        {
            Login = sp.Login,
            Rank = GuildRank.Leader,
            LastSeenUtc = 0,   // no logout recorded yet; online-ness is derived live
            CharName = sp.Char.TrimmedName,
            CharClass = sp.Char.Class,
            CharLevel = sp.Char.Level,
        });
        _world.Guilds[id] = guild;

        // Set the founder's per-account membership (mirror now, persisted through the account chain).
        sp.Guild = id;
        sp.GuildRank = GuildRank.Leader;
        _saver.MutateAccountInBackground(sp.Login, a => { a.Guild = id; a.GuildRank = GuildRank.Leader; });

        SaveGuild(guild);
        BroadcastPlayerGuild(index);

        _dispatcher.SendLocalizedChatToAll(ServerStrings.Guild_Founded,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Name", sp.Char.TrimmedName), ("GuildName", name));
        _logger.LogInformation("{Player} founded guild {Guild} (#{Id}).", sp.Char.TrimmedName, name, id);
    }

    /// <summary>Dissolve the leader's guild. Leader-only, and only when no other members remain
    /// (any vault funds are simply forfeited). Removes the guild + its file, clears the leader's
    /// membership, and announces the disband.</summary>
    public void DisbandGuild(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;

        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_DisbandNotLeader);
            return;
        }
        if (guild.Members.Count > 1)
        {
            Notify(index, ServerStrings.Guild_DisbandHasMembers);
            return;
        }

        string guildName = guild.Name;
        int id = guild.Index;

        // Disbanding mid-war = forfeit: the opponent wins each active war decisively, taking any wager
        // pot (their own ante back + our escrowed stake), before the guild dissolves. Done via the shared static
        // helpers (no GuildWarSystem dependency -> no cycle); GuildSystem owns the announce path directly.
        foreach (var war in guild.Wars.ToList())
        {
            var opponent = GuildById(war.OpponentIndex);
            if (opponent is null) continue;
            long pot = GuildWarFormulas.SettleWagerPot(opponent, guild, opponent);   // opponent wins, pre-unlink
            GuildWarFormulas.Unlink(guild, opponent);
            SaveGuild(opponent);   // persists + broadcasts the opponent's War panel (the war is gone)
            _dispatcher.SendLocalizedChatToAll(ServerStrings.GuildWar_WonForfeit,
                new ChatMetadata(GameColor.War, ChatChannel.War), ("Guild1", opponent.Name), ("Guild2", guildName));
            if (pot > 0)
            {
                _dispatcher.SendLocalizedChatToGuild(opponent.Index, ServerStrings.GuildWar_WonPot,
                    new ChatMetadata(GameColor.Guild, ChatChannel.Guild), ("GuildName", guildName), ("Gold", pot));
            }
        }

        // A dissolved guild also gives up its territory: what it controlled falls unclaimed and any challenge it
        // registered is withdrawn. Operates on the map groups directly — GuildSystem takes no GuildTerritorySystem
        // dependency (that would be a cycle), the same reason the war forfeits above go through static helpers.
        foreach (var group in _world.MapGroups.Values)
        {
            if (group.Territory && ReleaseTerritory(group, id))
                SaveMapGroup(group);
        }

        _world.Guilds.Remove(id);
        DeleteGuild(id);

        // Clear the (sole) member's account membership.
        sp.Guild = 0;
        sp.GuildRank = GuildRank.None;
        _saver.MutateAccountInBackground(sp.Login, a => { a.Guild = 0; a.GuildRank = GuildRank.None; });
        BroadcastPlayerGuild(index);

        _dispatcher.SendLocalizedChatToAll(ServerStrings.Guild_Disbanded,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice), ("GuildName", guildName));
        _logger.LogInformation("{Player} disbanded guild {Guild} (#{Id}).", sp.Char.TrimmedName, guildName, id);
    }

    /// <summary>Release everything the guild at <paramref name="guildIndex"/> holds on one territory group: its
    /// ownership falls unclaimed (weeks-held resets with it — a consecutive-hold streak is meaningless without an
    /// owner) and its pending challenge, if any, is dropped. Returns whether the group actually changed, so the
    /// caller persists only what it touched. Pure — mutates <paramref name="group"/> alone; exposed for tests.</summary>
    public static bool ReleaseTerritory(MapGroupRecord group, int guildIndex)
    {
        bool changed = group.Challengers.Remove(guildIndex);
        if (group.ControllingGuild == guildIndex)
        {
            group.ControllingGuild = 0;
            group.WeeksHeld = 0;
            changed = true;
        }
        return changed;
    }

    /// <summary>Leader-only: open or close the guild to join-requests. Open = the request flow and
    /// the open-guild browser accept it; closed = invite-only.</summary>
    public void SetOpenForMembership(int index, bool open)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        if (guild.OpenForMembership == open) return;

        guild.OpenForMembership = open;
        SaveGuild(guild);
        BroadcastGuildMembers(guild.Index);
        NotifyOk(index, open ? ServerStrings.Guild_OpenedForMembership : ServerStrings.Guild_ClosedForMembership);
    }

    /// <summary>Leader sets the guild MOTD (already trimmed/validated by the handler).</summary>
    public void SetMotd(int index, string motd)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        guild.Motd = motd;
        SaveGuild(guild);
        NotifyOk(index, ServerStrings.Guild_MotdSet);
    }

    /// <summary>Leader sets the guild's descriptive labels — deduplicated, defined-only, and capped
    /// at <see cref="Constants.MaxGuildLabels"/>.</summary>
    public void SetLabels(int index, IReadOnlyList<GuildLabel> labels)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        guild.Labels = labels.Where(l => Enum.IsDefined(l)).Distinct().Take(Constants.MaxGuildLabels).ToList();
        SaveGuild(guild);
        NotifyOk(index, ServerStrings.Guild_LabelsSet);
    }

    /// <summary>Leader sets the guild's overhead color (packed 0xRRGGBB). Rejects a color the
    /// <see cref="GuildColorPolicy"/> deems reserved (a named palette color or too near one).</summary>
    public void SetColor(int index, int rgb)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        rgb &= 0xFFFFFF; // opaque 24-bit; drop any stray high/alpha bits before validating + storing
        if (GuildColorPolicy.IsReserved(rgb))
        {
            Notify(index, ServerStrings.Guild_ColorReserved);
            return;
        }
        guild.Color = rgb;
        SaveGuild(guild);
        NotifyOk(index, ServerStrings.Guild_ColorSet);
    }

    /// <summary>Leader toggle: show/hide the guild's seasonal STANDING "(N)" in the overhead name
    /// cluster for the whole guild. Only the standing is gated — an Officer/Leader's rank word shows
    /// either way. Re-broadcasts each online member's guild data so the overhead updates live.</summary>
    public void SetShowRankOverhead(int index, bool show)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null)
        {
            Notify(index, ServerStrings.Guild_NotInOne);
            return;
        }
        if (sp.GuildRank != GuildRank.Leader)
        {
            Notify(index, ServerStrings.Guild_NeedLeader);
            return;
        }
        if (guild.ShowRankOverhead == show) return;

        guild.ShowRankOverhead = show;
        SaveGuild(guild);
        BroadcastGuildMembers(guild.Index);   // push the overhead change to every online member's observers
        NotifyOk(index, show ? ServerStrings.Guild_StandingOverheadOn : ServerStrings.Guild_StandingOverheadOff);
    }

    // ── Guild chat ─────────────────────────────────────────────────────────────────

    /// <summary>Route a guild-chat line to the whole guild (or, when <paramref name="officer"/>, just the
    /// leader + officers) with the right decorator and rank preface. A guildless sender is a silent no-op
    /// (the client guards too); the officer channel requires Officer+. Recipients who ignore the speaker's
    /// account are skipped by the dispatch (via <see cref="ChatMetadata.SpeakerLogin"/>).</summary>
    public void GuildChat(int index, string msg, bool officer)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var guild = GuildOf(sp);
        if (guild is null) return;                               // guildless: silent no-op
        if (officer && sp.GuildRank < GuildRank.Officer)
        {
            Notify(index, ServerStrings.Guild_NeedOfficer);
            return;
        }  // officer channel = leader/officers only

        long nowUtc = NowUtc;
        bool showAsPk = sp.Char.IsPk(nowUtc) && sp.PkGraceUntilUtc <= nowUtc;
        string name = sp.Char.TrimmedName;
        bool ranked = sp.GuildRank > GuildRank.Member;           // only a Leader/Officer gets a rank preface
        string rankWord = sp.GuildRank switch
        {
            GuildRank.Leader => ServerStrings.Get(ServerStrings.Guild_RankLeader),
            GuildRank.Officer => ServerStrings.Get(ServerStrings.Guild_RankOfficer),
            _ => "",
        };
        int color = officer ? GameColor.GuildOfficer : GameColor.Guild;
        var channel = officer ? ChatChannel.GuildOfficer : ChatChannel.Guild;
        var meta = new ChatMetadata(color, channel, name, sp.Char.Access, showAsPk, sp.Login);
        string key = officer
            ? (ranked ? ServerStrings.GuildOfficer_ChatSayRanked : ServerStrings.GuildOfficer_ChatSay)
            : (ranked ? ServerStrings.Guild_ChatSayRanked : ServerStrings.Guild_ChatSay);

        if (officer)
            _dispatcher.SendLocalizedChatToGuildOfficers(guild.Index, key, meta, ("Rank", rankWord), ("Name", name), ("Msg", msg));
        else
            _dispatcher.SendLocalizedChatToGuild(guild.Index, key, meta, ("Rank", rankWord), ("Name", name), ("Msg", msg));
    }
}
