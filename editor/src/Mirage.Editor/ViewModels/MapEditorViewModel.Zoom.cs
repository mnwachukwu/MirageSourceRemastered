using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mirage.Editor.ViewModels;

/// <summary>Canvas zoom, and the two callbacks the view hands back so the view model can invalidate
/// the tile grid without holding a reference to the control.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // Canvas zoom factor: 1.0 = one screen pixel per map pixel. Bound by the view's zoom transform.
    [ObservableProperty] private double _mapZoom = 1.0;

    /// <summary>Redraws one tile at (x, y). Set by the view; null in headless tests.</summary>
    public Action<int, int>? InvalidateTileGrid { get; set; }

    /// <summary>Redraws the whole grid — for edits whose effect is not confined to one tile (a fill,
    /// a paste, a map switch, a light change). Set by the view; null in headless tests.</summary>
    public Action? InvalidateAllTiles { get; set; }

    // Zoom steps by a factor of 1.5 per press and clamps at 1/8x .. 4x, so the extremes stay
    // reachable in a few presses without the canvas becoming unusable at either end.
    [RelayCommand]
    private void ZoomIn() => MapZoom = Math.Min(MapZoom * 1.5, 4.0);

    [RelayCommand]
    private void ZoomOut() => MapZoom = Math.Max(MapZoom / 1.5, 0.125);

    [RelayCommand]
    private void ZoomReset() => MapZoom = 1.0;
}
