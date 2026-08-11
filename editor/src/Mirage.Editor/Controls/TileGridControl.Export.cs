using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using SkiaSharp;
using System.Globalization;

namespace Mirage.Editor.Controls;

/// <summary>Rendering maps to a bitmap for PNG export — the same cell drawing without the grid,
/// overlays or selection chrome.</summary>
public sealed partial class TileGridControl : Control
{
    // ── PNG export (clean map art) ─────────────────────────────────────────────
    // The size of one map in native pixels — the export unit shared by all three export commands.
    public const int MapPixelW = GridCols * TileW; // 512
    public const int MapPixelH = GridRows * TileH; // 384

    // Draws the base Ground+Fringe of each placed map (no grid line, no attribute/light overlays, anim
    // layers shown) into `ctx`, clipped to the world band [bandTopPx, bandTopPx + bandHeightPx): a map's
    // pixel row that falls in the band is translated so the band's top sits at ctx-y 0.  Each placement is
    // (MapRecord, world PixelX, world PixelY).  The caller pre-fills the target black (opaque backdrop for
    // gaps and color-keyed-transparent tile pixels).  Used both for whole-image render (one full-height
    // band) and for streaming a large world one band at a time — see MapImageExport.
    public static void DrawPlacementsBand(DrawingContext ctx,
        IReadOnlyList<(MapRecord Map, int PixelX, int PixelY)> placements,
        IReadOnlyList<Bitmap?> tilesets, int bandTopPx, int bandHeightPx)
    {
        int bandBottomPx = bandTopPx + bandHeightPx;
        foreach (var (map, ox, oy) in placements)
        {
            if (oy + MapPixelH <= bandTopPx || oy >= bandBottomPx) continue; // map wholly outside the band
            for (int y = 0; y < GridRows; y++)
            {
                int tileTop = oy + y * TileH;
                if (tileTop + TileH <= bandTopPx || tileTop >= bandBottomPx) continue;
                for (int x = 0; x < GridCols; x++)
                {
                    var tile = map.Tile[x, y];
                    var dst = new Rect(ox + x * TileW, tileTop - bandTopPx, TileW, TileH);
                    DrawLayerStack(ctx, tilesets, tile.Ground, dst, animFrame: -1, hideIndex: -1);
                    DrawLayerStack(ctx, tilesets, tile.Fringe, dst, animFrame: -1, hideIndex: -1);
                }
            }
        }
    }

    // Renders the placed maps to a single PNG-ready bitmap (whole image in memory).  For the small exports
    // (one map, or the 3×3 observable area); the world export streams instead (MapImageExport).  Must be
    // called on the UI thread (RenderTargetBitmap is UI-thread-affine); the caller disposes the result.
    public static RenderTargetBitmap RenderMapsToBitmap(
        IReadOnlyList<(MapRecord Map, int PixelX, int PixelY)> placements,
        IReadOnlyList<Bitmap?> tilesets, int widthPx, int heightPx)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(widthPx, heightPx), new Vector(96, 96));
        using var ctx = rtb.CreateDrawingContext();
        ctx.FillRectangle(Brushes.Black, new Rect(0, 0, widthPx, heightPx));
        DrawPlacementsBand(ctx, placements, tilesets, bandTopPx: 0, bandHeightPx: heightPx);
        return rtb;
    }

    // The packed LayerCell value of the currently-selected layer (type + index) on a tile.
    private int SelectedLayerCellOf(TileRecord t)
    {
        var layers = SelectedLayerType switch
        {
            LayerType.Ground => t.Ground,
            LayerType.Fringe => t.Fringe,
            _ => t.Canopy,
        };
        int i = SelectedLayerIndex - 1;
        return i >= 0 && i < layers.Length ? layers[i] : LayerCell.Empty;
    }

    // ── Layout ────────────────────────────────────────────────────────────────
    protected override Size MeasureOverride(Size _) =>
        new(TotalCols * TileW * Zoom, TotalRows * TileH * Zoom);
}
