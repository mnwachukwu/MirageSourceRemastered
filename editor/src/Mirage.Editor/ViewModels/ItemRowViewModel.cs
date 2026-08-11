using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One item slot in the item editor's list — the editable mirror of an <see cref="ItemRecord"/>.
/// <para>Tracks its own dirty flag: every setter routes through <see cref="MarkDirty"/>, which is
/// suppressed while <c>_loading</c> is set so filling the row from a record or packet doesn't mark
/// it as an author edit.</para>
/// <para>The <c>Data1</c>/<c>Data2</c>/<c>Data3</c> fields are generic slots whose meaning depends on
/// <see cref="Type"/>; the <c>…Label</c> and <c>…Visible</c> properties below turn that into the
/// right caption and show/hide state for the form.</para>
/// </summary>
public sealed partial class ItemRowViewModel : ObservableObject
{
    /// <summary>1-based item slot number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Index into the item graphics strip.</summary>
    [ObservableProperty] private short _pic;
    [ObservableProperty] private ItemType _type;
    /// <summary>Type-dependent value — see <see cref="Data1Label"/>.</summary>
    [ObservableProperty] private short _data1;
    /// <summary>Type-dependent value — see <see cref="Data2Label"/>.</summary>
    [ObservableProperty] private short _data2;
    /// <summary>Type-dependent value — see <see cref="Data3Label"/>.</summary>
    [ObservableProperty] private short _data3;
    // Item restriction flags; each blocks exactly one action, enforced server-side.
    [ObservableProperty] private bool _nonTradeable;
    [ObservableProperty] private bool _nonListable;
    [ObservableProperty] private bool _nonMailable;
    [ObservableProperty] private bool _destroyOnDrop;

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public ItemRowViewModel(int index, ItemRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _name = r.Name;
        _pic = r.Pic;
        _type = r.Type;
        _data1 = r.Data1;
        _data2 = r.Data2;
        _data3 = r.Data3;
        _nonTradeable = r.NonTradeable;
        _nonListable = r.NonListable;
        _nonMailable = r.NonMailable;
        _destroyOnDrop = r.DestroyOnDrop;
    }

