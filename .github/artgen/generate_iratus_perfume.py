#!/usr/bin/env python3
"""Build polished transparent RimWorld-scale Iratus perfume art (no chat image)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math, random

ROOT=Path(__file__).resolve().parents[2]
OUTPUT=ROOT/"Textures/Things/Item/Drug/WNG_IratusPerfume.png"
OUTPUT.parent.mkdir(parents=True,exist_ok=True)
S=1024
rng=random.Random(411080)
canvas=Image.new("RGBA",(S,S),(0,0,0,0))
def over(layer):
    canvas.alpha_composite(layer)
def new():
    return Image.new("RGBA",(S,S),(0,0,0,0))
def g_oval(bounds,color,blur=26,alpha=120):
    mask=Image.new("L",(S,S),0)
    ImageDraw.Draw(mask).ellipse(bounds,fill=255)
    mask=mask.filter(ImageFilter.GaussianBlur(blur))
    light=Image.new("RGBA",(S,S),tuple(color)+(0,))
    light.putalpha(mask.point(lambda v: v*alpha//255))
    over(light)

# Restrained airborne luminous fragrance, not a baked-in shadow/background.
for i in range(11):
    x=350+i*30
    y=207+(i%3)*12
    ly=new()
    ld=ImageDraw.Draw(ly)
    ld.arc((x-38,y-85,x+38,y+33),90,225,fill=(230,196,127,40),width=8)
    over(ly.filter(ImageFilter.GaussianBlur(13)))

# Thick faceted chitin/obsidian outer case, violet enamel and brass edging.
g_oval((290,328,725,803),(188,105,210),60,73)
l=new(); d=ImageDraw.Draw(l)
# Art-deco octagonal parfum silhouette, cap, atomizer and shoulder pieces.
sil=[(381,336),(445,310),(578,310),(644,336),(696,416),(696,738),
     (654,786),(365,786),(323,738),(323,416)]
d.polygon(sil,fill=(13,18,32,255))
d.line(sil+[sil[0]],fill=(8,10,17,255),width=25,joint="curve")
inner=[(397,352),(459,332),(560,332),(625,353),(673,421),(673,724),
       (639,760),(380,760),(346,724),(346,421)]
d.polygon(inner,fill=(45,36,70,255))
d.line(inner+[inner[0]],fill=(180,142,87,255),width=11,joint="curve")
# Horizontal metal neck, bottle hood, glass stopper.
d.rounded_rectangle((456,243,565,337),radius=17,fill=(25,24,37,255),outline=(209,172,92,255),width=13)
d.rounded_rectangle((432,212,588,268),radius=13,fill=(30,32,49,255),outline=(235,202,121,255),width=9)
d.rounded_rectangle((462,187,559,224),radius=11,fill=(122,105,135,255),outline=(252,216,138,255),width=8)
d.rounded_rectangle((493,165,527,205),radius=6,fill=(207,173,102,255),outline=(255,227,161,255),width=5)
d.ellipse((483,142,536,175),fill=(54,48,74,255),outline=(237,204,132,255),width=7)
over(l)

# Volumetric violet glass and layered amber/rose fluid.
g_oval((355,367,671,740),(168,78,208),42,106)
l=new();d=ImageDraw.Draw(l)
d.rounded_rectangle((374,374,644,729),radius=49,fill=(55,39,85,249),outline=(123,115,160,255),width=8)
# Internal glass liquid is brightest at bottom; glowing organic pigment at the center.
for k in range(8):
    y0=446+k*31
    c=(114+int(k*7),58+int(k*8),132-int(k*7),156+int(k*10))
    d.rounded_rectangle((389,y0,630,707),radius=29,fill=c)
d.rounded_rectangle((400,526,617,707),radius=32,fill=(183,99,122,222))
d.arc((396,417,618,537),192,353,fill=(244,177,153,220),width=8)
over(l)
g_oval((416,486,590,674),(250,187,99),34,130)

# Suspended bioluminescent Iratus extract in perfume, not replicator cubes.
l=new();d=ImageDraw.Draw(l)
for i in range(30):
    x=rng.randrange(413,612);y=rng.randrange(430,696)
    rx=2+(i%4);ry=rx+3
    d.ellipse((x-rx,y-ry,x+rx,y+ry),fill=(252,221,157,110+(i%4)*20))
d.ellipse((435,535,475,566),fill=(255,226,151,110))
d.arc((386,385,630,726),164,224,fill=(247,212,200,205),width=18)
d.arc((407,399,625,715),300,353,fill=(251,213,183,153),width=10)
# Deliberately sparse graphic details to remain readable at 64 px.
over(l)

# Brushed inset gold trim and iconic Wraith wing/chitin motif.
l=new();d=ImageDraw.Draw(l)
d.polygon([(379,432),(404,411),(441,432),(457,487),(424,462)],
          fill=(37,28,52,250),outline=(201,169,105,255))
d.polygon([(639,432),(615,411),(579,432),(561,487),(594,462)],
          fill=(37,28,52,250),outline=(201,169,105,255))
for flip in (False,True):
    xx=lambda x:1021-x if flip else x
    pts=[(xx(410),488),(xx(445),509),(xx(476),559),(xx(498),603)]
    d.line(pts,fill=(218,178,120,221),width=9,joint="curve")
    d.ellipse((xx(486)-8,590,xx(486)+8,606),fill=(249,210,144,255))
# Golden crown-like engraving and embedded gemstone on face.
d.rounded_rectangle((457,359,565,397),radius=12,fill=(31,28,53,255),outline=(222,189,114,255),width=7)
d.polygon([(511,361),(531,377),(511,391),(491,377)],fill=(213,145,184,255),outline=(251,217,170,255))
d.line([(388,735),(444,750),(575,750),(630,735)],fill=(244,208,141,245),width=10,joint="curve")
d.line([(370,395),(348,444),(348,697),(375,734)],fill=(178,163,184,172),width=7,joint="curve")
d.line([(646,396),(671,440),(671,693),(643,731)],fill=(233,190,126,145),width=7,joint="curve")
over(l)

# Smooth downsample and fully transparent outside the icon.
out=canvas.resize((512,512),Image.Resampling.LANCZOS)
out.save(OUTPUT,"PNG",optimize=True)
img=Image.open(OUTPUT);img.load()
assert img.mode=="RGBA" and img.size==(512,512)
assert img.getchannel("A").getbbox() is not None
assert all(img.getpixel(p)[3]==0 for p in ((0,0),(511,0),(0,511),(511,511)))
print("Rendered WNG perfume item PNG: "+str(OUTPUT.relative_to(ROOT))+" size="+str(OUTPUT.stat().st_size))
