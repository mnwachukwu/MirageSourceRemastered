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

/// <summary>Everything the player types: say, emote, yell, broadcast, notices, whispers and rolls,
/// plus the guild, mail, market, trade, quest and social commands that arrive as chat-adjacent
/// packets.</summary>
public sealed partial class PacketHandler
{
    //  Chat / social handlers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Post-login runtime locale change. Pre-session clients send their locale on the actual
    /// pre-session packet (Login/NewAccount/etc.) and don't need this; the <see cref="ServerPlayer.Login"/>
    /// guard rejects stray sends from unauthenticated connections.
    /// </summary>
    private void HandleSetLanguage(int index, SetLanguagePacket p)
    {
        var sp = _pm[index];
        if (sp.Login.Length == 0) return;
        if (ServerStrings.IsLoaded(p.Locale)) sp.Language = p.Locale;
    }

    private void HandleSayMsg(int index, SayMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string say = p.Msg.Trim();
        if (say.Length == 0) return;
        if (!TextValidation.IsValidText(say))
        {
            HackingAttempt(index, "Say Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(say) {sp.Name}: {say}", "Say"), "AddLog/Say");
        _dispatcher.SendLocalizedChatToViewport(index, ServerStrings.PacketHandler_Say,
            new ChatMetadata(GameColor.Say, ChatChannel.Say, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", say));
        _dispatcher.SendChatBubble(index, PacketBuilder.ChatBubble(index, say, kind: 0), sp.Login, wholeRegion: false);
    }

    private void HandleEmoteMsg(int index, EmoteMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Emote Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(emote) {sp.Name} {p.Msg}", "Emote"), "AddLog/Emote");
        // Route through the per-recipient localized path (not a raw SendToViewport) so an emote respects
        // the recipient's ignore list via SpeakerLogin, like say/yell do.
        _dispatcher.SendLocalizedChatToViewport(index, ServerStrings.PacketHandler_Emote,
            new ChatMetadata(GameColor.Emote, ChatChannel.Say, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
    }

    private void HandleYellMsg(int index, YellMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string yell = p.Msg.Trim();
        if (yell.Length == 0) return;
        if (!TextValidation.IsValidText(yell))
        {
            HackingAttempt(index, "Yell Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        int mapNum = _pm[index].Char.Map;
        _bg.Run(_persistence.AddLogAsync($"(yell) {sp.Name}: {yell}", "Yell"), "AddLog/Yell");
        // Heard across the whole observable region (the speaker's cell and its neighbors).
        ChatToMap(mapNum, ServerStrings.PacketHandler_Yell,
            new ChatMetadata(GameColor.Yellow, ChatChannel.Yell, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", yell));
        _dispatcher.SendChatBubble(index, PacketBuilder.ChatBubble(index, yell, kind: 1), sp.Login, wholeRegion: true);
    }

    /// <summary>Viewport-scoped key-based system chat: heard only within the speaker's earshot.
    /// Used by roll and self-mumble. Each recipient resolves the line in their own session locale
    /// at the dispatcher loop. Channel is required because the two callers classify differently.</summary>
    private void ViewportMsg(int speakerIndex, string key, int color, ChatChannel channel,
        params (string Key, object? Value)[] args) =>
        _dispatcher.SendLocalizedChatToViewport(speakerIndex, key, new ChatMetadata(color, channel), args);

    private void HandleBroadcastMsg(int index, BroadcastMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Broadcast Text Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;

        string raw = p.Msg.Trim();
        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(broadcast) {sp.Name}: {raw}", "Broadcast"), "AddLog/Broadcast");
        _dispatcher.SendLocalizedChatToAll(ServerStrings.PacketHandler_Broadcast,
            new ChatMetadata(GameColor.Pink, ChatChannel.Broadcast, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", raw));
        // Broadcast bubble goes to every connected player. Render is viewport-gated client-side, so
        // latent observers see the bubble only if they enter the speaker's region during its lifetime.
        if (raw.Length > 0)
            _dispatcher.SendToAll(PacketBuilder.ChatBubble(index, raw, kind: 2));
    }

    private void HandleRoll(int index, RollPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (IsMutedAndNotify(index)) return;
        int max = p.Max < 2 ? 2 : p.Max;
        int result = _rng.Next(max) + 1;
        string name = _pm[index].Char.Name.Trim();
        if (max == 2)
        {
            ViewportMsg(index, ServerStrings.PacketHandler_RollCoin, GameColor.Roll, ChatChannel.Say,
                ("Name", name), ("Result", result == 1 ? "Heads" : "Tails"));
        }
        else
        {
            ViewportMsg(index, ServerStrings.PacketHandler_RollDice, GameColor.Roll, ChatChannel.Say,
                ("Name", name), ("Result", result), ("Max", max));
        }
    }

    private void HandleGuildCreate(int index, GuildCreatePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string name = p.Name.Trim();
        // Name-format validation here (mirrors character creation); the guild business rules
        // (eligibility, name uniqueness, funds) are enforced in GuildSystem.CreateGuild. Max counts the whole
        // string (underscores included); min counts alphanumerics only (rejects "A__" / all-underscore).
        switch (NameRules.CheckLength(name, Constants.MinFieldLength, Constants.NameLength))
        {
            case NameLengthResult.TooLong:
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.Guild_NameLength,
                    new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice), ("Max", Constants.NameLength));
                return;
            case NameLengthResult.TooShort:
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.Guild_NameNeedsAlnum,
                    new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice), ("Min", Constants.MinFieldLength));
                return;
        }
        if (!IsValidName(name))
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.Auth_InvalidName,
                new ChatMetadata(GameColor.BrightRed, ChatChannel.Notice));
            return;
        }
        _guilds.CreateGuild(index, name);
    }

    private void HandleGuildDisband(int index, GuildDisbandPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.DisbandGuild(index);
    }

    private void HandleGuildOfferInitiate(int index, GuildOfferInitiatePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string target = p.TargetName.Trim();
        if (target.Length == 0) return;
        if (p.IsRequest) _guilds.RequestJoin(index, target);
        else _guilds.Invite(index, target);
    }

    private void HandleGuildOfferRespond(int index, GuildOfferRespondPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (p.Accept) _guilds.AcceptOffer(index);
        else _guilds.DeclineOffer(index);
    }

    private void HandleGuildSetOpen(int index, GuildSetOpenPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetOpenForMembership(index, p.Open);
    }

    private void HandleGuildSetShowRank(int index, GuildSetShowRankPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetShowRankOverhead(index, p.Show);
    }

    // ── Creator debug: guild settlement + territory war lifecycle (affect every guild) ────────────────────
    private void HandleAdminGuildReset(int index, AdminGuildResetPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Creator)
        {
            HackingAttempt(index, "Guild reset");
            return;
        }
        _guildSchedule.RunManualSettlement(p.Scope);
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_GuildReset,
            new ChatMetadata(GameColor.Pink, ChatChannel.Notice), ("Scope", p.Scope.ToString()));
    }

    private void HandleAdminTerritoryWar(int index, AdminTerritoryWarPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Access < AdminLevel.Creator)
        {
            HackingAttempt(index, "Territory war debug");
            return;
        }
        var meta = new ChatMetadata(GameColor.Pink, ChatChannel.Notice);
        switch (p.Action)
        {
            case TerritoryWarDebugAction.Start:
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_WarStarted, meta, ("Count", _territory.DebugStartWarNight()));
                break;
            case TerritoryWarDebugAction.Advance:
                _dispatcher.SendLocalizedChatTo(index,
                    _territory.DebugAdvanceWar() ? ServerStrings.AdminCommand_WarAdvanced : ServerStrings.AdminCommand_NoWarInProgress, meta);
                break;
            case TerritoryWarDebugAction.End:
                _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_WarEnded, meta, ("Count", _territory.DebugEndWar()));
                break;
        }
    }

    private void HandleGuildLeave(int index, GuildLeavePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.LeaveGuild(index);
    }

    private void HandleGuildKick(int index, GuildKickPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.KickMember(index, p.Login.Trim());
    }

    private void HandleGuildPromote(int index, GuildPromotePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.PromoteMember(index, p.Login.Trim());
    }

    private void HandleGuildDemote(int index, GuildDemotePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.DemoteMember(index, p.Login.Trim());
    }

    private void HandleGuildTransfer(int index, GuildTransferPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.InitiateTransfer(index, p.Login.Trim());
    }

    private void HandleGuildSetMotd(int index, GuildSetMotdPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string motd = p.Motd.Trim();
        if (motd.Length > Constants.GuildMotdMaxLength) motd = motd[..Constants.GuildMotdMaxLength];
        if (motd.Length > 0 && !TextValidation.IsValidText(motd))
        {
            HackingAttempt(index, "Guild MOTD Modification");
            return;
        }
        _guilds.SetMotd(index, motd);
    }

    private void HandleGuildSetLabels(int index, GuildSetLabelsPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetLabels(index, p.Labels);
    }

    private void HandleGuildSetColor(int index, GuildSetColorPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SetColor(index, p.Rgb);
    }

    private void HandleGuildChat(int index, GuildChatPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        string msg = p.Msg.Trim();
        if (msg.Length == 0) return;
        if (!TextValidation.IsValidText(msg))
        {
            HackingAttempt(index, "Guild Chat Modification");
            return;
        }
        if (IsMutedAndNotify(index)) return;
        _guilds.GuildChat(index, msg, p.Officer);
    }

    private void HandleGuildWarPeace(int index, GuildWarPeacePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        switch (p.Action)
        {
            case GuildWarPeaceAction.Offer:
                _guildWar.OfferPeace(index, p.OpponentIndex, p.Offering);
                break;
            case GuildWarPeaceAction.Withdraw:
                _guildWar.WithdrawPeace(index, p.OpponentIndex);
                break;
            case GuildWarPeaceAction.Accept:
                _guildWar.RespondPeace(index, p.OpponentIndex, accept: true);
                break;
            case GuildWarPeaceAction.Reject:
                _guildWar.RespondPeace(index, p.OpponentIndex, accept: false);
                break;
        }
    }

    private void HandleGuildWarWager(int index, GuildWarWagerPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        switch (p.Action)
        {
            case GuildWarWagerAction.Propose:
                _guildWar.ProposeWager(index, p.OpponentIndex, p.Amount);
                break;
            case GuildWarWagerAction.Withdraw:
                _guildWar.WithdrawWager(index, p.OpponentIndex);
                break;
            case GuildWarWagerAction.Accept:
                _guildWar.AcceptWager(index, p.OpponentIndex);
                break;
            case GuildWarWagerAction.Reject:
                _guildWar.RejectWager(index, p.OpponentIndex);
                break;
        }
    }

    private void HandleGuildBrowseRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.Browse(index);
    }

    private void HandleGuildApply(int index, GuildApplyPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.Apply(index, p.Index);
    }

    private void HandleGuildReviewApplication(int index, GuildReviewApplicationPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.ReviewApplication(index, p.Login.Trim(), p.Accept);
    }

    private void HandleMailMarkRead(int index, MailMarkReadPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _mail.MarkRead(index, p.Id);
    }

    private void HandleMailDelete(int index, MailDeletePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _mail.Delete(index, p.Id, p.Outbox);
    }

    private void HandleMailClaim(int index, MailClaimPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _mail.Claim(index, p.Id);
    }

    // Compose + send player-to-player mail, addressed by ACCOUNT name (the mailbox is per-account). Items are
    // escrowed off the sender synchronously (anti-dupe) before any async recipient check; an unknown recipient
    // refunds them.
    private void HandleMailSend(int index, MailSendPacket p)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;

        // Recipients are a comma-separated list. A blank overall field OR any blank token ("a,,b", trailing
        // comma) is rejected; every token is injection-checked; you can never be a recipient (sole or in a list).
        var recipients = p.Recipient.Split(',').Select(t => t.Trim()).ToList();
        if (recipients.Count > Constants.MaxMailRecipients)
        {
            MailMsg(index, ServerStrings.Mail_TooManyRecipients, GameColor.BrightRed, ("Max", Constants.MaxMailRecipients));
            return;
        }
        if (recipients.Count == 0 || recipients.Any(r => r.Length == 0))
        {
            MailMsg(index, ServerStrings.Mail_NoRecipient, GameColor.BrightRed);
            return;
        }
        if (recipients.Any(r => !TextValidation.IsValidText(r)))
        {
            HackingAttempt(index, "Mail Recipient Modification");
            return;
        }
        if (recipients.Any(r => string.Equals(r, sp.Login, StringComparison.OrdinalIgnoreCase)))
        {
            MailMsg(index, ServerStrings.Mail_CannotMailSelf, GameColor.BrightRed);
            return;
        }

        bool multi = recipients.Count > 1;

        // Count staged attachments (resolve each slot to a real item). Attachments are DISALLOWED on a
        // multi-recipient send.
        int attachCount = 0;
        foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
            if (SlotValidation.IsValidInvSlot(spec.InvSlot) && sp.Char.Inv[spec.InvSlot].Num > 0) attachCount++;
        if (multi && attachCount > 0)
        {
            MailMsg(index, ServerStrings.Mail_MultiNoAttachments, GameColor.BrightRed);
            return;
        }

        // Non-mailable backstop for the single-recipient attachment path. Checked BEFORE escrowing.
        foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
        {
            if (!SlotValidation.IsValidInvSlot(spec.InvSlot)) continue;
            int num = sp.Char.Inv[spec.InvSlot].Num;
            if (num > 0 && num <= _world.Limits.Items && _world.Items[num].NonMailable)
            {
                MailMsg(index, ServerStrings.Mail_CannotMailItem, GameColor.BrightRed);
                return;
            }
        }

        // CoD: the receiver pays this to unlock the attachments. A CoD is single-recipient and MUST carry at least
        // one ITEM (non-gold) attachment — a gold-only or text-only CoD is meaningless. Clamp to the market ceiling.
        int codPrice = Math.Clamp(p.CodPrice, 0, Constants.MarketMaxPrice);
        if (codPrice > 0)
        {
            if (multi)
            {
                MailMsg(index, ServerStrings.Mail_CodSingleOnly, GameColor.BrightRed);
                return;
            }
            int itemAttach = 0;
            foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
            {
                if (!SlotValidation.IsValidInvSlot(spec.InvSlot)) continue;
                int num = sp.Char.Inv[spec.InvSlot].Num;
                if (num > 0 && num != Constants.GoldItemIndex) itemAttach++;
            }
            if (itemAttach == 0)
            {
                MailMsg(index, ServerStrings.Mail_CodNeedsItem, GameColor.BrightRed);
                return;
            }
        }

        // A blank subject becomes "(No Subject)" (the client warns first, but the substitution is authoritative).
        string subject = ClampText(p.Subject, Constants.MailSubjectMaxLength);
        if (string.IsNullOrWhiteSpace(subject)) subject = ServerStrings.Get(ServerStrings.Mail_NoSubjectDefault);
        string body = ClampText(p.Body, Constants.MailBodyMaxLength);

        // Cost (a gold sink): single = base + per-attachment; multi = base per recipient. A long body escalates
        // it (over threshold 1 -> x2, threshold 2 -> x10). Charged up front; the body clamp above bounds the length.
        int bodyMult = body.Length > Constants.MailVeryLongBodyThreshold ? Constants.MailVeryLongBodyCostMultiplier
                     : body.Length > Constants.MailLongBodyThreshold ? Constants.MailLongBodyCostMultiplier : 1;
        // Postage is two flat parts plus a percent of what the parcel is WORTH — see EconomyFormulas for
        // why the scaling half is keyed on the shipment rather than the sender. A multi-recipient send
        // carries no attachments (rejected above), so it has no value component.
        long attachedValue = 0;
        if (!multi)
        {
            var ch = sp.Char;
            foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
            {
                if (!SlotValidation.IsValidInvSlot(spec.InvSlot)) continue;
                var slot = ch.Inv[spec.InvSlot];
                if (slot.Num <= 0 || slot.Num > _world.Limits.Items) continue;
                // An EQUIPPED slot is refused by RemoveFromSlot, so it never actually ships — charging a
                // percentage of it would bill for a parcel that does not leave. (The flat per-attachment
                // fee still counts it, as it always has; that is a 50-gold quirk rather than a real one.)
                if (spec.InvSlot == ch.WeaponSlot || spec.InvSlot == ch.ArmorSlot
                    || spec.InvSlot == ch.HelmetSlot || spec.InvSlot == ch.ShieldSlot) continue;
                // Mirrors RemoveFromSlot exactly: currency sends the requested amount, where 0 or an
                // oversized request means the WHOLE stack; everything else always goes as a whole slot.
                var def = _world.Items[slot.Num];
                int quantity = def.Type == ItemType.Currency
                    ? (spec.Quantity <= 0 || spec.Quantity > slot.Quantity ? slot.Quantity : spec.Quantity)
                    : slot.Quantity;
                attachedValue += EconomyFormulas.MailAttachmentValue(slot.Num, quantity, def.Price);
            }
        }
        long cost = (multi ? EconomyFormulas.MailSendCost(0) * recipients.Count
                           : EconomyFormulas.MailSendCost(attachCount, attachedValue)) * bodyMult;
        if (ItemSystem.HasItem(sp.Char, _world.Items, Constants.GoldItemIndex) < cost)
        {
            MailMsg(index, ServerStrings.Mail_CannotAfford, GameColor.BrightRed, ("Cost", cost));
            return;
        }
        _items.TakeItem(index, Constants.GoldItemIndex, (int)cost);

        // Player-origin mail rides "in transit" for a random 10-15 min before it matures on both ends.
        long deliverAt = NowUtc
            + _rng.Next(Constants.MailP2PDeliveryMinSeconds, Constants.MailP2PDeliveryMaxSeconds + 1);

        if (multi)
        {
            // Text-only fan-out: deliver online recipients now; validate the offline ones TOGETHER so every
            // unreachable account collapses into ONE return-to-sender bounce. One optimistic batch confirmation.
            var offline = new List<string>();
            foreach (var to in recipients)
            {
                if (_pm.FindOnlineByLogin(to) != 0)
                    _mail.SendPlayerMail(sp.Login, to, subject, body, new List<MailAttachment>(), deliverAt);
                else
                    offline.Add(to);
            }
            if (offline.Count > 0)
                _bg.Run(DeliverMultiOffline(sp.Login, offline, subject, body, deliverAt), "MailSend");
            MailMsg(index, ServerStrings.Mail_SentToMany, GameColor.BrightGreen, ("Count", recipients.Count));
            return;
        }

        // Single recipient — escrow the staged attachments off the sender NOW, before any async gap.
        string only = recipients[0];
        var attachments = new List<MailAttachment>();
        foreach (var spec in p.Attach.Take(Constants.MaxMailAttachments))
        {
            var (num, qty, dur) = _items.RemoveFromSlot(index, spec.InvSlot, spec.Quantity);
            if (num > 0) attachments.Add(new MailAttachment { ItemNum = num, Quantity = qty, Dur = dur });
        }

        // Online recipient: deliver immediately. Offline: validate the account off-thread, then hop back to
        // deliver it — or, if there's no such account, RETURN the whole mail (attachments included) to the sender.
        if (_pm.FindOnlineByLogin(only) != 0)
        {
            _mail.SendPlayerMail(sp.Login, only, subject, body, attachments, deliverAt, codPrice);
            MailMsg(index, ServerStrings.Mail_Sent, GameColor.BrightGreen);
            return;
        }
        _bg.Run(DeliverOfflineMail(sp.Login, only, subject, body, attachments, deliverAt, codPrice), "MailSend");
    }

    // Pay a Collect-on-Delivery mail: verify the receiver can afford the price AND has room for every item
    // (all-or-nothing — a full bag must never eat a paid item), charge the gold, then release the items + remit
    // the taxed net to the sender via MailSystem.CompleteCod.
    private void HandleMailPayCod(int index, MailPayCodPacket p)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var m = sp.Mail.FirstOrDefault(x => x.Id == p.Id);
        if (m is null || m.CodPrice <= 0) return;                                   // not a live CoD
        if (NowUtc < m.DeliverAt) return;        // still in transit
        if (ItemSystem.HasItem(sp.Char, _world.Items, Constants.GoldItemIndex) < m.CodPrice)
        {
            MailMsg(index, ServerStrings.Mail_CannotAffordCod, GameColor.BrightRed, ("Cost", m.CodPrice));
            return;
        }
        if (!ItemSystem.CanReceiveAll(sp.Char, _world.Items, m.Attachments))
        {
            MailMsg(index, ServerStrings.Mail_CodNoRoom, GameColor.BrightRed);
            return;
        }

        _items.TakeItem(index, Constants.GoldItemIndex, m.CodPrice);
        _mail.CompleteCod(index, m.Id);
        MailMsg(index, ServerStrings.Mail_CodPaid, GameColor.BrightGreen);
    }

    // Single-recipient offline delivery: deliver if the account exists (confirming to the sender), else bounce
    // the whole mail — attachments included — back to the sender as normal return-to-sender mail.
    private async Task DeliverOfflineMail(string senderLogin, string to, string subject, string body,
        List<MailAttachment> attachments, long deliverAt, int codPrice = 0)
    {
        bool exists = await _persistence.AccountExistsAsync(to);
        _gameLoop.Post(() =>
        {
            if (exists)
            {
                _mail.SendPlayerMail(senderLogin, to, subject, body, attachments, deliverAt, codPrice);
                int si = _pm.FindOnlineByLogin(senderLogin);
                if (si != 0) MailMsg(si, ServerStrings.Mail_Sent, GameColor.BrightGreen);
            }
            else
            {
                ReturnToSender(senderLogin, new List<string> { to }, attachments, deliverAt);
            }
        });
    }

    // Multi-recipient offline delivery: validate the offline recipients together, deliver the ones that exist,
    // and collapse ALL unreachable accounts into a SINGLE return-to-sender bounce (multi is text-only).
    private async Task DeliverMultiOffline(string senderLogin, List<string> offline, string subject, string body, long deliverAt)
    {
        var existsFlags = new bool[offline.Count];
        for (int i = 0; i < offline.Count; i++)
            existsFlags[i] = await _persistence.AccountExistsAsync(offline[i]);
        _gameLoop.Post(() =>
        {
            var unreachable = new List<string>();
            for (int i = 0; i < offline.Count; i++)
            {
                if (existsFlags[i]) _mail.SendPlayerMail(senderLogin, offline[i], subject, body, new List<MailAttachment>(), deliverAt);
                else unreachable.Add(offline[i]);
            }
            if (unreachable.Count > 0) ReturnToSender(senderLogin, unreachable, new List<MailAttachment>(), deliverAt);
        });
    }

    // Bounce undeliverable mail back to the sender as normal "System" mail: the original attachments ride back
    // in it (nothing is lost, even for an offline sender), and the body names the account(s) that don't exist.
    // NOT instant — the bounce is deferred until the original would have been delivered, then rides its OWN
    // delivery time back, so a rejection never beats the original's feasible delivery and probing for account
    // names costs real time (as well as the spent, non-refunded postage). Deliver persists it for an offline sender.
    private void ReturnToSender(string senderLogin, List<string> unreachable, List<MailAttachment> attachments, long originalDeliverAt)
    {
        long deliverAt = originalDeliverAt
            + _rng.Next(Constants.MailP2PDeliveryMinSeconds, Constants.MailP2PDeliveryMaxSeconds + 1);
        string body = ServerStrings.Format(ServerStrings.Mail_ReturnedBody, ("Names", string.Join(", ", unreachable)));
        _mail.Deliver(senderLogin, "System", ServerStrings.Get(ServerStrings.Mail_ReturnedSubject), body, attachments, deliverAt);
    }

    private void MailMsg(int index, string key, int color, params (string, object?)[] args)
        => _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(color, ChatChannel.Notice), args);

    private static string ClampText(string s, int max) => s.Length <= max ? s : s[..max];

    // ── Marketplace ────────────────────────────────────────────────────────────

    private void HandleMarketOpen(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _market.Open(index);
    }

    private void HandleMarketCreate(int index, MarketCreatePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _market.List(index, p.InvSlot, p.Quantity, p.Price);
    }

    private void HandleMarketBuy(int index, MarketBuyPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _market.Buy(index, p.Id, p.Quantity);
    }

    private void HandleMarketCancel(int index, MarketCancelPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _market.Cancel(index, p.Id);
    }

    private void HandleMarketRefresh(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _market.Refresh(index);
    }

    // No IsPlaying gate: a closing panel should always clear the viewer flag (harmless if already gone).
    private void HandleMarketClose(int index) => _market.Close(index);

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

    // ── Social (friends / ignore) ─────────────────────────────────────────────
    // Adds are addressed by character name and validated in SocialSystem (must be online, not self);
    // removes are by account login, straight off the row the client is displaying.

    private void HandleGuildInfoRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _guilds.SendGuildInfo(index);
    }

    private void HandleSocialAddFriend(int index, SocialAddFriendPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Name))
        {
            HackingAttempt(index, "Friend Name Modification");
            return;
        }
        _social.AddFriend(index, p.Name.Trim());
    }

    private void HandleSocialAddIgnore(int index, SocialAddIgnorePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Name))
        {
            HackingAttempt(index, "Ignore Name Modification");
            return;
        }
        _social.AddIgnore(index, p.Name.Trim());
    }

    private void HandleSocialRemoveFriend(int index, SocialRemoveFriendPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _social.RemoveFriend(index, p.Login.Trim());
    }

    private void HandleSocialRemoveIgnore(int index, SocialRemoveIgnorePacket p)
    {
        if (!_pm[index].IsPlaying) return;
        _social.RemoveIgnore(index, p.Login.Trim());
    }

    private void HandleNoticeMsg(int index, NoticeMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Notice Text Modification");
            return;
        }
        if (_pm[index].Char.Access <= AdminLevel.Player) return;
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(notice) {sp.Name}: {p.Msg}", "Notice"), "AddLog/Notice");
        // Admin-to-all broadcast: classified as a System Notice (admin announcement), not a Chat channel.
        _dispatcher.SendLocalizedChatToAll(ServerStrings.PacketHandler_Notice,
            new ChatMetadata(GameColor.Notice, ChatChannel.Notice, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
    }

    private void HandleAdminMsg(int index, AdminMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Admin Text Modification");
            return;
        }
        if (_pm[index].Char.Access <= AdminLevel.Player) return;
        if (IsMutedAndNotify(index)) return;

        var sp = SpeakerOf(index);
        _bg.Run(_persistence.AddLogAsync($"(admin) {sp.Name}: {p.Msg}", "Admin"), "AddLog/Admin");
        _dispatcher.SendLocalizedChatToAdmins(ServerStrings.PacketHandler_Admin,
            new ChatMetadata(GameColor.AdminChat, ChatChannel.AdminChat, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
            ("Name", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
    }

    private void HandlePlayerMsg(int index, PlayerMsgPacket p)
    {
        if (!_pm[index].IsPlaying) return;
        if (!TextValidation.IsValidText(p.Msg))
        {
            HackingAttempt(index, "Player Msg Text Modification");
            return;
        }

        int target = _pm.FindPlayerByName(p.Target);

        if (target == index)
        {
            if (IsMutedAndNotify(index)) return;
            string mumbleName = _pm[index].Char.Name.Trim();
            int mumbleMap = _pm[index].Char.Map;
            _bg.Run(_persistence.AddLogAsync($"Map #{mumbleMap}: {mumbleName} begins to mumble to himself.", "Tell"), "AddLog/Tell-mumble");
            ViewportMsg(index, ServerStrings.PacketHandler_SelfMumble, GameColor.Green, ChatChannel.Tell, ("Name", mumbleName));
            return;
        }

        if (target > 0)
        {
            if (IsMutedAndNotify(index)) return;
            var sp = SpeakerOf(index);
            var tp = SpeakerOf(target);
            _bg.Run(_persistence.AddLogAsync($"{sp.Name} tells {tp.Name}, '{p.Msg}'", "Tell"), "AddLog/Tell");
            _dispatcher.SendLocalizedChatTo(target, ServerStrings.PacketHandler_TellFrom,
                new ChatMetadata(GameColor.Tell, ChatChannel.Tell, sp.Name, sp.Access, sp.ShowAsPk, sp.Login),
                ("From", AccessName(sp.Name, sp.Access)), ("Message", p.Msg));
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_TellTo,
                new ChatMetadata(GameColor.Tell, ChatChannel.Tell, tp.Name, tp.Access, tp.ShowAsPk),
                ("To", AccessName(tp.Name, tp.Access)), ("Message", p.Msg));
        }
        else
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
        }
    }

    private void HandleWhosOnline(int index)
    {
        if (!_pm[index].IsPlaying) return;
        _joinLeave.SendWhosOnline(index);
    }

    private void HandlePlayerInfoRequest(int index, PlayerInfoRequestPacket pkt)
    {
        if (!_pm[index].IsPlaying) return;

        int n = _pm.FindPlayerByName(pkt.Target);
        if (n == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.PacketHandler_PlayerNotOnline, new ChatMetadata(GameColor.White, ChatChannel.System));
            return;
        }

        var tp = _pm[n].Char;
        string login = _pm[n].Login.Trim();
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.AdminCommand_PlayerInfo,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Account", login), ("Name", tp.Name.Trim()));
        // Playtime line — the target's current character + account total, shown to any requester.
        long nowUtc = NowUtc;
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_Played,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Char", PlaytimeFormat.HoursMinutes(_pm[n].CharPlaytimeSeconds(nowUtc))),
            ("Total", PlaytimeFormat.HoursMinutes(_pm[n].AccountPlaytimeSeconds(nowUtc))));

        if (_pm[index].Char.Access <= AdminLevel.Monitor) return;

        long tnl = ExpFormulas.TnlForLevel(tp.Level);
        long withinLevel = tp.Exp - ExpFormulas.ExpFloorForLevel(tp.Level);
        string critChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerCriticalChancePerMille(tp.Str, tp.Level));
        string blockChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerBlockChancePerMille(tp.Def, tp.Level));
        string spellCritChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.SpellCriticalChancePerMille(tp.Int, tp.Level));

        void Say(string key, params (string K, object? V)[] args) =>
            _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice), args);
        Say(ServerStrings.AdminCommand_StatsHeader, ("Name", tp.Name.Trim()));
        Say(ServerStrings.AdminCommand_StatsLevel, ("Level", tp.Level), ("Current", withinLevel), ("Tnl", tnl), ("Total", tp.Exp));
        Say(ServerStrings.AdminCommand_StatsVitals, ("Hp", tp.Hp), ("MaxHp", tp.MaxHp), ("Mp", tp.Mp), ("MaxMp", tp.MaxMp), ("Sp", tp.Sp), ("MaxSp", tp.MaxSp));
        Say(ServerStrings.AdminCommand_StatsAttributes, ("Str", tp.Str), ("Def", tp.Def), ("Int", tp.Int), ("Spd", tp.Spd));
        Say(ServerStrings.AdminCommand_StatsChances, ("PCrit", critChance), ("Block", blockChance), ("MCrit", spellCritChance));
    }

    // /played — the requester's own playtime (current character + account total).
    private void HandlePlayedRequest(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var sp = _pm[index];
        long nowUtc = NowUtc;
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.Command_Played,
            new ChatMetadata(GameColor.BrightGreen, ChatChannel.Notice),
            ("Char", PlaytimeFormat.HoursMinutes(sp.CharPlaytimeSeconds(nowUtc))),
            ("Total", PlaytimeFormat.HoursMinutes(sp.AccountPlaytimeSeconds(nowUtc))));
    }

    private void HandleGetStats(int index)
    {
        if (!_pm[index].IsPlaying) return;
        var p = _pm[index].Char;

        long tnl = ExpFormulas.TnlForLevel(p.Level);
        long withinLevel = p.Exp - ExpFormulas.ExpFloorForLevel(p.Level);
        string critChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerCriticalChancePerMille(p.Str, p.Level));
        string blockChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerBlockChancePerMille(p.Def, p.Level));
        string spellCritChance = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.SpellCriticalChancePerMille(p.Int, p.Level));

        void Say(string key, params (string K, object? V)[] args) =>
            _dispatcher.SendLocalizedChatTo(index, key, new ChatMetadata(GameColor.White, ChatChannel.System), args);
        Say(ServerStrings.AdminCommand_StatsHeader, ("Name", p.Name.Trim()));
        Say(ServerStrings.AdminCommand_StatsLevel, ("Level", p.Level), ("Current", withinLevel), ("Tnl", tnl), ("Total", p.Exp));
        Say(ServerStrings.AdminCommand_StatsVitals, ("Hp", p.Hp), ("MaxHp", p.MaxHp), ("Mp", p.Mp), ("MaxMp", p.MaxMp), ("Sp", p.Sp), ("MaxSp", p.MaxSp));
        Say(ServerStrings.AdminCommand_StatsAttributes, ("Str", p.Str), ("Def", p.Def), ("Int", p.Int), ("Spd", p.Spd));
        Say(ServerStrings.AdminCommand_StatsChances, ("PCrit", critChance), ("Block", blockChance), ("MCrit", spellCritChance));
    }

    // ═══════════════════════════════════════════════════════════════════════════
}
