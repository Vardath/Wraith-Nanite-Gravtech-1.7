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
DEEP = (30, 35, 37, 255)
STEEL = (95, 103, 104, 255)
MID = (133, 141, 141, 255)
LIGHT = (220, 225, 222, 255)
CY = (47, 157, 163, 255)
CYHI = (139, 225, 219, 255)


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

    base = (49, 55, 57, 255) if tone < 0 else ((101, 109, 110, 255) if tone == 0 else (139, 147, 147, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=3)
    d.line((pad + 5, pad + r, pad + 5, pad + h - r), fill=(168, 176, 175, 190), width=2)
    d.line((pad + r, pad + h - 6, pad + w - r, pad + h - 6), fill=(8, 11, 12, 235), width=5)
    d.line((pad + w - 6, pad + r, pad + w - 6, pad + h - r), fill=(16, 19, 20, 230), width=4)

    if armor:
        inset = 8
        d.rounded_rectangle((pad + inset, pad + inset, pad + w - inset, pad + h - inset), radius=max(2, r - 4), outline=(179, 186, 184, 215), width=3)

    if grooves:
        if w >= h:
            for off in (-9, 9):
                yy = pad + h // 2 + off
                d.line((pad + 14, yy, pad + w - 14, yy), fill=(41, 47, 49, 235), width=3)
        else:
            for off in (-9, 9):
                xx = pad + w // 2 + off
                d.line((xx, pad + 14, xx, pad + h - 14), fill=(41, 47, 49, 235), width=3)

    if w > 44 and h > 26:
        x0 = pad + int(w * .18)
        x1 = pad + int(w * .82)
        y0 = pad + int(h * .30)
        y1 = pad + int(h * .70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(35, 41, 43, 255), outline=(145, 153, 152, 220), width=2)
        if accent:
            yy = (y0 + y1) // 2
            gm = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(gm)
            gd.rounded_rectangle((x0 + 8, yy - 4, x1 - 8, yy + 4), radius=2, fill=190)
            gm = gm.filter(ImageFilter.GaussianBlur(8))
            gl = Image.new('RGBA', im.size, (*CY[:3], 0))
            gl.putalpha(gm.point(lambda q: int(q * .28)))
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


def joint(canvas, p, radius, reinforced=False):
    x, y = p
    d = ImageDraw.Draw(canvas)
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=EDGE, outline=LIGHT, width=3)
    inner = (82, 90, 91, 255) if reinforced else (58, 65, 66, 255)
    d.ellipse((x - radius * .56, y - radius * .56, x + radius * .56, y + radius * .56), fill=inner, outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width, reinforced=False):
    chain(canvas, hip, knee, width, 2, 0, armor=reinforced, grooves=True)
    joint(canvas, knee, int(width * .36), reinforced=reinforced)
    chain(canvas, knee, ankle, width * .84, 2, -1, armor=False, grooves=False)
    joint(canvas, ankle, int(width * .29), reinforced=False)
    chain(canvas, ankle, foot, width * .68, 2, 0, armor=False, grooves=False)


