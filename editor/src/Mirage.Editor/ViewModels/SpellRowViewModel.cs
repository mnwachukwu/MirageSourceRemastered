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
/// <see cref="VitalAmount"/>, <see cref="IntReq"/> or the type re-raises <see cref="BaseMpCost"/>
/// and friends.</para>
/// </summary>
public sealed partial class SpellRowViewModel : ObservableObject
{
    /// <summary>1-based spell slot number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Classes allowed to learn it; null or empty = every class. Replaced wholesale by the
    /// class multi-select rather than mutated, so the change notification actually fires.</summary>
    [ObservableProperty] private List<short>? _allowedClasses;
    [ObservableProperty] private SpellType _type;

    // Type-specific fields — GiveItem uses the bottom three and no VitalAmount; every other type is
    // the other way round. See SpellRecord.
    /// <summary>The spell's magnitude, which also gates learning it. Unused by GiveItem.</summary>
    [ObservableProperty] private short _vitalAmount;
    /// <summary>GiveItem: the item handed over; unused by every other spell type.</summary>
    [ObservableProperty] private short _itemNum;
    /// <summary>GiveItem: how many; unused by every other spell type.</summary>
    [ObservableProperty] private short _itemAmount;
    /// <summary>GiveItem: its INT requirement, and hence its MP cost; unused by every other type.</summary>
    [ObservableProperty] private short _intReq;

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
        _allowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
        _type = r.Type;
        _vitalAmount = r.VitalAmount;
        _itemNum = r.ItemNum;
        _itemAmount = r.ItemAmount;
        _intReq = r.IntReq;
    }

    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnAllowedClassesChanged(List<short>? value) => MarkDirty();

    // VitalAmount and IntReq are the two gate values, so both re-raise the cost preview; ItemNum and
    // ItemAmount say what GiveItem hands over and have no bearing on cost.
    partial void OnVitalAmountChanged(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
        OnPropertyChanged(nameof(ReagentCost));
    }
    partial void OnItemNumChanged(short value) => MarkDirty();
    partial void OnItemAmountChanged(short value) => MarkDirty();
    partial void OnIntReqChanged(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
    }

    // The type decides the one varying caption, which fields show, AND which cost model applies, so
    // the whole derived set re-raises together.
    partial void OnTypeChanged(SpellType value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(VitalAmountLabel));
        OnPropertyChanged(nameof(VitalAmountVisible));
        OnPropertyChanged(nameof(IsGiveItem));
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
            AllowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
            Type = r.Type;
            VitalAmount = r.VitalAmount;
            ItemNum = r.ItemNum;
            ItemAmount = r.ItemAmount;
            IntReq = r.IntReq;
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
            AllowedClasses = pkt.AllowedClasses is null ? null : new List<short>(pkt.AllowedClasses);
            Type = pkt.Type;
            VitalAmount = pkt.VitalAmount;
            ItemNum = pkt.ItemNum;
            ItemAmount = pkt.ItemAmount;
            IntReq = pkt.IntReq;
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

    /// <summary>Project the row back into a record for saving. <see cref="SpellRecord.Normalize"/>d, so a
    /// field the current type does not use is written as 0 rather than carrying a previous type's value —
    /// which matters here because a stale IntReq would silently re-gate the spell. The row itself is left
    /// alone, so retyping and back within one session keeps the numbers.</summary>
    public SpellRecord ToRecord()
    {
        var r = new SpellRecord
        {
            Name = Name,
            AllowedClasses = AllowedClasses is null ? null : new List<short>(AllowedClasses),
            Type = Type,
            VitalAmount = VitalAmount,
            ItemNum = ItemNum,
            ItemAmount = ItemAmount,
            IntReq = IntReq,
        };
        r.Normalize();
        return r;
    }

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.
    /// Normalized through <see cref="ToRecord"/> so the online and offline saves store the same thing.</summary>
    public EditorSaveSpellPacket BuildSavePacket()
    {
        var r = ToRecord();
        return new EditorSaveSpellPacket
        {
            SpellNum = Index,
            Name = r.Name,
            AllowedClasses = r.AllowedClasses,
            Type = r.Type,
            VitalAmount = r.VitalAmount,
            ItemNum = r.ItemNum,
            ItemAmount = r.ItemAmount,
            IntReq = r.IntReq,
        };
    }

    // ── Captions and visibility ───────────────────────────────────────────────
    // The split is total: GiveItem shows the item picker, quantity and INT requirement; every other type
    // shows VitalAmount alone. Both flags defer to SpellRecord — the same rules Normalize clears by, so
    // a hidden field is exactly a zeroed one.

    /// <summary>Whether this is a GiveItem spell, which shows the item picker, quantity and INT
    /// requirement in place of the magnitude field.</summary>
    public bool IsGiveItem => SpellRecord.UsesItemFields(Type);

    /// <summary>Whether the magnitude field applies (everything except GiveItem).</summary>
    public bool VitalAmountVisible => SpellRecord.UsesVitalAmount(Type);

    /// <summary>Form caption for <see cref="VitalAmount"/> — which vital it moves, and in which
    /// direction, depends on the type.</summary>
    public string VitalAmountLabel => Type switch
    {
        SpellType.AddHp => EditorStrings.Get(EditorStrings.DataLabel_HpAmount),
        SpellType.AddMp => EditorStrings.Get(EditorStrings.DataLabel_MpAmount),
        SpellType.AddSp => EditorStrings.Get(EditorStrings.DataLabel_SpAmount),
        SpellType.SubHp => EditorStrings.Get(EditorStrings.DataLabel_Damage),
        SpellType.SubMp => EditorStrings.Get(EditorStrings.DataLabel_MpDrain),
        SpellType.SubSp => EditorStrings.Get(EditorStrings.DataLabel_SpDrain),
        _ => EditorStrings.Get(EditorStrings.DataLabel_VitalAmount),
    };

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

    // Reagent-per-cast preview (SubHp only) — the magic mirror of weapon-repair upkeep:
    // round(VitalAmount/10 × ~0.48 durability-lost-per-swing).  Shown instead of a fixed MP number since
    // that's the cost an author actually tunes.
    /// <summary>Whether to show the reagent-cost row (SubHp only).</summary>
    public bool ShowReagentCost => Type == SpellType.SubHp;
    /// <summary>Reagents consumed per cast; 0 for any type other than SubHp.</summary>
    public int ReagentCost => Type == SpellType.SubHp ? CombatFormulas.SubHpReagentCost(VitalAmount) : 0;
}
