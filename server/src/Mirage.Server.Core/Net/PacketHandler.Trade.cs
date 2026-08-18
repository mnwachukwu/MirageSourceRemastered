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

/// <summary>Direct player-to-player trade: the invite, the offer table, and the two-sided confirm.</summary>
public sealed partial class PacketHandler
{
    //  Trade handlers
    // ===========================================================================

    // ── Direct trade ────────────────────────────────────────────────────────────

    private void HandleTradeInvite(int index, TradeInvitePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Target))
        {
            HackingAttempt(index, "Trade Target Modification");
            return;
        }
        _trade.Request(index, p.Target.Trim());
    }

    private void HandleTradeRespond(int index, TradeRespondPacket p) { if (_pm[index].IsPlaying) _trade.Respond(index, p.Accept); }
    private void HandleTradeOfferAdd(int index, TradeOfferAddPacket p) { if (_pm[index].IsPlaying) _trade.OfferAdd(index, p.InvSlot, p.Quantity); }
    private void HandleTradeOfferRemove(int index, TradeOfferRemovePacket p) { if (_pm[index].IsPlaying) _trade.OfferRemove(index, p.Index); }
    private void HandleTradeConfirm(int index, TradeConfirmPacket p) { if (_pm[index].IsPlaying) _trade.Confirm(index, p.Confirmed); }
    private void HandleTradeCancel(int index) { if (_pm[index].IsPlaying) _trade.Cancel(index); }

    // ===========================================================================
}
