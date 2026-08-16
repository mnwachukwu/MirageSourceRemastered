using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// The action bar: four slots, each holding an item or a spell, bound by NUMBER (see
/// <see cref="PlayerHotkey"/>) and fired with 1–4 or the gamepad's face buttons under a trigger.
///
/// <para>Not an <c>IGamePanel</c> — it is chrome, not a window. It never opens or closes, never takes
/// focus, and never blocks movement; it draws in the sidebar strip just above the Mail/Options/Help
/// links and only ever claims a right-click that actually lands on one of its four boxes.</para>
///
/// <para>Everything it shows is derived from <see cref="ClientState"/> each frame rather than cached: a
/// bound slot goes gray the moment the bag runs out and colors again when it is restocked, with no
/// invalidation to get wrong.</para>
/// </summary>
public static class HotkeyBarPanel
{
    public const int IconSize = 32;
    private const int Gap = 6;
    // Sit the row directly above the link strip HudPanel lays out, with the same breathing gap the
    // links themselves use, so the two read as one stack in the sidebar rather than two stray widgets.
    private const int BarBottomGap = 6;
    private const int BarY = HudPanel.LinkStripY - IconSize - BarBottomGap;
    private const int BarW = Constants.MaxHotkeys * IconSize + (Constants.MaxHotkeys - 1) * Gap;
    private const int BarX = HudPanel.LinkStripCenterX - BarW / 2;

    // The key badge sits in the box's bottom-right corner, on its own dark plate so a digit stays legible
    // over a busy icon.
    private const int BadgeW = 11, BadgeH = 11;

    // Cooldown sweep: a fan of thin spokes from the box center, since SpriteBatch draws quads and a
    // genuine pie wedge would need geometry. 48 spokes over a 32px box leaves no visible gaps.
    private const int SweepSpokes = 48;

    private static readonly Color EmptyFill = new(18, 22, 24, 200);
    private static readonly Color BoundFill = new(28, 34, 36, 210);
    private static readonly Color UnavailableTint = new(70, 70, 70);
    private static readonly Color CooldownVeil = new(6, 8, 10, 165);
    private static readonly Color BadgePlate = new(0, 0, 0, 190);
    private static readonly Color BookCover = new(122, 74, 46);
    private static readonly Color BookPages = new(226, 220, 202);
    private static readonly Color BookSpine = new(72, 42, 26);

    /// <summary>Screen rect of one 1-based slot.</summary>
    public static Rectangle SlotBounds(int slot) =>
        new(BarX + (slot - 1) * (IconSize + Gap), BarY, IconSize, IconSize);

    /// <summary>The whole bar, for hit-testing "is the mouse over the bar at all".</summary>
    public static Rectangle Bounds => new(BarX, BarY, BarW, IconSize);

    /// <summary>The 1-based slot under a point, or 0.</summary>
    public static int SlotAt(Point p)
    {
        for (int i = 1; i <= Constants.MaxHotkeys; i++)
            if (SlotBounds(i).Contains(p)) return i;
        return 0;
    }

    // ── Resolution: a bound NUMBER to a live slot ────────────────────────────
    // Both of these are the reason hotkeys store numbers. They run per frame for drawing and again on
    // use; they are linear scans over 24-ish entries, which is nothing next to a draw call.

    /// <summary>First inventory slot holding this item number, or 0 when the bag has none.</summary>
    public static int FindInvSlot(ClientState state, int itemNum)
    {
        var me = state.Me;
        if (me?.Inv is null || itemNum <= 0) return 0;
        for (int i = 1; i <= Constants.MaxInv && i < me.Inv.Length; i++)
            if (me.Inv[i]?.Num == itemNum) return i;
        return 0;
    }

    /// <summary>Spellbook slot holding this spell number, or 0 when it is not known.</summary>
    public static int FindSpellSlot(ClientState state, int spellNum)
    {
        var me = state.Me;
        if (me?.Spell is null || spellNum <= 0) return 0;
        for (int i = 1; i <= Constants.MaxPlayerSpells && i < me.Spell.Length; i++)
            if (me.Spell[i] == spellNum) return i;
        return 0;
    }

    /// <summary>Whether the slot's binding can be acted on right now — the item is in the bag, or the
    /// spell is still known. Drives the gray/color state and is re-read every frame.</summary>
    public static bool IsAvailable(ClientState state, PlayerHotkey hk) => hk.Kind switch
    {
        HotkeyKind.Item => FindInvSlot(state, hk.Num) > 0,
        HotkeyKind.Spell => FindSpellSlot(state, hk.Num) > 0,
        _ => false,
    };

