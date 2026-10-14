from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math, random

ROOT = Path(__file__).resolve().parents[2]
CHAIR_DIR = ROOT / "Textures/Things/Building/Precursor/Control"
ICON_DIR = ROOT / "Textures/UI/WNG/Build"
S=2
W=H=512
SS=W*S
random.seed(137)

def sc(v): return int(v*S)
def sbox(b): return tuple(sc(v) for v in b)
def pts(p): return [(sc(x),sc(y)) for x,y in p]

def rr_mask(box,r):
    m=Image.new("L",(SS,SS),0); ImageDraw.Draw(m).rounded_rectangle(sbox(box),radius=sc(r),fill=255); return m
def poly_mask(p):
    m=Image.new("L",(SS,SS),0); ImageDraw.Draw(m).polygon(pts(p),fill=255); return m
def ellipse_mask(box):
    m=Image.new("L",(SS,SS),0); ImageDraw.Draw(m).ellipse(sbox(box),fill=255); return m
def ring_mask(outer,inner):
    return ImageChops.subtract(outer,inner)

def vgrad(top,bottom):
    g=Image.new("RGBA",(SS,SS))
    d=ImageDraw.Draw(g)
    for y in range(SS):
        t=y/max(1,SS-1)
        c=tuple(int(top[i]*(1-t)+bottom[i]*t) for i in range(3))+(255,)
        d.line((0,y,SS,y),fill=c)
    return g

def fill_grad(base,mask,top,bottom):
    g=vgrad(top,bottom); g.putalpha(mask); base.alpha_composite(g)

def fill(base,mask,color):
    lay=Image.new("RGBA",(SS,SS),color); lay.putalpha(mask); base.alpha_composite(lay)

def glow(base,mask,color=(61,215,255),blur=10,strength=.8):
    a=mask.filter(ImageFilter.GaussianBlur(sc(blur)))
    a=a.point(lambda p:min(255,int(p*strength)))
    lay=Image.new("RGBA",(SS,SS),color+(0,)); lay.putalpha(a); base.alpha_composite(lay)

def outline(draw, points, color, width=3):
    draw.line(pts(points+[points[0]]),fill=color,width=sc(width),joint="curve")

