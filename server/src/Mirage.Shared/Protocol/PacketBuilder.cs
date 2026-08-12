using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Shared.Protocol;

/// <summary>
/// Factory methods that construct typed packet POCOs from game state values.
/// Pass the returned packet to PacketSerializer.Serialize() for sending.
/// </summary>
public static class PacketBuilder
{
    // ── Account ──────────────────────────────────────────────────────────────

    public static AlertMsgPacket Alert(string message) =>
        new() { Message = message };

    public static AlertMsgPacket Alert(string message, AlertCode code) =>
        new() { Message = message, Code = code };

    public static SendClassesPacket SendClasses(IEnumerable<ClassRecord> classes) =>
        new()
        {
            Classes = classes.Select((c, i) => new SendClassesPacket.ClassData(
                c.Name, c.Sprite, c.Str, c.Def, c.Spd, c.Int)).ToArray()
        };

    public static SendCharsPacket SendChars(IEnumerable<PlayerRecord?> chars, ClassRecord[] classes) =>
        new()
        {
            Chars = chars.Select(p =>
            {
                if (p is null || string.IsNullOrWhiteSpace(p.Name))
                    return new SendCharsPacket.CharSlot("", 0, 0, 0, "");
                string cls = p.Class > 0 && p.Class < classes.Length && classes[p.Class]?.Name is { Length: > 0 } n
                    ? n.Trim() : "";
                return new SendCharsPacket.CharSlot(p.Name, p.Level, p.Class, p.Sprite, cls);
            }).ToArray()
        };

    // ── Game state ───────────────────────────────────────────────────────────

    public static WelcomePacket Welcome(int index) => new() { Index = index };
    public static PlayerInGamePacket PlayerInGame() => new();
    public static LeftGamePacket LeftGame(int index) => new() { Index = index };

    public static SendPlayerDataPacket PlayerData(int index, PlayerRecord p, int mapNum,
        long graceUntilUtc = 0, long aggressorUntilUtc = 0,
        int? guildId = null, GuildRank? guildRank = null, string? guildName = null, bool? guildOpen = null,
        int? guildColor = null, bool? guildShowRank = null, int? guildStanding = null) =>
        new()
        {
            Index = index,
            Name = p.Name,
            Sprite = p.Sprite,
            X = p.X,
            Y = p.Y,
            Dir = p.Dir,
            Layer = p.Layer,
            Map = mapNum,
            Level = p.Level,
            Class = p.Class,
            Sex = p.Sex,
            Access = p.Access,
            PkExpiryUtc = p.PkExpiryUtc,
            GraceUntilUtc = graceUntilUtc,
            AggressorUntilUtc = aggressorUntilUtc,
            GuildId = guildId,
            GuildRank = guildRank,
            GuildName = guildName,
            GuildOpen = guildOpen,
            GuildColor = guildColor,
            GuildShowRank = guildShowRank,
            GuildStanding = guildStanding,
            Dead = p.Dead,
            RespawnReadyUtc = p.RespawnReadyUtc,
        };

    public static AggressorRefreshPacket AggressorRefresh(int index, long aggressorUntilUtc) =>
        new() { Index = index, AggressorUntilUtc = aggressorUntilUtc };

    // ── Map ──────────────────────────────────────────────────────────────────

