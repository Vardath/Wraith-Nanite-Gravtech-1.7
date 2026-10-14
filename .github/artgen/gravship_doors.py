from PIL import Image,ImageDraw,ImageFilter
from pathlib import Path

PRE=Path("Textures/Things/Building/Precursor/Gravship")
GOA=Path("Textures/Things/Building/Goauld/Gravship")
WRA=Path("Textures/Things/Building/Wraith/Gravship")
for p in (PRE,GOA,WRA): p.mkdir(parents=True,exist_ok=True)

def c(n): return Image.new("RGBA",(n,n),(0,0,0,0))
def glow(im,fn,color,blur=15,a=160):
    g=c(im.width); d=ImageDraw.Draw(g); fn(d,color+(a,)); g=g.filter(ImageFilter.GaussianBlur(blur)); im.alpha_composite(g)
def center(im):
    b=im.getchannel("A").getbbox()
    if not b:return im
    dx=round(im.width/2-(b[0]+b[2])/2);dy=round(im.height/2-(b[1]+b[3])/2)
    o=c(im.width);o.alpha_composite(im,(dx,dy));return o

def asuran():
    S=1024; im=c(S); d=ImageDraw.Draw(im)
    d.rounded_rectangle((90,250,934,774),radius=90,fill=(26,34,42,255),outline=(144,163,176,255),width=20)
    d.rounded_rectangle((138,294,886,730),radius=68,fill=(45,56,66,255),outline=(190,204,213,255),width=10)
    d.polygon([(165,330),(475,330),(500,512),(475,694),(165,694)],fill=(57,69,80,255),outline=(104,127,142,255))
    d.polygon([(859,330),(549,330),(524,512),(549,694),(859,694)],fill=(57,69,80,255),outline=(104,127,142,255))
    for x in (270,365,659,754): d.line((x,365,x,659),fill=(28,37,45,255),width=16)
    glow(im,lambda gd,col: gd.rounded_rectangle((493,316,531,708),radius=18,fill=col),(55,220,255),16,170)
    d=ImageDraw.Draw(im); d.rounded_rectangle((500,328,524,696),radius=12,fill=(103,239,255,255))
    for y in (400,512,624):
        d.ellipse((478,y-14,506,y+14),fill=(92,225,248,255))
        d.ellipse((518,y-14,546,y+14),fill=(92,225,248,255))
    return center(im.resize((512,512),Image.Resampling.LANCZOS))

def goauld():
    S=1024; im=c(S); d=ImageDraw.Draw(im)
    d.rounded_rectangle((78,235,946,789),radius=70,fill=(18,15,13,255),outline=(117,72,27,255),width=28)
    d.rounded_rectangle((126,283,898,741),radius=46,fill=(43,31,21,255),outline=(194,124,43,255),width=20)
    d.rectangle((170,320,854,704),fill=(28,23,19,255),outline=(104,61,25,255),width=12)
    d.polygon([(175,323),(470,323),(500,512),(470,701),(175,701)],fill=(45,33,24,255),outline=(123,76,31,255))
    d.polygon([(849,323),(554,323),(524,512),(554,701),(849,701)],fill=(45,33,24,255),outline=(123,76,31,255))
    glow(im,lambda gd,col: gd.rounded_rectangle((482,300,542,724),radius=24,fill=col),(255,150,27),20,175)
    d=ImageDraw.Draw(im); d.rounded_rectangle((492,312,532,712),radius=17,fill=(176,100,27,255),outline=(255,194,77,255),width=7)
    d.polygon([(512,422),(602,512),(512,602),(422,512)],fill=(37,24,17,255),outline=(208,132,44,255))
    glow(im,lambda gd,col: gd.ellipse((472,472,552,552),fill=col),(255,154,30),18,180)
    d=ImageDraw.Draw(im); d.ellipse((486,486,538,538),fill=(245,160,48,255),outline=(255,228,146,255),width=6)
    for x in (225,800): d.rectangle((x,344,x+28,680),fill=(112,66,27,255))
    return center(im.resize((512,512),Image.Resampling.LANCZOS))

def wraith():
    S=1024; im=c(S); d=ImageDraw.Draw(im)
    body=[(95,300),(210,215),(430,245),(512,310),(594,245),(814,215),(929,300),(895,690),(760,795),(590,770),(512,710),(434,770),(264,795),(129,690)]
    d.polygon(body,fill=(42,24,38,255),outline=(86,54,79,255))
    inner=[(165,335),(265,285),(430,310),(480,365),(480,660),(420,715),(270,730),(165,655)]
    d.polygon(inner,fill=(62,31,50,255),outline=(102,62,91,255))
    r=[(1024-x,y) for x,y in inner]; d.polygon(r,fill=(62,31,50,255),outline=(102,62,91,255))
    glow(im,lambda gd,col: gd.rounded_rectangle((484,330,540,704),radius=25,fill=col),(64,210,167),20,150)
    d=ImageDraw.Draw(im); d.rounded_rectangle((495,342,529,692),radius=18,fill=(45,130,110,255),outline=(100,226,190,255),width=6)
    for y in (410,512,614):
        d.ellipse((445,y-18,480,y+18),fill=(91,52,82,255))
        d.ellipse((544,y-18,579,y+18),fill=(91,52,82,255))
    return center(im.resize((512,512),Image.Resampling.LANCZOS))

families=[
    (PRE,"WNG_AsuranGravshipDoor",asuran(),160),
    (GOA,"WNG_GoauldGravshipDoor",goauld(),512),
    (WRA,"WNG_WraithGravshipDoor",wraith(),160),
]
for folder,name,base,small in families:
    base.save(folder/f"{name}.png",optimize=True)
    for suf,ang in [("_east",0),("_north",90),("_south",-90),("_west",180)]:
        v=center(base.rotate(ang,resample=Image.Resampling.BICUBIC,expand=False)).resize((small,small),Image.Resampling.LANCZOS)
        v.save(folder/f"{name}{suf}.png",optimize=True)

for folder,name,base,small in families:
    for p in sorted(folder.glob(name+"*.png")):
        im=Image.open(p).convert("RGBA")
        assert im.getchannel("A").getbbox(), p
        assert im.getchannel("A").getextrema()[0]==0, p
        print(p,im.size,im.getchannel("A").getbbox())
