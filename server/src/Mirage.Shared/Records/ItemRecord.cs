using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class ItemRecord
{
    private string _name = string.Empty;
    private string? _trimmedName;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _trimmedName = null;
        }
    }
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — record names are stored fixed-width and
    /// every item message string TrimEnds them.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    public short Pic { get; set; }
    public ItemType Type { get; set; }

    // ── Type-specific fields ──────────────────────────────────────────────────
    // Each applies to some item types and is meaningless on the rest; the editor shows only the ones
    // that apply. (These replaced the VB6-era Data1/Data2/Data3 positional slots, where the same
    // number meant durability on a sword and healing on a potion.)
    //
    // All five are WhenWritingDefault, so a zero is left out of the file entirely and an item row
    // lists exactly the properties it has — a potion shows VitalAmount and nothing else. That is the
    // whole point of the expansion: the JSON has to read as a domain object, not as five slots of
    // which three happen to be blank. It is set per-property rather than on the serializer because
    // the global option is shared with map, player and guild persistence, where an explicit 0 is
    // worth keeping. Round-trips cleanly either way: absent deserializes back to 0.

    /// <summary>Weapon/Armor/Helmet/Shield: maximum durability. A worn piece breaks at 0 and stays in
    /// the bag, unequipped, until repaired. 0 = carries no durability budget, so it never breaks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Durability { get; set; }

    /// <summary>The six potion types: how much of the vital the potion moves. Add* restores it; Sub*
    /// drains that much and restores half as much of each of the other two.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short VitalAmount { get; set; }

    /// <summary>Spell scroll: the spell taught on use (1-based index into the spell table).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short SpellNum { get; set; }

    /// <summary>Weapon/Armor/Helmet/Shield: how good the piece is — one number driving three things.
    /// On a weapon it is damage (via <c>WeaponContribution</c>); on armor/helmet it is mitigation (via
    /// <c>GearMitigation</c>); on a shield, mitigation at a quarter weight (<c>ShieldMitigation</c>).
    /// It doubles as the stat needed to equip the piece — STR for a weapon, DEF for the rest, both
    /// through <c>GearStatRequirement</c> — and as the repair rate (<c>EconomyFormulas.RepairCost</c>).
    /// One field rather than separate damage/defense ones precisely because the repair and wear paths
    /// treat all four types alike.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Power { get; set; }

    /// <summary>Weapon/Armor/Helmet/Shield: the classes allowed to equip it (1-based ids). Empty or absent
    /// = every class, which is the usual case. Enforced server-side in ItemSystem's equip path; ask
    /// <see cref="ClassGate"/> rather than testing the list directly.
    /// <para>Nullable so an unrestricted item carries no key at all — the serializer's global
    /// WhenWritingNull does the work, and <see cref="Normalize"/> collapses an empty list to null so
    /// there is only ever one stored spelling of "anyone".</para></summary>
    public List<short>? AllowedClasses { get; set; }

    /// <summary>Item restriction flags. Each blocks exactly one action; banking is always allowed.
    /// Absent = false, so existing item data is unaffected. All four are enforced server-side:
    /// <see cref="NonTradeable"/> in TradeSystem, <see cref="NonListable"/> in MarketSystem,
    /// <see cref="NonMailable"/> on the mail-attach path, and <see cref="DestroyOnDrop"/> in
    /// ItemSystem's drop paths.</summary>
    public bool NonTradeable { get; set; }   // can't be staged in a player trade
    public bool NonListable { get; set; }    // can't be sold on the marketplace
    public bool NonMailable { get; set; }    // can't be attached to / sent by mail
    public bool DestroyOnDrop { get; set; }  // dropping it (voluntary or on death) destroys it

    // ── Which fields apply to which type ──────────────────────────────────────
    // The single statement of that rule. The editor asks it what to show, and <see cref="Normalize"/>
    // asks it what to clear, so the form and the file can't drift apart.
    //
    // This is the half of the expansion that actually removes the old format's hazard. Naming the
    // fields makes a row readable; only clearing the inapplicable ones makes it TRUE. Without it,
    // retyping a Weapon as a Potion leaves Power and ClassReq sitting on the record at their old
    // values — invisible in the editor (which hides them) but live in the file and in every packet.

    /// <summary>The four wearable types, which alone carry durability, power and a class requirement.</summary>
    public static bool IsEquipment(ItemType type) =>
        type is ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield;

    /// <summary>The six potion types, which alone carry <see cref="VitalAmount"/>.</summary>
    public static bool IsPotion(ItemType type) =>
        type is ItemType.PotionAddHp or ItemType.PotionAddMp or ItemType.PotionAddSp
             or ItemType.PotionSubHp or ItemType.PotionSubMp or ItemType.PotionSubSp;

    public static bool UsesDurability(ItemType type) => IsEquipment(type);
    public static bool UsesPower(ItemType type) => IsEquipment(type);
    public static bool UsesAllowedClasses(ItemType type) => IsEquipment(type);
    public static bool UsesVitalAmount(ItemType type) => IsPotion(type);
    public static bool UsesSpellNum(ItemType type) => type is ItemType.Spell;

    /// <summary>Zero every field that does not apply to the current <see cref="Type"/>, so the record
    /// carries only properties it actually has. Call on any path that writes an item — the editor's save
    /// and the server's handler for an editor save packet both do, the latter because the server is
    /// authoritative and will not store stale values a client sent it.
    /// <para>Key and Currency keep none of these: a door matches its key on the item's own id, so a key's
    /// numbers are unused.</para></summary>
    public void Normalize()
    {
        if (!UsesDurability(Type)) Durability = 0;
        if (!UsesVitalAmount(Type)) VitalAmount = 0;
        if (!UsesSpellNum(Type)) SpellNum = 0;
        if (!UsesPower(Type)) Power = 0;
        AllowedClasses = UsesAllowedClasses(Type) ? ClassGate.Normalize(AllowedClasses) : null;
    }
}
