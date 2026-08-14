using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>Store trade + item repair. Trade swaps GiveItem*GiveQuantity for GetItem*GetQuantity with the gates
/// (at the shop, a Store, well-formed slot, enough to give, room to receive); FixItem prices durability off
/// the shared repair formula and does a full repair when affordable, a best-effort partial when not, and
/// refuses below one point's cost.</summary>
[TestFixture]
public class ShopSystemTests
{
    const int Map = 1, ShopNum = 1, Idx = 1;
    const int Gold = Constants.GoldItemIndex;
    const int Sword = 10, Potion = 16;

    const int KeeperNpc = 1, KeeperSlot = 1;

    static (GameWorld world, ShopSystem shop, PlayerRecord p) Setup(ShopType type = ShopType.Store)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var shop = new ShopSystem(world, pm, dispatcher, items);

        world.Shops[ShopNum].ShopType = type;
        world.Items[Gold].Type = ItemType.Currency;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        // Shops are keeper-based now: place the shop's keeper NPC on the player's tile and open
        // it, so ActiveShop resolves (in-range + observed + keeper matches) exactly as a real interact would.
        OpenKeeperShop(world, sp, Idx);
        return (world, shop, sp.Char);
    }

    // Wire up the active shop the way OpenNpcShop does: assign the keeper, drop it on the player's tile, mark the
    // player an observer, and record the active-shop context. Every op then re-validates r=5 of this keeper.
    static void OpenKeeperShop(GameWorld world, ServerPlayer sp, int idx)
    {
        world.Shops[ShopNum].Keeper = KeeperNpc;
        var mn = world.MapNpcs[sp.Char.Map, KeeperSlot];
        mn.Num = KeeperNpc;
        mn.X = sp.Char.X;
        mn.Y = sp.Char.Y;
        world.MapObservers[sp.Char.Map].Add(idx);
        sp.SetActiveShop(ShopNum, sp.Char.Map, KeeperSlot);
    }

    // ── Trade ────────────────────────────────────────────────────────────────────

    // Trades are a dense 0-based list now; slot 1 (what the tests trade against) is TradeItem[0].
    static void SetTrade(GameWorld world, int giveItem, int giveVal, int getItem, int getVal)
    {
        var trades = world.Shops[ShopNum].TradeItem;
        trades.Clear();
        trades.Add(new TradeItemRecord { GiveItem = giveItem, GiveQuantity = giveVal, GetItem = getItem, GetQuantity = getVal });
    }

    [Test]
    public void Trade_Success_SwapsGiveForGet()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 100, Sword, 1);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Trade(Idx, ShopNum, tradeSlot: 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(0), "the price is taken");
            Assert.That(ItemSystem.HasItem(p, world.Items, Sword), Is.EqualTo(1), "the traded item is received");
        });
    }

    [Test]
    public void Trade_NotEnoughToGive_Refused()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 100, Sword, 1);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 50;  // only 50 of the 100 needed

        shop.Trade(Idx, ShopNum, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(50), "nothing is taken");
            Assert.That(ItemSystem.HasItem(p, world.Items, Sword), Is.EqualTo(0), "nothing is received");
        });
    }

    [Test]
    public void Trade_AtAnInn_Refused()
    {
        var (world, shop, p) = Setup(ShopType.Inn);   // not a Store
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 100, Sword, 1);
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Trade(Idx, ShopNum, 1);

        Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(100), "an Inn is not a trading store");
    }

    // A misconfigured slot (zero quantity) must not mint a free item — the explicit guard.
    [Test]
    public void Trade_ZeroQuantitySlot_Refused()
    {
        var (world, shop, p) = Setup();
        world.Items[Sword].Type = ItemType.Weapon;
        SetTrade(world, Gold, 0, Sword, 1);   // GiveQuantity 0
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.Trade(Idx, ShopNum, 1);

        Assert.That(ItemSystem.HasItem(p, world.Items, Sword), Is.EqualTo(0), "a zero-quantity trade hands out nothing");
    }

    // ── FixItem ──────────────────────────────────────────────────────────────────

    // A mid-band sword: max durability 100 at its tier's medium Power. Repair is now a share of the item's
    // VALUE (EconomyFormulas.RepairCost), so these tests take their expected gold FROM the formula rather
    // than restating its arithmetic — what is under test here is the shop's behavior (does it charge
    // exactly the quoted cost, restore exactly what was bought, refuse when a single point is unaffordable),
    // not the price curve, which EconomyFormulasTests pins separately. A tier-100 piece is used so the
    // numbers are large enough that the partial-repair division is actually exercised.
    const short RepairTier = 100;

    static ItemRecord SwordDef(GameWorld world) => world.Items[Sword];

    static void PlaceRepairableSword(GameWorld world, PlayerRecord p, int currentDur, int gold)
    {
        world.Shops[ShopNum].FixesItems = true;
        world.Items[Sword].Type = ItemType.Weapon;
        world.Items[Sword].Durability = 100;
        world.Items[Sword].LevelReq = RepairTier;
        world.Items[Sword].Power = (short)EconomyFormulas.ReferencePower(RepairTier);
        p.Inv[2].Num = Sword;
        p.Inv[2].Dur = currentDur;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = gold;
    }

    [Test]
    public void FixItem_Affordable_FullyRepairs()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 40, gold: 0);
        int cost = EconomyFormulas.RepairCost(60, SwordDef(world));
        int purse = cost * 2;
        p.Inv[1].Quantity = purse;

        shop.FixItem(Idx, invSlot: 2);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[2].Dur, Is.EqualTo(100), "restored to max durability");
            Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(purse - cost),
                "exactly the quoted full-repair cost is deducted");
        });
    }

    // Can't afford a full repair: buy as many points as the purse covers, at the quoted per-point rate.
    [Test]
    public void FixItem_Unaffordable_PartiallyRepairs()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 40, gold: 0);
        var def = SwordDef(world);
        int purse = EconomyFormulas.RepairCost(60, def) / 2;   // half of what a full repair costs
        p.Inv[1].Quantity = purse;

        int expectedPoints = EconomyFormulas.RepairPointsAffordable(purse, def);
        int expectedCost = EconomyFormulas.RepairCost(expectedPoints, def);

        shop.FixItem(Idx, invSlot: 2);

        Assert.Multiple(() =>
        {
            Assert.That(expectedPoints, Is.GreaterThan(0).And.LessThan(60), "the case must actually be partial");
            Assert.That(p.Inv[2].Dur, Is.EqualTo(40 + expectedPoints), "restores the points the gold covers");
            Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(purse - expectedCost),
                "spends what those points cost");
            Assert.That(expectedCost, Is.LessThanOrEqualTo(purse), "a partial repair never costs more than the purse");
        });
    }

    // Below the cost of even one durability point, the repair is refused outright.
    [Test]
    public void FixItem_BelowOnePointCost_Refused()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 40, gold: 0);
        int broke = EconomyFormulas.RepairRatePerPoint(SwordDef(world)) - 1;
        Assume.That(broke, Is.GreaterThan(0), "the per-point rate must exceed 1 for this case to exist");
        p.Inv[1].Quantity = broke;

        shop.FixItem(Idx, invSlot: 2);

        Assert.Multiple(() =>
        {
            Assert.That(p.Inv[2].Dur, Is.EqualTo(40), "no repair");
            Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(broke), "no gold spent");
        });
    }

    [Test]
    public void FixItem_AlreadyPerfect_NoOp()
    {
        var (world, shop, p) = Setup();
        PlaceRepairableSword(world, p, currentDur: 100, gold: 100);   // already at max

        shop.FixItem(Idx, invSlot: 2);

        Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(100), "a pristine item costs nothing");
    }

    [Test]
    public void FixItem_NonRepairableType_Refused()
    {
        var (world, shop, p) = Setup();
        world.Shops[ShopNum].FixesItems = true;
        world.Items[Potion].Type = ItemType.PotionAddHp;
        p.Inv[2].Num = Potion;
        p.Inv[1].Num = Gold;
        p.Inv[1].Quantity = 100;

        shop.FixItem(Idx, invSlot: 2);

        Assert.That(ItemSystem.HasItem(p, world.Items, Gold), Is.EqualTo(100), "a potion can't be repaired");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
