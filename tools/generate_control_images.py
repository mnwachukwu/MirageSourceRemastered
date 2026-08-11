#!/usr/bin/env python
"""
Generates the three controller-scheme reference images (Keyboard / Xbox / PlayStation)
used by the in-game Help. Each image is 800x600; the upper 470px holds the primary
controls (Move / Run / Attack / Pick Up / Cast / Cycle / Chat) and the bottom strip
holds the POTIONS hotkeys (1/2/3 on keyboard, trigger+face on gamepad). The image
has no header — the Controls panel already labels each scheme with a tab strip.

Run: python "generate_control_images(for C# project).py"
Output: ../client-csharp/src/Mirage.Client.Shell/assets/graphics/Controls{Keyboard,Xbox,Playstation}.png
"""
import os
from PIL import Image, ImageDraw, ImageFont

W, H = 800, 600

# ── Palette (matches the dark game-panel look) ───────────────────────────────
BG           = (12, 12, 22)
PANEL_BORDER = (74, 74, 108)
TITLE_COLOR  = (255, 210, 70)     # gold (retained for the POTIONS strip header)
ACTION_COLOR = (236, 236, 246)
SUB_COLOR    = (150, 150, 174)
GROUP_BG     = (20, 20, 38)
GROUP_BORDER = (92, 92, 134)
KEY_BORDER   = (12, 12, 20)

FONTS = "C:/Windows/Fonts/"
def f(name, size): return ImageFont.truetype(FONTS + name, size)
SECTION_F = f("consolab.ttf", 26)
ACTION_F  = f("consolab.ttf", 27)
SUB_F     = f("consola.ttf", 19)
KEY_F     = f("consolab.ttf", 30)
KEY_SM_F  = f("consolab.ttf", 18)
FACE_F    = f("consolab.ttf", 27)
PLUS_F    = f("consolab.ttf", 32)
FACE_R    = 26       # face-button radius (kept under the row pitch so buttons don't touch)

BTN_CX   = 168       # horizontal center of the (primary) button column
ACTION_X = 348       # left edge of the action text column


def cmid(d, x, y, text, font, fill):
    d.text((x, y), text, font=font, fill=fill, anchor="mm")

def cleft(d, x, y, text, font, fill):
    d.text((x, y), text, font=font, fill=fill, anchor="lm")


# ── Frame ────────────────────────────────────────────────────────────────────
# The image has no header — the in-game Controls panel already labels each
# scheme with its own tab strip ("Keyboard" / "Xbox" / "PlayStation"), so a
# duplicate title bar inside the picture was wasted vertical space.
def frame():
    img = Image.new("RGBA", (W, H), BG + (255,))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W - 1, H - 1], outline=PANEL_BORDER, width=3)
    return img, d


def group_box(d, x0, y0, x1, y1):
    d.rounded_rectangle([x0, y0, x1, y1], radius=10, fill=GROUP_BG, outline=GROUP_BORDER, width=2)


# ── Keyboard keycap ──────────────────────────────────────────────────────────
def keycap(d, cx, cy, w, h, label, fsize=30):
    x0, y0 = cx - w // 2, cy - h // 2
    x1, y1 = x0 + w, y0 + h
    base = (66, 66, 80); hi = (104, 104, 126); sh = (34, 34, 46)
    bev = 4
    d.rectangle([x0, y0, x1, y1], fill=base)
    d.rectangle([x0, y0, x1, y0 + bev], fill=hi)             # top highlight
    d.rectangle([x0, y0, x0 + bev, y1], fill=hi)             # left highlight
    d.rectangle([x0, y1 - bev, x1, y1], fill=sh)             # bottom shadow
    d.rectangle([x1 - bev, y0, x1, y1], fill=sh)             # right shadow
    d.rectangle([x0, y0, x1, y1], outline=KEY_BORDER, width=2)
    cmid(d, cx, cy + 1, label, f("consolab.ttf", fsize), (242, 242, 252))


def wasd(d, cx, cy):
    k = 52; gap = 6
    top = cy - (k + gap) // 2
    bot = cy + (k + gap) // 2
    keycap(d, cx, top, k, k, "W")
    keycap(d, cx - (k + gap), bot, k, k, "A")
    keycap(d, cx, bot, k, k, "S")
    keycap(d, cx + (k + gap), bot, k, k, "D")


