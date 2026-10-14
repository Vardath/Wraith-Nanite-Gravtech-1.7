from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
FINAL = 512

EDGE = (9, 12, 13, 255)
SHADOW = (0, 0, 0, 110)
DEEP = (29, 34, 36, 255)
STEEL = (89, 97, 99, 255)
MID = (128, 136, 137, 255)
LIGHT = (220, 225, 223, 255)
CY = (49, 157, 164, 255)
CYHI = (135, 223, 218, 255)


def module(size, tone=0, accent=False, armor=False, rib=False):
    w, h = map(int, size)
    pad = 22
    im = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = max(4, min(w, h) // 7)

    sh = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle((pad + 7, pad + 9, pad + w + 7, pad + h + 9), radius=r, fill=SHADOW)
    sh = sh.filter(ImageFilter.GaussianBlur(7))
    im.alpha_composite(sh)

    base = (48, 54, 56, 255) if tone < 0 else ((102, 110, 112, 255) if tone == 0 else (139, 147, 148, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=4)
    d.line((pad + 6, pad + r, pad + 6, pad + h - r), fill=(171, 179, 178, 200), width=3)
    d.line((pad + r, pad + h - 7, pad + w - r, pad + h - 7), fill=(8, 11, 12, 240), width=6)
    d.line((pad + w - 7, pad + r, pad + w - 7, pad + h - r), fill=(15, 18, 19, 235), width=5)

    if armor:
        inset = 9
        d.rounded_rectangle((pad + inset, pad + inset, pad + w - inset, pad + h - inset), radius=max(2, r - 4), outline=(183, 190, 188, 220), width=4)
        if w >= h:
            cy = pad + h // 2
            d.rectangle((pad + int(w*.16), cy - 6, pad + int(w*.84), cy + 6), fill=(34, 40, 42, 255))
        else:
            cx = pad + w // 2
            d.rectangle((cx - 6, pad + int(h*.16), cx + 6, pad + int(h*.84)), fill=(34, 40, 42, 255))

    if rib:
        for off in (-12, 12):
            if w >= h:
                yy = pad + h//2 + off
                d.line((pad+14, yy, pad+w-14, yy), fill=(173,181,180,180), width=3)
            else:
                xx = pad + w//2 + off
                d.line((xx, pad+14, xx, pad+h-14), fill=(173,181,180,180), width=3)

    if w > 46 and h > 28:
        x0 = pad + int(w * .18)
        x1 = pad + int(w * .82)
        y0 = pad + int(h * .30)
        y1 = pad + int(h * .70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r // 3), fill=(34, 40, 42, 255), outline=(149, 156, 155, 220), width=2)
        if accent:
            yy = (y0 + y1) // 2
            gm = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(gm)
            gd.rounded_rectangle((x0 + 10, yy - 5, x1 - 10, yy + 5), radius=3, fill=185)
            gm = gm.filter(ImageFilter.GaussianBlur(10))
            gl = Image.new('RGBA', im.size, (*CY[:3], 0))
            gl.putalpha(gm.point(lambda q: int(q * .28)))
            im.alpha_composite(gl)
            d = ImageDraw.Draw(im)
            d.rounded_rectangle((x0 + 11, yy - 3, x1 - 11, yy + 3), radius=2, fill=CYHI)

    rr = max(2, min(w, h)//18)
    for x, y in ((pad + 12, pad + 12), (pad + w - 12, pad + h - 12)):
        d.ellipse((x-rr, y-rr, x+rr, y+rr), fill=(22, 27, 28, 255), outline=LIGHT, width=1)
    return im


def place(canvas, image, center, angle):
    rot = image.rotate(angle, Image.Resampling.BICUBIC, expand=True)
    canvas.alpha_composite(rot, (round(center[0]-rot.width/2), round(center[1]-rot.height/2)))


def chain(canvas, a, b, width, parts=2, tone=0, armor=False, rib=False):
    ax, ay = a; bx, by = b
    dx, dy = bx-ax, by-ay
    length = math.hypot(dx, dy)
    angle = math.degrees(math.atan2(dy, dx))
    ux, uy = dx/length, dy/length
    gap = 7
    seg = (length-gap*(parts-1))/parts
    for i in range(parts):
        dist = i*(seg+gap)+seg/2
        place(canvas, module((seg,width), tone=tone, armor=armor, rib=rib), (ax+ux*dist, ay+uy*dist), angle)


def joint(canvas, p, radius):
    x,y=p
    d=ImageDraw.Draw(canvas)
    d.ellipse((x-radius,y-radius,x+radius,y+radius), fill=EDGE, outline=LIGHT, width=4)
    d.ellipse((x-radius*.58,y-radius*.58,x+radius*.58,y+radius*.58), fill=(61,68,69,255), outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width):
    chain(canvas, hip, knee, width, 2, 0, armor=True, rib=True)
    joint(canvas, knee, int(width*.39))
    chain(canvas, knee, ankle, width*.88, 2, -1, armor=True)
    joint(canvas, ankle, int(width*.31))
    chain(canvas, ankle, foot, width*.74, 2, 0, armor=True)


def mandible(canvas, root, elbow, tip, width):
    chain(canvas, root, elbow, width, 2, 0, armor=True)
    joint(canvas, elbow, int(width*.34))
    chain(canvas, elbow, tip, width*.74, 2, -1, armor=True)
    # crushing wedge at the end
    dx,dy = tip[0]-elbow[0], tip[1]-elbow[1]
    ang = math.degrees(math.atan2(dy,dx))
    place(canvas, module((72, width*.82), tone=1, armor=True, rib=True), tip, ang)


def titan_south():
    c = Image.new('RGBA', (S,S), (0,0,0,0))

    # Six enormous support legs, three pairs, with a taller siege posture than Bulwark.
    left = [
        ((421, 410), (294, 310), (198, 250), (120, 226)),
        ((394, 525), (252, 505), (144, 530), (78, 563)),
        ((420, 645), (310, 744), (226, 830), (170, 886)),
    ]
    right = [tuple((S-x,y) for x,y in pts) for pts in left]
    legs = left + right
    legs.sort(key=lambda pts: sum(y for _,y in pts)/4)
    for hip,knee,ankle,foot in legs:
        leg(c, hip,knee,ankle,foot, 55)

    # Tower-like heavy chassis: visibly assembled from many smaller blocks, not a scaled Bulwark slab.
    place(c, module((330, 360), tone=-1, armor=True, rib=True), (512, 548), 90)
    for y,w,h,tone,accent in [
        (350, 218, 76, 1, False),
        (425, 290, 92, 1, False),
        (510, 320, 104, 0, True),
        (604, 290, 98, 0, False),
        (690, 236, 82, -1, False),
    ]:
        place(c, module((w,h), tone=tone, accent=accent, armor=True, rib=True), (512,y), 0)

    # Vertical flanking towers give Titan its own silhouette and communicate stacked swarm mass.
    for x in (372,652):
        place(c, module((220,64), tone=1, armor=True, rib=True), (x,500), 90)
        place(c, module((150,54), tone=0, armor=True), (x,650), 90)

    # Huge siege mandibles: crushing structures, not Hunter scythes.
    mandible(c, (462,342), (405,255), (345,165), 42)
    mandible(c, (562,342), (619,255), (679,165), 42)
    place(c, module((176,54), tone=1, armor=True, rib=True), (512,285), 0)
    place(c, module((116,38), tone=0, accent=True, armor=True), (512,242), 0)

    d=ImageDraw.Draw(c)
    for x,y in ((421,410),(394,525),(420,645),(603,410),(630,525),(604,645)):
        d.rounded_rectangle((x-8,y-5,x+8,y+5), radius=3, fill=CYHI, outline=EDGE)

    box=c.getchannel('A').getbbox()
    if not box: raise RuntimeError('Titan render has empty alpha')
    crop=c.crop(box)
    scale=min(452/crop.width, 452/crop.height)
    crop=crop.resize((round(crop.width*scale), round(crop.height*scale)), Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(FINAL,FINAL),(0,0,0,0))
    out.alpha_composite(crop,((FINAL-crop.width)//2,(FINAL-crop.height)//2))
    return out

south=titan_south()
views={
    'south':south,
    'west':south.transpose(Image.Transpose.ROTATE_270),
    'north':south.transpose(Image.Transpose.ROTATE_180),
    'east':south.transpose(Image.Transpose.ROTATE_90),
}
for direction,image in views.items():
    image.save(OUT/f'WNG_ReplicatorTitan_{direction}.png','PNG',optimize=True)
views['south'].save(OUT/'WNG_ReplicatorTitan.png','PNG',optimize=True)

for suffix in ['', '_north','_east','_south','_west']:
    p=OUT/f'WNG_ReplicatorTitan{suffix}.png'
    with Image.open(p) as chk:
        chk.load()
        if chk.mode!='RGBA' or chk.size!=(512,512):
            raise RuntimeError(f'{p}: expected 512x512 RGBA')
        a=chk.getchannel('A')
        if not a.getbbox(): raise RuntimeError(f'{p}: empty alpha')
        edges=[
            a.crop((0,0,512,1)).getextrema()[1],
            a.crop((0,511,512,512)).getextrema()[1],
            a.crop((0,0,1,512)).getextrema()[1],
            a.crop((511,0,512,512)).getextrema()[1],
        ]
        if any(edges): raise RuntimeError(f'{p}: alpha touches canvas edge {edges}')

print('Generated only WNG Replicator Titan: base + north/east/south/west, six-legged heavy siege form with huge mandibles.')
