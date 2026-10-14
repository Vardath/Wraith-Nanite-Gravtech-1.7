from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
FINAL = 512

EDGE = (9, 12, 13, 255)
SHADOW = (0, 0, 0, 105)
DEEP = (31, 36, 38, 255)
STEEL = (94, 103, 105, 255)
MID = (132, 141, 141, 255)
LIGHT = (220, 226, 223, 255)
CY = (48, 160, 166, 255)
CYHI = (140, 228, 221, 255)


def module(size, tone=0, accent=False, armor=False, grooves=False):
    w, h = map(int, size)
    pad = 19
    im = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = max(4, min(w, h) // 7)

    sh = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle((pad + 6, pad + 8, pad + w + 6, pad + h + 8), radius=r, fill=SHADOW)
    sh = sh.filter(ImageFilter.GaussianBlur(6))
    im.alpha_composite(sh)

    base = (50, 57, 59, 255) if tone < 0 else ((101, 110, 112, 255) if tone == 0 else (139, 148, 149, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=3)
    d.line((pad + 5, pad + r, pad + 5, pad + h - r), fill=(170, 178, 176, 190), width=2)
    d.line((pad + r, pad + h - 6, pad + w - r, pad + h - 6), fill=(8, 11, 12, 235), width=5)
    d.line((pad + w - 6, pad + r, pad + w - 6, pad + h - r), fill=(16, 19, 20, 230), width=4)

    if armor:
        inset = 8
        d.rounded_rectangle((pad + inset, pad + inset, pad + w - inset, pad + h - inset), radius=max(2, r - 4), outline=(176, 184, 182, 210), width=3)

    if grooves:
        if w >= h:
            cy = pad + h // 2
            for off in (-9, 9):
                d.line((pad + 14, cy + off, pad + w - 14, cy + off), fill=(40, 47, 49, 235), width=3)
        else:
            cx = pad + w // 2
            for off in (-9, 9):
                d.line((cx + off, pad + 14, cx + off, pad + h - 14), fill=(40, 47, 49, 235), width=3)

    if w > 44 and h > 26:
        x0 = pad + int(w * .18)
        x1 = pad + int(w * .82)
        y0 = pad + int(h * .30)
        y1 = pad + int(h * .70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(36, 42, 44, 255), outline=(147, 154, 153, 220), width=2)
        if accent:
            yy = (y0 + y1) // 2
            gm = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(gm)
            gd.rounded_rectangle((x0 + 8, yy - 4, x1 - 8, yy + 4), radius=2, fill=190)
            gm = gm.filter(ImageFilter.GaussianBlur(8))
            gl = Image.new('RGBA', im.size, (*CY[:3], 0))
            gl.putalpha(gm.point(lambda q: int(q * .29)))
            im.alpha_composite(gl)
            d = ImageDraw.Draw(im)
            d.rounded_rectangle((x0 + 9, yy - 3, x1 - 9, yy + 3), radius=2, fill=CYHI)

    rr = max(2, min(w, h) // 18)
    for x, y in ((pad + 11, pad + 11), (pad + w - 11, pad + h - 11)):
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=(22, 27, 28, 255), outline=LIGHT, width=1)
    return im


def place(canvas, image, center, angle):
    rot = image.rotate(angle, Image.Resampling.BICUBIC, expand=True)
    canvas.alpha_composite(rot, (round(center[0] - rot.width / 2), round(center[1] - rot.height / 2)))


def chain(canvas, a, b, width, parts=2, tone=0, armor=False, grooves=False):
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
        place(canvas, module((seg, width), tone=tone, armor=armor, grooves=grooves), center, angle)


def joint(canvas, p, radius, powered=False):
    x, y = p
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=EDGE, outline=LIGHT, width=3)
    inner = CYHI if powered else (59, 66, 67, 255)
    d.ellipse((x - radius * .56, y - radius * .56, x + radius * .56, y + radius * .56), fill=inner, outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width, anchor=False):
    chain(canvas, hip, knee, width, 2, 0, armor=True, grooves=True)
    joint(canvas, knee, int(width * .37))
    chain(canvas, knee, ankle, width * .86, 2, -1, armor=True)
    joint(canvas, ankle, int(width * .31), powered=anchor)
    chain(canvas, ankle, foot, width * .70, 2, 0, armor=True, grooves=anchor)
    if anchor:
        # Wide stabilizing foot-pad for firing posture; still part of the same walking leg.
        dx = foot[0] - ankle[0]
        dy = foot[1] - ankle[1]
        ang = math.degrees(math.atan2(dy, dx))
        place(canvas, module((58, 30), tone=-1, armor=True, grooves=True), foot, ang)


def emitter_head(canvas, center, angle):
    x, y = center
    place(canvas, module((82, 42), tone=1, accent=True, armor=True, grooves=True), center, angle)
    mask = Image.new('L', canvas.size, 0)
    md = ImageDraw.Draw(mask)
    md.ellipse((x - 18, y - 18, x + 18, y + 18), fill=185)
    blur = mask.filter(ImageFilter.GaussianBlur(11))
    glow = Image.new('RGBA', canvas.size, (*CY[:3], 0))
    glow.putalpha(blur.point(lambda q: int(q * .24)))
    canvas.alpha_composite(glow)
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - 13, y - 13, x + 13, y + 13), fill=EDGE, outline=LIGHT, width=3)
    d.ellipse((x - 7, y - 7, x + 7, y + 7), fill=CYHI, outline=DEEP, width=2)


