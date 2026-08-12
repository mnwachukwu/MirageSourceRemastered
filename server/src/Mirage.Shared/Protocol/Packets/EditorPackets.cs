using Mirage.Shared;
using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S: editor session ──────────────────────────────────────────────────────

public sealed record EditorLoginPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorLogin;
    [JsonPropertyName("user")] public string Username { get; init; } = "";
    [JsonPropertyName("pass")] public string Password { get; init; } = "";
    [JsonPropertyName("locale")] public string Locale { get; init; } = "en";
}

public sealed record EditorRequestItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestItem;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
}

public sealed record EditorRequestNpcPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestNpc;
    [JsonPropertyName("npcNum")] public int NpcNum { get; init; }
}

public sealed record EditorRequestShopPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestShop;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
}

public sealed record EditorRequestSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestSpell;
    [JsonPropertyName("spellNum")] public int SpellNum { get; init; }
}

public sealed record EditorRequestMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
}

public sealed record EditorRequestClassPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestClass;
    [JsonPropertyName("classNum")] public int ClassNum { get; init; }
}

public sealed record EditorRequestMapGroupPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestMapGroup;
    [JsonPropertyName("groupNum")] public int GroupNum { get; init; }
}

public sealed record EditorRequestAllItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllItems;
}

public sealed record EditorRequestAllNpcsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllNpcs;
}

public sealed record EditorRequestAllShopsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllShops;
}

public sealed record EditorRequestAllSpellsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllSpells;
}

public sealed record EditorRequestAllClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllClasses;
}

public sealed record EditorRequestAllMapGroupsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllMapGroups;
}

public sealed record EditorSaveClassPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveClass;
    [JsonPropertyName("classNum")] public int ClassNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
}

public sealed record EditorSaveItemPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveItem;
    [JsonPropertyName("itemNum")] public int ItemNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("pic")] public short Pic { get; init; }
    [JsonPropertyName("type")] public ItemType Type { get; init; }
    // Type-specific fields; see ItemRecord for which apply to which ItemType.
    [JsonPropertyName("durability")] public short Durability { get; init; }
    [JsonPropertyName("vitalAmount")] public short VitalAmount { get; init; }
    [JsonPropertyName("spellNum")] public short SpellNum { get; init; }
    [JsonPropertyName("power")] public short Power { get; init; }
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    // Item restriction flags. See ItemRecord for behavior.
    [JsonPropertyName("nonTradeable")] public bool NonTradeable { get; init; }
    [JsonPropertyName("nonListable")] public bool NonListable { get; init; }
    [JsonPropertyName("nonMailable")] public bool NonMailable { get; init; }
    [JsonPropertyName("destroyOnDrop")] public bool DestroyOnDrop { get; init; }
}

public sealed record EditorSaveNpcPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveNpc;
    [JsonPropertyName("npcNum")] public int NpcNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("attackSay")] public string AttackSay { get; init; } = "";
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("spawnSecs")] public int SpawnSecs { get; init; }
    [JsonPropertyName("behavior")] public NpcBehavior Behavior { get; init; }
    [JsonPropertyName("group")] public int Group { get; init; }
    [JsonPropertyName("range")] public int Range { get; init; }
    [JsonPropertyName("dropChance")] public short DropChance { get; init; }
    [JsonPropertyName("dropItem")] public int DropItem { get; init; }
    [JsonPropertyName("dropValue")] public short DropValue { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
    [JsonPropertyName("extraHp")] public int ExtraHp { get; init; }
    [JsonPropertyName("isBoss")] public bool IsBoss { get; init; }
    [JsonPropertyName("emitsLight")] public bool EmitsLight { get; init; }
    [JsonPropertyName("light")] public LightSpec Light { get; init; }
}

public sealed record EditorSaveShopPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveShop;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("fixes")] public bool FixesItems { get; init; }
    [JsonPropertyName("shopType")] public ShopType ShopType { get; init; }
    [JsonPropertyName("allowBanking")] public bool AllowBanking { get; init; }
    // NPC template number this shop/inn is assigned to (0 = none) — the editor's Keeper picker. Drives the
    // client $ glyph + attack-key/right-click interact.
    [JsonPropertyName("keeper")] public int Keeper { get; init; }
    [JsonPropertyName("trades")] public TradeEntry[] Trades { get; init; } = [];

    public sealed record TradeEntry(
        [property: JsonPropertyName("giveItem")] int GiveItem,
        [property: JsonPropertyName("giveValue")] int GiveValue,
        [property: JsonPropertyName("getItem")] int GetItem,
        [property: JsonPropertyName("getValue")] int GetValue
    );
}

