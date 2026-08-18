namespace Mirage.Shared.Records;

/// <summary>A group of maps sharing a <see cref="Name"/> + map-like fallback properties, and optionally a
/// contestable TERRITORY. Index-keyed on disk (<c>mapgroups/mapgroup{Index}.json</c>), with
/// NON-unique names (like maps). A map references its group via <see cref="MapRecord.MapGroup"/>; a map's OWN
/// property always wins and the group fills in only what the map leaves unset (see <see cref="MapGroupResolve"/>).
/// A territory = a group with <see cref="Territory"/> = true plus a runtime <see cref="ControllingGuild"/>.</summary>
public sealed class MapGroupRecord
{
    /// <summary>Group id (>= 1); also the on-disk slot (<c>mapgroups/mapgroup{Index}.json</c>).</summary>
    public int Index { get; set; }
    /// <summary>Group name — an identifier (like a Map Name); NON-unique. It does not override map names; it
    /// slots into the display chain between a map's DisplayName and its raw Name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Player-facing group display name, inserted into the map name chain (Map DisplayName → this →
    /// Map Name → "Map N"). Blank falls through to <see cref="Name"/>.</summary>
    public string DisplayName { get; set; } = "";

    // ── Map-like fallbacks (used only where the map leaves its own unset; the map always wins) ──────────
    // The int fields use 0 as the "the group doesn't provide this" sentinel. Moral + the bools are NULLABLE so
    // a group can decline to provide one (null) — MapMoral.None / false are real values, not spare sentinels;
    // null on both map + group resolves to the hard default (None / false).
    public int Music { get; set; }
    public MapMoral? Moral { get; set; }
    public bool? Indoors { get; set; }
    public bool? AlwaysDark { get; set; }
    public int BootMap { get; set; }
    public int BootX { get; set; }
    public int BootY { get; set; }

    // Map-enter/leave greeting fallback: a map inherits any greeting field it leaves blank
    // from its group, so a multi-map building can define one greeting once at the group level.
    public string GreetingSpeaker { get; set; } = string.Empty;
    public string JoinSay { get; set; } = string.Empty;
    public string LeaveSay { get; set; } = string.Empty;

    // ── Territory ──────────────────────────────────────────────────────────────────────────────────────
    /// <summary>When true this group is a contestable territory (its maps must be non-safe). Capture points,
    /// income, and war-night contests key off this flag.</summary>
    public bool Territory { get; set; }
    /// <summary>The guild that currently controls this territory (0 = unclaimed). Persisted — territory
    /// ownership survives restarts and season resets.</summary>
    public int ControllingGuild { get; set; }

    // ── Territory income runtime state ───────────────────────────────────────────────────────────────────
    // Server-owned accumulators, persisted so they survive restarts (PendingIncome especially, so a restart
    // can't double-credit). The editor never authors these; a group save preserves them (see the save handler).
    /// <summary>Gold accrued from PvE kills in this territory since the last daily settlement (capped per day).
    /// Credited to <see cref="ControllingGuild"/>'s vault at the 00:00 settlement, then zeroed.</summary>
    public long PendingIncome { get; set; }
    /// <summary>Running total of income CREDITED this territory-week; snapshotted into
    /// <see cref="PreviousWeekIncome"/> and zeroed at the weekly reset.</summary>
    public long IncomeThisWeek { get; set; }
    /// <summary>The gold this territory generated for its owner over the previous week (the Territories-tab
    /// column). 0 for unclaimed/untaxed.</summary>
    public long PreviousWeekIncome { get; set; }
    /// <summary>Settlement idempotency: the last date the weekly PreviousWeekIncome roll ran for this territory
    /// (default MinValue = never). Keeps the daily settlement + /guildreset from double-rolling. Persisted.</summary>
    public DateOnly LastWeekRollDate { get; set; }
    /// <summary>Consecutive weeks the current owner has held this territory (0 = fresh). Drives the income
    /// multiplier (cap <see cref="Constants.TerritoryWeeksHeldCap"/>). Incremented on a successful weekly
    /// defense at war night; reset to 0 on capture.</summary>
    public int WeeksHeld { get; set; }

    // ── War-night challenge registration ─────────────────────────────────────────────────────────────────
    /// <summary>Guild indices registered to contest this territory at the next war night (up to
    /// <see cref="Constants.TerritoryMaxChallengers"/>). Persisted — registrations are made ahead of war night
    /// and must survive a restart. Cleared at resolution.</summary>
    public List<int> Challengers { get; set; } = new();
    /// <summary>True when the current owner has abandoned this territory by challenging a different one (the
    /// one-territory cap): it resolves as an unclaimed contest (no defender) at the next war night. The owner
    /// keeps its income until then. Reset at resolution.</summary>
    public bool DefenderAbandoned { get; set; }

    /// <summary>Deep copy for an off-thread save snapshot. All fields are value types or an immutable string
    /// EXCEPT <see cref="Challengers"/>, which is copied so the snapshot can't be mutated by a live edit.</summary>
    public MapGroupRecord Clone()
    {
        var copy = (MapGroupRecord)MemberwiseClone();
        copy.Challengers = new List<int>(Challengers);
        return copy;
    }
}
