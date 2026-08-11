namespace Mirage.Shared;

/// <summary>
/// Death-time loss math.  Drop-chance percentages live in <see cref="Constants"/>
/// since they're standalone game rules, not formula coefficients.
/// </summary>
public static class EconomyFormulas
{
    private const double PercentDenominator = 100.0;
    private const int EquipmentDamageFloor = 1;

    /// <summary>Each equipped item loses <paramref name="percentOfMax"/>% of its max durability,
    /// floor 1.  Used for the normal (10%) and PK (20%) death penalties.</summary>
    public static int EquipmentDamageOnDeath(int maxDur, int percentOfMax) =>
        Math.Max((int)Math.Round(maxDur * percentOfMax / PercentDenominator, MidpointRounding.AwayFromZero), EquipmentDamageFloor);

    /// <summary>Gold cost per durability point for repairing an item with the given <c>Data2</c> repair
    /// rate (<c>Data2 / 5</c>), floored at 1.</summary>
    public static int RepairRatePerPoint(int data2) => Math.Max(1, data2 / 5);

    /// <summary>Gold to repair <paramref name="durabilityPoints"/> of durability on an item with the given
    /// <c>Data2</c> repair rate: <c>points * ratePerPoint / 2</c>, floored at 1. The single source of truth
    /// for the repair formula — the shop repair path and the guild-war vault-repair sink both use it, so the
    /// war "vault pays 75% of the repair cost" is priced by the normal repair formula.</summary>
    public static int RepairCost(int durabilityPoints, int data2) =>
        Math.Max(1, durabilityPoints * RepairRatePerPoint(data2) / 2);
}
