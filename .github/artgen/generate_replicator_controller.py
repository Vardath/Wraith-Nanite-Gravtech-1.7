from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
FINAL = 512

EDGE = (9, 12, 13, 255)
SHADOW = (0, 0, 0, 100)
DEEP = (31, 36, 38, 255)
STEEL = (92, 100, 102, 255)
MID = (128, 136, 137, 255)
LIGHT = (218, 224, 222, 255)
CY = (48, 160, 166, 255)
CYHI = (138, 226, 220, 255)


def module(size, tone=0, accent=False, armor=False):
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

    base = (50, 56, 58, 255) if tone < 0 else ((101, 109, 111, 255) if tone == 0 else (136, 144, 145, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=3)
    d.line((pad + 5, pad + r, pad + 5, pad + h - r), fill=(166, 173, 173, 190), width=2)
    d.line((pad + r, pad + h - 6, pad + w - r, pad + h - 6), fill=(8, 11, 12, 235), width=5)
    d.line((pad + w - 6, pad + r, pad + w - 6, pad + h - r), fill=(16, 19, 20, 230), width=4)

    if armor:
        inset = 8
        d.rounded_rectangle((pad + inset, pad + inset, pad + w - inset, pad + h - inset), radius=max(2, r - 4), outline=(173, 181, 180, 210), width=3)

    if w > 44 and h > 26:
        x0 = pad + int(w * .18)
        x1 = pad + int(w * .82)
        y0 = pad + int(h * .30)
        y1 = pad + int(h * .70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(36, 42, 44, 255), outline=(145, 152, 152, 220), width=2)
        if accent:
            yy = (y0 + y1) // 2
            gm = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(gm)
            gd.rounded_rectangle((x0 + 8, yy - 4, x1 - 8, yy + 4), radius=2, fill=190)
            gm = gm.filter(ImageFilter.GaussianBlur(8))
            gl = Image.new('RGBA', im.size, (*CY[:3], 0))
            gl.putalpha(gm.point(lambda q: int(q * .30)))
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


def chain(canvas, a, b, width, parts=2, tone=0, armor=False):
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
        place(canvas, module((seg, width), tone=tone, armor=armor), center, angle)


def joint(canvas, p, radius):
    x, y = p
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=EDGE, outline=LIGHT, width=3)
    d.ellipse((x - radius * .56, y - radius * .56, x + radius * .56, y + radius * .56), fill=(58, 65, 66, 255), outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width):
    chain(canvas, hip, knee, width, 2, 0, armor=True)
    joint(canvas, knee, int(width * .37))
    chain(canvas, knee, ankle, width * .84, 2, -1, armor=False)
    joint(canvas, ankle, int(width * .30))
    chain(canvas, ankle, foot, width * .69, 2, 0, armor=False)


def glow_disc(canvas, center, radius):
    x, y = center
    mask = Image.new('L', canvas.size, 0)
    md = ImageDraw.Draw(mask)
    md.ellipse((x - radius, y - radius, x + radius, y + radius), fill=190)
    blur = mask.filter(ImageFilter.GaussianBlur(12))
    glow = Image.new('RGBA', canvas.size, (*CY[:3], 0))
    glow.putalpha(blur.point(lambda q: int(q * .28)))
    canvas.alpha_composite(glow)
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=EDGE, outline=LIGHT, width=4)
    d.ellipse((x - radius + 11, y - radius + 11, x + radius - 11, y + radius - 11), fill=DEEP, outline=CYHI, width=4)


def controller_south():
    c = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    # Six medium-load legs: more deliberate and stable than Hunter, lighter than Bulwark.
    left = [
        ((434, 435), (328, 353), (246, 315), (176, 306)),
        ((414, 525), (300, 515), (205, 538), (142, 565)),
        ((438, 620), (348, 699), (274, 768), (220, 820)),
    ]
    right = [tuple((S - x, y) for x, y in pts) for pts in left]
    legs = left + right
    legs.sort(key=lambda pts: sum(y for _, y in pts) / 4)
    for hip, knee, ankle, foot in legs:
        leg(c, hip, knee, ankle, foot, 34)

    # Medium command chassis, narrower than Bulwark and with a distinct dorsal architecture.
    place(c, module((210, 286), tone=-1, armor=True), (512, 540), 90)
    for y, w, h, tone, accent in [
        (406, 182, 62, 1, False),
        (470, 208, 68, 0, True),
        (536, 222, 72, 0, False),
        (604, 202, 66, -1, False),
        (667, 168, 54, -1, False),
    ]:
        place(c, module((w, h), tone=tone, accent=accent, armor=(y in (470, 536))), (512, y), 0)

    # Command lattice: an integrated ring of Replicator blocks, not a generic antenna or glowing dot.
    glow_disc(c, (512, 350), 50)
    lattice_centers = []
    for i in range(6):
        a = math.radians(-90 + i * 60)
        lattice_centers.append((512 + math.cos(a) * 94, 350 + math.sin(a) * 70))
    for i, p in enumerate(lattice_centers):
        chain(c, (512, 350), p, 14, 2, tone=-1, armor=False)
        place(c, module((54, 30), tone=(1 if i % 2 == 0 else 0), accent=True, armor=False), p, -90 + i * 60)

    # Forward coordination vanes: sensor/relay structures, not weapons.
    for sx in (-1, 1):
        root = (512 + sx * 44, 390)
        mid = (512 + sx * 82, 320)
        tip = (512 + sx * 112, 252)
        chain(c, root, mid, 18, 2, 0, armor=False)
        joint(c, mid, 8)
        chain(c, mid, tip, 14, 2, -1, armor=False)
        place(c, module((62, 24), tone=1, accent=True, armor=False), tip, 90 + sx * 12)

    # Small coordination lights at the leg roots tie the whole machine to the same swarm network.
    d = ImageDraw.Draw(c)
    for x, y in ((434, 435), (414, 525), (438, 620), (590, 435), (610, 525), (586, 620)):
        d.rounded_rectangle((x - 6, y - 4, x + 6, y + 4), radius=3, fill=CYHI, outline=EDGE)

    box = c.getchannel('A').getbbox()
    if not box:
        raise RuntimeError('Controller render has empty alpha')
    crop = c.crop(box)
    scale = min(438 / crop.width, 438 / crop.height)
    crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (FINAL, FINAL), (0, 0, 0, 0))
    out.alpha_composite(crop, ((FINAL - crop.width) // 2, (FINAL - crop.height) // 2))
    return out


south = controller_south()
views = {
    'south': south,
    'west': south.transpose(Image.Transpose.ROTATE_270),
    'north': south.transpose(Image.Transpose.ROTATE_180),
    'east': south.transpose(Image.Transpose.ROTATE_90),
}

for direction, image in views.items():
    image.save(OUT / f'WNG_ReplicatorController_{direction}.png', 'PNG', optimize=True)
views['south'].save(OUT / 'WNG_ReplicatorController.png', 'PNG', optimize=True)

for suffix in ['', '_north', '_east', '_south', '_west']:
    p = OUT / f'WNG_ReplicatorController{suffix}.png'
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

print('Generated only WNG Replicator Controller: base + north/east/south/west, six-legged medium command body with integrated coordination lattice.')
