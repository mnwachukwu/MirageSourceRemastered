using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The accept / turn-in offer window for a single quest. Opened from an NPC's interaction
/// (gossip) menu with a specific quest + action + the giver/turn-in NPC's (map, slot); shows the quest's
/// description, objectives (with live progress) and rewards, and a single action button. Confirming sends the
/// accept/turn-in (the server re-validates proximity + role); the fresh QuestLog push closes the loop.
/// </summary>
public sealed class QuestDialogPanel : IGamePanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 330, 280), minH: 200, minW: 300);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool LayoutChanged => _panel.LayoutChanged;
    public void SetBounds(Rectangle b) => _panel.SetBounds(b);
    public void ResetBounds() => _panel.ResetBounds();
    public bool ContainsMouse(Point p) => IsOpen && _panel.ContainsMouse(p);

    private int _questNum;
    private ClientState.QuestAction _action;
    private int _map, _slot;
    private readonly Button _actionBtn = new();
    private InputState _input = new();
    private int _labelsGeneration = -1;

    private const int Pad = 8;
    private const int LineH = 14;

    /// <summary>Show the offer for <paramref name="questNum"/> at the NPC (map, slot). Action = Accept or TurnIn.</summary>
    public void Open(int questNum, ClientState.QuestAction action, int map, int slot)
    {
        _questNum = questNum;
        _action = action;
        _map = map;
        _slot = slot;
        IsOpen = true;
        _labelsGeneration = -1;   // force a label refresh for the new action
    }

    public void Toggle() { IsOpen = !IsOpen; }

    public void Update(InputState input, ClientState state, ClientPacketSender sender)
    {
        if (!IsOpen) return;
        _input = input;
        _panel.Update(input);
        if (_panel.WasClosed)
        {
            IsOpen = false;
            return;
        }

        var c = _panel.ContentBounds;
        _actionBtn.Bounds = UiHelper.PanelBottomButton(c, 0, 1);
        // An Accept offer whose requirements aren't met yet is read-only: the button grays out and can't fire
        // (Button.IsClicked honors Enabled), but the panel still lists the requirements so the player sees why.
        _actionBtn.Enabled = _action == ClientState.QuestAction.TurnIn || state.IsQuestEligible(_questNum);
        if (_actionBtn.IsClicked(input))
        {
            if (_action == ClientState.QuestAction.Accept) sender.SendQuestAccept(_questNum, _map, _slot);
            else sender.SendQuestTurnIn(_questNum, _map, _slot);
            IsOpen = false;   // the server replies with a fresh QuestLog; the offer is done
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state, bool isActive)
    {
        if (!IsOpen) return;
        var def = _questNum >= 1 && _questNum < state.QuestDefs.Length ? state.QuestDefs[_questNum] : null;
        if (def is null)
        {
            IsOpen = false;
            return;
        }

        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _actionBtn.Label = ClientStrings.Get(_action == ClientState.QuestAction.Accept
                ? ClientStrings.QuestDialog_AcceptButton : ClientStrings.QuestDialog_TurnInButton);
        }

        _panel.Draw(sb, font, def.TrimmedName, isActive);
        var c = _panel.ContentBounds;
        float y = c.Y + Pad;

        // Flavor / description (word-wrapped to the content width).
        y = DrawWrapped(sb, font, def.Description, c.X + Pad, y, c.Width - Pad * 2, Color.LightGray);
        y += 6;

        // Objectives with live progress.
        sb.DrawString(font, ClientStrings.Get(ClientStrings.QuestDialog_ObjectivesHeader), new Vector2(c.X + Pad, y), UiHelper.DlgLabelColor);
        y += LineH + 2;
        var pq = state.FindQuest(_questNum);
        if (def.Objectives.Count == 0)
        {
            sb.DrawString(font, ClientStrings.Get(ClientStrings.QuestDialog_ObjectiveNone), new Vector2(c.X + Pad + 6, y), Color.White);
            y += LineH;
        }
        for (int k = 0; k < def.Objectives.Count; k++)
        {
            var o = def.Objectives[k];
            int prog = pq is not null && k < pq.Progress.Count ? pq.Progress[k] : 0;
            string target = o.Target >= 1 && o.Target < state.NpcDefs.Length
                ? (state.NpcDefs[o.Target]?.Name?.TrimEnd() ?? "?") : "?";
            string line = ClientStrings.Format(ClientStrings.QuestDialog_ObjectiveKill, ("Target", target), ("Have", prog), ("Need", o.Count));
            sb.DrawString(font, line, new Vector2(c.X + Pad + 6, y), prog >= o.Count ? Color.LightGreen : Color.White);
            y += LineH;
        }
        y += 6;

        // Rewards.
        sb.DrawString(font, ClientStrings.Get(ClientStrings.QuestDialog_RewardsHeader), new Vector2(c.X + Pad, y), UiHelper.DlgLabelColor);
        y += LineH + 2;
        if (def.RewardExp > 0)
        {
            sb.DrawString(font, ClientStrings.Format(ClientStrings.QuestDialog_RewardExp, ("Exp", def.RewardExp)), new Vector2(c.X + Pad + 6, y), Color.White);
            y += LineH;
        }
        foreach (var r in def.RewardItems)
        {
            if (r.ItemNum < 1 || r.ItemNum >= state.Items.Length) continue;
            string item = state.Items[r.ItemNum]?.Name?.TrimEnd() ?? "?";
            sb.DrawString(font, ClientStrings.Format(ClientStrings.QuestDialog_RewardItem, ("Item", item), ("Qty", r.Quantity)), new Vector2(c.X + Pad + 6, y), Color.White);
            y += LineH;
        }

        // Ineligible Accept offer: list the requirements (green = met, red = unmet) so the grayed-out Accept has a
        // visible reason. Uses the same QuestRequirements builder as the quest-log hover tooltip.
        if (_action == ClientState.QuestAction.Accept && !state.IsQuestEligible(_questNum))
        {
            var reqs = QuestRequirements.Build(state, _questNum, def);
            if (reqs.Count > 0)
            {
                y += 6;
                sb.DrawString(font, ClientStrings.Get(ClientStrings.QuestPanel_ReqHeader), new Vector2(c.X + Pad, y), UiHelper.DlgLabelColor);
                y += LineH + 2;
                foreach (var (text, met) in reqs)
                {
                    sb.DrawString(font, text, new Vector2(c.X + Pad + 6, y), met ? Color.LightGreen : Color.OrangeRed);
                    y += LineH;
                }
            }
        }

        _actionBtn.Draw(sb, font, _input);
        _panel.DrawOverlay(sb);
    }

    // Minimal greedy word-wrap; returns the y past the last drawn line.
    private static float DrawWrapped(SpriteBatch sb, SpriteFont font, string text, float x, float y, float maxWidth, Color color)
    {
        if (string.IsNullOrWhiteSpace(text)) return y;
        var words = text.Split(' ');
        string line = "";
        foreach (var w in words)
        {
            string test = line.Length == 0 ? w : line + " " + w;
            if (font.MeasureString(test).X > maxWidth && line.Length > 0)
            {
                sb.DrawString(font, line, new Vector2(x, y), color);
                y += LineH;
                line = w;
            }
            else
            {
                line = test;
            }
        }
        if (line.Length > 0)
        {
            sb.DrawString(font, line, new Vector2(x, y), color);
            y += LineH;
        }
        return y;
    }
}
