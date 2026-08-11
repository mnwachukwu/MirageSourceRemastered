using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.Logic;

/// <summary>
/// Centers the viewport on the local player within the seamless 3×3 map world.
/// The camera follows the player but clamps to the extent of the loaded grid: it
/// stops scrolling in a direction only when the entire row/column on that side is
/// empty.  So a column that holds a map only diagonally (e.g. the left column when
/// just an up-left map exists) is still reachable — empty cells in it simply render
/// black.  With no neighbors loaded at all the camera locks to the center map,
/// reproducing the original single-map view exactly (screenX = localX * PicX).
/// </summary>
public sealed class Camera
{
    public const int ViewW = (Constants.MaxMapX + 1) * Constants.PicX; // 512
    public const int ViewH = (Constants.MaxMapY + 1) * Constants.PicY; // 384
    private const int MapPxW = (Constants.MaxMapX + 1) * Constants.PicX; // 512
    private const int MapPxH = (Constants.MaxMapY + 1) * Constants.PicY; // 384

    /// <summary>World-pixel coordinate of the viewport's top-left corner (true, sub-pixel value).</summary>
    public float CameraX { get; private set; }
    public float CameraY { get; private set; }

    /// <summary>
    /// Recompute the camera from the local player's position on the center map.
    /// <paramref name="grid"/> is the 3×3 cell grid ([col,row], center [1,1]).  Scrolling
    /// in a direction is allowed as long as that side's row/column holds any map at all;
    /// it clamps only when the whole row/column is empty.
    /// </summary>
    public void Update(int playerLocalX, int playerLocalY, float xOffset, float yOffset, MapRecord?[,] grid)
    {
        // Center map sits at grid (1,1) → world tile origin (MapTilesX, MapTilesY).
        float pwx = (WorldCoordHelper.MapTilesX + playerLocalX) * Constants.PicX + xOffset;
        float pwy = (WorldCoordHelper.MapTilesY + playerLocalY) * Constants.PicY + yOffset;

        float camX = pwx - ViewW / 2f;
        float camY = pwy - ViewH / 2f;

        bool hasLeftCol = grid[0, 0] is not null || grid[0, 1] is not null || grid[0, 2] is not null;
        bool hasRightCol = grid[2, 0] is not null || grid[2, 1] is not null || grid[2, 2] is not null;
        bool hasTopRow = grid[0, 0] is not null || grid[1, 0] is not null || grid[2, 0] is not null;
        bool hasBotRow = grid[0, 2] is not null || grid[1, 2] is not null || grid[2, 2] is not null;

        // Scroll bounds reach the grid edge when that side has any map, else the center edge.
        float minCamX = hasLeftCol ? 0f : MapPxW;
        float maxCamX = (hasRightCol ? 3 * MapPxW : 2 * MapPxW) - ViewW;
        float minCamY = hasTopRow ? 0f : MapPxH;
        float maxCamY = (hasBotRow ? 3 * MapPxH : 2 * MapPxH) - ViewH;

        CameraX = Math.Clamp(camX, minCamX, maxCamX);
        CameraY = Math.Clamp(camY, minCamY, maxCamY);
    }

    /// <summary>Screen position (FLOAT, sub-pixel) of a world tile's top-left, including sub-tile
    /// movement offset.  Kept sub-pixel so the supersampled world target scrolls smoothly AND the
    /// camera-centered player lands on an exact pixel (no wobble); the composite is a plain downscale
    /// with no fractional slide.</summary>
    public (float sx, float sy) WorldTileToScreen(int worldTileX, int worldTileY, float xOff, float yOff)
        => (worldTileX * Constants.PicX + xOff - CameraX,
            worldTileY * Constants.PicY + yOff - CameraY);

    /// <summary>True if a world tile is at least partly inside the 512×384 viewport.</summary>
    public bool IsWorldTileVisible(int worldTileX, int worldTileY)
    {
        float sx = worldTileX * Constants.PicX - CameraX;
        float sy = worldTileY * Constants.PicY - CameraY;
        return sx > -Constants.PicX && sx < ViewW && sy > -Constants.PicY && sy < ViewH;
    }

    /// <summary>
    /// Inverse of the tile→screen mapping: which grid cell + local tile a screen
    /// pixel falls on.  Returns null for pixels outside the loaded 3×3 area or off
    /// the top/left of the world.
    /// </summary>
    public GridTileHit? ScreenToGridTile(int screenX, int screenY)
    {
        int worldX = (int)Math.Floor((screenX + CameraX) / Constants.PicX);
        int worldY = (int)Math.Floor((screenY + CameraY) / Constants.PicY);
        if (worldX < 0 || worldY < 0) return null;
        int col = worldX / WorldCoordHelper.MapTilesX;
        int row = worldY / WorldCoordHelper.MapTilesY;
        if (col < 0 || col > 2 || row < 0 || row > 2) return null;
        int localX = worldX % WorldCoordHelper.MapTilesX;
        int localY = worldY % WorldCoordHelper.MapTilesY;
        return new GridTileHit(col, row, localX, localY);
    }
}

/// <summary>Where a screen pixel landed: which cell of the loaded 3×3 map grid
/// (<see cref="Col"/>, <see cref="Row"/>) and which tile within that map
/// (<see cref="LocalX"/>, <see cref="LocalY"/>). Named rather than a four-<c>int</c> tuple — the two
/// pairs are in different coordinate spaces, and swapping one for the other reads fine and points at
/// the wrong map.</summary>
public readonly record struct GridTileHit(int Col, int Row, int LocalX, int LocalY);
