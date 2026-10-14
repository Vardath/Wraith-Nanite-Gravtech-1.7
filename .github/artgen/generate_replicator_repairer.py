from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
FINAL = 512

EDGE = (9, 12, 13, 255)
SHADOW = (0, 0, 0, 92)
DEEP = (31, 37, 39, 255)
STEEL = (94, 103, 105, 255)
MID = (132, 141, 141, 255)
LIGHT = (219, 225, 223, 255)
CY = (47, 159, 165, 255)
CYHI = (139, 226, 221, 255)


def module(size, tone=0, accent=False, armor=False, grooves=False):
    w, h = map(int, size)
    pad = 18
    im = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = max(4, min(w, h) // 7)

    sh = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle((pad + 5, pad + 7, pad + w + 5, pad + h + 7), radius=r, fill=SHADOW)
    sh = sh.filter(ImageFilter.GaussianBlur(5))
    im.alpha_composite(sh)

    base = (50, 57, 59, 255) if tone < 0 else ((102, 111, 113, 255) if tone == 0 else (139, 148, 149, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=3)
    d.line((pad + 5, pad + r, pad + 5, pad + h - r), fill=(170, 177, 176, 190), width=2)
    d.line((pad + r, pad + h - 6, pad + w - r, pad + h - 6), fill=(8, 11, 12, 235), width=5)
    d.line((pad + w - 6, pad + r, pad + w - 6, pad + h - r), fill=(16, 19, 20, 230), width=4)

    if armor:
        inset = 8
        d.rounded_rectangle((pad + inset, pad + inset, pad + w - inset, pad + h - inset), radius=max(2, r - 4), outline=(177, 185, 183, 215), width=3)

    if grooves:
        if w >= h:
            for off in (-9, 9):
                yy = pad + h // 2 + off
                d.line((pad + 14, yy, pad + w - 14, yy), fill=(42, 48, 50, 235), width=3)
        else:
            for off in (-9, 9):
                xx = pad + w // 2 + off
                d.line((xx, pad + 14, xx, pad + h - 14), fill=(42, 48, 50, 235), width=3)

    if w > 44 and h > 26:
        x0 = pad + int(w * .18)
        x1 = pad + int(w * .82)
        y0 = pad + int(h * .30)
        y1 = pad + int(h * .70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(36, 42, 44, 255), outline=(146, 154, 153, 220), width=2)
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
    for x, y in ((pad + 10, pad + 10), (pad + w - 10, pad + h - 10)):
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


def joint(canvas, p, radius, service=False):
    x, y = p
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=EDGE, outline=LIGHT, width=3)
    inner = CYHI if service else (60, 67, 68, 255)
    d.ellipse((x - radius * .55, y - radius * .55, x + radius * .55, y + radius * .55), fill=inner, outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width, service_tip=False):
    chain(canvas, hip, knee, width, 2, 0, armor=False, grooves=True)
    joint(canvas, knee, int(width * .34))
    chain(canvas, knee, ankle, width * .80, 2, -1, armor=False)
    joint(canvas, ankle, int(width * .28), service=service_tip)
    chain(canvas, ankle, foot, width * .64, 2, 0, armor=False)


def repair_head(canvas, center, angle):
    # Compact three-block service head with an illuminated rebuild aperture.
    place(canvas, module((58, 34), tone=1, accent=True, armor=False, grooves=True), center, angle)
    x, y = center
    mask = Image.new('L', canvas.size, 0)
    md = ImageDraw.Draw(mask)
    md.ellipse((x - 15, y - 15, x + 15, y + 15), fill=175)
    blur = mask.filter(ImageFilter.GaussianBlur(10))
    glow = Image.new('RGBA', canvas.size, (*CY[:3], 0))
    glow.putalpha(blur.point(lambda q: int(q * .22)))
    canvas.alpha_composite(glow)
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - 12, y - 12, x + 12, y + 12), fill=DEEP, outline=CYHI, width=3)
    d.ellipse((x - 5, y - 5, x + 5, y + 5), fill=CYHI)


