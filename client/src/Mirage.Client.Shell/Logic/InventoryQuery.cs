using Mirage.Client.Core.State;
using Mirage.Shared;

namespace Mirage.Client.Shell.Logic;

/// <summary>Pure queries over the local player's inventory, shared by the inventory + bank panels (extracted
/// so the bulk-amount math isn't duplicated).</summary>
public static class InventoryQuery
{
    /// <summary>How many inventory slots hold item <paramref name="itemNum"/> — the max for a bulk drop/deposit
    /// prompt. When <paramref name="skipEquipped"/> is set, the four equipped slots don't count.</summary>
    public static int CountInvSlotsMatching(ClientState state, int itemNum, bool skipEquipped)
    {
        var me = state.Me;
        if (me?.Inv is null) return 0;
        int count = 0;
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var s = me.Inv[i];
            if (s is null || s.Num != itemNum) continue;
            if (skipEquipped && (me.WeaponSlot == i || me.ArmorSlot == i || me.HelmetSlot == i || me.ShieldSlot == i)) continue;
            count++;
        }
        return count;
    }
}