# ── Gamepad parts ────────────────────────────────────────────────────────────
def dpad(d, cx, cy, s):
    arm = int(s * 0.30); L = int(s * 0.5)
    dark = (48, 48, 62); hi = (84, 84, 106)
    # dark border via a slightly larger plus underneath
    d.rectangle([cx - arm - 2, cy - L - 2, cx + arm + 2, cy + L + 2], fill=KEY_BORDER)
    d.rectangle([cx - L - 2, cy - arm - 2, cx + L + 2, cy + arm + 2], fill=KEY_BORDER)
    d.rectangle([cx - arm, cy - L, cx + arm, cy + L], fill=dark)
    d.rectangle([cx - L, cy - arm, cx + L, cy + arm], fill=dark)
    d.rectangle([cx - arm, cy - L, cx + arm, cy - L + 4], fill=hi)   # top edge highlight
    d.rectangle([cx - L, cy - arm, cx - L + 4, cy + arm], fill=hi)   # left edge highlight
    # arrow notches
    a = int(arm * 0.5)
    d.polygon([(cx, cy - L + 6), (cx - a, cy - L + 6 + a), (cx + a, cy - L + 6 + a)], fill=(150, 150, 175))
    d.polygon([(cx, cy + L - 6), (cx - a, cy + L - 6 - a), (cx + a, cy + L - 6 - a)], fill=(150, 150, 175))
    d.polygon([(cx - L + 6, cy), (cx - L + 6 + a, cy - a), (cx - L + 6 + a, cy + a)], fill=(150, 150, 175))
    d.polygon([(cx + L - 6, cy), (cx + L - 6 - a, cy - a), (cx + L - 6 - a, cy + a)], fill=(150, 150, 175))


def stick(d, cx, cy, r, label=None):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(26, 26, 38), outline=KEY_BORDER, width=3)
    rc = int(r * 0.62)
    d.ellipse([cx - rc, cy - rc, cx + rc, cy + rc], fill=(70, 70, 92), outline=(18, 18, 28), width=2)
    if label:
        # Replace the cosmetic highlight dot with an LS/RS marker on the stick cap.
        cmid(d, cx, cy + 1, label, f("consolab.ttf", max(12, int(rc * 0.95))), (236, 236, 246))
    else:
        hr = int(rc * 0.4); ox = int(rc * 0.42)
        d.ellipse([cx - ox - hr, cy - ox - hr, cx - ox + hr, cy - ox + hr], fill=(122, 122, 152))


def xbox_face(d, cx, cy, r, letter, color):
    dk = tuple(int(c * 0.5) for c in color)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color, outline=dk, width=3)
    lc = (22, 22, 28) if letter == "Y" else (255, 255, 255)
    cmid(d, cx, cy + 1, letter, FACE_F, lc)


def ps_face(d, cx, cy, r, symbol):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(34, 34, 46), outline=KEY_BORDER, width=3)
    s = int(r * 0.5)
    if symbol == "triangle":
        col = (88, 214, 156)
        pts = [(cx, cy - s), (cx - int(s * 0.92), cy + int(s * 0.62)), (cx + int(s * 0.92), cy + int(s * 0.62))]
        d.line(pts + [pts[0]], fill=col, width=4, joint="curve")
    elif symbol == "circle":
        col = (232, 84, 100)
        d.ellipse([cx - s, cy - s, cx + s, cy + s], outline=col, width=4)
    elif symbol == "cross":
        col = (96, 152, 232)
        d.line([cx - s, cy - s, cx + s, cy + s], fill=col, width=5)
        d.line([cx - s, cy + s, cx + s, cy - s], fill=col, width=5)
    elif symbol == "square":
        col = (232, 112, 182)
        d.rectangle([cx - s, cy - s, cx + s, cy + s], outline=col, width=4)


