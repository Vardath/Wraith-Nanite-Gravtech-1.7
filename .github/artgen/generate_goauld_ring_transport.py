#!/usr/bin/env python3
"""Create matching Goa'uld floor ring platform and segmented levitating-ring FX.
PNG assets are generated inside WNG; the game displays five animated FX overlays.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math, random

ROOT=Path(__file__).resolve().parents[2]
DIR=ROOT/"Textures/Things/Building/Goauld"
DIR.mkdir(parents=True,exist_ok=True)
S=1024
random.seed(20261010)

def layer():
    return Image.new("RGBA",(S,S),(0,0,0,0))
def gold_glow(canvas,box,amount=130,blur=14):
    m=Image.new("L",(S,S),0)
    ImageDraw.Draw(m).ellipse(box,outline=255,width=12)
    m=m.filter(ImageFilter.GaussianBlur(blur))
    g=Image.new("RGBA",(S,S),(252,179,39,0))
    g.putalpha(m.point(lambda a:a*amount//255))
    canvas.alpha_composite(g)
def finish(img,name):
    small=img.resize((512,512),Image.Resampling.LANCZOS)
    out=DIR/name
    small.save(out,"PNG",optimize=True)
    check=Image.open(out);check.load()
    assert check.size==(512,512) and check.mode=="RGBA"
    assert check.getchannel("A").getbbox()
    assert check.getpixel((0,0))[3]==0
    print("WNG RING ART",out.relative_to(ROOT),out.stat().st_size)

def plate():
    im=layer()
    d=ImageDraw.Draw(im)
    # Under-floor soft contact shading: no solid rectangular tile.
    shadow=layer()
    sd=ImageDraw.Draw(shadow)
    sd.ellipse((52,130,972,930),fill=(5,8,11,155))
    im.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(32)))
    # Seven concentric sloped polished bronze/chitin engineering rings.
    rings=[
        ((44,90,980,934),(18,17,18,255)),
        ((51,95,973,921),(117,78,35,255)),
        ((62,108,962,906),(218,157,68,255)),
        ((76,122,948,891),(37,34,31,255)),
        ((87,136,937,877),(150,111,57,255)),
        ((103,151,920,860),(49,47,44,255)),
        ((124,166,900,840),(171,119,53,255)),
        ((143,184,880,824),(18,24,25,255)),
    ]
    for box,color in rings:d.ellipse(box,fill=color)
    # Recessed inactive ring storage channels and brass seals.
    for box,col,width in [((110,151,912,862),(228,158,53,255),10),
                          ((133,174,892,842),(94,64,36,255),14),
                          ((167,201,858,807),(184,133,64,255),8)]:
        d.ellipse(box,outline=col,width=width)
    # Sunburst triangular Goa'uld module divisions (gold on dark disk).
    cx,cy=512,510
    for k in range(16):
        theta=2*math.pi*k/16
        x0=cx+358*math.cos(theta);y0=cy+318*math.sin(theta)
        x1=cx+398*math.cos(theta);y1=cy+353*math.sin(theta)
        d.line((x0,y0,x1,y1),fill=(24,23,25,255),width=14)
        d.line((x0+3,y0-4,x1+2,y1-4),fill=(177,117,43,240),width=4)
    # Small individual inset luminous nodes on outer annulus.
    for k in range(24):
        th=2*math.pi*k/24
        px=round(cx+426*math.cos(th));py=round(cy+369*math.sin(th))
        d.ellipse((px-10,py-10,px+10,py+10),fill=(21,19,17,255),
                  outline=(239,170,78,255),width=4)
        if k%3==0:d.ellipse((px-3,py-3,px+3,py+3),fill=(255,207,100,255))
    # Central transporter plate: flush dark disc with a raised golden iris.
    d.ellipse((220,260,802,756),fill=(28,34,35,255),
              outline=(214,160,72,255),width=16)
    d.ellipse((242,278,780,739),fill=(40,43,42,255),
              outline=(101,82,57,255),width=12)
    d.ellipse((275,307,749,710),fill=(27,30,33,255),
              outline=(179,121,51,255),width=8)
    for k in range(8):
        ang=2*math.pi*k/8
        x1=cx+78*math.cos(ang);y1=cy+67*math.sin(ang)
        x2=cx+201*math.cos(ang);y2=cy+173*math.sin(ang)
        d.line((x1,y1,x2,y2),fill=(6,12,16,255),width=19)
        d.line((x1,y1-2,x2,y2-2),fill=(141,105,52,255),width=5)
    d.ellipse((423,430,601,588),fill=(12,15,18,255),
              outline=(231,172,80,255),width=14)
    d.ellipse((444,446,580,572),outline=(110,73,31,255),width=6)
    d.polygon(((511,455),(569,540),(455,540)),fill=(206,139,46,255),
              outline=(255,216,129,255))
    d.polygon(((511,480),(543,531),(479,531)),fill=(250,196,85,255))
    gold_glow(im,(151,189,869,822),70,20)
    d=ImageDraw.Draw(im)
    d.arc((57,98,962,914),191,320,fill=(250,214,139,240),width=13)
    d.arc((110,146,906,862),0,167,fill=(191,135,65,195),width=8)
    d.arc((144,192,880,824),195,308,fill=(249,194,104,205),width=8)
    return im

def levitating_ring():
    im=layer()
    d=ImageDraw.Draw(im)
    # One horizontal annulus in shallow perspective. Layers on the screen
    # imitate the sidewall thickness and Goa'uld naquadah segmentation.
    # Each ring is drawn at a different screen-north offset by C#.
    outer=(42,285,980,641)
    d.ellipse((44,310,980,674),fill=(13,16,22,255))
    d.ellipse((46,287,978,641),fill=(128,81,29,255))
    d.ellipse((59,301,966,628),fill=(210,142,49,255))
    d.ellipse((73,316,950,615),fill=(34,30,28,255))
    d.ellipse((101,338,922,590),fill=(90,65,39,255))
    d.ellipse((119,345,903,578),fill=(1,3,6,0))
    # Inset gold core and warm blue/white energy at the ring interior.
    d.arc((72,316,950,617),181,355,fill=(255,224,137,255),width=17)
    d.arc((74,320,950,622),15,157,fill=(181,107,40,255),width=15)
    d.arc((97,333,926,609),200,342,fill=(91,223,233,255),width=8)
    d.arc((113,345,908,585),25,157,fill=(52,126,164,213),width=7)
    # Distinct radial separators: metal blocks curve around the ring.
    cx,cy=510,467
    for k in range(18):
        a=2*math.pi*k/18
        c,s=math.cos(a),math.sin(a)
        x0=cx+402*c; y0=cy+141*s
        x1=cx+466*c; y1=cy+174*s
        d.line((x0,y0,x1,y1),fill=(12,14,17,240),width=15)
        d.line((x0-2,y0-3,x1-2,y1-4),fill=(254,199,106,186),width=4)
    # Four tiny inset luminous runes for a purposeful high-quality silhouette.
    for k in range(8):
        a=2*math.pi*(k+.5)/8
        x=cx+430*math.cos(a); y=cy+154*math.sin(a)
        d.ellipse((x-7,y-7,x+7,y+7),fill=(255,204,91,255))
    # Soft emissive edge without a solid fill inside the open ring.
    halo=layer()
    hm=ImageDraw.Draw(halo)
    hm.arc((65,305,953,623),184,357,fill=(244,172,61,160),width=14)
    halo=halo.filter(ImageFilter.GaussianBlur(24))
    im.alpha_composite(halo)
    # Keep the center entirely transparent: characters remain visible.
    ImageDraw.Draw(im).ellipse((131,356,892,567),fill=(0,0,0,0))
    return im

finish(plate(),"WNG_GoauldTransportRings.png")
finish(levitating_ring(),"WNG_GoauldRingEffect.png")
