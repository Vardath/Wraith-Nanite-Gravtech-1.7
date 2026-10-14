from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageChops
import numpy as np, math, random, hashlib

ROOT=Path(__file__).resolve().parents[3]
APP=ROOT/"Textures/Things/Pawn/Humanlike/Apparel/Precursor"
PREFIX="WNG_HumanFormCombatArmor"

def bbox(mask):
    b=mask.getbbox()
    if not b: raise RuntimeError("empty apparel alpha")
    return b

def interp_palette(t, shadow, mid, high):
    s=np.array(shadow,np.float32); m=np.array(mid,np.float32); h=np.array(high,np.float32)
    out=np.empty(t.shape+(3,),np.float32)
    lo=t<0.52
    q=np.clip(t/0.52,0,1)
    out[lo]=s+(m-s)*q[lo,None]
    q2=np.clip((t-0.52)/0.48,0,1)
    out[~lo]=m+(h-m)*q2[~lo,None]
    return out

def luminance(arr, alpha):
    rgb=arr[...,:3].astype(np.float32)
    lum=.2126*rgb[...,0]+.7152*rgb[...,1]+.0722*rgb[...,2]
    m=alpha>8
    vals=lum[m]
    lo,hi=np.percentile(vals,[4,98]) if vals.size else (0,255)
    t=np.clip((lum-lo)/max(1.0,hi-lo),0,1)
    return np.sqrt(t)

def draw_design(mask, direction, seed):
    W,H=mask.size
    x0,y0,x1,y1=bbox(mask); w=x1-x0; h=y1-y0
    def P(points):
        return [(x0+int(px*w),y0+int(py*h)) for px,py in points]

    red=Image.new("L",(W,H),0)
    gold=Image.new("L",(W,H),0)
    dark=Image.new("L",(W,H),0)
    rd=ImageDraw.Draw(red); gd=ImageDraw.Draw(gold); dd=ImageDraw.Draw(dark)

    if direction in ("south","north","base"):
        # Distinct combat shell: short armored torso, articulated shoulders, no long coat tails.
        rd.polygon(P([(0.39,.12),(.61,.12),(.57,.52),(.64,.73),(.54,.88),(.46,.88),(.36,.73),(.43,.52)]),fill=220)
        rd.polygon(P([(0.12,.27),(.28,.17),(.36,.27),(.31,.49),(.18,.45)]),fill=150)
        rd.polygon(P([(.88,.27),(.72,.17),(.64,.27),(.69,.49),(.82,.45)]),fill=150)
        # central crimson lattice with gold separators
        gd.line(P([(.50,.10),(.50,.88)]),fill=255,width=max(1,int(w*.018)))
        gd.line(P([(.31,.21),(.43,.34),(.40,.66),(.32,.80)]),fill=235,width=max(1,int(w*.014)))
        gd.line(P([(.69,.21),(.57,.34),(.60,.66),(.68,.80)]),fill=235,width=max(1,int(w*.014)))
        # shoulder crescents
        gd.line(P([(.10,.25),(.22,.13),(.36,.21)]),fill=255,width=max(1,int(w*.02)))
        gd.line(P([(.90,.25),(.78,.13),(.64,.21)]),fill=255,width=max(1,int(w*.02)))
        # dark flexible joint channels
        dd.polygon(P([(.05,.31),(.18,.28),(.23,.74),(.13,.78),(.06,.61)]),fill=175)
        dd.polygon(P([(.95,.31),(.82,.28),(.77,.74),(.87,.78),(.94,.61)]),fill=175)
        if direction=="north":
            # back spine / command-precursor seam structure
            rd.polygon(P([(.45,.12),(.55,.12),(.58,.65),(.50,.82),(.42,.65)]),fill=205)
            gd.line(P([(.50,.12),(.50,.82)]),fill=255,width=max(1,int(w*.02)))
    else:
        # true side: forward chest plate, shoulder cap, segmented flank
        side=1 if direction=="east" else -1
        rd.polygon(P([(.26,.18),(.63,.12),(.79,.30),(.71,.56),(.48,.70),(.24,.57)]),fill=205)
        rd.polygon(P([(.18,.27),(.42,.16),(.52,.31),(.37,.48),(.18,.43)]),fill=150)
        gd.line(P([(.30,.20),(.62,.16),(.71,.35),(.60,.58),(.43,.72)]),fill=255,width=max(1,int(w*.018)))
        gd.line(P([(.24,.53),(.50,.48),(.66,.58)]),fill=220,width=max(1,int(w*.012)))
        dd.polygon(P([(.10,.35),(.23,.30),(.30,.73),(.18,.78),(.10,.60)]),fill=175)

    # restrained panel separators
    sep=Image.new("L",(W,H),0); sd=ImageDraw.Draw(sep)
    if direction in ("south","north","base"):
        for yy in (.35,.52,.69):
            sd.line(P([(.28,yy),(.72,yy)]),fill=115,width=max(1,int(w*.008)))
    else:
        for yy in (.38,.55,.68):
            sd.line(P([(.24,yy),(.66,yy)]),fill=100,width=max(1,int(w*.008)))

    for im in (red,gold,dark,sep):
        im.paste(ImageChops.multiply(im,mask))
    return red,gold,dark,sep