public sealed record EditorSaveSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveSpell;
    [JsonPropertyName("spellNum")] public int SpellNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("type")] public SpellType Type { get; init; }
    // Type-specific fields; see SpellRecord for which apply to which SpellType.
    [JsonPropertyName("vitalAmount")] public short VitalAmount { get; init; }
    [JsonPropertyName("itemNum")] public short ItemNum { get; init; }
    [JsonPropertyName("itemAmount")] public short ItemAmount { get; init; }
    [JsonPropertyName("intReq")] public short IntReq { get; init; }
}

public sealed record EditorSaveMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("map")] public SendMapPacket Map { get; init; } = null!;
}

// Editor-authored MapGroup fields. Moral + the environment bools are NULLABLE so a group can
// decline to provide one (null = inherit downstream). ControllingGuild is runtime state, NOT authored here —
// the server preserves it across a save.
public sealed record EditorSaveMapGroupPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveMapGroup;
    [JsonPropertyName("groupNum")] public int GroupNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("music")] public int Music { get; init; }
    [JsonPropertyName("moral")] public MapMoral? Moral { get; init; }
    [JsonPropertyName("indoors")] public bool? Indoors { get; init; }
    [JsonPropertyName("alwaysDark")] public bool? AlwaysDark { get; init; }
    [JsonPropertyName("bootMap")] public int BootMap { get; init; }
    [JsonPropertyName("bootX")] public int BootX { get; init; }
    [JsonPropertyName("bootY")] public int BootY { get; init; }
    // Map-enter/leave greeting fallback: a map inherits any blank greeting field from its group.
    [JsonPropertyName("greetingSpeaker")] public string GreetingSpeaker { get; init; } = "";
    [JsonPropertyName("joinSay")] public string JoinSay { get; init; } = "";
    [JsonPropertyName("leaveSay")] public string LeaveSay { get; init; } = "";
    [JsonPropertyName("territory")] public bool Territory { get; init; }
}

// ── S→C: editor session ──────────────────────────────────────────────────────

public sealed record EditorLoginResponsePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorLoginResponse;
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = "";
    [JsonPropertyName("access")] public AdminLevel AccessLevel { get; init; }
}

public sealed record EditorDataPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorData;
    [JsonPropertyName("items")] public NameEntry[] Items { get; init; } = [];
    [JsonPropertyName("npcs")] public NameEntry[] Npcs { get; init; } = [];
    [JsonPropertyName("shops")] public NameEntry[] Shops { get; init; } = [];
    [JsonPropertyName("spells")] public NameEntry[] Spells { get; init; } = [];
    [JsonPropertyName("maps")] public NameEntry[] Maps { get; init; } = [];
    [JsonPropertyName("classes")] public NameEntry[] Classes { get; init; } = [];
    [JsonPropertyName("mapGroups")] public NameEntry[] MapGroups { get; init; } = [];
    [JsonPropertyName("quests")] public NameEntry[] Quests { get; init; } = [];
    [JsonPropertyName("conversations")] public NameEntry[] Conversations { get; init; } = [];
    /// <summary>Indices of the currency-type items, so the editor can validate drop quantities
    /// (currency needs a quantity; other item types ignore it) without fetching every full record.</summary>
    [JsonPropertyName("currencyItems")] public int[] CurrencyItems { get; init; } = [];
    /// <summary>NPC footprint sizes (EffectiveSize, 1-based; index 0 unused) so the map editor renders +
    /// validates multi-tile spawn footprints without fetching every full NPC record.</summary>
    [JsonPropertyName("npcSizes")] public int[] NpcSizes { get; init; } = [];

    public sealed record NameEntry(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("name")] string Name
    );
}

public sealed record UpdateSpellPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateSpell;
    [JsonPropertyName("spellNum")] public int SpellNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("type")] public SpellType Type { get; init; }
    // Type-specific fields; see SpellRecord for which apply to which SpellType.
    [JsonPropertyName("vitalAmount")] public short VitalAmount { get; init; }
    [JsonPropertyName("itemNum")] public short ItemNum { get; init; }
    [JsonPropertyName("itemAmount")] public short ItemAmount { get; init; }
    [JsonPropertyName("intReq")] public short IntReq { get; init; }
}

