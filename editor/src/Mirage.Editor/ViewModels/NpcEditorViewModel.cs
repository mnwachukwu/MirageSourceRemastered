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

public sealed partial class NpcEditorViewModel : EditorViewModelBase<NpcRowViewModel>
{
    [ObservableProperty] private NpcRowViewModel? _selectedNpc;
    public override NpcRowViewModel? Selected => SelectedNpc;
    public ObservableCollection<NpcRowViewModel> Npcs { get; } = [];
    public override ObservableCollection<NpcRowViewModel> Items => Npcs;
    protected override string GetFilterText(NpcRowViewModel row) => row.DisplayName;
    public IEnumerable<NpcBehavior> Behaviors { get; } = Enum.GetValues<NpcBehavior>();
    public IEnumerable<FlickerStyle> FlickerStyles { get; } = Enum.GetValues<FlickerStyle>();
    // NPC footprint sizes shown as pixel dimensions (32x32 / 64x64 / 96x96); the bound value is the 1..N class.
    public IReadOnlyList<NpcSizeOption> NpcSizes { get; } =
        Enumerable.Range(1, Constants.MaxNpcSize).Select(s => new NpcSizeOption(s)).ToArray();

    // Two-way bridge for the Size ComboBox: reads/writes SelectedNpc.Size as an NpcSizeOption (Avalonia's
    // ComboBox has no SelectedValuePath, so the int<->option mapping lives here). Refreshed on NPC change.
    public NpcSizeOption? SelectedNpcSize
    {
        get => SelectedNpc is null ? null : NpcSizes.FirstOrDefault(o => o.Value == SelectedNpc.Size);
        set { if (SelectedNpc is not null && value is not null) SelectedNpc.Size = value.Value; }
    }
    private static readonly IReadOnlyList<EnumFilterOption<NpcBehavior>> _behaviorFilters =
        Enum.GetValues<NpcBehavior>().Select(b => new EnumFilterOption<NpcBehavior>(b)).ToArray();
    public IReadOnlyList<EnumFilterOption<NpcBehavior>> BehaviorFilters => _behaviorFilters;
    [ObservableProperty] private EnumFilterOption<NpcBehavior>? _behaviorFilter;
    partial void OnBehaviorFilterChanged(EnumFilterOption<NpcBehavior>? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    protected override bool MatchesFilter(NpcRowViewModel row) =>
        base.MatchesFilter(row) && (BehaviorFilter is null || row.Behavior == BehaviorFilter.Value);
    public override bool IsFilterActive => base.IsFilterActive || BehaviorFilter is not null;
    [RelayCommand] private void ClearBehaviorFilter() => BehaviorFilter = null;

    public NamedEntry[] ItemEntries => _data.LiveItemEntries;
    [ObservableProperty] private Bitmap? _spriteBitmap;
    public IReadOnlyList<int> SpriteEntries { get; private set; } = [];
    partial void OnSpriteBitmapChanged(Bitmap? value)
    {
        int count = value is null ? 0 : (int)(value.Size.Height / 32) - 1;
        SpriteEntries = Enumerable.Range(0, Math.Max(0, count) + 1).ToArray();
        OnPropertyChanged(nameof(SpriteEntries));
    }

    public NamedEntry? SelectedDropItem
    {
        get
        {
            var id = SelectedNpc?.DropItem ?? 0;
            return id > 0 && id < _data.LiveItemEntries.Length ? _data.LiveItemEntries[id] : null;
        }
        set
        {
            if (SelectedNpc is null) return;
            var id = value?.Id ?? 0;
            if (SelectedNpc.DropItem == id) return;
            SelectedNpc.DropItem = id;
            OnPropertyChanged(nameof(SelectedDropItem));
        }
    }

    // Currency drops need a quantity (>= 1); every other item type ignores it (should be 0). The server
    // enforces both on save; this surfaces the same rule live while editing.
    public string DropValueWarning
    {
        get
        {
            var npc = SelectedNpc;
            if (npc is null || npc.DropItem <= 0) return string.Empty;
            bool isCurrency = _data.IsCurrencyItem(npc.DropItem);
            if (isCurrency && npc.DropItemValue < 1)
                return EditorStrings.Get(EditorStrings.NpcEditor_DropWarnCurrencyQty);
            if (!isCurrency && npc.DropItemValue > 0)
                return EditorStrings.Get(EditorStrings.NpcEditor_DropWarnNonCurrencyQty);
            return string.Empty;
        }
    }
    public bool HasDropValueWarning => DropValueWarning.Length > 0;
    private void NotifyDropValueWarning()
    {
        OnPropertyChanged(nameof(DropValueWarning));
        OnPropertyChanged(nameof(HasDropValueWarning));
    }

    public NpcEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        // Refresh the item dropdown AND the drop-value warning: an item's currency-ness can flip under a
        // selected NPC without DropItem/DropItemValue changing, which would otherwise leave the warning stale.
        _data.EntriesInvalidated += () => { OnPropertyChanged(nameof(ItemEntries)); NotifyDropValueWarning(); };
    }

