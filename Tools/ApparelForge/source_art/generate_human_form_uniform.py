from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageOps
import numpy as np
from scipy.ndimage import gaussian_filter
import random, math

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "ArtSource" / "Apparel" / "human_form_uniform"
OUT.mkdir(parents=True, exist_ok=True)

S = 1536

STYLE_DIRS = [
    ROOT / "ArtSource" / "Apparel" / "wraith_commander_carapace",
    ROOT / "ArtSource" / "Apparel" / "wraith_warrior_carapace",
    ROOT / "ArtSource" / "Apparel" / "wraith_hunter_coat",
    ROOT / "ArtSource" / "Apparel" / "wraith_queen_raiment",
]

def _alpha_bbox(im: Image.Image):
    return im.getchannel("A").getbbox()

def _load_style(view: str) -> np.ndarray:
    fields = []
    for d in STYLE_DIRS:
        p = d / f"master_{view}.png"
        if not p.exists():
            continue
        im = Image.open(p).convert("RGBA")
        bb = _alpha_bbox(im)
        if not bb:
            continue
        crop = im.crop(bb).resize((S, S), Image.Resampling.LANCZOS)
        arr = np.asarray(crop).astype(np.float32)
        rgb = arr[..., :3]
        a = arr[..., 3] / 255.0
        lum = .2126*rgb[...,0] + .7152*rgb[...,1] + .0722*rgb[...,2]
        fill = np.median(lum[a > .15]) if np.any(a > .15) else 128.0
        lum[a < .05] = fill
        # Extract the painterly surface information, not donor colour/geometry.
        low = gaussian_filter(lum, 34)
        mid = gaussian_filter(lum, 7) - gaussian_filter(lum, 26)
        fine = lum - gaussian_filter(lum, 3)
        low = (low - np.mean(low)) / (np.std(low) + 1e-6)
        mid = (mid - np.mean(mid)) / (np.std(mid) + 1e-6)
        fine = (fine - np.mean(fine)) / (np.std(fine) + 1e-6)
        fields.append((low, mid, fine))
    if not fields:
        rng = np.random.default_rng(123)
        n = rng.normal(0, 1, (S, S))
        return np.stack([
            gaussian_filter(n, 34),
            gaussian_filter(n, 7)-gaussian_filter(n, 26),
            n-gaussian_filter(n, 3)
        ])
    # Blend multiple accepted masters so no one garment donates its geometry.
    low = np.mean([x[0] for x in fields], axis=0)
    mid = np.mean([x[1] for x in fields], axis=0)
    fine = np.mean([x[2] for x in fields], axis=0)
    return np.stack([low, mid, fine])

def _garment_mask(view: str) -> Image.Image:
    m = Image.new("L", (S,S), 0)
    d = ImageDraw.Draw(m)
    if view in ("south","north"):
        pts = [
            (430,320),(525,262),(670,228),(768,220),(866,228),(1011,262),(1106,320),
            (1148,454),(1136,720),(1092,1012),(1020,1198),(914,1326),(768,1375),
            (622,1326),(516,1198),(444,1012),(400,720),(388,454)
        ]
        d.polygon(pts, fill=255)
        d.ellipse((430,230,1106,690), fill=255)
        d.rounded_rectangle((432,470,1104,1228), radius=240, fill=255)
    else:
        pts = [
            (525,322),(620,270),(760,238),(874,250),(980,308),(1054,414),
            (1080,602),(1056,850),(1014,1070),(940,1246),(834,1344),(718,1364),
            (622,1320),(540,1220),(480,1050),(444,834),(434,600),(458,414)
        ]
        d.polygon(pts, fill=255)
        d.ellipse((515,242,1035,680), fill=255)
        d.rounded_rectangle((500,450,1045,1230), radius=210, fill=255)
    return m.filter(ImageFilter.GaussianBlur(2.5))

