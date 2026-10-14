from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import random, math

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures/Things/Building/Goauld/Gravship/WNG_GoauldPowerCoupler.png"

S = 2
W = H = 512
SS = W * S
random.seed(137)

def pts(seq):
    return [(int(x*S), int(y*S)) for x,y in seq]

def mask_poly(points):
    m = Image.new("L",(SS,SS),0)
    ImageDraw.Draw(m).polygon(pts(points), fill=255)
    return m

def mask_roundbox(box, radius):
    m=Image.new("L",(SS,SS),0)
    ImageDraw.Draw(m).rounded_rectangle(tuple(int(v*S) for v in box), radius=int(radius*S), fill=255)
    return m

def composite_color(base, mask, color):
    lay=Image.new("RGBA",(SS,SS),color)
    base.alpha_composite(Image.composite(lay, Image.new("RGBA",(SS,SS),(0,0,0,0)), mask))

def textured_fill(base, mask, top, bottom, noise=12):
    grad=Image.new("L",(1,SS))
    gp=grad.load()
    for y in range(SS):
        gp[0,y]=int(255*y/(SS-1))
    grad=grad.resize((SS,SS))
    a=Image.new("RGB",(SS,SS),top)
    b=Image.new("RGB",(SS,SS),bottom)
    rgb=Image.composite(b,a,grad)
    if noise:
        n=Image.effect_noise((SS,SS), max(1,noise*1.3)).convert("L")
        n=n.point(lambda v:max(0,min(255,128+(v-128)//3)))
        tint=Image.merge("RGB",(n,n,n))
        rgb=Image.blend(rgb,tint,0.08)
    rgba=rgb.convert("RGBA")
    rgba.putalpha(mask)
    base.alpha_composite(rgba)

def glow(base, mask, color, blur, strength=1.0):
    a=mask.filter(ImageFilter.GaussianBlur(blur*S))
    a=a.point(lambda p: min(255,int(p*strength)))
    col=Image.new("RGBA",(SS,SS),color)
    col.putalpha(a)
    base.alpha_composite(col)

def stroke_poly(draw, points, fill, width, joint="curve"):
    draw.line(pts(points+[points[0]]), fill=fill, width=int(width*S), joint=joint)

def inset_polygon(poly, cx, cy, factor):
    return [(cx+(x-cx)*factor, cy+(y-cy)*factor) for x,y in poly]

img=Image.new("RGBA",(SS,SS),(0,0,0,0))

shadow=Image.new("RGBA",(SS,SS),(0,0,0,0))
sd=ImageDraw.Draw(shadow)
sd.ellipse((62*S,72*S,450*S,458*S), fill=(0,0,0,150))
shadow=shadow.filter(ImageFilter.GaussianBlur(14*S))
img.alpha_composite(shadow)

body=[(256,42),(365,73),(438,147),(468,256),(438,365),(365,438),(256,470),(147,438),(74,365),(43,256),(74,147),(147,73)]
body_m=mask_poly(body)
textured_fill(img,body_m,(49,50,48),(20,22,22),noise=8)
d=ImageDraw.Draw(img)
stroke_poly(d, body, (11,12,12,255), 8)
stroke_poly(d, inset_polygon(body,256,256,.955), (104,78,42,210), 3)

facets=[
[(256,54),(350,82),(327,158),(256,137),(185,158),(162,82)],
[(430,160),(455,256),(430,352),(354,327),(375,256),(354,185)],
[(256,456),(350,430),(327,354),(256,375),(185,354),(162,430)],
[(82,352),(57,256),(82,160),(158,185),(137,256),(158,327)],
]
for p in facets:
    m=mask_poly(p)
    textured_fill(img,m,(62,63,60),(29,31,31),noise=5)
    dd=ImageDraw.Draw(img)
    stroke_poly(dd,p,(92,91,82,200),2)

d=ImageDraw.Draw(img)
for ang in range(0,360,45):
    a=math.radians(ang)
    r1,r2=118,190
    x1=256+math.cos(a)*r1; y1=256+math.sin(a)*r1
    x2=256+math.cos(a)*r2; y2=256+math.sin(a)*r2
    d.line((x1*S,y1*S,x2*S,y2*S), fill=(8,9,9,190), width=5*S)
    d.line((x1*S+S,y1*S+S,x2*S+S,y2*S+S), fill=(78,68,52,120), width=S)

ring_outer=mask_roundbox((125,125,387,387),42)
ring_inner=mask_roundbox((151,151,361,361),34)
ring=ImageChops.subtract(ring_outer,ring_inner)
textured_fill(img,ring,(31,32,31),(12,13,13),noise=4)

for box in [(235,81,277,143),(369,235,431,277),(235,369,277,431),(81,235,143,277)]:
    outer=mask_roundbox(box,8)
    composite_color(img,outer,(18,14,10,255))
    inset=(box[0]+8,box[1]+8,box[2]-8,box[3]-8)
    inner=mask_roundbox(inset,5)
    glow(img,inner,(255,122,16,255),7,0.75)
    textured_fill(img,inner,(255,183,57),(169,66,6),noise=3)
    dd=ImageDraw.Draw(img)
    cx=(inset[0]+inset[2])/2; cy=(inset[1]+inset[3])/2
    if (box[2]-box[0]) < (box[3]-box[1]):
        dd.line((cx*S,(inset[1]+3)*S,cx*S,(inset[3]-3)*S),fill=(255,230,143,210),width=2*S)
    else:
        dd.line(((inset[0]+3)*S,cy*S,(inset[2]-3)*S,cy*S),fill=(255,230,143,210),width=2*S)

jaws=[
[(192,101),(224,91),(240,159),(219,206),(181,194),(164,150)],
[(411,192),(421,224),(353,240),(306,219),(318,181),(362,164)],
[(320,411),(288,421),(272,353),(293,306),(331,318),(348,362)],
[(101,320),(91,288),(159,272),(206,293),(194,331),(150,348)],
]
for p in jaws:
    m=mask_poly(p)
    textured_fill(img,m,(190,143,79),(90,57,27),noise=8)
    dd=ImageDraw.Draw(img)
    stroke_poly(dd,p,(55,37,20,255),6)
    inner=inset_polygon(p,sum(x for x,y in p)/len(p),sum(y for x,y in p)/len(p),.86)
    stroke_poly(dd,inner,(235,190,112,150),2)

d=ImageDraw.Draw(img)
for x,y in [(201,127),(385,201),(311,385),(127,311),(311,127),(385,311),(201,385),(127,201)]:
    d.ellipse(((x-7)*S,(y-7)*S,(x+7)*S,(y+7)*S), fill=(38,31,22,255), outline=(196,145,76,255), width=2*S)
    d.ellipse(((x-2)*S,(y-2)*S,(x+2)*S,(y+2)*S), fill=(240,181,90,230))

housing=[(256,142),(318,177),(340,256),(318,335),(256,370),(194,335),(172,256),(194,177)]
hm=mask_poly(housing)
textured_fill(img,hm,(83,65,43),(31,28,24),noise=7)
stroke_poly(ImageDraw.Draw(img),housing,(210,155,79,255),5)

crystal=[(256,166),(294,191),(312,256),(294,321),(256,346),(218,321),(200,256),(218,191)]
cm=mask_poly(crystal)
glow(img,cm,(255,116,9,255),16,0.8)
core=Image.new("L",(SS,SS),0)
ImageDraw.Draw(core).rectangle((218*S,166*S,294*S,346*S),fill=255)
core=core.filter(ImageFilter.GaussianBlur(18*S))
cr=Image.new("RGBA",(SS,SS),(211,84,10,0))
alpha=ImageChops.multiply(cm, core.point(lambda p: min(235,int(90+p*0.58))))
cr.putalpha(alpha)
img.alpha_composite(cr)
stroke_poly(ImageDraw.Draw(img),crystal,(255,202,102,255),4)
stroke_poly(ImageDraw.Draw(img),inset_polygon(crystal,256,256,.86),(255,238,177,170),2)

d=ImageDraw.Draw(img)
d.line((256*S,174*S,256*S,338*S),fill=(255,244,205,175),width=2*S)
for yy,half in [(207,29),(256,45),(305,29)]:
    d.line(((256-half)*S,yy*S,(256+half)*S,yy*S),fill=(255,211,123,120),width=2*S)

for box in [(91,217,146,295),(366,217,421,295)]:
    m=mask_roundbox(box,10)
    textured_fill(img,m,(88,66,39),(32,29,24),noise=5)
    dd=ImageDraw.Draw(img)
    dd.rounded_rectangle(tuple(int(v*S) for v in box),radius=10*S,outline=(207,153,79,255),width=4*S)
    ix=(box[0]+13,box[1]+14,box[2]-13,box[3]-14)
    dd.rounded_rectangle(tuple(int(v*S) for v in ix),radius=5*S,fill=(16,18,18,255),outline=(88,76,56,255),width=2*S)
    for k in range(3):
        yy=(ix[1]+13+k*17)*S
        dd.line(((ix[0]+5)*S,yy,(ix[2]-5)*S,yy),fill=(231,126,24,220),width=4*S)

d=ImageDraw.Draw(img)
for cy,flip in [(111,1),(401,-1)]:
    eye=[(232,cy),(256,cy-10*flip),(280,cy),(256,cy+10*flip),(232,cy)]
    d.line(pts(eye),fill=(184,132,66,210),width=3*S,joint="curve")
    d.ellipse((251*S,(cy-5)*S,261*S,(cy+5)*S),fill=(231,153,57,220))
    d.line((256*S,(cy+10*flip)*S,256*S,(cy+25*flip)*S),fill=(184,132,66,180),width=3*S)

d=ImageDraw.Draw(img)
for _ in range(190):
    ang=random.random()*math.tau
    rr=random.uniform(90,205)
    x=256+math.cos(ang)*rr
    y=256+math.sin(ang)*rr
    if body_m.getpixel((int(x*S),int(y*S)))>0:
        ln=random.uniform(2,7)
        col=random.choice([(121,110,92,75),(203,155,86,55),(230,230,220,45)])
        d.line((x*S,y*S,(x+ln)*S,(y+random.uniform(-1,1))*S),fill=col,width=max(1,S//2))

img=img.filter(ImageFilter.UnsharpMask(radius=1.2*S, percent=65, threshold=5))
img=img.resize((W,H),Image.Resampling.LANCZOS)

px=img.load()
for y in range(H):
    for x in range(W):
        r,g,b,a=px[x,y]
        if a < 2:
            px[x,y]=(0,0,0,0)

alpha=img.getchannel("A")
bbox=alpha.getbbox()
if bbox is None:
    raise RuntimeError("empty coupler sprite")
if bbox[0] < 18 or bbox[1] < 18 or bbox[2] > 494 or bbox[3] > 494:
    raise RuntimeError(f"unsafe coupler alpha bounds: {bbox}")

OUT.parent.mkdir(parents=True,exist_ok=True)
img.save(OUT,"PNG",optimize=True)
print(f"generated {OUT} bbox={bbox} mode={img.mode} size={img.size}")
