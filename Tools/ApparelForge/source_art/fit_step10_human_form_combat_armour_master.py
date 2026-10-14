from pathlib import Path
from PIL import Image, ImageFilter, ImageEnhance, ImageChops
import numpy as np

ROOT=Path(__file__).resolve().parents[3]
APP=ROOT/"Textures/Things/Pawn/Humanlike/Apparel/Precursor"
MASTER=ROOT/"Tools/ApparelForge/source_art/masters/step10_human_form_combat_armour_master.png"
PREFIX="WNG_HumanFormCombatArmor"

master=Image.open(MASTER).convert("RGBA")
# Exact professional concept views from the approved Step 10 master board.
views={
    "south": master.crop((10,80,245,585)).crop((6,92,229,448)),
    "north": master.crop((425,80,625,585)).crop((4,92,196,448)),
    "east":  master.crop((235,80,430,585)).crop((3,92,191,448)),
}
views["west"]=views["east"].transpose(Image.Transpose.FLIP_LEFT_RIGHT)

def source_subject_mask(src):
    a=np.asarray(src.convert("RGB"),np.int16)
    mx=a.max(axis=2)
    mn=a.min(axis=2)
    # Professional source is white/red/gold on a near-black board.
    raw=((mx>46) | ((a[...,0]>42)&(a[...,0]>a[...,1]*1.12))).astype(np.uint8)*255
    m=Image.fromarray(raw,"L")
    m=m.filter(ImageFilter.MaxFilter(11)).filter(ImageFilter.MinFilter(7))
    m=m.filter(ImageFilter.GaussianBlur(1.25))
    return m

src_masks={k:source_subject_mask(v) for k,v in views.items()}

def target_direction(name):
    n=name.lower()
    if "_north" in n: return "north"
    if "_east" in n: return "east"
    if "_west" in n: return "west"
    return "south"

def ivory_base(mask):
    W,H=mask.size
    a=np.asarray(mask,np.float32)/255
    yy=np.linspace(0,1,H,dtype=np.float32)[:,None]
    shade=222 - 34*yy
    rgb=np.zeros((H,W,3),np.float32)
    rgb[...,0]=shade+8
    rgb[...,1]=shade+5
    rgb[...,2]=shade-3
    # slight edge darkening for RimWorld readability, not a recolour pass
    inner=np.asarray(mask.filter(ImageFilter.MinFilter(5)),np.float32)/255
    edge=np.clip(a-inner,0,1)
    rgb-=edge[...,None]*42
    out=np.dstack([np.clip(rgb,0,255).astype(np.uint8),np.asarray(mask,np.uint8)])
    return Image.fromarray(out,"RGBA")

def fit_one(path):
    target=Image.open(path).convert("RGBA")
    mask=target.getchannel("A")
    box=mask.getbbox()
    if not box: return target
    x0,y0,x1,y1=box
    bw,bh=x1-x0,y1-y0
    direction=target_direction(path.name)
    src=views[direction]
    sm=src_masks[direction]

    # Fit the finished art to the actual RimWorld apparel coverage.
    sw,sh=src.size
    scale=max(bw/sw,bh/sh)
    nw,nh=max(1,round(sw*scale)),max(1,round(sh*scale))
    art=src.resize((nw,nh),Image.Resampling.LANCZOS)
    am=sm.resize((nw,nh),Image.Resampling.LANCZOS)
    left=(nw-bw)//2
    top=(nh-bh)//2
    art=art.crop((left,top,left+bw,top+bh))
    am=am.crop((left,top,left+bw,top+bh))

    # Base is only for tiny gaps where the painted master has negative space.
    out=ivory_base(mask)
    layer=Image.new("RGBA",target.size,(0,0,0,0))
    layer.paste(art,(x0,y0),am)
    # Clip strictly to the RimWorld apparel mask.
    la=ImageChops.multiply(layer.getchannel("A"),mask)
    layer.putalpha(la)
    out.alpha_composite(layer)
    out=ImageEnhance.Contrast(out).enhance(1.05)
    out.putalpha(mask)
    return out

# Fit each body/direction from the finished master.
bodies=["Male","Female","Thin","Fat","Hulk"]
for body in bodies:
    for direction in ["south","north","east","west"]:
        p=APP/f"{PREFIX}_{body}_{direction}.png"
        fit_one(p).save(p,optimize=True)

# Body-base sprites follow the fitted south view.
for body in bodies:
    src=Image.open(APP/f"{PREFIX}_{body}_south.png").convert("RGBA")
    src.save(APP/f"{PREFIX}_{body}.png",optimize=True)

# Inventory/tile comes from the fitted male south artwork, not a separate generated tile.
Image.open(APP/f"{PREFIX}_Male_south.png").convert("RGBA").save(APP/f"{PREFIX}.png",optimize=True)

print("Step 10 master fit complete")
