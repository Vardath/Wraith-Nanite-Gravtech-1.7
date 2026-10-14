from PIL import Image,ImageDraw,ImageFilter,ImageChops
from pathlib import Path

ROOT=Path("Textures/Things/Pawn/Humanlike/Apparel/Precursor")
FAMILIES=["WNG_HumanFormCombatArmor","WNG_HumanFormUniform","WNG_PrecursorCommandArmor"]

def grad(size,top,bottom):
    w,h=size; im=Image.new("RGBA",size); px=im.load()
    for y in range(h):
        t=y/max(1,h-1); c=tuple(int(top[i]*(1-t)+bottom[i]*t) for i in range(4))
        for x in range(w): px[x,y]=c
    return im

def clip(layer,mask):
    layer.putalpha(ImageChops.multiply(layer.getchannel("A"),mask)); return layer

def glowline(base,pts,col,width=2):
    g=Image.new("RGBA",base.size,(0,0,0,0)); gd=ImageDraw.Draw(g)
    gd.line(pts,fill=col[:3]+(150,),width=width*3,joint="curve")
    g=g.filter(ImageFilter.GaussianBlur(2.5)); base.alpha_composite(g)
    ImageDraw.Draw(base).line(pts,fill=col,width=width,joint="curve")

def render(src,style):
    old=Image.open(src).convert("RGBA"); mask=old.getchannel("A"); b=mask.getbbox()
    if not b:return old
    x0,y0,x1,y1=b; w=x1-x0; h=y1-y0; cx=(x0+x1)//2
    low=src.stem.lower()
    side="east" if "_east" in low else "west" if "_west" in low else "north" if "_north" in low else "south"

    if style=="uniform":
        top=(58,69,80,255); bottom=(22,28,34,255); edge=(9,13,17,255); accent=(58,212,239,255)
    elif style=="combat":
        top=(92,103,115,255); bottom=(29,35,42,255); edge=(8,11,14,255); accent=(47,219,255,255)
    else:
        top=(105,111,118,255); bottom=(32,37,44,255); edge=(10,12,15,255); accent=(255,171,44,255)

    out=Image.new("RGBA",old.size,(0,0,0,0))
    base=grad(old.size,top,bottom); base.putalpha(mask); out.alpha_composite(base)

    dil=mask.filter(ImageFilter.MaxFilter(5))
    ed=ImageChops.subtract(dil,mask)
    edge_layer=Image.new("RGBA",old.size,edge); edge_layer.putalpha(ed); out.alpha_composite(edge_layer)

    lay=Image.new("RGBA",old.size,(0,0,0,0)); d=ImageDraw.Draw(lay)
    def P(rel,fill,outline=(10,14,18,255),wid=2):
        pts=[(x0+int(px*w),y0+int(py*h)) for px,py in rel]
        d.polygon(pts,fill=fill); d.line(pts+[pts[0]],fill=outline,width=wid,joint="curve")

    if style=="uniform":
        if side in ("south","north"):
            P([(0.08,.12),(.36,.06),(.50,.18),(.64,.06),(.92,.12),(.84,.92),(.16,.92)],(46,56,66,255),(15,20,25,255),1)
            P([(.18,.18),(.42,.14),(.50,.24),(.58,.14),(.82,.18),(.70,.68),(.50,.80),(.30,.68)],(55,66,76,255),(18,24,29,255),1)
        else:
            P([(.18,.12),(.78,.10),(.83,.88),(.24,.90)],(48,58,68,255),(15,20,25,255),1)
            P([(.30,.20),(.70,.18),(.68,.70),(.34,.74)],(57,68,78,255),(18,24,29,255),1)

    elif style=="combat":
        if side in ("south","north"):
            P([(.03,.12),(.22,.02),(.43,.12),(.36,.34),(.10,.32)],(102,113,124,255))
            P([(.97,.12),(.78,.02),(.57,.12),(.64,.34),(.90,.32)],(102,113,124,255))
            P([(.26,.18),(.50,.08),(.74,.18),(.68,.73),(.50,.93),(.32,.73)],(49,59,69,255))
            P([(.06,.35),(.30,.30),(.31,.72),(.14,.82),(.04,.60)],(67,77,88,255))
            P([(.94,.35),(.70,.30),(.69,.72),(.86,.82),(.96,.60)],(67,77,88,255))
        else:
            P([(.10,.14),(.40,.02),(.78,.13),(.72,.35),(.25,.36)],(96,108,120,255))
            P([(.17,.34),(.76,.30),(.83,.69),(.60,.90),(.22,.76)],(48,58,68,255))
            for k in range(3):
                yy=.45+k*.11
                P([(.25,yy),(.68,yy-.02),(.65,yy+.06),(.30,yy+.08)],(62,72,82,255),(14,19,24,255),1)

    else:
        if side in ("south","north"):
            P([(.02,.13),(.20,.01),(.42,.10),(.34,.33),(.10,.31)],(120,126,132,255),(24,26,30,255),2)
            P([(.98,.13),(.80,.01),(.58,.10),(.66,.33),(.90,.31)],(120,126,132,255),(24,26,30,255),2)
            P([(.25,.17),(.50,.06),(.75,.17),(.68,.74),(.50,.94),(.32,.74)],(55,62,70,255),(18,22,27,255),2)
            P([(.08,.37),(.31,.31),(.32,.69),(.16,.80),(.05,.61)],(78,84,91,255))
            P([(.92,.37),(.69,.31),(.68,.69),(.84,.80),(.95,.61)],(78,84,91,255))
        else:
            P([(.10,.14),(.40,.01),(.80,.13),(.72,.36),(.24,.36)],(116,122,128,255))
            P([(.16,.34),(.78,.30),(.83,.69),(.60,.90),(.22,.77)],(58,64,71,255))

    out.alpha_composite(clip(lay,mask))

    if side in ("south","north"):
        if style=="uniform":
            glowline(out,[(cx,y0+int(.16*h)),(cx,y0+int(.38*h))],accent,1)
            glowline(out,[(cx,y0+int(.38*h)),(cx-int(.17*w),y0+int(.56*h))],accent,1)
            glowline(out,[(cx,y0+int(.38*h)),(cx+int(.17*w),y0+int(.56*h))],accent,1)
            ImageDraw.Draw(out).line((x0+int(.25*w),y0+int(.72*h),x1-int(.25*w),y0+int(.72*h)),fill=(107,125,136,255),width=2)
        elif style=="combat":
            glowline(out,[(cx,y0+int(.20*h)),(cx,y0+int(.72*h))],accent,2)
            glowline(out,[(cx,y0+int(.45*h)),(cx-int(.18*w),y0+int(.55*h))],accent,1)
            glowline(out,[(cx,y0+int(.45*h)),(cx+int(.18*w),y0+int(.55*h))],accent,1)
        else:
            glowline(out,[(cx,y0+int(.18*h)),(cx,y0+int(.67*h))],(68,220,255,255),2)
            dd=ImageDraw.Draw(out); r=max(2,w//20); cy=y0+int(.40*h)
            dd.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(255,182,60,255),outline=(255,225,152,255),width=1)
            glowline(out,[(cx-int(.15*w),y0+int(.22*h)),(cx-int(.28*w),y0+int(.35*h))],accent,1)
            glowline(out,[(cx+int(.15*w),y0+int(.22*h)),(cx+int(.28*w),y0+int(.35*h))],accent,1)
    else:
        xline=x0+int((.61 if side=="east" else .39)*w)
        glowline(out,[(xline,y0+int(.22*h)),(xline,y0+int(.72*h))],accent if style!="command" else (68,220,255,255),2)
        if style=="command":
            dd=ImageDraw.Draw(out); cy=y0+int(.42*h); r=max(2,w//18)
            dd.ellipse((xline-r,cy-r,xline+r,cy+r),fill=(255,182,60,255))

    out.putalpha(mask)
    return out

for fam in FAMILIES:
    style="uniform" if "Uniform" in fam else "combat" if "CombatArmor" in fam else "command"
    files=sorted(ROOT.glob(fam+"*.png"))
    assert len(files)==26,(fam,len(files))
    for p in files:
        im=render(p,style)
        im.save(p,optimize=True)
        a=im.getchannel("A")
        assert a.getbbox() and a.getextrema()[0]==0,p
        print(p,im.size,a.getbbox())

print("Precursor apparel generated: 78")