def glow_bar(canvas, box):
    x0, y0, x1, y1 = box
    mask = Image.new('L', canvas.size, 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle(box, radius=4, fill=180)
    blur = mask.filter(ImageFilter.GaussianBlur(10))
    gl = Image.new('RGBA', canvas.size, (*CY[:3], 0))
    gl.putalpha(blur.point(lambda q: int(q * .22)))
    canvas.alpha_composite(gl)
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle(box, radius=4, fill=CYHI, outline=EDGE, width=2)


def breach_mandible(canvas, side):
    # Block-chain crushing jaw. No smooth drill, auger or tank silhouette.
    sx = side
    root = (512 + sx * 58, 394)
    elbow = (512 + sx * 105, 335)
    wrist = (512 + sx * 126, 278)
    tip = (512 + sx * 96, 220)
    chain(canvas, root, elbow, 30, 2, tone=0, armor=True, grooves=True)
    joint(canvas, elbow, 13, reinforced=True)
    chain(canvas, elbow, wrist, 27, 2, tone=-1, armor=True, grooves=True)
    joint(canvas, wrist, 11, reinforced=True)
    chain(canvas, wrist, tip, 24, 2, tone=0, armor=True, grooves=True)

    # Inward-facing interlocking block teeth identify the pair as breaching mandibles.
    d = ImageDraw.Draw(canvas)
    for i in range(4):
        y = 255 - i * 17
        outer_x = 512 + sx * (90 - i * 5)
        inner_x = 512 + sx * (61 - i * 3)
        pts = [(outer_x, y + 8), (outer_x, y - 8), (inner_x, y)]
        d.polygon(pts, fill=(126, 134, 134, 255), outline=EDGE)
        d.line(pts + [pts[0]], fill=LIGHT, width=2, joint='curve')


def burrower_south():
    c = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    # Exactly six walking legs. Front pair are deliberately heavier bracing limbs for breaching.
    left = [
        ((426, 442), (322, 372), (238, 340), (166, 338)),
        ((412, 530), (300, 524), (206, 548), (142, 577)),
        ((440, 618), (350, 696), (278, 764), (222, 818)),
    ]
    right = [tuple((S - x, y) for x, y in pts) for pts in left]
    legs = left + right
    legs.sort(key=lambda pts: sum(y for _, y in pts) / 4)
    for hip, knee, ankle, foot in legs:
        front = hip[1] < 480
        leg(c, hip, knee, ankle, foot, 36 if front else 31, reinforced=front)

    # Hunter-mass but reinforced forebody: broader shoulders, lower centre, compact rear.
    place(c, module((196, 278), tone=-1, armor=True, grooves=True), (512, 548), 90)
    for y, w, h, tone, accent, armored in [
        (412, 202, 66, 1, False, True),
        (473, 216, 72, 0, True, True),
        (540, 204, 68, 0, False, True),
        (604, 178, 58, -1, False, False),
        (657, 144, 48, -1, False, False),
    ]:
        place(c, module((w, h), tone=tone, accent=accent, armor=armored, grooves=True), (512, y), 0)

    # Wide shoulder bridge transmits load from the mandibles into the six-legged chassis.
    place(c, module((286, 52), tone=1, accent=False, armor=True, grooves=True), (512, 414), 0)
    for x in (424, 464, 560, 600):
        place(c, module((54, 34), tone=0, accent=(x in (464, 560)), armor=True, grooves=False), (x, 414), 90)

    breach_mandible(c, -1)
    breach_mandible(c, 1)

    # Central crushing wedge between the jaws: still block-built, not a drill bit.
    d = ImageDraw.Draw(c)
    wedge = [(474, 298), (550, 298), (540, 238), (512, 194), (484, 238)]
    d.polygon(wedge, fill=EDGE)
    inner = [(483, 292), (541, 292), (532, 243), (512, 211), (492, 243)]
    d.polygon(inner, fill=(104, 113, 113, 255))
    d.line(inner + [inner[0]], fill=LIGHT, width=3, joint='curve')
    for y in (272, 252, 232):
        glow_bar(c, (502, y - 3, 522, y + 3))

    # Forehead armour blocks make the specialist read as a structural breaker, not another Hunter.
    for x in (466, 512, 558):
        place(c, module((58, 40), tone=1 if x == 512 else 0, accent=(x == 512), armor=True, grooves=True), (x, 344), 0)

    box = c.getchannel('A').getbbox()
    if not box:
        raise RuntimeError('Burrower render has empty alpha')
    crop = c.crop(box)
    scale = min(432 / crop.width, 432 / crop.height)
    crop = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (FINAL, FINAL), (0, 0, 0, 0))
    out.alpha_composite(crop, ((FINAL - crop.width) // 2, (FINAL - crop.height) // 2))
    return out


south = burrower_south()
views = {
    'south': south,
    'west': south.transpose(Image.Transpose.ROTATE_270),
    'north': south.transpose(Image.Transpose.ROTATE_180),
    'east': south.transpose(Image.Transpose.ROTATE_90),
}

for direction, image in views.items():
    image.save(OUT / f'WNG_ReplicatorBurrower_{direction}.png', 'PNG', optimize=True)
views['south'].save(OUT / 'WNG_ReplicatorBurrower.png', 'PNG', optimize=True)

for suffix in ['', '_north', '_east', '_south', '_west']:
    p = OUT / f'WNG_ReplicatorBurrower{suffix}.png'
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

print('Generated only WNG Replicator Burrower: base + north/east/south/west, exactly six walking legs, Hunter-mass reinforced breaching body with twin block mandibles and central crushing wedge.')
