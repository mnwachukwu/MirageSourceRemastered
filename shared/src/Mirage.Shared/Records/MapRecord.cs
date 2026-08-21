using Mirage.Shared.Serialization;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// One authored map: its identity, its four neighbor links, the MapGroup-inheritable properties, the
/// tile grid, and the NPC-spawn and light lists.
/// <para>Several properties are only HALF the answer on their own — they fall back to the map's
/// <see cref="MapGroup"/> when unset. Always read those through <c>MapGroupResolve</c> or the
/// <c>GameWorld.*Of(mapNum)</c> helpers; a raw read gives the map's own value and silently misses the
/// inherited one.</para>
/// </summary>
public sealed class MapRecord
{
    /// <summary>Internal identifier, shown in editor lists and tools — not player-facing.</summary>
    public string Name { get; set; } = string.Empty;
    // Player-facing name shown in the client HUD. When blank the client falls back to Name, then to a
    // generic "Map N". Name stays the internal identifier (used by editor lists and tools).
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Bumped on every save. A client holding an older revision re-fetches the map on next
    /// observe, which is how an edit reaches players without a reconnect.</summary>
    public int Revision { get; set; }
    // Neighbor map numbers for the seamless world (0 = no map that way). Diagonals are not stored —
    // WorldCoordHelper.BuildMapGrid derives them by composing two cardinal links.
    public int Up { get; set; }
    public int Down { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }

    // ── MapGroup-inheritable properties ──────────────────────────────────────────
    // ALWAYS read these through MapGroupResolve / the GameWorld.*Of(mapNum) helpers so the group fallback is
    // honored — a raw read returns the map's own value only. The int fields (Music/Boot) use 0 as the
    // "not set → inherit the group" sentinel (0 == absent, with no distinct meaning apart from "unset").
    // Moral and the environment bools are NULLABLE instead — MapMoral.None and false are real, meaningful
    // values with no spare sentinel: null = "not set → inherit", an explicit value overrides the group, and
    // null on both map+group resolves to the hard default (None / false). The greeting strings use "" (blank)
    // as their inherit sentinel and resolve per-field (map's own wins if non-blank, else the group's).
    public MapMoral? Moral { get; set; }
    public int Music { get; set; }
    public int BootMap { get; set; }
    public int BootX { get; set; }
    public int BootY { get; set; }
    public bool? Indoors { get; set; }

    // The two lighting overrides are MUTUALLY EXCLUSIVE — a map cannot be both — and are resolved together by
    // MapGroupResolve.Lighting rather than read directly, so the exclusivity survives inheritance. Independent
    // of Moral: whether you can be attacked here and whether you can see here are separate claims.
    public bool? AlwaysLit { get; set; }
    public bool? AlwaysDark { get; set; }

    // ── Map-enter/leave greeting ─────────────────────────────────────────────────
    // The flavor lines spoken when a player steps onto / off this map. GreetingSpeaker is who "says" them.
    // All three inherit the MapGroup per-field when blank; resolve through GameWorld.GreetingOf /
    // MapGroupResolve.Greeting.
    public string GreetingSpeaker { get; set; } = string.Empty;
    public string JoinSay { get; set; } = string.Empty;
    public string LeaveSay { get; set; } = string.Empty;

    // Index into MapGroups (0 = none). The group supplies the fallbacks above + an optional territory.
    /// <summary>Index into MapGroups (0 = none). The group supplies the inheritable fallbacks above
    /// plus an optional territory.</summary>
    public int MapGroup { get; set; }

    // Tile grid [0..MaxMapX, 0..MaxMapY] — already 0-based
    [JsonConverter(typeof(TileArrayConverter))]
    public TileRecord[,] Tile { get; set; } =
        new TileRecord[Constants.MaxMapX + 1, Constants.MaxMapY + 1];

    // NPC spawn entries: a dense 0-based list (0..Count-1, cap MaxMapNpcs). Entry i drives the runtime spawn
    // post MapNpcs[map, i + 1]; each entry carries the NPC type plus its optional fixed spawn tile. Only
    // non-empty rows are stored, so a reload restores exactly the authored rows.
    public List<MapNpcEntry> Npcs { get; set; } = new();

    // Placed light sources: sparse list, at most one per tile. Emitted client-side at night / in
    // AlwaysDark maps. Empty by default, so old maps deserialize with no lights (no migration).
    public List<PlacedLight> Lights { get; set; } = new();

    /// <summary>Fills the tile grid with empty tiles, so every cell is addressable without a null check.</summary>
    public MapRecord()
    {
        for (int x = 0; x <= Constants.MaxMapX; x++)
        {
            for (int y = 0; y <= Constants.MaxMapY; y++)
                Tile[x, y] = new TileRecord();
        }
    }
}