    protected override void AfterSave(NpcRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineNpcName(vm.Index, vm.Name);
    }

    protected override string TypeName => EditorStrings.Get(EditorStrings.NpcEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.NpcEditor_TypeNamePlural);
    protected override int GetIndex(NpcRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(NpcRowViewModel vm) => vm.IsDirty;
    protected override void ClearDirtyState(NpcRowViewModel vm) => vm.ClearDirty();

    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllNpcsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Npcs)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.NpcNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedNpcChanged(NpcRowViewModel? oldValue, NpcRowViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnNpcPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnNpcPropertyChanged;
        NotifyDirtyState();
        OnPropertyChanged(nameof(SelectedDropItem));
        OnPropertyChanged(nameof(SelectedNpcSize));
        NotifyDropValueWarning();
        if (newValue is not null && !newValue.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(newValue);
    }
    private void OnNpcPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NpcRowViewModel.DropItem))
        {
            OnPropertyChanged(nameof(SelectedDropItem));
            NotifyDropValueWarning();
        }
        else if (e.PropertyName == nameof(NpcRowViewModel.DropItemValue))
        {
            NotifyDropValueWarning();
        }
    }

    public void LoadOffline()
    {
        SelectedNpc = null;
        Npcs.Clear();
        for (int i = 1; i < _data.OfflineNpcs.Length; i++)
            Npcs.Add(new NpcRowViewModel(i, _data.OfflineNpcs[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Npcs.Count), ("EntityType", TypeNamePlural));
    }

    public void LoadOnline()
    {
        if (_data.OnlineNpcs is null) return;
        SelectedNpc = null;
        Npcs.Clear();
        foreach (var entry in _data.OnlineNpcs)
            Npcs.Add(new NpcRowViewModel(entry.Num, new NpcRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Npcs.Count), ("EntityType", TypeNamePlural));
    }

    protected override async Task<IPacket?> RequestFromServerAsync(NpcRowViewModel vm)
        => await _conn.RequestNpcAsync(vm.Index);

    protected override void ApplyServerResponse(NpcRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateNpcPacket)pkt);

    protected override IPacket BuildSavePacket(NpcRowViewModel vm) => vm.BuildSavePacket();

    protected override Task SaveOfflineAsync(NpcRowViewModel vm)
        => _data.SaveOfflineNpcAsync(vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(NpcRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineNpcs[vm.Index]);
}

/// <summary>A selectable NPC footprint size shown as its pixel dimensions (e.g. "64x64"); <see cref="Value"/>
/// is the 1..N size class stored on the NPC (bound via the ComboBox's SelectedValuePath).</summary>
public sealed record NpcSizeOption(int Value)
{
    public override string ToString() => $"{Value * Constants.PicX}x{Value * Constants.PicX}";
}