def repairer_south():
    c = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    # Exactly six locomotor legs. Front pair are slimmer precision-service legs rather than attack scythes.
    left = [
        ((432, 432), (338, 362), (266, 330), (202, 324)),
        ((414, 523), (310, 515), (228, 538), (168, 565)),
        ((438, 612), (354, 688), (286, 756), (236, 808)),
    ]
    right = [tuple((S - x, y) for x, y in pts) for pts in left]
    legs = left + right
    legs.sort(key=lambda pts: sum(y for _, y in pts) / 4)
    for idx, (hip, knee, ankle, foot) in enumerate(legs):
        front_leg = hip[1] < 470
        leg(c, hip, knee, ankle, foot, 28 if front_leg else 31, service_tip=front_leg)

    # Narrow Hunter-mass service chassis: compact, light and clearly not a combat tank.
    place(c, module((172, 258), tone=-1, armor=False, grooves=True), (512, 542), 90)
    for y, w, h, tone, accent in [
        (421, 150, 52, 1, False),
        (475, 182, 58, 0, True),
        (534, 192, 62, 0, False),
        (594, 174, 56, -1, False),
        (648, 140, 46, -1, False),
    ]:
        place(c, module((w, h), tone=tone, accent=accent, armor=False, grooves=True), (512, y), 0)

    # Distinctive service yoke and block magazine across the dorsal body.
    place(c, module((250, 42), tone=1, accent=False, armor=False, grooves=True), (512, 510), 0)
    for x in (438, 476, 548, 586):
        place(c, module((46, 30), tone=0, accent=(x in (476, 548)), armor=False), (x, 510), 90)

    # Two integrated repair tool rails. They are short forebody equipment, not extra walking legs.
    for sx in (-1, 1):
        root = (512 + sx * 46, 430)
        elbow = (512 + sx * 82, 364)
        tip = (512 + sx * 112, 310)
        chain(c, root, elbow, 17, 2, tone=0, armor=False, grooves=True)
        joint(c, elbow, 8, service=True)
        chain(c, elbow, tip, 13, 2, tone=-1, armor=False, grooves=False)
        repair_head(c, tip, 90 + sx * 13)

    # Central reconstruction aperture: support identity without a weapon silhouette.
    repair_head(c, (512, 372), 0)
    place(c, module((104, 28), tone=1, accent=True, armor=False, grooves=True), (512, 338), 0)

    d = ImageDraw.Draw(c)
    for x, y in ((432, 432), (414, 523), (438, 612), (592, 432), (610, 523), (586, 612)):
        d.rounded_rectangle((x - 5, y - 3, x + 5, y + 3), radius=2, fill=CYHI, outline=EDGE)

    box = c.getchannel('A').getbbox()
    if not box:
        raise RuntimeError('Repairer render has empty alpha')
    crop = c.crop(box)
    scale = min(420 / crop.width, 420 / crop.height)
    crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (FINAL, FINAL), (0, 0, 0, 0))
    out.alpha_composite(crop, ((FINAL - crop.width) // 2, (FINAL - crop.height) // 2))
    return out


south = repairer_south()
views = {
    'south': south,
    'west': south.transpose(Image.Transpose.ROTATE_270),
    'north': south.transpose(Image.Transpose.ROTATE_180),
    'east': south.transpose(Image.Transpose.ROTATE_90),
}

for direction, image in views.items():
    image.save(OUT / f'WNG_ReplicatorRepairer_{direction}.png', 'PNG', optimize=True)
views['south'].save(OUT / 'WNG_ReplicatorRepairer.png', 'PNG', optimize=True)

for suffix in ['', '_north', '_east', '_south', '_west']:
    p = OUT / f'WNG_ReplicatorRepairer{suffix}.png'
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

print('Generated only WNG Replicator Repairer: base + north/east/south/west, six-legged Hunter-mass support body with integrated service yoke and repair tool clusters.')
