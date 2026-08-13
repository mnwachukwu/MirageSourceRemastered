using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Accounts;

/// <summary>The per-class starting loadout — <see cref="ClassRecord.StartingItems"/> and
/// <see cref="ClassRecord.StartingSpells"/>.
///
/// <para>The canonicalization half is pinned here. The GRANT half lives in
/// <c>PacketHandler.Account.GrantStartingLoadout</c> and is exercised through the gate formulas below:
/// the rule is that NOBODY STARTS WITH SOMETHING THEY CANNOT USE, so what matters is that the generator
/// and the engine ask the same question and agree on the answer. Those two asking different questions is
/// the failure that would ship a class whose authored gear silently vanishes at creation.</para></summary>
[TestFixture]
public class StartingLoadoutTests
{
    [Test]
    public void Normalize_DropsLinesNamingNoItem()
    {
        var cls = new ClassRecord
        {
            Name = "Warrior",
            StartingItems = [new ClassStartingItem { ItemNum = 4 }, new ClassStartingItem { ItemNum = 0 }],
        };

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems!, Has.Count.EqualTo(1));
            Assert.That(cls.StartingItems[0].ItemNum, Is.EqualTo(4));
        });
    }

    [Test]
    public void Normalize_DedupesSpells()
    {
        // The spellbook is a set. A duplicate would burn one of MaxPlayerSpells slots for nothing, since
        // every other learn path refuses a spell already known.
        var cls = new ClassRecord { Name = "Sage", StartingSpells = [3, 13, 3, 13, 7] };

        cls.Normalize();

        Assert.That(cls.StartingSpells, Is.EqualTo(new[] { 3, 13, 7 }));
    }

    [Test]
    public void Normalize_CollapsesEmptyToNull()
    {
        // A class that grants nothing should carry no key at all — the Knight's whole book.
        var cls = new ClassRecord { Name = "Knight", StartingItems = [], StartingSpells = [] };

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems, Is.Null);
            Assert.That(cls.StartingSpells, Is.Null);
        });
    }

    [Test]
    public void Normalize_CapsToWhatACharacterCanHold()
    {
        var cls = new ClassRecord { Name = "Hoarder", StartingItems = [], StartingSpells = [] };
        for (int i = 1; i <= Constants.MaxInv + 10; i++) cls.StartingItems.Add(new ClassStartingItem { ItemNum = i });
        for (int i = 1; i <= Constants.MaxPlayerSpells + 10; i++) cls.StartingSpells.Add(i);

        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems!, Has.Count.EqualTo(Constants.MaxInv));
            Assert.That(cls.StartingSpells!, Has.Count.EqualTo(Constants.MaxPlayerSpells));
        });
    }

    [Test]
    public void Normalize_IsIdempotent()
    {
        var cls = new ClassRecord { Name = "Sage", StartingItems = [new ClassStartingItem { ItemNum = 4 }], StartingSpells = [3, 3] };

        cls.Normalize();
        cls.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(cls.StartingItems!, Has.Count.EqualTo(1));
            Assert.That(cls.StartingSpells!, Has.Count.EqualTo(1));
        });
    }

    // ── The gate the grant path applies ──────────────────────────────────────
    // A brand-new character has EXACTLY its class's base stats, so "can this class start with it?" is
    // GearStatRequirement(power, classStat) <= classStat. These pin the cases the ten-class roster
    // actually lands on, because each one reads as a design statement rather than an accident.

    [TestCase(15, 10, true, TestName = "Warrior STR 15 lifts a heavy tier-1 weapon (Power 10)")]
    [TestCase(5, 10, false, TestName = "Wanderer STR 5 cannot lift a heavy tier-1 weapon")]
    [TestCase(5, 6, true, TestName = "Wanderer STR 5 lifts a LIGHT tier-1 weapon (Power 6)")]
    [TestCase(0, 6, false, TestName = "A 0-STR class lifts nothing at all")]
    public void GearGate_AtStartingStats(int classStat, int power, bool expected)
    {
        bool canUse = CombatFormulas.GearStatRequirement(power, classStat) <= classStat;

        Assert.That(canUse, Is.EqualTo(expected));
    }

    [Test]
    public void GearGate_ACapIsACeilingNotAFloor()
    {
        // The Warrior's chart says MEDIUM armor, but DEF 5 cannot meet a Power-8 medium piece at level 1
        // (requirement 7). It starts in light instead. That is the cap working as designed — a class
        // falls back rather than being locked out — and it is why the generator picks the best PASSING
        // piece rather than the piece its chart names.
        const int warriorDef = 5, mediumPower = 8, lightPower = 6;

        Assert.Multiple(() =>
        {
            Assert.That(CombatFormulas.GearStatRequirement(mediumPower, warriorDef), Is.GreaterThan(warriorDef));
            Assert.That(CombatFormulas.GearStatRequirement(lightPower, warriorDef), Is.LessThanOrEqualTo(warriorDef));
        });
    }

    [TestCase(15, 10, true, TestName = "Sorcerer INT 15 casts a high tier-1 spell (VitalAmount 10)")]
    [TestCase(5, 10, false, TestName = "Wanderer INT 5 cannot cast a high tier-1 spell")]
    [TestCase(5, 6, true, TestName = "Wanderer INT 5 casts a LOW tier-1 spell (VitalAmount 6)")]
    [TestCase(0, 6, false, TestName = "A 0-INT class starts with no spell at all")]
    public void SpellGate_AtStartingStats(int classInt, int vitalAmount, bool expected)
    {
        var spell = new SpellRecord { Name = "S", Type = SpellType.SubHp, VitalAmount = (short)vitalAmount };

        bool canLearn = CombatFormulas.GetSpellIntRequirement(spell, classInt) <= classInt;

        Assert.That(canLearn, Is.EqualTo(expected));
    }
}
