using Mirage.Shared.Records;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

// ── C→S ─────────────────────────────────────────────────────────────────────

public sealed record RequestNewMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RequestNewMap;
}

public sealed record NeedMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NeedMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("revision")] public int Revision { get; init; }
}

// C→S: client confirms it has received map data
public sealed record MapDataClientPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapData;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
}

// C→S: client's disk cache is stale for a pre-loaded neighbor; request its full data.
// Unlike NeedMap (which serves the player's own map), this honors the specific mapNum,
// validated server-side to be one of the player's currently observable neighbors.
public sealed record NeedNeighborMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.NeedNeighborMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("col")] public int Col { get; init; }
    [JsonPropertyName("row")] public int Row { get; init; }
}

/// <summary>C→S: after a seamless border crossing the client has shifted its grid and asks the server
/// to re-sync its now-current observable region (newly-revealed edge maps, their entities, players).</summary>
public sealed record RequestRegionSyncPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RequestRegionSync;
}

// ── S→C ─────────────────────────────────────────────────────────────────────

/// <summary>S→C: the MapGroup definitions, shipped as an independent client-cached def like
/// items/npcs/shops. Sent in bulk at join and re-broadcast per-group on a live editor save (via
/// UpdateMapGroupPacket). The client resolves each map's EFFECTIVE inheritable values against its cached group
/// on demand (ClientState.*Of + MapGroupResolve), so a group edit reaches online players without a map reload
/// or a revision bump.</summary>
public sealed record SendMapGroupsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendMapGroups;
    [JsonPropertyName("groups")] public GroupData[] Groups { get; init; } = [];

    // Only the inheritable fields the client resolves against. Territory / ControllingGuild are server- and
    // contest-side concerns the client's render/predict paths never read, so they stay off the wire here.
    public sealed record GroupData(
        [property: JsonPropertyName("num")] int Num,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("moral")] MapMoral? Moral,
        [property: JsonPropertyName("music")] int Music,
        [property: JsonPropertyName("indoors")] bool? Indoors,
        [property: JsonPropertyName("alwaysDark")] bool? AlwaysDark,
        [property: JsonPropertyName("bootMap")] int BootMap,
        [property: JsonPropertyName("bootX")] int BootX,
        [property: JsonPropertyName("bootY")] int BootY
    );
}

