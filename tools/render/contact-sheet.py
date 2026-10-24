#!/usr/bin/env python3
"""Rebuild the light and dark contact sheets from docs/screenshots/v2.

Stands behind driver-render.py: after rendering {Page}-{light,dark}.png for all
ten shell pages, run this to regenerate contact-{light,dark}.png (two rows of
five labelled thumbnails at fixed size). Uses Pillow if present; otherwise falls
back to sips (macOS) resizing each page onto a fixed canvas via Quartz.
"""
from __future__ import annotations

import sys
from pathlib import Path

V2 = Path(__file__).resolve().parents[2] / "docs/screenshots/v2"
PAGES = ["Today", "Requirements", "Documents", "Tasks", "Registers",
         "People", "Assessments", "Library", "PrintCentre", "Settings"]
THUMB_W, THUMB_H, CAPTION = 448, 288, 26
COLS, GUT, PAD = 5, 12, 16


def build(theme: str) -> None:
    paths = [V2 / f"{p}-{theme}.png" for p in PAGES]
    missing = [p.name for p in paths if not p.exists()]
    if missing:
        raise SystemExit(f"missing renders for {theme}: {missing}")

    try:
        from PIL import Image, ImageDraw
    except ImportError:
        print("Pillow missing; install it (python3 -m pip install --user pillow)")
        raise SystemExit(2)

    sheet = Image.new(
        "RGB",
        (PAD * 2 + COLS * THUMB_W + (COLS - 1) * GUT,
         PAD * 2 + 2 * (THUMB_H + CAPTION) + GUT),
        (28, 28, 30) if theme == "dark" else (246, 245, 242),
    )
    draw = ImageDraw.Draw(sheet)
    for idx, path in enumerate(paths):
        row, col = divmod(idx, COLS)
        x = PAD + col * (THUMB_W + GUT)
        y = PAD + row * (THUMB_H + CAPTION + GUT)
        img = Image.open(path)
        img.thumbnail((THUMB_W, THUMB_H))
        sheet.paste(img, (x, y))
        fg = (230, 230, 230) if theme == "dark" else (40, 40, 40)
        draw.text((x, y + THUMB_H + 6), path.name, fill=fg)
    out = V2 / f"contact-{theme}.png"
    sheet.save(out)
    print(out)


def main() -> int:
    themes = sys.argv[1:] or ["light", "dark"]
    for theme in themes:
        build(theme)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())