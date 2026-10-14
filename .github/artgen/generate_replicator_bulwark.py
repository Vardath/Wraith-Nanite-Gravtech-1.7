from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
FINAL = 512

EDGE = (10, 13, 14, 255)
SHADOW = (0, 0, 0, 100)
DEEP = (32, 37, 39, 255)
STEEL = (91, 99, 101, 255)
MID = (126, 134, 135, 255)
LIGHT = (214, 220, 218, 255)
CY = (54, 163, 168, 255)
CYHI = (138, 226, 219, 255)


def module(size, tone=0, accent=False, armor=False, crusher=False):
    w, h = map(int, size)
    pad = 20
    im = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = max(4, min(w, h) // 7)

    sh = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle((pad + 6, pad + 8, pad + w + 6, pad + h + 8), radius=r, fill=SHADOW)
    sh = sh.filter(ImageFilter.GaussianBlur(6))
    im.alpha_composite(sh)

    base = (54, 60, 62, 255) if tone < 0 else ((100, 108, 110, 255) if tone == 0 else (132, 140, 141, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=3)
    d.line((pad + 5, pad + r, pad + 5, pad + h - r), fill=(164, 171, 170, 190), width=2)
    d.line((pad + r, pad + h - 6, pad + w - r, pad + h - 6), fill=(10, 13, 14, 230), width=5)
    d.line((pad + w - 6, pad + r, pad + w - 6, pad + h - r), fill=(16, 20, 21, 220), width=4)

    if armor:
        inset = 8
        d.rounded_rectangle((pad + inset, pad + inset, pad + w - inset, pad + h - inset), radius=max(2, r - 4), outline=(170, 178, 177, 210), width=3)
        if w >= h:
            cy = pad + h // 2
            d.rectangle((pad + int(w*.20), cy - 5, pad + int(w*.80), cy + 5), fill=(37, 43, 45, 255))
        else:
            cx = pad + w // 2
            d.rectangle((cx - 5, pad + int(h*.20), cx + 5, pad + int(h*.80)), fill=(37, 43, 45, 255))

    if w > 42 and h > 26:
        x0 = pad + int(w * .18)
        x1 = pad + int(w * .82)
        y0 = pad + int(h * .30)
        y1 = pad + int(h * .70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(38, 44, 46, 255), outline=(145, 152, 151, 220), width=2)
        if accent:
            yy = (y0 + y1) // 2
            gm = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(gm)
            gd.rounded_rectangle((x0 + 8, yy - 4, x1 - 8, yy + 4), radius=2, fill=180)
            gm = gm.filter(ImageFilter.GaussianBlur(8))
            gl = Image.new('RGBA', im.size, (*CY[:3], 0))
            gl.putalpha(gm.point(lambda q: int(q * .28)))
            im.alpha_composite(gl)
            d = ImageDraw.Draw(im)
            d.rounded_rectangle((x0 + 9, yy - 3, x1 - 9, yy + 3), radius=2, fill=CYHI)

    if crusher:
        d.polygon([
            (pad + w - 5, pad + 4),
            (pad + w + 20, pad + int(h*.24)),
            (pad + w + 24, pad + int(h*.76)),
            (pad + w - 5, pad + h - 4),
        ], fill=(67, 74, 76, 255), outline=LIGHT)
        d.line((pad + w + 2, pad + int(h*.30), pad + w + 2, pad + int(h*.70)), fill=(25, 29, 30, 255), width=4)

    rr = max(2, min(w, h) // 18)
    for x, y in ((pad + 11, pad + 11), (pad + w - 11, pad + h - 11)):
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=(23, 28, 29, 255), outline=LIGHT, width=1)
    return im


def place(canvas, image, center, angle):
    rot = image.rotate(angle, Image.Resampling.BICUBIC, expand=True)
    canvas.alpha_composite(rot, (round(center[0] - rot.width / 2), round(center[1] - rot.height / 2)))


def chain(canvas, a, b, width, parts=2, tone=0, armor=False, crusher_last=False):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    length = math.hypot(dx, dy)
    angle = math.degrees(math.atan2(dy, dx))
    ux, uy = dx / length, dy / length
    gap = 6
    seg = (length - gap * (parts - 1)) / parts
    for i in range(parts):
        dist = i * (seg + gap) + seg / 2
        center = (ax + ux * dist, ay + uy * dist)
        place(canvas, module((seg, width), tone=tone, armor=armor, crusher=crusher_last and i == parts - 1), center, angle)


def joint(canvas, p, radius):
    x, y = p
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=EDGE, outline=LIGHT, width=3)
    d.ellipse((x - radius * .57, y - radius * .57, x + radius * .57, y + radius * .57), fill=(59, 66, 67, 255), outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width, crusher=False):
    chain(canvas, hip, knee, width, 2, 0, armor=True)
    joint(canvas, knee, int(width * .38))
    chain(canvas, knee, ankle, width * .86, 2, -1, armor=True)
    joint(canvas, ankle, int(width * .31))
    chain(canvas, ankle, foot, width * .72, 2, 0, armor=True, crusher_last=crusher)


def bulwark_south():
    c = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    left = [
        ((430, 424), (312, 332), (224, 292), (150, 288), True),
        ((407, 520), (282, 510), (172, 535), (102, 560), False),
        ((432, 620), (334, 710), (250, 790), (188, 846), False),
    ]
    right = [tuple((S - x, y) for x, y in pts[:4]) + (pts[4],) for pts in left]
    legs = left + right
    legs.sort(key=lambda t: sum(y for _, y in t[:4]) / 4)
    for hip, knee, ankle, foot, crusher in legs:
        leg(c, hip, knee, ankle, foot, 43, crusher)

    place(c, module((290, 310), tone=-1, armor=True), (512, 535), 90)

    for y, w, h, tone, accent in [
        (397, 250, 86, 1, False),
        (480, 282, 92, 0, False),
        (566, 264, 88, 0, True),
        (647, 214, 72, -1, False),
    ]:
        place(c, module((w, h), tone=tone, accent=accent, armor=True), (512, y), 0)

    for x in (390, 634):
        place(c, module((180, 58), tone=1, armor=True), (x, 510), 90)
        place(c, module((110, 48), tone=0, armor=True), (x, 625), 90)

    place(c, module((210, 62), tone=1, armor=True), (512, 322), 0)
    place(c, module((156, 46), tone=0, accent=False, armor=True), (512, 277), 0)

    d = ImageDraw.Draw(c)
    for x, y in ((430, 424), (407, 520), (432, 620), (594, 424), (617, 520), (592, 620)):
        d.rounded_rectangle((x-7, y-4, x+7, y+4), radius=3, fill=CYHI, outline=EDGE)

    box = c.getchannel('A').getbbox()
    if not box:
        raise RuntimeError('Bulwark render has empty alpha')
    crop = c.crop(box)
    scale = min(448 / crop.width, 448 / crop.height)
    crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (FINAL, FINAL), (0, 0, 0, 0))
    out.alpha_composite(crop, ((FINAL - crop.width)//2, (FINAL - crop.height)//2))
    return out


south = bulwark_south()
views = {
    'south': south,
    'west': south.transpose(Image.Transpose.ROTATE_270),
    'north': south.transpose(Image.Transpose.ROTATE_180),
    'east': south.transpose(Image.Transpose.ROTATE_90),
}

for direction, image in views.items():
    image.save(OUT / f'WNG_ReplicatorBulwark_{direction}.png', 'PNG', optimize=True)
views['south'].save(OUT / 'WNG_ReplicatorBulwark.png', 'PNG', optimize=True)

for suffix in ['', '_north', '_east', '_south', '_west']:
    p = OUT / f'WNG_ReplicatorBulwark{suffix}.png'
    with Image.open(p) as chk:
        chk.load()
        if chk.mode != 'RGBA' or chk.size != (512,512):
            raise RuntimeError(f'{p}: expected 512x512 RGBA')
        a = chk.getchannel('A')
        if not a.getbbox():
            raise RuntimeError(f'{p}: empty alpha')
        edges = [
            a.crop((0,0,512,1)).getextrema()[1],
            a.crop((0,511,512,512)).getextrema()[1],
            a.crop((0,0,1,512)).getextrema()[1],
            a.crop((511,0,512,512)).getextrema()[1],
        ]
        if any(edges):
            raise RuntimeError(f'{p}: alpha touches canvas edge {edges}')

print('Generated only WNG Replicator Bulwark: base + north/east/south/west, six-legged armored crusher Stargate block form.')
