using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests;

/// <summary>Trade-slot quantity coercion — the authoritative editor-side backstop that keeps a shop trade
/// well-formed: an empty side carries 0, a non-currency item pins to exactly 1 (gear never stacks), and a
/// currency item allows 1..9999. The spinner Min/Max bounds track the item the same way.</summary>
[TestFixture]
public class TradeRowViewModelTests
{
    const int Gold = 1;    // the test predicate treats this id as currency
    const int Sword = 2;   // non-currency

    static TradeRowViewModel Trade(TradeItemRecord? r = null) =>
        new(1, r ?? new TradeItemRecord(), () => [], id => id == Gold);

    [Test]
    public void NonCurrencyGive_PinsQuantityToOne()
    {
        var t = Trade();
        t.GiveItem = Sword;
        t.GiveValue = 5;      // a sword never stacks
        Assert.That(t.GiveValue, Is.EqualTo(1));
    }

    [Test]
    public void CurrencyGive_AllowsQuantityAboveOne()
    {
        var t = Trade();
        t.GiveItem = Gold;
        t.GiveValue = 100;
        Assert.That(t.GiveValue, Is.EqualTo(100));
    }

    [Test]
    public void ClearingTheItem_ZeroesQuantity()
    {
        var t = Trade(new TradeItemRecord { GiveItem = Gold, GiveValue = 50 });
        t.GiveItem = 0;
        Assert.That(t.GiveValue, Is.EqualTo(0), "an empty side carries no quantity");
    }

    // Coercion applies symmetrically to the "get" side.
    [Test]
    public void GetSide_CoercesToo()
    {
        var t = Trade();
        t.GetItem = Sword;
        t.GetValue = 9;
        Assert.That(t.GetValue, Is.EqualTo(1));
    }

    [Test]
    public void ValueBounds_TrackTheItem()
    {
        var t = Trade();
        Assert.Multiple(() =>
        {
            Assert.That(t.GiveValueMin, Is.EqualTo(0), "empty side: 0..0");
            Assert.That(t.GiveValueMax, Is.EqualTo(0));
        });

        t.GiveItem = Sword;
        Assert.Multiple(() =>
        {
            Assert.That(t.GiveValueMin, Is.EqualTo(1), "non-currency: exactly 1");
            Assert.That(t.GiveValueMax, Is.EqualTo(1));
        });

        t.GiveItem = Gold;
        Assert.Multiple(() =>
        {
            Assert.That(t.GiveValueMin, Is.EqualTo(1));
            Assert.That(t.GiveValueMax, Is.EqualTo(9999), "currency: up to 9999");
        });
    }

    // The ctor seeds raw fields (no coercion), so a well-formed record round-trips untouched.
    [Test]
    public void ToRecord_RoundTripsAllFourFields()
    {
        var t = Trade(new TradeItemRecord { GiveItem = Gold, GiveValue = 100, GetItem = Sword, GetValue = 1 });
        var r = t.ToRecord();
        Assert.That((r.GiveItem, r.GiveValue, r.GetItem, r.GetValue), Is.EqualTo((Gold, 100, Sword, 1)));
    }
}
