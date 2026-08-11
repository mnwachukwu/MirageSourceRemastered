using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text;

namespace Mirage.Client.Shell.Screens;

/// <summary>The right-click menus over players and NPCs, and the quest/conversation dialogs they
/// open. Includes the stacked-NPC case, where several NPCs occupy one tile.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    private void OpenPlayerContextMenu(string targetName, Point at)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        var me = _ctx.State.Me;
        if (targetName == me.Name.Trim()) return;
        var access = me.Access;
        var sender = _ctx.Sender;

        var items = new List<ContextMenu.Item>
        {
            new(ClientStrings.Get(ClientStrings.ContextMenu_Info), () => sender.SendPlayerInfoRequest(targetName)),
        };
        // Party Invite — Player & Monitor only; hidden for Mapper+ (admins can't party).
        if (access <= AdminLevel.Monitor)
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_PartyInvite), () => sender.SendPartyRequest(targetName)));
        items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Whisper), () => StartWhisper(targetName)));

        // Direct trade — a Player-only economy action (matches guild). The server validates that the
        // target is online and within casting range (r=5), so an out-of-range click just gets a refusal.
        if (access == AdminLevel.Player)
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Trade), () => sender.SendTradeInvite(targetName)));

        // Friends / Ignore. Both are keyed to the target's ACCOUNT (the server resolves this character
        // name to it), so ignoring here silences every character that player owns, on every channel.
        items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_AddFriend), () => sender.SendSocialAddFriend(targetName)));
        items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Ignore), () => sender.SendSocialAddIgnore(targetName)));

        // Guild (a Player-only feature). Invite a guildless player if I'm an officer/leader; or request
        // to join the target's guild if I'm guildless and their open guild has an officer/leader here.
        if (access == AdminLevel.Player)
        {
            var target = FindPlayerByName(targetName);
            if (target is not null)
            {
                if (me.GuildRank >= GuildRank.Officer && target.GuildId == 0)
                    items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_GuildInvite), () => sender.SendGuildInvite(targetName)));
                if (me.GuildId == 0 && target.GuildRank >= GuildRank.Officer && target.GuildOpen)
                    items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_GuildRequest), () => sender.SendGuildJoinRequest(targetName)));
            }
        }

        if (access > AdminLevel.Player) // Monitor+
        {
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Mute), () => sender.SendMute(targetName)));
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Kick), () => sender.SendKick(targetName)));
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Ban), () => sender.SendBan(targetName)));
        }
        if (access > AdminLevel.Mapper) // Developer+
        {
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_TeleportTo), () => sender.SendWarpMeTo(targetName)));
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_BringHere), () => sender.SendWarpToMe(targetName)));
        }
        if (access >= AdminLevel.Creator)
        {
            var subItems = new List<ContextMenu.Item>
            {
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Player),    () => sender.SendSetAccess(targetName, AdminLevel.Player)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Monitor),   () => sender.SendSetAccess(targetName, AdminLevel.Monitor)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Mapper),    () => sender.SendSetAccess(targetName, AdminLevel.Mapper)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Developer), () => sender.SendSetAccess(targetName, AdminLevel.Developer)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Creator),   () => sender.SendSetAccess(targetName, AdminLevel.Creator)),
            };
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_SetAccess), subItems));
        }

        var screen = new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH);
        _contextMenu.Open(at, targetName, items, screen, _gameFont!);
    }

    // Right-click / interact on an NPC → a menu of its roles (keeper shop/inn + actionable quests), each enabled
    // ONLY within r=5 (grayed out of range, enabling live as the player approaches — a per-frame EnabledFn).
    private void OpenNpcContextMenu(TargetRef npc, Point at)
        => OpenNpcMenusForStack(new[] { npc }, at);

    /// <summary>Opens the right-click menu for one or more NPCs stacked under the cursor. ONE menu-bearing NPC →
    /// its actions flat, titled by its name. TWO-plus (a ground mob + a mob on the bridge above the same tile) →
    /// one menu whose top level is each NPC's NAME, expanding to that NPC's actions, so it's clear which set of
    /// actions belongs to which NPC. NPCs with no keeper/quest menu are skipped.</summary>
    private void OpenNpcMenusForStack(IReadOnlyList<TargetRef> stack, Point at)
    {
        if (_gameFont is null) return;
        var groups = new List<(string Name, List<ContextMenu.Item> Items)>();
        bool blockedByLayer = false;
        foreach (var npc in stack)
        {
            if (BuildNpcMenuItems(npc, out string name) is { } items)
            {
                // A menu-bearing NPC on a plane the player's doesn't connect to is unreachable, so it contributes no
                // menu — the cursor can't tell the two apart, and the server refuses the interact anyway. Noted (not
                // returned on) so a reachable NPC stacked under the same pixel still opens its own menu silently.
                if (NpcLayerReachable(npc)) groups.Add((name, items));
                else blockedByLayer = true;
            }
        }

        if (groups.Count == 0)
        {
            // Only complain when a menu WOULD have opened but for the layer; right-clicking a plain mob (on either
            // plane) has never opened anything, so it stays quiet.
            if (blockedByLayer) AddChatLine(ClientStrings.Get(ClientStrings.GameplayScreen_NpcOtherLayer), GameColor.BrightRed);
            return;
        }

        var screen = new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH);
        if (groups.Count == 1)
        {
            _contextMenu.Open(at, groups[0].Name, groups[0].Items, screen, _gameFont);   // single → flat, titled by name
        }
        else
        {
            var top = new List<ContextMenu.Item>(groups.Count);
            foreach (var g in groups) top.Add(new ContextMenu.Item(g.Name, g.Items));     // per-NPC name → its action submenu
            _contextMenu.Open(at, "", top, screen, _gameFont);
        }
    }

    // Can the local player reach this native map NPC across the two layers? Same plane always; across them only
    // from a ramp's mount side — so a bridge keeper is unreachable from the ground beneath it (even though both
    // draw under the same cursor pixel) but IS reachable from the ramp's foot. Mirrors the server's interact gate
    // (GameWorld.IsNpcInInteractRange) so the menu never offers what it would refuse. A slot that no longer
    // resolves counts as unreachable and simply drops out of the menu.
    private bool NpcLayerReachable(TargetRef npc)
    {
        if (npc.Kind != TargetKind.Npc) return false;
        var npcs = _ctx.State.NpcsForMap(npc.B);
        if (npcs is null || !SlotValidation.IsValidNpcSlot(npc.A)) return false;
        if (!ResolveTargetTile(npc, out int m, out int nx, out int ny)) return false;
        var off = CellOffsetForMapClient(m);
        if (off is null) return false;
        return ClientLineOfSight.LayerConnectsFromLocalPlayer(
            _ctx.State, off.Value.ox + nx, off.Value.oy + ny, npcs[npc.A].Layer);
    }

    /// <summary>Builds the Talk/Shop/Quest actions for ONE npc (each range-gated), or null if it has no right-click
    /// menu. <paramref name="name"/> is its display name (menu title / per-NPC label when stacked).</summary>
    private List<ContextMenu.Item>? BuildNpcMenuItems(TargetRef npc, out string name)
    {
        name = "";
        if (npc.Kind != TargetKind.Npc) return null;   // right-click menus are for native NPCs (A=slot, B=map); a chasing guest has none
        var npcs = _ctx.State.NpcsForMap(npc.B);
        int num = npcs is not null && SlotValidation.IsValidNpcSlot(npc.A) ? npcs[npc.A].Num : 0;
        if (num <= 0) return null;
        // Only keeper / quest NPCs get a right-click menu; a plain-talk mob
        // is handled by the melee key, not this menu.
        if (_ctx.State.NpcKeeperShop[num] == 0 && _ctx.State.NpcQuestGlyph[num] == 0) return null;
        name = _ctx.State.NpcDefs[num]?.Name?.Trim() ?? "";
        int npcSize = _ctx.State.NpcDefs[num]?.EffectiveSize ?? 1;   // footprint-aware r=5: an oversize NPC is reachable by its body
        bool InRange()
        {
            var me = _ctx.State.Me;
            if (!ResolveTargetTile(npc, out int m, out int nx, out int ny)) return false;
            var off = CellOffsetForMapClient(m);
            if (off is null) return false;
            return WorldCoordHelper.IsInSpellRange(
                WorldCoordHelper.MapTilesX + me.X, WorldCoordHelper.MapTilesY + me.Y, 1,
                off.Value.ox + nx, off.Value.oy + ny, npcSize);
        }
        var items = new List<ContextMenu.Item>();

        // Conversation ("Talk"): present when the NPC has a dialogue tree. Forces Choice.Talk so it opens the chat
        // even for an NPC that also keeps a shop / gives quests (talk-first is the melee default; here it's explicit).
        if (_ctx.State.ConversationForNpc(num) > 0)
        {
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Talk),
                () => _ctx.Sender.SendNpcInteract(npc.B, npc.A, NpcInteractChoice.Talk),   // B = map, A = slot
                (Func<bool>)InRange));
        }

        // Keeper shop/inn: an inn keeper (kind 2) reads "Inn", a store keeper "Shop". Forces Choice.Shop
        // so a keeper that also gives quests still opens the shop from this item (no re-route into the quest menu).
        if (_ctx.State.NpcKeeperShop[num] != 0)
        {
            string label = _ctx.State.NpcKeeperShop[num] == 2
                ? ClientStrings.Get(ClientStrings.ContextMenu_Inn)
                : ClientStrings.Get(ClientStrings.ContextMenu_Shop);
            items.Add(new(label,
                () => _ctx.Sender.SendNpcInteract(npc.B, npc.A, NpcInteractChoice.Shop),   // B = map, A = slot
                (Func<bool>)InRange));
        }

        // Visible quests: a "Quest:" / "Turn in:" item per quest → the offer dialog. An ineligible giver still gets a
        // "Quest:" item; opening it shows the dialog read-only (requirements listed, Accept disabled) — not hidden.
        foreach (var (questNum, action, _) in _ctx.State.VisibleQuestsAt(num))
        {
            string qname = _ctx.State.QuestDefs[questNum]?.TrimmedName ?? "";
            string label = action == ClientState.QuestAction.Accept
                ? ClientStrings.Format(ClientStrings.ContextMenu_QuestAccept, ("Name", qname))
                : ClientStrings.Format(ClientStrings.ContextMenu_QuestTurnIn, ("Name", qname));
            int qn = questNum, map = npc.B, slot = npc.A;
            var act = action;
            items.Add(new(label, () => OpenQuestDialog(qn, act, map, slot), (Func<bool>)InRange));
        }

        return items.Count > 0 ? items : null;
    }

    // Open the accept/turn-in offer for a quest at the given NPC (from the NPC menu). It's a panel, so it joins
    // the normal z-order / focus flow (mirrors OpenShop / OpenInnPanel).
    private void OpenQuestDialog(int questNum, ClientState.QuestAction action, int map, int slot)
    {
        _questDialog.Open(questNum, action, map, slot);
        BringToFront(PanelQuestDialog);
        _panelFocused = true;
    }

    /// <summary>Server reply to a talk-first interact that resolved to a quest (melee key, or a conversation's
    /// "ask about quests" hand-off). Opens the quest OFFER panel directly for the common single-quest case — the
    /// melee key has no cursor, and funnelling one quest through a one-item menu is the "opening a quest opens a
    /// menu" wart. Only when several quests are actionable at once does it fall back to the centered NPC menu so
    /// the player can pick which. (map, slot) identify the NPC.</summary>
    public void OpenNpcQuestMenuAt(int map, int slot)
    {
        if (_gameFont is null) return;
        var npcs = _ctx.State.NpcsForMap(map);
        int num = npcs is not null && SlotValidation.IsValidNpcSlot(slot) ? npcs[slot].Num : 0;
        if (num <= 0) return;

        int firstQuest = 0;
        var firstAction = ClientState.QuestAction.Accept;
        int visible = 0;
        foreach (var (questNum, action, _) in _ctx.State.VisibleQuestsAt(num))
        {
            visible++;
            if (visible == 1)
            {
                firstQuest = questNum;
                firstAction = action;
            }
        }

        if (visible == 1)
            OpenQuestDialog(firstQuest, firstAction, map, slot);
        else
            OpenNpcContextMenu(new TargetRef(TargetKind.Npc, slot, map), new Point(UiHelper.RefW / 2, UiHelper.RefH / 2));
    }

    /// <summary>Server reply to an interact that resolved to a conversation (OpenNpcConversation) — open the
    /// conversation panel for the NPC (map, slot) on conversation <paramref name="conv"/>. Joins the z-order flow.</summary>
    public void OpenConversationAt(int map, int slot, int conv)
    {
        _conversation.Open(conv, map, slot);
        BringToFront(PanelConversation);
        _panelFocused = true;
    }

    private PlayerRecord? FindPlayerByName(string name)
    {
        var players = _ctx.State.Players;
        for (int i = 1; i < players.Length; i++)
            if (players[i].Name.Trim() == name) return players[i];
        return null;
    }

    /// <summary>Whisper menu item — focuses chat and prefills `/w &lt;name&gt; ` for the user to type.</summary>
    private void StartWhisper(string targetName)
    {
        _chat.StartWhisper(targetName);
    }

    // Single entry point for every panel show/hide path (slash commands, sidebar buttons,
    // hotkeys, links). Closing only happens when the panel is already the topmost open one;
    // a buried panel is raised to the front instead of toggled, and a closed panel is opened.
}
