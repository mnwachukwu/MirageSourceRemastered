using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Shared;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>One record family's ceiling. Clamped as it is typed, so the dialog cannot hold a value the
/// world would refuse.</summary>
public sealed partial class WorldLimitRowViewModel(string labelKey, int value) : ObservableObject
{
    public string Label => EditorStrings.Get(labelKey);

    [ObservableProperty] private int _value = value;

    partial void OnValueChanged(int value)
    {
        int clamped = Math.Clamp(value, 1, RecordLimits.Ceiling);
        if (clamped != value) Value = clamped;
    }
}

/// <summary>
/// A world's record ceilings: how many items, NPCs, maps and the rest it has room for.
///
/// <para>Read from and written to <c>world.json</c> at the world's root, so the size travels with the
/// folder and two worlds of different sizes can be open in turn. Lowering a ceiling hides the slots above
/// it from the pickers; it deletes nothing, and raising it again brings them back.</para>
///
/// <para>Offline only. Connected, the ceilings are the server's and are stated in the hello.</para>
/// </summary>
public sealed partial class WorldSettingsDialogViewModel : ObservableObject
{
    public ObservableCollection<WorldLimitRowViewModel> Rows { get; } = [];

    /// <summary>False while connected, which disables every field.</summary>
    public bool IsConfigurable { get; }

    public string OfflineOnlyNotice => EditorStrings.Get(EditorStrings.WorldSettings_OfflineOnlyNotice);
    public string Intro => EditorStrings.Get(EditorStrings.WorldSettings_Intro);

    public event Action<RecordLimits>? Confirmed;
    public event Action? Canceled;

    public WorldSettingsDialogViewModel(RecordLimits limits, bool isOnline)
    {
        IsConfigurable = !isOnline;
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Items"), limits.Items));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("NPCs"), limits.Npcs));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Shops"), limits.Shops));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Spells"), limits.Spells));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Quests"), limits.Quests));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Conversations"), limits.Conversations));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("Maps"), limits.Maps));
        Rows.Add(new(MainWindowViewModel.SectionLabelKey("MapGroups"), limits.MapGroups));
    }

    private int At(int i) => Rows[i].Value;

    [RelayCommand]
    private void Confirm() => Confirmed?.Invoke(new RecordLimits
    {
        Items = At(0),
        Npcs = At(1),
        Shops = At(2),
        Spells = At(3),
        Quests = At(4),
        Conversations = At(5),
        Maps = At(6),
        MapGroups = At(7),
    }.Clamped(RecordLimits.Ceiling));

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}
