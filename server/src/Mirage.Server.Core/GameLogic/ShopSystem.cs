using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Shop and inn interactions: the map-enter/leave greeting, item-for-item trades, and repair.
/// <para>Every entry point re-checks that the shop is actually open for the player via
/// <see cref="ServerPlayer.ActiveShop"/> — a shop is reachable only while standing by its keeper, so a
/// stale client-side shop number can't be used to trade from across the map.</para></summary>
public sealed class ShopSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;

    public ShopSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ItemSystem items)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _items = items;
    }

    // ── Map-enter/leave greeting (called by movement + join systems) ──────────
    // The greeting lives on the MAP (+ MapGroup) rather than the shop, since shops are not map-bound: it
    // speaks the map's own JoinSay/LeaveSay, said by its GreetingSpeaker. Blank fields stay silent, and
    // there is no generic "you walk into a store/inn" line (a map can't know store-vs-inn).

    /// <summary>Speak the map's join greeting, if it has one. Silent when the resolved line is blank.</summary>
    public void OnJoinMap(int index)
    {
        var g = _world.GreetingOf(_pm[index].Char.Map);
        if (!string.IsNullOrWhiteSpace(g.JoinSay))
            SendMsg(index, ServerStrings.ShopSystem_JoinSay, GameColor.Npc, ("ShopName", g.Speaker.TrimEnd()), ("JoinSay", g.JoinSay.TrimEnd()));
    }

    /// <summary>Speak the map's leave greeting, if it has one.</summary>
    public void OnLeaveMap(int index)
    {
        var g = _world.GreetingOf(_pm[index].Char.Map);
        if (!string.IsNullOrWhiteSpace(g.LeaveSay))
            SendMsg(index, ServerStrings.ShopSystem_LeaveSay, GameColor.Npc, ("ShopName", g.Speaker.TrimEnd()), ("LeaveSay", g.LeaveSay.TrimEnd()));
    }

    // ── Trade ─────────────────────────────────────────────────────────────────

    /// <summary>Execute one row of a shop's trade list: take the row's "give" stack from the player and
    /// hand back its "get" stack. An ordinary purchase is just a row whose give side is the currency item.
    /// <para>Refuses unless the shop is open for this player, is a Store, the player holds enough, and the
    /// bag has room — checked in that order so the player gets the most specific message.</para></summary>
    public void Trade(int index, int shopNum, int tradeSlot)
    {
        if (!_pm[index].IsPlaying) return;
        if (shopNum <= 0 || shopNum > Constants.MaxShops) return;
        var p = _pm[index].Char;
        var shop = _world.Shops[shopNum];

        // tradeSlot is 1-based: the client sends its display index + 1, resolved here as TradeItem[slot - 1].
        if (tradeSlot < 1 || tradeSlot > shop.TradeItem.Count) return;

        if (_pm[index].ActiveShop(_world, index) != shopNum)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotAtShop, GameColor.BrightRed);
            return;
        }

        if (shop.ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotAtShop, GameColor.BrightRed);
            return;
        }

        var trade = shop.TradeItem[tradeSlot - 1];
        // Not a real trade unless both sides carry an item AND a positive quantity. A zero give quantity
        // slips past the HasItem check below (HasItem(...) >= 0 is always true) and a zero get quantity
        // still mints the item, so a misconfigured slot would hand out free items. Reject outright. The
        // editor and the shop-save handler both keep quantities >= 1, so this only guards legacy/bad data.
        if (trade.GiveItem <= 0 || trade.GetItem <= 0 || trade.GiveValue <= 0 || trade.GetValue <= 0) return;

        if (ItemSystem.HasItem(p, _world.Items, trade.GiveItem) < trade.GiveValue)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotEnoughTrade, GameColor.BrightRed);
            return;
        }

        if (ItemSystem.FindOpenInvSlot(p, _world.Items, trade.GetItem) == 0)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return;
        }

        _items.TakeItem(index, trade.GiveItem, trade.GiveValue);
        _items.GiveItem(index, trade.GetItem, trade.GetValue);
        SendMsg(index, ServerStrings.ShopSystem_TradedWith, GameColor.Yellow, ("ShopName", shop.TrimmedName));
    }

    // ── Fix item ──────────────────────────────────────────────────────────────

    /// <summary>Repair one inventory slot at a repair-capable Store, charging by the durability points
    /// actually restored.
    /// <para>A player who can't afford a full repair gets a PARTIAL one — as many points as their gold
    /// covers — rather than a refusal; only being unable to afford a single point is rejected.</para></summary>
    public void FixItem(int index, int invSlot)
    {
        if (!_pm[index].IsPlaying) return;
        if (!SlotValidation.IsValidInvSlot(invSlot)) return;

        var p = _pm[index].Char;
        int shopNum = _pm[index].ActiveShop(_world, index);

        if (shopNum <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoRepairShop, GameColor.BrightRed);
            return;
        }

        var shop = _world.Shops[shopNum];
        if (shop.ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoRepairShop, GameColor.BrightRed);
            return;
        }

        if (!shop.FixesItems)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoRepairType, GameColor.BrightRed);
            return;
        }

        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoItemInSlot, GameColor.BrightRed);
            return;
        }

        var item = _world.Items[itemNum];
        if (item.Type is not (ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield))
        {
            SendMsg(index, ServerStrings.ShopSystem_CannotRepair, GameColor.BrightRed);
            return;
        }

        int durNeeded = item.Durability - p.Inv[invSlot].Dur;
        if (durNeeded <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_PerfectCond, GameColor.White);
            return;
        }

        // Cost per durability point + total for a full repair — the shared repair formula (also used by the
        // guild-war vault-repair sink, so both price durability the same way).
        int ratePerPoint = EconomyFormulas.RepairRatePerPoint(item.Power);
        int goldNeeded = EconomyFormulas.RepairCost(durNeeded, item.Power);

        long playerGold = ItemSystem.HasItem(p, _world.Items, Constants.GoldItemIndex);

        if (playerGold < ratePerPoint)
        {
            SendMsg(index, ServerStrings.ShopSystem_InsufficientGold, GameColor.BrightRed);
            return;
        }

        if (playerGold >= goldNeeded)
        {
            _items.TakeItem(index, Constants.GoldItemIndex, goldNeeded);
            p.Inv[invSlot].Dur = item.Durability;
            _dispatcher.SendTo(index, new InventoryUpdatePacket
            {
                Slot = invSlot,
                Num = p.Inv[invSlot].Num,
                Value = p.Inv[invSlot].Value,
                Dur = p.Inv[invSlot].Dur
            });
            SendMsg(index, ServerStrings.ShopSystem_FullyRestored, GameColor.BrightBlue, ("Gold", goldNeeded));
        }
        else
        {
            // Partial repair: restore as many durability points as the player can afford
            int durPartial = (int)(playerGold / ratePerPoint);
            int goldActual = EconomyFormulas.RepairCost(durPartial, item.Power);
            _items.TakeItem(index, Constants.GoldItemIndex, goldActual);
            p.Inv[invSlot].Dur += durPartial;
            _dispatcher.SendTo(index, new InventoryUpdatePacket
            {
                Slot = invSlot,
                Num = p.Inv[invSlot].Num,
                Value = p.Inv[invSlot].Value,
                Dur = p.Inv[invSlot].Dur
            });
            SendMsg(index, ServerStrings.ShopSystem_PartiallyFixed, GameColor.BrightBlue, ("Gold", goldActual));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
}
