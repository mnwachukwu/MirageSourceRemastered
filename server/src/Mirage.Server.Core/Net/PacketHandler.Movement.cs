using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>Movement and facing packets — the two highest-frequency messages the server receives.</summary>
public sealed partial class PacketHandler
{
    //  Movement handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandlePlayerMove(int index, PlayerMovePacket p)
    {
        if (!_pm[index].IsPlaying || _pm[index].GettingMap) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't move (client freezes this; server enforces)
        if (p.Dir > Direction.Right)
        {
            HackingAttempt(index, "Invalid Direction");
            return;
        }
        if (p.Movement < MovementType.Walking || p.Movement > MovementType.Running)
        {
            HackingAttempt(index, "Invalid Movement");
            return;
        }

        // Casting does not block movement, so a move packet is always honored regardless of a recent cast.
        _movement.PlayerMove(index, p.Dir, p.Movement);
    }

    private void HandlePlayerDir(int index, PlayerDirPacket p)
    {
        if (!_pm[index].IsPlaying || _pm[index].GettingMap) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't turn
        if (p.Dir > Direction.Right)
        {
            HackingAttempt(index, "Invalid Direction");
            return;
        }
        _movement.PlayerDir(index, p.Dir);
    }

    // ═══════════════════════════════════════════════════════════════════════════
}
