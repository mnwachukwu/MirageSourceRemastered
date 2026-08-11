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

/// <summary>The screen-space pass over the world: HUD, panels in z-order, floating combat text,
/// and the small primitives (bars, arrows, fixed-cell text) those share.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    // ── Rendering helpers ──────────────────────────────────────────────────────

    // Drawn at the FLOAT screen position (native source size); the DrawWorld supersample transform scales
    // it up, so sub-pixel positions rasterize at supersample granularity → smooth scroll, steady player.
    private void DrawTile(SpriteBatch sb, TileDrawCmd cmd)
    {
        if (cmd.Sheet < 0 || cmd.Sheet >= _tilesets.Length) return;
        var tex = _tilesets[cmd.Sheet];
        if (tex is null) return;
        var src = TileAtlas.GetSourceRect(cmd.Sheet, cmd.TileIndex);
        if (src == Rectangle.Empty) return;
        sb.Draw(tex, new Vector2(cmd.ScreenX, cmd.ScreenY), src, Color.White);
    }

    private void DrawSprite(SpriteBatch sb, SpriteDrawCmd cmd)
    {
        // Pick the size-matched atlas (32/64/96 cell). A larger NPC draws at its native cell size from
        // (ScreenX,ScreenY) - the top-left of its footprint - so no scaling or offset is needed. A missing
        // size sheet (art not added yet) draws nothing; the NPC's bars/name/collision still work.
        var tex = cmd.Size >= 3 ? _ctx.Sprites96 : cmd.Size == 2 ? _ctx.Sprites64 : _sprites;
        if (tex is null) return;
        int cell = cmd.Size * Constants.PicX;
        var src = SpriteAtlas.GetSourceRect(cmd.SpriteRow, cmd.Dir, cmd.AnimFrame, cell);
        sb.Draw(tex, new Vector2(cmd.ScreenX, cmd.ScreenY), src, Color.White);
    }

    private void DrawItem(SpriteBatch sb, ItemDrawCmd cmd)
    {
        var src = ItemAtlas.GetSourceRect(cmd.Pic);
        if (src == Rectangle.Empty) return;
        sb.Draw(_items!, new Vector2(cmd.ScreenX, cmd.ScreenY), src, Color.White);
    }

    // Reused across all DrawStringFixed calls; avoids one string allocation per character.
    private static readonly StringBuilder _charBuf = new(4);

    // Each character advances by a fixed cell width so that space characters (whose advance
    // MonoGame may zero-out for bitmap fonts) still create a visible gap.
    // Draw one world-anchored name label (the floating overhead names AND the demoted corpse names share
    // this): fixed-cell text with a black drop shadow, a raw-RGB override winning over the palette index,
    // LineOffset stacking by real font height, and an edge clamp so a name near the viewport border slides
    // inward instead of getting scissor-clipped.
    // Capture-point flag geometry (px), drawn within one 32x32 tile.
    private const float ContestFlagPoleInset = 6f;     // pole x, left of the tile center
    private const float ContestFlagMargin = 3f;        // pole top/bottom inset from the tile edges
    private const float ContestFlagPennantW = 14f;     // pennant reach to the right of the pole
    private const float ContestFlagPennantApexY = 5f;  // pennant apex y, below the pole top
    private const float ContestFlagPennantBotY = 10f;  // pennant base y, below the pole top
    private const float ContestLabelGap = 2f;          // gap between the flag top and the name label
    private const float ContestCircleAlpha = 0.65f;    // radius-circle opacity (a subtle marker, not a wall)
    private const float ContestCircleThickness = 1.5f;

    // Draw one contest capture point in the world layer: a radius circle, a small triangular flag, and the
    // point's name above it, all in the per-viewer control color. Walk-over-able (drawn under the entities).
    private static void DrawContestPoint(SpriteBatch sb, SpriteFont nameFont, ContestPointCmd cp, float nameCellW, float nameLineH)
    {
        Color color = cp.Control switch
        {
            ContestControl.Own => UiHelper.ContestOwnColor,
            ContestControl.Enemy => UiHelper.ContestEnemyColor,
            _ => UiHelper.ContestNeutralColor,
        };
        // Radius circle centered on the point tile.
        var center = new Vector2(cp.ScreenX + Constants.PicX / 2f, cp.ScreenY + Constants.PicY / 2f);
        UiHelper.DrawCircleOutline(sb, center, cp.RadiusPx, color * ContestCircleAlpha, ContestCircleThickness);

        // Flag: a dark pole + a colored triangular pennant near its top, centered on the tile.
        float poleX = cp.ScreenX + Constants.PicX / 2f - ContestFlagPoleInset;
        float poleTop = cp.ScreenY + ContestFlagMargin;
        float poleBot = cp.ScreenY + Constants.PicY - ContestFlagMargin;
        UiHelper.DrawLine(sb, new Vector2(poleX, poleTop), new Vector2(poleX, poleBot), Color.Black, 2f);
        var apexA = new Vector2(poleX, poleTop);
        var apexB = new Vector2(poleX + ContestFlagPennantW, poleTop + ContestFlagPennantApexY);
        var apexC = new Vector2(poleX, poleTop + ContestFlagPennantBotY);
        UiHelper.FillTriangle(sb, apexA, apexB, apexC, color);
        UiHelper.DrawLine(sb, apexA, apexB, Color.Black, 1f);
        UiHelper.DrawLine(sb, apexB, apexC, Color.Black, 1f);

        // Name above the flag, colored to match via RgbOverride; drawn in the world layer like a corpse name.
        int rgb = (color.R << 16) | (color.G << 8) | color.B;
        var labelCmd = new TextDrawCmd(cp.ScreenX + Constants.PicX / 2f, poleTop - ContestLabelGap, cp.Label,
            0, AlignBottom: true, RgbOverride: rgb);
        DrawWorldName(sb, nameFont, labelCmd, nameCellW, nameLineH);
    }

    private static void DrawWorldName(SpriteBatch sb, SpriteFont nameFont, TextDrawCmd cmd, float nameCellW, float nameLineH)
    {
        // The overhead guild line carries the guild name in Text plus (optionally) a numeric rank + standing to
        // append: "<Guild> {Rank} ({Standing})". Localize the rank word + assemble here (the logic layer that
        // built the command has no string table).
        string text = cmd.Text;
        if (cmd.GuildRankWord > 0) text += " " + OverheadRankWord((GuildRank)cmd.GuildRankWord);
        if (cmd.GuildStanding > 0) text += " (" + cmd.GuildStanding + ")";
        Color nameColor = cmd.RgbOverride >= 0
            ? new Color(GameColor.RedOf(cmd.RgbOverride), GameColor.GreenOf(cmd.RgbOverride), GameColor.BlueOf(cmd.RgbOverride))
            : ChatPanel.GetColor(cmd.ColorIndex);
        float totalW = nameCellW * text.Length;
        // AlignBottom (labels ABOVE the sprite): the name's bottom sits at ScreenY and extra lines ($/?/.../guild)
        // stack UPWARD. Flipped BELOW the sprite (AlignBottom=false — NPC/player near the top of the view): the name
        // is TOP-aligned at ScreenY, so extra lines must stack DOWNWARD below it. A LineOffset that always
        // subtracts rides below-sprite markers back up into the sprite, overlapping the name.
        float drawY = cmd.AlignBottom
            ? cmd.ScreenY - nameLineH - cmd.LineOffset * nameLineH
            : cmd.ScreenY + cmd.LineOffset * nameLineH;
        float nameX = cmd.ScreenX - totalW / 2f;
        float maxNameX = Camera.ViewW - totalW;
        if (maxNameX < 0) maxNameX = 0;
        nameX = Math.Clamp(nameX, 0, maxNameX);
        var namePos = new Vector2(nameX, drawY);
        // Crossed-swords "at war" marker to the left of the guild name (plan: identify guilds you're at war with).
        if (cmd.AtWar) DrawCrossedSwords(sb, nameX, drawY, nameLineH, nameColor);
        DrawStringFixed(sb, nameFont, text, namePos + new Vector2(2, 2), Color.Black, nameCellW);
        DrawStringFixed(sb, nameFont, text, namePos + new Vector2(1, 1), Color.Black, nameCellW);
        DrawStringFixed(sb, nameFont, text, namePos, nameColor, nameCellW);
    }

    // The localized overhead rank word (Officer / Leader) for the overhead rank line.
    private static string OverheadRankWord(GuildRank rank) => ClientStrings.Get(rank switch
    {
        GuildRank.Leader => ClientStrings.SocialPanel_RankLeader,
        GuildRank.Officer => ClientStrings.SocialPanel_RankOfficer,
        _ => ClientStrings.SocialPanel_RankMember,
    });

    // An improvised crossed-swords glyph (two blades + a crossguard bar) drawn just left of the guild name,
    // in the guild's color, when the viewer is at war with that guild. A black shadow underlay keeps it
    // legible over any terrain, matching the name text's outline.
    private static void DrawCrossedSwords(SpriteBatch sb, float textLeftX, float topY, float lineH, Color color)
    {
        float sz = Math.Max(6f, lineH - 4f);
        float x = textLeftX - sz - 2f;   // sits just left of the text's left edge
        float y = topY + 2f;
        void Swords(Vector2 off, Color c)
        {
            UiHelper.DrawLine(sb, new Vector2(x, y + sz) + off, new Vector2(x + sz, y) + off, c, 2f);          // "/" blade
            UiHelper.DrawLine(sb, new Vector2(x, y) + off, new Vector2(x + sz, y + sz) + off, c, 2f);          // "\" blade
            UiHelper.DrawLine(sb, new Vector2(x + 1, y + sz - 3) + off, new Vector2(x + sz - 1, y + sz - 3) + off, c, 1f); // crossguard
        }
        Swords(new Vector2(1, 1), Color.Black);   // shadow
        Swords(Vector2.Zero, color);
    }

    private static void DrawStringFixed(SpriteBatch sb, SpriteFont font, string text, Vector2 pos, Color color, float cellW)
    {
        var cur = pos;
        foreach (char c in text)
        {
            if (c != ' ')
            {
                _charBuf.Clear();
                _charBuf.Append(c);
                sb.DrawString(font, _charBuf, cur, color);
            }
            cur.X += cellW;
        }
    }

    // Downward-pointing filled triangle: widest row at top, 1px tip at tipY.
    // centerX/tipY are float (track the entity sub-pixel); w/h are the genuine pixel dimensions of the
    // triangle, and rowW is its per-row pixel width — only the half-width CENTER offset is float (/2f).
    private void DrawDownArrow(SpriteBatch sb, float centerX, float tipY, int w, int h, Color color)
    {
        for (int row = 0; row < h; row++)
        {
            int rowW = Math.Max(1, w - 2 * row);
            UiHelper.DrawFilledRect(sb, centerX - rowW / 2f, tipY - (h - 1) + row, rowW, 1, color);
        }
    }

    // Upward-pointing filled triangle: 1px tip at tipY (top), widest row at bottom.
    private void DrawUpArrow(SpriteBatch sb, float centerX, float tipY, int w, int h, Color color)
    {
        for (int row = 0; row < h; row++)
        {
            int rowW = Math.Max(1, w - 2 * (h - 1 - row));
            UiHelper.DrawFilledRect(sb, centerX - rowW / 2f, tipY + row, rowW, 1, color);
        }
    }

    private void DrawBar(SpriteBatch sb, float x, float y, float w, float h, float frac, Color fill)
    {
        if (frac < 0) return;
        UiHelper.DrawFilledRect(sb, x, y, w, h, UiHelper.BarBg);
        if (frac > 0)
            UiHelper.DrawFilledRect(sb, x, y, w * frac, h, fill);
    }
}
