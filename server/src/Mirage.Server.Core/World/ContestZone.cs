using Mirage.Shared;

namespace Mirage.Server.Core.World;

/// <summary>Runtime projection of one live territory contest, published into <see cref="GameWorld.ContestZones"/>
/// by GuildTerritorySystem so MovementSystem (setup radius walls + non-participant entry warnings) and
/// SpawnSystem (NPC spawn suppression) can read the war state WITHOUT referencing GuildTerritorySystem — that
/// keeps those systems acyclic (GuildTerritorySystem already depends on them for push-out warps + despawns).
/// Runtime-only, like the contest itself; the list is empty whenever no contest runs.</summary>
public sealed class ContestZone
{
    /// <summary>The contested territory (MapGroup) index.</summary>
    public int TerritoryIndex;
    /// <summary>The territory's display name, for the non-participant entry warning.</summary>
    public string Name = "";
    /// <summary>The defending guild (0 = an unclaimed contest) — the only guild whose members may enter a
    /// capture radius during setup.</summary>
    public int DefenderGuild;
    /// <summary>True only during the setup phase, while the capture-radius walls are active.</summary>
    public bool SetupPhase;
    /// <summary>The participating guild indices (defender + challengers); non-participants are warned on entry.</summary>
    public HashSet<int> Participants = new();
    /// <summary>Every map in the territory — NPC-spawn suppression + entry-crossing membership.</summary>
    public List<int> Maps = new();
    /// <summary>The capture-point centers — the setup radius walls, applied on the point's own layer
    /// (a ground point doesn't wall a bridge above it).</summary>
    public List<CapturePoint> Points = new();

    /// <summary>Just the position of a capture point: what the radius walls and the spawn suppression need,
    /// without the owner/meter/challenger state that the live <see cref="ContestPoint"/> carries — the
    /// projection is the whole reason this type exists.
    ///
    /// <para>Named rather than a <c>(int, int, int, WorldLayer)</c> tuple because the three leading
    /// <c>int</c>s are a map number and a coordinate pair, which positional construction gave no way to
    /// tell apart.</para></summary>
    public readonly record struct CapturePoint(int Map, int X, int Y, WorldLayer Layer);
}
