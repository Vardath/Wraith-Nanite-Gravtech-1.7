from pathlib import Path
from PIL import Image, ImageFilter, ImageEnhance, ImageChops
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
APP = ROOT / "Textures/Things/Pawn/Humanlike/Apparel/Precursor"
SRC_PREFIX = "WNG_PrecursorFieldArmor"
UNI_PREFIX = "WNG_HumanFormUniform"
OUT_PREFIX = "WNG_HumanFormFieldArmor"

def norm_luma(arr, alpha):
    rgb = arr[..., :3].astype(np.float32)
    lum = rgb[...,0]*0.2126 + rgb[...,1]*0.7152 + rgb[...,2]*0.0722
    m = alpha > 8
    vals = lum[m]
    if vals.size == 0:
        return np.zeros_like(lum)
    lo, hi = np.percentile(vals, [5, 97])
    t = np.clip((lum-lo) / max(1.0, hi-lo), 0, 1)
    return np.sqrt(t)

def shade_palette(t, shadow, mid, high):
    shadow=np.array(shadow,np.float32)
    mid=np.array(mid,np.float32)
    high=np.array(high,np.float32)
    out=np.empty(t.shape+(3,),np.float32)
    low=t<0.52
    q=np.clip(t/0.52,0,1)
    out[low]=shadow+(mid-shadow)*q[low,None]
    q2=np.clip((t-0.52)/0.48,0,1)
    out[~low]=mid+(high-mid)*q2[~low,None]
    return out

def color_masks(arr, uniform, alpha):
    r,g,b=[arr[...,i].astype(np.float32) for i in range(3)]
    ur,ug,ub=[uniform[...,i].astype(np.float32) for i in range(3)]
    m=alpha>8

    red=((r>g*1.10)&(r>b*1.13)&(r>48)) | ((ur>ug*1.10)&(ur>ub*1.13)&(ur>55))
    gold=((r>70)&(g>45)&(r>g*1.03)&(g>b*1.18)) | ((ur>75)&(ug>48)&(ur>ug*1.03)&(ug>ub*1.18))
    red &= m
    gold &= m & ~red

    # Slightly broaden the real painted accents without inventing new armour geometry.
    red_im=Image.fromarray((red*255).astype(np.uint8),"L").filter(ImageFilter.MaxFilter(3))
    gold_im=Image.fromarray((gold*255).astype(np.uint8),"L").filter(ImageFilter.MaxFilter(3))
    red=np.array(red_im)>48
    gold=(np.array(gold_im)>48) & ~red & m
    return red, gold

def render(src: Path):
    suffix=src.name[len(SRC_PREFIX):]
    uni=APP/(UNI_PREFIX+suffix)
    if not uni.exists():
        uni=APP/(UNI_PREFIX+".png")

    field=Image.open(src).convert("RGBA")
    uniform=Image.open(uni).convert("RGBA").resize(field.size,Image.Resampling.LANCZOS)
    fa=np.array(field)
    ua=np.array(uniform)
    alpha=fa[...,3]

    t=norm_luma(fa,alpha)

    # Borrow fine textile/paint wear only, never silhouette, from the finished Human Form Uniform.
    ug=uniform.convert("L")
    blur=ug.filter(ImageFilter.GaussianBlur(2.2))
    detail=(np.asarray(ug,np.float32)-np.asarray(blur,np.float32))/255.0
    t=np.clip(t + detail*0.16,0,1)

    # Asuran/Human-form palette: warm white/ivory shell, crimson inset material, antique gold trim.
    base=shade_palette(t,(47,46,44),(164,160,149),(239,234,218))
    redc=shade_palette(t,(56,8,9),(143,24,24),(220,71,47))
    goldc=shade_palette(t,(70,45,14),(168,118,43),(235,198,103))

    red,gold=color_masks(fa,ua,alpha)

    # Preserve the source armour's internal plate definition as understated antique-gold edge work.
    lum=Image.fromarray((t*255).astype(np.uint8),"L")
    edges=lum.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.GaussianBlur(0.55))
    edge_arr=np.asarray(edges,np.float32)
    interior=np.asarray(Image.fromarray(alpha,"L").filter(ImageFilter.MinFilter(3)),np.uint8)>8
    edge_mask=(edge_arr>48)&interior&~red
    # Only use the strongest structural edges; keep white dominant.
    gold |= edge_mask & (t>0.24)

    out=base
    out=np.where(red[...,None],redc,out)
    out=np.where(gold[...,None],goldc,out)

    # Low-level dark jointing from the original shell adds depth without making the suit black.
    dark=(t<0.14)&(alpha>8)&~red&~gold
    joint=np.array((38,34,32),np.float32)
    out=np.where(dark[...,None],joint,out)

    # Warm directional finish for a painted, production-quality read at RimWorld scale.
    rgb=np.clip(out,0,255).astype(np.uint8)
    result=Image.fromarray(np.dstack([rgb,alpha]),"RGBA")
    result=ImageEnhance.Contrast(result).enhance(1.07)
    result.putalpha(Image.fromarray(alpha,"L"))
    return result

sources=sorted(APP.glob(SRC_PREFIX+"*.png"))
assert len(sources)==26, f"Expected 26 source field-armour textures, got {len(sources)}"

for src in sources:
    out=render(src)
    dst=APP/(OUT_PREFIX+src.name[len(SRC_PREFIX):])
    out.save(dst,optimize=True)
    print(dst.relative_to(ROOT),out.getbbox())
