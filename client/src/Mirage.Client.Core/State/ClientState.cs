using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>
/// Central mutable game state for the client — one instance owns everything the renderer,
/// panels, and packet handlers read.
/// All arrays are 1-based (index 0 is an unused dummy) to match the server convention.
/// </summary>
public sealed partial class ClientState
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public int MyIndex { get; set; }
    public bool InGame { get; set; }
    public bool GettingMap { get; set; }

    /// <summary>
    /// Raised at the end of <see cref="ShiftGrid"/> with the world-pixel offset the data slid
    /// (dxPixels, dyPixels). Subscribers like the floating-text layer that store positions in the
    /// world-pixel frame need to re-anchor by the same offset so they stay over the spot they were
    /// spawned at instead of drifting off-screen when the player crosses a seam.
    /// </summary>
    public event Action<int, int>? GridShifted;

    /// <summary>
    /// Raised at the end of <see cref="ClearMapState"/> — i.e. on a true warp/teleport/full reload
    /// (seamless crossings re-frame via <see cref="ShiftGrid"/> and raise <see cref="GridShifted"/>
    /// instead). Subscribers that hold world-pixel state decoupled from the entity records this
    /// method resets (e.g. the floating-text layer) must drop it here, or it lingers over the old
    /// map's coord frame at the warp destination until it ages out.
    /// </summary>
    public event Action? MapStateCleared;

    // Seamless-crossing prediction: the client shifts its grid the instant it steps off an edge
    // (no round-trip pause), then reconciles with the server.  PendingCrossToMap != 0 means a
    // predicted cross is awaiting confirmation: a matching SeamlessCross confirms it; a self
    // move-correction means the server rejected it and we revert by reloading PendingCrossFromMap.
    public int PendingCrossToMap { get; private set; }
    public int PendingCrossFromMap { get; private set; }
    public int PendingCrossFromRevision { get; private set; }

    public void BeginPendingCross(int fromMap, int fromRevision, int toMap)
    {
        PendingCrossFromMap = fromMap;
        PendingCrossFromRevision = fromRevision;
        PendingCrossToMap = toMap;
    }

    public void ClearPendingCross()
    {
        PendingCrossToMap = 0;
        PendingCrossFromMap = 0;
        PendingCrossFromRevision = 0;
    }

    /// <summary>Place the local player on the freshly-crossed-into map at (x, y), snap the
    /// movement interpolation, and clear the predicted-cross state. Single point of mutation
    /// for the post-cross fields so the 5-field assignment doesn't drift across call sites.</summary>
    public void ApplySeamlessCross(int mapNum, int x, int y, WorldLayer layer)
    {
        var me = Me;
        me.Map = mapNum;
        me.X = x;
        me.Y = y;
        me.Layer = layer;   // two-layer world: a bridge continues across the seam (server-authoritative)
        me.XOffset = 0;
        me.YOffset = 0;
        me.Moving = MovementType.None;
    }
    public string AccountName { get; set; } = "";
    public string CurrentCharName { get; set; } = "";

    /// <summary>Shortcut to the local player's record.</summary>
    public PlayerRecord Me => Players[MyIndex];

    // ── Players (1-based, 1..MaxPlayers) ─────────────────────────────────────

    public PlayerRecord[] Players { get; } = InitPlayers();

    private static PlayerRecord[] InitPlayers()
    {
        var arr = new PlayerRecord[Constants.MaxPlayers + 1];
        for (int i = 1; i <= Constants.MaxPlayers; i++) arr[i] = new PlayerRecord();
        arr[0] = new PlayerRecord(); // dummy
        return arr;
    }

    // ── Maps: seamless 3×3 grid ([col, row], center = [1,1]) ──────────────────

    /// <summary>
    /// The current map and its 8 pre-loaded neighbors.  A null cell means that
    /// neighbor isn't loaded (or doesn't exist), which clamps camera scrolling in
    /// that direction.  The center cell [1,1] is always present.
    /// </summary>
    public MapRecord?[,] NeighborMaps { get; } = InitNeighborMaps();

    private static MapRecord?[,] InitNeighborMaps()
    {
        var g = new MapRecord?[3, 3];
        g[1, 1] = new MapRecord();
        return g;
    }

    /// <summary>The current ("center") map — alias for <c>NeighborMaps[1,1]</c>.</summary>
    public MapRecord Map
    {
        get => NeighborMaps[1, 1]!;
        set => NeighborMaps[1, 1] = value;
    }

    /// <summary>Server map number of the current ("center") map.</summary>
    public int CenterMapNum { get; set; }

    /// <summary>
    /// Server map number occupying each 3×3 grid cell ([col,row]); 0 = no map.
    /// [1,1] mirrors <see cref="CenterMapNum"/>.  Lets the client route incoming
    /// entity packets (tagged with mapNum) to the right cell.
    /// </summary>
    public int[,] NeighborMapNums { get; } = new int[3, 3];

    /// <summary>Door-open state per tile on the center map; indexed [x, y, (int)WorldLayer].
    /// Two-plane world: a fringe-deck door on a bridge is independent of the ground door beneath it.
    /// Settable so a seamless crossing can swap it with a neighbor cell's grid (see ShiftGrid).</summary>
    public bool[,,] TempTile { get; private set; } = new bool[Constants.MaxMapX + 1, Constants.MaxMapY + 1, 2];

    /// <summary>
    /// Door-open state for the 8 neighbor maps ([col,row]; [1,1] unused — center uses
    /// <see cref="TempTile"/>).  Mirrors the center's door tracking so neighbor doors predict
    /// collision identically.  Each cell is an always-allocated [x,y,layer] bool grid.
    /// </summary>
    public bool[,][,,] NeighborTempTiles { get; } = InitNeighborTempTiles();

    private static bool[,][,,] InitNeighborTempTiles()
    {
        var g = new bool[3, 3][,,];
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                g[c, r] = new bool[Constants.MaxMapX + 1, Constants.MaxMapY + 1, 2];
        }

        return g;
    }

    /// <summary>Door-open grid for a map number — the center grid, a neighbor cell, or null.</summary>
    public bool[,,]? TempTilesForMap(int mapNum)
    {
        if (mapNum <= 0) return null;
        if (mapNum == CenterMapNum) return TempTile;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                if (!(c == 1 && r == 1) && NeighborMapNums[c, r] == mapNum)
                    return NeighborTempTiles[c, r];
            }
        }

        return null;
    }

    /// <summary>One blood pool: a size×size tile RECTANGLE (top-left X,Y in map-local coords) with a shared
    /// stain <see cref="Amount"/> (drives the decal's blob SIZE + droplet COUNT) and <see cref="Freshness"/>
    /// (0..1 OPACITY: a hit redarkens to 1, then it fades with the amount).  Mirrors the server's BloodPool;
    /// pools overlap freely.</summary>
    public sealed class BloodPool
    {
        public int X;
        public int Y;
        public int Size;
        public float Amount;
        public float Freshness;
        public WorldLayer Layer;
    }

    /// <summary>Blood pools keyed by MAP NUMBER (not by observable-cell).  A deposit makes the server replace a
    /// map's WHOLE list (<c>BloodUpdatePacket</c>); <c>BloodProcessor</c> replays the shared linear decay locally
    /// each frame and drops dried pools.  Keyed by map num, so a seamless crossing needs no grid shuffle — it
    /// just prunes maps that scrolled out of view (<see cref="PruneBloodToObserved"/>).</summary>
    public Dictionary<int, List<BloodPool>> BloodByMap { get; } = new();

    /// <summary>The pool list for a map, created empty on first use.</summary>
    public List<BloodPool> BloodPoolsForMap(int mapNum)
    {
        if (!BloodByMap.TryGetValue(mapNum, out var list))
            BloodByMap[mapNum] = list = new List<BloodPool>();
        return list;
    }

    /// <summary>True when <paramref name="mapNum"/> is the center or one of the 8 neighbor maps currently in view.</summary>
    public bool IsObservedMap(int mapNum)
    {
        if (mapNum <= 0) return false;
        if (mapNum == CenterMapNum) return true;
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
                if (NeighborMapNums[c, r] == mapNum) return true;
        }

        return false;
    }

    /// <summary>Drop blood for maps that scrolled out of the observable 3×3 (called after a seam re-frame); the
    /// server re-snapshots any map that comes back into view, so this can't lose live blood we still need.</summary>
    public void PruneBloodToObserved()
    {
        if (BloodByMap.Count == 0) return;
        List<int>? drop = null;
        foreach (int m in BloodByMap.Keys)
            if (!IsObservedMap(m)) (drop ??= new()).Add(m);
        if (drop is not null)
            foreach (int m in drop) BloodByMap.Remove(m);
    }
}
