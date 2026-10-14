from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math, random

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures/Things/Building/Precursor/Gravship"
OUT.mkdir(parents=True, exist_ok=True)

W = H = 512
S = 3
SS = W * S
random.seed(137)

IVORY_TOP=(238,236,222)
IVORY_BOTTOM=(166,165,151)
STONE_TOP=(167,160,143)
STONE_BOTTOM=(95,93,84)
DARK_TOP=(58,68,75)
DARK_BOTTOM=(23,30,35)
EDGE=(24,30,34,255)
GOLD=(179,142,76,235)
CYAN=(62,221,250,255)
CYAN_HI=(190,251,255,255)

def sc(v): return int(round(v*S))
def pts(seq): return [(sc(x),sc(y)) for x,y in seq]

def canvas():
    return Image.new("RGBA",(SS,SS),(0,0,0,0))

def poly_mask(seq):
    m=Image.new("L",(SS,SS),0)
    ImageDraw.Draw(m).polygon(pts(seq),fill=255)
    return m

def rr_mask(box,r):
    m=Image.new("L",(SS,SS),0)
    ImageDraw.Draw(m).rounded_rectangle(tuple(sc(x) for x in box),radius=sc(r),fill=255)
    return m

def ellipse_mask(box):
    m=Image.new("L",(SS,SS),0)
    ImageDraw.Draw(m).ellipse(tuple(sc(x) for x in box),fill=255)
    return m

def vgrad(top,bottom):
    g=Image.new("RGBA",(SS,SS),(0,0,0,0))
    d=ImageDraw.Draw(g)
    for y in range(SS):
        t=y/max(1,SS-1)
        c=tuple(int(top[i]*(1-t)+bottom[i]*t) for i in range(3))+(255,)
        d.line((0,y,SS,y),fill=c)
    return g

def fill_grad(img,mask,top,bottom):
    g=vgrad(top,bottom); g.putalpha(mask); img.alpha_composite(g)

def fill(img,mask,color):
    lay=Image.new("RGBA",(SS,SS),color); lay.putalpha(mask); img.alpha_composite(lay)

def glow(img,mask,color=(54,216,255),blur=10,strength=.8):
    a=mask.filter(ImageFilter.GaussianBlur(sc(blur)))
    a=a.point(lambda p:min(255,int(p*strength)))
    lay=Image.new("RGBA",(SS,SS),color+(0,)); lay.putalpha(a); img.alpha_composite(lay)

def draw_poly_outline(d,seq,color=EDGE,width=4):
    p=pts(seq)
    d.line(p+[p[0]],fill=color,width=sc(width),joint="curve")

def shadow(img):
    sh=Image.new("RGBA",(SS,SS),(0,0,0,0))
    d=ImageDraw.Draw(sh)
    d.rounded_rectangle((sc(34),sc(80),sc(478),sc(430)),radius=sc(66),fill=(0,0,0,155))
    sh=sh.filter(ImageFilter.GaussianBlur(sc(14)))
    img.alpha_composite(sh)

