from PIL import Image, ImageDraw, ImageFilter, ImageChops
from pathlib import Path

ROOT=Path("Textures/Things/Pawn/Humanlike/Apparel/Precursor")
FILES=sorted(ROOT.glob("WNG_PrecursorFieldArmor*.png"))

DARK=(20,25,31,255)
EDGE=(7,10,13,255)
CYAN=(45,215,255,255)
CYAN2=(160,245,255,255)

def vertical_gradient(size, top, bottom):
    w,h=size
    im=Image.new("RGBA",size)
    px=im.load()
    for y in range(h):
        t=y/max(1,h-1)
        c=tuple(int(top[i]*(1-t)+bottom[i]*t) for i in range(4))
        for x in range(w):
            px[x,y]=c
    return im

def mask_clip(layer, mask):
    layer.putalpha(ImageChops.multiply(layer.getchannel("A"),mask))
    return layer

def glow_line(base, pts, width=2):
    glow=Image.new("RGBA",base.size,(0,0,0,0))
    gd=ImageDraw.Draw(glow)
    gd.line(pts,fill=(30,210,255,180),width=width*3,joint="curve")
    glow=glow.filter(ImageFilter.GaussianBlur(3))
    base.alpha_composite(glow)
    d=ImageDraw.Draw(base)
    d.line(pts,fill=CYAN,width=width,joint="curve")
    d.line(pts,fill=CYAN2,width=1,joint="curve")

def render(src):
    old=Image.open(src).convert("RGBA")
    mask=old.getchannel("A")
    box=mask.getbbox()
    if not box:
        return old

    x0,y0,x1,y1=box
    w=x1-x0
    h=y1-y0
    cx=(x0+x1)//2
    low=src.stem.lower()
    side="east" if "_east" in low else "west" if "_west" in low else "north" if "_north" in low else "south"
    is_base=src.stem=="WNG_PrecursorFieldArmor"

    out=Image.new("RGBA",old.size,(0,0,0,0))
    grad=vertical_gradient(old.size,(68,77,88,255),(20,25,31,255))
    grad.putalpha(mask)
    out.alpha_composite(grad)

    dil=mask.filter(ImageFilter.MaxFilter(5))
    edge=ImageChops.subtract(dil,mask)
    edge_layer=Image.new("RGBA",old.size,EDGE)
    edge_layer.putalpha(edge)
    out.alpha_composite(edge_layer)

    panel=Image.new("RGBA",old.size,(0,0,0,0))
    d=ImageDraw.Draw(panel)

    def P(relpts, fill, outline=(8,11,15,255), width=2):
        pts=[(x0+int(px*w),y0+int(py*h)) for px,py in relpts]
        d.polygon(pts,fill=fill)
        d.line(pts+[pts[0]],fill=outline,width=width,joint="curve")

    if side in ("south","north"):
        P([(0.03,0.12),(0.22,0.02),(0.43,0.12),(0.35,0.34),(0.11,0.32)],(83,94,105,255))
        P([(0.97,0.12),(0.78,0.02),(0.57,0.12),(0.65,0.34),(0.89,0.32)],(83,94,105,255))
        P([(0.28,0.20),(0.50,0.10),(0.72,0.20),(0.68,0.72),(0.50,0.92),(0.32,0.72)],(42,50,60,255))
        P([(0.08,0.36),(0.31,0.31),(0.32,0.70),(0.15,0.78),(0.05,0.61)],(55,64,74,255))
        P([(0.92,0.36),(0.69,0.31),(0.68,0.70),(0.85,0.78),(0.95,0.61)],(55,64,74,255))
        P([(0.24,0.72),(0.50,0.82),(0.76,0.72),(0.68,0.94),(0.32,0.94)],(27,33,40,255))
    else:
        P([(0.12,0.16),(0.40,0.03),(0.77,0.14),(0.71,0.34),(0.26,0.36)],(82,93,104,255))
        P([(0.18,0.34),(0.74,0.30),(0.82,0.67),(0.61,0.88),(0.23,0.75)],(42,50,59,255))
        P([(0.28,0.49),(0.70,0.44),(0.67,0.72),(0.36,0.78)],(61,70,81,255))
        for k in range(3):
            yy=.45+k*.11
            P([(0.25,yy),(0.66,yy-.02),(0.64,yy+.06),(0.30,yy+.08)],(31,38,46,255),width=1)

    panel=mask_clip(panel,mask)
    out.alpha_composite(panel)

    hi=Image.new("RGBA",old.size,(0,0,0,0))
    hd=ImageDraw.Draw(hi)
    hd.line([(x0+2,y0+int(.18*h)),(cx,y0+int(.06*h)),(x1-3,y0+int(.18*h))],fill=(185,196,205,155),width=1)
    hd.line([(x0+int(.18*w),y0+int(.34*h)),(x0+int(.30*w),y0+int(.70*h))],fill=(130,144,156,110),width=1)
    hd.line([(x1-int(.18*w),y0+int(.34*h)),(x1-int(.30*w),y0+int(.70*h))],fill=(130,144,156,110),width=1)
    out.alpha_composite(mask_clip(hi,mask))

    if side=="south" or is_base:
        glow_line(out,[(cx,y0+int(.24*h)),(cx,y0+int(.66*h))],2)
        d=ImageDraw.Draw(out)
        r=max(2,w//22)
        d.ellipse((cx-r,y0+int(.50*h)-r,cx+r,y0+int(.50*h)+r),fill=CYAN2,outline=CYAN,width=1)
        glow_line(out,[(cx,y0+int(.66*h)),(cx-int(.10*w),y0+int(.77*h))],1)
        glow_line(out,[(cx,y0+int(.66*h)),(cx+int(.10*w),y0+int(.77*h))],1)
    elif side=="north":
        glow_line(out,[(cx,y0+int(.20*h)),(cx,y0+int(.78*h))],2)
        glow_line(out,[(cx,y0+int(.42*h)),(cx-int(.14*w),y0+int(.52*h))],1)
        glow_line(out,[(cx,y0+int(.42*h)),(cx+int(.14*w),y0+int(.52*h))],1)
    else:
        xline=x0+int((.60 if side=="east" else .40)*w)
        glow_line(out,[(xline,y0+int(.22*h)),(xline,y0+int(.72*h))],2)
        glow_line(out,[(xline,y0+int(.48*h)),(xline+int((.12 if side=="east" else -.12)*w),y0+int(.58*h))],1)

    out.putalpha(mask)
    return out

assert len(FILES)==26, f"Expected 26 Precursor Field Armour PNGs, got {len(FILES)}"
for p in FILES:
    im=render(p)
    im.save(p,optimize=True)
    print(p, im.getbbox(), p.stat().st_size)
