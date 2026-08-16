using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Players;

public sealed class ServerPlayer
{
    // ── Persisted ────────────────────────────────────────────────────────────
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";

    // 1-based char slots: indices 1..MaxChars; index 0 unused
    public PlayerRecord[] Chars { get; } = new PlayerRecord[Constants.MaxChars + 1];

    // Account-shared bank: loaded from AccountRecord.Bank at login and written back on every character
    // save, so all characters on the account share one vault. 1-based, indices 1..MaxBankSlots.
    public PlayerInvSlot[] Bank { get; set; } = AccountRecord.NewBank();

    /// <summary>Deep copy of the bank for an off-thread save — same contract as
    /// <see cref="PlayerRecord.Clone"/>: the game thread keeps mutating the live array while the
    /// background write runs, so callers hand the saver a detached snapshot.</summary>
    public PlayerInvSlot[] CloneBank()
    {
        var c = new PlayerInvSlot[Bank.Length];
        for (int i = 0; i < Bank.Length; i++)
        {
            var s = Bank[i];
            c[i] = s is null ? null! : new PlayerInvSlot { Num = s.Num, Quantity = s.Quantity, Dur = s.Dur };
        }
        return c;
    }

    // ── Runtime (not persisted) ───────────────────────────────────────────────
    public bool IsConnected { get; set; }
    public bool IsGhost { get; set; }
    public bool InGame { get; set; }
    public string RemoteIp { get; set; } = "";

    // Set by PlayerManager.MarkDirty on any change a player could otherwise roll back by
    // hard-disconnecting before the 60 s autosave (item drop/pickup, durability break, death,
    // level-up, inventory sort). GameLoop.FlushDirtyPlayers writes the player at end of tick and
    // clears this. Transient — never persisted.
    public bool SaveDirty { get; set; }

    // Client-authoritative session locale. Default until first pre-session packet (Login/NewAccount/
    // etc.) updates from packet.Locale; mid-session changes arrive via SetLanguagePacket. Never
    // persisted — dies with the session.
    public string Language { get; set; } = "en";

    // Active char slot index (1-based, 1..MaxChars); only meaningful when InGame = true
    public int CharNum { get; set; }

    public long AttackTimer { get; set; }
    public long CombatExpiresAt { get; set; }
    public bool WasInCombat { get; set; }
    public long PvpAttackerUntil { get; set; }
    public long PkGraceUntilUtc { get; set; }
    // UTC-seconds the current session's playtime was last banked into Char.PlayTimeSeconds — set at JoinGame,
    // advanced by the periodic save + on logout. 0 = not yet in-game. Transient; the persisted per-character
    // total lives on PlayerRecord.PlayTimeSeconds.
    public long PlayTimeAnchorUtc { get; set; }
    // UTC-seconds this session began (set once at JoinGame, never re-anchored). The session length it
    // yields is what accrues into the guild active-member rolling total at logout.
    public long SessionStartUtc { get; set; }

    // Per-target guild-war diminishing returns: how farmed this player is as a war-kill target.
    // Stage 1 = fresh (full attrition value), climbing per war death of this player; it decays 1 stage per
    // GuildWarDrRecoverySeconds and recovers 1 whenever this player earns a war kill. Transient (a fresh
    // login is treated as stage 1) — DR is a short-horizon anti-farm dial, not persisted state.
    public int WarDrStage { get; set; }
    public long WarDrLastUtc { get; set; }

    // UTC-seconds mirror of AccountRecord.MutedUntilUtc — copied on login so chat handlers do an O(1)
    // check without disk I/O. The persistent value still lives on AccountRecord.
    public long MutedUntilUtc { get; set; }
    public long DataTimer { get; set; }
    public long DataBytes { get; set; }
    public long DataPackets { get; set; }

    // ── Guild & social (per-account mirror) ───────────────────────────────────
    // Mirrors of AccountRecord.Guild/GuildRank/Friends/Ignore, copied at login (like Bank and
    // MutedUntilUtc) so in-game checks are O(1) and lock-free. The persistent values live on
    // AccountRecord; any mutation updates both this mirror and the account (persisted via PlayerSaver).
    public int Guild { get; set; }
    public GuildRank GuildRank { get; set; }
    public List<string> Friends { get; set; } = new();
    public List<string> Ignore { get; set; } = new();

