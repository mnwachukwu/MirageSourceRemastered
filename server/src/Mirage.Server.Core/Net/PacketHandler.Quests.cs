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

/// <summary>Player quests. Accept and turn-in are gated by the interaction layer (the player must be at the giver/turn-in NPC within r=5); abandon comes from the quest log, so it stays proximity-free.</summary>
public sealed partial class PacketHandler
{
    //  Quest handlers
    // ===========================================================================

    // ── Player quests ─────────────────────────────────────────────────────────
    // Accept/turn-in are gated by the interaction layer: the player must be at the quest's
    // giver (accept) / turn-in (turn-in) NPC and within r=5. TryResolveInteractNpc is the authoritative proximity
    // + visibility backstop; then we re-check the NPC's role. QuestSystem still owns eligibility/rewards. Abandon
    // is driven from the quest-log panel (no NPC), so it stays proximity-free.
    private void HandleQuestAccept(int index, QuestAcceptPacket p)
    {
        if (!_pm[index].IsPlaying || !SlotValidation.IsValidQuestNum(p.QuestNum, _world.Limits.Quests)) return;
        if (!TryResolveInteractNpc(index, p.MapNum, p.NpcSlot, out int npcNum)) return;
        if (_world.Quests[p.QuestNum].GiverNpc != npcNum) return;   // accepting is only allowed at the giver
        _quests.Accept(index, p.QuestNum);
    }
    private void HandleQuestTurnIn(int index, QuestTurnInPacket p)
    {
        if (!_pm[index].IsPlaying || !SlotValidation.IsValidQuestNum(p.QuestNum, _world.Limits.Quests)) return;
        if (!TryResolveInteractNpc(index, p.MapNum, p.NpcSlot, out int npcNum)) return;
        if (_world.Quests[p.QuestNum].EffectiveTurnInNpc != npcNum) return;   // turning in is only allowed at the turn-in NPC
        _quests.TurnIn(index, p.QuestNum);
    }
    private void HandleQuestAbandon(int index, QuestAbandonPacket p) { if (_pm[index].IsPlaying) _quests.Abandon(index, p.QuestNum); }
}