public sealed record UpdateShopPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateShop;
    [JsonPropertyName("shopNum")] public int ShopNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("fixes")] public bool FixesItems { get; init; }
    [JsonPropertyName("shopType")] public ShopType ShopType { get; init; }
    [JsonPropertyName("allowBanking")] public bool AllowBanking { get; init; }
    [JsonPropertyName("keeper")] public int Keeper { get; init; }
    [JsonPropertyName("trades")] public EditorSaveShopPacket.TradeEntry[] Trades { get; init; } = [];
}

public sealed record UpdateClassPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateClass;
    [JsonPropertyName("classNum")] public int ClassNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
    [JsonPropertyName("str")] public int Str { get; init; }
    [JsonPropertyName("def")] public int Def { get; init; }
    [JsonPropertyName("spd")] public int Spd { get; init; }
    [JsonPropertyName("int")] public int Int { get; init; }
}

public sealed record EditorAllItemsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllItems;
    [JsonPropertyName("items")] public UpdateItemPacket[] Items { get; init; } = [];
}

public sealed record EditorAllNpcsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllNpcs;
    [JsonPropertyName("npcs")] public UpdateNpcPacket[] Npcs { get; init; } = [];
}

public sealed record EditorAllShopsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllShops;
    [JsonPropertyName("shops")] public UpdateShopPacket[] Shops { get; init; } = [];
}

public sealed record EditorAllSpellsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllSpells;
    [JsonPropertyName("spells")] public UpdateSpellPacket[] Spells { get; init; } = [];
}

public sealed record EditorAllClassesPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllClasses;
    [JsonPropertyName("classes")] public UpdateClassPacket[] Classes { get; init; } = [];
}

// S->C: one group's full state (RequestMapGroup response). Mirrors the authored fields; ControllingGuild is
// included read-only so the editor can surface who currently holds a territory.
public sealed record UpdateMapGroupPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateMapGroup;
    [JsonPropertyName("groupNum")] public int GroupNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("music")] public int Music { get; init; }
    [JsonPropertyName("moral")] public MapMoral? Moral { get; init; }
    [JsonPropertyName("indoors")] public bool? Indoors { get; init; }
    [JsonPropertyName("alwaysDark")] public bool? AlwaysDark { get; init; }
    [JsonPropertyName("bootMap")] public int BootMap { get; init; }
    [JsonPropertyName("bootX")] public int BootX { get; init; }
    [JsonPropertyName("bootY")] public int BootY { get; init; }
    [JsonPropertyName("greetingSpeaker")] public string GreetingSpeaker { get; init; } = "";
    [JsonPropertyName("joinSay")] public string JoinSay { get; init; } = "";
    [JsonPropertyName("leaveSay")] public string LeaveSay { get; init; } = "";
    [JsonPropertyName("territory")] public bool Territory { get; init; }
    [JsonPropertyName("controllingGuild")] public int ControllingGuild { get; init; }
}

public sealed record EditorAllMapGroupsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllMapGroups;
    [JsonPropertyName("mapGroups")] public UpdateMapGroupPacket[] MapGroups { get; init; } = [];
}

// ── Quest editor ─────────────────────────────────────────────────────────────

public sealed record EditorRequestQuestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestQuest;
    [JsonPropertyName("questNum")] public int QuestNum { get; init; }
}

public sealed record EditorRequestAllQuestsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllQuests;
}

/// <summary>C→S: save an authored quest. Objectives/rewards ride the shared <see cref="Objective"/> /
/// <see cref="QuestReward"/> records (only non-empty entries are sent). Mirrors EditorSaveShopPacket.</summary>
public sealed record EditorSaveQuestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveQuest;
    [JsonPropertyName("questNum")] public int QuestNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("desc")] public string Description { get; init; } = "";
    [JsonPropertyName("obj")] public List<Objective> Objectives { get; init; } = new();
    [JsonPropertyName("reqLvl")] public int ReqLevel { get; init; }
    [JsonPropertyName("reqStr")] public int ReqStr { get; init; }
    [JsonPropertyName("reqDef")] public int ReqDef { get; init; }
    [JsonPropertyName("reqSpd")] public int ReqSpd { get; init; }
    [JsonPropertyName("reqInt")] public int ReqInt { get; init; }
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("prereq")] public int PrereqQuest { get; init; }
    [JsonPropertyName("rewExp")] public long RewardExp { get; init; }
    [JsonPropertyName("rewItems")] public List<QuestReward> RewardItems { get; init; } = new();
    [JsonPropertyName("repExp")] public long RepeatRewardExp { get; init; }
    [JsonPropertyName("repItems")] public List<QuestReward> RepeatRewardItems { get; init; } = new();
    [JsonPropertyName("giver")] public int GiverNpc { get; init; }
    [JsonPropertyName("turnIn")] public int TurnInNpc { get; init; }
    [JsonPropertyName("repeat")] public bool Repeatable { get; init; }
    [JsonPropertyName("cadence")] public QuestCadence Cadence { get; init; }
}

