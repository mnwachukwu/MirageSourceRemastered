using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>The shared repair-cost formula (the single source of truth used by both the shop repair path
/// and the guild-war vault-repair sink) + the on-death equipment wear.</summary>
[TestFixture]
public class EconomyFormulasTests
{
    [Test]
    public void RepairRatePerPoint_IsPowerOver5_FlooredAt1()
    {
        Assert.That(EconomyFormulas.RepairRatePerPoint(50), Is.EqualTo(10));
        Assert.That(EconomyFormulas.RepairRatePerPoint(3), Is.EqualTo(1));   // 0 -> floor 1
    }

    [Test]
    public void RepairCost_IsPointsTimesRateOverTwo_FlooredAt1()
    {
        Assert.That(EconomyFormulas.RepairCost(durabilityPoints: 10, power: 50), Is.EqualTo(50));   // 10*10/2
        Assert.That(EconomyFormulas.RepairCost(4, 50), Is.EqualTo(20));                             // 4*10/2
        Assert.That(EconomyFormulas.RepairCost(1, 3), Is.EqualTo(1));                               // 1*1/2 -> floor 1
    }

    [Test]
    public void EquipmentDamageOnDeath_IsPercentOfMax_FlooredAt1()
    {
        Assert.That(EconomyFormulas.EquipmentDamageOnDeath(maxDur: 100, percentOfMax: 20), Is.EqualTo(20));
        Assert.That(EconomyFormulas.EquipmentDamageOnDeath(100, 10), Is.EqualTo(10));
        Assert.That(EconomyFormulas.EquipmentDamageOnDeath(1, 10), Is.EqualTo(1));   // 0.1 -> floor 1
    }
}
