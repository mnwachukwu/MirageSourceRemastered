using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>The per-recipient hit lines. Each is resolved in the recipient's own locale, so the
/// attacker and the victim read the same exchange in their own language.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // ForPlayer so it lands in the recipient's locale alongside the outer template.
    private void SendYouHitMsg(int index, string target, string weapName, int damage, int color, bool killing = false)
    {
        string suffix = killing ? ServerStrings.ForPlayer(index, ServerStrings.CombatSystem_KillSuffix) : "";
        if (weapName.Length > 0)
        {
            SendMsg(index, ServerStrings.CombatSystem_YouHitWithWeapon, color,
                ("Target", target), ("Weapon", weapName), ("Damage", damage), ("Suffix", suffix));
        }
        else
        {
            SendMsg(index, ServerStrings.CombatSystem_YouHit, color,
                ("Target", target), ("Damage", damage), ("Suffix", suffix));
        }
    }

    private void SendTheyHitYouMsg(int index, string attacker, string weapName, int damage, int color)
    {
        if (weapName.Length > 0)
        {
            SendMsg(index, ServerStrings.CombatSystem_TheyHitYouWithWeapon, color,
                ("Attacker", attacker), ("Weapon", weapName), ("Damage", damage));
        }
        else
        {
            SendMsg(index, ServerStrings.CombatSystem_TheyHitYou, color,
                ("Attacker", attacker), ("Damage", damage));
        }
    }
}
