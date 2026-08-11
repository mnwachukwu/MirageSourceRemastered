using Microsoft.Xna.Framework;
using Mirage.Shared;

namespace Mirage.Client.Shell.Rendering;

/// <summary>
/// Maps a (sheet, 1-based tile number) pair to a source rectangle in that tileset's texture.
/// Each sheet's column count is derived from its own texture width, so sheets may differ in size.
/// </summary>
public static class TileAtlas
{
    // Column count per sheet, indexed by sheet number. Defaults to the original 7-wide layout.
    private static int[] _colsPerSheet = [7];

    /// <summary>Call once after the tileset textures are loaded, passing each sheet's pixel width
    /// (index = sheet number; a 0 width yields a 1-column fallback for that slot).</summary>
    public static void Init(int[] sheetWidths)
    {
        if (sheetWidths is null || sheetWidths.Length == 0)
        {
            _colsPerSheet = [7];
            return;
        }
        var cols = new int[sheetWidths.Length];
        for (int i = 0; i < sheetWidths.Length; i++)
            cols[i] = Math.Max(1, sheetWidths[i] / Constants.PicX);
        _colsPerSheet = cols;
    }

    public static Rectangle GetSourceRect(int sheet, int tileNum)
    {
        if (tileNum <= 0) return Rectangle.Empty;
        if (sheet < 0 || sheet >= _colsPerSheet.Length) return Rectangle.Empty;
        int cols = _colsPerSheet[sheet];
        int index = tileNum - 1;
        int col = index % cols;
        int row = index / cols;
        return new Rectangle(col * Constants.PicX, row * Constants.PicY, Constants.PicX, Constants.PicY);
    }
}