def surface_flecks(img,mask,count=80):
    d=ImageDraw.Draw(img)
    for _ in range(count):
        x=random.randint(sc(50),sc(462)); y=random.randint(sc(70),sc(440))
        if mask.getpixel((x,y))>220:
            if random.random()<.65:
                col=(245,242,225,random.randint(18,38))
            else:
                col=(78,72,59,random.randint(22,42))
            dx=random.randint(1,4)*S
            d.line((x,y,x+dx,y+random.randint(-1,1)*S),fill=col,width=max(1,S//2))

def panel_lines(d, box, count=4):
    x0,y0,x1,y1=box
    for i in range(count):
        y=y0+(i+1)*(y1-y0)/(count+1)
        d.line((sc(x0+8),sc(y),sc(x1-8),sc(y)),fill=(91,223,245,120),width=sc(1))

def render_south():
    img=canvas()
    shadow(img)

    # Overall 3x2 console silhouette: broad forward console, angled wings, open operator side.
    outer=[
        (52,116),(112,72),(400,72),(460,116),(452,334),
        (402,382),(337,382),(315,420),(197,420),(175,382),(110,382),(60,334)
    ]
    outer_m=poly_mask(outer)
    fill_grad(img,outer_m,IVORY_TOP,IVORY_BOTTOM)
    d=ImageDraw.Draw(img)
    draw_poly_outline(d,outer,EDGE,6)
    draw_poly_outline(d,[(73,128),(124,94),(388,94),(439,128),(431,318),(390,354),(325,354),(300,393),(212,393),(187,354),(122,354),(81,318)],(202,174,114,170),2)

    # Main dark console recess.
    main=[(91,132),(130,108),(382,108),(421,132),(415,302),(378,332),(318,332),(291,363),(221,363),(194,332),(134,332),(97,302)]
    main_m=poly_mask(main)
    fill_grad(img,main_m,DARK_TOP,DARK_BOTTOM)
    draw_poly_outline(d,main,(31,38,43,255),4)

    # Front display bank - makes it clearly a console, not a crystal.
    display=rr_mask((154,119,358,206),18)
    glow(img,display,color=(40,185,225),blur=12,strength=.22)
    fill_grad(img,display,(30,68,84),(14,39,52))
    d=ImageDraw.Draw(img)
    d.rounded_rectangle((sc(154),sc(119),sc(358),sc(206)),radius=sc(18),outline=(146,151,138,255),width=sc(4))
    d.rounded_rectangle((sc(170),sc(132),sc(342),sc(191)),radius=sc(12),outline=(71,202,235,210),width=sc(2))

    # Navigation/holo display: contained projection surface, not freestanding crystal.
    holo=rr_mask((206,135,306,188),12)
    glow(img,holo,color=(47,219,255),blur=12,strength=.75)
    fill_grad(img,holo,(31,148,183),(16,76,103))
    d=ImageDraw.Draw(img)
    # stylized star-map / course plot
    d.line(pts([(222,171),(248,147),(272,165),(294,143)]),fill=(174,248,255,230),width=sc(2))
    for x,y in [(222,171),(248,147),(272,165),(294,143)]:
        d.ellipse((sc(x-3),sc(y-3),sc(x+3),sc(y+3)),fill=CYAN_HI)
    d.line((sc(177),sc(151),sc(198),sc(151)),fill=CYAN,width=sc(2))
    d.line((sc(177),sc(161),sc(194),sc(161)),fill=(105,225,245,210),width=sc(2))
    d.line((sc(316),sc(151),sc(337),sc(151)),fill=CYAN,width=sc(2))
    d.line((sc(320),sc(161),sc(337),sc(161)),fill=(105,225,245,210),width=sc(2))

    # Angled left/right pilot control wings with banks of controls.
    left=[(104,222),(174,205),(205,229),(190,320),(132,339),(101,303)]
    right=[(408,222),(338,205),(307,229),(322,320),(380,339),(411,303)]
    for p in (left,right):
        m=poly_mask(p)
        fill_grad(img,m,STONE_TOP,STONE_BOTTOM)
        draw_poly_outline(ImageDraw.Draw(img),p,(45,48,46,255),4)
    # inset screens on control wings
    lscreen=[(124,237),(171,224),(185,239),(174,287),(132,299),(118,283)]
    rscreen=[(388,237),(341,224),(327,239),(338,287),(380,299),(394,283)]
    for p in (lscreen,rscreen):
        m=poly_mask(p)
        glow(img,m,color=(51,210,242),blur=8,strength=.35)
        fill_grad(img,m,(33,104,126),(15,48,63))
        draw_poly_outline(ImageDraw.Draw(img),p,(126,203,211,210),2)

    d=ImageDraw.Draw(img)
    # console buttons and sliders, asymmetric enough to look functional
    for y in [246,258,270]:
        d.line((sc(132),sc(y),sc(168),sc(y-6)),fill=(102,225,244,210),width=sc(1))
        d.line((sc(344),sc(y-6),sc(380),sc(y)),fill=(102,225,244,210),width=sc(1))
    for x,y in [(140,288),(158,283),(176,278),(336,278),(354,283),(372,288)]:
        m=ellipse_mask((x-4,y-4,x+4,y+4)); glow(img,m,blur=4,strength=.65); fill(img,m,(60,215,245,255))

    # Central flight-control slab leading toward operator.
    slab=[(211,217),(301,217),(320,247),(306,333),(286,354),(226,354),(206,333),(192,247)]
    slab_m=poly_mask(slab)
    fill_grad(img,slab_m,(211,207,191),(126,126,118))
    draw_poly_outline(ImageDraw.Draw(img),slab,(53,52,46,255),4)
    d=ImageDraw.Draw(img)
    d.rounded_rectangle((sc(218),sc(236),sc(294),sc(318)),radius=sc(14),fill=(45,54,58,255),outline=(178,146,78,230),width=sc(2))
    # central control interface strip
    core=rr_mask((249,246,263,307),6); glow(img,core,blur=7,strength=.8); fill(img,core,(63,219,249,255))
    d.rounded_rectangle((sc(249),sc(246),sc(263),sc(307)),radius=sc(6),outline=(198,250,255,220),width=sc(1))
    for y in [248,274,304]:
        d.line((sc(226),sc(y),sc(243),sc(y)),fill=(81,204,228,170),width=sc(2))
        d.line((sc(269),sc(y),sc(286),sc(y)),fill=(81,204,228,170),width=sc(2))

    # Explicit operator recess / standing pad on SOUTH side.
    pad=[(205,342),(307,342),(331,374),(303,417),(209,417),(181,374)]
    pad_m=poly_mask(pad)
    fill_grad(img,pad_m,(80,84,80),(38,44,46))
    draw_poly_outline(ImageDraw.Draw(img),pad,(156,126,74,240),3)
    ring_o=ellipse_mask((224,355,288,411)); ring_i=ellipse_mask((235,365,277,401))
    ring=ImageChops.subtract(ring_o,ring_i)
    glow(img,ring,blur=7,strength=.7)
    fill(img,ring,(59,205,239,220))
    d=ImageDraw.Draw(img)
    d.ellipse((sc(246),sc(373),sc(266),sc(393)),fill=(22,46,57,255),outline=(184,244,251,220),width=sc(2))

    # Side housings / integrated support modules.
    for box in [(74,158,123,222),(389,158,438,222)]:
        m=rr_mask(box,12); fill_grad(img,m,(225,223,209),(139,140,131))
        d=ImageDraw.Draw(img); d.rounded_rectangle(tuple(sc(v) for v in box),radius=sc(12),outline=(52,55,54,255),width=sc(3))
        cx=(box[0]+box[2])/2
        line_m=rr_mask((cx-4,box[1]+13,cx+4,box[3]-13),3); glow(img,line_m,blur=5,strength=.55); fill(img,line_m,(62,215,244,245))

    # Small cyan status nodes around frame.
    for x,y in [(113,109),(399,109),(91,319),(421,319)]:
        m=ellipse_mask((x-6,y-6,x+6,y+6)); glow(img,m,blur=5,strength=.75); fill(img,m,(66,221,248,255))
        d=ImageDraw.Draw(img); d.ellipse((sc(x-3),sc(y-3),sc(x+3),sc(y+3)),fill=CYAN_HI)

    surface_flecks(img,outer_m,110)

    # Downsample, sharpen, and clean transparent RGB.
    img=img.resize((W,H),Image.Resampling.LANCZOS)
    img=img.filter(ImageFilter.UnsharpMask(radius=1.3,percent=55,threshold=3))
    px=img.load()
    for y in range(H):
        for x in range(W):
            r,g,b,a=px[x,y]
            if a<3:
                px[x,y]=(0,0,0,0)
    return img

def rotate_clean(im, transpose):
    out=im.transpose(transpose)
    px=out.load()
    for y in range(out.height):
        for x in range(out.width):
            r,g,b,a=px[x,y]
            if a==0 and (r or g or b):
                px[x,y]=(0,0,0,0)
    return out

def validate(path):
    with Image.open(path) as im:
        im.load()
        if im.mode!="RGBA" or im.size!=(512,512):
            raise RuntimeError(f"{path}: expected 512x512 RGBA, got {im.mode} {im.size}")
        a=im.getchannel("A")
        if a.getbbox() is None:
            raise RuntimeError(f"{path}: fully transparent")
        edges=[
            a.crop((0,0,512,1)).getextrema()[1],
            a.crop((0,511,512,512)).getextrema()[1],
            a.crop((0,0,1,512)).getextrema()[1],
            a.crop((511,0,512,512)).getextrema()[1],
        ]
        if any(v>8 for v in edges):
            raise RuntimeError(f"{path}: visible alpha touches edge {edges}")

south=render_south()
views={
    "":south,
    "_south":south,
    "_north":rotate_clean(south,Image.Transpose.ROTATE_180),
    "_east":rotate_clean(south,Image.Transpose.ROTATE_90),
    "_west":rotate_clean(south,Image.Transpose.ROTATE_270),
}
for suffix,im in views.items():
    path=OUT/f"WNG_PrecursorPilotConsole{suffix}.png"
    im.save(path,"PNG",optimize=True)
    validate(path)

print("Generated professional Asuran pilot console family: broad helm console, forward display bank, angled controls, and explicit operator station.")
