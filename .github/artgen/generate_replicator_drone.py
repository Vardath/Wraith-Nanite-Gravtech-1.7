from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
HI = 1024
FINAL = 512
CX = CY = HI // 2

EDGE = (15, 19, 20, 255)
DEEP = (31, 36, 38, 255)
STEEL = (91, 99, 101, 255)
MID = (119, 128, 129, 255)
LIGHT = (197, 205, 204, 255)
WHITE = (228, 232, 230, 255)
ACCENT = (63, 178, 178, 255)
ACCENT_HI = (137, 226, 218, 255)


def block(size, tone=0, accent=False, inset=True):
    w, h = map(int, size)
    pad = 24
    im = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = max(5, min(w, h) // 8)

    shadow = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle((pad + 7, pad + 10, pad + w + 7, pad + h + 10), radius=r, fill=(0, 0, 0, 105))
    shadow = shadow.filter(ImageFilter.GaussianBlur(7))
    im.alpha_composite(shadow)

    base = [DEEP, STEEL, MID][max(0, min(2, tone + 1))]
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 5, pad + 5, pad + w - 5, pad + h - 5), radius=max(3, r - 3), fill=base)

    # brushed metal bands and directional lighting
    for y in range(pad + 8, pad + h - 7, 6):
        t = (y - pad) / max(1, h)
        delta = int((0.5 - t) * 18)
        col = tuple(max(0, min(255, c + delta)) for c in base[:3]) + (140,)
        d.line((pad + 10, y, pad + w - 10, y), fill=col, width=3)
    d.line((pad + r, pad + 6, pad + w - r, pad + 6), fill=LIGHT, width=4)
    d.line((pad + 7, pad + r, pad + 7, pad + h - r), fill=(155, 164, 163, 210), width=2)
    d.line((pad + r, pad + h - 7, pad + w - r, pad + h - 7), fill=(9, 12, 13, 230), width=5)
    d.line((pad + w - 7, pad + r, pad + w - 7, pad + h - r), fill=(16, 20, 21, 230), width=4)

    if inset and w > 42 and h > 28:
        x0, x1 = pad + int(w * .20), pad + int(w * .80)
        y0, y1 = pad + int(h * .31), pad + int(h * .69)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(40, 47, 49, 255), outline=(145, 153, 152, 220), width=2)
        d.line((x0 + 4, y0 + 3, x1 - 4, y0 + 3), fill=(181, 188, 185, 150), width=2)
        if accent:
            yy = (y0 + y1) // 2
            glow = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(glow)
            gd.rounded_rectangle((x0 + 10, yy - 4, x1 - 10, yy + 4), radius=3, fill=170)
            glow = glow.filter(ImageFilter.GaussianBlur(10))
            lay = Image.new('RGBA', im.size, (*ACCENT[:3], 0))
            lay.putalpha(glow.point(lambda q: int(q * .30)))
            im.alpha_composite(lay)
            d = ImageDraw.Draw(im)
            d.rounded_rectangle((x0 + 11, yy - 3, x1 - 11, yy + 3), radius=2, fill=ACCENT_HI)

    # small fasteners keep it recognisably built from hard Replicator blocks
    rr = max(2, min(w, h) // 18)
    for x, y in ((pad + 11, pad + 11), (pad + w - 11, pad + h - 11)):
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=(24, 29, 30, 255), outline=LIGHT, width=1)
    return im


def paste_rot(canvas, image, center, angle):
    r = image.rotate(angle, Image.Resampling.BICUBIC, expand=True)
    canvas.alpha_composite(r, (round(center[0] - r.width / 2), round(center[1] - r.height / 2)))


def segment(canvas, a, b, width, tone=0, accent=False):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    length = max(10, math.hypot(dx, dy))
    angle = math.degrees(math.atan2(dy, dx))
    mid = ((ax + bx) / 2, (ay + by) / 2)
    paste_rot(canvas, block((length, width), tone=tone, accent=accent), mid, angle)


