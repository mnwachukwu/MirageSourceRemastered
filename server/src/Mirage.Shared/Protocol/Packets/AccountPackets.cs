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

    /// <summary>One selectable class: its name, sprite, starting stat spread, pitch, and the loadout a
    /// new character of it would be created with.
    ///
    /// <para>The loadout is carried as ITEM AND SPELL NUMBERS, resolved against the catalog on
    /// <see cref="NewCharClassesPacket"/>. It is filled only on that packet — the in-game class list has
    /// no use for it and already has the whole armory to hand.</para></summary>
    public sealed record ClassData(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("spriteM")] int SpriteMale,
        [property: JsonPropertyName("spriteF")] int SpriteFemale,
        [property: JsonPropertyName("str")] int Str,
        [property: JsonPropertyName("def")] int Def,
        [property: JsonPropertyName("spd")] int Spd,
        [property: JsonPropertyName("int")] int Int,
        [property: JsonPropertyName("desc")] string Description = "",
        [property: JsonPropertyName("worn")] int[]? Worn = null,
        [property: JsonPropertyName("carried")] CarriedItem[]? Carried = null,
        [property: JsonPropertyName("spells")] int[]? Spells = null
    );

    /// <summary>A carried starting item: its number, plus the stack size for currency (0 for everything
    /// else, which is always exactly one).</summary>
    public sealed record CarriedItem(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("value")] int Value
    );
}

/// <summary>S-&gt;C: the class list for the character-creation screen, with the starting loadout each
/// class grants. Reuses <see cref="SendClassesPacket.ClassData"/> so both class screens read one shape.
///
/// <para>The loadout rides along as DEFINITIONS rather than pre-rendered text, because the screen shows
/// the real in-game item and spell tooltips on hover and those are built from the records — power, the
/// stat requirement with its class-affinity head-start, durability, MP cost. Flattened strings could not
/// feed them, and a second tooltip written for this one screen would drift from the one it imitates.
/// A player here has not joined, so the client holds no item or spell table to resolve numbers against;
/// what it gets instead is the handful of records the ten classes actually reference, deduped — a few
/// dozen entries, not the armory.</para></summary>
public sealed record NewCharClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NewCharClasses;
    [JsonPropertyName("classes")] public SendClassesPacket.ClassData[] Classes { get; init; } = [];

    /// <summary>Every item any class's loadout references, deduped and keyed by number.</summary>
    [JsonPropertyName("itemDefs")] public ItemDef[] ItemDefs { get; init; } = [];
    /// <summary>Every spell any class's loadout references, deduped and keyed by number.</summary>
    [JsonPropertyName("spellDefs")] public SpellDef[] SpellDefs { get; init; } = [];

    /// <summary>An item definition, narrowed to what the item tooltip reads. Field names match
    /// <see cref="UpdateItemPacket"/>, which is the same projection for the in-game table.</summary>
    public sealed record ItemDef(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("pic")] short Pic,
        [property: JsonPropertyName("type")] ItemType Type,
        [property: JsonPropertyName("durability")] short Durability,
        [property: JsonPropertyName("vitalAmount")] short VitalAmount,
        [property: JsonPropertyName("power")] short Power,
        [property: JsonPropertyName("levelReq")] short LevelReq,
        [property: JsonPropertyName("allowedClasses")] List<short>? AllowedClasses
    );

    /// <summary>A spell definition, narrowed to what the spell tooltip reads — MP cost, magnitude, and
    /// both gates all derive from these.</summary>
    public sealed record SpellDef(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] SpellType Type,
        [property: JsonPropertyName("vitalAmount")] short VitalAmount,
        [property: JsonPropertyName("intReq")] short IntReq,
        [property: JsonPropertyName("levelReq")] short LevelReq,
        [property: JsonPropertyName("allowedClasses")] List<short>? AllowedClasses
    );
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
