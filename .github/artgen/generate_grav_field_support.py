from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

S=4
W=H=256
OUTS={
 "goauld": Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldGravFieldProjector.png"),
 "asuran": Path("Textures/Things/Building/Precursor/Gravship/WNG_AsuranGravFieldExtender.png"),
}

def sc(v): return int(round(v*S))
def poly(d, pts, fill, outline=None, width=1):
    pts=[(sc(x),sc(y)) for x,y in pts]
    d.polygon(pts,fill=fill)
    if outline:
        d.line(pts+[pts[0]],fill=outline,width=sc(width),joint="curve")
def ell(d, box, fill, outline=None, width=1):
    box=tuple(sc(v) for v in box)
    d.ellipse(box,fill=fill,outline=outline,width=sc(width) if outline else 1)
def rr(d, box, r, fill, outline=None, width=1):
    box=tuple(sc(v) for v in box)
    d.rounded_rectangle(box,radius=sc(r),fill=fill,outline=outline,width=sc(width) if outline else 1)
def line(d, pts, fill, width):
    d.line([(sc(x),sc(y)) for x,y in pts],fill=fill,width=sc(width),joint="curve")

def glow(base, center, radius, color, strength=180):
    g=Image.new("RGBA",base.size,(0,0,0,0))
    gd=ImageDraw.Draw(g)
    x,y=center
    gd.ellipse((sc(x-radius),sc(y-radius),sc(x+radius),sc(y+radius)),fill=(*color,strength))
    g=g.filter(ImageFilter.GaussianBlur(sc(radius*0.65)))
    base.alpha_composite(g)

def save(img,p):
    p.parent.mkdir(parents=True,exist_ok=True)
    img=img.resize((W,H),Image.Resampling.LANCZOS)
    # clean fully transparent RGB
    px=img.load()
    for y in range(H):
        for x in range(W):
            r,g,b,a=px[x,y]
            if a==0: px[x,y]=(0,0,0,0)
    img.save(p,"PNG",optimize=True)
    Image.open(p).convert("RGBA").load()

def goauld():
    im=Image.new("RGBA",(W*S,H*S),(0,0,0,0)); d=ImageDraw.Draw(im)
    # subtle amber field glow
    glow(im,(128,128),58,(255,135,24),70)
    d=ImageDraw.Draw(im)
    # broad naquadah base
    poly(d,[(48,154),(76,94),(128,64),(180,94),(208,154),(176,190),(80,190)],
         (24,24,27,255),(7,7,8,255),4)
    poly(d,[(58,153),(82,104),(128,78),(174,104),(198,153),(170,178),(86,178)],
         (44,38,34,255),(103,65,31,255),3)
    # inset floor plates
    poly(d,[(78,151),(93,117),(128,98),(163,117),(178,151),(158,166),(98,166)],
         (17,18,20,255),(188,111,33,255),2)
    # central gravitic ring/core
    glow(im,(128,137),31,(255,139,28),130); d=ImageDraw.Draw(im)
    ell(d,(96,105,160,169),(9,9,11,255),(215,137,45,255),4)
    ell(d,(104,113,152,161),(35,24,13,255),(255,179,62,255),3)
    ell(d,(114,123,142,151),(255,139,22,255),(255,221,132,255),3)
    ell(d,(121,130,135,144),(255,231,153,255),None)
    # four integrated emitter pylons
    pylons=[
      [(67,153),(78,116),(91,101),(98,110),(91,145),(82,164)],
      [(189,153),(178,116),(165,101),(158,110),(165,145),(174,164)],
      [(96,92),(113,74),(121,80),(116,108),(102,121),(92,112)],
      [(160,92),(143,74),(135,80),(140,108),(154,121),(164,112)]
    ]
    for pts in pylons:
        poly(d,pts,(31,30,31,255),(8,8,9,255),3)
        # amber trim roughly through center
        cx=sum(x for x,y in pts)/len(pts); cy=sum(y for x,y in pts)/len(pts)
        ell(d,(cx-4,cy-4,cx+4,cy+4),(244,130,26,255),(255,194,83,255),1)
    # decorative gold ribs
    for ang in [45,135,225,315]:
        a=math.radians(ang); x1=128+36*math.cos(a); y1=137+36*math.sin(a)
        x2=128+68*math.cos(a); y2=137+49*math.sin(a)
        line(d,[(x1,y1),(x2,y2)],(202,119,38,255),3)
    # outer feet
    for box in [(48,151,79,174),(177,151,208,174),(74,177,102,194),(154,177,182,194)]:
        rr(d,box,5,(29,28,29,255),(117,75,37,255),2)
    save(im,OUTS["goauld"])

def asuran():
    im=Image.new("RGBA",(W*S,H*S),(0,0,0,0)); d=ImageDraw.Draw(im)
    glow(im,(128,135),62,(22,188,255),70); d=ImageDraw.Draw(im)
    # low-profile circular Ancient/Asuran pedestal
    ell(d,(48,86,208,196),(43,49,54,255),(186,201,209,255),4)
    ell(d,(58,96,198,186),(16,24,29,255),(98,139,157,255),3)
    ell(d,(72,108,184,174),(42,50,55,255),(175,196,204,255),3)
    # cyan gravitic core
    glow(im,(128,139),34,(18,192,255),150); d=ImageDraw.Draw(im)
    ell(d,(93,104,163,174),(8,21,28,255),(151,220,239,255),4)
    ell(d,(103,114,153,164),(14,95,126,255),(65,213,255,255),3)
    ell(d,(114,125,142,153),(50,205,255,255),(206,248,255,255),2)
    # four clean emitter fins
    fins=[
      [(62,139),(72,106),(94,91),(101,102),(91,128),(84,158)],
      [(194,139),(184,106),(162,91),(155,102),(165,128),(172,158)],
      [(99,94),(115,74),(124,80),(122,112),(108,123),(97,111)],
      [(157,94),(141,74),(132,80),(134,112),(148,123),(159,111)]
    ]
    for pts in fins:
        poly(d,pts,(210,217,220,255),(86,102,110,255),3)
        # dark inset
        xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
        cx=sum(xs)/len(xs); cy=sum(ys)/len(ys)
        ell(d,(cx-5,cy-5,cx+5,cy+5),(16,75,94,255),(65,213,255,255),2)
    # radial cyan channels
    for ang in [0,45,90,135,180,225,270,315]:
        a=math.radians(ang)
        p1=(128+38*math.cos(a),139+28*math.sin(a))
        p2=(128+65*math.cos(a),139+43*math.sin(a))
        line(d,[p1,p2],(54,191,227,255),2)
    # segmented outer shell pads
    for ang in range(0,360,45):
        a=math.radians(ang)
        cx=128+74*math.cos(a); cy=139+49*math.sin(a)
        rr(d,(cx-10,cy-6,cx+10,cy+6),4,(182,191,196,255),(70,86,94,255),2)
    save(im,OUTS["asuran"])

goauld()
asuran()
print("generated and fully decoded grav-field support sprites")
