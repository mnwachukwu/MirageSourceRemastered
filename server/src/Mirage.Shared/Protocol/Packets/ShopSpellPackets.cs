using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── Shop ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: raise the (client-local) Inn panel — the response when a player interacts with an NPC that
/// keeps an Inn. Carries the keeper's shop number so the inn's actions (set spawn / bank / market) resolve the
/// right inn from anywhere the keeper stands (shops are not map-bound).</summary>
public sealed record OpenInnPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.OpenInn;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
}

public sealed record TradeRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.TradeRequest;
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

public sealed record TradePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Trade;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("tradeSlot")] public int TradeSlot { get; init; }
}

public sealed record FixItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.FixItem;
    [JsonPropertyName("invSlot")] public int InvSlot { get; init; }
}

public sealed record SendShopsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendShops;
    [JsonPropertyName("shops")] public ShopData[] Shops { get; init; } = [];

    public sealed record ShopData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("fixes")] bool FixesItems,
        [property: JsonPropertyName("shopType")] ShopType ShopType,
        [property: JsonPropertyName("allowBanking")] bool AllowBanking
    );
}

/// <summary>S→C: everything needed to draw an open shop — its barter rows AND its sales list.
///
/// <para>The two are separate because they are different transactions, not two spellings of one. A trade row
/// names both sides explicitly and can ask for anything; a sales entry is just an item number, priced from
/// <see cref="Records.ItemRecord.Price"/>, which the client already holds from the item definitions. So the
/// sales list costs one int per entry on the wire no matter how large the shopfront gets.</para></summary>
public sealed record SendTradePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendTrade;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("trades")] public TradeRow[] Trades { get; init; } = [];
    /// <summary>Item numbers the shop sells for gold, in authored display order.</summary>
    [JsonPropertyName("sales")] public int[] Sales { get; init; } = [];

    public sealed record TradeRow(
        [property: JsonPropertyName("giveItem")] int GiveItem,
        [property: JsonPropertyName("giveQuantity")] int GiveQuantity,
        [property: JsonPropertyName("getItem")] int GetItem,
        [property: JsonPropertyName("getQuantity")] int GetQuantity
    );
}

// ── Spell ────────────────────────────────────────────────────────────────────

public sealed record SpellsRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.Spells;
}

public sealed record SendSpellsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendSpells;
    [JsonPropertyName("spells")] public SpellData[] Spells { get; init; } = [];

    public sealed record SpellData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("allowedClasses")] List<short>? AllowedClasses,
        [property: JsonPropertyName("type")] SpellType Type,
        // Type-specific fields; see SpellRecord for which apply to which SpellType.
        [property: JsonPropertyName("vitalAmount")] short VitalAmount,
        [property: JsonPropertyName("itemNum")] short ItemNum,
        [property: JsonPropertyName("itemQuantity")] short ItemQuantity,
        [property: JsonPropertyName("intReq")] short IntReq,
        [property: JsonPropertyName("levelReq")] short LevelReq
    );
}

public sealed record PlayerSpellsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerSpells;
    [JsonPropertyName("spells")] public int[] Spells { get; init; } = [];
    [JsonPropertyName("preparedSpell")] public int PreparedSpell { get; init; }
}

/// <summary>C→S: player prepared or unprepared a spell slot.</summary>
public sealed record SetPreparedSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetPreparedSpell;
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

/// <summary>C→S: player chose to forget (unlearn) a spell, freeing the slot.</summary>
public sealed record ForgetSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.ForgetSpell;
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

/// <summary>S→C: the character's whole action bar, 1-based slots 1..MaxHotkeys flattened to a 0-based
/// wire array (as PlayerSpells does with the spell book). Sent at join and echoed after every change, so
/// the client never has to guess whether its own edit was accepted.</summary>
public sealed record PlayerHotkeysPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerHotkeys;
    /// <summary>Per slot, the <see cref="Records.HotkeyKind"/> as a byte; parallel to <see cref="Nums"/>.</summary>
    [JsonPropertyName("kinds")] public byte[] Kinds { get; init; } = [];
    /// <summary>Per slot, the item or spell NUMBER — never a bag or book position.</summary>
    [JsonPropertyName("nums")] public short[] Nums { get; init; } = [];
}

/// <summary>C→S: bind or clear one action-bar slot. Kind <see cref="Records.HotkeyKind.None"/> clears it.
/// The server validates and echoes <see cref="PlayerHotkeysPacket"/>; it never trusts this to be
/// in-range or to name something real.</summary>
public sealed record SetHotkeyPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetHotkey;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("kind")] public byte Kind { get; init; }
    [JsonPropertyName("num")] public short Num { get; init; }
}
