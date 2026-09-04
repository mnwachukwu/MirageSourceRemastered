using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using System.Linq;

namespace Mirage.Client.Shell.Panels;

/// <summary>Party-panel-styled in-world HUD for a live territory contest, drawn only for a war
/// participant (<c>state.Contest</c> is non-null): a capture-status panel top-center while the player stands
/// inside a capture point, and a KotH score list top-right while they stand in the contested territory.
/// Display-only (no input), so it has just a Draw.</summary>
public sealed class ContestHudPanel
{
    private const int Pad = 4;
    private const int RowH = 14;
    private const int ScoreInnerW = 150;
    private const int StatusInnerW = 150;
    private const int BarH = 8;
    private const int EdgeMargin = 4;
    private static readonly Color PanelBg = new(15, 15, 25, 200);
    private static readonly Color PanelBorder = new(80, 80, 120);

    public void Draw(SpriteBatch sb, SpriteFont font, ClientState state)
    {
        var contest = state.Contest;
        if (contest is null || !contest.Active) return;
        int myGuild = state.Me.GuildId;
        DrawScoreList(sb, font, contest, state, myGuild);
        DrawCaptureStatus(sb, font, contest, state, myGuild);
    }

    // Top-right KotH score list — only while the player stands in the contested territory (current map's group).
    private static void DrawScoreList(SpriteBatch sb, SpriteFont font, TerritoryContestPacket contest, ClientState state, int myGuild)
    {
        if (state.Map.MapGroup != contest.TerritoryIndex) return;
        var scores = contest.Scores.OrderByDescending(s => s.Score).ToList();
        int panelW = ScoreInnerW + Pad * 2;
        int panelH = Pad * 2 + RowH + scores.Count * RowH;   // header + one row per guild
        int x = (int)Camera.ViewW - panelW - EdgeMargin;
        int y = EdgeMargin;
        DrawPanel(sb, new Rectangle(x, y, panelW, panelH));

        int innerX = x + Pad;
        sb.DrawString(font, ClientStrings.Get(ClientStrings.Contest_ScoreHeader), new Vector2(innerX, y + Pad), Color.White);
        int rowY = y + Pad + RowH;
        foreach (var s in scores)
        {
            Color col = s.GuildId == myGuild ? UiHelper.ContestOwnColor : Color.LightGray;
            string sc = s.Score.ToString();
            float scW = font.MeasureString(sc).X;
            sb.DrawString(font, UiHelper.FitText(font, s.GuildName, ScoreInnerW - (int)scW - 6), new Vector2(innerX, rowY), col);
            sb.DrawString(font, sc, new Vector2(x + panelW - Pad - scW, rowY), col);
            rowY += RowH;
        }
    }

    // Which 3x3 cell a map occupies, false when it is not one of the nine loaded around the player.
    private static bool CellOf(ClientState state, int mapNum, out int col, out int row)
    {
        for (col = 0; col < 3; col++)
            for (row = 0; row < 3; row++)
                if (state.NeighborMapNums[col, row] == mapNum) return true;
        col = row = 0;
        return false;
    }

    // Top-center capture status — only while the player stands inside a capture point's radius.
    private static void DrawCaptureStatus(SpriteBatch sb, SpriteFont font, TerritoryContestPacket contest, ClientState state, int myGuild)
    {
        var me = state.Me;
        ContestPointView? inPoint = null;
        // World coordinates across the 3x3, so a point whose zone spills over a seam onto the tile I am
        // standing on reads as held — the same reach the server scores with.
        var (myWx, myWy) = state.CenterToWorld(me.X, me.Y);
        foreach (var pt in contest.Points)
        {
            if (pt.Layer != me.Layer) continue;
            if (!CellOf(state, pt.Map, out int col, out int row)) continue;
            var (ptWx, ptWy) = state.ToWorld(col, row, pt.X, pt.Y);
            if (TerritoryContestFormulas.WithinRadius(myWx, myWy, ptWx, ptWy, Constants.TerritoryCapturePointRadius))
            {
                inPoint = pt;
                break;
            }
        }

        if (inPoint is null) return;
        var p = inPoint;

        Color ctrlColor;
        string ctrlText;
        if (p.OwnerGuild <= 0)
        {
            ctrlColor = UiHelper.ContestNeutralColor;
            ctrlText = ClientStrings.Get(ClientStrings.Contest_Neutral);
        }
        else if (p.OwnerGuild == myGuild)
        {
            ctrlColor = UiHelper.ContestOwnColor;
            ctrlText = ClientStrings.Get(ClientStrings.Contest_HeldByYou);
        }
        else
        {
            ctrlColor = UiHelper.ContestEnemyColor;
            ctrlText = ClientStrings.Get(ClientStrings.Contest_HeldByEnemy);
        }

        // "Under attack" line only when an owned point has a rival actively pushing its meter — the extra row is
        // reserved in the panel height only then, so a securely-held point stays compact.
        bool underAttack = p.OwnerGuild > 0 && p.ChallengerGuild > 0 && p.ChallengerGuild != p.OwnerGuild;
        int textRows = underAttack ? 3 : 2;                 // label + control (+ under-attack)

        int panelW = StatusInnerW + Pad * 2;
        int panelH = Pad * 2 + RowH * textRows + BarH;
        int x = ((int)Camera.ViewW - panelW) / 2;
        int y = EdgeMargin;
        DrawPanel(sb, new Rectangle(x, y, panelW, panelH));
        int innerX = x + Pad;

        sb.DrawString(font, p.Label, new Vector2(innerX, y + Pad), Color.White);
        sb.DrawString(font, ctrlText, new Vector2(innerX, y + Pad + RowH), ctrlColor);
        if (underAttack)
        {
            sb.DrawString(font, ClientStrings.Get(ClientStrings.Contest_UnderAttack),
                new Vector2(innerX, y + Pad + RowH * 2), UiHelper.ContestEnemyColor);
        }

        // Capture bar = the controller's HOLD STRENGTH: full when secure (meter at -Full), draining toward empty
        // as a rival pushes it up toward the +Full flip. Reads intuitively as "how firmly this point is held."
        int full = Constants.TerritoryCaptureFull;
        float frac = Math.Clamp((full - p.Meter) / (float)(full * 2), 0f, 1f);
        var barRect = new Rectangle(innerX, y + Pad + RowH * textRows, StatusInnerW, BarH);
        UiHelper.DrawFilledRect(sb, barRect, UiHelper.BarBg);
        UiHelper.DrawFilledRect(sb, new Rectangle(barRect.X, barRect.Y, (int)(barRect.Width * frac), barRect.Height), ctrlColor);
        UiHelper.DrawBorder(sb, barRect, Color.Gray);
    }

    private static void DrawPanel(SpriteBatch sb, Rectangle rect)
    {
        var shadow = new Rectangle(rect.X + UiHelper.PanelShadowOffset, rect.Y + UiHelper.PanelShadowOffset, rect.Width, rect.Height);
        UiHelper.DrawFilledRect(sb, shadow, UiHelper.PanelShadowColor);
        UiHelper.DrawFilledRect(sb, rect, PanelBg);
        UiHelper.DrawBorder(sb, rect, PanelBorder);
    }
}
