using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One class slot in the class editor's list — the editable mirror of a <see cref="ClassRecord"/>.
/// <para>Alongside the usual row duties it previews what a freshly-created character of this class
/// would have at level 1, so a designer can see the consequences of a stat spread while typing it.
/// Editing a stat re-raises only the previews that stat actually feeds.</para>
/// </summary>
public sealed partial class ClassRowViewModel : ObservableObject
{
    /// <summary>1-based class slot number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Sprite a character of this class starts with.</summary>
    [ObservableProperty] private int _sprite;
    [ObservableProperty] private int _str;
    [ObservableProperty] private int _def;
    [ObservableProperty] private int _spd;
    [ObservableProperty] private int _int;

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    // ── Theoretical fresh Lv.1 character of this class ──────────────────────
    // At character creation each player stat is copied from the class stat (one-time copy in
    // CreateCharacter), so a fresh L=1 character has playerStat = classStat for every stat.
    // The previews below evaluate every formula with that assumption — what a player gets the
    // instant they finish character creation.  All formulas route through the same shared
    // classes the live game uses, so the editor stays in lockstep with combat.

    public int LevelOneMaxHp => StatFormulas.GetPlayerMaxHp(1, Def, Def);
    public int LevelOneMaxMp => StatFormulas.GetPlayerMaxMp(1, Int, Int);
    public int LevelOneMaxSp => StatFormulas.GetPlayerMaxSp(1, Spd, Spd);

    public int LevelOneHpRegen => StatFormulas.GetPlayerHpRegen(Def);
    public int LevelOneMpRegen => StatFormulas.GetPlayerMpRegen(Int);
    public int LevelOneSpRegen => StatFormulas.GetPlayerSpRegen(Spd);

    // Base damage and mitigation with NO equipment — what the class deals/absorbs bare-handed.
    public int LevelOnePhysDamage => CombatFormulas.UnarmedDamage(Str);
    public int LevelOneMagicDamage => CombatFormulas.SpellPower(Int);
    public int LevelOneMit => CombatFormulas.PlayerProtection(1, Def);   // one universal MIT at level 1 (level baseline + DEF bonus; defends P-DMG and M-DMG alike)

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public ClassRowViewModel(int index, ClassRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _name = r.Name;
        _sprite = r.Sprite;
        _str = r.Str;
        _def = r.Def;
        _spd = r.Spd;
        _int = r.Int;
    }

    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnSpriteChanged(int value) => MarkDirty();
    // Each stat re-raises only the previews it feeds: STR is offense-only, DEF drives HP + regen +
    // mitigation, SPD the stamina pool, INT the mana pool and spell power.
    partial void OnStrChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOnePhysDamage));
    }
    partial void OnDefChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOneMaxHp));
        OnPropertyChanged(nameof(LevelOneHpRegen));
        OnPropertyChanged(nameof(LevelOneMit));
    }
    partial void OnSpdChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOneMaxSp));
        OnPropertyChanged(nameof(LevelOneSpRegen));
    }
    partial void OnIntChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOneMaxMp));
        OnPropertyChanged(nameof(LevelOneMpRegen));
        OnPropertyChanged(nameof(LevelOneMagicDamage));
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
    public void LoadFromRecord(ClassRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            Sprite = r.Sprite;
            Str = r.Str;
            Def = r.Def;
            Spd = r.Spd;
            Int = r.Int;
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Refill from a server response and mark the row loaded; does not clear the dirty flag.</summary>
    public void ApplyPacket(UpdateClassPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            Sprite = pkt.Sprite;
            Str = pkt.Str;
            Def = pkt.Def;
            Spd = pkt.Spd;
            Int = pkt.Int;
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
    public ClassRecord ToRecord() => new()
    {
        Name = Name,
        Sprite = Sprite,
        Str = Str,
        Def = Def,
        Spd = Spd,
        Int = this.Int,
    };

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveClassPacket BuildSavePacket() => new()
    {
        ClassNum = Index,
        Name = Name,
        Sprite = Sprite,
        Str = Str,
        Def = Def,
        Spd = Spd,
        Int = this.Int,
    };
}
