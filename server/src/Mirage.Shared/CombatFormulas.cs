using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// Combat math shared by server and client: damage, mitigation, block/dodge/crit chance, spell
/// costs, and durability wear.
/// Each chance helper returns an integer chance value bounded by its cap so callers own the
/// RNG roll via <see cref="RollPerMille"/> and any prerequisite gates (SP > 0, slot equipped).
/// At the current <see cref="Constants.ChanceScaleFactor"/> = 1 the value IS the percent
/// (cap 35 → 35% block/crit, cap 15 → 15% player dodge, cap 10 → 10% NPC dodge); raising the dial
/// to 10 reinterprets the same numbers as per-mille (35 → 3.5%, 15 → 1.5%, 10 → 1.0%) and
/// widens the roll space accordingly, so caps in the single-percent range and tenths-of-a-percent
/// mid-range values stay representable as integers. Vary and CritDamage roll RNG internally —
/// the random distribution IS the formula.
/// </summary>
public static class CombatFormulas
{
    // ── Damage ────────────────────────────────────────────────────────────────

    public const double DamageVariance = 0.10;

    // Power-curve divisors — double so callers compose fractional intermediates and round only at
    // the final int conversion.
    private const double DamageCurveDivisor = 10.0;
    private const double DamageCurveExponent = 1.5;
    // Str and Int are the same offense stat, so unarmed and spell base share one shift.
    private const int OffenseShift = 20;

    // Player protection is level-primary: a baseline everyone gets off level, plus a DEF stat bonus
    // on top (0 at Def=0). The baseline keeps a low-/no-DEF build survivable at its level without
    // investing DEF; the DEF bonus is the stat's payoff. Smaller divisor = more mitigation.
    private const double LevelMitigationDivisor = 11.0;
    private const double DefMitigationDivisor = 14.0;
    private const int MitigationLevelShift = 20;          // low-level floor on the baseline (shares the offense shift family)

    // Universal min-damage floor: any landed hit deals at least this fraction of its varied raw,
    // whatever the mitigation — stacked DEF is never full immunity and a weak attacker can always
    // chip. Bounds mitigation without capping the DEF stat.
    public const double MinDamageFloorPercent = 0.12;
    // Higher PvE-only floor for a PLAYER hitting an NPC (see ResolvePlayerVsNpcDamage): a low-offense
    // hybrid whose raw sits under a tanky mob's mitigation would otherwise collapse to the 12% floor
    // and grind roughly 10x a pure's kill time. Pures are unaffected (raw already exceeds mit).
    public const double PveMinDamageFloorPercent = 0.35;
    // Players deal half damage to each other, so PvP is markedly more survivable than PvE at equal level.
    public const double PvpDamageMultiplier = 0.5;

    /// <summary>Player unarmed swing damage; weapon item bonus added by caller via
    /// <see cref="WeaponContribution"/>.  Sub-quadratic in Str via <c>Math.Pow(str+20, 1.5)/10</c>
    /// — scales slower than the quadratic HP curve so TTK grows with stat investment instead of
    /// staying flat from level to level.  Shares the <see cref="DamageCurveExponent"/> with
    /// <see cref="PlayerProtection"/> so damage and mitigation grow in step and combat never
    /// plateaus.  The shift gives a low-end floor: even Str=0 produces ~9 base damage.</summary>
    public static int UnarmedDamage(int str) =>
        Math.Max((int)Math.Round(Math.Pow(str + OffenseShift, DamageCurveExponent) / DamageCurveDivisor, MidpointRounding.AwayFromZero), 1);

    /// <summary>Int's contribution to a spell's raw amount — the exact mirror of <see cref="UnarmedDamage"/>
    /// with Int in place of Str.  Str and Int are the same offense stat: a spell is a swing delivered at
    /// range rather than at melee reach, so it deals identical base damage per point.  The only asymmetry
    /// between warrior and caster is range (and the MP cost plus post-cast root that pay for it), never
    /// the damage curve.  Caller adds the Data1 contribution via <see cref="SpellContribution"/>, composed
    /// in <see cref="RawSpellPower"/>.</summary>
    public static int SpellPower(int @int) =>
        Math.Max((int)Math.Round(Math.Pow(@int + OffenseShift, DamageCurveExponent) / DamageCurveDivisor, MidpointRounding.AwayFromZero), 1);