    // Sends the map's RAW inheritable fields — 0 / null mean "inherit from the MapGroup". Both the
    // editor and the game client resolve the effective value themselves against their cached group (the client
    // caches groups from SendMapGroupsPacket / UpdateMapGroupPacket, then resolves via MapGroupResolve). This is
    // why a group edit needs no map re-send: the map packet never carries a group-derived value to go stale.
    // forEditor = true also carries the NPC entries' authoring pins + the greeting (the game client renders NPCs
    // from live spawn packets and the server speaks the greeting, so both stay off the game-client wire).
    public static SendMapPacket SendMap(int mapNum, MapRecord map, int col = 1, int row = 1, bool forEditor = false)
    {
        var tiles = new List<SendMapPacket.TileData>();
        for (int x = 0; x <= Constants.MaxMapX; x++)
        {
            for (int y = 0; y <= Constants.MaxMapY; y++)
            {
                var t = map.Tile[x, y];
                // Sparse: omit fully-default tiles; the client rebuilds from a blank grid.
                if (!SendMapPacket.TileData.IsDefault(t))
                    tiles.Add(SendMapPacket.TileData.From(x, y, t));
            }
        }

        return new SendMapPacket
        {
            MapNum = mapNum,
            Col = col,
            Row = row,
            Revision = map.Revision,
            Name = map.Name,
            DisplayName = map.DisplayName,
            // Raw inheritable fields (0 / null = inherit); resolved client-side against the cached group.
            Moral = map.Moral,
            Music = map.Music,
            BootMap = map.BootMap,
            BootX = map.BootX,
            BootY = map.BootY,
            Indoors = map.Indoors,
            AlwaysDark = map.AlwaysDark,
            MapGroup = map.MapGroup,
            Up = map.Up,
            Down = map.Down,
            Left = map.Left,
            Right = map.Right,
            Tiles = tiles.ToArray(),
            // Editor gets full entries (pins for the authoring round-trip); the game client gets the same NPC
            // types with pins stripped — it renders NPCs from live spawn packets and never needs the pins.
            Npcs = forEditor
                ? map.Npcs.ToArray()
                : map.Npcs.Select(e => e with { PinX = null, PinY = null }).ToArray(),
            Lights = map.Lights.ToArray(),
            // Editor-only authoring data — the SERVER speaks the map greeting from its own MapRecord (client never needs it).
            GreetingSpeaker = forEditor ? map.GreetingSpeaker : "",
            JoinSay = forEditor ? map.JoinSay : "",
            LeaveSay = forEditor ? map.LeaveSay : "",
        };
    }

    public static JoinMapPacket JoinMap(int index) => new() { Index = index };
    public static LeaveMapPacket LeaveMap(int index) => new() { Index = index };
    public static PlayerXYPacket PlayerXY(int index, int x, int y) => new() { Index = index, X = x, Y = y };

    // ── Movement ─────────────────────────────────────────────────────────────

    public static SendPlayerMovePacket PlayerMove(
        int index, int x, int y, Direction dir, MovementType movement, WorldLayer layer = WorldLayer.Ground) =>
        new() { Index = index, X = x, Y = y, Dir = dir, Movement = movement, Layer = layer };

    // ── Vitals ───────────────────────────────────────────────────────────────

    public static SendHpPacket SendHp(int index, int hp, int maxHp,
        bool showFloat = false, bool isCrit = false, int damage = 0, int msSinceCombat = int.MaxValue) =>
        new() { Index = index, Hp = hp, MaxHp = maxHp, ShowFloat = showFloat, IsCrit = isCrit, Damage = damage, MsSinceCombat = msSinceCombat };

    /// <summary>
    /// Converts a server-side <paramref name="combatExpiresAt"/> to wire-format ms elapsed since the
    /// combat window opened.  Returns <see cref="int.MaxValue"/> when not in combat.  Used by every
    /// sync packet that carries combat state (PartyVitals, SendHp, MapNpcs, TraversalNpc) so the
    /// client can compute the right LastCombatMs stamp on its own clock instead of restarting the
    /// 10s window each time it re-observes an entity.
    /// </summary>
    public static int MsSinceCombat(long combatExpiresAt, long nowMs, long combatDurationMs) =>
        (combatExpiresAt > 0 && nowMs < combatExpiresAt)
            ? (int)(combatDurationMs - (combatExpiresAt - nowMs))
            : int.MaxValue;

    public static SendMpPacket SendMp(int index, int mp, int maxMp,
        bool showFloat = false) =>
        new() { Index = index, Mp = mp, MaxMp = maxMp, ShowFloat = showFloat };

    public static SendSpPacket SendSp(int index, int sp, int maxSp,
        bool showFloat = false) =>
        new() { Index = index, Sp = sp, MaxSp = maxSp, ShowFloat = showFloat };

    public static SendStatsPacket SendStats(PlayerRecord p) =>
        new()
        {
            Str = p.Str,
            Def = p.Def,
            Spd = p.Spd,
            Int = p.Int,
            Points = p.Points,
            Level = p.Level,
            Exp = p.Exp,
        };

