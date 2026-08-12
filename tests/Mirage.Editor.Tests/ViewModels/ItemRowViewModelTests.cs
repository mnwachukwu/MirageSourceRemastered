using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests;

/// <summary>The item-editor row view-model: faithful record round-trip, the dirty-flag lifecycle, and the
/// load-clobbers-save guard (applying a server packet must NOT flip a clean row to "edited"), plus the
/// type-driven Data1/2/3 field visibility that mirrors which item types actually use each data slot.</summary>
[TestFixture]
public class ItemRowViewModelTests
{
    static ItemRecord Sword() => new()
    {
        Name = "Rusty Sword", Pic = 12, Type = ItemType.Weapon, Data1 = 100, Data2 = 8, Data3 = 2,
    };

    static ItemRowViewModel Row(ItemType type) => new(1, new ItemRecord { Type = type });

    [Test]
    public void Ctor_FromRecord_RoundTrips_AndIsClean()
    {
        var vm = new ItemRowViewModel(3, Sword());
        var r = vm.ToRecord();
        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("Rusty Sword"));
            Assert.That(r.Pic, Is.EqualTo((short)12));
            Assert.That(r.Type, Is.EqualTo(ItemType.Weapon));
            Assert.That(r.Data1, Is.EqualTo((short)100));
            Assert.That(r.Data2, Is.EqualTo((short)8));
            Assert.That(r.Data3, Is.EqualTo((short)2));
            Assert.That(vm.IsDirty, Is.False, "a freshly loaded row is not dirty");
            Assert.That(vm.IsLoaded, Is.True);
        });
    }

    [Test]
    public void EditingAField_MarksDirty()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Data1 = 55;
        Assert.That(vm.IsDirty, Is.True);
    }

    [Test]
    public void ClearDirty_Resets()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Name = "Edited";
        vm.ClearDirty();
        Assert.That(vm.IsDirty, Is.False);
    }

    // Loading from a record must not read as an edit (the _loading guard), and it clears any prior dirt.
    [Test]
    public void LoadFromRecord_UpdatesFields_WithoutMarkingDirty()
    {
        var vm = new ItemRowViewModel(3, Sword());
        vm.Name = "Edited";   // now dirty

        vm.LoadFromRecord(new ItemRecord { Name = "Shield", Type = ItemType.Shield, Data1 = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "a load is not an edit");
            Assert.That(vm.ToRecord().Type, Is.EqualTo(ItemType.Shield));
            Assert.That(vm.ToRecord().Data1, Is.EqualTo((short)50));
        });
    }

    // The reported failure shape (cf. NpcSizeRoundTrip): an online packet load must NOT dirty a clean row,
    // else an open-then-save would silently rewrite the item.
    [Test]
    public void ApplyPacket_SeedsFields_MarksLoaded_ButNotDirty()
    {
        var vm = new ItemRowViewModel(1, new ItemRecord { Name = "" }, isLoaded: false);

        vm.ApplyPacket(new UpdateItemPacket { Name = "Potion", Type = ItemType.PotionAddHp, Data1 = 25 });

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "loading from the wire is not an edit");
            Assert.That(vm.IsLoaded, Is.True, "the row is now loaded");
            Assert.That(vm.ToRecord().Name, Is.EqualTo("Potion"));
            Assert.That(vm.ToRecord().Type, Is.EqualTo(ItemType.PotionAddHp));
            Assert.That(vm.ToRecord().Data1, Is.EqualTo((short)25));
        });
    }

    // Data-slot visibility follows the item type: only equipment exposes Data2 (bonus/req) and Data3 (class
    // req); keys/currency/none expose nothing; a spell scroll flags Data1 as a spell number.
    [Test]
    public void DataVisibility_FollowsItemType()
    {
        Assert.Multiple(() =>
        {
            var weapon = Row(ItemType.Weapon);
            Assert.That(weapon.Data1Visible, Is.True);
            Assert.That(weapon.Data2Visible, Is.True);
            Assert.That(weapon.Data3Visible, Is.True);
            Assert.That(weapon.Data1IsSpell, Is.False);

            var potion = Row(ItemType.PotionAddHp);
            Assert.That(potion.Data1Visible, Is.True, "potion amount is editable");
            Assert.That(potion.Data2Visible, Is.False, "potions have no Data2");
            Assert.That(potion.Data3Visible, Is.False);

            var spell = Row(ItemType.Spell);
            Assert.That(spell.Data1Visible, Is.True);
            Assert.That(spell.Data1IsSpell, Is.True, "Data1 is a spell number for scrolls");
            Assert.That(spell.Data2Visible, Is.False);

            Assert.That(Row(ItemType.Key).Data1Visible, Is.False, "keys carry no editable data");
            Assert.That(Row(ItemType.Currency).Data1Visible, Is.False);
            Assert.That(Row(ItemType.None).Data1Visible, Is.False);
        });
    }
}
