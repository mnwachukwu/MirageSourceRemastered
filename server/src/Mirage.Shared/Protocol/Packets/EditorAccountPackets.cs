using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// The editor's account family — CREATOR only, and the only editor packets that describe a PERSON rather
// than a piece of content.
//
// Three things are absent on purpose and must stay absent:
//
//   The PASSWORD. It is never read into these shapes, never sent, and never round-tripped on save. The
//   editor cannot show what it never receives, and a save that carried one back could overwrite a
//   credential with whatever the form happened to hold.
//
//   The MODERATION timers and the ban list. Those are an operator's job, done from the server window; an
//   account manager that could also punish would put the same decision in two places with two audit
//   trails. See the moderation work for where they live.
//
//   GUILD MEMBERSHIP is sent but never accepted back. GuildRecord.Members is a roster cache kept in step
//   at every mutation, so writing Account.Guild directly would desync the guild from its own roster.
//   Moving somebody between guilds has to go through GuildSystem.

/// <summary>C→S: one page of the account browser. Search matches the login.</summary>
public sealed record EditorRequestAccountsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAccounts;
    [JsonPropertyName("search")] public string Search { get; init; } = "";
    /// <summary>Keep only this access level; null for every level. Costs the server a full scan, because
    /// the level is inside each record rather than in its file name.</summary>
    [JsonPropertyName("access")] public AdminLevel? Access { get; init; }
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("pageSize")] public int PageSize { get; init; } = 25;
}

/// <summary>S→C: the requested page, plus the total that matched so the browser can size its pager.</summary>
public sealed record EditorAccountListPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAccountList;
    [JsonPropertyName("accounts")] public List<EditorAccountRow> Accounts { get; init; } = new();
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("pageSize")] public int PageSize { get; init; }
}

/// <summary>One row of the browser. <see cref="IsOnline"/> is live game state, so it is only as fresh as
/// the last request — the browser re-asks while it is open.</summary>
public sealed record EditorAccountRow
{
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    [JsonPropertyName("online")] public bool IsOnline { get; init; }
    /// <summary>The character they are playing right now, when they are. Empty otherwise.</summary>
    [JsonPropertyName("playingAs")] public string PlayingAs { get; init; } = "";
    [JsonPropertyName("chars")] public List<string> CharNames { get; init; } = new();
}

/// <summary>C→S: the full record for one account.</summary>
public sealed record EditorRequestAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAccount;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
}

/// <summary>S→C: one account's editable state, plus the read-only facts worth seeing beside it.</summary>
public sealed record EditorAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAccount;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    [JsonPropertyName("online")] public bool IsOnline { get; init; }
    /// <summary>Read-only. Shown so a Creator can see it; changing it is GuildSystem's job.</summary>
    [JsonPropertyName("guild")] public int Guild { get; init; }
    [JsonPropertyName("guildRank")] public GuildRank GuildRank { get; init; }
    [JsonPropertyName("chars")] public List<EditorCharRow> Chars { get; init; } = new();
}

/// <summary>One character slot. Slot is 1-based and identifies the row on save; an empty
/// <see cref="Name"/> means the slot is unused and nothing else on the row means anything.</summary>
public sealed record EditorCharRow
{
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("class")] public int Class { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    /// <summary>long, matching <c>PlayerRecord.Exp</c> — an int would silently clip a high-level total.</summary>
    [JsonPropertyName("exp")] public long Exp { get; init; }
    [JsonPropertyName("map")] public int Map { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("points")] public int Points { get; init; }
}

/// <summary>
/// C→S: apply an edit. Only what a Creator may change is here — access, and the per-character fields
/// above. Everything else on the account is left exactly as it was on disk.
/// </summary>
public sealed record EditorSaveAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveAccount;
    [JsonPropertyName("login")] public string Login { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel Access { get; init; }
    [JsonPropertyName("chars")] public List<EditorCharRow> Chars { get; init; } = new();
}
