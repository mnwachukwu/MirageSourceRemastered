using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests;

// Deterministic locks on the damage/vital invariants that Simulations/CombatSim only checks by printed
// eyeball (the sim reimplements the math standalone, so it can silently drift from Mirage.Shared).  These
// pin the REAL formulas.  Parity/mirror properties (STR==INT offense, NPC<->player pools/regen/mit) already
// live in NpcPlayerFormulaParityTests; this file covers the pieces that fixture doesn't: the damage floors,
// crit/variance shape, and the player HP==MP mirror.
[TestFixture]
public class CombatInvariantTests
{
    static readonly int[] StatSweep = { 0, 1, 5, 10, 30, 60, 100, 150, 200, 255 };
    static readonly int[] LevelSweep = { 1, 10, 30, 60, 100, 150, 200, 255 };
    static readonly int[] RawSweep = { 1, 5, 10, 50, 100, 500, 1000 };
    const int HugeProtection = 1_000_000;   // more mitigation than any real hit -> the floor always binds

    // A stacked-DEF defender is never fully immune: through unlimited mitigation the hit still lands the
    // MinDamageFloorPercent (12%) fraction of the varied raw.  Locks the "no immortal tank" guardrail.
    [Test]
    public void ResolveDamage_FloorsAtMinDamagePercent_ThroughAnyMitigation()
    {
        foreach (int raw in RawSweep)
        {
            int expectedFloor = (int)Math.Round(raw * CombatFormulas.MinDamageFloorPercent, MidpointRounding.AwayFromZero);
            Assert.That(CombatFormulas.ResolveDamage(raw, HugeProtection), Is.EqualTo(expectedFloor),
                $"through huge mitigation the hit floors at 12% of raw={raw}");
        }
        // Any hit of real size (>= 5 raw) always deals at least 1 through any DEF.
        Assert.That(CombatFormulas.ResolveDamage(5, HugeProtection), Is.GreaterThanOrEqualTo(1));
        Assert.That(CombatFormulas.ResolveDamage(1000, HugeProtection), Is.GreaterThanOrEqualTo(1));
    }

    // Player-vs-NPC HP damage uses a HIGHER floor (35%) so a low-offense build still chips a tanky mob
    // instead of walling — and it is never below the standard floor.
    [Test]
    public void ResolvePlayerVsNpcDamage_FloorsHigher_AndNeverBelowStandard()
    {
        foreach (int raw in RawSweep)
        {
            int expectedPve = (int)Math.Round(raw * CombatFormulas.PveMinDamageFloorPercent, MidpointRounding.AwayFromZero);
            Assert.That(CombatFormulas.ResolvePlayerVsNpcDamage(raw, HugeProtection), Is.EqualTo(expectedPve),
                $"PvE HP damage floors at 35% of raw={raw}");
            Assert.That(CombatFormulas.ResolvePlayerVsNpcDamage(raw, HugeProtection),
                Is.GreaterThanOrEqualTo(CombatFormulas.ResolveDamage(raw, HugeProtection)),
                "the PvE floor is never below the standard floor");
        }
    }

    // A crit always beats the raw hit it upgrades (1.25x + positive noise + 1), for every sample.
    [Test]
    public void CritDamage_AlwaysExceedsRaw()
    {
        foreach (int raw in RawSweep)
        {
            for (int i = 0; i < 2000; i++)
                Assert.That(CombatFormulas.CritDamage(raw), Is.GreaterThan(raw), $"a crit must exceed raw={raw}");
        }
    }

    // Variance stays inside the +/-DamageVariance band around the input, for every sample.
    [Test]
    public void Vary_StaysWithinVarianceBand()
    {
        double v = CombatFormulas.DamageVariance;
        foreach (int dmg in RawSweep)
        {
            int lo = (int)Math.Floor(dmg * (1.0 - v));
            int hi = (int)Math.Ceiling(dmg * (1.0 + v));
            for (int i = 0; i < 2000; i++)
            {
                int varied = CombatFormulas.Vary(dmg);
                Assert.That(varied, Is.InRange(lo, hi), $"Vary({dmg}) must stay within +/-{v:P0}");
            }
        }
    }

    // HP and MP are one 1:1 pool at equal investment: at the same level and stat, DEF-driven HP equals
    // INT-driven MP (the mirror's defining pool property).
    [Test]
    public void PlayerMaxHp_EqualsMaxMp_AtEqualInvestment()
    {
        foreach (int level in LevelSweep)
        {
            foreach (int stat in StatSweep)
            {
                Assert.That(StatFormulas.GetPlayerMaxHp(level, stat, 0), Is.EqualTo(StatFormulas.GetPlayerMaxMp(level, stat, 0)),
                    $"HP (DEF) must equal MP (INT) at level={level}, stat={stat}");
            }
        }
    }

    // Caster reagent parity: the on-death reagent loss = per-cast cost x multiplier, scaled by
    // the death's wear percent (a PK/war 20% death costs double a normal 10% death), and 0 with no tier.
    [Test]
    public void CasterDeathReagentLoss_ScalesWithTierAndWearPercent()
    {
        int perCast = CombatFormulas.SubHpReagentCost(100);
        int normal = CombatFormulas.CasterDeathReagentLoss(tierVitalAmount: 100, wearPercent: 10);
        Assert.That(normal, Is.EqualTo(perCast * Constants.CasterDeathReagentMultiplier));
        Assert.That(CombatFormulas.CasterDeathReagentLoss(100, 20), Is.EqualTo(normal * 2));   // PK/war doubles it
        Assert.That(CombatFormulas.CasterDeathReagentLoss(tierVitalAmount: 0, wearPercent: 10), Is.EqualTo(0));   // no tier
    }
}