    // ── Draw ─────────────────────────────────────────────────────────────────

    /// <param name="cooldownFraction">How much of the shared 1s beat is still to run, 1→0. The cooldown is
    /// global across all four slots, so one value covers the row.</param>
    public static void Draw(SpriteBatch sb, SpriteFont font, ClientState state, Texture2D? itemsTex,
                            float cooldownFraction, bool gamepadActive, InputState input, bool canHover)
    {
        var me = state.Me;
        if (me?.Hotkeys is null) return;

        for (int slot = 1; slot <= Constants.MaxHotkeys; slot++)
        {
            var box = SlotBounds(slot);
            var hk = slot < me.Hotkeys.Length ? me.Hotkeys[slot] : PlayerHotkey.Empty;
            bool available = hk.IsBound && IsAvailable(state, hk);

            UiHelper.DrawFilledRect(sb, box, hk.IsBound ? BoundFill : EmptyFill);

            if (hk.IsBound)
            {
                // Unavailable draws the same art dimmed rather than blank: the player needs to see WHICH
                // potion they have run out of, not merely that the slot is unusable.
                var tint = available ? Color.White : UnavailableTint;
                if (hk.Kind == HotkeyKind.Item) DrawItemIcon(sb, box, state, itemsTex, hk.Num, tint);
                else DrawBookIcon(sb, box, tint);
            }

            UiHelper.DrawBorder(sb, box, UiHelper.UiControlBorder);
            DrawKeyBadge(sb, font, box, slot, gamepadActive);
        }

        // The sweep goes on last so it veils every slot uniformly — it is one shared beat, not four.
        if (cooldownFraction > 0f)
            for (int slot = 1; slot <= Constants.MaxHotkeys; slot++)
                DrawCooldownSweep(sb, SlotBounds(slot), cooldownFraction);

        // The trigger modifier is a property of the GROUP, not of any one slot, so it is labeled once to
        // the left of the row rather than repeated on all four badges.
        if (gamepadActive)
        {
            string mod = ClientStrings.Get(ClientStrings.HotkeyBar_GamepadModifier);
            var size = font.MeasureString(mod);
            sb.DrawString(font, mod, new Vector2(BarX - size.X - Gap, BarY + (IconSize - size.Y) / 2f), Color.LightGray);
        }

        if (canHover) NotifyHover(state, input, itemsTex);
    }

    private static void DrawItemIcon(SpriteBatch sb, Rectangle box, ClientState state, Texture2D? itemsTex, int itemNum, Color tint)
    {
        if (itemsTex is null || itemNum <= 0 || itemNum >= state.Items.Length) return;
        int pic = state.Items[itemNum]?.Pic ?? -1;
        if (pic < 0) return;
        sb.Draw(itemsTex, box, Rendering.ItemAtlas.GetSourceRect((short)pic), tint);
    }

    /// <summary>A spell has no art of its own, so every spell slot shows the same book and the hover
    /// tooltip says which one. Keeps the bar readable at 32px, where distinct glyphs would not be.</summary>
    private static void DrawBookIcon(SpriteBatch sb, Rectangle box, Color tint)
    {
        int pad = 5;
        var cover = new Rectangle(box.X + pad, box.Y + pad, box.Width - pad * 2, box.Height - pad * 2);
        UiHelper.DrawFilledRect(sb, cover, Multiply(BookCover, tint));
        // Page block inset from the fore-edge, and a darker spine down the left.
        var pages = new Rectangle(cover.X + 5, cover.Y + 3, cover.Width - 8, cover.Height - 6);
        UiHelper.DrawFilledRect(sb, pages, Multiply(BookPages, tint));
        var spine = new Rectangle(cover.X, cover.Y, 4, cover.Height);
        UiHelper.DrawFilledRect(sb, spine, Multiply(BookSpine, tint));
    }

    private static Color Multiply(Color c, Color tint) =>
        new(c.R * tint.R / 255, c.G * tint.G / 255, c.B * tint.B / 255, c.A);

