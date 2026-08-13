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
/// ten 50% lines drops five things a kill. Authoring restraint, not a cap, keeps that sane —
/// <see cref="NpcRecord.Drops"/> is capped only at <see cref="Constants.MaxNpcDrops"/> so a runaway table
/// cannot flood a tile.</para></summary>
public sealed class NpcDrop
{
    /// <summary>1-based index into the item table. 0 or out of range = an inert line, skipped at roll time
    /// rather than treated as an error; an editor can hold a half-authored row without breaking a kill.</summary>
    public int ItemNum { get; set; }

    /// <summary>How many. Meaningful for a CURRENCY item, where it is the stack size; ignored for
    /// everything else, since one drop of a sword is one sword regardless. Clamped to at least 1 for
    /// currency at roll time, mirroring what the single-drop path always did.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Value { get; set; }

    /// <summary>Chance as a direct percent: 1 = 1%, 50 = 50%, 100 or more = always. 0 or less never drops,
    /// which is the way to park a line without deleting it. Rolled against
    /// <see cref="CombatFormulas.RollPercent"/>, which returns [0..99], so the drop lands when the roll is
    /// BELOW this.</summary>
    public short Chance { get; set; }

    /// <summary>A line worth rolling: it names a real item and can actually land.</summary>
    [JsonIgnore]
    public bool IsLive => ItemNum > 0 && Chance > 0;
}
