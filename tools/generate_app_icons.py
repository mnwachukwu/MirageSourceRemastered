"""Generates the application icon family from one drawing routine.

Three icons, all the same mark — a lit tile on a dark grid, matching the site's favicon — telling
each other apart by a badge in the bottom-right corner:

    client      no badge        the plain mark is reserved for the game itself
    editor      a pencil        it authors content
    server      an open folder  it serves a world off disk

Each app's installer carries that same app's icon. There is deliberately no separate installer
mark: a fourth, disc-badged icon shared by all three made every Setup.exe look identical in a
downloads folder, which is the one place the distinction actually matters.

Drawn rather than rasterized. The mark is nine rounded rectangles and the badges are a handful of
polygons and circles, so Pillow can render it directly — which avoids depending on an SVG
rasterizer, and means the geometry lives here as numbers rather than in a file something else has
to parse.

Everything is drawn once at 1024px and downsampled with Lanczos to each target size. Pillow's
primitives are not anti-aliased, so drawing straight to 32px would produce visible stair-stepping;
supersampling is what makes the small sizes look drawn rather than crushed.

Run:  python tools/generate_app_icons.py
Out:  assets/icons/{client,editor,server}.{ico,png,icns}
      assets/icons/contact-sheet.png   (all three, every size, for eyeballing)

Regenerate after changing the palette. Nothing does this automatically, and the site's favicon
carries the same colors by hand — see the site repo's README.
"""

from __future__ import annotations

import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw

# ── Palette ───────────────────────────────────────────────────────────────────
# Matches the site: --accent #9aa8f5 on --bg #0b0e14, with --line #262e3d tiles.
PLATE = (11, 14, 20, 255)
DIM = (38, 46, 61, 255)
LIT = (154, 168, 245, 255)

# ── Geometry, in the favicon's 32-unit space ──────────────────────────────────
UNITS = 32
PLATE_RADIUS = 6

# The eight unlit tiles, then the lit one. Identical to public/favicon.svg in the site repo.
TILES = [
    (4, 4, 7, 7), (13, 4, 7, 7), (22, 4, 6, 7),
    (4, 13, 7, 7), (22, 13, 6, 7),
    (4, 22, 7, 6), (13, 22, 7, 6), (22, 22, 6, 6),
]
LIT_TILE = (13, 13, 7, 7)

# The badge sits on the bottom-right tile, drawn in the accent directly on the grid.
#
# An earlier version put the glyph on a filled circle, which made the badge half the width of the
# icon and turned every glyph into a silhouette fighting a hard circular edge. Without the plate the
# glyph can be about one tile across, and accent-on-tile carries enough contrast on its own.
BADGE_CX, BADGE_CY, BADGE_R = 25.0, 25.0, 4.3

SS = 1024 // UNITS  # supersample: 32 device pixels per unit

SIZES = [16, 24, 32, 48, 64, 128, 256, 512, 1024]
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]


def _u(v: float) -> float:
    """Unit space to supersampled device pixels."""
    return v * SS


def _poly(draw: ImageDraw.ImageDraw, points: list[tuple[float, float]], fill) -> None:
    draw.polygon([(_u(x), _u(y)) for x, y in points], fill=fill)


def _badge_local(dx: float, dy: float) -> tuple[float, float]:
    """Badge-local coordinates (-1..1 across the badge) to unit space."""
    return BADGE_CX + dx * BADGE_R, BADGE_CY + dy * BADGE_R


def _expand(points: list[tuple[float, float]], amount: float) -> list[tuple[float, float]]:
    """Grows a polygon about its centroid.

    Used to paint a slightly larger copy in the badge color underneath a shape, which leaves a gap
    around it. Two adjacent dark shapes on one badge otherwise merge into a blob at icon sizes —
    outlining is what keeps them legible as separate parts.
    """
    cx = sum(x for x, _ in points) / len(points)
    cy = sum(y for _, y in points) / len(points)
    return [(cx + (x - cx) * (1 + amount), cy + (y - cy) * (1 + amount)) for x, y in points]