    private static void DrawKeyBadge(SpriteBatch sb, SpriteFont font, Rectangle box, int slot, bool gamepadActive)
    {
        var plate = new Rectangle(box.Right - BadgeW - 1, box.Bottom - BadgeH - 1, BadgeW, BadgeH);
        UiHelper.DrawFilledRect(sb, plate, BadgePlate);

        if (gamepadActive && GamepadGlyphs.PreferPlayStation)
        {
            GamepadGlyphs.DrawPlayStationFace(sb, plate, PlayStationFace(slot));
            return;
        }

        string label = gamepadActive ? GamepadFace(slot) : slot.ToString();
        var size = font.MeasureString(label);
        sb.DrawString(font, label,
            new Vector2(plate.X + (plate.Width - size.X) / 2f, plate.Y + (plate.Height - size.Y) / 2f),
            Color.White);
    }

    /// <summary>Slot to face button: 1→X, 2→Y, 3→B, 4→A. Not arbitrary — X/Y/B are the legacy HP/MP/SP
    /// potion buttons, so existing muscle memory carries over, and the fourth slot takes A (still plain
    /// pickup without the trigger held).</summary>
    public static string GamepadFace(int slot) => slot switch { 1 => "X", 2 => "Y", 3 => "B", _ => "A" };

    /// <summary>The same four physical positions in Sony's vocabulary: X→Square, Y→Triangle, B→Circle,
    /// A→Cross. One mapping, two names — the button under the thumb never moves.</summary>
    public static GamepadGlyphs.PsFace PlayStationFace(int slot) => slot switch
    {
        1 => GamepadGlyphs.PsFace.Square,
        2 => GamepadGlyphs.PsFace.Triangle,
        3 => GamepadGlyphs.PsFace.Circle,
        _ => GamepadGlyphs.PsFace.Cross,
    };

    /// <summary>The dimming veil for the shared cooldown, drawn as a spoke fan sweeping clockwise from
    /// twelve o'clock. <paramref name="fraction"/> is how much is LEFT, so the veil retreats and the icon
    /// returns to color as the beat runs out.</summary>
    private static void DrawCooldownSweep(SpriteBatch sb, Rectangle box, float fraction)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        var center = new Vector2(box.Center.X, box.Center.Y);
        // Reach the corners, or the veil would leave four lit triangles behind.
        float radius = MathF.Sqrt(box.Width * box.Width + box.Height * box.Height) / 2f + 1f;
        int spokes = Math.Max(1, (int)MathF.Ceiling(SweepSpokes * fraction));
        float sweep = MathF.Tau * fraction;
        // Thick enough that neighboring spokes overlap at the rim rather than fanning into stripes.
        float thickness = radius * sweep / spokes + 2f;
        for (int i = 0; i <= spokes; i++)
        {
            float a = -MathF.PI / 2f + sweep * i / spokes;
            var tip = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            UiHelper.DrawLine(sb, center, tip, CooldownVeil, thickness);
        }
    }

    // ── Hover ────────────────────────────────────────────────────────────────

    private static void NotifyHover(ClientState state, InputState input, Texture2D? itemsTex)
    {
        var me = state.Me;
        if (me?.Hotkeys is null) return;
        int slot = SlotAt(input.MousePosition);
        if (slot == 0) return;

        var hk = slot < me.Hotkeys.Length ? me.Hotkeys[slot] : PlayerHotkey.Empty;
        if (!hk.IsBound)
        {
            Tooltip.NotifyHoverText(TooltipScope, (TooltipScope, slot),
                ClientStrings.Get(ClientStrings.HotkeyBar_EmptyHint), input.MousePosition);
            return;
        }

        if (hk.Kind == HotkeyKind.Item && hk.Num > 0 && hk.Num < state.Items.Length)
        {
            // Reuse the real item tooltip so a hotkeyed weapon reads exactly as it does in the bag —
            // requirements, mitigation and all — rather than getting a second, thinner description.
            // The live inventory slot (when there is one) carries durability and stack size.
            if (state.Items[hk.Num] is { } item)
            {
                int inv = FindInvSlot(state, hk.Num);
                Tooltip.NotifyHoverItem(TooltipScope, (TooltipScope, slot), item,
                    inv > 0 ? me.Inv[inv] : null, me, state.Classes, itemsTex, input.MousePosition);
            }
        }
        else if (hk.Kind == HotkeyKind.Spell && hk.Num > 0 && hk.Num < state.SpellDefs.Length)
        {
            // The whole point of the shared book glyph: the tooltip is what says which spell this is.
            if (state.SpellDefs[hk.Num] is { } spell)
                Tooltip.NotifyHoverSpell(TooltipScope, (TooltipScope, slot), spell,
                    me, state.Classes, state.Items, state.Weather, input.MousePosition);
        }
    }

    private const string TooltipScope = "hotkeybar";
}
