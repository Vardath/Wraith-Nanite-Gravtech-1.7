#!/usr/bin/env python3
"""Deterministic, professionally shaded in-mod resource icon: Iratus pheromone gland."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math, random

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Textures/Things/Item/Resource/WNG_IratusPheromones.png"
OUT.parent.mkdir(parents=True, exist_ok=True)
S=1024
im=Image.new("RGBA",(S,S),(0,0,0,0))
r=random.Random(812107)
def layer(): return Image.new("RGBA",(S,S),(0,0,0,0))
def glow_ellipse(box,color,blur=26,opacity=160):
    a=Image.new("L",(S,S));ImageDraw.Draw(a).ellipse(box,fill=255)
    a=a.filter(ImageFilter.GaussianBlur(blur))
    ink=Image.new("RGBA",(S,S),(*color,0)); ink.putalpha(a.point(lambda p:int(p*opacity/255)))
    im.alpha_composite(ink)
def shadow(box,blur=19,alpha=110):
    a=layer();ImageDraw.Draw(a).ellipse(box,fill=(2,8,8,alpha))
    im.alpha_composite(a.filter(ImageFilter.GaussianBlur(blur)))
def stroke(path,fill,width=8):
    ImageDraw.Draw(im).line(path,fill=fill,width=width,joint="curve")
# Headspace vapor traces: visibly tied to pheromones, retained inside the item silhouette.
for k in range(11):
    x=276+k*37
    y=205+r.randrange(-15,18)
    pts=[(x,y),(x+r.randrange(-30,30),y-34),(x+r.randrange(-34,33),y-83)]
    a=layer();ImageDraw.Draw(a).line(pts,fill=(152,217,98,34+k%3*13),width=9,joint="curve")
    im.alpha_composite(a.filter(ImageFilter.GaussianBlur(14)))
# On-ground core shadow and chitin body.
shadow((215,682,816,873),32,140)
glow_ellipse((295,318,724,777),(194,239,91),70,92)
d=ImageDraw.Draw(im)
# Symmetric, organic Wraith chitin containment carapace, broad readable silhouette.
outer=[(342,247),(285,303),(245,415),(243,588),(289,733),(401,818),(536,841),
       (656,810),(747,733),(788,589),(782,416),(737,302),(665,252),(580,231),(426,230)]
d.polygon(outer,fill=(11,19,22,255))
d.line(outer+[outer[0]],fill=(3,10,12,255),width=22,joint="curve")
mid=[(353,281),(302,345),(280,461),(282,574),(327,699),(429,771),(535,786),
     (638,765),(705,693),(737,572),(740,451),(704,335),(650,279),(571,261),(428,259)]
d.polygon(mid,fill=(46,61,55,255))
d.line(mid+[mid[0]],fill=(108,125,93,255),width=10,joint="curve")
# Organic radiating scallops.
for i in range(5):
    y=332+i*82
    w=74-i*8
    d.ellipse((273-w//3,y-30,354+w//3,y+60),fill=(32,49,47,255),outline=(133,148,93,250),width=6)
    d.ellipse((663-w//3,y-30,743+w//3,y+60),fill=(32,49,47,255),outline=(133,148,93,250),width=6)
# Amber reservoir cavity with dark mineral lip.
reservoir=[(408,301),(503,279),(611,302),(663,382),(684,531),(642,699),
           (568,760),(472,754),(395,695),(358,543),(363,395)]
d.polygon(reservoir,fill=(13,23,23,255))
d.line(reservoir+[reservoir[0]],fill=(164,175,116,255),width=13,joint="curve")
# Three phosphorescent Iratus pheromone chambers.
for cx,cy,rx,ry in [(444,467,64,135),(563,465,65,139),(511,651,85,80)]:
    box=(cx-rx,cy-ry,cx+rx,cy+ry)
    glow_ellipse(box,(237,200,46),27,145)
    d=ImageDraw.Draw(im)
    d.ellipse(box,fill=(90,92,20,255),outline=(208,207,89,255),width=11)
    inner=(cx-rx+13,cy-ry+14,cx+rx-13,cy+ry-13)
    d.ellipse(inner,fill=(168,135,30,255))
    d.ellipse((inner[0]+8,inner[1]+8,inner[2]-14,cy+23),
              fill=(232,182,39,230))
    d.arc((cx-rx+22,cy-ry+18,cx+rx-22,cy+ry-21),190,302,fill=(255,250,178,230),width=13)
    d.ellipse((cx-rx+30,cy-ry+24,cx-rx+43,cy-ry+85),fill=(255,255,200,180))
    for j in range(4):
        yy=cy-ry+45+j*(2*ry-90)//4
        bx=cx+r.randrange(-rx//2,rx//2)
        d.ellipse((bx,yy,bx+10,yy+10),
                  fill=(255,235,90,120))
# Gold-vein Wraith restraint spines, central hinge and armored clasp.
d=ImageDraw.Draw(im)
d.line([(349,331),(408,371),(445,445)],fill=(33,42,34,255),width=34,joint="curve")
d.line([(669,331),(604,371),(563,440)],fill=(33,42,34,255),width=34,joint="curve")
d.line([(348,677),(415,662),(459,621)],fill=(33,42,34,255),width=31,joint="curve")
d.line([(675,677),(609,654),(558,615)],fill=(33,42,34,255),width=31,joint="curve")
for path in [[(340,322),(405,364),(446,442)],[(679,322),(616,364),(566,439)],
             [(337,682),(411,652),(457,619)],[(680,681),(609,650),(563,613)]]:
    d.line(path,fill=(155,169,110,255),width=8,joint="curve")
d.rounded_rectangle((459,258,558,317),radius=21,fill=(28,44,40,255),outline=(157,165,113,255),width=8)
d.rounded_rectangle((459,735,562,802),radius=23,fill=(27,41,39,255),outline=(150,164,111,255),width=7)
# Preserved microscopic chitin stippling and tiny amber motes.
for i in range(130):
    x=r.randint(310,718);y=r.randint(303,725)
    if (x-511)**2/205**2+(y-517)**2/270**2>1:continue
    a=r.choice([18,28,41,60]);radius=r.choice([1,2,3])
    d.ellipse((x-radius,y-radius,x+radius,y+radius),fill=(206,210,122,a))
# Harsh glass catchlights / lower ground contact ridge.
d.arc((287,293,733,782),142,217,fill=(206,223,154,205),width=7)
d.arc((262,261,764,806),311,385,fill=(130,155,107,235),width=6)
# Filter down to a clean, crisp alpha asset with fully transparent exterior pixels.
canvas=im.resize((512,512),Image.Resampling.LANCZOS)
canvas.save(OUT,optimize=True)
check=Image.open(OUT)
assert check.mode=="RGBA" and check.size==(512,512)
alpha=check.getchannel("A")
assert alpha.getbbox() is not None
assert alpha.getpixel((0,0))==0
print(f"Created {OUT.relative_to(ROOT)} ({OUT.stat().st_size} bytes), RGBA 512px.")