def _draw_pencil(draw: ImageDraw.ImageDraw) -> None:
    """A pencil lying at 45°, tip toward the top-right.

    Built along an axis rather than as literal corner coordinates, so the proportions stay right if
    the badge is ever resized: `d` runs up-right along the pencil, `p` is across it.

    Kept narrow with a long taper on purpose. An earlier, fatter version with a stubby point read as
    a plain diagonal bar once it got down to 32px — the taper is most of what says "pencil".
    """
    dx, dy = math.cos(math.radians(-45)), math.sin(math.radians(-45))
    px, py = -dy, dx
    w = 0.30

    def at(t: float, s: float) -> tuple[float, float]:
        return _badge_local(dx * t + px * s, dy * t + py * s)

    # Body, from the blunt end to where the taper starts.
    _poly(draw, [at(-1.05, w), at(0.05, w), at(0.05, -w), at(-1.05, -w)], LIT)
    # A long tip — nearly half the pencil, which is most of what says "pencil" rather than "bar".
    _poly(draw, [at(0.05, w), at(1.10, 0), at(0.05, -w)], LIT)
    # The ferrule: a notch of plate color near the blunt end, reading as the metal collar. At icon
    # sizes it survives as a break in the silhouette, which is enough to stop it looking like a stick.
    _poly(draw, [at(-0.62, w), at(-0.44, w), at(-0.44, -w), at(-0.62, -w)], PLATE)


def _draw_folder(draw: ImageDraw.ImageDraw) -> None:
    """An open folder: a back panel with a tab, and a front flap leaning away from it.

    The flap is painted over an expanded copy of itself in the badge color, so a gap separates it
    from the back panel. Without that the two merge and the badge reads as an undifferentiated
    blob well before 32px.
    """
    back = [(-1.00, 0.30), (-1.00, -0.86), (-0.30, -0.86), (-0.08, -0.50),
            (0.74, -0.50), (0.74, 0.30)]
    _poly(draw, [_badge_local(x, y) for x, y in back], LIT)

    # Wider at the bottom than the top, which is what reads as "open" rather than as a second
    # rectangle sitting in front.
    flap = [(-0.74, -0.16), (0.90, -0.16), (1.14, 0.72), (-1.00, 0.72)]
    _poly(draw, [_badge_local(x, y) for x, y in _expand(flap, 0.16)], PLATE)
    _poly(draw, [_badge_local(x, y) for x, y in flap], LIT)


BADGES = {
    "client": None,
    "editor": _draw_pencil,
    "server": _draw_folder,
}


def render(variant: str) -> Image.Image:
    """Draws one icon at full supersampled resolution."""
    side = UNITS * SS
    img = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    draw.rounded_rectangle([0, 0, side - 1, side - 1], radius=_u(PLATE_RADIUS), fill=PLATE)

    for x, y, w, h in TILES:
        draw.rounded_rectangle([_u(x), _u(y), _u(x + w), _u(y + h)], radius=_u(1), fill=DIM)

    x, y, w, h = LIT_TILE
    draw.rounded_rectangle([_u(x), _u(y), _u(x + w), _u(y + h)], radius=_u(1), fill=LIT)

    badge = BADGES[variant]
    if badge is not None:
        badge(draw)

    # Clip to the plate. The badge is deliberately allowed to overflow the rounded corner while
    # drawing; masking afterwards trims it to the silhouette instead of leaving a bulge.
    mask = Image.new("L", (side, side), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, side - 1, side - 1], radius=_u(PLATE_RADIUS), fill=255
    )
    img.putalpha(Image.composite(img.getchannel("A"), Image.new("L", (side, side), 0), mask))
    return img


def scaled(master: Image.Image, size: int) -> Image.Image:
    return master.resize((size, size), Image.LANCZOS)


