using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Mirage.Editor.ViewModels;

/// <summary>Change notification for the selected map's own properties, and the hook that keeps the
/// view in step when the selected map's record is replaced elsewhere.
///
/// <para>RECOVERED: these two methods were lost to a bad file split and restored from the compiled
/// assembly. The bodies are the exact set of notifications the previous build emitted; the
/// <c>nameof</c> form and these comments were reconstructed (the compiler bakes <c>nameof</c> down to
/// a string literal, so that much could not survive the round-trip). If either reads oddly against
/// your memory of the original, this is the file to check.</para></summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    /// <summary>Re-raises every property the map Properties panel and the neighbour grid bind to.
    /// Called whenever the selected map changes wholesale — a different map selected, a record
    /// swapped in by a load, or an edit applied outside the bound setters — because those paths
    /// mutate the underlying <c>MapRecord</c> without going through the individual properties.
    /// <para>Also rebuilds the map's NPC-spawn rows, which are derived from the record rather than
    /// bound to it.</para></summary>
    private void NotifyMapProperties()
    {
        OnPropertyChanged(nameof(MapName));
        OnPropertyChanged(nameof(MapDisplayName));
        OnPropertyChanged(nameof(SelectedMapMoral));
        OnPropertyChanged(nameof(MapGroup));
        OnPropertyChanged(nameof(SelectedMapGroup));
        OnPropertyChanged(nameof(MapUp));
        OnPropertyChanged(nameof(MapDown));
        OnPropertyChanged(nameof(MapLeft));
        OnPropertyChanged(nameof(MapRight));
        OnPropertyChanged(nameof(MapMusic));
        OnPropertyChanged(nameof(MapBootMap));
        OnPropertyChanged(nameof(MapBootX));
        OnPropertyChanged(nameof(MapBootY));
        OnPropertyChanged(nameof(MapGreetingSpeaker));
        OnPropertyChanged(nameof(MapJoinSay));
        OnPropertyChanged(nameof(MapLeaveSay));
        OnPropertyChanged(nameof(MapIndoors));
        OnPropertyChanged(nameof(MapAlwaysDark));
        OnPropertyChanged(nameof(MapRevisionText));
        OnPropertyChanged(nameof(UsedTilesheets));
        OnPropertyChanged(nameof(HasSelectedMap));
        RebuildMapNpcRows();
        OnPropertyChanged(nameof(SelectedMapUp));
        OnPropertyChanged(nameof(SelectedMapDown));
        OnPropertyChanged(nameof(SelectedMapLeft));
        OnPropertyChanged(nameof(SelectedMapRight));
        OnPropertyChanged(nameof(SelectedMapBootMap));
        OnPropertyChanged(nameof(NeighborMapUp));
        OnPropertyChanged(nameof(NeighborMapDown));
        OnPropertyChanged(nameof(NeighborMapLeft));
        OnPropertyChanged(nameof(NeighborMapRight));
        OnPropertyChanged(nameof(NeighborMapUpLeft));
        OnPropertyChanged(nameof(NeighborMapUpRight));
        OnPropertyChanged(nameof(NeighborMapDownLeft));
        OnPropertyChanged(nameof(NeighborMapDownRight));
    }

    /// <summary>Bridges a <see cref="MapRowViewModel"/> edit into this view model. Only the
    /// <c>Record</c> notification matters: the row raises it when its whole record is replaced (see
    /// MapRowViewModel), which invalidates the bound properties, the hovered-tile readout, and every
    /// cached tile in the grid at once.</summary>
    private void OnMapRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapRowViewModel.Record))
        {
            NotifyMapProperties();
            NotifyHoveredTile();
            InvalidateAllTiles?.Invoke();
        }
    }
}