def artillery_south():
    c = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    # Exactly six stable, Bulwark-mass legs. Wider stance and anchoring feet make the firing role obvious.
    left = [
        ((424, 430), (320, 350), (230, 316), (156, 316), False),
        ((402, 530), (282, 520), (174, 548), (102, 578), True),
        ((426, 626), (326, 718), (242, 802), (176, 862), True),
    ]
    right = [tuple((S - x, y) for x, y in pts[:4]) + (pts[4],) for pts in left]
    legs = left + right
    legs.sort(key=lambda t: sum(y for _, y in t[:4]) / 4)
    for hip, knee, ankle, foot, anchor in legs:
        leg(c, hip, knee, ankle, foot, 39, anchor=anchor)

    # Long, low support chassis: same mass tier as Bulwark, but not its square crusher silhouette.
    place(c, module((252, 342), tone=-1, armor=True, grooves=True), (512, 548), 90)
    for y, w, h, tone, accent in [
        (418, 206, 66, 1, False),
        (478, 238, 72, 0, True),
        (544, 256, 78, 0, False),
        (614, 228, 70, -1, False),
        (676, 188, 58, -1, False),
    ]:
        place(c, module((w, h), tone=tone, accent=accent, armor=True, grooves=True), (512, y), 0)

    # Paired power shoulders. They are body-grown emitter roots, not weapon mounts bolted on afterward.
    for sx in (-1, 1):
        place(c, module((80, 146), tone=1, accent=True, armor=True, grooves=True), (512 + sx * 102, 505), 90)
        place(c, module((72, 92), tone=0, accent=False, armor=True), (512 + sx * 105, 586), 90)

    # Twin pulse array: two parallel Replicator-block emitter spines grown forward from the chassis.
    for sx in (-1, 1):
        x = 512 + sx * 42
        chain(c, (x, 446), (x, 310), 27, 3, tone=0, armor=True, grooves=True)
        chain(c, (x, 310), (x, 226), 22, 2, tone=1, armor=False, grooves=True)
        emitter_head(c, (x, 190), 90)

    # Central brace ties both emitters into the body and reinforces the unmistakable forward firing direction.
    place(c, module((142, 38), tone=-1, accent=False, armor=True, grooves=True), (512, 330), 0)
    place(c, module((126, 34), tone=0, accent=True, armor=True, grooves=True), (512, 270), 0)

    # Rear capacitor bank: clustered blocks rather than a conventional ammunition box.
    for x in (452, 492, 532, 572):
        place(c, module((42, 58), tone=(1 if x in (492, 532) else 0), accent=(x in (492, 532)), armor=True, grooves=True), (x, 690), 90)

    d = ImageDraw.Draw(c)
    for x, y in ((424, 430), (402, 530), (426, 626), (600, 430), (622, 530), (598, 626)):
        d.rounded_rectangle((x - 6, y - 4, x + 6, y + 4), radius=3, fill=CYHI, outline=EDGE)

    box = c.getchannel('A').getbbox()
    if not box:
        raise RuntimeError('Artillery render has empty alpha')
    crop = c.crop(box)
    scale = min(450 / crop.width, 450 / crop.height)
    crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (FINAL, FINAL), (0, 0, 0, 0))
    out.alpha_composite(crop, ((FINAL - crop.width) // 2, (FINAL - crop.height) // 2))
    return out


south = artillery_south()
views = {
    'south': south,
    'west': south.transpose(Image.Transpose.ROTATE_270),
    'north': south.transpose(Image.Transpose.ROTATE_180),
    'east': south.transpose(Image.Transpose.ROTATE_90),
}

for direction, image in views.items():
    image.save(OUT / f'WNG_ReplicatorArtillery_{direction}.png', 'PNG', optimize=True)
views['south'].save(OUT / 'WNG_ReplicatorArtillery.png', 'PNG', optimize=True)

for suffix in ['', '_north', '_east', '_south', '_west']:
    p = OUT / f'WNG_ReplicatorArtillery{suffix}.png'
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

print('Generated only WNG Replicator Artillery: base + north/east/south/west, six-legged Bulwark-mass long-range support body with integrated twin pulse array.')
