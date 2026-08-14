using Mirage.Shared;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record UseItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UseItem;
    [JsonPropertyName("slot")] public int Slot { get; init; }
}

/// <summary>C→S: tidy the sender's own inventory into the canonical sort order (see
/// <c>ItemSystem.SortInventory</c>). No payload — the server reorders the caller's bag, remaps the
/// equipped-slot indices, and resyncs.</summary>
public sealed record SortInventoryPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SortInventory;
}

public sealed record MapGetItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapGetItem;
}

public sealed record MapDropItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapDropItem;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

/// <summary>C→S: bulk drop, keyed by item id rather than a specific inventory slot. Server scans
/// inventory for matching non-currency slots (skipping equipped), clamps to the map's
/// player-dropped clutter cap, and drops up to <c>Quantity</c> items. Quantity = 0 means "as many as
/// fit". Currency uses the per-slot <see cref="MapDropItemPacket"/>.</summary>
public sealed record MapDropBulkPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapDropBulk;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}

// ── S→C ─────────────────────────────────────────────────────────────────────

public sealed record SendInventoryPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendInventory;
    [JsonPropertyName("slots")] public InvSlotData[] Slots { get; init; } = [];

    public sealed record InvSlotData(
        [property: JsonPropertyName("slot")] int Slot,
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("dur")] int Dur
    );
}

public sealed record InventoryUpdatePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.InventoryUpdate;
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
    [JsonPropertyName("dur")] public int Dur { get; init; }
}

public sealed record EquippedGearPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EquippedGear;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("armor")] public int Armor { get; init; }
    [JsonPropertyName("weapon")] public int Weapon { get; init; }
    [JsonPropertyName("helmet")] public int Helmet { get; init; }
    [JsonPropertyName("shield")] public int Shield { get; init; }
}

public sealed record MapItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapItems;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("items")] public MapItemData[] Items { get; init; } = [];

    public sealed record MapItemData(
        [property: JsonPropertyName("slot")] int Slot,
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("dur")] int Dur,
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        // Lets the client count PlayerDropped items so the inventory Drop button can disable when
        // the voluntary clutter cap is reached without also locking out drops just because NPC loot
        // happens to be on the ground. Default 0 = TileDefined for backward-compatible removals
        // (Num=0 sentinel packets don't carry meaningful Source).
        [property: JsonPropertyName("src")] ItemSource Source = ItemSource.TileDefined,
        // Two-layer world: the logical layer the drop sits on (Ground omitted on the wire). Removal sentinels
        // (Num=0) leave it default — the client removes by slot, so the layer is irrelevant there.
        [property: JsonPropertyName("layer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WorldLayer Layer = WorldLayer.Ground
    );
}