    /// <summary>int view of <see cref="Pic"/> for the numeric spinner, which does not bind to short.</summary>
    public int PicAsInt
    {
        get => Pic;
        set => Pic = (short)value;
    }
    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnPicChanged(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(PicAsInt));
    }
    partial void OnData1Changed(short value) => MarkDirty();
    partial void OnData2Changed(short value) => MarkDirty();
    partial void OnData3Changed(short value) => MarkDirty();
    partial void OnNonTradeableChanged(bool value) => MarkDirty();
    partial void OnNonListableChanged(bool value) => MarkDirty();
    partial void OnNonMailableChanged(bool value) => MarkDirty();
    partial void OnDestroyOnDropChanged(bool value) => MarkDirty();

    // Changing the type re-labels and re-shows the data slots, so every derived caption and
    // visibility flag has to re-raise alongside the dirty mark.
    partial void OnTypeChanged(ItemType value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(Data1Label));
        OnPropertyChanged(nameof(Data2Label));
        OnPropertyChanged(nameof(Data3Label));
        OnPropertyChanged(nameof(Data1Visible));
        OnPropertyChanged(nameof(Data2Visible));
        OnPropertyChanged(nameof(Data3Visible));
        OnPropertyChanged(nameof(Data1IsSpell));
    }

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Mark the row clean after a successful save or discard.</summary>
    public void ClearDirty()
    {
        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Refill from an on-disk record (offline load or discard). Leaves the row clean.</summary>
    public void LoadFromRecord(ItemRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            Pic = r.Pic;
            Type = r.Type;
            Data1 = r.Data1;
            Data2 = r.Data2;
            Data3 = r.Data3;
            NonTradeable = r.NonTradeable;
            NonListable = r.NonListable;
            NonMailable = r.NonMailable;
            DestroyOnDrop = r.DestroyOnDrop;
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Refill from a server response and mark the row loaded. Unlike
    /// <see cref="LoadFromRecord"/> this does not clear the dirty flag, so the push-changes flow can
    /// still see edits made before the packet arrived.</summary>
    public void ApplyPacket(UpdateItemPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            Pic = pkt.Pic;
            Type = pkt.Type;
            Data1 = pkt.Data1;
            Data2 = pkt.Data2;
            Data3 = pkt.Data3;
            NonTradeable = pkt.NonTradeable;
            NonListable = pkt.NonListable;
            NonMailable = pkt.NonMailable;
            DestroyOnDrop = pkt.DestroyOnDrop;
        }
        finally
        {
            _loading = false;
        }
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Project the row back into a record for saving.</summary>
    public ItemRecord ToRecord() => new()
    {
        Name = Name,
        Pic = Pic,
        Type = Type,
        Data1 = Data1,
        Data2 = Data2,
        Data3 = Data3,
        NonTradeable = NonTradeable,
        NonListable = NonListable,
        NonMailable = NonMailable,
        DestroyOnDrop = DestroyOnDrop,
    };

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveItemPacket BuildSavePacket() => new()
    {
        ItemNum = Index,
        Name = Name,
        Pic = Pic,
        Type = Type,
        Data1 = Data1,
        Data2 = Data2,
        Data3 = Data3,
        NonTradeable = NonTradeable,
        NonListable = NonListable,
        NonMailable = NonMailable,
        DestroyOnDrop = DestroyOnDrop,
    };

    /// <summary>Form caption for Data1, which varies by item type (durability, potion amount, spell number).</summary>
    public string Data1Label => Type switch
    {
        ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield => EditorStrings.Get(EditorStrings.DataLabel_Durability),
        ItemType.PotionAddHp or ItemType.PotionSubHp => EditorStrings.Get(EditorStrings.DataLabel_HpAmount),
        ItemType.PotionAddMp or ItemType.PotionSubMp => EditorStrings.Get(EditorStrings.DataLabel_MpAmount),
        ItemType.PotionAddSp or ItemType.PotionSubSp => EditorStrings.Get(EditorStrings.DataLabel_SpAmount),
        ItemType.Spell => EditorStrings.Get(EditorStrings.DataLabel_SpellNumber),
        _ => EditorStrings.Get(EditorStrings.DataLabel_Data1),
    };

    // ── Data2 ─────────────────────────────────────────────────────────────────
    // Weapon: damage added to GetPlayerDamage (via WeaponContribution DR); doubles as min STR to equip.
    // Armor:  defense added to GetPlayerProtection (via GearMitigation DR); doubles as min DEF to equip.
    // Helmet: defense added to GetPlayerProtection (via GearMitigation DR); doubles as min DEF to equip.
    // Shield: MIT via ShieldMitigation (1/4 of GearMit, paired against Def); doubles as min DEF to equip.
    // All other types: Data2 unused.
    /// <summary>Form caption for Data2 — damage on a weapon, defense on armor/helmet/shield.</summary>
    public string Data2Label => Type switch
    {
        ItemType.Weapon => EditorStrings.Get(EditorStrings.DataLabel_Damage),
        ItemType.Armor or ItemType.Helmet => EditorStrings.Get(EditorStrings.DataLabel_Defense),
        ItemType.Shield => EditorStrings.Get(EditorStrings.DataLabel_Defense),
        _ => EditorStrings.Get(EditorStrings.DataLabel_Data2),
    };

    // ── Data3 ─────────────────────────────────────────────────────────────────
    // Equipment (Weapon/Armor/Helmet/Shield): class ID required to equip (1-based, 0 = unrestricted).
    // All other types: Data3 unused.
    /// <summary>Form caption for Data3 — the class requirement on equipment.</summary>
    public string Data3Label => Type switch
    {
        ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield => EditorStrings.Get(EditorStrings.DataLabel_ClassReq),
        _ => EditorStrings.Get(EditorStrings.DataLabel_Data3),
    };

    // ── Visibility ────────────────────────────────────────────────────────────
    // Key items have no editable data: a door (Key tile) references its key by item id, so the item's
    // own Data1/2/3 are unused — the match is on the inventory item number against tile.Data1.
    /// <summary>Whether the Data1 field applies to this item type.</summary>
    public bool Data1Visible => Type is not (ItemType.None or ItemType.Currency or ItemType.Key);
    /// <summary>Whether Data1 holds a spell number, so the form can offer a spell picker.</summary>
    public bool Data1IsSpell => Type == ItemType.Spell;

    // Show Data2 only for types where it has a game effect (Weapon/Armor/Helmet/Shield).
    // Key, potions, spell scrolls, Currency, None: Data2 unused.
    /// <summary>Whether the Data2 field applies to this item type.</summary>
    public bool Data2Visible =>
        Type is ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield;

    // Show Data3 (class requirement) only for the four equipment types.
    /// <summary>Whether the Data3 field applies to this item type.</summary>
    public bool Data3Visible =>
        Type is ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield;
}
