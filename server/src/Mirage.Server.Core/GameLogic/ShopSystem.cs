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

    // ── Barter ────────────────────────────────────────────────────────────────

    /// <summary>Execute one row of a shop's barter table: take the row's "give" stack from the player and
    /// hand back its "get" stack. An ordinary purchase is just a row whose give side is the currency item.
    /// <para>Refuses unless the shop is open for this player, is a Store, the player holds enough, and the
    /// bag has room — checked in that order so the player gets the most specific message.</para></summary>
    public void Barter(int index, int shopNum, int barterSlot)
    {
        if (!_pm[index].IsPlaying) return;
        if (shopNum <= 0 || shopNum > _world.Limits.Shops) return;
        var p = _pm[index].Char;
        var shop = _world.Shops[shopNum];

        // barterSlot is 1-based: the client sends its display index + 1, resolved here as BarterItem[slot - 1].
        if (barterSlot < 1 || barterSlot > shop.BarterItem.Count) return;

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

        var row = shop.BarterItem[barterSlot - 1];
        // Not a real barter unless both sides carry an item AND a positive quantity. A zero give quantity
        // slips past the HasItem check below (HasItem(...) >= 0 is always true) and a zero get quantity
        // still mints the item, so a misconfigured slot would hand out free items. Reject outright. The
        // editor and the shop-save handler both keep quantities >= 1, so this only guards legacy/bad data.
        if (row.GiveItem <= 0 || row.GetItem <= 0 || row.GiveQuantity <= 0 || row.GetQuantity <= 0) return;

        if (ItemSystem.HasItem(p, _world.Items, row.GiveItem) < row.GiveQuantity)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotEnoughTrade, GameColor.BrightRed);
            return;
        }

        if (ItemSystem.FindOpenInvSlot(p, _world.Items, row.GetItem) == 0)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return;
        }

        _items.TakeItem(index, row.GiveItem, row.GiveQuantity);
        _items.GiveItem(index, row.GetItem, row.GetQuantity);
        SendMsg(index, ServerStrings.ShopSystem_TradedWith, GameColor.Yellow, ("ShopName", shop.TrimmedName));
    }

    // ── Buy / sell ────────────────────────────────────────────────────────────
    // The gold storefront, as opposed to Barter's item-for-item table. A sales entry is just an item number:
    // the price comes from ItemRecord.Price, which is what let a shopfront be authored by picking
    // items instead of hand-writing a give→get row each (see ShopRecord.SalesItem).

    /// <summary>Buy one unit of a sales-list entry for its <see cref="ItemRecord.Price"/>.
    /// <para>Refuses unless the shop is open for this player, is a Store, the entry is priced, the purse
    /// covers it, and the bag has room — in that order, so the player gets the most specific message.</para></summary>
    public void Buy(int index, int shopNum, int salesSlot)
    {
        if (!_pm[index].IsPlaying) return;
        if (shopNum <= 0 || shopNum > _world.Limits.Shops) return;
        var p = _pm[index].Char;
        var shop = _world.Shops[shopNum];

        // 1-based on the wire, like BarterSlot: the client sends its display index + 1.
        if (salesSlot < 1 || salesSlot > shop.SalesItem.Count) return;

        if (_pm[index].ActiveShop(_world, index) != shopNum || shop.ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotAtShop, GameColor.BrightRed);
            return;
        }

        int itemNum = shop.SalesItem[salesSlot - 1];
        if (itemNum <= 0 || itemNum > _world.Limits.Items) return;
        var item = _world.Items[itemNum];

        // An unpriced entry is a data bug, and handing it over free is the same class of bug as the
        // zero-quantity barter row that used to mint items. Refuse rather than give it away.
        int price = item.Price;
        if (price <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotForSale, GameColor.BrightRed, ("ItemName", item.TrimmedName));
            return;
        }

        if (ItemSystem.HasItem(p, _world.Items, Constants.GoldItemIndex) < price)
        {
            SendMsg(index, ServerStrings.ShopSystem_InsufficientGold, GameColor.BrightRed);
            return;
        }

        if (ItemSystem.FindOpenInvSlot(p, _world.Items, itemNum) == 0)
        {
            SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed);
            return;
        }

        _items.TakeItem(index, Constants.GoldItemIndex, price);
        _items.GiveItem(index, itemNum, 1);
        SendMsg(index, ServerStrings.ShopSystem_Bought, GameColor.Yellow,
            ("ItemName", item.TrimmedName), ("Gold", price));
    }

    /// <summary>Sell one inventory slot to the open shop for
    /// <see cref="EconomyFormulas.ItemSellValue"/> — a quarter of the item's value, scaled by condition.
    ///
    /// <para><b>A zero-gold sale still goes through.</b> A broken piece, or anything the pricing model
    /// values at nothing, is bought for nothing rather than refused: the vendor doubles as the way to
    /// empty a bag, and a slot you cannot clear is worse than a slot that clears for free. What is
    /// refused is <see cref="ItemRecord.NonJunkable"/> — gold, valor and treasure — because those either
    /// are the currency or are meant to reach a specific buyer through the barter table.</para></summary>
    public void Sell(int index, int invSlot, int quantity)
    {
        if (!_pm[index].IsPlaying) return;
        if (!SlotValidation.IsValidInvSlot(invSlot)) return;

        var p = _pm[index].Char;
        int shopNum = _pm[index].ActiveShop(_world, index);
        if (shopNum <= 0 || _world.Shops[shopNum].ShopType != ShopType.Store)
        {
            SendMsg(index, ServerStrings.ShopSystem_NotAtShop, GameColor.BrightRed);
            return;
        }

        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0)
        {
            SendMsg(index, ServerStrings.ShopSystem_NoItemInSlot, GameColor.BrightRed);
            return;
        }

        var item = _world.Items[itemNum];
        if (item.NonJunkable)
        {
            SendMsg(index, ServerStrings.ShopSystem_CannotSell, GameColor.BrightRed, ("ItemName", item.TrimmedName));
            return;
        }

        // Selling gear off your own back would silently unequip it; make the player take it off first,
        // the same rule the bank deposit uses.
        if (p.WeaponSlot == invSlot || p.ArmorSlot == invSlot || p.HelmetSlot == invSlot || p.ShieldSlot == invSlot)
        {
            SendMsg(index, ServerStrings.ShopSystem_UnequipFirst, GameColor.BrightRed, ("ItemName", item.TrimmedName));
            return;
        }

        // Currency-style stacks sell by amount (0 or an oversized ask means the whole stack, matching
        // RemoveFromSlot); everything else is one indivisible piece and carries its own durability.
        bool stacks = item.Type == ItemType.Currency;
        int have = stacks ? Math.Max(p.Inv[invSlot].Quantity, 1) : 1;
        int amount = stacks ? (quantity <= 0 || quantity > have ? have : quantity) : 1;

        var spell = item.Type == ItemType.Spell && item.SpellNum > 0 && item.SpellNum <= _world.Limits.Spells
            ? _world.Spells[item.SpellNum] : null;
        long gold = (long)EconomyFormulas.ItemSellValue(item, p.Inv[invSlot].Dur, spell) * amount;

        _items.TakeItem(index, itemNum, amount);
        if (gold > 0) _items.GiveItem(index, Constants.GoldItemIndex, (int)Math.Min(gold, int.MaxValue));

        if (gold > 0)
            SendMsg(index, ServerStrings.ShopSystem_Sold, GameColor.Yellow,
                ("ItemName", item.TrimmedName), ("Gold", gold));
        else
            SendMsg(index, ServerStrings.ShopSystem_SoldForNothing, GameColor.Gray, ("ItemName", item.TrimmedName));
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
        int goldNeeded = EconomyFormulas.RepairCost(durNeeded, item);

        long playerGold = ItemSystem.HasItem(p, _world.Items, Constants.GoldItemIndex);

        // How many points the purse actually covers. Asked exactly rather than as gold/ratePerPoint —
        // the rate is a floored display figure, and dividing by it can name a point count that costs a
        // gold more than the player has.
        int affordable = Math.Min(EconomyFormulas.RepairPointsAffordable(playerGold, item), durNeeded);
        if (affordable <= 0)
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
                Quantity = p.Inv[invSlot].Quantity,
                Dur = p.Inv[invSlot].Dur
            });
            SendMsg(index, ServerStrings.ShopSystem_FullyRestored, GameColor.BrightBlue, ("Gold", goldNeeded));
        }
        else
        {
            // Partial repair: restore as many durability points as the player can afford
            int durPartial = affordable;
            int goldActual = EconomyFormulas.RepairCost(durPartial, item);
            _items.TakeItem(index, Constants.GoldItemIndex, goldActual);
            p.Inv[invSlot].Dur += durPartial;
            _dispatcher.SendTo(index, new InventoryUpdatePacket
            {
                Slot = invSlot,
                Num = p.Inv[invSlot].Num,
                Quantity = p.Inv[invSlot].Quantity,
                Dur = p.Inv[invSlot].Dur
            });
            SendMsg(index, ServerStrings.ShopSystem_PartiallyFixed, GameColor.BrightBlue, ("Gold", goldActual));
        }
    }
}
