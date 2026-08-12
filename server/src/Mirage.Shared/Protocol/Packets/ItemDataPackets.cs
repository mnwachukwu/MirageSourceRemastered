using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>S-&gt;C: the full item definition table, sent once at join and cached by the client.</summary>
public sealed record SendItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendItems;
    [JsonPropertyName("items")] public ItemData[] Items { get; init; } = [];

    /// <summary>One item's definition as sent to the client.</summary>
    public sealed record ItemData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("pic")] short Pic,
        [property: JsonPropertyName("type")] ItemType Type,
        // Type-specific fields; see ItemRecord for which apply to which ItemType.
        [property: JsonPropertyName("durability")] short Durability,
        [property: JsonPropertyName("vitalAmount")] short VitalAmount,
        [property: JsonPropertyName("spellNum")] short SpellNum,
        [property: JsonPropertyName("power")] short Power,
        [property: JsonPropertyName("levelReq")] short LevelReq,
        [property: JsonPropertyName("allowedClasses")] List<short>? AllowedClasses,
        // Item restriction flags — drive the client's list/mail/drop-warning gates.
        [property: JsonPropertyName("nonTradeable")] bool NonTradeable,
        [property: JsonPropertyName("nonListable")] bool NonListable,
        [property: JsonPropertyName("nonMailable")] bool NonMailable,
        [property: JsonPropertyName("destroyOnDrop")] bool DestroyOnDrop
    );
}

/// <summary>S-&gt;C: one item's definition — the editor's request response, and the live broadcast on an editor save so clients refresh without reconnecting.</summary>
public sealed record UpdateItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateItem;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("pic")] public short Pic { get; init; }
    [JsonPropertyName("type")] public ItemType Type { get; init; }
    // Type-specific fields; see ItemRecord for which apply to which ItemType.
    [JsonPropertyName("durability")] public short Durability { get; init; }
    [JsonPropertyName("vitalAmount")] public short VitalAmount { get; init; }
    [JsonPropertyName("spellNum")] public short SpellNum { get; init; }
    [JsonPropertyName("power")] public short Power { get; init; }
    [JsonPropertyName("levelReq")] public short LevelReq { get; init; }
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    // Item restriction flags. See ItemRecord for behavior.
    [JsonPropertyName("nonTradeable")] public bool NonTradeable { get; init; }
    [JsonPropertyName("nonListable")] public bool NonListable { get; init; }
    [JsonPropertyName("nonMailable")] public bool NonMailable { get; init; }
    [JsonPropertyName("destroyOnDrop")] public bool DestroyOnDrop { get; init; }
}
