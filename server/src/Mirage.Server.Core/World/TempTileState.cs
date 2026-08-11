using Mirage.Shared;

namespace Mirage.Server.Core.World;

/// <summary>Per-map RUNTIME tile state that is never persisted: which doors stand open and when a
/// picked-up tile item is due back. Rebuilt empty on every map load.</summary>
public sealed class TempTileState
{
    // Per-(tile, layer) door state: a Key door on the fringe deck of a bridge is independent of the ground door
    // beneath it. Indexed [x, y, (int)WorldLayer]; a door is read/opened on the layer its Key attribute lives on.
    // 0 = shut; non-zero = TickCount64 when THIS door opened, so each ages out on its own clock instead of
    // sharing one per-map timer (same 0-means-inactive encoding as ItemRespawnTimers below).
    // Prefer IsDoorOpen/OpenDoor over indexing this directly.
    public long[,,] DoorOpenedAt { get; } = new long[Constants.MaxMapX + 1, Constants.MaxMapY + 1, 2];
    // Per-(tile, layer) item respawn: 0 = item is present; non-zero = TickCount64 when the tile item was removed.
    // Layer-indexed so a Ground tile-item and a Fringe tile-item sharing one (x,y) respawn independently.
    public long[,,] ItemRespawnTimers { get; } = new long[Constants.MaxMapX + 1, Constants.MaxMapY + 1, 2];

    public bool IsDoorOpen(int x, int y, WorldLayer layer) => DoorOpenedAt[x, y, (int)layer] != 0;

    /// <summary>Stamps a door open at <paramref name="now"/> (TickCount64) so the auto-close sweep ages it out
    /// on its own clock. Callers gate on <see cref="IsDoorOpen"/> first — re-stamping an already-open door
    /// would extend its window. Tick 0 clamps to 1 so it can't collide with the "shut" sentinel.</summary>
    public void OpenDoor(int x, int y, WorldLayer layer, long now) =>
        DoorOpenedAt[x, y, (int)layer] = now == 0 ? 1 : now;
}