def joint(canvas, p, r=15):
    x, y = p
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - r, y - r, x + r, y + r), fill=EDGE, outline=LIGHT, width=3)
    d.ellipse((x - r * .52, y - r * .52, x + r * .52, y + r * .52), fill=(58, 66, 67, 255), outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width=30):
    segment(canvas, hip, knee, width, tone=0)
    joint(canvas, knee, int(width * .34))
    segment(canvas, knee, ankle, width * .82, tone=-1)
    joint(canvas, ankle, int(width * .28))
    segment(canvas, ankle, foot, width * .66, tone=0)
    # short squared terminal foot, not a fantasy claw
    vx, vy = foot[0] - ankle[0], foot[1] - ankle[1]
    ll = math.hypot(vx, vy) or 1
    ux, uy = vx / ll, vy / ll
    tip = (foot[0] + ux * 26, foot[1] + uy * 26)
    segment(canvas, foot, tip, width * .46, tone=1)


def drone_south():
    c = Image.new('RGBA', (HI, HI), (0, 0, 0, 0))

    # Six legs, in three unmistakable pairs. Compact and light: this is the smallest swarm unit.
    left = [
        ((444, 438), (330, 330), (250, 235), (180, 190)),
        ((420, 515), (305, 505), (205, 520), (135, 540)),
        ((445, 595), (345, 690), (270, 785), (215, 850)),
    ]
    right = [tuple((HI - x, y) for x, y in pts) for pts in left]
    all_legs = left + right
    all_legs.sort(key=lambda pts: sum(y for _, y in pts) / len(pts))
    for pts in all_legs:
        leg(c, *pts, width=28)

    # Narrow central body; unlike later forms it has no armor slabs, command crown, drill or gun organ.
    paste_rot(c, block((176, 248), tone=-1, accent=False), (CX, 525), 90)
    paste_rot(c, block((138, 78), tone=0, accent=False), (CX, 420), 0)
    paste_rot(c, block((150, 76), tone=0, accent=True), (CX, 505), 0)
    paste_rot(c, block((126, 68), tone=-1, accent=False), (CX, 590), 0)

    # Side spine blocks reinforce the canonical hard-block construction without making it bulky.
    for x in (452, 572):
        paste_rot(c, block((92, 34), tone=0, accent=False), (x, 525), 90)

    # Small paired feeder/manipulator jaws. They are not extra legs.
    segment(c, (482, 394), (458, 340), 19, tone=-1)
    segment(c, (542, 394), (566, 340), 19, tone=-1)
    segment(c, (458, 340), (440, 306), 14, tone=1)
    segment(c, (566, 340), (584, 306), 14, tone=1)

    # clean crop and scale with generous transparent margin
    box = c.getchannel('A').getbbox()
    if not box:
        raise RuntimeError('Drone render has empty alpha')
    crop = c.crop(box)
    scale = min(430 / crop.width, 430 / crop.height)
    crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (FINAL, FINAL), (0, 0, 0, 0))
    out.alpha_composite(crop, ((FINAL - crop.width) // 2, (FINAL - crop.height) // 2))
    return out


south = drone_south()
views = {
    'south': south,
    'west': south.transpose(Image.Transpose.ROTATE_270),
    'north': south.transpose(Image.Transpose.ROTATE_180),
    'east': south.transpose(Image.Transpose.ROTATE_90),
}

for direction, image in views.items():
    path = OUT / f'WNG_ReplicatorDrone_{direction}.png'
    image.save(path, 'PNG', optimize=True)
views['south'].save(OUT / 'WNG_ReplicatorDrone.png', 'PNG', optimize=True)

for suffix in ['', '_north', '_east', '_south', '_west']:
    p = OUT / f'WNG_ReplicatorDrone{suffix}.png'
    with Image.open(p) as chk:
        chk.load()
        if chk.mode != 'RGBA' or chk.size != (512, 512):
            raise RuntimeError(f'{p}: expected 512x512 RGBA')
        a = chk.getchannel('A')
        if not a.getbbox():
            raise RuntimeError(f'{p}: empty alpha')
        edges = [
            a.crop((0, 0, 512, 1)).getextrema()[1],
            a.crop((0, 511, 512, 512)).getextrema()[1],
            a.crop((0, 0, 1, 512)).getextrema()[1],
            a.crop((511, 0, 512, 512)).getextrema()[1],
        ]
        if any(edges):
            raise RuntimeError(f'{p}: alpha touches canvas edge {edges}')

print('Generated only WNG Replicator Drone: base + north/east/south/west, six-legged Stargate block form.')
