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

public sealed record SendTradePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendTrade;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("trades")] public TradeRow[] Trades { get; init; } = [];

    public sealed record TradeRow(
        [property: JsonPropertyName("giveItem")] int GiveItem,
        [property: JsonPropertyName("giveValue")] int GiveValue,
        [property: JsonPropertyName("getItem")] int GetItem,
        [property: JsonPropertyName("getValue")] int GetValue
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
        [property: JsonPropertyName("itemAmount")] short ItemAmount,
        [property: JsonPropertyName("intReq")] short IntReq
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