public sealed record SendMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SendMap;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
    [JsonPropertyName("revision")] public int Revision { get; init; }
    // Seamless-scroll grid cell this map occupies on the client (center = 1,1).
    // Neighbor pre-loads carry their own cell; the center map keeps the default.
    [JsonPropertyName("col")] public int Col { get; init; } = 1;
    [JsonPropertyName("row")] public int Row { get; init; } = 1;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("up")] public int Up { get; init; }
    [JsonPropertyName("down")] public int Down { get; init; }
    [JsonPropertyName("left")] public int Left { get; init; }
    [JsonPropertyName("right")] public int Right { get; init; }
    // MapGroup-inheritable fields. Always RAW here — the map's own value, with 0 (int fields) or
    // null (Moral + the bools) meaning "inherit from the MapGroup". BOTH the editor (authoring the inherit/
    // override state) and the game client now receive raw and resolve the effective value themselves against
    // their cached group (client: ClientState.*Of + MapGroupResolve; server-side gameplay: GameWorld.*Of). The
    // group is shipped independently (SendMapGroupsPacket at join + UpdateMapGroupPacket on live edit), so a
    // group edit reaches online players without touching any map's revision.
    [JsonPropertyName("moral")] public MapMoral? Moral { get; init; }
    [JsonPropertyName("music")] public int Music { get; init; }
    [JsonPropertyName("bootMap")] public int BootMap { get; init; }
    [JsonPropertyName("bootX")] public int BootX { get; init; }
    [JsonPropertyName("bootY")] public int BootY { get; init; }
    [JsonPropertyName("indoors")] public bool? Indoors { get; init; }
    [JsonPropertyName("alwaysDark")] public bool? AlwaysDark { get; init; }
    // Map-enter/leave greeting. Editor-authored, editor-only: PacketBuilder.SendMap leaves
    // these blank for the game client (which never speaks them — the server does, from its own MapRecord), and
    // carries them only to the editor so a map load/save round-trips the greeting.
    [JsonPropertyName("greetingSpeaker")] public string GreetingSpeaker { get; init; } = "";
    [JsonPropertyName("joinSay")] public string JoinSay { get; init; } = "";
    [JsonPropertyName("leaveSay")] public string LeaveSay { get; init; } = "";
    // The map's MapGroup reference (0 = none). Raw id in both flavors — the group id isn't itself resolved;
    // it drives the effective-value fallback above (game client) and round-trips the editor's group pick.
    [JsonPropertyName("mapGroup")] public int MapGroup { get; init; }
    [JsonPropertyName("tiles")] public TileData[] Tiles { get; init; } = [];
    // NPC spawn entries: the map's dense NPC list — each entry's NPC type plus its optional
    // fixed spawn pin. The editor gets full entries (pins included) for the authoring round-trip; the game
    // client gets the same entries with pins stripped (PacketBuilder.SendMap), since it renders NPCs from live
    // spawn packets and never needs the pins.
    [JsonPropertyName("npcs")] public MapNpcEntry[] Npcs { get; init; } = [];
    [JsonPropertyName("lights")] public PlacedLight[] Lights { get; init; } = [];

    // Ground/Fringe carry the tile's layer stacks as packed LayerCell ints (see LayerCell), trimmed of
    // trailing empties to stay compact on the wire.  Tiles that are fully default are omitted from
    // SendMapPacket.Tiles entirely (the client rebuilds from a blank grid), so this is a sparse format.
    public sealed record TileData(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("g")] int[] Ground,
        [property: JsonPropertyName("f")] int[] Fringe,
        [property: JsonPropertyName("type")] TileType Type,
        [property: JsonPropertyName("d1")] short Data1,
        [property: JsonPropertyName("d2")] short Data2,
        [property: JsonPropertyName("d3")] short Data3,
        // Canopy: the third visual stack, drawn atop everything (mirrors Ground/Fringe, trailing-empties trimmed).
        [property: JsonPropertyName("c")] int[] Canopy,
        // Fringe-layer gameplay attribute; non-null iff a walkable fringe layer (bridge top) exists here.
        [property: JsonPropertyName("fa"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FringeData? FringeAttr
    )
    {
        /// <summary>Build a wire tile from a map tile, trimming trailing-empty layers.</summary>
        public static TileData From(int x, int y, TileRecord t) =>
            new(x, y, Trim(t.Ground), Trim(t.Fringe), t.Type, t.Data1, t.Data2, t.Data3,
                Trim(t.Canopy),
                t.FringeAttr is { } fa ? new FringeData(fa.Type, fa.Data1, fa.Data2, fa.Data3) : null);

        /// <summary>Materialize a fresh <see cref="TileRecord"/> from this wire tile.</summary>
        public TileRecord ToTile()
        {
            var tile = new TileRecord { Type = Type, Data1 = Data1, Data2 = Data2, Data3 = Data3 };
            CopyClamped(Ground, tile.Ground);
            CopyClamped(Fringe, tile.Fringe);
            CopyClamped(Canopy, tile.Canopy);
            if (FringeAttr is { } fa)
                tile.FringeAttr = new FringeAttr { Type = fa.Type, Data1 = fa.Data1, Data2 = fa.Data2, Data3 = fa.Data3 };
            return tile;
        }

        /// <summary>True when a tile carries nothing — no layers, default type, no data, no fringe layer —
        /// so it can be omitted from a sparse SendMap.</summary>
        public static bool IsDefault(TileRecord t)
        {
            if (t.Type != TileType.Walkable || t.Data1 != 0 || t.Data2 != 0 || t.Data3 != 0) return false;
            if (t.FringeAttr is not null) return false;
            foreach (int p in t.Ground) if (!LayerCell.IsEmpty(p)) return false;
            foreach (int p in t.Fringe) if (!LayerCell.IsEmpty(p)) return false;
            foreach (int p in t.Canopy) if (!LayerCell.IsEmpty(p)) return false;
            return true;
        }

        private static int[] Trim(int[] layers)
        {
            int last = -1;
            for (int i = 0; i < layers.Length; i++)
                if (!LayerCell.IsEmpty(layers[i])) last = i;
            if (last < 0) return [];
            var r = new int[last + 1];
            Array.Copy(layers, r, last + 1);
            return r;
        }

        private static void CopyClamped(int[]? src, int[] dest)
        {
            if (src is null) return;
            int n = Math.Min(src.Length, dest.Length);
            for (int i = 0; i < n; i++) dest[i] = src[i];
        }
    }

    /// <summary>Compact wire form of a tile's fringe-layer attribute (see <see cref="Records.FringeAttr"/>).
    /// Its PRESENCE (non-null on <see cref="TileData"/>) marks that a walkable fringe layer exists.</summary>
    public sealed record FringeData(
        [property: JsonPropertyName("type")] TileType Type,
        [property: JsonPropertyName("d1")] short Data1,
        [property: JsonPropertyName("d2")] short Data2,
        [property: JsonPropertyName("d3")] short Data3
    );
}

public sealed record JoinMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.JoinMap;
    [JsonPropertyName("index")] public int Index { get; init; }
    // Full player data is sent in a separate SendPlayerDataPacket
}

public sealed record LeaveMapPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.LeaveMap;
    [JsonPropertyName("index")] public int Index { get; init; }
}

public sealed record PlayerXYPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerXY;
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("x")] public int X { get; init; }
    [JsonPropertyName("y")] public int Y { get; init; }
}
