#!/usr/bin/env python3
"""Generate the QSPP Manager macOS app icon: deep-green rounded square,
cream shield with a check, matching the app's design tokens (#23614D / #F5F5EB).

Run from the repo root:  python3 scripts/make-icon.py
Produces assets/art/QsppManager.icns (via iconutil) plus intermediate appiconset.
Requires: Pillow, macOS iconutil.
"""
from pathlib import Path

from PIL import Image, ImageDraw

GREEN = (35, 97, 77)        # Brush.Green #23614D
GREEN_TOP = (43, 116, 92)   # subtle top highlight
CREAM = (245, 245, 235)     # Brush.Paper #F5F5EB
SIZE = 1024
REPO = Path(__file__).resolve().parents[1]
ART = REPO / "assets" / "art"


def rounded_square(draw: ImageDraw.ImageDraw, scale: float) -> None:
    """macOS-style rounded square inset to Apple's grid (left/right/top/bottom 10%)."""
    pad = int(SIZE * 0.10)
    r = int(SIZE * 0.2243)
    box = (pad, pad, SIZE - pad, SIZE - pad)
    draw.rounded_rectangle(box, radius=r, fill=GREEN)


def shield_points(cx: float, cy: float, w: float, h: float) -> list[tuple[float, float]]:
    """Shield silhouette centred at (cx, cy): rounded top corners, tapering to a point."""
    pts: list[tuple[float, float]] = []
    x0, x1 = cx - w / 2, cx + w / 2
    y0, y1 = cy - h / 2, cy + h / 2
    # top edge
    pts += [(x0 + w * 0.08, y0), (x1 - w * 0.08, y0)]
    # right side slightly bowed
    pts += [(x1, y0 + h * 0.16), (x1, y1 - h * 0.45)]
    # right taper curve to the bottom point (approximated)
    steps = 14
    for i in range(1, steps + 1):
        t = i / steps
        bx = x1 - (x1 - cx) * (t ** 1.35)
        by = (y1 - h * 0.45) + (y1 - (y1 - h * 0.45)) * (t ** 0.85)
        pts += [(bx, by)]
    # left taper mirrored (bottom point is shared)
    for i in range(steps, 0, -1):
        t = i / steps
        bx = x0 + (cx - x0) * (t ** 1.35)
        by = (y1 - h * 0.45) + (y1 - (y1 - h * 0.45)) * (t ** 0.85)
        pts += [(bx, by)]
    pts += [(x0, y1 - h * 0.45), (x0, y0 + h * 0.16)]
    return pts


def shield_and_check(layer: Image.Image) -> None:
    d = ImageDraw.Draw(layer)
    pts = shield_points(cx=SIZE / 2, cy=SIZE / 2, w=SIZE * 0.44, h=SIZE * 0.56)
    # shield outline ring: draw cream shield then inner green to leave a ring
    d.polygon(pts, fill=CREAM)
    inner = shield_points(cx=SIZE / 2, cy=SIZE / 2 - SIZE * 0.004, w=SIZE * 0.40, h=SIZE * 0.52)
    d.polygon(inner, fill=GREEN)
    # check mark, thick strokes
    cx, cy = SIZE / 2, SIZE / 2 - SIZE * 0.006
    a = (cx - SIZE * 0.095, cy + SIZE * 0.010)
    b = (cx - SIZE * 0.022, cy + SIZE * 0.082)
    c = (cx + SIZE * 0.102, cy - SIZE * 0.082)
    d.line([a, b], fill=CREAM, width=int(SIZE * 0.052))
    d.line([b, c], fill=CREAM, width=int(SIZE * 0.052))
    # round the joints
    r = int(SIZE * 0.026)
    for p in (a, b, c):
        d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=CREAM)


def build_size(px: int) -> Image.Image:
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    rounded_square(ImageDraw.Draw(layer), 1.0)
    shield_and_check(layer)
    return layer.resize((px, px), Image.LANCZOS)


def main() -> None:
    ART.mkdir(parents=True, exist_ok=True)
    sizes = [16, 32, 64, 128, 256, 512, 1024]
    iconset = ART / "QsppManager.iconset"
    iconset.mkdir(parents=True, exist_ok=True)
    master = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    rounded_square(ImageDraw.Draw(master), 1.0)
    shield_and_check(master)
    master.save(ART / "QsppManager-1024.png")
    for px in sizes:
        build_size(px).save(iconset / f"icon_{px}x{px}.png")
        if px <= 512:
            build_size(px * 2).save(iconset / f"icon_{px}x{px}@2x.png")
    import subprocess
    icns = ART / "QsppManager.icns"
    subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o", str(icns)], check=True)
    print(f"icns written: {icns}")


if __name__ == "__main__":
    main()