def add_scratches(img,mask,count,colors):
    d=ImageDraw.Draw(img)
    for _ in range(count):
        x=random.randint(sc(40),sc(472)); y=random.randint(sc(40),sc(472))
        if mask.getpixel((x,y))>0:
            l=random.randint(sc(2),sc(7))
            d.line((x,y,x+l,y+random.randint(-1,1)*S),fill=random.choice(colors),width=max(1,S//2))

def normalize_canvas(img, max_dim=464):
    bbox=img.getchannel("A").getbbox()
    if bbox is None:
        return img
    obj=img.crop(bbox)
    obj.thumbnail((max_dim,max_dim),Image.Resampling.LANCZOS)
    out=Image.new("RGBA",(W,H),(0,0,0,0))
    out.alpha_composite(obj,((W-obj.width)//2,(H-obj.height)//2))
    return out

def render_chair_south():
    img=Image.new("RGBA",(SS,SS),(0,0,0,0))
    sh=Image.new("RGBA",(SS,SS),(0,0,0,0))
    ImageDraw.Draw(sh).ellipse(sbox((48,38,464,480)),fill=(0,0,0,150))
    sh=sh.filter(ImageFilter.GaussianBlur(sc(13))); img.alpha_composite(sh)

    outer=ellipse_mask((45,28,467,490))
    inner=ellipse_mask((70,54,442,464))
    fill_grad(img,outer,(80,83,82),(26,30,31))
    fill_grad(img,inner,(46,52,55),(18,23,25))
    d=ImageDraw.Draw(img)
    d.ellipse(sbox((45,28,467,490)),outline=(177,145,93,255),width=sc(5))
    d.ellipse(sbox((70,54,442,464)),outline=(210,201,177,210),width=sc(3))

    for deg in range(0,360,30):
        a=math.radians(deg)
        x1=256+math.cos(a)*170; y1=256+math.sin(a)*186
        x2=256+math.cos(a)*198; y2=256+math.sin(a)*216
        d.line((sc(x1),sc(y1),sc(x2),sc(y2)),fill=(10,14,15,200),width=sc(4))
        d.line((sc(x1+1),sc(y1+1),sc(x2+1),sc(y2+1)),fill=(157,139,103,120),width=sc(1))

    for deg in [18,72,108,162,198,252,288,342]:
        a=math.radians(deg)
        cx=256+math.cos(a)*188; cy=256+math.sin(a)*205
        m=ellipse_mask((cx-8,cy-13,cx+8,cy+13))
        glow(img,m,blur=6,strength=.7); fill(img,m,(76,218,255,235))

    halo=ellipse_mask((154,38,358,232))
    halo_in=ellipse_mask((176,62,336,214))
    ring=ring_mask(halo,halo_in)
    fill_grad(img,ring,(205,194,164),(92,79,59))
    d=ImageDraw.Draw(img); d.ellipse(sbox((154,38,358,232)),outline=(42,46,47,255),width=sc(5))

    back=[(213,58),(299,58),(325,103),(315,184),(287,213),(225,213),(197,184),(187,103)]
    bm=poly_mask(back); fill_grad(img,bm,(219,218,209),(119,123,121))
    outline(ImageDraw.Draw(img),back,(72,64,52,255),5)
    core=rr_mask((241,67,271,170),11); glow(img,core,blur=10,strength=.85); fill_grad(img,core,(177,246,255),(26,155,218))
    ImageDraw.Draw(img).rounded_rectangle(sbox((241,67,271,170)),radius=sc(11),outline=(222,244,244,220),width=sc(2))

    for box,r in [((211,154,301,225),16),((198,214,314,286),18),((207,276,305,345),18)]:
        m=rr_mask(box,r)
        fill_grad(img,m,(231,229,218),(170,171,164))
        ImageDraw.Draw(img).rounded_rectangle(sbox(box),radius=sc(r),outline=(98,91,75,255),width=sc(4))

    for p in [[(177,182),(211,163),(204,296),(178,315),(159,268),(160,213)],
              [(335,182),(301,163),(308,296),(334,315),(353,268),(352,213)]]:
        m=poly_mask(p); fill_grad(img,m,(194,190,176),(87,89,86)); outline(ImageDraw.Draw(img),p,(87,70,49,255),4)

    seat=[(196,318),(316,318),(333,357),(310,398),(202,398),(179,357)]
    sm=poly_mask(seat); fill_grad(img,sm,(232,229,217),(157,160,156)); outline(ImageDraw.Draw(img),seat,(95,82,61,255),5)

    arms=[
        [(114,173),(176,142),(194,188),(184,342),(149,388),(105,354),(92,239)],
        [(398,173),(336,142),(318,188),(328,342),(363,388),(407,354),(420,239)],
    ]
    for p in arms:
        m=poly_mask(p); fill_grad(img,m,(183,160,118),(76,69,57)); outline(ImageDraw.Draw(img),p,(45,43,39,255),6)
    for box in [(115,211,183,335),(329,211,397,335)]:
        m=rr_mask(box,18); fill_grad(img,m,(202,202,192),(94,99,99))
        ImageDraw.Draw(img).rounded_rectangle(sbox(box),radius=sc(18),outline=(101,82,55,255),width=sc(3))

    for box in [(126,222,172,294),(340,222,386,294)]:
        m=rr_mask(box,10); glow(img,m,blur=9,strength=.7)
        fill_grad(img,m,(86,210,235),(19,87,121))
        dd=ImageDraw.Draw(img); dd.rounded_rectangle(sbox(box),radius=sc(10),outline=(207,235,233,220),width=sc(2))
        for k in range(4):
            y=235+k*13
            dd.line((sc(box[0]+8),sc(y),sc(box[2]-8),sc(y)),fill=(101,225,255,100),width=sc(1))
        dd.ellipse(sbox(((box[0]+box[2])/2-8,252,(box[0]+box[2])/2+8,268)),outline=(156,239,255,200),width=sc(2))

    for p in [[(112,362),(160,343),(198,390),(187,454),(142,476),(103,430)],
              [(400,362),(352,343),(314,390),(325,454),(370,476),(409,430)]]:
        m=poly_mask(p); fill_grad(img,m,(171,154,120),(70,65,56)); outline(ImageDraw.Draw(img),p,(45,43,39,255),5)
        ip=[(256+(x-256)*.92,256+(y-256)*.92) for x,y in p]
        outline(ImageDraw.Draw(img),ip,(221,206,166,130),2)

    pad=ellipse_mask((213,392,299,478)); fill_grad(img,pad,(97,101,101),(35,40,41))
    d=ImageDraw.Draw(img); d.ellipse(sbox((213,392,299,478)),outline=(174,151,104,220),width=sc(3))
    ringo=ellipse_mask((225,405,287,467)); ringi=ellipse_mask((234,414,278,458))
    rg=ring_mask(ringo,ringi); glow(img,rg,blur=6,strength=.6); fill(img,rg,(65,205,245,220))

    d=ImageDraw.Draw(img)
    for seg in [((118,181),(151,169)),((394,181),(361,169)),((124,363),(151,351)),((388,363),(361,351))]:
        d.line((sc(seg[0][0]),sc(seg[0][1]),sc(seg[1][0]),sc(seg[1][1])),fill=(76,218,255,220),width=sc(4))

    add_scratches(img,outer,150,[(220,213,190,38),(120,111,93,55),(240,240,233,32)])
    img=img.filter(ImageFilter.UnsharpMask(radius=2,percent=60,threshold=4))
    img=img.resize((W,H),Image.Resampling.LANCZOS)
    px=img.load()
    for y in range(H):
        for x in range(W):
            r,g,b,a=px[x,y]
            if a<2: px[x,y]=(0,0,0,0)
    return normalize_canvas(img)

def render_plinth():
    img=Image.new("RGBA",(SS,SS),(0,0,0,0))
    sh=Image.new("RGBA",(SS,SS),(0,0,0,0))
    ImageDraw.Draw(sh).ellipse(sbox((62,70,450,456)),fill=(0,0,0,145))
    sh=sh.filter(ImageFilter.GaussianBlur(sc(12))); img.alpha_composite(sh)
    body=ellipse_mask((57,48,455,456)); fill_grad(img,body,(201,202,195),(68,73,74))
    d=ImageDraw.Draw(img); d.ellipse(sbox((57,48,455,456)),outline=(146,119,77,255),width=sc(5))
    inner=ellipse_mask((102,95,410,409)); fill_grad(img,inner,(216,214,204),(126,129,126))
    d.ellipse(sbox((102,95,410,409)),outline=(111,91,60,220),width=sc(4))
    ro=ellipse_mask((117,110,395,394)); ri=ellipse_mask((132,125,380,379)); ring=ring_mask(ro,ri)
    glow(img,ring,blur=8,strength=.65); fill(img,ring,(58,205,242,210))

    pylons=[
        [(219,48),(293,48),(311,118),(291,143),(221,143),(201,118)],
        [(455,215),(455,289),(385,307),(360,287),(360,217),(385,197)],
        [(219,456),(293,456),(311,386),(291,361),(221,361),(201,386)],
        [(57,215),(57,289),(127,307),(152,287),(152,217),(127,197)]
    ]
    for p in pylons:
        m=poly_mask(p); fill_grad(img,m,(222,220,211),(112,104,88)); outline(ImageDraw.Draw(img),p,(103,79,47,255),4)

    for x,y in [(256,90),(402,252),(256,414),(110,252)]:
        m=ellipse_mask((x-14,y-14,x+14,y+14)); glow(img,m,blur=7,strength=.8); fill_grad(img,m,(186,249,255),(20,145,202))
        ImageDraw.Draw(img).ellipse(sbox((x-14,y-14,x+14,y+14)),outline=(203,195,172,220),width=sc(3))

    d=ImageDraw.Draw(img)
    d.ellipse(sbox((155,148,357,352)),outline=(126,122,109,220),width=sc(3))
    d.ellipse(sbox((224,217,288,281)),outline=(101,104,102,180),width=sc(3))
    for deg in range(0,360,45):
        a=math.radians(deg)
        x1=256+math.cos(a)*40; y1=252+math.sin(a)*40
        x2=256+math.cos(a)*96; y2=252+math.sin(a)*96
        d.line((sc(x1),sc(y1),sc(x2),sc(y2)),fill=(91,92,87,170),width=sc(2))

    add_scratches(img,body,120,[(232,230,216,30),(125,110,82,45)])
    img=img.filter(ImageFilter.UnsharpMask(radius=2,percent=55,threshold=4))
    img=img.resize((W,H),Image.Resampling.LANCZOS)
    px=img.load()
    for y in range(H):
        for x in range(W):
            r,g,b,a=px[x,y]
            if a<2: px[x,y]=(0,0,0,0)
    return normalize_canvas(img)

def save_family(base, south, destdir):
    destdir.mkdir(parents=True,exist_ok=True)
    views={
      base+".png": south,
      base+"_south.png": south,
      base+"_north.png": south.transpose(Image.Transpose.ROTATE_180),
      base+"_east.png": south.transpose(Image.Transpose.ROTATE_90),
      base+"_west.png": south.transpose(Image.Transpose.ROTATE_270),
    }
    for name,im in views.items():
        p=destdir/name
        im.save(p,"PNG",optimize=True)
        with Image.open(p) as ck:
            ck.load()
            assert ck.size==(512,512) and ck.mode=="RGBA"
            a=ck.getchannel("A"); bbox=a.getbbox(); assert bbox is not None
            assert bbox[0]>=18 and bbox[1]>=18 and bbox[2]<=494 and bbox[3]<=494, (name,bbox)
            edges=[a.crop((0,0,512,1)).getextrema()[1],a.crop((0,511,512,512)).getextrema()[1],
                   a.crop((0,0,1,512)).getextrema()[1],a.crop((511,0,512,512)).getextrema()[1]]
            assert not any(edges),(name,edges)

chair=render_chair_south()
plinth=render_plinth()
save_family("WNG_AncientControlChair",chair,CHAIR_DIR)
save_family("WNG_AsuranDormantReconstructionPlinth",plinth,CHAIR_DIR)

ICON_DIR.mkdir(parents=True,exist_ok=True)
icon=Image.new("RGBA",(256,256),(0,0,0,0))
thumb=chair.copy()
bbox=thumb.getchannel("A").getbbox()
thumb=thumb.crop(bbox)
thumb.thumbnail((220,220),Image.Resampling.LANCZOS)
icon.alpha_composite(thumb,((256-thumb.width)//2,(256-thumb.height)//2))
icon.save(ICON_DIR/"WNG_AncientControlChair.png","PNG",optimize=True)
print("generated Ancient control chair family, build icon, and separate Asuran reconstruction plinth family")