    /// <summary>Weapon Data2 contribution to a player's raw melee damage.  Twice
    /// <see cref="GearMitigation"/> — a weapon pulls the weight of two armor pieces at matched Data2 and
    /// asymptotes at 2×Str.  At Data2 == Str the contribution exactly equals two armor pieces' combined
    /// mitigation, so total offense (1 weapon) and total defense (2 armor pieces) scale identically with
    /// stat: stat investment decides matched-gear fights, not gear arrangement.</summary>
    public static int WeaponContribution(int data2, int str) =>
        (int)Math.Round(2.0 * GearMitigationD(data2, Math.Max(str, 1)), MidpointRounding.AwayFromZero);

    /// <summary>Spell Data1 contribution — the exact mirror of <see cref="WeaponContribution"/> with Data1
    /// in place of a weapon's Data2 and Int in place of Str, DR-capped at 2×Int.  A prepared spell is a
    /// weapon delivered at range, so its Data1 pulls the weight of two armor pieces at matched data exactly
    /// as a weapon does.  Shared by every Add/Sub spell branch, so heals self-cap the same way.</summary>
    public static int SpellContribution(int data1, int @int) =>
        (int)Math.Round(2.0 * GearMitigationD(data1, Math.Max(@int, 1)), MidpointRounding.AwayFromZero);

    /// <summary>Diminishing-returns gear contribution: <c>data2 * stat / (stat + data2)</c>.
    /// Asymptotes at the paired stat — a single armor or helmet piece can never exceed the player's
    /// raw defensive stat in mitigation.  Used directly by armor/helmet; the shield's chip routes through
    /// <see cref="ShieldMitigation"/> (/4); weapon routes through
    /// <see cref="WeaponContribution"/> (which applies a 2× factor).</summary>
    public static int GearMitigation(int data2, int stat) =>
        (int)Math.Round(GearMitigationD(data2, stat), MidpointRounding.AwayFromZero);

    private static double GearMitigationD(int data2, int stat)
    {
        if (data2 <= 0) return 0.0;
        double denom = Math.Max(stat + data2, 1);
        return data2 * (double)stat / denom;
    }

    /// <summary>The shield's contribution to mitigation: 1/4 of a full armor piece, asymptoting at ~Def/4.
    /// A shield's main defensive jobs are block and this light chip; the chip lets it soak a little without
    /// handing a shielded build a full third armor piece.  DEF defends physical and magic identically, so
    /// there is no separate magic gear chip.</summary>
    private const double ShieldMitigationDivisor = 4.0;
    public static int ShieldMitigation(int data2, int def) =>
        (int)Math.Round(GearMitigationD(data2, def) / ShieldMitigationDivisor, MidpointRounding.AwayFromZero);

