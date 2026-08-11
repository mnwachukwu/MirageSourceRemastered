using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>
/// One NPC slot in the NPC editor's list — the editable mirror of an <see cref="NpcRecord"/>.
/// <para>Beyond the usual row duties (dirty tracking, load, save) this drives the editor's live
/// stat readout: level, vitals, regen, mitigation, damage, EXP, and the combat chances are all
/// computed through the shared formula classes, so the preview and the running game can never
/// disagree. Editing any stat re-raises that whole derived set via <see cref="NotifyLevelDerived"/>.</para>
/// </summary>
public sealed partial class NpcRowViewModel : ObservableObject
{
    /// <summary>1-based NPC template number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Line the NPC speaks when it engages a player; blank for silent.</summary>
    [ObservableProperty] private string _attackSay = "";
    [ObservableProperty] private int _sprite;
    // Footprint size class 1..3 (1 = 32x32, 2 = 64x64, 3 = 96x96); the form spinner clamps to [1, MaxNpcSize].
    [ObservableProperty] private int _size = 1;
    /// <summary>Seconds between despawn and respawn for this template's spawn slots.</summary>
    [ObservableProperty] private int _spawnSecs;
    [ObservableProperty] private NpcBehavior _behavior;
    /// <summary>Comrade group id — same-group NPCs come to each other's aid (0 = no group).</summary>
    [ObservableProperty] private int _group;
    /// <summary>Aggro / sight radius in tiles.</summary>
    [ObservableProperty] private int _range;
    /// <summary>Percent chance to drop <see cref="DropItem"/> on death (0 = never, >= 100 = always).</summary>
    [ObservableProperty] private short _dropChance;
    /// <summary>Item slot dropped on death (0 = none).</summary>
    [ObservableProperty] private int _dropItem;
    /// <summary>Quantity of <see cref="DropItem"/> dropped.</summary>
    [ObservableProperty] private short _dropItemValue;
    [ObservableProperty] private int _str;
    [ObservableProperty] private int _def;
    [ObservableProperty] private int _spd;
    [ObservableProperty] private int _int;
    /// <summary>Flat HP added on top of the stat-derived pool — the boss/wall lever.</summary>
    [ObservableProperty] private int _extraHp;
    // Boss classification (author flag) — drives the compressed guild-quest kill count; see NpcRecord.IsBoss.
    // Not inferred from HP/Size; a tanky mob isn't automatically a boss.
    [ObservableProperty] private bool _isBoss;
    // Editor-only EXP preview level: shows the reward for a typical player of this level (defaults to the mob's own
    // level on load; scrub it up/down to see the mob's EXP across the band you'll place it in).  Never touches live EXP.
    // Nullable so clearing the spinner (empty text) binds cleanly instead of throwing a conversion error;
    // a blank box is treated as level 0 by ExpDrop.
    [ObservableProperty] private int? _previewLevel = 1;
    partial void OnPreviewLevelChanged(int? value) => OnPropertyChanged(nameof(ExpDrop));
    // The mob's own player-equivalent level (StatFormulas.NpcLevel) as of the last load or stat edit — the anchor
    // for auto-following the preview spinner.  While PreviewLevel sits on this value, a stat edit that moves the
    // mob's level drags the spinner along; once the designer scrubs it off, it detaches (see NotifyLevelDerived).
    private int _ownLevel;
    [ObservableProperty] private bool _emitsLight;
    // Light attributes (used only when EmitsLight is true), authored via the conditional block in the form.
    [ObservableProperty] private Color _lightColor = ColorHex.ToColor(LightSpec.Torch.Rgb);
    partial void OnLightColorChanged(Color value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LightColorHex));
    }
    [ObservableProperty] private double _lightRadius = LightSpec.Torch.Radius;   // tiles
    partial void OnLightRadiusChanged(double value) => MarkDirty();
    [ObservableProperty] private FlickerStyle _lightFlicker = LightSpec.Torch.Flicker;
    partial void OnLightFlickerChanged(FlickerStyle value) => MarkDirty();
    [ObservableProperty] private int _lightIntensity = 100;   // percent, 0..100
    partial void OnLightIntensityChanged(int value) => MarkDirty();

    // Hex form of LightColor, kept in sync with the color picker (edit either, both update).
    /// <summary>"RRGGBB" text form of <see cref="LightColor"/>; an unparseable value is ignored.</summary>
    public string LightColorHex
    {
        get => $"{LightColor.R:X2}{LightColor.G:X2}{LightColor.B:X2}";
        set { if (ColorHex.TryParse(value, out var c)) LightColor = c; }
    }

    // The light config block shows only when the NPC emits light.
    /// <summary>Whether the form's light-attribute block is shown.</summary>
    public bool LightBlockVisible => EmitsLight;

    // Composed light spec, reused by ToRecord() and the save packet.
    /// <summary>The four light fields composed into the shared spec, with intensity as a 0..1 fraction.</summary>
    public LightSpec Light => new(ColorHex.ToRgb(LightColor), (float)LightRadius, LightFlicker,
        Math.Clamp(LightIntensity, 0, 100) / 100f);

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    // ── Calculated stats ──────────────────────────────────────────────────────
    // All read through the shared formula classes so editor preview stays in lockstep
    // with the live game.  Change a formula in Mirage.Shared and the editor follows.
    // ONE player-faithful LEVEL, straight from the shared StatFormulas.NpcLevel: all four stats (SPD included)
    // count as investment, so level = (statSum - 20)/3 + 1 -- exactly what level a player with this point spread
    // would be (an authored class starts at 20 total; each level adds Constants.PointsPerLevel = 3).  SPD buys
    // DURABILITY (its level floor lifts HP + mit) like a SPD-heavy player, so it belongs in the ONE level that
    // drives vitals, mit, EXP, and the on-target strength readout alike -- there is no separate "combat" level.
    /// <summary>Sum of the four stats — the point spread the virtual level is inferred from.</summary>
    public int StatTotal => Str + Def + Int + Spd;
    /// <summary>The mob's player-equivalent level, as text for the readout.</summary>
    public string Level => $"{StatFormulas.NpcLevel(Str, Def, Int, Spd)}";

    public int MaxHp => StatFormulas.GetNpcMaxHp(Str, Def, Int, Spd, ExtraHp);
    public int MaxMp => StatFormulas.GetNpcMaxMp(Str, Def, Int, Spd);
    public int MaxSp => StatFormulas.GetNpcMaxSp(Spd);
    public int HpRegen => StatFormulas.GetNpcHpRegen(Def);
    public int MpRegen => StatFormulas.GetNpcMpRegen(Int);
    public int SpRegen => StatFormulas.GetNpcSpRegen(Spd);
    /// <summary>EXP a player of <see cref="PreviewLevel"/> would earn for the kill.</summary>
    public int ExpDrop => ExpFormulas.EstimatedExpVsLevel(Str, Def, Int, Spd, ExtraHp, PreviewLevel ?? 0);
    // DropChance is a direct percent: 0 = never, >= 100 = always, otherwise the value itself.
    /// <summary>Drop chance as display text ("never" / "always" / "N%").</summary>
    public string DropChancePct => DropChance <= 0 ? EditorStrings.Get(EditorStrings.NpcEditor_DropChanceNever)
                                 : DropChance >= 100 ? EditorStrings.Get(EditorStrings.NpcEditor_DropChanceAlways)
                                 : $"{DropChance}%";
    // Non-blocking authoring guard: flag a half-configured drop (chance without item, or item without chance).
    /// <summary>Warning text for a half-configured drop; empty when the pair is consistent.</summary>
    public string DropConfigWarning =>
        DropChance > 0 && DropItem <= 0 ? EditorStrings.Get(EditorStrings.NpcEditor_DropWarnChanceNoItem)
      : DropItem > 0 && DropChance <= 0 ? EditorStrings.Get(EditorStrings.NpcEditor_DropWarnItemNoChance)
      : string.Empty;
    /// <summary>Whether to show the drop-configuration warning.</summary>
    public bool HasDropConfigWarning => DropConfigWarning.Length > 0;
    // Combat chances — require SP > 0 at runtime; show stat-derived maximum here
    public string CritChancePct => CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.NpcCriticalChancePerMille(Str));
    public string SpellCritChancePct => CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.NpcSpellCriticalChancePerMille(Int));
    public string BlockChancePct => CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.NpcBlockChancePerMille(Def));
    public string DodgeChancePct => CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.NpcDodgeChancePerMille(Def));
    // Combat output / mitigation — same formulas the live combat uses (NPCs roll matched implicit gear).
    // Both P-DMG and M-DMG are the CENTER of a symmetric +-10% Vary (shown as the base number, mirroring the
    // player readout).  P-DMG = NpcMeleeBaseDamage(Str); M-DMG = NpcSpellBaseMagnitude(Int) — the same curve
    // off Int.  MIT = NpcProtection = PlayerProtection(NpcLevel, Def) + a fully-kitted defender's gear (armor +
    // helmet full + shield 1/4, all at matched Def) — one axis that resists P-DMG and M-DMG alike.
    public int PhysDamage => CombatFormulas.NpcMeleeBaseDamage(Str);
    public int MagicDamage => CombatFormulas.NpcSpellBaseMagnitude(Int);
    public int Mit => CombatFormulas.NpcProtection(Str, Def, Int, Spd);

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public NpcRowViewModel(int index, NpcRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _name = r.Name;
        _attackSay = r.AttackSay;
        _sprite = r.Sprite;
        _size = r.EffectiveSize;
        _spawnSecs = r.SpawnSecs;
        _behavior = r.Behavior;
        _group = r.Group;
        _range = r.Range;
        _dropChance = r.DropChance;
        _dropItem = r.DropItem;
        _dropItemValue = r.DropItemValue;
        _str = r.Str;
        _def = r.Def;
        _spd = r.Spd;
        _int = r.Int;
        _extraHp = r.ExtraHp;
        _isBoss = r.IsBoss;
        SyncPreviewLevelToOwn();   // default the EXP preview to the mob's own level (and anchor auto-follow there)
        _emitsLight = r.EmitsLight;
        _lightColor = ColorHex.ToColor(r.Light.Rgb);
        _lightRadius = r.Light.Radius;
        _lightFlicker = r.Light.Flicker;
        _lightIntensity = (int)Math.Round(r.Light.Intensity * 100);
    }

    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnIsBossChanged(bool value) => MarkDirty();
    partial void OnEmitsLightChanged(bool value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LightBlockVisible));
    }
    partial void OnAttackSayChanged(string value) => MarkDirty();
    partial void OnSpriteChanged(int value) => MarkDirty();
    partial void OnSizeChanged(int value) => MarkDirty();
    partial void OnSpawnSecsChanged(int value) => MarkDirty();
    partial void OnBehaviorChanged(NpcBehavior value) => MarkDirty();
    partial void OnGroupChanged(int value) => MarkDirty();
    partial void OnRangeChanged(int value) => MarkDirty();
    partial void OnDropChanceChanged(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(DropChancePct));
        NotifyDropWarning();
    }
    partial void OnDropItemChanged(int value)
    {
        MarkDirty();
        NotifyDropWarning();
    }
    partial void OnDropItemValueChanged(short value) => MarkDirty();
    private void NotifyDropWarning()
    {
        OnPropertyChanged(nameof(DropConfigWarning));
        OnPropertyChanged(nameof(HasDropConfigWarning));
    }
    // Every stat feeds NpcLevel (SPD included), which drives the HP/MP pools, mitigation, EXP, and the Level
    // readout — so a change to ANY stat refreshes this shared set (each handler adds its own stat-specific extras).
    private void NotifyLevelDerived()
    {
        // Auto-follow: while the EXP-preview spinner is parked on the mob's own level, keep it pinned there as this
        // stat edit shifts that level, so the designer keeps seeing the reward at the mob's level.  Once they scrub
        // the spinner to a custom level it detaches and stays put.  Skipped during a load — the load paths seed the
        // spinner + anchor explicitly via SyncPreviewLevelToOwn (and one-stat-at-a-time loading would thrash it).
        if (!_loading)
        {
            int newOwnLevel = StatFormulas.NpcLevel(Str, Def, Int, Spd);
            if (newOwnLevel != _ownLevel)
            {
                if (PreviewLevel == _ownLevel) PreviewLevel = newOwnLevel;
                _ownLevel = newOwnLevel;
            }
        }
        OnPropertyChanged(nameof(StatTotal));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(MaxHp));
        OnPropertyChanged(nameof(MaxMp));
        OnPropertyChanged(nameof(ExpDrop));
        OnPropertyChanged(nameof(Mit));
    }

    // Seed the EXP-preview spinner to the mob's OWN player-equivalent level and anchor auto-follow there.
    // INVARIANT: EVERY load path (ctor, offline reload, online packet) must call this, or the spinner opens on
    // the placeholder 1 instead of the mob's level. Add the call when adding a load path.
    private void SyncPreviewLevelToOwn()
    {
        _ownLevel = StatFormulas.NpcLevel(Str, Def, Int, Spd);
        PreviewLevel = _ownLevel;
    }

    partial void OnStrChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(CritChancePct));
        OnPropertyChanged(nameof(PhysDamage));
        NotifyLevelDerived();
    }
    partial void OnDefChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(HpRegen));
        OnPropertyChanged(nameof(BlockChancePct));
        OnPropertyChanged(nameof(DodgeChancePct));
        NotifyLevelDerived();
    }
    partial void OnSpdChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(MaxSp));
        OnPropertyChanged(nameof(SpRegen));
        NotifyLevelDerived();   // SPD feeds NpcLevel too, so it lifts HP/MP/mit/EXP and the Level readout
    }
    partial void OnIntChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(MpRegen));
        OnPropertyChanged(nameof(MagicDamage));
        OnPropertyChanged(nameof(SpellCritChancePct));
        NotifyLevelDerived();
    }
    partial void OnExtraHpChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(MaxHp));    // flat 1:1 add to the HP pool...
        OnPropertyChanged(nameof(ExpDrop));  // ...and counts toward kill-EXP
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
    public void LoadFromRecord(NpcRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            AttackSay = r.AttackSay;
            Sprite = r.Sprite;
            Size = r.EffectiveSize;
            SpawnSecs = r.SpawnSecs;
            Behavior = r.Behavior;
            Group = r.Group;
            Range = r.Range;
            DropChance = r.DropChance;
            DropItem = r.DropItem;
            DropItemValue = r.DropItemValue;
            Str = r.Str;
            Def = r.Def;
            Spd = r.Spd;
            Int = r.Int;
            ExtraHp = r.ExtraHp;
            IsBoss = r.IsBoss;
            SyncPreviewLevelToOwn();   // re-default the EXP preview to this mob's own level on open (re-anchor auto-follow)
            EmitsLight = r.EmitsLight;
            LightColor = ColorHex.ToColor(r.Light.Rgb);
            LightRadius = r.Light.Radius;
            LightFlicker = r.Light.Flicker;
            LightIntensity = (int)Math.Round(r.Light.Intensity * 100);
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Refill from a server response and mark the row loaded; does not clear the dirty flag.</summary>
    public void ApplyPacket(UpdateNpcPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            AttackSay = pkt.AttackSay;
            Sprite = pkt.Sprite;
            Size = pkt.Size;
            SpawnSecs = pkt.SpawnSecs;
            Behavior = pkt.Behavior;
            Group = pkt.Group;
            Range = pkt.Range;
            DropChance = pkt.DropChance;
            DropItem = pkt.DropItem;
            DropItemValue = pkt.DropValue;
            Str = pkt.Str;
            Def = pkt.Def;
            Spd = pkt.Spd;
            Int = pkt.Int;
            ExtraHp = pkt.ExtraHp;
            IsBoss = pkt.IsBoss;
            SyncPreviewLevelToOwn();   // online load: default the EXP preview to the mob's own level
            EmitsLight = pkt.EmitsLight;
            LightColor = ColorHex.ToColor(pkt.Light.Rgb);
            LightRadius = pkt.Light.Radius;
            LightFlicker = pkt.Light.Flicker;
            LightIntensity = (int)Math.Round(pkt.Light.Intensity * 100);
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
    public NpcRecord ToRecord() => new()
    {
        Name = Name,
        AttackSay = AttackSay,
        Sprite = Sprite,
        Size = Size,
        SpawnSecs = SpawnSecs,
        Behavior = Behavior,
        Group = Group,
        Range = Range,
        DropChance = DropChance,
        DropItem = DropItem,
        DropItemValue = DropItemValue,
        Str = Str,
        Def = Def,
        Spd = Spd,
        Int = this.Int,
        ExtraHp = ExtraHp,
        IsBoss = IsBoss,
        EmitsLight = EmitsLight,
        Light = Light,
    };

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveNpcPacket BuildSavePacket() => new()
    {
        NpcNum = Index,
        Name = Name,
        AttackSay = AttackSay,
        Sprite = Sprite,
        Size = Size,
        SpawnSecs = SpawnSecs,
        Behavior = Behavior,
        Group = Group,
        Range = Range,
        DropChance = DropChance,
        DropItem = DropItem,
        DropValue = DropItemValue,
        Str = Str,
        Def = Def,
        Spd = Spd,
        Int = this.Int,
        ExtraHp = ExtraHp,
        IsBoss = IsBoss,
        EmitsLight = EmitsLight,
        Light = Light,
    };
}
