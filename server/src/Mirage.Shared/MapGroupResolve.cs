using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>Resolves a map's EFFECTIVE property when it belongs to a MapGroup: the map's own value
/// wins, and the group fills in only what the map leaves unset. Two flavors of "unset":
/// <list type="bullet">
/// <item>The int fields (Music/Shop/Boot) use 0 as the sentinel — a non-zero map value wins, else a non-zero
/// group value, else 0. Explicit 0 can't override to "on" (0 == absent), which is fine: 0 has no meaning
/// distinct from "unset" for these.</item>
/// <item>Moral and the environment bools (Indoors/AlwaysDark) are NULLABLE on both map and group, because
/// MapMoral.None / false are real, meaningful values with no spare sentinel: <c>map ?? group ?? default</c> —
/// an explicit value on the map overrides the group, null inherits, and null on both (incl. a null group)
/// resolves safely to the default (None / false).</item>
/// </list>
/// Pure and null-group-safe, so it's directly unit-testable and is the single source of truth behind the
/// <c>GameWorld.*Of(mapNum)</c> helpers + the client resolve-on-send.</summary>
public static class MapGroupResolve
{
    public static MapMoral Moral(MapRecord map, MapGroupRecord? g) => map.Moral ?? g?.Moral ?? MapMoral.None;
    public static int Music(MapRecord map, MapGroupRecord? g) => map.Music != 0 ? map.Music : g?.Music ?? 0;

    // Nullable bools: explicit value wins, else inherit the group, else false (also covers a null group).
    public static bool Indoors(MapRecord map, MapGroupRecord? g) => map.Indoors ?? g?.Indoors ?? false;
    public static bool AlwaysDark(MapRecord map, MapGroupRecord? g) => map.AlwaysDark ?? g?.AlwaysDark ?? false;

    // The boot map + X + Y travel as a set keyed on BootMap (0 = unset): if the map sets its own boot map, its
    // own X/Y accompany it; otherwise the whole boot destination inherits from the group.
    public static int BootMap(MapRecord map, MapGroupRecord? g) => map.BootMap != 0 ? map.BootMap : g?.BootMap ?? 0;
    public static int BootX(MapRecord map, MapGroupRecord? g) => map.BootMap != 0 ? map.BootX : g?.BootX ?? 0;
    public static int BootY(MapRecord map, MapGroupRecord? g) => map.BootMap != 0 ? map.BootY : g?.BootY ?? 0;

    /// <summary>The player-facing display name chain: map DisplayName -> group DisplayName ->
    /// map Name -> "" (the caller supplies the final "Map N" fallback). Names use "" (blank) as their unset
    /// sentinel. Group null-safe.</summary>
    public static string DisplayName(MapRecord map, MapGroupRecord? g)
    {
        if (!string.IsNullOrWhiteSpace(map.DisplayName)) return map.DisplayName.Trim();
        if (g is not null && !string.IsNullOrWhiteSpace(g.DisplayName)) return g.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(map.Name)) return map.Name.Trim();
        return "";
    }

    /// <summary>The effective map-enter/leave greeting: each of the three strings resolves
    /// independently — the map's own value wins when non-blank, else the group's, else "" — so a map can override
    /// just one line (e.g. its own speaker) while inheriting the rest from the group. Blank fields simply produce
    /// no message. Group null-safe.</summary>
    public static MapGreeting Greeting(MapRecord map, MapGroupRecord? g) => new(
        Pick(map.GreetingSpeaker, g?.GreetingSpeaker),
        Pick(map.JoinSay, g?.JoinSay),
        Pick(map.LeaveSay, g?.LeaveSay));

    private static string Pick(string own, string? group) =>
        !string.IsNullOrWhiteSpace(own) ? own : group ?? "";
}

/// <summary>The resolved map greeting: who speaks it and the enter/leave lines. A value
/// type with structural equality, so MovementSystem can detect a greeting change across a map crossing by
/// comparing the old and new tuples (contiguous maps sharing a group greeting compare equal → stay silent).</summary>
public readonly record struct MapGreeting(string Speaker, string JoinSay, string LeaveSay);
