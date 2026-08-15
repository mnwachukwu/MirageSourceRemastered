using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>One line of an NPC's drop table: an item, how many, and how often.
///
/// <para>Every entry is rolled INDEPENDENTLY on a kill, so one death can yield nothing, one thing, or
/// several. That is deliberate and is what the old single-drop field could not express: a bandit that
/// almost always drops a little gold, sometimes a potion, and very rarely its sword is three entries at
/// three chances, not one weighted pick. A weighted table would force exactly one outcome per kill and
/// make "usually gold, occasionally also a potion" unauthorable.</para>
///
/// <para>The cost of independent rolls is that expected yield is the SUM of the chances, so a table with
/// ten 50% lines drops five things a kill. Authoring restraint keeps that sane; there is deliberately NO
/// length cap on <see cref="NpcRecord.Drops"/>, because repeated lines are the only way to author a
/// multi-item payout (see <see cref="Quantity"/>), which makes any length limit a limit on payout.</para>
///
/// <para>A hoard of twelve is therefore twelve lines at one chance each, not one line of twelve — and it
/// behaves better for it: instead of a flat 2% for the whole pile, a boss becomes ~21% to yield at least
/// one, occasionally two, which is the shape a windfall wants.</para></summary>
public sealed class NpcDrop
{
    /// <summary>1-based index into the item table. 0 or out of range = an inert line, skipped at roll time
    /// rather than treated as an error; an editor can hold a half-authored row without breaking a kill.</summary>
    public int ItemNum { get; set; }

    /// <summary>How many. Meaningful ONLY for a CURRENCY item, where it is the stack size. Clamped to at
    /// least 1 for currency at roll time, mirroring what the single-drop path always did.
    ///
    /// <para>⚠️ For anything else this field is DEAD — not "usually one", but never read at all.
    /// <c>ItemSystem.FindOpenInvSlot</c> merges slots for Currency alone; everything else takes a fresh
    /// slot and <c>HasItem</c> reports a flat 1 no matter what is written here. A line of
    /// <c>{sword, quantity 12}</c> yields ONE sword, silently. Author twelve lines instead.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Quantity { get; set; }

    /// <summary>Chance as a direct percent: 1 = 1%, 50 = 50%, 100 or more = always. 0 or less never drops,
    /// which is the way to park a line without deleting it. Rolled against
    /// <see cref="CombatFormulas.RollPercent"/>, which returns [0..99], so the drop lands when the roll is
    /// BELOW this.</summary>
    public short Chance { get; set; }

    /// <summary>A line worth rolling: it names a real item and can actually land.</summary>
    [JsonIgnore]
    public bool IsLive => ItemNum > 0 && Chance > 0;
}
