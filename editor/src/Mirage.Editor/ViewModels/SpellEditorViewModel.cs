using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The spell-list editor. Adds two category filters on top of the inherited name filter —
/// spell type and class requirement — plus id-to-entry adapters so the GiveItem and class pickers can
/// bind to <see cref="Mirage.Editor.Models.NamedEntry"/> while the record stores a bare id.</summary>
public sealed partial class SpellEditorViewModel : EditorViewModelBase<SpellRowViewModel>
{
    [ObservableProperty] private SpellRowViewModel? _selectedSpell;
    public override SpellRowViewModel? Selected => SelectedSpell;
    public ObservableCollection<SpellRowViewModel> Spells { get; } = [];
    public override ObservableCollection<SpellRowViewModel> Items => Spells;
    /// <inheritdoc/>
    protected override string GetFilterText(SpellRowViewModel row) => row.DisplayName;
    public IEnumerable<SpellType> SpellTypes { get; } = Enum.GetValues<SpellType>();
    private static readonly IReadOnlyList<EnumFilterOption<SpellType>> _spellTypeFilters =
        Enum.GetValues<SpellType>().Select(t => new EnumFilterOption<SpellType>(t)).ToArray();
    public IReadOnlyList<EnumFilterOption<SpellType>> SpellTypeFilters => _spellTypeFilters;
    [ObservableProperty] private EnumFilterOption<SpellType>? _typeFilter;
    partial void OnTypeFilterChanged(EnumFilterOption<SpellType>? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    [RelayCommand] private void ClearTypeFilter() => TypeFilter = null;

    public NamedEntry[] ItemEntries => _data.LiveItemEntries;
    /// <summary>Picker adapter for a GiveItem spell's target item: maps the row's numeric Data1 to and
    /// from an entry. Null when nothing is selected or Data1 is 0.</summary>
    public NamedEntry? SelectedGiveItem
    {
        get
        {
            if (SelectedSpell is null) return null;
            var id = SelectedSpell.Data1;
            return id > 0 && id < ItemEntries.Length ? ItemEntries[id] : null;
        }
        set
        {
            if (SelectedSpell is null) return;
            var id = (short)(value?.Id ?? 0);
            if (SelectedSpell.Data1 == id) return;
            SelectedSpell.Data1 = id;
            OnPropertyChanged(nameof(SelectedGiveItem));
        }
    }

    public NamedEntry[] ClassEntries => _data.LiveClassEntries;
    /// <summary>Class entries for the FILTER dropdown, dropping the id-0 "(none)" sentinel — filtering
    /// on "any class" would be the same as no filter at all.</summary>
    public NamedEntry[] ClassReqFilterEntries => _data.LiveClassEntries.Skip(1).ToArray();
    [ObservableProperty] private NamedEntry? _classReqFilter;
    partial void OnClassReqFilterChanged(NamedEntry? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    [RelayCommand] private void ClearClassReqFilter() => ClassReqFilter = null;

    /// <summary>Name filter (inherited) AND the type / class-requirement filters. While either category
    /// filter is active, unset slots are skipped — they default to AddHp with Data1 = 0 and would
    /// otherwise flood every type-based result.</summary>
    protected override bool MatchesFilter(SpellRowViewModel row) =>
        base.MatchesFilter(row) &&
        // When a category filter is active, skip unset slots — they default to AddHp/Data1=0
        // and would otherwise flood any type-based filter result.
        ((TypeFilter is null && ClassReqFilter is null) || (!string.IsNullOrEmpty(row.Name) && row.Data1 != 0)) &&
        (TypeFilter is null || row.Type == TypeFilter.Value) &&
        (ClassReqFilter is null || row.ClassReq == ClassReqFilter.Id);
    public override bool IsFilterActive => base.IsFilterActive || TypeFilter is not null || ClassReqFilter is not null;

    /// <summary>Picker adapter for the class requirement, mapping the row's numeric ClassReq to and from
    /// an entry. Null means "any class" (id 0).</summary>
    public NamedEntry? SelectedClassReq
    {
        get
        {
            var id = SelectedSpell?.ClassReq ?? 0;
            return id > 0 && id < ClassEntries.Length ? ClassEntries[id] : null;
        }
        set
        {
            if (SelectedSpell is null) return;
            var id = value?.Id ?? 0;
            if (SelectedSpell.ClassReq == id) return;
            SelectedSpell.ClassReq = id;
            OnPropertyChanged(nameof(SelectedClassReq));
        }
    }

    public SpellEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () =>
        {
            OnPropertyChanged(nameof(ItemEntries));
            OnPropertyChanged(nameof(ClassEntries));
            OnPropertyChanged(nameof(ClassReqFilterEntries));
        };
    }

    /// <summary>Patch the cached online name index after a save, so the list caption reflects a renamed
    /// record without re-fetching the whole index.</summary>
    protected override void AfterSave(SpellRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineSpellName(vm.Index, vm.Name);
    }

    protected override string TypeName => EditorStrings.Get(EditorStrings.SpellEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.SpellEditor_TypeNamePlural);
    /// <inheritdoc/>
    protected override int GetIndex(SpellRowViewModel vm) => vm.Index;
    /// <inheritdoc/>
    protected override bool GetIsDirty(SpellRowViewModel vm) => vm.IsDirty;
    /// <inheritdoc/>
    protected override void ClearDirtyState(SpellRowViewModel vm) => vm.ClearDirty();

    /// <summary>Pre-fill every placeholder row from one bulk server response, so browsing the list after
    /// connecting is instant instead of fetching per selection. No-op offline; canceled on disconnect.</summary>
    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllSpellsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Spells)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.SpellNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedSpellChanged(SpellRowViewModel? oldValue, SpellRowViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnSpellPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnSpellPropertyChanged;
        NotifyDirtyState();
        if (newValue is not null && !newValue.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(newValue);
        OnPropertyChanged(nameof(SelectedClassReq));
        OnPropertyChanged(nameof(SelectedGiveItem));
    }

    private void OnSpellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SpellRowViewModel.ClassReq))
            OnPropertyChanged(nameof(SelectedClassReq));
        if (e.PropertyName is nameof(SpellRowViewModel.Data1))
            OnPropertyChanged(nameof(SelectedGiveItem));
    }

    /// <summary>Rebuild the list from the on-disk records, fully populated — offline editing has no
    /// server to lazy-load from.</summary>
    public void LoadOffline()
    {
        SelectedSpell = null;
        Spells.Clear();
        for (int i = 1; i < _data.OfflineSpells.Length; i++)
            Spells.Add(new SpellRowViewModel(i, _data.OfflineSpells[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Spells.Count), ("EntityType", TypeNamePlural));
    }

    /// <summary>Rebuild the list from the server's name index as NAME-ONLY placeholders
    /// (<c>isLoaded: false</c>). Each row's full definition arrives when it is selected, or sooner via
    /// <see cref="EagerLoadAllAsync"/>.</summary>
    public void LoadOnline()
    {
        if (_data.OnlineSpells is null) return;
        SelectedSpell = null;
        Spells.Clear();
        foreach (var entry in _data.OnlineSpells)
            Spells.Add(new SpellRowViewModel(entry.Num, new SpellRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Spells.Count), ("EntityType", TypeNamePlural));
    }

    /// <inheritdoc/>
    protected override async Task<IPacket?> RequestFromServerAsync(SpellRowViewModel vm)
        => await _conn.RequestSpellAsync(vm.Index);

    /// <inheritdoc/>
    protected override void ApplyServerResponse(SpellRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateSpellPacket)pkt);

    /// <inheritdoc/>
    protected override IPacket BuildSavePacket(SpellRowViewModel vm) => vm.BuildSavePacket();

    /// <inheritdoc/>
    protected override Task SaveOfflineAsync(SpellRowViewModel vm)
        => _data.SaveOfflineSpellAsync(vm.Index, vm.ToRecord());

    /// <inheritdoc/>
    protected override void LoadFromOfflineRecord(SpellRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineSpells[vm.Index]);
}
