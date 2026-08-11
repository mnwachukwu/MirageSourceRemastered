using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

public sealed partial class ShopRowViewModel : ObservableObject
{
    public int Index { get; }
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _fixesItems;
    [ObservableProperty] private ShopType _shopType;
    [ObservableProperty] private bool _allowBanking;
    // Keeper: the NPC template this shop/inn is assigned to (0 = none). SelectedKeeper is the NamedEntry facade
    // the DropdownAutoCompleteBox binds to; KeeperEntries is the live NPC list.
    [ObservableProperty] private int _keeper;

    public bool IsStore
    {
        get => ShopType == ShopType.Store;
        set { if (value && ShopType != ShopType.Store) ShopType = ShopType.Store; }
    }
    public bool IsInn
    {
        get => ShopType == ShopType.Inn;
        set { if (value && ShopType != ShopType.Inn) ShopType = ShopType.Inn; }
    }

    public ObservableCollection<TradeRowViewModel> Trades { get; } = [];

    /// <summary>True when the trade table has no rows — drives the editor's empty-state hint.</summary>
    public bool HasNoTrades => Trades.Count == 0;

    public bool IsDirty => _textDirty || _fixesDirty || _typeDirty || _structuralDirty || Trades.Any(t => t.IsDirty);
    private bool _textDirty;
    private bool _fixesDirty;
    private bool _typeDirty;
    private bool _structuralDirty;   // a trade row was added/removed (structural change, not a field edit)
    private bool _loading;

    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    private readonly Func<NamedEntry[]> _entriesProvider;
    private readonly Func<NamedEntry[]> _npcEntriesProvider;
    private readonly Func<int, bool> _isCurrency;

    public NamedEntry[] KeeperEntries => _npcEntriesProvider();

    public NamedEntry? SelectedKeeper
    {
        get => EntryFor(KeeperEntries, Keeper);
        set
        {
            var id = value?.Id ?? 0;
            if (Keeper == id) return;
            Keeper = id;   // OnKeeperChanged marks dirty + re-notifies SelectedKeeper
        }
    }

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;

    public ShopRowViewModel(int index, ShopRecord r, Func<NamedEntry[]> entriesProvider, Func<NamedEntry[]> npcEntriesProvider, Func<int, bool> isCurrency, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _entriesProvider = entriesProvider;
        _npcEntriesProvider = npcEntriesProvider;
        _isCurrency = isCurrency;
        _name = r.Name;
        _fixesItems = r.FixesItems;
        _shopType = r.ShopType;
        _allowBanking = r.AllowBanking;
        _keeper = r.Keeper;

        Trades.CollectionChanged += OnTradesCollectionChanged;
        _loading = true;
        LoadTrades(r);
        _loading = false;
    }

    private void OnTradesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TradeRowViewModel t in e.OldItems)
                t.PropertyChanged -= OnTradeRowPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (TradeRowViewModel t in e.NewItems)
                t.PropertyChanged += OnTradeRowPropertyChanged;
        }

        OnPropertyChanged(nameof(HasNoTrades));
        AddTradeCommand.NotifyCanExecuteChanged();   // re-enable + when a remove drops below the ceiling
        if (_loading) return;
        _structuralDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand(CanExecute = nameof(CanAddTrade))]
    private void AddTrade() =>
        Trades.Add(new TradeRowViewModel(Trades.Count + 1, new TradeItemRecord(), _entriesProvider, _isCurrency));

    private bool CanAddTrade() => Trades.Count < Constants.MaxTrades;

    [RelayCommand]
    private void RemoveTrade(TradeRowViewModel row) => Trades.Remove(row);

    private void OnTradeRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        OnPropertyChanged(nameof(IsDirty));
    }

    partial void OnNameChanged(string value)
    {
        if (_loading) return;
        _textDirty = true;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnFixesItemsChanged(bool value)
    {
        if (_loading) return;
        _fixesDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnShopTypeChanged(ShopType value)
    {
        if (_loading) return;
        _typeDirty = true;
        OnPropertyChanged(nameof(IsStore));
        OnPropertyChanged(nameof(IsInn));
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnAllowBankingChanged(bool value)
    {
        if (_loading) return;
        _typeDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnKeeperChanged(int value)
    {
        // Refresh the dropdown selection even while loading a record; only an actual edit dirties the row.
        OnPropertyChanged(nameof(SelectedKeeper));
        if (_loading) return;
        _typeDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    public void ClearDirty()
    {
        _textDirty = _fixesDirty = _typeDirty = _structuralDirty = false;
        foreach (var t in Trades) t.ClearDirty();
        OnPropertyChanged(nameof(IsDirty));
    }

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(KeeperEntries));
        OnPropertyChanged(nameof(SelectedKeeper));
        foreach (var t in Trades) t.NotifyEntriesChanged();
    }

    // Build trade rows from a record, skipping empties and the legacy null-at-index-0, so the table shows
    // only real trades (dense). Callers set _loading around this so the rebuild doesn't dirty the row.
    private void LoadTrades(ShopRecord r)
    {
        Trades.Clear();
        int slot = 1;
        foreach (var t in r.TradeItem)
        {
            if (t is null || (t.GiveItem <= 0 && t.GetItem <= 0)) continue;
            Trades.Add(new TradeRowViewModel(slot++, t, _entriesProvider, _isCurrency));
        }
    }

    public void LoadFromRecord(ShopRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            FixesItems = r.FixesItems;
            ShopType = r.ShopType;
            AllowBanking = r.AllowBanking;
            Keeper = r.Keeper;
            LoadTrades(r);
        }
        finally
        {
            _loading = false;
        }
        _textDirty = _fixesDirty = _typeDirty = _structuralDirty = false;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void ApplyPacket(UpdateShopPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            FixesItems = pkt.FixesItems;
            ShopType = pkt.ShopType;
            AllowBanking = pkt.AllowBanking;
            Keeper = pkt.Keeper;
            Trades.Clear();
            int slot = 1;
            foreach (var t in pkt.Trades)
            {
                if (t.GiveItem <= 0 && t.GetItem <= 0) continue;   // skip empty rows — table stays dense
                Trades.Add(new TradeRowViewModel(slot++, new TradeItemRecord
                {
                    GiveItem = t.GiveItem,
                    GiveValue = t.GiveValue,
                    GetItem = t.GetItem,
                    GetValue = t.GetValue,
                }, _entriesProvider, _isCurrency));
            }
        }
        finally
        {
            _loading = false;
        }

        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public ShopRecord ToRecord()
    {
        var r = new ShopRecord
        {
            Name = Name,
            FixesItems = FixesItems,
            ShopType = ShopType,
            AllowBanking = AllowBanking,
            Keeper = Keeper,
        };
        // Persist a dense list of real trades only — empty rows (either side itemless) are dropped, so a
        // blank row anywhere leaves no gap in the saved data.
        r.TradeItem = Trades.Where(t => !t.IsEmpty).Select(t => t.ToRecord()).ToList();
        return r;
    }

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveShopPacket BuildSavePacket() => new()
    {
        ShopNum = Index,
        Name = Name,
        FixesItems = FixesItems,
        ShopType = ShopType,
        AllowBanking = AllowBanking,
        Keeper = Keeper,
        Trades = Trades.Select(t => new EditorSaveShopPacket.TradeEntry(
            t.GiveItem, t.GiveValue, t.GetItem, t.GetValue)).ToArray(),
    };
}
