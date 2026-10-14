from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures" / "Things" / "Pawn" / "Replicator"
MASTER = OUT / "WNG_ReplicatorBurrower.png"
SCALE = 4
SIZE = 512

EDGE = (20, 24, 25, 255)
STEEL = (94, 102, 102, 255)
HIGHLIGHT = (180, 188, 184, 255)
CYAN_HI = (151, 232, 222, 255)

if not MASTER.is_file():
    raise RuntimeError(f"Burrower master missing: {MASTER}")

base = Image.open(MASTER).convert("RGBA")
if base.size != (SIZE, SIZE):
    raise RuntimeError(f"Unexpected Burrower master size: {base.size}")

hi = base.resize((SIZE * SCALE, SIZE * SCALE), Image.Resampling.NEAREST)
draw = ImageDraw.Draw(hi)


def sc(value):
    return int(round(value * SCALE))


def rrect(box, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(
        tuple(sc(v) for v in box),
        radius=sc(radius),
        fill=fill,
        outline=outline,
        width=sc(width),
    )


def polygon(points, fill, outline=None, width=1):
    pts = [(sc(x), sc(y)) for x, y in points]
    draw.polygon(pts, fill=fill)
    if outline:
        draw.line(pts + [pts[0]], fill=outline, width=sc(width), joint="curve")


# Reinforced forward shoulder / boring-head mount. This keeps the established
# SG-1 block-Replicator visual grammar: modular steel rectangles, dark seams,
# restrained cyan role markers, no generic humanoid/mech plating.
rrect((218, 146, 294, 178), 5, EDGE)
rrect((222, 150, 290, 174), 4, (78, 86, 87, 255), HIGHLIGHT, 1)
rrect((240, 156, 272, 168), 3, (38, 44, 45, 255), (135, 145, 142, 255), 1)
rrect((246, 159, 266, 165), 2, CYAN_HI)


def drill(x0, y0, side):
    # A tapered chain of Replicator blocks instead of a smooth manufactured auger.
    # It reads as a structural breaching specialist while remaining visibly made
    # from the same reconfigurable machine blocks as the rest of the swarm.
    centers = [
        (x0, y0),
        (x0 + side * 5, y0 - 18),
        (x0 + side * 9, y0 - 36),
        (x0 + side * 12, y0 - 54),
    ]
    widths = [18, 16, 14, 12]
    heights = [18, 17, 16, 15]

    for (cx, cy), ww, hh in zip(centers, widths, heights):
        rrect((cx - ww / 2, cy - hh / 2, cx + ww / 2, cy + hh / 2), 3, EDGE)
        rrect(
            (cx - ww / 2 + 2, cy - hh / 2 + 2, cx + ww / 2 - 2, cy + hh / 2 - 2),
            2,
            STEEL,
            HIGHLIGHT,
            1,
        )
        draw.line(
            (
                sc(cx - ww / 2 + 4),
                sc(cy - hh / 2 + 4),
                sc(cx + ww / 2 - 4),
                sc(cy - hh / 2 + 4),
            ),
            fill=(205, 211, 207, 160),
            width=sc(1),
        )

    cx, cy = centers[-1]
    rrect((cx - 4, cy - 2, cx + 4, cy + 2), 1, CYAN_HI)

    tip_y = cy - heights[-1] / 2 - 12
    half = 5
    polygon(
        [
            (cx - half, cy - heights[-1] / 2),
            (cx + half, cy - heights[-1] / 2),
            (cx, tip_y),
        ],
        (108, 116, 115, 255),
        EDGE,
        2,
    )
    polygon(
        [
            (cx - 2.2, cy - heights[-1] / 2 - 1),
            (cx + 2.2, cy - heights[-1] / 2 - 1),
            (cx, tip_y + 4),
        ],
        (35, 41, 42, 255),
    )


drill(236, 145, -1)
drill(276, 145, 1)

# Compact forward sensor/role marker between the two boring mandibles.
rrect((246, 115, 266, 137), 3, EDGE)
rrect((249, 118, 263, 133), 2, (105, 113, 112, 255), HIGHLIGHT, 1)
rrect((252, 123, 260, 128), 1, CYAN_HI)

south = hi.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
px = south.load()
for y in range(SIZE):
    for x in range(SIZE):
        if px[x, y][3] < 2:
            px[x, y] = (0, 0, 0, 0)

family = {
    "": south,
    "_south": south,
    "_north": south.rotate(180, Image.Resampling.BICUBIC),
    "_west": south.rotate(90, Image.Resampling.BICUBIC),
    "_east": south.rotate(270, Image.Resampling.BICUBIC),
}

for suffix, image in family.items():
    path = OUT / f"WNG_ReplicatorBurrower{suffix}.png"
    image.save(path, "PNG", optimize=True)
    with Image.open(path) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (SIZE, SIZE):
            raise RuntimeError(f"{path}: bad mode/size")
        alpha = check.getchannel("A")
        if not alpha.getbbox():
            raise RuntimeError(f"{path}: empty alpha")
        edge_max = max(
            alpha.crop((0, 0, SIZE, 1)).getextrema()[1],
            alpha.crop((0, SIZE - 1, SIZE, SIZE)).getextrema()[1],
            alpha.crop((0, 0, 1, SIZE)).getextrema()[1],
            alpha.crop((SIZE - 1, 0, SIZE, SIZE)).getextrema()[1],
        )
        if edge_max != 0:
            raise RuntimeError(f"{path}: alpha touches canvas edge")

print("Upgraded Replicator Burrower five-direction family")
