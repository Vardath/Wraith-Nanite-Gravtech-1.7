from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageChops, ImageFilter, ImageDraw
import argparse, json, hashlib, io, math
import numpy as np
import cairosvg

ROOT=Path(__file__).resolve().parents[2]
PROFILE_ROOT=ROOT/"Tools/ApparelForge/profiles"
SOURCE_ROOT=ROOT/"ArtSource/Apparel"
LIVE_ROOT=ROOT/"Textures/Things/Pawn/Humanlike/Apparel"
OUT=192
HI=768

def svg_mask(path: Path) -> Image.Image:
    raw=cairosvg.svg2png(url=str(path),output_width=HI,output_height=HI)
    return Image.open(io.BytesIO(raw)).convert("RGBA").getchannel("A")

def bbox(mask: Image.Image):
    a=np.array(mask)>8
    ys,xs=np.nonzero(a)
    if not len(xs): raise RuntimeError("empty vanilla mask")
    return xs.min(),ys.min(),xs.max()+1,ys.max()+1

def body_alpha(body_dir: Path, body: str, direction: str) -> Image.Image:
    p=body_dir/f"Naked_{body}_{direction}.png"
    if not p.exists(): raise FileNotFoundError(p)
    return Image.open(p).convert("RGBA").resize((HI,HI),Image.Resampling.LANCZOS).getchannel("A")

def deform_mask(base: Image.Image, body_dir: Path, body: str, direction: str) -> Image.Image:
    male=body_alpha(body_dir,"Male",direction)
    targ=body_alpha(body_dir,body,direction)
    mb=bbox(male); tb=bbox(targ); ab=bbox(base)
    sx=(tb[2]-tb[0])/max(1,mb[2]-mb[0])
    sy=(tb[3]-tb[1])/max(1,mb[3]-mb[1])
    crop=base.crop(ab)
    crop=crop.resize((max(1,round(crop.width*sx)),max(1,round(crop.height*sy))),Image.Resampling.LANCZOS)
    mcx=(mb[0]+mb[2])/2; mcy=(mb[1]+mb[3])/2
    tcx=(tb[0]+tb[2])/2; tcy=(tb[1]+tb[3])/2
    acx=(ab[0]+ab[2])/2; acy=(ab[1]+ab[3])/2
    out=Image.new("L",(HI,HI),0)
    out.paste(crop,(round(acx+(tcx-mcx)-crop.width/2),round(acy+(tcy-mcy)-crop.height/2)))
    return out

def fit_master(master: Image.Image, target_mask: Image.Image, preserve_ratio: bool=False) -> Image.Image:
    """Content-preserving projection only.

    This function never invents or paints costume detail. It rescales a finished
    external master painting to the authoritative vanilla RimWorld contour and clips
    it. The illustration itself must already be finished before ApparelForge sees it.
    """
    src=master.convert("RGBA")
    sb=src.getchannel("A").getbbox()
    tb=target_mask.getbbox()
    if not sb or not tb: raise RuntimeError("empty source or target")
    crop=src.crop(sb)
    tw,th = tb[2]-tb[0], tb[3]-tb[1]
    if preserve_ratio:
        # Keep the finished master's proportions intact. Scale to cover the vanilla
        # silhouette, center it, then clip to the authoritative mask.
        scale=max(tw/max(1,crop.width),th/max(1,crop.height))
        nw=max(1,round(crop.width*scale)); nh=max(1,round(crop.height*scale))
        fitted=crop.resize((nw,nh),Image.Resampling.LANCZOS)
        x=tb[0]+(tw-nw)//2; y=tb[1]+(th-nh)//2
    else:
        fitted=crop.resize((tw,th),Image.Resampling.LANCZOS)
        x,y=tb[0],tb[1]
    out=Image.new("RGBA",(HI,HI),(0,0,0,0))
    out.alpha_composite(fitted,(x,y))
    out.putalpha(ImageChops.multiply(out.getchannel("A"),target_mask))
    return out