def write_window_bmp(path: Path, master: Image.Image, size: int = 256) -> None:
    """Writes the BMP MonoGame uses for the client's window and taskbar icon.

    Separate from everything else here because MonoGame does not read the executable's icon for
    this. `SdlGameWindow` looks for an embedded resource — `<EntryNamespace>.Icon.bmp`, then a bare
    `Icon.bmp` — and falls through to `MonoGame.bmp`, its own logo, embedded in the framework
    assembly. Ship neither and every window and taskbar button shows MonoGame's branding.

    Hand-rolled rather than left to Pillow because it has to be a **BITMAPV4HEADER** with explicit
    channel masks. The route in is `SDL_LoadBMP`, and a plain BITMAPINFOHEADER at 32bpp — which is
    what Pillow writes — leaves the fourth channel formally undefined; SDL is entitled to read it as
    padding and often does, which turns the mark's rounded corners into black ones. V4 states the
    alpha mask outright and removes the question.
    """
    icon = scaled(master, size)

    # Bottom-up rows, and BGRA per pixel to match the masks declared below.
    rows = []
    pixels = icon.load()
    for y in range(size - 1, -1, -1):
        row = bytearray()
        for x in range(size):
            r, g, b, a = pixels[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    bits = b"".join(rows)

    header = struct.pack(
        "<IiiHHIIiiII"      # BITMAPINFOHEADER
        "IIII"              # R/G/B/A masks
        "I36sIII",          # color space + endpoints + gamma
        108,                # header size — V4
        size, size,
        1, 32,
        3,                  # BI_BITFIELDS
        len(bits),
        2835, 2835,         # ~72 DPI
        0, 0,
        0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000,
        0x57696E20,         # 'Win ' — sRGB-ish, and what SDL expects to ignore
        b"\0" * 36,
        0, 0, 0,
    )
    offset = 14 + len(header)
    file_header = struct.pack("<2sIHHI", b"BM", offset + len(bits), 0, 0, offset)
    path.write_bytes(file_header + header + bits)


def write_icns(path: Path, master: Image.Image) -> None:
    """Writes a macOS .icns.

    The format is a trivial container — 'icns', total length, then typed chunks — and every type
    used here takes a PNG payload, so it can be assembled without Apple's iconutil (which only
    exists on macOS, and this has to run on the machine that builds).
    """
    import io

    types = {
        "icp4": 16, "icp5": 32, "icp6": 64,
        "ic07": 128, "ic08": 256, "ic09": 512, "ic10": 1024,
    }
    chunks = b""
    for code, size in types.items():
        buf = io.BytesIO()
        scaled(master, size).save(buf, format="PNG")
        data = buf.getvalue()
        chunks += code.encode("ascii") + struct.pack(">I", len(data) + 8) + data

    path.write_bytes(b"icns" + struct.pack(">I", len(chunks) + 8) + chunks)


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    out = root / "assets" / "icons"
    out.mkdir(parents=True, exist_ok=True)

    masters = {}
    for variant in BADGES:
        master = render(variant)
        masters[variant] = master

        # Windows: the executable icon and the Velopack setup icon.
        scaled(master, 256).save(
            out / f"{variant}.ico",
            format="ICO",
            sizes=[(s, s) for s in ICO_SIZES],
        )
        # Linux, and anything that wants a plain bitmap.
        scaled(master, 512).save(out / f"{variant}.png", format="PNG")
        # macOS bundles.
        write_icns(out / f"{variant}.icns", master)

        print(f"  {variant:<10} ico + png + icns")

    # The client's in-game window and taskbar icon. Written next to its csproj rather than into
    # assets/icons/, because MonoGame finds it as an embedded resource of that project and the
    # convention it documents is a file called Icon.bmp sitting there.
    window_icon = root / "client" / "src" / "Mirage.Client.Shell" / "Icon.bmp"
    write_window_bmp(window_icon, masters["client"])
    print(f"  {'client':<10} Icon.bmp  ->  {window_icon.relative_to(root)}")

    _contact_sheet(masters, out / "contact-sheet.png")
    print(f"\nwrote {len(BADGES) * 3 + 1} files to {out}")


def _contact_sheet(masters: dict[str, Image.Image], path: Path) -> None:
    """Every variant at every size on one strip, for judging the small sizes by eye."""
    sizes = [16, 24, 32, 48, 64, 128]
    pad, label = 12, 92
    width = label + sum(s + pad for s in sizes)
    height = pad + len(masters) * (max(sizes) + pad)

    sheet = Image.new("RGBA", (width, height), (16, 20, 29, 255))
    draw = ImageDraw.Draw(sheet)

    for row, (variant, master) in enumerate(masters.items()):
        y = pad + row * (max(sizes) + pad)
        draw.text((10, y + max(sizes) // 2 - 6), variant, fill=(231, 234, 240, 255))
        x = label
        for size in sizes:
            sheet.alpha_composite(scaled(master, size), (x, y + (max(sizes) - size) // 2))
            x += size + pad

    sheet.save(path, format="PNG")


if __name__ == "__main__":
    main()
