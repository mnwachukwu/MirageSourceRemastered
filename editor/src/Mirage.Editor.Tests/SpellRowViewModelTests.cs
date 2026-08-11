using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests;

/// <summary>The spell-editor row: record round-trip, the dirty lifecycle + wire-load guard (ApplyPacket must
/// not read as an edit), and the type-driven flags — Data1 is an item number only for GiveItem spells, and
/// the per-cast reagent cost is shown only for the SubHp "weapon" spell.</summary>
[TestFixture]
public class SpellRowViewModelTests
{
    static SpellRecord Fireball() => new()
    {
        Name = "Fireball", ClassReq = 2, Type = SpellType.SubHp, Data1 = 40, Data2 = 0, Data3 = 0,
    };

    [Test]
    public void Ctor_FromRecord_RoundTrips_AndIsClean()
    {
        var vm = new SpellRowViewModel(4, Fireball());
        var r = vm.ToRecord();
        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("Fireball"));
            Assert.That(r.ClassReq, Is.EqualTo(2));
            Assert.That(r.Type, Is.EqualTo(SpellType.SubHp));
            Assert.That(r.Data1, Is.EqualTo((short)40));
            Assert.That(vm.IsDirty, Is.False);
            Assert.That(vm.IsLoaded, Is.True);
        });
    }

    [Test]
    public void EditingAField_MarksDirty_ClearResets()
    {
        var vm = new SpellRowViewModel(4, Fireball());
        vm.Data1 = 60;
        Assert.That(vm.IsDirty, Is.True);
        vm.ClearDirty();
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public void ApplyPacket_SeedsFields_MarksLoaded_ButNotDirty()
    {
        var vm = new SpellRowViewModel(1, new SpellRecord { Name = "" }, isLoaded: false);
        vm.ApplyPacket(new UpdateSpellPacket { Name = "Heal", Type = SpellType.AddHp, Data1 = 30 });
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsDirty, Is.False, "a wire load is not an edit");
            Assert.That(vm.IsLoaded, Is.True);
            Assert.That(vm.ToRecord().Name, Is.EqualTo("Heal"));
            Assert.That(vm.ToRecord().Type, Is.EqualTo(SpellType.AddHp));
        });
    }

    [Test]
    public void Data1IsGiveItem_OnlyForGiveItemSpells()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Row(SpellType.GiveItem).Data1IsGiveItem, Is.True);
            Assert.That(Row(SpellType.SubHp).Data1IsGiveItem, Is.False);
            Assert.That(Row(SpellType.AddHp).Data1IsGiveItem, Is.False);
        });
    }

    // The reagent-per-cast line is the SubHp spell's real cost; other spell types pay MP only.
    [Test]
    public void ShowReagentCost_OnlyForSubHp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Row(SpellType.SubHp).ShowReagentCost, Is.True);
            Assert.That(Row(SpellType.SubHp, data1: 40).ReagentCost, Is.GreaterThan(0), "a SubHp spell costs reagents");
            Assert.That(Row(SpellType.AddHp).ShowReagentCost, Is.False);
            Assert.That(Row(SpellType.AddMp).ReagentCost, Is.EqualTo(0));
        });
    }

    static SpellRowViewModel Row(SpellType type, short data1 = 10) =>
        new(1, new SpellRecord { Type = type, Data1 = data1 });
}
