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

/// <summary>Party invites and membership, and the spell list: prepare, cast, and forget.</summary>
public sealed partial class PacketHandler
{
    //  Party handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandlePartyRequest(int index, PartyRequestPacket p)
    {
        if (!_pm[index].IsPlaying) return;

        int targetIndex = _pm.FindPlayerByName(p.Target);
        if (targetIndex == index) return;

        _party.SendPartyRequest(index, p.Target);
    }

    private void HandleJoinParty(int index, JoinPartyPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _party.JoinParty(index);
    }

    private void HandleLeaveParty(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _party.LeaveParty(index);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Spell handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleSpellsRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;
        // Spell array is 1-based; send slots 1..MaxPlayerSpells
        _dispatcher.SendTo(index, new PlayerSpellsPacket { Spells = p.Spell[1..], PreparedSpell = p.PreparedSpell });
    }

    private void HandleCast(int index, CastPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _spells.CastSpell(index, p.Spell, forceSelf: p.Self);
    }
    private void HandleSetPreparedSpell(int index, SetPreparedSpellPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't manage spells
        if (p.Slot < 0 || p.Slot > Constants.MaxPlayerSpells)
        {
            HackingAttempt(index, "Invalid PreparedSpell slot");
            return;
        }
        var chr = _pm[index].Char;
        if (p.Slot > 0 && chr.Spell[p.Slot] <= 0) return;
        chr.PreparedSpell = p.Slot;
        // Echo PlayerSpellsPacket: SpellPanel tracks its own slot for UI purposes, so this is the only thing
        // that refreshes the authoritative PreparedSpell on the client's PlayerRecord (read by e.g. the
        // StatsPanel M-DMG breakdown).
        _dispatcher.SendTo(index, new PlayerSpellsPacket { Spells = chr.Spell[1..], PreparedSpell = chr.PreparedSpell });
    }

    private void HandleForgetSpell(int index, ForgetSpellPacket p)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        if (sp.Char.Dead) return;  // a corpse can't manage spells
        if (!SlotValidation.IsValidSpellSlot(p.Slot))
        {
            HackingAttempt(index, "Invalid ForgetSpell slot");
            return;
        }
        var chr = sp.Char;
        if (chr.Spell[p.Slot] <= 0) return;

        if (sp.IsInCombat(Environment.TickCount64))
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_StudyCombat, new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
            return;
        }

        var spellRec = _world.Spells[chr.Spell[p.Slot]];
        string spellName = spellRec?.TrimmedName ?? "the spell";

        chr.Spell[p.Slot] = 0;
        // Auto-unprepare if the forgotten slot was the prepared one — otherwise PreparedSpell would
        // dangle and the client UI would briefly show "Prepared" on the now-empty row.
        if (chr.PreparedSpell == p.Slot) chr.PreparedSpell = 0;

        _dispatcher.SendTo(index, new PlayerSpellsPacket
        {
            Spells = chr.Spell[1..],
            PreparedSpell = chr.PreparedSpell
        });
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_ForgotSpell, new ChatMetadata(GameColor.Yellow, ChatChannel.System), ("Spell", spellName));
    }

    // ═══════════════════════════════════════════════════════════════════════════
}
