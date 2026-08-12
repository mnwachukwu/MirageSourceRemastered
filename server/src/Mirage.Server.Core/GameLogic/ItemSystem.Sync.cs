using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Pushing inventory state to the client, and the potion vital helpers that broadcast a
/// change the moment it lands.</summary>
public sealed partial class ItemSystem : GameSystem
{
    // ── Send helpers ──────────────────────────────────────────────────────────

    /// <summary>Push one inventory slot's current contents to its owner.</summary>
    public void SendInventoryUpdate(int index, int slot)
    {
        var p = _pm[index].Char;
        _dispatcher.SendTo(index, new InventoryUpdatePacket
        {
            Slot = slot,
            Num = p.Inv[slot].Num,
            Value = p.Inv[slot].Value,
            Dur = p.Inv[slot].Dur
        });
    }

    private void SendEquippedGear(int index)
    {
        var p = _pm[index].Char;
        SendToMap(_world, p.Map, new EquippedGearPacket
        {
            Index = index,
            Armor = p.ArmorSlot,
            Weapon = p.WeaponSlot,
            Helmet = p.HelmetSlot,
            Shield = p.ShieldSlot
        });
    }

    // SendMsg / ViewportMsg come from GameSystem. ViewportMsg is earshot-scoped, like a "say" — the
    // world state that goes with these (the dropped-item spawn, the MapKey door toggle) is sent
    // separately to observers because the whole region renders it.

    // ── Potion helpers ───────────────────────────────────────────────────────
    // Shared implementation for the six PotionAdd*/PotionSub* item types: one Add helper, one Sub
    // helper, and per-vital read/write/broadcast plumbing.

    private enum PotionVital { Hp, Mp, Sp }

    /// <summary>PotionAdd{Hp,Mp,Sp}: restore <see cref="ItemRecord.VitalAmount"/> of one vital,
    /// clamped to its max. Refuses (with a chat message) if the vital is already at max.</summary>
    private void ApplyAddPotion(int index, PlayerRecord p, ItemRecord item, int itemNum, PotionVital vital)
    {
        if (GetVital(p, vital) >= GetVitalMax(p, vital))
        {
            SendMsg(index, ServerStrings.ItemSystem_VitalFull, GameColor.BrightRed, ("Vital", VitalName(vital)));
            return;
        }
        SetVital(p, vital, Math.Min(GetVital(p, vital) + item.VitalAmount, GetVitalMax(p, vital)));
        BroadcastVital(index, p, vital);
        TakeItem(index, itemNum, 0);
        SendMsg(index, ServerStrings.ItemSystem_UsedPotion, GameColor.White, ("Item", item.Name));
    }

    /// <summary>PotionSub{Hp,Mp,Sp}: drain <see cref="ItemRecord.VitalAmount"/> from one vital
    /// (floored at 0), and gain half that amount on each of the OTHER two vitals (clamped
    /// to their maxes). Refuses (with a chat message) if the drained vital is already 0.
    /// All three vital updates are broadcast even when only one moved meaningfully.</summary>
    private void ApplySubPotion(int index, PlayerRecord p, ItemRecord item, int itemNum, PotionVital drainVital)
    {
        if (GetVital(p, drainVital) <= 0)
        {
            SendMsg(index, ServerStrings.ItemSystem_CantUsePotion, GameColor.BrightRed);
            return;
        }
        int gain = item.VitalAmount / 2;
        SetVital(p, drainVital, Math.Max(GetVital(p, drainVital) - item.VitalAmount, 0));
        foreach (var v in new[] { PotionVital.Hp, PotionVital.Mp, PotionVital.Sp })
        {
            if (v != drainVital)
                SetVital(p, v, Math.Min(GetVital(p, v) + gain, GetVitalMax(p, v)));
        }

        BroadcastVital(index, p, PotionVital.Hp);
        BroadcastVital(index, p, PotionVital.Mp);
        BroadcastVital(index, p, PotionVital.Sp);
        TakeItem(index, itemNum, 0);
    }

    private static int GetVital(PlayerRecord p, PotionVital v) => v switch
    {
        PotionVital.Hp => p.Hp,
        PotionVital.Mp => p.Mp,
        _ => p.Sp,
    };

    private static int GetVitalMax(PlayerRecord p, PotionVital v) => v switch
    {
        PotionVital.Hp => p.MaxHp,
        PotionVital.Mp => p.MaxMp,
        _ => p.MaxSp,
    };

    private static void SetVital(PlayerRecord p, PotionVital v, int value)
    {
        switch (v)
        {
            case PotionVital.Hp:
                p.Hp = value;
                break;
            case PotionVital.Mp:
                p.Mp = value;
                break;
            case PotionVital.Sp:
                p.Sp = value;
                break;
        }
    }

    private static string VitalName(PotionVital v) => v switch
    {
        PotionVital.Hp => "HP",
        PotionVital.Mp => "MP",
        _ => "SP",
    };

    private void BroadcastVital(int index, PlayerRecord p, PotionVital v)
    {
        var observers = _world.MapObservers[p.Map];
        switch (v)
        {
            case PotionVital.Hp:
                _dispatcher.SendToObservers(observers, PacketBuilder.SendHp(index, p.Hp, p.MaxHp, showFloat: true));
                break;
            case PotionVital.Mp:
                _dispatcher.SendToObservers(observers, PacketBuilder.SendMp(index, p.Mp, p.MaxMp, showFloat: true));
                break;
            case PotionVital.Sp:
                _dispatcher.SendToObservers(observers, PacketBuilder.SendSp(index, p.Sp, p.MaxSp, showFloat: true));
                break;
        }
    }
}
