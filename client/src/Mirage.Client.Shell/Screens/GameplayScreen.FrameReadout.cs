using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>
/// The frame readout, stacked upward from just above the action bar. Toggled with <c>/fps</c>.
///
/// <para>Everyone sees the rate. A Developer or above also sees the DISTRIBUTION and the catch-up counters,
/// because a rate cannot show a stutter: one 100 ms frame a second moves 60 fps to 58, which reads as fine.
/// The median says how it runs, the 99th and the worst say how it hitches, and the catch-up count says
/// whether the fixed timestep is compounding an overrun into the frames after it.</para>
/// </summary>
public sealed partial class GameplayScreen
{
    private const int ReadoutGap = 4;
    private static readonly Color ReadoutRate = new(210, 214, 224);
    private static readonly Color ReadoutDetail = new(150, 156, 172);
    private static readonly Color ReadoutAlarm = new(226, 140, 90);

    // Rebuilt four times a second, not sixty. The bands describe ten seconds, so a faster refresh says
    // nothing new — and formatting six lines every frame is enough allocation to show up in the KB/s the
    // readout itself is reporting.
    private const double ReadoutRefreshMs = 250;
    private readonly List<(string Text, Color Color)> _readoutLines = [];
    private double _readoutBuiltMs = double.NegativeInfinity;
    private float _readoutWidth;

    /// <summary>
    /// Where the block starts, so its RIGHT edge never runs off the screen.
    ///
    /// <para>🔴 The action bar sits in the bottom-right corner — its left edge is at 583 of 800 — so anchoring
    /// text there leaves about two hundred pixels before the screen ends, and the longest line needs half as
    /// much again. Lines share a left edge (ragged-left is unreadable) and the block's right edge lands on the
    /// bar's, so it grows leftward into the room it needs and a longer translation costs nothing.</para>
    /// </summary>
    internal static float ReadoutLeft(float barLeft, float barRight, float widest)
        => MathF.Max(0f, MathF.Min(barLeft, barRight - widest));

    private void DrawFrameReadout(SpriteBatch sb, SpriteFont font, double nowMs)
    {
        var state = _ctx.State;
        if (!state.ShowFps) return;

        if (nowMs - _readoutBuiltMs >= ReadoutRefreshMs)
        {
            _readoutBuiltMs = nowMs;
            BuildFrameReadout(state);
            _readoutWidth = 0f;
            foreach (var (text, _) in _readoutLines)
                _readoutWidth = MathF.Max(_readoutWidth, font.MeasureString(text).X);
        }

        float x = ReadoutLeft(HotkeyBarPanel.Bounds.Left, HotkeyBarPanel.Bounds.Right, _readoutWidth);
        float y = HotkeyBarPanel.Bounds.Top - ReadoutGap;
        for (int i = _readoutLines.Count - 1; i >= 0; i--)
        {
            var size = font.MeasureString(_readoutLines[i].Text);
            y -= size.Y;
            var at = new Vector2(x, y);
            // A one-pixel drop shadow, since this sits over whatever the world happens to be showing.
            sb.DrawString(font, _readoutLines[i].Text, at + new Vector2(1, 1), Color.Black * 0.75f);
            sb.DrawString(font, _readoutLines[i].Text, at, _readoutLines[i].Color);
        }
    }

    private void BuildFrameReadout(Mirage.Client.Core.State.ClientState state)
    {
        // Built bottom-up, then drawn upward from the bar, so adding a line pushes the stack away from the
        // action bar rather than over it.
        var lines = _readoutLines;
        lines.Clear();
        lines.Add((ClientStrings.Format(ClientStrings.ChatPanel_FpsDisplay, ("Fps", state.GameFps)), ReadoutRate));

        if (state.Me.Access >= AdminLevel.Developer)
        {
            var s = state.Frames.Take();
            if (s.Frames > 0)
            {
                lines.Add((ClientStrings.Format(ClientStrings.Hud_FrameBands,
                    ("P50", Ms(s.Frame.P50)), ("P99", Ms(s.Frame.P99)), ("Max", Ms(s.Frame.Max))),
                    s.Frame.Max >= StutterMs ? ReadoutAlarm : ReadoutDetail));
                // Draw's own worst next to the frame's says whether a spike happened INSIDE the draw or
                // between two of them — the difference between work and a stall.
                lines.Add((ClientStrings.Format(ClientStrings.Hud_FrameHalves,
                    ("Update", Ms(s.Update.P99)), ("Draw", Ms(s.Draw.P99)), ("DrawMax", Ms(s.Draw.Max))),
                    s.Draw.P99 >= BudgetMs ? ReadoutAlarm : ReadoutDetail));
                // The light passes on their own line: they are the part of a draw that scales with how many
                // emitters are on screen, and a draw that only says "slow" cannot say which half.
                lines.Add((ClientStrings.Format(ClientStrings.Hud_FrameLight,
                    ("Light", Ms(s.Light.P99)), ("LightMax", Ms(s.Light.Max))),
                    s.Light.P99 >= BudgetMs / 2 ? ReadoutAlarm : ReadoutDetail));
                // Windowed first, session total in brackets: the first says whether it is happening, the
                // second only that it once did.
                lines.Add((ClientStrings.Format(ClientStrings.Hud_FrameCatchUp,
                    ("Frames", s.CatchUpFrames), ("Updates", s.ExtraUpdates),
                    ("Total", s.TotalCatchUpFrames), ("Slow", s.SlowFrames)),
                    s.CatchUpFrames > 0 ? ReadoutAlarm : ReadoutDetail));
                lines.Add((ClientStrings.Format(ClientStrings.Hud_FrameWorst,
                    ("Total", Ms(s.Worst.TotalMs)), ("Draw", Ms(s.Worst.DrawMs)),
                    ("Update", Ms(s.Worst.UpdateMs)),
                    ("Gc", $"{s.Worst.Gen0}/{s.Worst.Gen1}/{s.Worst.Gen2}")),
                    s.Worst.Gen2 > 0 ? ReadoutAlarm : ReadoutDetail));
                // Windowed collections and the churn driving them, with the session totals behind. A rate
                // can be judged; a total that only ever climbs cannot.
                lines.Add((ClientStrings.Format(ClientStrings.Hud_FrameGc,
                    ("Gen0", s.WindowGen0), ("Gen2", s.WindowGen2), ("Kb", s.KbPerSecond.ToString("0")),
                    ("Total", $"{s.Gen0}/{s.Gen1}/{s.Gen2}")),
                    s.WindowGen2 > 0 ? ReadoutAlarm : ReadoutDetail));
            }
        }
    }

    /// <summary>Where a frame stops being a frame and starts being a hitch. Twice the 60 fps budget: at that
    /// point the display has already missed a refresh, which is the first moment anything is visible.</summary>
    private const double StutterMs = 33.4;

    /// <summary>One refresh at 60 Hz. A draw whose 99th sits above this is over budget before anything has
    /// gone wrong, which is a different problem from a spike and wants saying separately.</summary>
    private const double BudgetMs = 16.67;

    // Two decimals below ten, one above: the interesting frames are the small ones, and a worst frame of
    // 180 does not need a hundredth.
    private static string Ms(double ms) => ms < 10 ? ms.ToString("0.00") : ms.ToString("0.0");
}
