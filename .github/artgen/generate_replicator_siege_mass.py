from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
FINAL = 512

EDGE = (8, 11, 12, 255)
SHADOW = (0, 0, 0, 115)
DARK = (41, 47, 49, 255)
STEEL = (96, 104, 106, 255)
MID = (136, 144, 145, 255)
LIGHT = (222, 227, 225, 255)
CY = (46, 154, 161, 255)
CYHI = (132, 222, 216, 255)


def module(size, tone=0, accent=False, armor=False, rib=False, ram=False):
    w, h = map(int, size)
    pad = 24
    im = Image.new('RGBA', (w + pad * 2 + (32 if ram else 0), h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = max(4, min(w, h) // 7)

    sh = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle((pad + 8, pad + 10, pad + w + 8, pad + h + 10), radius=r, fill=SHADOW)
    sh = sh.filter(ImageFilter.GaussianBlur(7))
    im.alpha_composite(sh)

    base = (49, 55, 57, 255) if tone < 0 else ((105, 113, 115, 255) if tone == 0 else (145, 153, 154, 255))
    d.rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=EDGE)
    d.rounded_rectangle((pad + 4, pad + 4, pad + w - 4, pad + h - 4), radius=max(2, r - 3), fill=base)
    d.line((pad + r, pad + 5, pad + w - r, pad + 5), fill=LIGHT, width=4)
    d.line((pad + 6, pad + r, pad + 6, pad + h - r), fill=(174, 182, 181, 210), width=3)
    d.line((pad + r, pad + h - 7, pad + w - r, pad + h - 7), fill=(7, 10, 11, 245), width=6)
    d.line((pad + w - 7, pad + r, pad + w - 7, pad + h - r), fill=(14, 18, 19, 240), width=5)

    if armor:
        inset = 10
        d.rounded_rectangle((pad + inset, pad + inset, pad + w - inset, pad + h - inset), radius=max(2, r - 4), outline=(188, 195, 193, 225), width=4)
        if w >= h:
            cy = pad + h // 2
            d.rectangle((pad + int(w*.13), cy - 7, pad + int(w*.87), cy + 7), fill=(33, 39, 41, 255))
        else:
            cx = pad + w // 2
            d.rectangle((cx - 7, pad + int(h*.13), cx + 7, pad + int(h*.87)), fill=(33, 39, 41, 255))

    if rib:
        if w >= h:
            for off in (-14, 14):
                yy = pad + h//2 + off
                d.line((pad+14, yy, pad+w-14, yy), fill=(177,185,183,185), width=3)
        else:
            for off in (-14, 14):
                xx = pad + w//2 + off
                d.line((xx, pad+14, xx, pad+h-14), fill=(177,185,183,185), width=3)

    if w > 48 and h > 30:
        x0, x1 = pad + int(w*.18), pad + int(w*.82)
        y0, y1 = pad + int(h*.30), pad + int(h*.70)
        d.rounded_rectangle((x0, y0, x1, y1), radius=max(2, r//3), fill=(33, 39, 41, 255), outline=(151, 158, 157, 220), width=2)
        if accent:
            yy = (y0 + y1) // 2
            glow = Image.new('L', im.size, 0)
            gd = ImageDraw.Draw(glow)
            gd.rounded_rectangle((x0+10, yy-5, x1-10, yy+5), radius=3, fill=190)
            glow = glow.filter(ImageFilter.GaussianBlur(10))
            lay = Image.new('RGBA', im.size, (*CY[:3], 0))
            lay.putalpha(glow.point(lambda q: int(q*.30)))
            im.alpha_composite(lay)
            d = ImageDraw.Draw(im)
            d.rounded_rectangle((x0+11, yy-3, x1-11, yy+3), radius=2, fill=CYHI)

    if ram:
        tipx = pad + w + 28
        d.polygon([
            (pad+w-4, pad+5),
            (tipx, pad+h//2),
            (pad+w-4, pad+h-5),
            (pad+w-18, pad+h//2),
        ], fill=(70, 77, 79, 255), outline=LIGHT)
        d.line((pad+w-9, pad+h//2, tipx-4, pad+h//2), fill=(24, 29, 30, 255), width=5)

    rr = max(2, min(w,h)//18)
    for x,y in ((pad+12,pad+12),(pad+w-12,pad+h-12)):
        d.ellipse((x-rr,y-rr,x+rr,y+rr), fill=(21,26,27,255), outline=LIGHT, width=1)
    return im


def place(canvas, image, center, angle):
    r = image.rotate(angle, Image.Resampling.BICUBIC, expand=True)
    canvas.alpha_composite(r, (round(center[0]-r.width/2), round(center[1]-r.height/2)))


def chain(canvas, a, b, width, parts=2, tone=0, armor=False, rib=False):
    ax, ay = a; bx, by = b
    dx, dy = bx-ax, by-ay
    length = math.hypot(dx,dy)
    ang = math.degrees(math.atan2(dy,dx))
    ux, uy = dx/length, dy/length
    gap = 8
    seg = (length-gap*(parts-1))/parts
    for i in range(parts):
        dist = i*(seg+gap)+seg/2
        place(canvas, module((seg,width), tone=tone, armor=armor, rib=rib), (ax+ux*dist, ay+uy*dist), ang)


def joint(canvas, p, radius):
    x,y=p
    d=ImageDraw.Draw(canvas)
    d.ellipse((x-radius,y-radius,x+radius,y+radius), fill=EDGE, outline=LIGHT, width=4)
    d.ellipse((x-radius*.58,y-radius*.58,x+radius*.58,y+radius*.58), fill=(63,70,71,255), outline=MID, width=2)


def leg(canvas, hip, knee, ankle, foot, width):
    chain(canvas, hip, knee, width, 2, 0, armor=True, rib=True)
    joint(canvas, knee, int(width*.40))
    chain(canvas, knee, ankle, width*.90, 2, -1, armor=True, rib=True)
    joint(canvas, ankle, int(width*.32))
    chain(canvas, ankle, foot, width*.76, 2, 0, armor=True)


def siege_mass_south():
    c = Image.new('RGBA',(S,S),(0,0,0,0))

    # Six major load-bearing legs only: much broader and heavier than Titan.
    left = [
        ((400,392),(270,306),(164,270),(82,278)),
        ((370,520),(220,500),(112,535),(54,580)),
        ((402,654),(290,758),(196,842),(132,900)),
    ]
    right = [tuple((S-x,y) for x,y in pts) for pts in left]
    legs = left + right
    legs.sort(key=lambda pts: sum(y for _,y in pts)/4)
    for hip,knee,ankle,foot in legs:
        leg(c,hip,knee,ankle,foot,68)

    # Broad mobile concentration of swarm material, not a stretched insect.
    place(c, module((430,360), tone=-1, armor=True, rib=True), (512,548), 90)
    for y,w,h,tone,accent in [
        (360,330,94,1,False),
        (444,390,110,1,False),
        (538,420,118,0,True),
        (640,382,108,0,False),
        (730,300,88,-1,False),
    ]:
        place(c, module((w,h), tone=tone, accent=accent, armor=True, rib=True), (512,y), 0)

    # Massive flanking block banks make the chassis visibly denser than Titan.
    for x in (335,689):
        place(c, module((250,74), tone=1, armor=True, rib=True), (x,505), 90)
        place(c, module((180,62), tone=0, armor=True, rib=True), (x,670), 90)

    # Devouring ram: one brutal forward structure built from multiple Replicator block beams.
    place(c, module((260,72), tone=1, armor=True, rib=True), (512,292), 0)
    place(c, module((230,64), tone=0, armor=True, ram=True), (512,220), -90)
    for x in (455,569):
        place(c, module((150,48), tone=0, armor=True, ram=True), (x,250), -82 if x<512 else -98)
    place(c, module((120,40), tone=-1, accent=True, armor=True), (512,178), 0)

    # Reinforced rear mass makes it read as a moving swarm concentration rather than a single enlarged chassis.
    for x in (412,512,612):
        place(c, module((118,50), tone=0, armor=True, rib=True), (x,786), 0)

    d=ImageDraw.Draw(c)
    for x,y in ((400,392),(370,520),(402,654),(624,392),(654,520),(622,654)):
        d.rounded_rectangle((x-10,y-6,x+10,y+6), radius=4, fill=CYHI, outline=EDGE)

    box=c.getchannel('A').getbbox()
    if not box: raise RuntimeError('Siege Mass render has empty alpha')
    crop=c.crop(box)
    scale=min(456/crop.width,456/crop.height)
    crop=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(FINAL,FINAL),(0,0,0,0))
    out.alpha_composite(crop,((FINAL-crop.width)//2,(FINAL-crop.height)//2))
    return out

south=siege_mass_south()
views={
    'south':south,
    'west':south.transpose(Image.Transpose.ROTATE_270),
    'north':south.transpose(Image.Transpose.ROTATE_180),
    'east':south.transpose(Image.Transpose.ROTATE_90),
}
for direction,image in views.items():
    image.save(OUT/f'WNG_ReplicatorSiegeMass_{direction}.png','PNG',optimize=True)
views['south'].save(OUT/'WNG_ReplicatorSiegeMass.png','PNG',optimize=True)

for suffix in ['', '_north','_east','_south','_west']:
    p=OUT/f'WNG_ReplicatorSiegeMass{suffix}.png'
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

print('Generated only WNG Replicator Siege Mass: base + north/east/south/west, six-legged largest swarm concentration with devouring ram.')
