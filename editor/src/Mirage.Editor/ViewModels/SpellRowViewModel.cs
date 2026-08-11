using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One spell slot in the spell editor's list — the editable mirror of a <see cref="SpellRecord"/>.
/// <para>Dirty tracking works as in the other row view-models: setters mark dirty unless
/// <c>_loading</c> is set while filling from a record or packet.</para>
/// <para>Also drives the editor's live cost preview. Because a spell's MP cost is a pure function of
/// its own metadata — no caster stat enters — the preview is the exact in-game cost, so editing
/// <see cref="Data1"/> or the type re-raises <see cref="BaseMpCost"/> and friends.</para>
/// </summary>
public sealed partial class SpellRowViewModel : ObservableObject
{
    /// <summary>1-based spell slot number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Class allowed to learn it (1-based; 0 = any class).</summary>
    [ObservableProperty] private int _classReq;
    [ObservableProperty] private SpellType _type;
    /// <summary>The spell's magnitude, or the item id for GiveItem — see <see cref="Data1Label"/>.</summary>
    [ObservableProperty] private short _data1;
    /// <summary>GiveItem quantity; unused by every other spell type.</summary>
    [ObservableProperty] private short _data2;
    /// <summary>GiveItem cost / requirement modifier; unused by every other spell type.</summary>
    [ObservableProperty] private short _data3;

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public SpellRowViewModel(int index, SpellRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _name = r.Name;
        _classReq = r.ClassReq;
        _type = r.Type;
        _data1 = r.Data1;
        _data2 = r.Data2;
        _data3 = r.Data3;
    }

    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnClassReqChanged(int value) => MarkDirty();

    // Data1 and Data3 feed the cost formulas, so both re-raise the preview; Data2 is GiveItem
    // quantity only and has no bearing on cost.
    partial void OnData1Changed(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
        OnPropertyChanged(nameof(ReagentCost));
    }
    partial void OnData2Changed(short value) => MarkDirty();
    partial void OnData3Changed(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
    }

    // The type decides every data-slot caption AND which cost model applies, so the whole derived
    // set re-raises together.
    partial void OnTypeChanged(SpellType value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(Data1Label));
        OnPropertyChanged(nameof(Data1IsGiveItem));
        OnPropertyChanged(nameof(Data2Label));
        OnPropertyChanged(nameof(Data3Label));
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
        OnPropertyChanged(nameof(ShowReagentCost));
        OnPropertyChanged(nameof(ReagentCost));
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
    public void LoadFromRecord(SpellRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            ClassReq = r.ClassReq;
            Type = r.Type;
            Data1 = r.Data1;
            Data2 = r.Data2;
            Data3 = r.Data3;
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(BaseMpCost));
    }

    /// <summary>Refill from a server response and mark the row loaded; does not clear the dirty flag.</summary>
    public void ApplyPacket(UpdateSpellPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            ClassReq = pkt.ClassReq;
            Type = pkt.Type;
            Data1 = pkt.Data1;
            Data2 = pkt.Data2;
            Data3 = pkt.Data3;
        }
        finally
        {
            _loading = false;
        }
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(BaseMpCost));
    }

    /// <summary>Project the row back into a record for saving.</summary>
    public SpellRecord ToRecord() => new()
    {
        Name = Name,
        ClassReq = ClassReq,
        Type = Type,
        Data1 = Data1,
        Data2 = Data2,
        Data3 = Data3,
    };

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveSpellPacket BuildSavePacket() => new()
    {
        SpellNum = Index,
        Name = Name,
        ClassReq = ClassReq,
        Type = Type,
        Data1 = Data1,
        Data2 = Data2,
        Data3 = Data3,
    };

    // ── Data labels ───────────────────────────────────────────────────────────

    /// <summary>Whether Data1 holds an item id, so the form can offer an item picker and show Data2/Data3.</summary>
    public bool Data1IsGiveItem => Type == SpellType.GiveItem;

    /// <summary>Form caption for Data1, which varies by spell type (heal/drain amount, damage, item number).</summary>
    public string Data1Label => Type switch
    {
        SpellType.AddHp => EditorStrings.Get(EditorStrings.DataLabel_HpAmount),
        SpellType.AddMp => EditorStrings.Get(EditorStrings.DataLabel_MpAmount),
        SpellType.AddSp => EditorStrings.Get(EditorStrings.DataLabel_SpAmount),
        SpellType.SubHp => EditorStrings.Get(EditorStrings.DataLabel_Damage),
        SpellType.SubMp => EditorStrings.Get(EditorStrings.DataLabel_MpDrain),
        SpellType.SubSp => EditorStrings.Get(EditorStrings.DataLabel_SpDrain),
        SpellType.GiveItem => EditorStrings.Get(EditorStrings.DataLabel_ItemNumber),
        _ => EditorStrings.Get(EditorStrings.DataLabel_Data1),
    };

    // Data2/Data3 are authored only for GiveItem (item quantity + its cost/level modifier); every other
    // spell type derives everything from Data1, so the editor hides these two rows for them (Data1IsGiveItem).
    /// <summary>Form caption for Data2 (GiveItem quantity).</summary>
    public string Data2Label => EditorStrings.Get(EditorStrings.DataLabel_Quantity);
    /// <summary>Form caption for Data3 (GiveItem cost / requirement modifier).</summary>
    public string Data3Label => EditorStrings.Get(EditorStrings.DataLabel_CostLevelModifier);

    // MP cost preview.  UTILITY spells (heals, drains, GiveItem) pay a pure function of spell metadata — no caster
    // Int enters, so this preview is the exact in-game cost.  SubHp is the exception: it's the caster's sustainable
    // "weapon", so its MP is a trivial caster-pool fraction (MaxMP / 20, caster-dependent) and its real per-cast
    // cost is the REAGENT below — so for SubHp the MP line shows the pool-fraction formula, not a fixed number.
    /// <summary>The spell's fixed MP cost, as the game will charge it.</summary>
    public int BaseMpCost => CombatFormulas.GetSpellMpCost(ToRecord());
    /// <summary>MP cost as shown in the form — a number, or the pool-fraction formula for SubHp.</summary>
    public string MpCostDisplay => Type == SpellType.SubHp
        ? EditorStrings.Get(EditorStrings.SpellEditor_SubHpMpCostValue)
        : BaseMpCost.ToString();

    // Reagent-per-cast preview (SubHp only) — the magic mirror of weapon-repair upkeep: round(Data1/10 × ~0.48
    // durability-lost-per-swing).  Shown instead of a fixed MP number since that's the cost an author actually tunes.
    /// <summary>Whether to show the reagent-cost row (SubHp only).</summary>
    public bool ShowReagentCost => Type == SpellType.SubHp;
    /// <summary>Reagents consumed per cast; 0 for any type other than SubHp.</summary>
    public int ReagentCost => Type == SpellType.SubHp ? CombatFormulas.SubHpReagentCost(Data1) : 0;
}
