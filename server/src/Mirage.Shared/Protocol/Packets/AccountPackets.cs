using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

/// <summary>C-&gt;S: request the class list for the account and new-character screens.</summary>
public sealed record GetClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GetClasses;
}

/// <summary>C-&gt;S: create an account. <c>Locale</c> tells the server which language to reply in, since no session exists yet.</summary>
public sealed record NewAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NewAccount;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C-&gt;S: delete an account and every character on it, re-authenticating first.</summary>
public sealed record DelAccountPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DelAccount;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C-&gt;S: change an account password, authenticating with the old one.</summary>
public sealed record ChangePasswordPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ChangePassword;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("newpass")] public string NewPassword { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C-&gt;S: authenticate. Carries the client's version triple so the server can reject a mismatched build before touching the account store.</summary>
public sealed record LoginPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Login;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("maj")] public int Major { get; init; }
    [JsonPropertyName("min")] public int Minor { get; init; }
    [JsonPropertyName("rev")] public int Revision { get; init; }
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C-&gt;S: switch the language for this session, so later server text arrives localized.</summary>
public sealed record SetLanguagePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetLanguage;
    [JsonPropertyName("locale")] public string Locale { get; init; } = "";
}

/// <summary>C-&gt;S: create a character in the next free slot.</summary>
public sealed record AddCharPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AddChar;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("sex")] public Sex Sex { get; init; }
    [JsonPropertyName("class")] public int Class { get; init; }
}

/// <summary>C-&gt;S: delete the character in <c>Slot</c>.</summary>
public sealed record DelCharPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DelChar;
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

/// <summary>
/// C-&gt;S: enter the world as the character in <c>Slot</c>. <c>Locale</c> rides along for the same
/// reason the other pre-session packets carry one: handling this packet immediately produces
/// localized text (the welcome, the MOTD, the join broadcast), so the language has to be settled
/// before the handler runs. A <see cref="SetLanguagePacket"/> cannot do that job — the client only
/// learns it is in-game from the reply to this packet, a round trip after that text is on the wire.
/// </summary>
public sealed record UseCharPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UseChar;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

/// <summary>C-&gt;S: leave the world but stay logged in, returning to character select.</summary>
public sealed record LogoutToCharSelectPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.LogoutToCharSelect;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S-&gt;C: a message for the alert dialog. <c>Code</c> carries the flow-control result for auth alerts so the client branches on a stable value instead of the localized text.</summary>
public sealed record AlertMsgPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AlertMsg;
    [JsonPropertyName("msg")] public string Message { get; init; } = "";
    // Flow-control result for auth alerts; None (the default) for ordinary alerts. Lets the client
    // branch on a stable code rather than matching the localized Message text.
    [JsonPropertyName("code")] public AlertCode Code { get; init; } = AlertCode.None;
}

/// <summary>S-&gt;C: the class list, in response to <see cref="GetClassesPacket"/>.</summary>
public sealed record SendClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendClasses;
    [JsonPropertyName("classes")] public ClassData[] Classes { get; init; } = [];

    /// <summary>One selectable class: its name, sprite, and starting stat spread.</summary>
    public sealed record ClassData(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("sprite")] int Sprite,
        [property: JsonPropertyName("str")] int Str,
        [property: JsonPropertyName("def")] int Def,
        [property: JsonPropertyName("spd")] int Spd,
        [property: JsonPropertyName("int")] int Int
    );
}

/// <summary>S-&gt;C: the class list for the character-creation screen. Reuses <see cref="SendClassesPacket.ClassData"/> so both screens read one shape.</summary>
public sealed record NewCharClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NewCharClasses;
    [JsonPropertyName("classes")] public SendClassesPacket.ClassData[] Classes { get; init; } = [];
}

/// <summary>S-&gt;C: the account's character slots, for the selection screen.</summary>
public sealed record SendCharsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendChars;
    [JsonPropertyName("chars")] public CharSlot[] Chars { get; init; } = [];

    /// <summary>One character slot. <c>ClassName</c> is resolved server-side so the selection screen
    /// needs no class table of its own.</summary>
    public sealed record CharSlot(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("level")] int Level,
        [property: JsonPropertyName("class")] int Class,
        [property: JsonPropertyName("sprite")] int Sprite,
        [property: JsonPropertyName("className")] string ClassName
    );
}