def texture_noise(shape, seed):
    rng=np.random.default_rng(seed)
    n=rng.normal(0,1,shape).astype(np.float32)
    im=Image.fromarray(np.uint8(np.clip((n-n.min())/(n.max()-n.min()+1e-6)*255,0,255)),"L")
    low=np.asarray(im.filter(ImageFilter.GaussianBlur(5.0)),np.float32)/255
    fine=np.asarray(im.filter(ImageFilter.GaussianBlur(.65)),np.float32)/255
    return (low-.5)*.05+(fine-.5)*.018

def render(src):
    im=Image.open(src).convert("RGBA")
    arr=np.array(im)
    alpha=arr[...,3]
    mask=Image.fromarray(alpha,"L")
    stem=src.stem.lower()
    if stem==PREFIX.lower(): direction="base"
    elif "_north" in stem: direction="north"
    elif "_east" in stem: direction="east"
    elif "_west" in stem: direction="west"
    else: direction="south"

    t=luminance(arr,alpha)
    seed=int(hashlib.sha256(src.name.encode()).hexdigest()[:8],16)
    t=np.clip(t+texture_noise(t.shape,seed),0,1)

    # White/ivory Asuran composite is dominant.
    ivory=interp_palette(t,(58,57,53),(177,174,164),(246,241,224))
    crimson=interp_palette(t,(55,7,10),(137,22,27),(219,70,54))
    gold=interp_palette(t,(61,39,13),(155,108,37),(237,198,101))
    graphite=interp_palette(t,(12,13,14),(34,35,35),(72,72,68))

    rm,gm,dm,sm=draw_design(mask,direction,seed)
    r=np.asarray(rm,np.float32)/255
    g=np.asarray(gm,np.float32)/255
    d=np.asarray(dm,np.float32)/255
    s=np.asarray(sm,np.float32)/255

    out=ivory.copy()
    out=out*(1-r[...,None])+crimson*r[...,None]
    out=out*(1-g[...,None])+gold*g[...,None]
    out=out*(1-d[...,None])+graphite*d[...,None]
    # fine separator grooves
    out=out*(1-(s*.34)[...,None])+graphite*(s*.34)[...,None]

    # reinforce actual shell edges in antique gold without flooding the white base
    edge=np.asarray(mask.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.GaussianBlur(.5)),np.float32)/255
    edge=np.clip(edge*0.42,0,.34)
    out=out*(1-edge[...,None])+gold*edge[...,None]

    rgb=np.clip(out,0,255).astype(np.uint8)
    res=Image.fromarray(np.dstack([rgb,alpha]),"RGBA")
    res=ImageEnhance.Contrast(res).enhance(1.08)
    res.putalpha(mask)
    return res

files=sorted(APP.glob(PREFIX+"*.png"))
assert len(files)==26, f"Expected 26 Human Form Combat Armour PNGs, got {len(files)}"
for p in files:
    out=render(p)
    out.save(p,optimize=True)
    print(p.relative_to(ROOT),out.getbbox())
