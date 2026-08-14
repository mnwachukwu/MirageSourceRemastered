using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>The drop TABLE replacing the single DropChance/DropItem/DropItemValue triple.
///
/// <para>Two things carry real risk and are pinned here. First the MIGRATION: a world authored before the
/// table has to keep its drops, and it only gets one chance — <c>Normalize</c> clears the legacy fields as
/// it folds them, so a bug that loses the fold loses the data silently. Second the CANONICAL FORM: the
/// roller reads the table directly, so an inert line saved to disk is a line that looks authored and does
/// nothing.</para></summary>
[TestFixture]
public class NpcDropTableTests
{
    [Test]
    public void Normalize_FoldsALegacySingleDropIntoTheTable()
    {
        var npc = new NpcRecord { Name = "Bandit", DropChance = 40, DropItem = 12, DropItemValue = 7 };

        npc.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops, Is.Not.Null);
            Assert.That(npc.Drops!, Has.Count.EqualTo(1));
            Assert.That(npc.Drops[0].ItemNum, Is.EqualTo(12));
            Assert.That(npc.Drops[0].Chance, Is.EqualTo((short)40));
            Assert.That(npc.Drops[0].Quantity, Is.EqualTo((short)7));
            // Cleared, so the next save writes the table and nothing else — the file stops being legacy.
            Assert.That(npc.DropChance, Is.EqualTo((short)0));
            Assert.That(npc.DropItem, Is.EqualTo(0));
            Assert.That(npc.DropItemValue, Is.EqualTo((short)0));
        });
    }

    [Test]
    public void Normalize_IsIdempotent_SoLoadThenSaveDoesNotDuplicate()
    {
        // Normalize runs on load AND on every editor save. If folding were not guarded by the legacy
        // fields being cleared, a save after a load would append the same drop a second time.
        var npc = new NpcRecord { Name = "Bandit", DropChance = 40, DropItem = 12 };

        npc.Normalize();
        npc.Normalize();
        npc.Normalize();

        Assert.That(npc.Drops!, Has.Count.EqualTo(1));
    }

    [Test]
    public void Normalize_LeavesAModernRecordAlone()
    {
        var npc = new NpcRecord
        {
            Name = "Bandit",
            Drops = [new NpcDrop { ItemNum = 1, Quantity = 10, Chance = 90 },
                     new NpcDrop { ItemNum = 40, Chance = 2 }],
        };

        npc.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops!, Has.Count.EqualTo(2));
            Assert.That(npc.Drops[0].ItemNum, Is.EqualTo(1));
            Assert.That(npc.Drops[1].ItemNum, Is.EqualTo(40));
        });
    }

    [Test]
    public void Normalize_DropsInertLines()
    {
        var npc = new NpcRecord
        {
            Name = "Bandit",
            Drops =
            [
                new NpcDrop { ItemNum = 1, Chance = 50 },    // live
                new NpcDrop { ItemNum = 0, Chance = 50 },    // no item — a half-authored row
                new NpcDrop { ItemNum = 9, Chance = 0 },     // can never land
            ],
        };

        npc.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops!, Has.Count.EqualTo(1));
            Assert.That(npc.Drops[0].ItemNum, Is.EqualTo(1));
        });
    }

    [Test]
    public void Normalize_CollapsesAnEmptyTableToNull()
    {
        // "Drops nothing" should carry no key on disk at all, matching how an unrestricted AllowedClasses
        // collapses. An empty [] and a missing key must not be two spellings of the same thing.
        var npc = new NpcRecord { Name = "Rat", Drops = [] };

        npc.Normalize();

        Assert.That(npc.Drops, Is.Null);
    }

    [Test]
    public void Normalize_CapsTheTable()
    {
        var npc = new NpcRecord { Name = "Hoarder", Drops = [] };
        for (int i = 1; i <= Constants.MaxNpcDrops + 5; i++)
            npc.Drops.Add(new NpcDrop { ItemNum = i, Chance = 10 });

        npc.Normalize();

        Assert.That(npc.Drops!, Has.Count.EqualTo(Constants.MaxNpcDrops));
    }

    [Test]
    public void Normalize_ALegacyRecordWithNoDropStaysEmpty()
    {
        // DropChance without DropItem (or the reverse) was always a misconfiguration; it must not become
        // a live table line naming item 0.
        var chanceOnly = new NpcRecord { Name = "A", DropChance = 50 };
        var itemOnly = new NpcRecord { Name = "B", DropItem = 3 };

        chanceOnly.Normalize();
        itemOnly.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(chanceOnly.Drops, Is.Null);
            Assert.That(itemOnly.Drops, Is.Null);
        });
    }

    [Test]
    public void IsLive_RequiresBothAnItemAndAChance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NpcDrop { ItemNum = 1, Chance = 1 }.IsLive, Is.True);
            Assert.That(new NpcDrop { ItemNum = 1, Chance = 0 }.IsLive, Is.False);
            Assert.That(new NpcDrop { ItemNum = 0, Chance = 1 }.IsLive, Is.False);
            Assert.That(new NpcDrop().IsLive, Is.False);
        });
    }
}
