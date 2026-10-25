#!/usr/bin/env python3
"""Generate the Hugh's QSPP Folio macOS app icon from the client avatar logo.

Deep-green rounded square (brand #23614D) with the pharmacist avatar in a
cream disc — the avatar's green shirt sits nearly on the brand tone, so the
portrait blends into the palette naturally.

Run from the repo root:  python3 scripts/make-icon.py
Reads assets/art/HughLogo.png (square source). Produces assets/art/QsppManager.icns
(via iconutil) plus intermediate appiconset and QsppManager-1024.png.
Requires: Pillow, macOS iconutil.
"""
from pathlib import Path
import sys

from PIL import Image, ImageDraw, ImageOps

GREEN = (35, 97, 77)        # Brush.Green #23614D
GREEN_TOP = (43, 116, 92)   # subtle top highlight (unused, kept for palette)
CREAM = (245, 245, 235)     # Brush.Paper #F5F5EB
SIZE = 1024
REPO = Path(__file__).resolve().parents[1]
ART = REPO / "assets" / "art"
LOGO = ART / "HughLogo.png"


def rounded_square(layer: Image.Image) -> None:
    """macOS-style rounded square inset to Apple's grid (left/right/top/bottom 10%)."""
    d = ImageDraw.Draw(layer)
    pad = int(SIZE * 0.10)
    r = int(SIZE * 0.2243)
    box = (pad, pad, SIZE - pad, SIZE - pad)
    d.rounded_rectangle(box, radius=r, fill=GREEN)


def avatar_crop(diameter: int) -> Image.Image:
    """Circular crop of the avatar's face from the source logo, at `diameter` px."""
    src = Image.open(LOGO).convert("RGBA")
    w, h = src.size
    side = min(w, h)
    if w != h:
        src = ImageOps.fit(src, (side, side), Image.LANCZOS)
    # Face occupies roughly x 21%..82%, y 1%..64% of the frame → circle centre/radius:
    cx, cy, r = int(side * 0.517), int(side * 0.340), int(side * 0.360)
    box = (cx - r, cy - r, cx + r, cy + r)
    face = src.crop(box)
    # Scale so the crop fills the requested diameter.
    face = face.resize((diameter, diameter), Image.LANCZOS)
    mask = Image.new("L", (diameter * 4, diameter * 4), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, diameter * 4 - 1, diameter * 4 - 1), fill=255)
    mask = mask.resize((diameter, diameter), Image.LANCZOS)
    face.putalpha(mask)
    return face


def avatar_disc(layer: Image.Image) -> None:
    """Cream disc (breathing ring) + the avatar circle, centred on the squircle."""
    cx = cy = SIZE // 2
    d = ImageDraw.Draw(layer)
    ring = int(SIZE * 0.352)       # cream disc radius
    d.ellipse((cx - ring, cy - ring, cx + ring, cy + ring), fill=CREAM)
    disc = int(SIZE * 0.318)       # avatar radius
    face = avatar_crop(disc * 2)
    layer.alpha_composite(face, (cx - disc, cy - disc))
    # Thin green keyline separates the portrait from the cream ring at small sizes.
    kw = max(4, SIZE // 256)
    d.ellipse((cx - disc - kw // 2, cy - disc - kw // 2, cx + disc + kw // 2, cy + disc + kw // 2),
              outline=GREEN, width=kw)


def build_size(px: int) -> Image.Image:
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    rounded_square(layer)
    avatar_disc(layer)
    return layer.resize((px, px), Image.LANCZOS)


def main() -> None:
    if not LOGO.exists():
        sys.exit(f"missing source logo: {LOGO}")
    ART.mkdir(parents=True, exist_ok=True)
    sizes = [16, 32, 64, 128, 256, 512, 1024]
    iconset = ART / "QsppManager.iconset"
    iconset.mkdir(parents=True, exist_ok=True)
    master = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    rounded_square(master)
    avatar_disc(master)
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