from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
FINAL = 512

EDGE = (11, 14, 15, 255)
SHADOW = (0, 0, 0, 90)
DEEP = (31, 36, 38, 255)
STEEL = (83, 91, 93, 255)
MID = (119, 126, 127, 255)
LIGHT = (205, 212, 210, 255)
CY = (62, 173, 176, 255)
CYHI = (145, 231, 222, 255)


def module(size, tone=0, accent=False, blade=False):
    w, h = map(int, size)
    pad = 18
    im = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = max(3, min(w, h) // 7)

    sh = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle((pad + 5, pad + 7, pad + w + 5, pad + h + 7), radius=r, fill=SHADOW)
    sh = sh.filter(ImageFilter.GaussianBlur(5))
    im.alpha_composite(sh)

    base = (58, 64, 66, 255) if tone < 0 else ((95, 103, 105, 255) if tone == 0 else (127, 135, 136, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=3)
    d.line((pad + 5, pad + r, pad + 5, pad + h - r), fill=(156, 164, 163, 190), width=2)
    d.line((pad + r, pad + h - 6, pad + w - r, pad + h - 6), fill=(12, 16, 17, 230), width=4)
    d.line((pad + w - 6, pad + r, pad + w - 6, pad + h - r), fill=(18, 22, 23, 210), width=3)

    if w > 40 and h > 24:
        x0 = pad + int(w * .18)
        x1 = pad + int(w * .82)
        y0 = pad + int(h * .30)
        y1 = pad + int(h * .70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(38, 44, 46, 255), outline=(138, 146, 145, 220), width=2)
        if accent:
            yy = (y0 + y1) // 2
            gm = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(gm)
            gd.rounded_rectangle((x0 + 8, yy - 4, x1 - 8, yy + 4), radius=2, fill=170)
            gm = gm.filter(ImageFilter.GaussianBlur(8))
            gl = Image.new('RGBA', im.size, (*CY[:3], 0))
            gl.putalpha(gm.point(lambda q: int(q * .28)))
            im.alpha_composite(gl)
            d = ImageDraw.Draw(im)
            d.rounded_rectangle((x0 + 9, yy - 3, x1 - 9, yy + 3), radius=2, fill=CYHI)

    rr = max(2, min(w, h) // 18)
    for x, y in ((pad + 10, pad + 10), (pad + w - 10, pad + h - 10)):
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=(25, 30, 31, 255), outline=LIGHT, width=1)

    if blade:
        d.polygon([(pad + w - 2, pad + 5), (pad + w + 16, pad + h // 2), (pad + w - 2, pad + h - 5)], fill=(38, 43, 44, 255), outline=LIGHT)
    return im


def place(canvas, image, center, angle):
    rot = image.rotate(angle, Image.Resampling.BICUBIC, expand=True)
    canvas.alpha_composite(rot, (round(center[0] - rot.width / 2), round(center[1] - rot.height / 2)))


def chain(canvas, a, b, width, parts=3, tone=0, accent_last=False, blade_last=False):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    length = math.hypot(dx, dy)
    angle = math.degrees(math.atan2(dy, dx))
    ux, uy = dx / length, dy / length
    gap = 5
    seg = (length - gap * (parts - 1)) / parts
    for i in range(parts):
        dist = i * (seg + gap) + seg / 2
        center = (ax + ux * dist, ay + uy * dist)
        place(canvas, module((seg, width), tone=tone, accent=accent_last and i == parts - 1, blade=blade_last and i == parts - 1), center, angle)


def joint(canvas, p, radius):
    x, y = p
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=EDGE, outline=LIGHT, width=3)
    d.ellipse((x - radius * .55, y - radius * .55, x + radius * .55, y + radius * .55), fill=(58, 65, 66, 255), outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width, blade=False):
    chain(canvas, hip, knee, width, 3, 0)
    joint(canvas, knee, int(width * .34))
    chain(canvas, knee, ankle, width * .80, 3, -1)
    joint(canvas, ankle, int(width * .27))
    chain(canvas, ankle, foot, width * .62, 2, 0, blade_last=blade)


def hunter_south():
    canvas = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    # Six legs exactly. The front pair are the Hunter's scything pursuit cutters.
    left = [
        ((452, 430), (330, 310), (240, 215), (150, 145), True),
        ((430, 522), (302, 505), (190, 520), (110, 542), False),
        ((455, 615), (355, 720), (282, 815), (225, 875), False),
    ]
    right = [tuple((S - x, y) for x, y in pts[:4]) + (pts[4],) for pts in left]
    legs = left + right
    legs.sort(key=lambda t: sum(y for _, y in t[:4]) / 4)
    for hip, knee, ankle, foot, blade in legs:
        leg(canvas, hip, knee, ankle, foot, 27, blade)

    # Hunter is longer, lower and narrower than the Drone: a fast pursuit body, not a scaled Drone.
    place(canvas, module((170, 315), -1, False), (512, 535), 90)
    for y, w, h, tone, accent in [
        (400, 150, 72, 0, False),
        (478, 164, 70, 0, True),
        (560, 146, 64, 0, False),
        (635, 112, 52, -1, False),
    ]:
        place(canvas, module((w, h), tone, accent), (512, y), 0)

    # Narrow side ribs keep the body visibly fast/light instead of becoming armored like Bulwark.
    for x in (447, 577):
        place(canvas, module((126, 30), 0, False), (x, 515), 90)
        place(canvas, module((72, 24), -1, False), (x, 620), 90)

    # Forward sensor/head deck differentiates the pursuit silhouette from the basic Drone.
    place(canvas, module((112, 42), 1, False), (512, 338), 0)
    place(canvas, module((78, 34), 0, True), (512, 298), 0)

    d = ImageDraw.Draw(canvas)
    for x, y in ((452, 430), (430, 522), (455, 615), (572, 430), (594, 522), (569, 615)):
        d.ellipse((x - 5, y - 5, x + 5, y + 5), fill=CYHI, outline=EDGE, width=2)

    box = canvas.getchannel('A').getbbox()
    if not box:
        raise RuntimeError('Hunter render has empty alpha')
    crop = canvas.crop(box)
    scale = min(438 / crop.width, 438 / crop.height)
    crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (FINAL, FINAL), (0, 0, 0, 0))
    out.alpha_composite(crop, ((FINAL - crop.width) // 2, (FINAL - crop.height) // 2))
    return out


south = hunter_south()
views = {
    'south': south,
    'west': south.transpose(Image.Transpose.ROTATE_270),
    'north': south.transpose(Image.Transpose.ROTATE_180),
    'east': south.transpose(Image.Transpose.ROTATE_90),
}

for direction, image in views.items():
    image.save(OUT / f'WNG_ReplicatorHunter_{direction}.png', 'PNG', optimize=True)
views['south'].save(OUT / 'WNG_ReplicatorHunter.png', 'PNG', optimize=True)

for suffix in ['', '_north', '_east', '_south', '_west']:
    p = OUT / f'WNG_ReplicatorHunter{suffix}.png'
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

print('Generated only WNG Replicator Hunter: base + north/east/south/west, six-legged pursuit/scything Stargate block form.')
