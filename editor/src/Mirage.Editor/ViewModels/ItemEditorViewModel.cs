using Avalonia.Media.Imaging;
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

public sealed partial class ItemEditorViewModel : EditorViewModelBase<ItemRowViewModel>
{
    [ObservableProperty] private ItemRowViewModel? _selectedItem;
    public override ItemRowViewModel? Selected => SelectedItem;
    public override ObservableCollection<ItemRowViewModel> Items { get; } = [];
    protected override string GetFilterText(ItemRowViewModel row) => row.DisplayName;
    public IEnumerable<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();
    private static readonly IReadOnlyList<EnumFilterOption<ItemType>> _itemTypeFilters =
        Enum.GetValues<ItemType>().Where(t => t != ItemType.None).Select(t => new EnumFilterOption<ItemType>(t)).ToArray();
    public IReadOnlyList<EnumFilterOption<ItemType>> ItemTypeFilters => _itemTypeFilters;
    [ObservableProperty] private EnumFilterOption<ItemType>? _typeFilter;
    partial void OnTypeFilterChanged(EnumFilterOption<ItemType>? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    protected override bool MatchesFilter(ItemRowViewModel row) =>
        base.MatchesFilter(row) && (TypeFilter is null || row.Type == TypeFilter.Value);
    public override bool IsFilterActive => base.IsFilterActive || TypeFilter is not null;
    [RelayCommand] private void ClearTypeFilter() => TypeFilter = null;
    [ObservableProperty] private Bitmap? _itemBitmap;
    public IReadOnlyList<int> ItemPicEntries { get; private set; } = [];
    partial void OnItemBitmapChanged(Bitmap? value)
    {
        int count = value is null ? 0 : (int)(value.Size.Height / 32);
        ItemPicEntries = Enumerable.Range(0, count).ToArray();
        OnPropertyChanged(nameof(ItemPicEntries));
    }

    public ItemEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () =>
        {
            OnPropertyChanged(nameof(SpellEntries));
            OnPropertyChanged(nameof(ClassEntries));
        };
    }

    public NamedEntry[] ClassEntries => _data.LiveClassEntries;

    /// <summary>Selected class for the equipment Data3 class-requirement field. Mirrors the
    /// SpellEditorViewModel.SelectedClassReq pattern — Data3 = 0 means no class restriction.</summary>
    public NamedEntry? SelectedClassReq
    {
        get
        {
            if (SelectedItem is null) return null;
            var id = SelectedItem.Data3;
            return id > 0 && id < ClassEntries.Length ? ClassEntries[id] : null;
        }
        set
        {
            if (SelectedItem is null) return;
            var id = (short)(value?.Id ?? 0);
            if (SelectedItem.Data3 == id) return;
            SelectedItem.Data3 = id;
            OnPropertyChanged(nameof(SelectedClassReq));
        }
    }

    protected override void AfterSave(ItemRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineItem(vm.Index, vm.Name, vm.Type);
    }

    protected override string TypeName => EditorStrings.Get(EditorStrings.ItemEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.ItemEditor_TypeNamePlural);
    protected override int GetIndex(ItemRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(ItemRowViewModel vm) => vm.IsDirty;
    protected override void ClearDirtyState(ItemRowViewModel vm) => vm.ClearDirty();

    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllItemsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Items)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.ItemNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    public NamedEntry[] SpellEntries => _data.LiveSpellEntries;

    public NamedEntry? SelectedSpellItem
    {
        get
        {
            if (SelectedItem is null) return null;
            var id = SelectedItem.Data1;
            return id > 0 && id < SpellEntries.Length ? SpellEntries[id] : null;
        }
        set
        {
            if (SelectedItem is null) return;
            var id = (short)(value?.Id ?? 0);
            if (SelectedItem.Data1 == id) return;
            SelectedItem.Data1 = id;
            OnPropertyChanged(nameof(SelectedSpellItem));
        }
    }

    partial void OnSelectedItemChanged(ItemRowViewModel? oldValue, ItemRowViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnItemPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnItemPropertyChanged;
        NotifyDirtyState();
        if (newValue is not null && !newValue.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(newValue);
        OnPropertyChanged(nameof(SelectedSpellItem));
        OnPropertyChanged(nameof(SelectedClassReq));
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ItemRowViewModel.Data1) or nameof(ItemRowViewModel.Type))
            OnPropertyChanged(nameof(SelectedSpellItem));
        if (e.PropertyName is nameof(ItemRowViewModel.Data3))
            OnPropertyChanged(nameof(SelectedClassReq));
    }

    public void LoadOffline()
    {
        SelectedItem = null;
        Items.Clear();
        for (int i = 1; i < _data.OfflineItems.Length; i++)
            Items.Add(new ItemRowViewModel(i, _data.OfflineItems[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Items.Count), ("EntityType", TypeNamePlural));
    }

    public void LoadOnline()
    {
        if (_data.OnlineItems is null) return;
        SelectedItem = null;
        Items.Clear();
        foreach (var entry in _data.OnlineItems)
            Items.Add(new ItemRowViewModel(entry.Num, new ItemRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Items.Count), ("EntityType", TypeNamePlural));
    }

    protected override async Task<IPacket?> RequestFromServerAsync(ItemRowViewModel vm)
        => await _conn.RequestItemAsync(vm.Index);

    protected override void ApplyServerResponse(ItemRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateItemPacket)pkt);

    protected override IPacket BuildSavePacket(ItemRowViewModel vm) => vm.BuildSavePacket();

    protected override Task SaveOfflineAsync(ItemRowViewModel vm)
        => _data.SaveOfflineItemAsync(vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(ItemRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineItems[vm.Index]);
}