    /// <summary>Crit damage: 1.25× raw + uniform noise in [0, raw/2 + 1) + 1.  Always > raw.
    /// Used for both melee and spell crits.  Noise is sampled continuously (NextDouble × range)
    /// so the half-raw range carries fractional precision; the final value rounds to nearest.</summary>
    private const double CritBaseMultiplier = 1.25;
    private const double CritNoiseHalfMultiplier = 0.5;
    public static int CritDamage(int raw)
    {
        double noise = Random.Shared.NextDouble() * (raw * CritNoiseHalfMultiplier + 1.0);
        return (int)Math.Round(raw * CritBaseMultiplier + noise + 1.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Bell-curve variance, ±DamageVariance centered on dmg.  Apply to the
    /// raw attack BEFORE subtracting defense — varies the swing, not the leak.</summary>
    public static int Vary(int dmg) =>
        (int)Math.Round(dmg * (1.0 - DamageVariance + BellRand(4) * (2 * DamageVariance)), MidpointRounding.AwayFromZero);

    /// <summary>Irwin-Hall bell sample on [0, 1): average of <paramref name="rolls"/>
    /// independent uniforms.  More rolls = tighter bell on 0.5.</summary>
    private static double BellRand(int rolls)
    {
        double sum = 0;
        for (int i = 0; i < rolls; i++)
            sum += Random.Shared.NextDouble();
        return sum / rolls;
    }

    // ── Protection (one universal MIT — DEF defends physical and magic identically) ──────

    /// <summary>Player mitigation — the single universal MIT (physical == magic).  Level-primary:
    /// <c>(level+20)^1.5 / 11</c> is a baseline everyone gets, plus <c>def^1.5 / 14</c>, the DEF stat's
    /// payoff on top (0 at Def=0).  Gear adds via <see cref="GearMitigation"/> (armor/helmet full,
    /// shield 1/4).  DEF is a defensive sidegrade — it buys survivability rather than an outright win.
    /// Matched ^1.5 exponent with damage so mit and damage scale in step.</summary>
    public static int PlayerProtection(int level, int def) =>
        (int)Math.Round(PlayerProtectionD(level, def), MidpointRounding.AwayFromZero);

    private static double PlayerProtectionD(int level, int def) =>
        Math.Pow(Math.Max(level, 0) + MitigationLevelShift, DamageCurveExponent) / LevelMitigationDivisor
        + Math.Pow(Math.Max(def, 0), DamageCurveExponent) / DefMitigationDivisor;

    /// <summary>NPC mitigation — the same level-primary curve as a player (<see cref="PlayerProtection"/>),
    /// fed the NPC's <see cref="StatFormulas.NpcLevel"/> (all four stats, so a fast NPC's level floor makes
    /// it tankier exactly like a SPD-heavy player), plus a fully-kitted defender's gear baked in since NPCs
    /// wear none: armor + helmet at full <see cref="GearMitigation"/> + a shield at 1/4
    /// (<see cref="ShieldMitigationDivisor"/>), all at matched Def (Data2 = Def).  One universal MIT
    /// (physical == magic); no favor multiplier, since HP-only favor handles the NPC bias.</summary>
    public static int NpcProtection(int str, int def, int @int, int spd) =>
        (int)Math.Round(
            PlayerProtectionD(StatFormulas.NpcLevel(str, def, @int, spd), def)
            + 2.0 * GearMitigationD(def, def)                          // armor + helmet, full weight
            + GearMitigationD(def, def) / ShieldMitigationDivisor,     // shield, 1/4 weight
            MidpointRounding.AwayFromZero);

    public static int NpcProtection(NpcRecord npc) => NpcProtection(npc.Str, npc.Def, npc.Int, npc.Spd);

    /// <summary>Final damage from a landed hit: subtract mitigation, apply the universal
    /// <see cref="MinDamageFloorPercent"/> floor (a fraction of the varied raw, so a stacked-DEF defender is never
    /// fully immune), then a damage multiplier (<see cref="PvpDamageMultiplier"/> for player-vs-player, 1.0
    /// otherwise).  ONE chokepoint for every melee/spell damage path so the floor and the PvP cut can't drift
    /// between sites.  <paramref name="variedRaw"/> is the post-Vary (and post-crit) attack amount; block/dodge
    /// fully negate BEFORE this and never call in.</summary>
    public static int ResolveDamage(int variedRaw, int protection) => ResolveDamage(variedRaw, protection, 1.0, MinDamageFloorPercent);

    public static int ResolveDamage(int variedRaw, int protection, double damageMultiplier) =>
        ResolveDamage(variedRaw, protection, damageMultiplier, MinDamageFloorPercent);

    public static int ResolveDamage(int variedRaw, int protection, double damageMultiplier, double floorPercent)
    {
        int floor = (int)Math.Round(variedRaw * floorPercent, MidpointRounding.AwayFromZero);
        int dmg = Math.Max(variedRaw - protection, floor);
        return Math.Max((int)Math.Round(dmg * damageMultiplier, MidpointRounding.AwayFromZero), 0);
    }

    /// <summary>Final HP damage for a PLAYER hitting an NPC: <see cref="ResolveDamage(int,int)"/> but with the higher
    /// <see cref="PveMinDamageFloorPercent"/> so a low-offense build whose raw sits under a tanky mob's mitigation
    /// still makes real progress instead of a wall-hitting slog.  HP damage ONLY — MP/SP drains keep the standard
    /// floor.  Pures are unaffected (raw already &gt; mit).</summary>
    public static int ResolvePlayerVsNpcDamage(int variedRaw, int protection) =>
        ResolveDamage(variedRaw, protection, 1.0, PveMinDamageFloorPercent);

    // ── Block / dodge / crit chance ───────────────────────────────────────────
    // No hidden gate. The returned value IS the actual probability rolled against RollPerMille();
    // divide by Constants.ChanceScaleFactor (= 1 today) for the displayed percent.
    // Live caps: player block/crit/spellcrit 35%, player dodge 15%, NPC block/crit 25%, NPC dodge 10%
    // (NPC caps are lower because NPC DEF also drives HP/EXP). Bump Constants.ChanceScaleFactor to 10
    // and the same cap constants below reread as per-mille (3.5% / 2.5% / 1.0%) without any code change.
    // Callers own the RNG roll and SP/slot prerequisites.

    // Player chance is (Stat + Level) / divisor, capped; NPCs use Stat / divisor (no level term).  Because the
    // player numerator includes Level, players use LARGER divisors so the cap needs real endgame investment:
    // player crit/block 9, dodge 18 (dodge = 2x block, half its rate) — a 35% cap at Stat+Level=315.  NPCs use
    // crit/block 7, dodge 14 (caps: block/crit 25%, dodge 10%) — a 25% cap at Stat=175.
    private const double PlayerBlockChanceDivisor = 9.0;
    private const double PlayerDodgeChanceDivisor = 18.0;
    private const double PlayerCritChanceDivisor = 9.0;
    private const double SpellCritChanceDivisor = 9.0;
    private const double NpcCritChanceDivisor = 7.0;
    private const double NpcBlockChanceDivisor = 7.0;
    private const double NpcDodgeChanceDivisor = 14.0;
    // Caps below read as percent at ChanceScaleFactor=1; reread as per-mille at higher scale.
    private const int PlayerBlockChanceCapPerMille = 35;   // also caps magic-block
    private const int PlayerDodgeChanceCapPerMille = 15;   // a bit under half the block cap
    private const int PlayerCritChanceCapPerMille = 35;    // matches the block cap
    private const int SpellCritChanceCapPerMille = 35;
    private const int NpcCritChanceCapPerMille = 25;       // 25% (2.5% at scale 10)
    private const int NpcBlockChanceCapPerMille = 25;      // 25% (2.5% at scale 10)
    private const int NpcDodgeChanceCapPerMille = 10;      // 10% (1.0% at scale 10)

    /// <summary>Shield block chance (caller requires ShieldSlot > 0 and SP > 0).</summary>
    public static int PlayerBlockChancePerMille(int def, int level) =>
        Math.Min((int)Math.Round((def + level) / PlayerBlockChanceDivisor, MidpointRounding.AwayFromZero), PlayerBlockChanceCapPerMille);

    /// <summary>No-shield dodge chance (caller requires ShieldSlot == 0 and SP > 0).</summary>
    public static int PlayerDodgeChancePerMille(int def, int level) =>
        Math.Min((int)Math.Round((def + level) / PlayerDodgeChanceDivisor, MidpointRounding.AwayFromZero), PlayerDodgeChanceCapPerMille);

    /// <summary>Weapon crit chance (caller requires WeaponSlot > 0 and SP > 0).</summary>
    public static int PlayerCriticalChancePerMille(int str, int level) =>
        Math.Min((int)Math.Round((str + level) / PlayerCritChanceDivisor, MidpointRounding.AwayFromZero), PlayerCritChanceCapPerMille);

    /// <summary>Spell crit chance (caller requires SP > 0).</summary>
    public static int SpellCriticalChancePerMille(int @int, int level) =>
        Math.Min((int)Math.Round((@int + level) / SpellCritChanceDivisor, MidpointRounding.AwayFromZero), SpellCritChanceCapPerMille);

    /// <summary>NPC melee crit chance (caller requires SP > 0).</summary>
    public static int NpcCriticalChancePerMille(int str) =>
        Math.Min((int)Math.Round(str / NpcCritChanceDivisor, MidpointRounding.AwayFromZero), NpcCritChanceCapPerMille);

    /// <summary>NPC spell crit chance — the INT mirror of <see cref="NpcCriticalChancePerMille"/> (STR melee
    /// crit), sharing the same NPC crit dials.  Caller requires SP > 0.</summary>
    public static int NpcSpellCriticalChancePerMille(int @int) =>
        Math.Min((int)Math.Round(@int / NpcCritChanceDivisor, MidpointRounding.AwayFromZero), NpcCritChanceCapPerMille);

    /// <summary>NPC block chance (caller requires SP > 0).</summary>
    public static int NpcBlockChancePerMille(int def) =>
        Math.Min((int)Math.Round(def / NpcBlockChanceDivisor, MidpointRounding.AwayFromZero), NpcBlockChanceCapPerMille);

    /// <summary>NPC dodge chance (caller requires SP > 0).  Independent of block.</summary>
    public static int NpcDodgeChancePerMille(int def) =>
        Math.Min((int)Math.Round(def / NpcDodgeChanceDivisor, MidpointRounding.AwayFromZero), NpcDodgeChanceCapPerMille);

    // ── SP costs for combat actions ───────────────────────────────────────────
    // Block/crit cost 10% of MaxSP; dodge costs 20% (twice the price), which keeps reactions a rationed
    // resource rather than something in-combat SP regen makes free.  Negating a spell costs exactly the
    // same SP as negating a melee hit — magic and melee defense are the same action.  Computed in double
    // and rounded once so small pools don't floor at 1.

    private const double SpBlockCritPercent = 0.10;
    private const double SpDodgePercent = 0.20;

    public static int SpCostForBlockOrCrit(int maxSp) => Math.Max((int)Math.Round(maxSp * SpBlockCritPercent, MidpointRounding.AwayFromZero), 1);

    public static int SpCostForDodge(int maxSp) => Math.Max((int)Math.Round(maxSp * SpDodgePercent, MidpointRounding.AwayFromZero), 1);

    // ── Spell power / RNG helpers ─────────────────────────────────────────────

    /// <summary>Raw spell amount before crit and bell-curve variance.  Composes the caster's
    /// stat-based contribution (<see cref="SpellPower"/>) with the spell's content contribution
    /// (<see cref="SpellContribution"/> on Data1 — DR-capped at 2×Int, the same curve as a weapon).  Both pieces are
    /// sub-quadratic / bounded so designer-typed Data1 numbers can't blow past mitigation.
    /// Shared by player-target, NPC-target, and caster-self spell paths — heals scale through
    /// this same formula by design.</summary>
    public static int RawSpellPower(int @int, int data1) =>
        SpellPower(@int) + SpellContribution(data1, @int);

    /// <summary>Roll a uniform percentile in [0..99]. Seam for durability proc bands and
    /// item-drop chances — anything that lives on 1-percent granularity.</summary>
    public static int RollPercent() => Random.Shared.Next(Constants.PercentRollSides);

    /// <summary>Roll the seam for block/dodge/crit chances. The roll space scales with
    /// <see cref="Constants.ChanceScaleFactor"/>: at scale 1 it's [0..99] (percent), at scale 10
    /// it's [0..999] (per-mille for tenths-of-a-percent precision).</summary>
    public static int RollPerMille() => Random.Shared.Next(Constants.ChancePercentRollSides);

    /// <summary>Divisor turning a raw chance value into a displayed percent.</summary>
    public static int ChancePercentDivisor => Constants.ChanceScaleFactor;
    /// <summary>Decimal places to print for a chance value, derived from
    /// <see cref="Constants.ChanceScaleFactor"/> so the format matches the actual granularity:
    /// scale 1 -> 0 decimals (50 -> "50%"), scale 10 -> 1 decimal (50 -> "5.0%"), scale 100 -> 2.
    /// Dividing by the scale introduces exactly log10(scale) decimal places; Ceiling handles
    /// non-powers-of-10 (scale=5 still wants 1 decimal, e.g. 12/5 = 2.4) and Max(0,…) guards a
    /// degenerate scale &lt; 1 where log10 goes negative.</summary>
    public static int ChanceDisplayDecimals => Math.Max(0, (int)Math.Ceiling(Math.Log10(Constants.ChanceScaleFactor)));
    /// <summary>Render a raw chance value as a percent string.  Shared between server (/stats command)
    /// and client (StatsPanel, NewCharScreen) so both sides print the same format.</summary>
    public static string FormatPerMilleAsPercent(int chance) =>
        (chance / (double)ChancePercentDivisor).ToString($"F{ChanceDisplayDecimals}") + "%";

    // ── Durability warning bands ─────────────────────────────────────────────
    // Exactly one tier fires for a given durability percentage — the lowest band the item
    // falls into. Above DurWarnExcellentPct: silent. At/below DurWarnCriticalPct:
    // deterministic message (no roll). Middle bands each gate their message on a per-tier
    // proc chance, so a worn item nags at a calibrated rate instead of every hit.
    public const int DurWarnExcellentPct = 75;  // pct strictly below this: any warning at all
    public const int DurWarnRepairPct = 25;  // <= this: "needs repairing"
    public const int DurWarnWornPct = 50;  // <= this (and > Repair): "getting worn"
    public const int DurWarnCriticalPct = 5;   // <= this: critical, no roll

    public const int DurWarnRepairProcPct = 25;  // chance the Repair-band message prints
    public const int DurWarnWornProcPct = 15;  // chance the Worn-band message prints
    public const int DurWarnFineProcPct = 5;   // chance the Fine-band message (>50% pct) prints

    // ── Durability degrade chance ────────────────────────────────────────────
    // Worn gear chips faster than fresh gear: a hit only costs 1 durability on a roll whose odds
    // rise as the piece wears down (current dur as a percent of max). Exactly one band applies —
    // the highest condition tier the item still meets. Below 25% the chip is certain.
    public const int DurDegradeHealthyPct = 75;  // >= this %: best condition
    public const int DurDegradeWornPct = 50;  // >= this % (and < Healthy)
    public const int DurDegradeDamagedPct = 25;  // >= this % (and < Worn)

    public const int DurDegradeHealthyChancePct = 25;   // >= 75% condition: 25% chance to chip
    public const int DurDegradeWornChancePct = 50;   // 50-74%: 50%
    public const int DurDegradeDamagedChancePct = 75;   // 25-49%: 75%
    public const int DurDegradeCriticalChancePct = 100;  // < 25%: always chips

    /// <summary>Percent chance that a single hit reduces a worn item's durability by 1, scaled by
    /// current condition (<paramref name="dur"/> as a percent of <paramref name="maxDur"/>). Fresher
    /// gear chips less often; badly worn gear chips every hit. Caller rolls via <see cref="RollPercent"/>
    /// (degrade when the roll is below this value). <paramref name="maxDur"/> must be &gt; 0.</summary>
    public static int DurabilityDegradeChancePercent(int dur, int maxDur)
    {
        double pct = (double)dur * 100 / maxDur;
        if (pct >= DurDegradeHealthyPct) return DurDegradeHealthyChancePct;
        if (pct >= DurDegradeWornPct) return DurDegradeWornChancePct;
        if (pct >= DurDegradeDamagedPct) return DurDegradeDamagedChancePct;
        return DurDegradeCriticalChancePct;
    }

    // ── Spell costs ───────────────────────────────────────────────────────────

    // Class-affinity gate head-start: a spell needs INT off Data1 exactly as a weapon needs STR off
    // Data2, so power gates itself on the matching stat.  Effective requirement = raw - round(classStat/K)
    // for STR (weapons), DEF (armor/helmet/shield), and INT (spells) alike.  It shifts only the ACCESS
    // THRESHOLD, never combat power, so the STR/INT mirror holds (equal stats fight identically regardless
    // of class).  Rounds to nearest so every class-stat point pays off uniformly rather than toward a
    // flooring boundary.  Larger divisor = smaller head-start.
    private const double ClassAffinityGateDivisor = 4.0;

    public static int ClassAffinityBonus(int classStat) =>
        (int)Math.Round(classStat / ClassAffinityGateDivisor, MidpointRounding.AwayFromZero);

    // Gear equip requirement after the wearer's class head-start.  Data2 is the raw STR (weapon) or DEF
    // (armor/helmet/shield) requirement.  Floored at 1, matching spells: a Data2=0 piece is a data mistake
    // (every real item carries a requirement), not a valid "free" case.
    public static int GearStatRequirement(int data2, int classStat) =>
        Math.Max(1, data2 - ClassAffinityBonus(classStat));

    // Spell INT requirement, the magic-side mirror of GearStatRequirement.  GiveItem gates off Data3
    // (Data1 is an item ID there).  Floored at 1: a real spell always carries magnitude Data1>=1, so
    // unlike free gear it keeps a token requirement even for a high-INT class.
    private static int RawSpellIntRequirement(SpellRecord spell, int classInt) =>
        spell.Data1 - ClassAffinityBonus(classInt);

    public static int GetSpellIntRequirement(SpellRecord spell, int classInt) =>
        spell.Type == SpellType.GiveItem
            ? Math.Max(1, spell.Data3 - ClassAffinityBonus(classInt))
            : Math.Max(1, RawSpellIntRequirement(spell, classInt));

    // The pre-head-start INT requirement a spell is authored with: Data1 for a normal spell (its
    // magnitude doubles as its gate), Data3 for GiveItem (Data1 is an item ID there).  Pairs with
    // GetSpellIntRequirement; the gap between the two is the head-start actually applied, shown as "(-N)".
    public static int RawSpellRequirement(SpellRecord spell) =>
        spell.Type == SpellType.GiveItem ? spell.Data3 : spell.Data1;

    // Cost = (gate + 15)^1.5 / SpellMpCostDivisor, where gate = RawSpellRequirement.  MP cost is a pure
    // function of the value that gates learning the spell, uniform across every spell type, and carries no
    // classInt term so cost stays class-independent (a class-based reduction would be an asymmetric,
    // non-recoverable perk).  Class identity comes from the MaxMP pool and the affinity head-start on the
    // INT gate, not from cost.  Same shifted-quadratic shape as SpellPower, the MaxMp pool, and HP/MP regen.
    // Sub-quadratic growth keeps low-Data1 spells affordable while high-Data1 spells reach genuinely large
    // costs (D1=250 → 718 MP at divisor=6) that bite even against endgame pools.  Larger divisor = cheaper.
    private const double SpellMpCostDivisor = 6.0;
    // AddMp/AddSp pay an "undo premium": restoring a vital costs slightly more mana than the SubMp/SubSp
    // drain that took it.  AddHp is excluded — its counterpart SubHp is the caster's reagent-gated trivial-MP
    // weapon, so there is no full-mana damage cost for a heal to be priced slightly above.
    private const double AddSpellCostMultiplier = 1.10;
    private const int SpellMpCostShift = 15;

    public static int GetSpellMpCost(SpellRecord spell)
    {
        double cost = Math.Pow(RawSpellRequirement(spell) + SpellMpCostShift, DamageCurveExponent) / SpellMpCostDivisor;
        if (spell.Type is SpellType.AddMp or SpellType.AddSp)
            cost *= AddSpellCostMultiplier;
        return Math.Max(1, (int)Math.Round(cost, MidpointRounding.AwayFromZero));
    }

    // ── Caster resource model: SubHp is the sustainable weapon; MP is utility; a reagent is the per-cast sink ──
    // SubHp (basic damage) does NOT pay the utility cost above.  Its MP cost is a flat, trivial fraction of the
    // caster's pool — mana is a distant "don't marathon forever" ceiling that regen covers over a normal fight,
    // not a per-cast damage gate.  /20 gives a hybrid caster ~55-65 casts before OOM; a pure-Int caster's regen
    // fully sustains it.  Level-independent because in-combat MP regen is ~a constant % of the pool at every level.
    private const int SubHpMpCostDivisor = 20;
    public static int GetSubHpSpellMpCost(int maxMp) =>
        Math.Max(1, (int)Math.Round((double)maxMp / SubHpMpCostDivisor, MidpointRounding.AwayFromZero));

    // Reagent consumed per SubHp cast — the magic-side mirror of weapon-repair upkeep, priced in reagents worth
    // 1 gold each.  A warrior's per-SWING upkeep = (gold to repair 1 durability) × (durability actually lost that
    // swing).  Gold per durability = Data2/10 (ShopSystem: full repair = durNeeded × (Data2/5) / 2).  A swing CHIPS
    // on a rising CHANCE (DurabilityDegradeChancePercent), NOT every hit, so the true durability lost per swing is
    // the swing-weighted average of those chances (~0.48, AvgDurabilityDegradePerHit) — a caster is no more bound to
    // "1 cast = 1 durability cost" than a warrior is to "1 hit = 1 durability".  So reagents/cast =
    // round(Data1/10 × 0.48) ≈ Data1/21.  Consumption scales with spell power (Data1, the mirror of a weapon's
    // Data2), never the item's fixed value.  Floored at 1 so every cast carries a token cost.
    private const double RepairGoldPerDurabilityDivisor = 10.0;   // = ShopSystem ratePerPoint (Data2/5), halved for full repair
    public static int SubHpReagentCost(int data1) =>
        Math.Max(1, (int)Math.Round(data1 / RepairGoldPerDurabilityDivisor * AvgDurabilityDegradePerHit(), MidpointRounding.AwayFromZero));

    // The wear percent of a normal (non-PK, non-war) death — the basis the caster-death multiplier is
    // calibrated to (a PK/war death passes 20, i.e. double).
    private const int NormalDeathWearPercent = 10;

    /// <summary>Reagents a caster destroys on death: the per-cast reagent cost at its tier
    /// (<paramref name="tierData1"/> = the prepared spell's power, else the strongest known SubHp
    /// spell's) times <see cref="Constants.CasterDeathReagentMultiplier"/>, scaled by the death's
    /// <paramref name="wearPercent"/> (10 normal, 20 PK/war). Priced off the prepared spell independently of
    /// any equipped weapon (whose durability wears separately). Mirrors a warrior's weapon-repair cost at
    /// 1 reagent = 1 gold. 0 when the caster has no offensive tier.</summary>
    public static int CasterDeathReagentLoss(int tierData1, int wearPercent) =>
        tierData1 <= 0 ? 0
            : SubHpReagentCost(tierData1) * Constants.CasterDeathReagentMultiplier * wearPercent / NormalDeathWearPercent;

    /// <summary>Swing-weighted average durability lost per hit over a full 100%→0% wear cycle: total durability
    /// (100) divided by the hits needed to traverse every condition band (each band's width ÷ its chip chance =
    /// hits spent in that band).  The four 25%-wide bands at 25/50/75/100% chance give 100 / (100+50+33.3+25)
    /// ≈ 0.48.  Derived from the <see cref="DurabilityDegradeChancePercent"/> bands, so it tracks any change
    /// to them.</summary>
    private static double AvgDurabilityDegradePerHit()
    {
        (int WidthPct, int ChancePct)[] bands =
        {
            (100 - DurDegradeHealthyPct,               DurDegradeHealthyChancePct),
            (DurDegradeHealthyPct - DurDegradeWornPct, DurDegradeWornChancePct),
            (DurDegradeWornPct - DurDegradeDamagedPct, DurDegradeDamagedChancePct),
            (DurDegradeDamagedPct,                     DurDegradeCriticalChancePct),
        };
        double hits = 0;
        foreach (var (widthPct, chancePct) in bands)
            hits += widthPct / (chancePct / 100.0);   // durability points in the band ÷ chip-fraction = hits to cross it
        return 100.0 / hits;
    }

    /// <summary>Map an MP-scale spell effect onto the linear, ~10× smaller SP pool by preserving the FRACTION:
    /// an SP drain or restore moves the same % of SP that the equivalent MP spell moves of MP.  Used by
    /// SubSp/AddSp so an SP spell (authored on the MP scale, like SubMp/AddMp) nicks a scale-correct sliver
    /// instead of emptying the pool.</summary>
    public static int ScaleMpEffectToSp(int mpAmount, int maxMp, int maxSp) =>
        maxMp <= 0 ? 0 : (int)Math.Round((double)maxSp * mpAmount / maxMp, MidpointRounding.AwayFromZero);

    /// <summary>NPC melee damage — unified with the player formula at matched gear.  Equals
    /// <see cref="UnarmedDamage"/> + <see cref="WeaponContribution"/>(Str, Str), i.e. the same
    /// damage an equivalent-Str player would deal wielding a Data2=Str weapon.  No damage favor;
    /// HP-only favor (in StatFormulas.GetNpcMaxHp) handles the "NPC slightly stronger" feel
    /// without compounding combat-math advantages.</summary>
    public static int NpcMeleeBaseDamage(int str) =>
        UnarmedDamage(str) + WeaponContribution(str, str);

    /// <summary>NPC spell base magnitude — the center of a symmetric ±10% <see cref="Vary"/> roll, the exact
    /// mirror of <see cref="NpcMeleeBaseDamage"/> on the magic side.  Equals <see cref="SpellPower"/>(Int) +
    /// <see cref="SpellContribution"/>(Int, Int), i.e. the same raw spell power an equivalent-Int player would
    /// deliver with a Data1=Int spell — and, since spell and melee curves are identical, exactly
    /// <see cref="NpcMeleeBaseDamage"/> evaluated on Int.  No damage favor (HP-only favor handles the NPC bias).</summary>
    public static int NpcSpellBaseMagnitude(int @int) =>
        SpellPower(@int) + SpellContribution(@int, @int);
}
