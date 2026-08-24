using Mirage.Shared;

namespace Mirage.Client.Core.State;

/// <summary>
/// Which Key doors stand open on one map, for the client's collision prediction and its door art.
///
/// <para>Sparse: an entry exists only for a door that is actually open, so a map costs what its open
/// doors cost and nothing for its size. The server's <c>TempTileState</c> keeps its half the same way, and
/// the two stay in step because both are told about a door by the same MapKey packet.</para>
///
/// <para>Indexed rather than enumerated at every read site — the hot paths (rendering a tile, predicting
/// a step) ask about one tile at a time.</para>
/// </summary>
public sealed class OpenDoors
{
    private readonly HashSet<(int X, int Y, int Layer)> _open = [];

    /// <summary>True when the door on this (tile, layer) stands open.</summary>
    public bool this[int x, int y, int layer] => _open.Contains((x, y, layer));

    /// <inheritdoc cref="this[int, int, int]"/>
    public bool this[int x, int y, WorldLayer layer] => _open.Contains((x, y, (int)layer));

    /// <summary>How many doors are open — the whole cost of a sweep over this map.</summary>
    public int Count => _open.Count;

    public void Set(int x, int y, int layer, bool open)
    {
        if (open) _open.Add((x, y, layer));
        else _open.Remove((x, y, layer));
    }

    /// <summary>Every door shut — what a map load or a grid reframe leaves behind.</summary>
    public void Clear() => _open.Clear();
}