    /// <summary>
    /// Snapshot of a partnered player pushed to the partner.  <paramref name="combatExpiresAt"/> = 0
    /// (or in the past) means "not in combat" and lands as int.MaxValue on the wire — the client
    /// converts to its own clock and runs the existing 10 s combat-window check.
    /// </summary>
    public static PartyVitalsPacket PartyVitals(int index, PlayerRecord p, long combatExpiresAt,
        long pkGraceUntilUtc, long nowMs, long combatDurationMs)
    {
        long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool showAsPk = p.IsPk(nowUtc) && pkGraceUntilUtc <= nowUtc;
        int msSince = MsSinceCombat(combatExpiresAt, nowMs, combatDurationMs);
        return new()
        {
            Index = index,
            Name = p.TrimmedName,
            Level = p.Level,
            Hp = p.Hp, MaxHp = p.MaxHp,
            Mp = p.Mp, MaxMp = p.MaxMp,
            Sp = p.Sp, MaxSp = p.MaxSp,
            MapNum = p.Map, X = p.X, Y = p.Y,
            ShowAsPk = showAsPk,
            Access = p.Access,
            MsSinceCombat = msSince,
        };
    }

    /// <summary>Empty-name notify that tells the recipient to tear down their party overlay.</summary>
    public static PartyVitalsPacket PartyCleared() => new();

    // ── Items ────────────────────────────────────────────────────────────────

    public static SendItemsPacket SendItems(IEnumerable<(int num, ItemRecord item)> items) =>
        new()
        {
            Items = items.Select(x => new SendItemsPacket.ItemData(
                x.num, x.item.Name, x.item.Pic, x.item.Type,
                x.item.Durability, x.item.VitalAmount, x.item.SpellNum, x.item.Power,
                // Copied, not aliased: a packet outlives this call and the record stays editable.
                x.item.AllowedClasses is null ? null : new List<short>(x.item.AllowedClasses),
                x.item.NonTradeable, x.item.NonListable, x.item.NonMailable, x.item.DestroyOnDrop)).ToArray()
        };

    public static UpdateItemPacket UpdateItem(int itemNum, ItemRecord item) =>
        new()
        {
            ItemNum = itemNum,
            Name = item.Name,
            Pic = item.Pic,
            Type = item.Type,
            Durability = item.Durability,
            VitalAmount = item.VitalAmount,
            SpellNum = item.SpellNum,
            Power = item.Power,
            AllowedClasses = item.AllowedClasses is null ? null : new List<short>(item.AllowedClasses),
            NonTradeable = item.NonTradeable,
            NonListable = item.NonListable,
            NonMailable = item.NonMailable,
            DestroyOnDrop = item.DestroyOnDrop,
        };

    // ── Chat ─────────────────────────────────────────────────────────────────

    public static ChatMsgPacket ChatMsg(string msg, int color, ChatChannel channel) =>
        new() { Msg = msg, Color = color, Channel = channel };

    /// <summary>Player-originated chat overload. Carries speaker identity so the client can color
    /// the name and attach a right-click span. ShowAsPk is frozen at send time.</summary>
    public static ChatMsgPacket ChatMsg(string msg, int color, ChatChannel channel, string speakerName, AdminLevel speakerAccess, bool speakerShowAsPk) =>
        new()
        {
            Msg = msg,
            Color = color,
            Channel = channel,
            SpeakerName = speakerName,
            SpeakerAccess = speakerAccess,
            SpeakerShowAsPk = speakerShowAsPk,
        };

    public static ChatBubblePacket ChatBubble(int playerIndex, string msg, byte kind) =>
        new() { PlayerIndex = playerIndex, Msg = msg, Kind = kind };

    public static NpcChatBubblePacket NpcChatBubble(int mapNum, int npcSlot, string msg, byte kind) =>
        new() { MapNum = mapNum, NpcSlot = npcSlot, Msg = msg, Kind = kind };

    public static NpcChatBubblePacket TraversalNpcChatBubble(int spawnMap, int spawnSlot, string msg, byte kind) =>
        new() { NpcSlot = 0, SpawnMap = spawnMap, SpawnSlot = spawnSlot, Msg = msg, Kind = kind };

    // ── Weather / time ───────────────────────────────────────────────────────

    public static PlayersOnlinePacket PlayersOnline(int count) => new() { Count = count };
    public static WeatherPacket Weather(WeatherType weather) => new() { Weather = weather };
    public static TimeOfDayPacket TimeOfDay(TimePhase phase, float progress) => new() { Phase = phase, Progress = progress };
}