def pill(d, cx, cy, w, h, label, fill, fsize=18):
    x0, y0 = cx - w // 2, cy - h // 2
    d.rounded_rectangle([x0, y0, x0 + w, y0 + h], radius=h // 2, fill=fill, outline=KEY_BORDER, width=3)
    cmid(d, cx, cy + 1, label, f("consolab.ttf", fsize), (236, 236, 246))


# ── Potions strip (shared bottom band) ───────────────────────────────────────
STRIP_TOP = 470
STRIP_TITLE_Y = 496
STRIP_ROW_Y   = 540
STRIP_LABEL_Y = 585


def potions_divider(d):
    d.line([(20, STRIP_TOP), (W - 21, STRIP_TOP)], fill=GROUP_BORDER, width=2)


def potions_keyboard(d):
    potions_divider(d)
    cleft(d, 70, STRIP_TITLE_Y, "POTIONS", SECTION_F, TITLE_COLOR)
    # Three columns, centered in evenly spaced thirds of the canvas.
    items = [(165, "1", "HP Potion"), (430, "2", "MP Potion"), (695, "3", "SP Potion")]
    for cx, key, label in items:
        keycap(d, cx, STRIP_ROW_Y, 54, 46, key, 30)
        cmid(d, cx, STRIP_LABEL_Y, label, SUB_F, ACTION_COLOR)


def potions_gamepad(d, xbox, lt, rt, faces_potion):
    potions_divider(d)
    cleft(d, 70, STRIP_TITLE_Y, "POTIONS", SECTION_F, TITLE_COLOR)
    # Modifier reminder, inline with the title: "Hold [LT] or [RT]"
    cleft(d, 215, STRIP_TITLE_Y, "Hold", SUB_F, SUB_COLOR)
    trigger_fill = (44, 44, 60)
    pill(d, 305, STRIP_TITLE_Y, 70, 30, lt, trigger_fill, fsize=16)
    cleft(d, 350, STRIP_TITLE_Y, "or", SUB_F, SUB_COLOR)
    pill(d, 410, STRIP_TITLE_Y, 70, 30, rt, trigger_fill, fsize=16)
    # Three columns of "+" + face_button + label, evenly spaced.
    centers = [165, 430, 695]
    labels  = ["HP Potion", "MP Potion", "SP Potion"]
    for cx, face_btn, label in zip(centers, faces_potion, labels):
        cmid(d, cx - 50, STRIP_ROW_Y, "+", PLUS_F, ACTION_COLOR)
        if xbox:
            letter, color = face_btn
            xbox_face(d, cx, STRIP_ROW_Y, FACE_R, letter, color)
        else:
            ps_face(d, cx, STRIP_ROW_Y, FACE_R, face_btn)
        cmid(d, cx, STRIP_LABEL_Y, label, SUB_F, ACTION_COLOR)


# ── Builders ─────────────────────────────────────────────────────────────────
def build_keyboard():
    img, d = frame()
    # Move (WASD cluster).  Ctrl+WASD turns the same keys into a face-only input,
    # so it rides as a sub-line under "Move" instead of consuming a new row.
    group_box(d, 70, 16, 266, 134)
    wasd(d, BTN_CX, 75)
    cleft(d, ACTION_X, 75 - 11, "Move", ACTION_F, ACTION_COLOR)
    cleft(d, ACTION_X, 75 + 13, "(Hold Ctrl: face only)", SUB_F, SUB_COLOR)
    rows = [
        (165, ("key", "Shift", 120, 46, 24), "Run", "(Hold)"),
        (220, ("key", "E", 54, 46, 30), "Attack", None),
        (275, ("key", "F", 54, 46, 30), "Pick Up", None),
        (330, ("key", "Q", 54, 46, 30), "Cast Prepared Spell", "(+Ctrl Self)"),
        (385, ("key", "Tab", 84, 46, 24), "Cycle Target", "(+Shift Reverse, +Ctrl Self)"),
        (440, ("key", "Enter", 112, 46, 24), "Chat", None),
    ]
    for cy, btn, action, sub in rows:
        _, label, w, h, fs = btn
        keycap(d, BTN_CX, cy, w, h, label, fs)
        if sub:
            cleft(d, ACTION_X, cy - 11, action, ACTION_F, ACTION_COLOR)
            cleft(d, ACTION_X, cy + 13, sub, SUB_F, SUB_COLOR)
        else:
            cleft(d, ACTION_X, cy, action, ACTION_F, ACTION_COLOR)
    potions_keyboard(d)
    return img


def build_gamepad(scheme):
    xbox = scheme == "xbox"
    img, d = frame()

    lb, lt, rb, rt = ("LB", "LT", "RB", "RT") if xbox else ("L1", "L2", "R1", "R2")

    # Move (D-pad + left stick) on top; right stick (face-only) on a second row.
    # Sticks are r=28 and the right-stick row rides high enough that the group-box
    # bottom (y=128) clears the first face button (Run, top y=132) just below it.
    # The face-button column underneath is a tight fit between this box and the
    # shoulder box, so the Move box must not bleed down into it. LS/RS markers live
    # on each stick cap.
    group_box(d, 70, 12, 272, 128)
    dpad(d, 124, 52, 72)
    stick(d, 216, 52, 28, "LS")
    cleft(d, ACTION_X, 52, "Move", ACTION_F, ACTION_COLOR)
    stick(d, 216, 96, 28, "RS")
    cleft(d, ACTION_X, 96, "Face Direction", ACTION_F, ACTION_COLOR)

    # Face buttons -> run / attack / pickup / cast.
    if xbox:
        faces = [
            (158, ("B", (214, 69, 59)),  "Run", "(Hold)"),
            (213, ("X", (59, 124, 214)), "Attack", None),
            (268, ("A", (107, 191, 58)), "Pick Up", None),
            (323, ("Y", (230, 185, 59)), "Cast Prepared Spell", None),
        ]
        # Potion combos: X (attack→HP), Y (cast→MP), B (run→SP) — face button mirrors the action that drains the resource.
        faces_potion = [("X", (59, 124, 214)), ("Y", (230, 185, 59)), ("B", (214, 69, 59))]
    else:
        faces = [
            (158, "circle",   "Run", "(Hold)"),
            (213, "square",   "Attack", None),
            (268, "cross",    "Pick Up", None),
            (323, "triangle", "Cast Prepared Spell", None),
        ]
        # Potion combos: Square (attack→HP), Triangle (cast→MP), Circle (run→SP).
        faces_potion = ["square", "triangle", "circle"]
    for cy, btn, action, sub in faces:
        if xbox:
            letter, color = btn
            xbox_face(d, BTN_CX, cy, FACE_R, letter, color)
        else:
            ps_face(d, BTN_CX, cy, FACE_R, btn)
        if sub:
            cleft(d, ACTION_X, cy - 11, action, ACTION_F, ACTION_COLOR)
            cleft(d, ACTION_X, cy + 13, sub, SUB_F, SUB_COLOR)
        else:
            cleft(d, ACTION_X, cy, action, ACTION_F, ACTION_COLOR)

    # Shoulders -> next / prev target, with LB+RB held together for self target.
    # Triggers are reserved for the potion combo, so they no longer appear in the
    # cycle-target group.
    group_box(d, 70, 354, 220, 466)
    bumper = (62, 62, 80)
    pill(d, 145, 372, 90, 34, lb, bumper)
    pill(d, 145, 410, 90, 34, rb, bumper)
    # Combo: smaller LB and RB pills with a "+" between, on the third row.
    pill(d, 110, 446, 50, 28, lb, bumper, fsize=14)
    cmid(d, 145, 446, "+", PLUS_F, ACTION_COLOR)
    pill(d, 180, 446, 50, 28, rb, bumper, fsize=14)
    cleft(d, ACTION_X, 372, "Next Target", ACTION_F, ACTION_COLOR)
    cleft(d, ACTION_X, 410, "Prev Target", ACTION_F, ACTION_COLOR)
    cleft(d, ACTION_X, 446, "Target Self", ACTION_F, ACTION_COLOR)

    potions_gamepad(d, xbox, lt, rt, faces_potion)
    return img


def main():
    out = os.path.join(os.path.dirname(__file__), "..", "client-csharp", "src", "Mirage.Client.Shell", "assets", "graphics")
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)
    build_keyboard().save(os.path.join(out, "ControlsKeyboard.png"))
    build_gamepad("xbox").save(os.path.join(out, "ControlsXbox.png"))
    build_gamepad("playstation").save(os.path.join(out, "ControlsPlaystation.png"))
    print("Wrote Controls{Keyboard,Xbox,Playstation}.png to", out)


if __name__ == "__main__":
    main()