/// <summary>S→C: one quest's full definition — the RequestQuest response, an EditorAllQuests element, AND the
/// live broadcast to game clients on an editor save (so quest defs refresh without a reconnect, mirroring
/// the shop-keeper live-refresh). Identical field set to EditorSaveQuestPacket.</summary>
public sealed record UpdateQuestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateQuest;
    [JsonPropertyName("questNum")] public int QuestNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("desc")] public string Description { get; init; } = "";
    [JsonPropertyName("obj")] public List<Objective> Objectives { get; init; } = new();
    [JsonPropertyName("reqLvl")] public int ReqLevel { get; init; }
    [JsonPropertyName("reqStr")] public int ReqStr { get; init; }
    [JsonPropertyName("reqDef")] public int ReqDef { get; init; }
    [JsonPropertyName("reqSpd")] public int ReqSpd { get; init; }
    [JsonPropertyName("reqInt")] public int ReqInt { get; init; }
    [JsonPropertyName("allowedClasses")] public List<short>? AllowedClasses { get; init; }
    [JsonPropertyName("prereq")] public int PrereqQuest { get; init; }
    [JsonPropertyName("rewExp")] public long RewardExp { get; init; }
    [JsonPropertyName("rewItems")] public List<QuestReward> RewardItems { get; init; } = new();
    [JsonPropertyName("repExp")] public long RepeatRewardExp { get; init; }
    [JsonPropertyName("repItems")] public List<QuestReward> RepeatRewardItems { get; init; } = new();
    [JsonPropertyName("giver")] public int GiverNpc { get; init; }
    [JsonPropertyName("turnIn")] public int TurnInNpc { get; init; }
    [JsonPropertyName("repeat")] public bool Repeatable { get; init; }
    [JsonPropertyName("cadence")] public QuestCadence Cadence { get; init; }
}

public sealed record EditorAllQuestsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllQuests;
    [JsonPropertyName("quests")] public UpdateQuestPacket[] Quests { get; init; } = [];
}

// ── Conversation editor (NPC conversations) ──────────────────────────────────

public sealed record EditorRequestConversationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestConversation;
    [JsonPropertyName("convNum")] public int ConvNum { get; init; }
}

public sealed record EditorRequestAllConversationsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorRequestAllConversations;
}

/// <summary>C→S: save an authored conversation. The node tree (each node's choices) rides the shared
/// <see cref="ConversationNode"/> records (only non-empty entries are sent). Mirrors EditorSaveQuestPacket.</summary>
public sealed record EditorSaveConversationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorSaveConversation;
    [JsonPropertyName("convNum")] public int ConvNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("speaker")] public int SpeakerNpc { get; init; }
    [JsonPropertyName("root")] public int RootNodeId { get; init; }
    [JsonPropertyName("nodes")] public List<ConversationNode> Nodes { get; init; } = new();
}

/// <summary>S→C: one conversation's full definition — the RequestConversation response, an EditorAllConversations
/// element, AND the live broadcast to game clients on an editor save (so conversation defs + the "..." glyphs
/// refresh without a reconnect, mirroring the quest live-refresh). Identical field set to EditorSaveConversation.</summary>
public sealed record UpdateConversationPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.UpdateConversation;
    [JsonPropertyName("convNum")] public int ConvNum { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("speaker")] public int SpeakerNpc { get; init; }
    [JsonPropertyName("root")] public int RootNodeId { get; init; }
    [JsonPropertyName("nodes")] public List<ConversationNode> Nodes { get; init; } = new();
}

public sealed record EditorAllConversationsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.EditorAllConversations;
    [JsonPropertyName("conversations")] public UpdateConversationPacket[] Conversations { get; init; } = [];
}
