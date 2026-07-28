"""Regenerate grass.png as a uniform-density, edge-wrapping seamless tile.

Keeps the hand-drawn palette (sampled from the original file) and the
comma/dash blade look, but fixes the two problems from the first pass:
  1. density gradient (dense top -> sparse bottom)
  2. strokes that don't continue across the tile border

Backs up the original to grass_handdrawn_original.png before overwriting.
"""
import math
import random
import shutil
from pathlib import Path

from PIL import Image, ImageDraw, ImageChops

ASSETS = Path(__file__).parent / "godot" / "assets"
SRC = ASSETS / "grass.png"
BACKUP = ASSETS / "grass_handdrawn_original.png"

SIZE = 256
BG_COLOR = (34, 176, 76)
STROKE_COLOR = (17, 47, 10)
STROKE_COUNT = 200
random.seed(7)


def bezier_points(p0, p1, p2, steps=8):
    pts = []
    for i in range(steps + 1):
        t = i / steps
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]
        pts.append((x, y))
    return pts


def draw_blade(draw, cx, cy, angle, length, curve, width):
    a = math.radians(angle)
    p0 = (cx - math.cos(a) * length / 2, cy - math.sin(a) * length / 2)
    p2 = (cx + math.cos(a) * length / 2, cy + math.sin(a) * length / 2)
    perp = (-math.sin(a), math.cos(a))
    p1 = (cx + perp[0] * curve, cy + perp[1] * curve)
    pts = bezier_points(p0, p1, p2)
    draw.line(pts, fill=STROKE_COLOR, width=width, joint="curve")
    r = width / 2
    draw.ellipse((p0[0] - r, p0[1] - r, p0[0] + r, p0[1] + r), fill=STROKE_COLOR)
    draw.ellipse((p2[0] - r, p2[1] - r, p2[0] + r, p2[1] + r), fill=STROKE_COLOR)


def draw_blade_wrapped(draw, cx, cy, angle, length, curve, width):
    # draw the blade at all 9 toroidal offsets so anything crossing an edge
    # reappears on the opposite edge -- guarantees a seamless tile
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            draw_blade(draw, cx + dx, cy + dy, angle, length, curve, width)


def generate():
    im = Image.new("RGB", (SIZE, SIZE), BG_COLOR)
    draw = ImageDraw.Draw(im)

    for _ in range(STROKE_COUNT):
        cx = random.uniform(0, SIZE)
        cy = random.uniform(0, SIZE)
        angle = random.uniform(0, 360)
        length = random.uniform(14, 22)
        curve = random.uniform(-5, 5)
        width = random.randint(2, 4)
        draw_blade_wrapped(draw, cx, cy, angle, length, curve, width)

    return im.convert("RGBA")


def main():
    if not BACKUP.exists():
        shutil.copy(SRC, BACKUP)
        print(f"backed up original to {BACKUP.name}")

    seamless = generate()
    seamless.save(SRC)
    print(f"wrote seamless tile to {SRC.name}")

    # verification previews, same technique used to diagnose the first pass
    rgb = seamless.convert("RGB")
    wrapped = ImageChops.offset(rgb, SIZE // 2, SIZE // 2)
    wrapped.save(ASSETS / "grass_wrapped_preview.png")

    tiled = Image.new("RGB", (SIZE * 2, SIZE * 2))
    for x in range(2):
        for y in range(2):
            tiled.paste(rgb, (x * SIZE, y * SIZE))
    tiled.save(ASSETS / "grass_tiled_preview.png")
    print("regenerated verification previews")


if __name__ == "__main__":
    main()
