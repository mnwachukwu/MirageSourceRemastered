using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>ServerPlayer.ActiveShop resolution: a keeper-opened shop only resolves while the
/// player still stands within interact range of the keeper NPC that opened it AND that keeper still keeps that
/// shop. So an unopened shop, an unobserved map, walking out of range, a vanished keeper, or a reassigned
/// keeper all close it. This gate fronts every shop/inn/market op, so it's exercised indirectly everywhere but
/// pinned directly here (NpcInteractTests covers the ShopAssignedToNpc/KeeperShopKind half; this covers the
/// range-integrated whole).</summary>
[TestFixture]
public class ActiveShopResolutionTests
{
    const int Map = 1, ShopNum = 3, KeeperNpc = 5, KeeperSlot = 1, Index = 1;

    static (GameWorld world, ServerPlayer sp) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Shops[ShopNum].ShopType = ShopType.Inn;
        world.Shops[ShopNum].Keeper = KeeperNpc;      // shop #3 is kept by NPC #5
        var mn = world.MapNpcs[Map, KeeperSlot];
        mn.Num = KeeperNpc;
        mn.X = 5;
        mn.Y = 5;

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 5;  // standing on the keeper's tile
        world.MapObservers[Map].Add(Index);
        sp.SetActiveShop(ShopNum, Map, KeeperSlot);
        return (world, sp);
    }

    [Test]
    public void ActiveShop_KeeperInRange_ResolvesTheShop()
    {
        var (world, sp) = Setup();
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(ShopNum));
    }

    [Test]
    public void ActiveShop_NoOpenShop_ReturnsZero()
    {
        var (world, sp) = Setup();
        sp.ClearActiveShop();
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0));
    }

    [Test]
    public void ActiveShop_KeeperOutOfRange_ClosesTheShop()
    {
        var (world, sp) = Setup();
        world.MapNpcs[Map, KeeperSlot].X = 20;
        world.MapNpcs[Map, KeeperSlot].Y = 20;  // well beyond interact range
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0), "walking out of interact range closes the shop");
    }

    [Test]
    public void ActiveShop_PlayerNotObservingMap_ReturnsZero()
    {
        var (world, sp) = Setup();
        world.MapObservers[Map].Remove(Index);
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0));
    }

    [Test]
    public void ActiveShop_KeeperVanished_ReturnsZero()
    {
        var (world, sp) = Setup();
        world.MapNpcs[Map, KeeperSlot].Num = 0;   // the keeper NPC died / despawned
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0), "a vanished keeper closes the shop");
    }

    // The keeper you opened with no longer keeps THIS shop (reassigned/misconfig) — the open shop is stale.
    [Test]
    public void ActiveShop_KeeperNoLongerKeepsThisShop_ReturnsZero()
    {
        var (world, sp) = Setup();
        world.Shops[ShopNum].Keeper = 99;   // shop reassigned to a different keeper NPC
        Assert.That(sp.ActiveShop(world, Index), Is.EqualTo(0), "the keeper no longer keeps the active shop");
    }
}