def _colourize_texture(style: np.ndarray, mask: Image.Image, view: str, seed: int) -> Image.Image:
    rng = np.random.default_rng(seed)
    low, mid, fine = style
    yy, xx = np.mgrid[0:S, 0:S]

    # Broad cloth lighting; deliberately soft, not hard-panel shading.
    cx = .44 if view != "east" else .47
    key = np.exp(-(((xx/S)-cx)/.40)**2 - (((yy/S)-.34)/.55)**2)
    falloff = np.clip((yy/S)-.57, 0, 1) * .21 + np.clip((xx/S)-.78,0,1)*.08

    # Faint woven cloth / nanite weave.
    weave = 2.0*np.sin(xx/8.2) + 1.5*np.sin(yy/9.5) + 0.9*np.sin((xx+yy)/15.0)
    stochastic = gaussian_filter(rng.normal(0,1,(S,S)), 1.2)

    L = (
        .54
        + .24*key
        - falloff
        + .075*low
        + .060*mid
        + .018*fine
        + .006*weave
        + .008*stochastic
    )
    L = np.clip(L, .28, .95)

    # Asuran / Ancient engineered cloth: warm-silver rather than sterile white.
    r = 88 + 142*L
    g = 96 + 144*L
    b = 100 + 147*L
    arr = np.zeros((S,S,4), np.uint8)
    arr[...,0] = np.clip(r,0,255)
    arr[...,1] = np.clip(g,0,255)
    arr[...,2] = np.clip(b,0,255)
    arr[...,3] = np.asarray(mask)
    return Image.fromarray(arr, "RGBA")

def _poly_mask(points, blur=1.5):
    m = Image.new("L",(S,S),0)
    ImageDraw.Draw(m).polygon(points, fill=255)
    return m.filter(ImageFilter.GaussianBlur(blur))

def _paint_region(im: Image.Image, points, style: np.ndarray, colour=(40,47,51), alpha=225, seed=0, edge=(120,135,139), edge_alpha=135):
    pm = _poly_mask(points, 2.0)
    low,mid,fine = style
    yy,xx=np.mgrid[0:S,0:S]
    rng=np.random.default_rng(seed)
    tex = .055*low + .08*mid + .025*fine + .012*gaussian_filter(rng.normal(0,1,(S,S)),1)
    grad = np.clip(.86 - .18*(yy/S) + tex, .48, 1.08)
    a=np.zeros((S,S,4), np.uint8)
    for c in range(3):
        a[...,c]=np.clip(colour[c]*grad,0,255)
    a[...,3]=(np.asarray(pm)*(alpha/255)).astype(np.uint8)
    im.alpha_composite(Image.fromarray(a,"RGBA"))
    e=Image.new("RGBA",(S,S),(0,0,0,0))
    d=ImageDraw.Draw(e)
    d.line(points+[points[0]], fill=(*edge,edge_alpha), width=4, joint="curve")
    im.alpha_composite(e.filter(ImageFilter.GaussianBlur(.7)))

def _stroke(im, pts, colour, width, alpha=150, blur=.5, seed=0):
    rnd=random.Random(seed)
    j=[(x+rnd.uniform(-1.8,1.8), y+rnd.uniform(-1.8,1.8)) for x,y in pts]
    lay=Image.new("RGBA",(S,S),(0,0,0,0))
    ImageDraw.Draw(lay).line(j, fill=(*colour,alpha), width=width, joint="curve")
    if blur:
        lay=lay.filter(ImageFilter.GaussianBlur(blur))
    im.alpha_composite(lay)

def _stitches(im, a, b, count, seed=0):
    rnd=random.Random(seed)
    x1,y1=a; x2,y2=b
    dx=x2-x1; dy=y2-y1; ll=(dx*dx+dy*dy)**.5
    nx=-dy/ll; ny=dx/ll
    lay=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(lay)
    for i in range(count+1):
        t=i/count
        x=x1+dx*t+rnd.uniform(-1.2,1.2); y=y1+dy*t+rnd.uniform(-1.2,1.2)
        ln=5+rnd.uniform(-1.3,1.3)
        d.line((x-nx*ln,y-ny*ln,x+nx*ln,y+ny*ln), fill=(183,191,190,105), width=2)
    im.alpha_composite(lay)