def fit_master_silhouette(master: Image.Image, target_mask: Image.Image, cfg: dict) -> Image.Image:
    """Remake the RimWorld tile around the finished master-art silhouette.

    Vanilla geometry is used only as the pawn-sized envelope/anchor. The finished
    master keeps its own alpha silhouette and is never clipped into a vanilla
    clothing mask. This is for garments whose art itself defines the live tile.
    """
    src=master.convert("RGBA")
    sb=src.getchannel("A").getbbox()
    tb=target_mask.getbbox()
    if not sb or not tb:
        raise RuntimeError("empty source or target")
    crop=src.crop(sb)
    tw,th=tb[2]-tb[0],tb[3]-tb[1]
    sx=float(cfg.get("width_scale",1.0))
    sy=float(cfg.get("height_scale",1.0))
    nw=max(1,round(tw*sx)); nh=max(1,round(th*sy))
    fitted=crop.resize((nw,nh),Image.Resampling.LANCZOS)

    ox=round(float(cfg.get("x_offset",0.0))*tw)
    oy=round(float(cfg.get("y_offset",0.0))*th)
    cx=(tb[0]+tb[2])//2 + ox
    cy=(tb[1]+tb[3])//2 + oy
    x=cx-nw//2; y=cy-nh//2

    out=Image.new("RGBA",(HI,HI),(0,0,0,0))
    out.alpha_composite(fitted,(x,y))
    return out

def validate_master(path: Path, direction: str, min_size: int):
    if not path.exists(): raise FileNotFoundError(path)
    im=Image.open(path).convert("RGBA")
    if min(im.size)<min_size:
        raise RuntimeError(f"{path}: master must be at least {min_size}px, got {im.size}")
    a=np.array(im.getchannel("A"))
    cov=(a>16).mean()
    if cov<.06 or cov>.75:
        raise RuntimeError(f"{path}: suspicious alpha coverage {cov:.3f}")
    if im.getchannel("A").getbbox() is None:
        raise RuntimeError(f"{path}: empty alpha")
    return im

def visual_qa(images: dict[str,Image.Image], cfg: dict):
    for name,im in images.items():
        a=np.array(im)
        m=a[...,3]>16
        if not m.any(): raise RuntimeError(f"QA empty {name}")
        rgb=a[...,:3].astype(np.float32)
        lum=.2126*rgb[...,0]+.7152*rgb[...,1]+.0722*rgb[...,2]
        vals=lum[m]
        if vals.std()<float(cfg.get("min_contrast",14)):
            raise RuntimeError(f"QA flat art {name}: contrast {vals.std():.1f}")
        if (vals>235).mean()>float(cfg.get("max_white_fraction",.015)):
            raise RuntimeError(f"QA clipped highlights {name}")
        # Reject source-like bright perimeter rings.
        dist=np.array(Image.fromarray((m*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0)))
        from scipy.ndimage import distance_transform_edt
        d=distance_transform_edt(m)
        edge=(d>0)&(d<=2); inner=d>6
        if edge.any() and inner.any():
            delta=float(lum[edge].mean()-lum[inner].mean())
            if delta>float(cfg.get("max_edge_lift",22)):
                raise RuntimeError(f"QA bright outline/ring {name}: {delta:.1f}")