    /// <summary>True when this account ignores <paramref name="login"/> — the single predicate behind
    /// "an ignored account cannot reach you on any channel". Ignore is per-ACCOUNT, so this blocks every
    /// character that login owns. Evaluated per-recipient inside the chat dispatch loops, so it stays a
    /// plain allocation-free scan (these lists are short).</summary>
    public bool Ignores(string? login)
    {
        if (string.IsNullOrEmpty(login)) return false;
        for (int i = 0; i < Ignore.Count; i++)
            if (string.Equals(Ignore[i], login, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    // Mirror of AccountRecord.Mail (copied at login); mail ops update this + persist to the account.
    public List<MailMessage> Mail { get; set; } = new();

    // Mirror of AccountRecord.Outbox (copied at login); sent player-origin mail, shown "in transit" until it
    // matures. Read-only from the client's side.
    public List<MailMessage> Outbox { get; set; } = new();

    // A pending guild-join offer awaiting THIS player's response — an invite I received (I would
    // join <see cref="GuildOfferGuild"/>) or a join-request I was asked to approve (then
    // <see cref="GuildOfferOther"/>, by account login, would join). 0 = none; lazily expired
    // (checked on response). One at a time, latest wins — mirrors the party-invite pattern.
    public int GuildOfferGuild { get; set; }
    public string GuildOfferOther { get; set; } = "";
    public GuildOfferKind GuildOfferKind { get; set; }
    public long GuildOfferExpiresAt { get; set; }

    // True while the player has the marketplace panel open (set on Open, cleared on Close / leave). Drives the
    // live-listing broadcast: a change to any listing re-syncs every viewer, so browsers see it without a
    // close-reopen. A disconnect / ghost isn't viewing, so this is cleared in LeftGame.
    public bool ViewingMarket { get; set; }

    // ── Active shop/inn ───────────────────────────────────────────────────────
    // Shops are not map-bound: a shop/inn is "open" for this player only while they stand by the keeper NPC
    // that opened it. Set in PacketHandler.OpenNpcShop (shop number + the keeper's map/slot); every follow-up op
    // (buy/repair/bank/market/set-spawn) re-validates r=5 of that keeper via ActiveShop, and it's cleared on a
    // map change (MovementSystem.PlayerWarp). Transient — never persisted.
    public int ActiveShopNum { get; set; }
    public int ActiveShopKeeperMap { get; set; }
    public int ActiveShopKeeperSlot { get; set; }

    public void SetActiveShop(int shopNum, int keeperMap, int keeperSlot)
    {
        ActiveShopNum = shopNum;
        ActiveShopKeeperMap = keeperMap;
        ActiveShopKeeperSlot = keeperSlot;
    }

    public void ClearActiveShop() => SetActiveShop(0, 0, 0);

    /// <summary>The shop/inn this player currently has open, re-validated to still be within r=5 of the keeper
    /// NPC that opened it (and that the keeper still keeps it) — else 0. A shop is reachable only while
    /// standing by its keeper, never by occupying a particular map.</summary>
    public int ActiveShop(GameWorld world, int index)
    {
        if (ActiveShopNum <= 0) return 0;
        if (!world.IsNpcInInteractRange(index, Char, ActiveShopKeeperMap, ActiveShopKeeperSlot, out int keeperNpc)) return 0;
        return world.ShopAssignedToNpc(keeperNpc) == ActiveShopNum ? ActiveShopNum : 0;
    }

    public bool GettingMap { get; set; }
    // Set during login when a ghost exists for this account; used in HandleUseChar to do a takeover.
    // 0 = no pending ghost.
    public int GhostTransferSlot { get; set; }

    // Party: 1-based index of partner player; 0 = not in a party
    public int PartyPlayer { get; set; }
    public bool InParty { get; set; }
    public bool PartyStarter { get; set; }
    // Pending invite expiry stamped on the inviter only; 0 = no pending invite. The pair is dropped
    // when PartySystem.Tick sees now >= this value and the invite hasn't been accepted.
    public long PartyInviteExpiresAt { get; set; }

    // Direct player-to-player trade (mirrors the party invite fields). TradePartner is the other party during
    // both a pending invite and an active trade; InTrade distinguishes the two. TradeOffer holds the items
    // escrowed off this player until the atomic swap or a cancel returns them.
    public int TradePartner { get; set; }
    public bool InTrade { get; set; }
    public bool TradeStarter { get; set; }
    public long TradeInviteExpiresAt { get; set; }
    public bool TradeConfirmed { get; set; }
    // The escrow list lives on the PERSISTED record (Char.TradeOffer), not on this runtime object, so a crash
    // or shutdown mid-trade can't wipe items the offer already pulled out of Inv — they ride the normal
    // character save and are returned to the bag on next login (see PlayerRecord.TradeOffer /
    // TradeSystem.RecoverEscrowOnLogin). Only dereferenced for an in-game player, exactly like Char itself.
    public List<PlayerInvSlot> TradeOffer => Char.TradeOffer;

    // Combat targeting (TargetType: 0=player 1=npc 2=self 3=traversal-npc; Target: 1-based player/npc
    // index; 0 = none)
    public byte TargetType { get; set; }
    public int Target { get; set; }
    // Map of the current NPC target (TargetType==1). For player/self targets this is unused.
    // Lets the player keep an NPC targeted across a seamless border for casting.
    public int TargetMap { get; set; }
    // Identity of a targeted traversal (chasing) NPC (TargetType==3). It has no fixed slot, so it is
    // addressed by its permanent (SpawnMap, SpawnSlot); TargetMap holds its current map for range.
    public int TargetSpawnMap { get; set; }
    public int TargetSpawnSlot { get; set; }

    // The exact warp tile this player last stepped through, recorded so a chasing NPC follows the
    // SAME doorway rather than guessing one by scanning. Self-validating: a chaser only trusts it when
    // WarpFromMap is its own map AND WarpToMap is where the player now stands (stale otherwise). Set on
    // every warp-tile use; never cleared (the validation handles staleness).
    public int WarpFromMap { get; set; }
    public int WarpFromX { get; set; }
    public int WarpFromY { get; set; }
    public int WarpToMap { get; set; }
    public int WarpToX { get; set; }
    public int WarpToY { get; set; }

    // PvP damage contribution tracking — 1-based by player index; cleared when this player dies
    public int[] DamageByPlayer { get; } = new int[Constants.MaxPlayers + 1];

    /// <summary>Zero every entry in <see cref="DamageByPlayer"/>. Called on death/respawn/regen
    /// timeout — anywhere the PvP kill-credit ledger should restart.</summary>
    public void ClearDamageCredit() => Array.Clear(DamageByPlayer, 0, DamageByPlayer.Length);

    // ── Convenience ──────────────────────────────────────────────────────────

    /// <summary>Returns the active character record (Chars[CharNum]). Only call when InGame = true.</summary>
    public PlayerRecord Char => Chars[CharNum];

    // ── Playtime ───────────────────────────────────────────────────────────────

    /// <summary>Bank the current session's elapsed time into the active character's persisted total and
    /// re-anchor to <paramref name="nowUtc"/> (called by the periodic save + on logout). A no-op amount
    /// before JoinGame set the anchor.</summary>
    public void BankPlaytime(long nowUtc)
    {
        Char.PlayTimeSeconds += LiveSessionSeconds(nowUtc);
        PlayTimeAnchorUtc = nowUtc;
    }

    /// <summary>The active character's total playtime including the not-yet-banked current session.</summary>
    public long CharPlaytimeSeconds(long nowUtc) => Char.PlayTimeSeconds + LiveSessionSeconds(nowUtc);

    /// <summary>The account total: every character's banked playtime plus the active character's live session
    /// (only the active character accrues right now).</summary>
    public long AccountPlaytimeSeconds(long nowUtc)
    {
        long total = 0;
        for (int i = 1; i <= Constants.MaxChars; i++) total += Chars[i].PlayTimeSeconds;
        return total + LiveSessionSeconds(nowUtc);
    }

    private long LiveSessionSeconds(long nowUtc) =>
        PlayTimeAnchorUtc > 0 && nowUtc > PlayTimeAnchorUtc ? nowUtc - PlayTimeAnchorUtc : 0;

    public ServerPlayer()
    {
        for (int i = 1; i <= Constants.MaxChars; i++)
            Chars[i] = new PlayerRecord();
    }

    /// <summary>True when the player is in the game world, whether connected or a combat ghost.</summary>
    public bool IsPlaying => (IsConnected || IsGhost) && InGame;

    /// <summary>True iff <see cref="CombatExpiresAt"/> sits strictly in the future.
    /// `CombatExpiresAt == 0` means "never entered combat" — the zero check guards against
    /// a stale 0 reading as "in combat forever" once the loop's TickCount64 advances.</summary>
    public bool IsInCombat(long now) => CombatExpiresAt > 0 && now < CombatExpiresAt;

    /// <summary>True iff <see cref="PvpAttackerUntil"/> sits strictly in the future — i.e. the
    /// player currently has an active aggressor flag.  Same zero-guard as <see cref="IsInCombat"/>.</summary>
    public bool IsAggressor(long now) => PvpAttackerUntil > 0 && now < PvpAttackerUntil;

    /// <summary>Convert the runtime <see cref="PvpAttackerUntil"/> (TickCount64 ms) to a UTC-seconds
    /// expiry suitable for the wire.  Returns 0 when the flag is inactive so the client clears its
    /// flashing-red overlay.  The 1 s rounding is deliberate — clients render once per frame and
    /// don't need sub-second precision.</summary>
    public long ToAggressorUntilUtc(long now, long nowUtc) =>
        PvpAttackerUntil > now ? nowUtc + (PvpAttackerUntil - now) / 1000 : 0;

    /// <summary>Clock-reading shortcut over <see cref="ToAggressorUntilUtc"/> for the many
    /// <c>PacketBuilder.PlayerData</c> broadcasts that don't already have <c>now</c>/<c>nowUtc</c>
    /// in scope.  Always pass this on any PlayerData broadcast so the wire field stays correct —
    /// passing the default 0 would silently clear the client's flashing-name overlay any time an
    /// unrelated event re-broadcast the player record.</summary>
    public long AggressorUntilUtcNow
    {
        get
        {
            // Deliberately reads the machine clock rather than an injected IClock: ServerPlayer is a
            // plain state record living in a fixed array, so threading a clock into every slot would
            // cost more than it buys — and this is a UNIT CONVERSION, not a rule. PvpAttackerUntil is
            // a monotonic tick deadline; the wire field is a UTC second, so this restates the same
            // remaining duration in the other unit. Nothing here decides anything a test would assert.
            long now = Environment.TickCount64;
            return PvpAttackerUntil > now
                ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (PvpAttackerUntil - now) / 1000
                : 0;
        }
    }
}