def _folds(im, mask, view, seed):
    rng=random.Random(seed)
    # Organic cloth folds rather than armour panel edges.
    lay=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(lay)
    for i in range(32):
        if view=="east":
            x=rng.randint(560,960); y=rng.randint(450,1160)
        else:
            x=rng.randint(500,1035); y=rng.randint(450,1180)
        length=rng.randint(55,150)
        bend=rng.randint(-18,18)
        col=(20,25,28,rng.randint(16,30)) if i%2 else (235,238,236,rng.randint(10,20))
        d.line([(x,y),(x+bend,y+length//2),(x+bend//2,y+length)], fill=col, width=rng.randint(3,7))
    lay=lay.filter(ImageFilter.GaussianBlur(5))
    lay.putalpha(ImageChops.multiply(lay.getchannel("A"),mask))
    im.alpha_composite(lay)

def _rubbed_wear(im, mask, seed):
    rng=np.random.default_rng(seed)
    arr=np.asarray(im).astype(np.float32)
    m=np.asarray(mask)>30
    yy,xx=np.mgrid[0:S,0:S]
    for _ in range(80):
        x=int(rng.integers(430,1110)); y=int(rng.integers(300,1290))
        if not (0<=x<S and 0<=y<S and m[y,x]):
            continue
        rx=float(rng.integers(10,40)); ry=float(rng.integers(18,65))
        g=np.exp(-(((xx-x)/rx)**2+((yy-y)/ry)**2)/2)
        amt=float(rng.uniform(-9,10))
        arr[...,:3]=np.clip(arr[...,:3]+g[...,None]*amt*m[...,None],0,255)
    return Image.fromarray(arr.astype(np.uint8),"RGBA")

def _render(view: str) -> Image.Image:
    style=_load_style(view)
    mask=_garment_mask(view)
    im=_colourize_texture(style,mask,view, {"south":1101,"north":2202,"east":3303}[view])

    if view=="south":
        # Soft graphite collar and inset front weave.
        _paint_region(im,[(624,300),(768,244),(912,300),(884,410),(768,458),(652,410)],
                      style,(38,44,48),236,1,(145,156,158),160)
        _paint_region(im,[(682,474),(768,510),(854,474),(880,742),(844,1035),(768,1142),(692,1035),(656,742)],
                      style,(48,55,59),205,2,(116,131,135),125)
        # darker side textile, irregular and cloth-like
        _paint_region(im,[(430,520),(555,470),(630,555),(646,1045),(555,1170),(458,1085)],
                      style,(39,45,49),150,3,(105,119,122),85)
        _paint_region(im,[(1106,520),(981,470),(906,555),(890,1045),(981,1170),(1078,1085)],
                      style,(39,45,49),150,4,(105,119,122),85)

        _stroke(im,[(490,650),(610,708),(686,686)],(146,158,159),5,125,1,11)
        _stroke(im,[(1046,650),(926,708),(850,686)],(146,158,159),5,125,1,12)
        _stroke(im,[(516,855),(642,910),(700,892)],(126,140,143),4,115,1,13)
        _stroke(im,[(1020,855),(894,910),(836,892)],(126,140,143),4,115,1,14)
        _stroke(im,[(555,1038),(684,1096),(768,1082),(852,1096),(981,1038)],(143,155,157),5,135,1,15)

        # Ancient/Asuran restrained inlay, deliberately dull not neon.
        _stroke(im,[(596,602),(688,646),(768,624),(848,646),(940,602)],(68,96,100),6,115,.8,16)
        _stroke(im,[(626,932),(704,962),(768,950),(832,962),(910,932)],(68,96,100),5,95,.8,17)

        _stitches(im,(653,414),(692,1035),28,21)
        _stitches(im,(883,414),(844,1035),28,22)

        lay=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(lay)
        d.rounded_rectangle((722,1038,814,1084), radius=12, fill=(28,33,36,225), outline=(160,171,172,190), width=3)
        d.rounded_rectangle((747,1052,789,1070), radius=5, fill=(72,100,104,130))
        im.alpha_composite(lay)

    elif view=="north":
        _paint_region(im,[(624,304),(768,250),(912,304),(886,406),(768,448),(650,406)],
                      style,(37,43,47),230,31,(142,154,156),155)
        _paint_region(im,[(688,462),(768,494),(848,462),(874,756),(842,1038),(768,1138),(694,1038),(662,756)],
                      style,(45,52,56),198,32,(113,129,132),120)
        _paint_region(im,[(434,522),(558,474),(632,554),(648,1048),(558,1168),(462,1082)],
                      style,(38,44,48),145,33,(103,117,120),80)
        _paint_region(im,[(1102,522),(978,474),(904,554),(888,1048),(978,1168),(1074,1082)],
                      style,(38,44,48),145,34,(103,117,120),80)

        _stroke(im,[(768,450),(768,1130)],(148,160,161),4,125,.7,35)
        _stroke(im,[(508,672),(626,722),(670,706)],(136,149,151),5,120,.8,36)
        _stroke(im,[(1028,672),(910,722),(866,706)],(136,149,151),5,120,.8,37)
        _stroke(im,[(552,1038),(684,1094),(768,1080),(852,1094),(984,1038)],(140,153,155),5,130,.8,38)
        _stroke(im,[(606,616),(692,654),(768,634),(844,654),(930,616)],(68,95,99),6,105,.8,39)

        _stitches(im,(651,408),(694,1038),28,41)
        _stitches(im,(885,408),(842,1038),28,42)

        lay=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(lay)
        d.ellipse((730,1038,806,1114), fill=(28,33,36,215), outline=(157,169,171,180), width=3)
        d.ellipse((752,1060,784,1092), fill=(70,98,102,120))
        im.alpha_composite(lay)

    else:
        _paint_region(im,[(615,310),(742,252),(860,270),(962,334),(990,410),(936,474),(688,448)],
                      style,(38,44,48),232,61,(142,154,156),150)
        _paint_region(im,[(680,466),(802,500),(916,454),(954,740),(924,1032),(816,1150),(704,1070),(650,762)],
                      style,(46,53,57),198,62,(113,129,132),115)
        _paint_region(im,[(462,544),(560,500),(642,566),(662,1050),(576,1170),(490,1080)],
                      style,(38,44,48),145,63,(103,117,120),80)

        _stroke(im,[(624,520),(686,594),(650,762),(706,1068)],(148,160,161),5,120,.9,64)
        _stroke(im,[(918,468),(954,632),(950,840),(910,1055)],(142,154,156),5,115,.9,65)
        _stroke(im,[(676,700),(804,748),(916,700)],(68,96,100),6,105,.8,66)
        _stroke(im,[(700,930),(818,972),(912,938)],(68,96,100),5,90,.8,67)
        _stitches(im,(686,448),(704,1070),28,68)

        lay=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(lay)
        d.rounded_rectangle((770,1032,852,1078), radius=11, fill=(28,33,36,220), outline=(158,170,172,185), width=3)
        im.alpha_composite(lay)

    _folds(im,mask,view,{"south":71,"north":72,"east":73}[view])
    im=_rubbed_wear(im,mask,{"south":401,"north":402,"east":403}[view])

    # Dark, soft worn edge. No vector-white rim.
    inner=mask.filter(ImageFilter.GaussianBlur(16))
    edge=ImageChops.subtract(mask,inner)
    sh=Image.new("RGBA",(S,S),(14,17,19,0))
    sh.putalpha(edge.point(lambda p:min(68,int(p*.28))))
    im.alpha_composite(sh)
    im.putalpha(ImageChops.multiply(im.getchannel("A"),mask))
    return im

for view in ("south","north","east"):
    out=_render(view)
    out.save(OUT/f"master_{view}.png", optimize=True, compress_level=9)
    print("painted", OUT/f"master_{view}.png", out.size)