def sheet(images: dict[str,Image.Image], item: str, path: Path):
    names=[
        f"{item}.png",
        f"{item}_Male_south.png",
        f"{item}_Male_north.png",
        f"{item}_Male_east.png",
        f"{item}_Female_south.png",
        f"{item}_Hulk_south.png",
    ]
    bg=Image.new("RGBA",(OUT*3,OUT*2),(24,24,24,255))
    for i,n in enumerate(names):
        bg.alpha_composite(images[n],((i%3)*OUT,(i//3)*OUT))
    path.parent.mkdir(parents=True,exist_ok=True)
    bg.save(path)

def validate_profile(p: dict):
    for k in ("id","item","faction","research","rimworld","masters","qa"):
        if k not in p: raise RuntimeError(f"missing profile key {k}")
    raw=json.dumps(p).lower()
    for forbidden in ("historical_wng_apparel","procedural_paint","component_contours","garment_blueprint"):
        if forbidden in raw:
            raise RuntimeError(f"forbidden source/generator mode: {forbidden}")
    if set(p["masters"].keys()) != {"south","north","east"}:
        raise RuntimeError("exactly south/north/east finished master paintings are required")
    return p

def run(profile: dict, vanilla_dir: Path, body_dir: Path, workdir: Path):
    family=profile["rimworld"]["family"]
    source_dir=SOURCE_ROOT/profile["id"]
    masters={}
    min_size=int(profile["masters"].get("_min_size",1024)) if "_min_size" in profile["masters"] else 1024
    for d in ("south","north","east"):
        masters[d]=validate_master(source_dir/profile["masters"][d],d,min_size)

    base_masks={}
    for d in ("south","north","east"):
        p=vanilla_dir/f"{family}_Male_{d}.svg"
        if not p.exists(): raise FileNotFoundError(p)
        base_masks[d]=svg_mask(p)

    masks={}
    cover_body=bool(profile["rimworld"].get("cover_body",False))
    for body in profile["rimworld"]["body_types"]:
        masks[body]={}
        for d in ("south","north","east"):
            m=deform_mask(base_masks[d],body_dir,body,d)
            # Some vanilla outerwear sprites deliberately leave the torso open.
            # For full-body garments, preserve the vanilla outer contour while
            # unioning the matching vanilla naked-body alpha so the apparel
            # actually covers the pawn instead of exposing a large center hole.
            if cover_body:
                m=ImageChops.lighter(m,body_alpha(body_dir,body,d))
            masks[body][d]=m

    outdir=LIVE_ROOT/profile.get("output_dir","Wraith")
    outdir.mkdir(parents=True,exist_ok=True)
    item=profile["item"]
    result={}
    tile_source_hi=None

    # Finished external master -> body/facing variants. No repainting occurs here.
    mapping_mode=profile["rimworld"].get("mapping_mode","vanilla_clip")
    mapping_cfg=profile["rimworld"].get("master_silhouette_mapping",{})
    for body in profile["rimworld"]["body_types"]:
        for d in ("south","north","east"):
            src=masters[d].resize((HI,HI),Image.Resampling.LANCZOS)
            if mapping_mode=="master_silhouette":
                hi=fit_master_silhouette(src,masks[body][d],mapping_cfg.get(d,{}))
            else:
                hi=fit_master(src,masks[body][d],bool(profile["rimworld"].get("preserve_master_ratio",False)))
            if body=="Male" and d=="south":
                tile_source_hi=hi.copy()
            im=hi.resize((OUT,OUT),Image.Resampling.LANCZOS)
            name=f"{item}_{body}_{d}.png"; im.save(outdir/name); result[name]=im
            if d=="east":
                west=im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                name=f"{item}_{body}_west.png"; west.save(outdir/name); result[name]=west
        alias=result[f"{item}_{body}_south.png"].copy()
        alias.save(outdir/f"{item}_{body}.png"); result[f"{item}_{body}.png"]=alias

    for d in ("south","north","east","west"):
        alias=result[f"{item}_Male_{d}.png"].copy()
        alias.save(outdir/f"{item}_{d}.png"); result[f"{item}_{d}.png"]=alias

    # Inventory/tile is built from the already-fitted Male/South worn art so
    # the world tile matches the actual garment shape seen on the pawn.
    # It is never used as a worn tile.
    if tile_source_hi is None:
        raise RuntimeError("missing fitted Male/South art for inventory")
    sb=tile_source_hi.getchannel("A").getbbox()
    if not sb: raise RuntimeError("empty fitted art for inventory")
    crop=tile_source_hi.crop(sb)
    scale=float(profile["rimworld"].get("tile_scale",.78))
    maxw=round(HI*scale); maxh=round(HI*scale)
    r=min(maxw/max(1,crop.width),maxh/max(1,crop.height))
    crop=crop.resize((max(1,round(crop.width*r)),max(1,round(crop.height*r))),Image.Resampling.LANCZOS)
    tile=Image.new("RGBA",(HI,HI),(0,0,0,0))
    tile.alpha_composite(crop,((HI-crop.width)//2,(HI-crop.height)//2))
    tile=tile.resize((OUT,OUT),Image.Resampling.LANCZOS)
    tile.save(outdir/f"{item}.png"); result[f"{item}.png"]=tile

    if len(result)!=30: raise RuntimeError(f"expected 30 files, got {len(result)}")
    visual_qa(result,profile["qa"])
    sheet(result,item,workdir/"contact-sheet.png")

    hashes={n:hashlib.sha256((outdir/n).read_bytes()).hexdigest() for n in sorted(result)}
    (workdir/"output-manifest.json").write_text(json.dumps({"item":item,"files":hashes},indent=2))
    print(json.dumps({"status":"ok","item":item,"files":len(result)},indent=2))

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("profile",nargs="?")
    ap.add_argument("--vanilla-dir",type=Path)
    ap.add_argument("--body-dir",type=Path)
    ap.add_argument("--workdir",type=Path,default=Path("/tmp/wng-apparel-forge"))
    ap.add_argument("--validate-profiles-only",action="store_true")
    args=ap.parse_args()

    if args.validate_profiles_only:
        checked=[]
        for f in sorted(PROFILE_ROOT.glob("*.json")):
            validate_profile(json.loads(f.read_text())); checked.append(f.name)
        print(json.dumps({"status":"ok","profiles":checked},indent=2)); return

    if not args.profile or args.vanilla_dir is None or args.body_dir is None:
        ap.error("profile, --vanilla-dir and --body-dir required")
    p=validate_profile(json.loads((PROFILE_ROOT/f"{args.profile}.json").read_text()))
    run(p,args.vanilla_dir,args.body_dir,args.workdir)

if __name__=="__main__":
    main()
