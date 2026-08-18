using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The map list: the row collection, the name filter over it, the per-row subscriptions,
/// and the dirty flags the save commands read.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // Tracks the map row we've subscribed to for Record changes
    private MapRowViewModel? _subscribedMap;
    private readonly List<MapRowViewModel> _subscribedMapRows = [];

    public bool IsSelectedMapDirty => SelectedMap is not null && SelectedMap.IsDirty;
    public bool HasAnyDirtyMap => Maps.Any(m => m.IsDirty);

    private void NotifyMapDirtyState()
    {
        OnPropertyChanged(nameof(IsSelectedMapDirty));
        OnPropertyChanged(nameof(HasAnyDirtyMap));
    }

    private void HookMaps()
    {
        Maps.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _subscribedMapRows)
                    row.PropertyChanged -= OnMapItemPropertyChanged;
                _subscribedMapRows.Clear();
                NotifyMapDirtyState();
                OnPropertyChanged(nameof(FilteredMaps));
                OnPropertyChanged(nameof(FilterStatus));
                return;
            }
            if (e.NewItems is not null)
            {
                foreach (MapRowViewModel row in e.NewItems.Cast<MapRowViewModel>())
                {
                    row.PropertyChanged += OnMapItemPropertyChanged;
                    _subscribedMapRows.Add(row);
                }
            }

            if (e.OldItems is not null)
            {
                foreach (MapRowViewModel row in e.OldItems.Cast<MapRowViewModel>())
                {
                    row.PropertyChanged -= OnMapItemPropertyChanged;
                    _subscribedMapRows.Remove(row);
                }
            }

            NotifyMapDirtyState();
            OnPropertyChanged(nameof(FilteredMaps));
            OnPropertyChanged(nameof(FilterStatus));
        };
    }

    private void OnMapItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsDirty")
            NotifyMapDirtyState();
    }

    public ObservableCollection<MapRowViewModel> Maps { get; } = [];
    public IEnumerable<MapRowViewModel> FilteredMaps =>
        string.IsNullOrEmpty(FilterText)
            ? Maps
            : Maps.Where(m => m.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
    public bool IsFilterActive => !string.IsNullOrEmpty(FilterText);
    public string FilterStatus => EditorStrings.Format(EditorStrings.Status_FilterCount,
        ("Filtered", FilteredMaps.Count()), ("Total", Maps.Count));
    [RelayCommand] private void ClearFilter() => FilterText = "";
}
