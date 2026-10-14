from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageChops
from bs4 import BeautifulSoup
import argparse, hashlib, io, json, math, random, re, subprocess
import numpy as np
import requests
import cairosvg
from scipy.ndimage import gaussian_filter, distance_transform_edt

ROOT = Path(__file__).resolve().parents[2]
APPAREL_ROOT = ROOT / "Textures/Things/Pawn/Humanlike/Apparel"
BUILDING_ROOT = ROOT / "Textures/Things/Building"
PROFILE_ROOT = ROOT / "Tools/ArtGen/profiles"
OUT = 192
HI = 768
HIST_REF = "22ab5e03335aad03731814541361ab29d5124319"
UA = "WNG-ArtGen/2.0 (+reference research for local mod art)"

@dataclass
class RefReport:
    verified: int
    total: int
    pages: list[dict]
    image_stats: list[dict]

class StargateReferencePass:
    def __init__(self, profile: dict, workdir: Path):
        self.profile = profile
        self.cfg = profile["stage_1_stargate"]
        self.workdir = workdir
        self.session = requests.Session()
        self.session.headers["User-Agent"] = UA

    def _text(self, html: str) -> str:
        soup = BeautifulSoup(html, "html.parser")
        for tag in soup(["script","style","noscript"]):
            tag.extract()
        return " ".join(soup.stripped_strings)

    def _candidate_images(self, html: str, base_url: str) -> list[str]:
        soup = BeautifulSoup(html, "html.parser")
        urls = []
        for prop in ("og:image","twitter:image"):
            node = soup.find("meta", attrs={"property":prop}) or soup.find("meta", attrs={"name":prop})
            if node and node.get("content"):
                urls.append(requests.compat.urljoin(base_url,node["content"]))
        for img in soup.find_all("img"):
            words = " ".join([img.get("alt",""),img.get("title",""),img.get("class",[None])[0] or ""]).lower()
            src = img.get("src") or img.get("data-src")
            if src and any(k in words for k in ("wraith","costume","queen","armor","armour","hive","stargate")):
                urls.append(requests.compat.urljoin(base_url,src))
        dedup=[]
        for u in urls:
            if u not in dedup: dedup.append(u)
        return dedup[:4]

    def _download_image_stats(self, url: str) -> dict|None:
        try:
            r=self.session.get(url,timeout=18)
            r.raise_for_status()
            if len(r.content)>8_000_000: return None
            im=Image.open(io.BytesIO(r.content)).convert("RGB")
            if im.width < 100 or im.height < 100:
                return None
            bad=url.lower()
            if any(k in bad for k in ("gravatar","banner","poster","button","logo","avatar")):
                return None
            im.thumbnail((512,512),Image.Resampling.LANCZOS)
            a=np.array(im,dtype=np.float32)
            lum=.2126*a[...,0]+.7152*a[...,1]+.0722*a[...,2]
            sat=a.max(2)-a.min(2)
            return {
                "url":url,
                "sha256":hashlib.sha256(r.content).hexdigest(),
                "mean_luminance":float(lum.mean()),
                "contrast":float(lum.std()),
                "mean_saturation":float(sat.mean()),
                "size":[im.width,im.height]
            }
        except Exception:
            return None

    def run(self) -> RefReport:
        pages=[]; image_stats=[]; verified=0
        for src in self.cfg["sources"]:
            row={"url":src["url"],"role":src["role"],"ok":False,"terms":{}}
            try:
                r=self.session.get(src["url"],timeout=20)
                row["http_status"]=r.status_code
                row["sha256"]=hashlib.sha256(r.content).hexdigest()
                r.raise_for_status()
                text=self._text(r.text)
                low=text.lower()
                for term in src.get("required_terms",[]):
                    row["terms"][term]=term.lower() in low
                # A source is considered live/usable when it returned substantial Stargate
                # content and at least one expected cue is present. Exact phrase sets are
                # intentionally not brittle gates because interviews are mirrored/reformatted.
                row["ok"]=len(text) > 500 and (any(row["terms"].values()) if row["terms"] else True)
                if row["ok"]: verified+=1
                if src.get("image_policy")!="metadata_only":
                    for u in self._candidate_images(r.text,src["url"]):
                        st=self._download_image_stats(u)
                        if st: image_stats.append(st)
            except Exception as e:
                row["error"]=str(e)[:300]
            pages.append(row)

        minimum=int(self.cfg.get("minimum_live_sources",max(3,math.ceil(len(self.cfg["sources"])*0.5))))
        min_visual=int(self.cfg.get("minimum_visual_references",0))
        if verified < minimum:
            raise RuntimeError(f"StargateReferencePass failed: {verified}/{len(self.cfg['sources'])} live sources; need {minimum}")
        if len(image_stats) < min_visual:
            raise RuntimeError(f"StargateReferencePass failed: {len(image_stats)} live visual references; need {min_visual}")
        rep=RefReport(verified,len(self.cfg["sources"]),pages,image_stats)
        self.workdir.mkdir(parents=True,exist_ok=True)
        (self.workdir/"stargate_reference_report.json").write_text(json.dumps(rep.__dict__,indent=2))

        # Carry the costume research into synthesis as an explicit design brief.
        # ArtGen is not allowed to collapse faction identity into a colour palette.
        design=self.cfg.get("design_rules",{})
        brief={
            "faction":self.profile.get("faction"),
            "item":self.profile.get("item"),
            "verified_sources":verified,
            "source_roles":[p["role"] for p in pages if p.get("ok")],
            "materials":design.get("material_priority",[]),
            "construction":design.get("construction",[]),
            "motifs":design.get("motifs",[]),
            "avoid":design.get("avoid",[]),
            "visual_reference_count":len(image_stats)
        }
        (self.workdir/"stargate_design_brief.json").write_text(json.dumps(brief,indent=2))
        return rep

class RimWorldImplementationPass:
    def __init__(self, profile: dict, vanilla_dir: Path, body_dir: Path|None=None):
        self.profile=profile
        self.cfg=profile["stage_2_rimworld"]
        self.vanilla_dir=vanilla_dir
        self.body_dir=body_dir

    def _visible_mask(self, path: Path) -> Image.Image:
        im=Image.open(path).convert("RGBA").resize((HI,HI),Image.Resampling.LANCZOS)
        a=np.array(im.getchannel("A"))
        rgb=np.array(im)[...,:3]
        m=((a>8)&(rgb.max(axis=2)>28)).astype(np.uint8)*255
        return Image.fromarray(m,"L").filter(ImageFilter.GaussianBlur(.45))

    def _svg_mask(self, path: Path) -> Image.Image:
        raw=cairosvg.svg2png(url=str(path),output_width=HI,output_height=HI)
        im=Image.open(io.BytesIO(raw)).convert("RGBA")
        a=np.array(im.getchannel("A"),dtype=np.uint8)
        return Image.fromarray(a,"L")

    def _body_alpha(self, body: str, direction: str) -> Image.Image:
        if self.body_dir is None:
            raise RuntimeError("body_dir is required for powerarmor_svg_deformed")
        p=self.body_dir/f"Naked_{body}_{direction}.png"
        if not p.exists():
            raise FileNotFoundError(p)
        im=Image.open(p).convert("RGBA").resize((HI,HI),Image.Resampling.LANCZOS)
        return im.getchannel("A")

    @staticmethod
    def _bbox_arr(mask: Image.Image):
        a=np.array(mask)>8
        ys,xs=np.nonzero(a)
        if not len(xs): raise RuntimeError("empty implementation mask")
        return xs.min(),ys.min(),xs.max()+1,ys.max()+1

    def _deform_powerarmor(self, base: Image.Image, body: str, direction: str) -> Image.Image:
        male=self._body_alpha("Male",direction)
        targ=self._body_alpha(body,direction)
        mb=self._bbox_arr(male); tb=self._bbox_arr(targ); ab=self._bbox_arr(base)
        mw,mh=mb[2]-mb[0],mb[3]-mb[1]
        tw,th=tb[2]-tb[0],tb[3]-tb[1]
        sx=tw/max(1,mw); sy=th/max(1,mh)
        acx=(ab[0]+ab[2])/2; acy=(ab[1]+ab[3])/2
        mcx=(mb[0]+mb[2])/2; mcy=(mb[1]+mb[3])/2
        tcx=(tb[0]+tb[2])/2; tcy=(tb[1]+tb[3])/2

        crop=base.crop(ab)
        nw=max(1,int(round(crop.width*sx))); nh=max(1,int(round(crop.height*sy)))
        crop=crop.resize((nw,nh),Image.Resampling.LANCZOS)
        out=Image.new("L",(HI,HI),0)
        px=int(round(acx+(tcx-mcx)-nw/2)); py=int(round(acy+(tcy-mcy)-nh/2))
        out.paste(crop,(px,py))

        # keep vanilla contour safely within canvas without distorting it
        b=out.getbbox()
        if b:
            pad=16
            dx=0; dy=0
            if b[0]<pad: dx=pad-b[0]
            elif b[2]>HI-pad: dx=(HI-pad)-b[2]
            if b[1]<pad: dy=pad-b[1]
            elif b[3]>HI-pad: dy=(HI-pad)-b[3]
            if dx or dy:
                shifted=Image.new("L",(HI,HI),0)
                shifted.paste(out,(dx,dy))
                out=shifted
        return out

    def _powerarmor_run(self) -> dict:
        masks={}; metrics={}
        bases={}
        for d in ("south","north","east"):
            p=self.vanilla_dir/f"PowerArmor_Male_{d}.svg"
            if not p.exists(): raise FileNotFoundError(p)
            bases[d]=self._svg_mask(p)

        for body in self.cfg["body_types"]:
            masks[body]={}
            for d in ("south","north","east"):
                m=self._deform_powerarmor(bases[d],body,d)
                if bool(self.cfg.get("cover_body",False)):
                    m=ImageChops.lighter(m,self._body_alpha(body,d))
                masks[body][d]=m
                b=m.getbbox()
                metrics[f"{body}_{d}"]={"bbox":list(b),"opaque":int((np.array(m)>128).sum())}

        # Ground tile derives from the vanilla PowerArmor front contour, centered smaller.
        src=masks["Male"]["south"]
        sb=src.getbbox()
        crop=src.crop(sb)
        tw=int((sb[2]-sb[0])*.78); th=int((sb[3]-sb[1])*.78)
        crop=crop.resize((tw,th),Image.Resampling.LANCZOS)
        tile=Image.new("L",(HI,HI),0)
        tile.paste(crop,((HI-tw)//2,(HI-th)//2))
        return {"masks":masks,"tile":tile,"metrics":metrics}

    def _svg_family_run(self, fam: str) -> dict:
        """Use a vanilla male SVG contour as the authoritative apparel family,
        then deform that contour to the actual vanilla pawn body variants.

        Some vanilla apparel families (notably Duster) are distributed in the
        reference repository as SVG source rather than per-body PNG exports.
        This keeps RimWorld geometry authoritative instead of inventing a fallback.
        """
        masks={}; metrics={}; bases={}
        for d in ("south","north","east"):
            p=self.vanilla_dir/f"{fam}_Male_{d}.svg"
            if not p.exists():
                raise FileNotFoundError(p)
            bases[d]=self._svg_mask(p)

        for body in self.cfg["body_types"]:
            masks[body]={}
            for d in ("south","north","east"):
                m=self._deform_powerarmor(bases[d],body,d)
                masks[body][d]=m
                b=m.getbbox()
                metrics[f"{body}_{d}"]={"bbox":list(b),"opaque":int((np.array(m)>128).sum())}

        src=masks["Male"]["south"]
        sb=src.getbbox()
        crop=src.crop(sb)
        scale=float(self.cfg.get("tile_scale",.78))
        tw=max(1,int((sb[2]-sb[0])*scale)); th=max(1,int((sb[3]-sb[1])*scale))
        crop=crop.resize((tw,th),Image.Resampling.LANCZOS)
        tile=Image.new("L",(HI,HI),0)
        tile.paste(crop,((HI-tw)//2,(HI-th)//2))
        return {"masks":masks,"tile":tile,"metrics":metrics}

    def run(self) -> dict:
        mode=self.cfg.get("implementation_mode","png_family")
        if mode=="powerarmor_svg_deformed":
            return self._powerarmor_run()
        if mode=="svg_family_deformed":
            return self._svg_family_run(self.cfg["vanilla_family"])

        fam=self.cfg["vanilla_family"]
        masks={}
        metrics={}
        for body in self.cfg["body_types"]:
            masks[body]={}
            for d in ("south","north","east"):
                p=self.vanilla_dir/f"{fam}_{body}_{d}.png"
                if not p.exists(): raise FileNotFoundError(p)
                m=self._visible_mask(p)
                if not m.getbbox(): raise RuntimeError(f"empty vanilla mask {p}")
                masks[body][d]=m
                b=m.getbbox()
                metrics[f"{body}_{d}"]={"bbox":list(b),"opaque":int((np.array(m)>128).sum())}
        tile=self._visible_mask(self.vanilla_dir/f"{self.cfg['tile_family']}.png")
        return {"masks":masks,"tile":tile,"metrics":metrics}

class SynthesisPass:
    def __init__(self, profile: dict, ref: RefReport, impl: dict):
        self.profile=profile
        self.s1=profile["stage_1_stargate"]
        self.s3=profile["stage_3_synthesis"]
        self.ref=ref
        self.impl=impl
        self.palette=self.s3["palette"]
        self.rng=np.random.default_rng(int(hashlib.sha256(profile["id"].encode()).hexdigest()[:8],16))
        self.benchmark_stats=self._benchmark_stats()
        self.garment_metrics=[]

    def _benchmark_stats(self):
        stats=[]
        for rel in self.s3["wNG_quality_benchmarks"]:
            p=ROOT/rel
            im=Image.open(p).convert("RGBA")
            a=np.array(im)
            m=a[...,3]>16
            if not m.any(): continue
            rgb=a[...,:3].astype(np.float32)
            lum=.2126*rgb[...,0]+.7152*rgb[...,1]+.0722*rgb[...,2]
            stats.append({"contrast":float(lum[m].std()),"luminance":float(lum[m].mean())})
        return stats

    def _historical(self, item: str, body: str, direction: str, bb) -> Image.Image:
        raise RuntimeError(
            "Historical WNG apparel input is forbidden. Rebuild the profile from "
            "Stargate references + vanilla RimWorld geometry + non-apparel WNG quality benchmarks."
        )

    def _noise(self, sigma: float, shape=(HI,HI)):
        n=self.rng.normal(0,1,shape)
        n=gaussian_filter(n,sigma)
        return (n-n.mean())/(n.std()+1e-6)

    def _material(self, mask: Image.Image, shadow, mid, high, kind: str, hist: Image.Image|None=None):
        """
        Painted material model for RimWorld scale.
        Broad garment folds define form; grain is subtle. The leather response is
        smooth and polished, while reptile scales are added separately.
        """
        m=np.array(mask,dtype=np.float32)/255.0
        yy,xx=np.mgrid[0:HI,0:HI]

        light=((1-yy/HI)*0.67 + (1-xx/HI)*0.33)
        light=(light-light.min())/(light.max()-light.min()+1e-6)

        fine=self._noise(1.25)
        medium=self._noise(9.0)
        broad_noise=self._noise(36.0)
        grain=.18*fine + .34*medium + .08*broad_noise

        if hist is not None:
            raise RuntimeError("Historical apparel relief is forbidden in the WNG ArtGen")
        fold_shape=np.zeros((HI,HI),dtype=np.float32)
        crease=np.zeros((HI,HI),dtype=np.float32)

        dist=distance_transform_edt(m>.1)
        edge=np.clip(1-dist/13,0,1)

        # Historical painted folds provide most of the value structure.
        lum=.235 + .255*light + grain/480.0 + .285*fold_shape + crease/335.0 - .09*edge
        lum=np.clip(lum,0,1)

        lo=np.array(shadow,float); mi=np.array(mid,float); hi=np.array(high,float)
        rgb=np.empty((HI,HI,3),dtype=np.float32)
        lower=lum<.5
        t=np.clip(lum*2,0,1)
        rgb[lower]=lo+(mi-lo)*t[lower,None]
        t2=np.clip((lum-.5)*2,0,1)
        rgb[~lower]=mi+(hi-mi)*t2[~lower,None]

        # Narrow polished-leather catchlights sit on raised folds only.
        raised=np.clip(fold_shape+.18,0,1)
        spec=(np.clip((lum-.41)/.34,0,1)**2.15) * (.34+.66*raised)
        sheen=np.array([46,50,46],dtype=np.float32) if kind=="leather" else np.array([31,30,34],dtype=np.float32)
        rgb=np.clip(rgb+spec[...,None]*sheen,0,255)

        out=np.dstack([rgb.astype(np.uint8),(m*255).astype(np.uint8)])
        return Image.fromarray(out,"RGBA")

    def _poly_mask(self, bb, pts, feather=5):
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        q=[(int(x0+x*w),int(y0+y*h)) for x,y in pts]
        m=Image.new("L",(HI,HI),0)
        ImageDraw.Draw(m).polygon(q,fill=255)
        if feather: m=m.filter(ImageFilter.GaussianBlur(feather))
        return m

    def _organic_poly_mask(self, bb, pts, feather=0.65):
        """Smooth an authored plate outline without changing its construction zone."""
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        q=[(x0+x*w,y0+y*h) for x,y in pts]
        for _ in range(3):
            nxt=[]
            for i,p0 in enumerate(q):
                p1=q[(i+1)%len(q)]
                nxt.append((.75*p0[0]+.25*p1[0],.75*p0[1]+.25*p1[1]))
                nxt.append((.25*p0[0]+.75*p1[0],.25*p0[1]+.75*p1[1]))
            q=nxt
        m=Image.new("L",(HI,HI),0)
        ImageDraw.Draw(m).polygon([(int(x),int(y)) for x,y in q],fill=255)
        if feather:
            m=m.filter(ImageFilter.GaussianBlur(feather))
        return m

    def _smooth_path_points(self, bb, pts, passes=3):
        """Convert normalized garment-pattern points to a smooth authored curve."""
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        q=[(x0+float(x)*w,y0+float(y)*h) for x,y in pts]
        if len(q)<3:
            return q
        for _ in range(max(0,int(passes))):
            nxt=[]
            for i,p0 in enumerate(q):
                p1=q[(i+1)%len(q)]
                nxt.append((.75*p0[0]+.25*p1[0],.75*p0[1]+.25*p1[1]))
                nxt.append((.25*p0[0]+.75*p1[0],.25*p0[1]+.75*p1[1]))
            q=nxt
        return q

    def _garment_mask(self, bb, pts, outer_mask, feather=.65):
        """Semantic garment-piece mask. Curves are smoothed; hard polygons are forbidden."""
        q=self._smooth_path_points(bb,pts,3)
        m=Image.new("L",(HI,HI),0)
        ImageDraw.Draw(m).polygon([(int(x),int(y)) for x,y in q],fill=255)
        if feather:
            m=m.filter(ImageFilter.GaussianBlur(float(feather)))
        return ImageChops.multiply(m,outer_mask)

    def _material_spec(self, name):
        mats=self.s3.get("materials",{})
        if name not in mats:
            raise RuntimeError(f"Costume material '{name}' is not defined in stage_3_synthesis.materials")
        spec=mats[name]
        for k in ("shadow","mid","high","kind"):
            if k not in spec:
                raise RuntimeError(f"Costume material '{name}' missing '{k}'")
        return spec

    def _garment_fold_field(self, bb, guides):
        """Paint authored drape/folds from garment-pattern guides, never cloud-noise folds."""
        field=np.zeros((HI,HI),dtype=np.float32)
        if not guides:
            return field
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        for g in guides:
            pts=[(int(x0+float(x)*w),int(y0+float(y)*h)) for x,y in g.get("points",[])]
            if len(pts)<2:
                continue
            width=max(2.0,float(g.get("width",.018))*min(w,h))
            strength=float(g.get("strength",1.0))
            line=Image.new("L",(HI,HI),0)
            ImageDraw.Draw(line).line(pts,fill=255,width=max(1,int(width*.45)),joint="curve")
            a=np.array(line,dtype=np.float32)/255.0
            # Dark crease with a broader raised shoulder: cloth/leather drape, not panels.
            crease=gaussian_filter(a,max(.8,width*.24))
            shoulder=gaussian_filter(a,max(1.6,width*1.20))
            field += strength*(shoulder*.72-crease*1.10)
        return np.clip(field,-1.5,1.5)

    def _authored_height_field(self, bb, fold_guides=None, relief_guides=None):
        """Build a sculpting field only from authored costume guides.

        This is the form layer of Raster Painter v3. Randomness is never allowed to
        define garment construction; only the profile's folds/relief paths do that.
        """
        field=self._garment_fold_field(bb,fold_guides or []).astype(np.float32)
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        for g in relief_guides or []:
            pts=[(int(x0+float(x)*w),int(y0+float(y)*h)) for x,y in g.get("points",[])]
            if len(pts)<2:
                continue
            width=max(2.0,float(g.get("width",.012))*min(w,h))
            strength=float(g.get("height",g.get("strength",.55)))
            line=Image.new("L",(HI,HI),0)
            ImageDraw.Draw(line).line(pts,fill=255,width=max(1,int(width*.42)),joint="curve")
            a=np.array(line,dtype=np.float32)/255.0
            core=gaussian_filter(a,max(.8,width*.22))
            shoulder=gaussian_filter(a,max(1.4,width*.95))
            field += strength*(shoulder*.95-core*.48)
        return np.clip(field,-2.0,2.0)

    def _brush_scumble(self, mask, spec, seed_offset, density=1.0):
        """Low-opacity irregular brushwork used as hand-painted material breakup.

        Dabs are sparse, directional and material-aware. They never create panel
        boundaries and remain subordinate to the authored form/lighting.
        """
        bb=mask.getbbox()
        if not bb:
            return Image.new("RGBA",(HI,HI),(0,0,0,0))
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        kind=str(spec["kind"]).lower()
        rng=random.Random(61001+int(seed_offset)*131+x0*7+y0*11)
        layer=Image.new("RGBA",(HI,HI),(0,0,0,0))
        d=ImageDraw.Draw(layer)
        shadow=np.array(spec["shadow"],dtype=int)
        high=np.array(spec["high"],dtype=int)

        base=max(9,int((w*h)/(HI*HI)*95*density))
        if kind in ("leather","reptile_leather"):
            count=base
        elif kind in ("hard_rubber","grown_rubber","simulated_bone","bone_chitin"):
            count=max(7,int(base*.72))
        else:
            count=max(6,int(base*.55))

        for i in range(count):
            cx=rng.randint(x0,max(x0,x1-1)); cy=rng.randint(y0,max(y0,y1-1))
            rw=max(3,int(w*rng.uniform(.025,.085)))
            rh=max(2,int(h*rng.uniform(.008,.035)))
            if kind in ("hard_rubber","grown_rubber","simulated_bone","bone_chitin"):
                rh=max(2,int(rw*rng.uniform(.12,.32)))
            dark=tuple(np.clip(shadow*rng.uniform(.72,.98),0,255).astype(int))+(rng.randint(16,38),)
            light=tuple(np.clip(high*rng.uniform(.82,1.04),0,255).astype(int))+(rng.randint(10,30),)
            d.ellipse((cx-rw,cy-rh,cx+rw,cy+rh),fill=dark)
            if i%3==0:
                d.arc((cx-rw,cy-rh,cx+rw,cy+rh),205,335,fill=light,width=max(1,int(rh*.28)))

        layer=layer.filter(ImageFilter.GaussianBlur(.45))
        return self._clip(layer,mask)

    def _material_microdetail(self, mask, material_name, seed_offset):
        """Material-specific hand-painted microdetail for final RimWorld scale."""
        spec=self._material_spec(material_name)
        kind=str(spec["kind"]).lower()
        bb=mask.getbbox()
        if not bb:
            return Image.new("RGBA",(HI,HI),(0,0,0,0))
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        rng=random.Random(72017+int(seed_offset)*173+x0+y0*3)
        layer=Image.new("RGBA",(HI,HI),(0,0,0,0))
        d=ImageDraw.Draw(layer)

        shadow=tuple(spec["shadow"]); high=tuple(spec["high"])
        if kind=="reptile_leather":
            # Irregular overlapping pebbled scales, not a tiled grid.
            count=max(18,int((w*h)/(HI*HI)*150))
            for _ in range(count):
                cx=rng.randint(x0,max(x0,x1-1)); cy=rng.randint(y0,max(y0,y1-1))
                rx=max(6,int(w*rng.uniform(.028,.060)))
                ry=max(4,int(rx*rng.uniform(.48,.80)))
                d.arc((cx-rx,cy-ry,cx+rx,cy+ry),185,355,
                      fill=tuple(min(255,v+18) for v in shadow)+(rng.randint(75,125),),
                      width=max(1,int(rx*.16)))
                d.arc((cx-rx+2,cy-ry+2,cx+rx-2,cy+ry-2),10,165,
                      fill=tuple(min(255,v+10) for v in high)+(rng.randint(25,52),),width=1)
        elif kind=="leather":
            # Pores, rubbed creases, and occasional stitch-like scars.
            count=max(16,int((w*h)/(HI*HI)*155))
            for _ in range(count):
                cx=rng.randint(x0,max(x0,x1-1)); cy=rng.randint(y0,max(y0,y1-1))
                if rng.random()<.68:
                    r=rng.choice([1,1,2,2,3])
                    d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=shadow+(rng.randint(28,62),))
                else:
                    ln=max(5,int(w*rng.uniform(.025,.07)))
                    d.line((cx,cy,min(x1-1,cx+ln),cy+rng.randint(-3,3)),
                           fill=high+(rng.randint(25,55),),width=1)
        elif kind in ("hard_rubber","grown_rubber"):
            # Moulded organic striation plus scuffed production edges.
            for i in range(6):
                fx=(i+1)/6
                pts=[]
                phase=rng.uniform(-.6,.6)
                for j in range(13):
                    t=j/12
                    xx=x0+w*(fx+math.sin(t*math.pi*1.35+phase)*.020)
                    yy=y0+h*(.10+.80*t)
                    pts.append((int(xx),int(yy)))
                d.line(pts,fill=shadow+(96,),width=max(2,int(w*.0075)),joint="curve")
                d.line([(x-1,y-2) for x,y in pts],fill=high+(48,),width=max(1,int(w*.0025)),joint="curve")
            for _ in range(max(4,int(w*h/(HI*HI)*35))):
                sx=rng.randint(x0,max(x0,x1-1)); sy=rng.randint(y0,max(y0,y1-1))
                ln=max(6,int(w*rng.uniform(.04,.10)))
                d.line((sx,sy,min(x1-1,sx+ln),sy+rng.randint(-2,3)),fill=high+(rng.randint(18,40),),width=1)
        elif kind in ("simulated_bone","bone_chitin"):
            # Fibrous cast/grown striation.
            for i in range(8):
                fx=(i+1)/8
                pts=[]
                for j in range(11):
                    t=j/10
                    xx=x0+w*(fx+math.sin(t*math.pi*1.5+i*.7)*.016)
                    yy=y0+h*(.08+.84*t)
                    pts.append((int(xx),int(yy)))
                d.line(pts,fill=shadow+(82,),width=max(2,int(w*.0065)),joint="curve")
                d.line([(x-1,y-1) for x,y in pts],fill=high+(44,),width=1,joint="curve")

        return self._clip(layer,mask)

    def _contact_shadow(self, outer_mask, piece_mask, strength=70, radius=7, offset=(2,3)):
        """Paint overlap depth before a semantic piece is composited."""
        a=np.array(piece_mask,dtype=np.uint8)
        sh=Image.fromarray(a,"L").filter(ImageFilter.GaussianBlur(radius))
        shifted=Image.new("L",(HI,HI),0)
        shifted.paste(sh,offset)
        shifted=ImageChops.multiply(shifted,outer_mask)
        col=Image.new("RGBA",(HI,HI),(0,0,0,0))
        col.putalpha(shifted.point(lambda v:int(v*strength/255)))
        return col

    def _garment_material(self, mask, material_name, hist=None, fold_guides=None, seed_offset=0, relief_guides=None):
        """Raster Painter v3 material pass.

        Semantic masks only bound the paint. Visible form comes from sculpted height,
        authored folds/relief, directional lighting, contact depth and hand-painted
        material breakup. Historical WNG apparel is never sampled.
        """
        if hist is not None:
            raise RuntimeError("Historical apparel input is forbidden in Raster Painter v3")
        spec=self._material_spec(material_name)
        kind=str(spec["kind"]).lower()
        lo=np.array(spec["shadow"],dtype=np.float32)
        mi=np.array(spec["mid"],dtype=np.float32)
        hi=np.array(spec["high"],dtype=np.float32)
        rough=float(spec.get("roughness",.55))
        m=np.array(mask,dtype=np.float32)/255.0
        inside=m>.07
        if not inside.any():
            return Image.new("RGBA",(HI,HI),(0,0,0,0))

        bb=mask.getbbox(); x0,y0,x1,y1=bb; w=max(1,x1-x0); h=max(1,y1-y0)
        yy,xx=np.mgrid[0:HI,0:HI]
        lx=(xx-x0)/w; ly=(yy-y0)/h

        # Sculpted height: convex material body + authored folds/ridges.
        dist=distance_transform_edt(inside).astype(np.float32)
        dmax=max(1.0,float(np.percentile(dist[inside],96)))
        convex=np.sqrt(np.clip(dist/dmax,0,1))
        authored=self._authored_height_field(bb,fold_guides,relief_guides)

        # Material body curvature. Hard surfaces bulge more; cloth/leather remain softer.
        body_amp=.50
        if kind in ("hard_rubber","grown_rubber"): body_amp=.78
        elif kind in ("simulated_bone","bone_chitin"): body_amp=.88
        elif kind in ("leather","reptile_leather"): body_amp=.58
        elif kind in ("cloth","woven","uniform_fabric","wool","velvet"): body_amp=.38

        height=convex*body_amp + authored*.30

        # Gentle whole-piece form prevents flat cut-out appearance.
        form=(1-((lx-.43)/.78)**2)*.08 + (1-((ly-.38)/.95)**2)*.05
        height += form.astype(np.float32)

        gy,gx=np.gradient(height)
        normal_strength=float(spec.get("normal_strength",2.1 if kind in ("hard_rubber","grown_rubber","simulated_bone","bone_chitin") else 1.35))
        nx=-gx*normal_strength; ny=-gy*normal_strength; nz=np.ones_like(nx)
        nlen=np.sqrt(nx*nx+ny*ny+nz*nz)+1e-6
        nx/=nlen; ny/=nlen; nz/=nlen

        # Painterly upper-left/key + soft frontal fill.
        L=np.array([-0.46,-0.64,0.61],dtype=np.float32); L/=np.linalg.norm(L)
        diffuse=np.clip(nx*L[0]+ny*L[1]+nz*L[2],0,1)
        broad=.36+.48*diffuse+.10*(1-ly)+.06*(1-lx)

        # Edge recess/contact occlusion is material-dependent.
        edge=np.clip(1-dist/(8 if kind in ("cloth","woven","velvet") else 12),0,1)
        lum=np.clip(broad-edge*(.12 if kind in ("hard_rubber","simulated_bone") else .085),0,1)

        # Hand-painted scumble influences tone only slightly; it cannot define form.
        rng=np.random.default_rng(47017+int(seed_offset)*37)
        n=rng.normal(0,1,(HI,HI))
        low=gaussian_filter(n,18.0)
        low=(low-low.mean())/(low.std()+1e-6)
        lum=np.clip(lum+low*.012,0,1)

        rgb=np.empty((HI,HI,3),dtype=np.float32)
        lower=lum<.50
        t=np.clip(lum/.50,0,1)
        rgb[lower]=lo+(mi-lo)*t[lower,None]
        t2=np.clip((lum-.50)/.50,0,1)
        rgb[~lower]=mi+(hi-mi)*t2[~lower,None]

        # Physically different highlight responses keep leather/rubber/bone distinct.
        specpow=3.2+rough*7.0
        halfz=np.clip((nz+.15*nx-.10*ny),0,1)
        specular=(halfz**specpow)*(1-rough*.64)
        if kind=="velvet": specular*=.18
        elif kind in ("cloth","woven","uniform_fabric","wool"): specular*=.24
        elif kind in ("leather","reptile_leather"): specular*=.72
        elif kind in ("hard_rubber","grown_rubber"): specular*=.56
        elif kind in ("simulated_bone","bone_chitin"): specular*=.36
        elif kind in ("spandex","stretch_fabric","satin","silk"): specular*=.90

        spec_tint=np.array(spec.get("specular_tint",[28,28,28]),dtype=np.float32)
        rgb=np.clip(rgb+specular[...,None]*spec_tint,0,255)

        base=Image.fromarray(np.dstack([rgb.astype(np.uint8),(m*255).astype(np.uint8)]),"RGBA")
        base.alpha_composite(self._brush_scumble(mask,spec,seed_offset,float(spec.get("brush_density",1.0))))
        base.alpha_composite(self._material_microdetail(mask,material_name,seed_offset))

        # Directional edge modelling: recess on the lower/right, narrow catch only
        # where an upper-left light would actually strike. Never a uniform cartoon outline.
        edge_band=(dist>1)&(dist<7)&inside
        ma=m.astype(np.float32)
        my,mx=np.gradient(ma)
        mag=np.sqrt(mx*mx+my*my)+1e-6
        ex=-mx/mag; ey=-my/mag
        edge_light=np.clip(ex*(-.52)+ey*(-.70),0,1)
        edge_dark=np.clip(ex*(.52)+ey*(.70),0,1)
        ep=np.zeros((HI,HI,4),dtype=np.uint8)
        lit=edge_band&(edge_light>.18)
        dark=edge_band&(edge_dark>.18)
        if kind in ("hard_rubber","grown_rubber","simulated_bone","bone_chitin"):
            # Screen-used Wraith rubber/bone reads by recessed edge depth and rubbed
            # high points, not a bright ring around the whole piece.
            ep[lit,:3]=np.clip(mi*.88,0,255).astype(np.uint8)
            ep[lit,3]=(edge_light[lit]*9).astype(np.uint8)
            ep[dark,:3]=np.clip(lo*.58,0,255).astype(np.uint8)
            ep[dark,3]=(edge_dark[dark]*72).astype(np.uint8)
        else:
            ep[lit,:3]=np.clip(hi*.82,0,255).astype(np.uint8)
            ep[lit,3]=(edge_light[lit]*18).astype(np.uint8)
            ep[dark,:3]=np.clip(lo*.68,0,255).astype(np.uint8)
            ep[dark,3]=(edge_dark[dark]*42).astype(np.uint8)
        base.alpha_composite(Image.fromarray(ep,"RGBA"))

        # Clear-coated screen-used rubber gets a few broad reflection strokes rather
        # than a plastic-looking full-surface shine.
        if kind in ("hard_rubber","grown_rubber") and float(spec.get("clearcoat",0))>0:
            coat=float(spec.get("clearcoat",.5))
            glaze=Image.new("RGBA",(HI,HI),(0,0,0,0))
            gd=ImageDraw.Draw(glaze)
            rr=random.Random(88031+int(seed_offset)*29)
            for k in range(3):
                yy0=int(y0+h*(.22+k*.24)+rr.uniform(-.025,.025)*h)
                xstart=int(x0+w*(.22+rr.uniform(-.03,.03)))
                xend=int(x0+w*(.72+rr.uniform(-.03,.03)))
                gd.arc((xstart,yy0-int(h*.045),xend,yy0+int(h*.045)),195,340,
                       fill=(218,226,222,int(30+coat*36)),width=max(1,int(min(w,h)*.006)))
            base.alpha_composite(self._clip(glaze.filter(ImageFilter.GaussianBlur(.55)),mask))

        base.putalpha(mask)
        return base

    def _garment_relief(self, out, bb, piece_mask, guides, material_name):
        """Legacy surface accent. V3 sculpts relief in the height field only."""
        if not guides or self.s3.get("renderer")=="stargate_costume_illustration_v3":
            return
        spec=self._material_spec(material_name)
        shadow=np.array(spec["shadow"],dtype=int)
        high=np.array(spec["high"],dtype=int)
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        layer=Image.new("RGBA",(HI,HI),(0,0,0,0))
        d=ImageDraw.Draw(layer)
        for g in guides:
            pts=[(int(x0+float(x)*w),int(y0+float(y)*h)) for x,y in g.get("points",[])]
            if len(pts)<2:
                continue
            width=max(2,int(float(g.get("width",.010))*min(w,h)))
            alpha=min(72,int(g.get("alpha",90)))
            d.line(pts,fill=tuple(np.clip(shadow*.76,0,255).astype(int))+(alpha,),
                   width=max(2,width+1),joint="curve")
            d.line([(x-1,y-1) for x,y in pts],
                   fill=tuple(np.clip(high*.96,0,255).astype(int))+(max(14,int(alpha*.28)),),
                   width=max(1,width//3),joint="curve")
        out.alpha_composite(self._clip(layer,piece_mask))

    def _clear_prop_cover(self, out, piece_mask, seed_offset=0, strength=.5):
        bb=piece_mask.getbbox()
        if not bb:
            return
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        rng=random.Random(94031+int(seed_offset)*53+x0+y0)
        glaze=Image.new("RGBA",(HI,HI),(0,0,0,0))
        wash=Image.new("L",(HI,HI),0)
        wd=ImageDraw.Draw(wash)
        wd.ellipse((int(x0-w*.10),int(y0-h*.18),int(x0+w*.78),int(y0+h*.78)),fill=int(25*strength))
        wash=ImageChops.multiply(wash.filter(ImageFilter.GaussianBlur(max(3,int(min(w,h)*.055)))),piece_mask)
        tint=Image.new("RGBA",(HI,HI),(158,176,170,0)); tint.putalpha(wash)
        glaze.alpha_composite(tint)
        gd=ImageDraw.Draw(glaze)
        for i in range(3):
            yy0=int(y0+h*(.18+i*.22)+rng.uniform(-.022,.022)*h)
            xa=int(x0+w*(.18+rng.uniform(-.02,.03)))
            xb=int(x0+w*(.66+rng.uniform(-.03,.04)))
            gd.arc((xa,yy0-int(h*.045),xb,yy0+int(h*.045)),200,338,
                   fill=(218,228,224,int(18+26*strength)),width=max(1,int(min(w,h)*.006)))
        out.alpha_composite(self._clip(glaze.filter(ImageFilter.GaussianBlur(.45)),piece_mask))

    def _hard_surface_edge_wear(self, out, piece_mask, material_name, seed_offset=0):
        spec=self._material_spec(material_name)
        kind=str(spec["kind"]).lower()
        if kind not in ("hard_rubber","grown_rubber","simulated_bone","bone_chitin"):
            return
        bb=piece_mask.getbbox()
        if not bb:
            return
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        inside=np.array(piece_mask)>24
        dist=distance_transform_edt(inside)
        edge=(dist>1)&(dist<8)
        ys,xs=np.where(edge)
        if len(xs)==0:
            return
        rng=random.Random(95017+int(seed_offset)*83+x0*2+y0*3)
        hi=tuple(int(v) for v in spec["high"])
        lo=tuple(int(v) for v in spec["shadow"])
        layer=Image.new("RGBA",(HI,HI),(0,0,0,0))
        d=ImageDraw.Draw(layer)
        count=max(6,min(26,int((w+h)/30)))
        for i in range(count):
            idx=rng.randrange(len(xs)); x=int(xs[idx]); y=int(ys[idx])
            ln=rng.randint(2,max(3,int(min(w,h)*.016)))
            ang=rng.uniform(-.9,.9)
            dx=int(math.cos(ang)*ln); dy=int(math.sin(ang)*ln)
            if i%3:
                d.line((x,y,x+dx,y+dy),fill=hi+(rng.randint(14,38),),width=1)
            else:
                rr=rng.choice([1,1,2])
                d.ellipse((x-rr,y-rr,x+rr,y+rr),fill=lo+(rng.randint(30,65),))
        out.alpha_composite(self._clip(layer,piece_mask))

    def _garment_seam(self, out, bb, seam):
        pts=seam.get("points",[])
        if len(pts)<2:
            return
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        q=[(int(x0+float(x)*w),int(y0+float(y)*h)) for x,y in pts]
        width=max(1,int(float(seam.get("width",.006))*min(w,h)))
        d=ImageDraw.Draw(out)
        d.line(q,fill=tuple(seam.get("shadow",[10,11,11]))+(int(seam.get("alpha",180)),),width=max(1,width+2),joint="curve")
        d.line([(x-1,y-1) for x,y in q],fill=tuple(seam.get("highlight",[118,122,117]))+(110,),width=max(1,width),joint="curve")
        if seam.get("stitch",False):
            self._stitch(out,q,max(9,int(float(seam.get("stitch_spacing",.035))*h)),
                         tuple(seam.get("stitch_color",[118,110,105]))+(150,))

    def _garment_closure(self, out, bb, closure):
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        typ=closure.get("type","snaps")
        if typ=="snaps":
            a=closure["from"]; b=closure["to"]; count=max(1,int(closure.get("count",5)))
            for i in range(count):
                t=(i+.5)/count
                x=int(x0+(a[0]*(1-t)+b[0]*t)*w); y=int(y0+(a[1]*(1-t)+b[1]*t)*h)
                self._snap(out,x,y,max(2,int(min(w,h)*float(closure.get("radius",.008)))))
        elif typ=="lacing":
            l0,l1=closure["left"]; r0,r1=closure["right"]
            self._lacing(
                out,
                [(int(x0+l0[0]*w),int(y0+l0[1]*h)),(int(x0+l1[0]*w),int(y0+l1[1]*h))],
                [(int(x0+r0[0]*w),int(y0+r0[1]*h)),(int(x0+r1[0]*w),int(y0+r1[1]*h))],
                int(closure.get("pairs",7))
            )
        elif typ=="zipper":
            a=closure["from"]; b=closure["to"]
            p0=(int(x0+a[0]*w),int(y0+a[1]*h)); p1=(int(x0+b[0]*w),int(y0+b[1]*h))
            d=ImageDraw.Draw(out)
            d.line((p0,p1),fill=(12,12,13,210),width=max(2,int(min(w,h)*.010)))
            length=max(1,int(math.dist(p0,p1))); teeth=max(4,int(closure.get("teeth",10)))
            for i in range(teeth):
                t=(i+.5)/teeth
                x=int(p0[0]*(1-t)+p1[0]*t); y=int(p0[1]*(1-t)+p1[1]*t)
                d.line((x-2,y,x+2,y),fill=(128,126,124,155),width=1)
        elif typ=="belt":
            a=closure["from"]; b=closure["to"]
            p0=(int(x0+a[0]*w),int(y0+a[1]*h)); p1=(int(x0+b[0]*w),int(y0+b[1]*h))
            d=ImageDraw.Draw(out)
            d.line((p0,p1),fill=(18,18,18,225),width=max(3,int(min(w,h)*float(closure.get("width",.018)))))
            t=.5; x=int(p0[0]*(1-t)+p1[0]*t); y=int(p0[1]*(1-t)+p1[1]*t)
            rr=max(2,int(min(w,h)*.012))
            d.rectangle((x-rr,y-rr,x+rr,y+rr),outline=(137,133,126,190),width=max(1,rr//3))

        elif typ=="segmented_belt":
            a=closure.get("from",[.25,.62]); b=closure.get("to",[.75,.62])
            count=max(3,int(closure.get("count",6)))
            d=ImageDraw.Draw(out)
            p0=(x0+float(a[0])*w,y0+float(a[1])*h)
            p1=(x0+float(b[0])*w,y0+float(b[1])*h)
            bandw=max(4,int(min(w,h)*float(closure.get("width",.022))))
            d.line((int(p0[0]),int(p0[1]),int(p1[0]),int(p1[1])),
                   fill=(8,10,10,230),width=bandw)
            for i in range(count):
                t=(i+.5)/count
                x=int(p0[0]*(1-t)+p1[0]*t); y=int(p0[1]*(1-t)+p1[1]*t)
                segw=max(5,int(abs(p1[0]-p0[0])/count*.62))
                segh=max(3,int(bandw*.72))
                d.rounded_rectangle((x-segw//2,y-segh//2,x+segw//2,y+segh//2),
                    radius=max(1,segh//3),fill=(24,29,27,235),
                    outline=(67,74,70,175),width=1)
                if i%2==0:
                    d.line((x-segw//3,y-segh//4,x+segw//3,y-segh//4),
                           fill=(114,121,116,70),width=1)
            # practical central buckle, subdued
            bx=int((p0[0]+p1[0])*.5); by=int((p0[1]+p1[1])*.5)
            rr=max(3,int(bandw*.55))
            d.rounded_rectangle((bx-rr,by-rr,bx+rr,by+rr),radius=max(1,rr//3),
                fill=(18,20,20,245),outline=(103,105,102,190),width=max(1,rr//3))

        elif typ=="self_destruct":
            pos=closure.get("position",[.50,.31])
            x=int(x0+float(pos[0])*w); y=int(y0+float(pos[1])*h)
            r=max(4,int(min(w,h)*float(closure.get("radius",.022))))
            d=ImageDraw.Draw(out)
            # recessed black leather/rubber mount
            d.ellipse((x-r*1.5,y-r*1.25,x+r*1.5,y+r*1.25),
                      fill=(7,9,9,232),outline=(45,52,50,205),width=max(1,r//4))
            # translucent protective cover
            cover=Image.new("RGBA",(HI,HI),(0,0,0,0))
            cd=ImageDraw.Draw(cover)
            cd.ellipse((x-r*1.12,y-r*.88,x+r*1.12,y+r*.88),
                       fill=(145,165,160,36),outline=(184,198,194,88),width=max(1,r//5))
            cd.arc((x-r*.95,y-r*.72,x+r*.95,y+r*.72),205,325,
                   fill=(226,236,232,72),width=max(1,r//5))
            out.alpha_composite(cover.filter(ImageFilter.GaussianBlur(max(1,r//7))))
            # tiny restrained activation core, not a glow stripe
            core=tuple(self.palette.get("biotech",[56,78,70]))
            d=ImageDraw.Draw(out)
            cr=max(2,r//3)
            d.ellipse((x-cr,y-cr,x+cr,y+cr),fill=core+(205,),outline=(20,28,26,230),width=1)

    def _garment_wear(self, out, bb, wear):
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        rng=random.Random(44531+int(wear.get("seed",0))*101)
        count=max(0,int(wear.get("count",4)))
        d=ImageDraw.Draw(out)
        for _ in range(count):
            x=int(x0+rng.uniform(*wear.get("x_range",[.18,.82]))*w)
            y=int(y0+rng.uniform(*wear.get("y_range",[.22,.90]))*h)
            ln=max(3,int(w*rng.uniform(.025,.075)))
            dy=rng.randint(-3,3)
            d.line((x,y,min(x1-1,x+ln),y+dy),fill=tuple(wear.get("highlight",[171,170,163]))+(rng.randint(35,75),),width=1)
            d.line((x+1,y+2,min(x1-1,x+ln)+1,y+dy+2),fill=(8,9,9,rng.randint(40,85)),width=1)

    def _render_costume_blueprint(self, body, direction, mask):
        """Profile-driven Stargate wardrobe renderer.

        It paints named costume pieces (lapel, yoke, vest, sash, epaulette, cuff,
        hem, skirt/tail, belt, etc.) rather than arbitrary armour panels.
        """
        bp=self.s3["garment_blueprint"]
        view=bp["views"][direction]
        bb=mask.getbbox()
        if not bb:
            return Image.new("RGBA",(OUT,OUT),(0,0,0,0))

        hist=None

        base_material=bp["base_material"]
        out=self._garment_material(mask,base_material,hist,view.get("folds",[]),100,view.get("base_relief",[]))
        semantic_union=Image.new("L",(HI,HI),0)
        max_piece_frac=0.0
        mask_area=max(1,int((np.array(mask)>16).sum()))

        for i,piece in enumerate(view.get("pieces",[])):
            pm=self._garment_mask(bb,piece["points"],mask,float(piece.get("feather",.65)))
            # Semantic cutouts let screen-derived costume pieces form crescents,
            # split guards and open lacing zones instead of becoming filled blobs.
            for cut in piece.get("cutouts",[]):
                cm=self._garment_mask(bb,cut,mask,float(piece.get("feather",.65)))
                pm=ImageChops.subtract(pm,cm)
            semantic_union=ImageChops.lighter(semantic_union,pm)
            frac=float((np.array(pm)>16).sum()/mask_area)
            max_piece_frac=max(max_piece_frac,frac)
            out.alpha_composite(self._contact_shadow(mask,pm,
                int(piece.get("contact_shadow",58)),int(piece.get("shadow_radius",6)),
                tuple(piece.get("shadow_offset",[2,3]))))
            layer=self._garment_material(pm,piece["material"],hist,piece.get("folds",[]),200+i,piece.get("relief",[]))
            out.alpha_composite(layer)
            self._garment_relief(out,bb,pm,piece.get("relief",[]),piece["material"])
            if self.s3.get("renderer")=="stargate_costume_illustration_v3":
                if piece.get("clear_cover"):
                    self._clear_prop_cover(out,pm,200+i,float(piece.get("clear_cover_strength",.5)))
                self._hard_surface_edge_wear(out,pm,piece["material"],200+i)
            if piece.get("mirror_x"):
                mirrored=pm.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                mirrored_relief=[]
                for g in piece.get("relief",[]):
                    mg=dict(g)
                    mg["points"]=[[1-float(x),float(y)] for x,y in g.get("points",[])]
                    mirrored_relief.append(mg)
                out.alpha_composite(self._contact_shadow(mask,mirrored,
                    int(piece.get("contact_shadow",58)),int(piece.get("shadow_radius",6)),
                    tuple(piece.get("shadow_offset",[-2,3]))))
                layer2=self._garment_material(mirrored,piece["material"],hist,piece.get("folds",[]),300+i,mirrored_relief)
                out.alpha_composite(layer2)
                self._garment_relief(out,bb,mirrored,mirrored_relief,piece["material"])
                if self.s3.get("renderer")=="stargate_costume_illustration_v3":
                    if piece.get("clear_cover"):
                        self._clear_prop_cover(out,mirrored,300+i,float(piece.get("clear_cover_strength",.5)))
                    self._hard_surface_edge_wear(out,mirrored,piece["material"],300+i)
                semantic_union=ImageChops.lighter(semantic_union,mirrored)
                max_piece_frac=max(max_piece_frac,float((np.array(mirrored)>16).sum()/mask_area))

        for seam in view.get("seams",[]):
            self._garment_seam(out,bb,seam)
        for closure in view.get("closures",[]):
            self._garment_closure(out,bb,closure)
        if view.get("wear"):
            self._garment_wear(out,bb,view["wear"])

        covered=float((np.array(semantic_union)>16).sum()/mask_area)
        self.garment_metrics.append({
            "body":body,
            "direction":direction,
            "design_class":bp["design_class"],
            "piece_count":len(view.get("pieces",[])),
            "semantic_piece_coverage":covered,
            "base_visible_fraction":max(0.0,1.0-covered),
            "max_piece_fraction":max_piece_frac,
            "seam_count":len(view.get("seams",[])),
            "closure_count":len(view.get("closures",[]))
        })

        out=self._finish(out,mask)
        out=out.resize((OUT,OUT),Image.Resampling.LANCZOS)
        post=float(self.s3.get("post_downsample_contrast",1.0))
        if abs(post-1.0)>1e-6:
            alpha=out.getchannel("A")
            out=ImageEnhance.Contrast(out).enhance(post)
            out.putalpha(alpha)
        return out

    def _tile_costume_blueprint(self, mask):
        out=self._render_costume_blueprint("Male","south",mask)
        alpha=out.getchannel("A")
        out=ImageEnhance.Contrast(out).enhance(float(self.s3.get("tile_contrast",1.06)))
        out.putalpha(alpha)
        return out

    def _line_mask(self, pts, width, blur=1.5):
        m=Image.new("L",(HI,HI),0); d=ImageDraw.Draw(m)
        d.line(pts,fill=255,width=max(1,int(width)),joint="curve")
        if blur: m=m.filter(ImageFilter.GaussianBlur(blur))
        return m

    def _clip(self, layer, mask):
        a=np.array(layer.getchannel("A"),dtype=np.uint16)
        mm=np.array(mask,dtype=np.uint16)
        layer=layer.copy(); layer.putalpha(Image.fromarray(((a*mm)//255).astype(np.uint8),"L"))
        return layer

    def _leather(self, mask, hist=None, contrast=False):
        p=self.palette
        if contrast:
            return self._material(mask,p["contrast_shadow"],p["contrast_mid"],p["contrast_high"],"leather",hist)
        return self._material(mask,p["leather_shadow"],p["leather_mid"],p["leather_high"],"leather",hist)

    def _reptile(self, mask, hist=None, seed_offset=0):
        """
        Black reptile-pattern leather from the Stargate commander costume.
        Pattern is deliberately larger/cleaner than noise so it survives 192px.
        """
        p=self.palette
        base=self._material(mask,p["reptile_shadow"],p["reptile_mid"],p["reptile_high"],"reptile",hist)
        bb=mask.getbbox()
        if not bb:
            return base
        x0,y0,x1,y1=bb
        w=x1-x0
        rng=random.Random(49031+seed_offset+x0+y0)

        tex=Image.new("RGBA",(HI,HI),(0,0,0,0))
        d=ImageDraw.Draw(tex)
        step_x=max(18,int(w*.072))
        step_y=max(12,int(step_x*.58))
        row=0
        for y in range(y0-step_y,y1+step_y,step_y):
            offset=step_x//2 if row%2 else 0
            for x in range(x0-step_x,x1+step_x,step_x):
                cx=x+offset+rng.randint(-1,1)
                cy=y+rng.randint(-1,1)
                rx=max(6,int(step_x*.43))
                ry=max(4,int(step_y*.43))
                # Dark lower edge + restrained upper catchlight = leather scale, not armour plate.
                d.arc((cx-rx,cy-ry,cx+rx,cy+ry),188,352,fill=(9,8,11,175),width=max(2,int(HI/320)))
                d.arc((cx-rx+2,cy-ry+2,cx+rx-2,cy+ry-2),12,168,fill=(154,144,158,78),width=max(1,int(HI/420)))
            row+=1
        tex=self._clip(tex,mask)
        base.alpha_composite(tex)
        return base

    def _stitch(self, im, pts, spacing=18, color=(122,112,119,150)):
        d=ImageDraw.Draw(im)
        seg=[]
        for a,b in zip(pts[:-1],pts[1:]):
            L=math.dist(a,b); seg.append((a,b,L))
        total=sum(x[2] for x in seg)
        pos=0
        while pos<total:
            remain=pos
            for a,b,L in seg:
                if remain<=L:
                    t=remain/max(1,L)
                    x=a[0]+(b[0]-a[0])*t; y=a[1]+(b[1]-a[1])*t
                    d.line((x-2,y-1,x+2,y+1),fill=color,width=1)
                    break
                remain-=L
            pos+=spacing

    def _gem(self, im, x, y, r=4):
        col=tuple(self.palette.get("biotech",[145,140,198]))
        glow=Image.new("RGBA",(HI,HI),(0,0,0,0))
        gd=ImageDraw.Draw(glow)
        gd.ellipse((x-r*4,y-r*4,x+r*4,y+r*4),fill=col+(34,))
        im.alpha_composite(glow.filter(ImageFilter.GaussianBlur(max(2,r*2))))
        d=ImageDraw.Draw(im)
        d.ellipse((x-r,y-r,x+r,y+r),fill=col+(220,),outline=(48,45,58,240),width=max(1,r//3))
        d.ellipse((x-r*.35,y-r*.35,x+r*.35,y+r*.35),fill=(225,231,236,215))

    def _destruct_module(self, im, x, y, r=7):
        """Wraith warrior chest self-destruct under a clear/translucent cover."""
        d=ImageDraw.Draw(im)
        # black leather/rubber mounting recess
        d.ellipse((x-r*1.55,y-r*1.35,x+r*1.55,y+r*1.35),fill=(10,11,12,235),outline=(64,66,68,220),width=max(1,r//3))
        # clear plastic cover catches a cool silver highlight
        cover=Image.new("RGBA",(HI,HI),(0,0,0,0))
        cd=ImageDraw.Draw(cover)
        cd.ellipse((x-r*1.2,y-r,x+r*1.2,y+r),fill=(155,170,172,48),outline=(188,198,200,120),width=max(1,r//4))
        im.alpha_composite(cover.filter(ImageFilter.GaussianBlur(max(1,r//5))))
        # restrained biotech core beneath the cover
        col=tuple(self.palette.get("biotech",[72,155,150]))
        glow=Image.new("RGBA",(HI,HI),(0,0,0,0))
        gd=ImageDraw.Draw(glow)
        gd.ellipse((x-r*.62,y-r*.62,x+r*.62,y+r*.62),fill=col+(52,))
        im.alpha_composite(glow.filter(ImageFilter.GaussianBlur(max(2,r))))
        d=ImageDraw.Draw(im)
        d.ellipse((x-r*.34,y-r*.34,x+r*.34,y+r*.34),fill=col+(220,),outline=(28,40,40,240),width=max(1,r//4))
        d.ellipse((x-r*.12,y-r*.12,x+r*.12,y+r*.12),fill=(210,226,224,210))

    def _snap(self, im, x, y, r=3):
        col=tuple(self.palette["metal"])
        d=ImageDraw.Draw(im)
        d.ellipse((x-r,y-r,x+r,y+r),fill=(32,30,33,230),outline=col+(205,),width=max(1,r//2))
        d.ellipse((x-r*.35,y-r*.35,x+r*.35,y+r*.35),fill=(174,169,175,180))

    def _lacing(self, im, left_pts, right_pts, pairs=7):
        d=ImageDraw.Draw(im)
        # Sample straight-ish paired eyelets and crossing cords.
        for i in range(pairs):
            t=(i+.5)/pairs
            lx=left_pts[0][0]*(1-t)+left_pts[-1][0]*t
            ly=left_pts[0][1]*(1-t)+left_pts[-1][1]*t
            rx=right_pts[0][0]*(1-t)+right_pts[-1][0]*t
            ry=right_pts[0][1]*(1-t)+right_pts[-1][1]*t
            self._snap(im,int(lx),int(ly),2)
            self._snap(im,int(rx),int(ry),2)
            if i%2==0:
                d.line((lx,ly,rx,ry+3),fill=(20,18,21,210),width=2)
            else:
                d.line((rx,ry,lx,ly+3),fill=(20,18,21,210),width=2)

    def _finish(self, out, mask):
        ship_contrast=[x["contrast"] for x in self.benchmark_stats if 20<x["contrast"]<100]
        live_contrast=[x["contrast"] for x in self.ref.image_stats if min(x.get("size",[0,0]))>=100 and 15<x["contrast"]<95]
        if "finish_contrast_target" in self.s3:
            target=float(self.s3["finish_contrast_target"])
        elif ship_contrast:
            # Ships benchmark readable depth, but apparel remains materially darker.
            target=float(np.clip(np.median(ship_contrast)*.72,31.0,40.0))
        elif live_contrast:
            target=float(np.clip(np.median(live_contrast),30.0,39.0))
        else:
            target=34.0

        a=np.array(out,dtype=np.float32)
        m=np.array(mask)>16
        rgb=a[...,:3]
        yy,xx=np.mgrid[0:HI,0:HI]

        # Broad leather modelling, not cloud noise.
        bb=mask.getbbox()
        if bb:
            x0,y0,x1,y1=bb
            w=max(1,x1-x0); h=max(1,y1-y0)
            localx=(xx-x0)/w; localy=(yy-y0)/h
            broad=np.exp(-(((localx-.34)/.30)**2+((localy-.34)/.58)**2))
            broad=(broad-.22)*14.0
            rgb=np.clip(rgb+broad[...,None]*m[...,None],0,255)

        # Vanilla-readable leather edge: dark boundary, narrow polished catch just inside it.
        dist=distance_transform_edt(m)
        outer=(dist>0)&(dist<=3)
        catch=(dist>3)&(dist<=8)
        recess=(dist>8)&(dist<=15)
        rgb[outer]=np.clip(rgb[outer]-np.array([6,6,6]),0,255)
        rgb[catch]=np.clip(rgb[catch]+np.array([6,7,6]),0,255)
        rgb[recess]=np.clip(rgb[recess]-np.array([2,2,2]),0,255)

        lum=.2126*rgb[...,0]+.7152*rgb[...,1]+.0722*rgb[...,2]
        cur=lum[m].std() if m.any() else 1
        scale=np.clip(target/max(cur,1),1.0,1.88)
        mean=rgb[m].mean(axis=0) if m.any() else np.array([45,48,46])
        rgb=(rgb-mean)*scale+mean
        a[...,:3]=np.clip(rgb,0,255)
        a[...,3]=np.array(mask)
        out=Image.fromarray(a.astype(np.uint8),"RGBA")
        out=ImageEnhance.Contrast(out).enhance(float(self.s3.get("finish_contrast_enhance",1.04)))
        out=out.filter(ImageFilter.UnsharpMask(
            radius=float(self.s3.get("finish_unsharp_radius",.95)),
            percent=int(self.s3.get("finish_unsharp_percent",62)),
            threshold=int(self.s3.get("finish_unsharp_threshold",3))
        ))
        out.putalpha(mask)
        return out

    def _paint_hunter(self, body, direction, mask):
        bb=mask.getbbox(); x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        hist=self._historical("WNG_HunterCoat",body,direction,bb)
        accent=self._historical("WNG_QueenRaiment",body,direction,bb)
        out=self._leather(mask,hist,False)

        if direction=="south":
            # Black reptile-leather inner vest, matching production Wraith commander construction.
            vest=self._poly_mask(bb,[(.34,.05),(.66,.05),(.62,.55),(.56,.83),(.44,.83),(.38,.55)],1)
            vest=ImageChops.multiply(vest,mask)
            out.alpha_composite(self._reptile(vest,accent,10))

            # Narrow contrast leather lapels following the Duster neck/torso, not armour plates.
            lapL=self._poly_mask(bb,[(.31,.06),(.43,.05),(.48,.22),(.42,.51),(.35,.47)],1)
            lapR=self._poly_mask(bb,[(.69,.06),(.57,.05),(.52,.22),(.58,.51),(.65,.47)],1)
            for lm in (lapL,lapR):
                lm=ImageChops.multiply(lm,mask)
                out.alpha_composite(self._leather(lm,hist,True))

            # Layered reptile-leather epaulettes; three small overlapping panels per shoulder.
            shoulders=[
              [(.10,.10),(.22,.055),(.32,.085),(.30,.17),(.14,.18)],
              [(.13,.16),(.25,.11),(.34,.14),(.31,.22),(.17,.23)]
            ]
            for idx,pts in enumerate(shoulders):
                for side in (pts,[(1-x,y) for x,y in pts]):
                    sm=ImageChops.multiply(self._poly_mask(bb,side,1),mask)
                    out.alpha_composite(self._reptile(sm,hist,30+idx))

            # Side lacing and subtle front closures.
            self._lacing(
                out,
                [(int(x0+.18*w),int(y0+.38*h)),(int(x0+.20*w),int(y0+.74*h))],
                [(int(x0+.24*w),int(y0+.38*h)),(int(x0+.26*w),int(y0+.74*h))],
                6
            )
            for i in range(5):
                yy=int(y0+h*(.27+i*.075))
                self._snap(out,int(x0+.51*w),yy,2)
            self._stitch(out,[(int(x0+.70*w),int(y0+.30*h)),(int(x0+.74*w),int(y0+.78*h))],max(13,.055*h))

        elif direction=="north":
            # Back yoke and layered shoulder panels.
            yoke=self._poly_mask(bb,[(.12,.08),(.88,.08),(.79,.29),(.50,.34),(.21,.29)],1)
            yoke=ImageChops.multiply(yoke,mask)
            out.alpha_composite(self._reptile(yoke,hist,80))
            center=self._poly_mask(bb,[(.43,.18),(.57,.18),(.55,.80),(.45,.80)],1)
            center=ImageChops.multiply(center,mask)
            out.alpha_composite(self._leather(center,accent,True))

            # Characteristic side/back lacing from production costume.
            self._lacing(
                out,
                [(int(x0+.36*w),int(y0+.31*h)),(int(x0+.38*w),int(y0+.76*h))],
                [(int(x0+.43*w),int(y0+.31*h)),(int(x0+.45*w),int(y0+.76*h))],
                7
            )
            self._lacing(
                out,
                [(int(x0+.57*w),int(y0+.31*h)),(int(x0+.55*w),int(y0+.76*h))],
                [(int(x0+.64*w),int(y0+.31*h)),(int(x0+.62*w),int(y0+.76*h))],
                7
            )

        else:
            # Side profile: reptile shoulder panel + inner vest glimpse + visible lacing.
            shoulder=self._poly_mask(bb,[(.28,.06),(.70,.05),(.86,.20),(.72,.29),(.43,.23)],1)
            shoulder=ImageChops.multiply(shoulder,mask)
            out.alpha_composite(self._reptile(shoulder,hist,110))
            sidepanel=self._poly_mask(bb,[(.48,.24),(.72,.26),(.69,.76),(.53,.80)],1)
            sidepanel=ImageChops.multiply(sidepanel,mask)
            out.alpha_composite(self._reptile(sidepanel,accent,120))
            self._lacing(
                out,
                [(int(x0+.50*w),int(y0+.36*h)),(int(x0+.52*w),int(y0+.74*h))],
                [(int(x0+.57*w),int(y0+.36*h)),(int(x0+.59*w),int(y0+.74*h))],
                6
            )
            self._stitch(out,[(int(x0+.31*w),int(y0+.28*h)),(int(x0+.34*w),int(y0+.78*h))],max(13,.055*h))

        out=self._finish(out,mask)
        out=out.resize((OUT,OUT),Image.Resampling.LANCZOS)
        # Profile-controlled final-scale separation. Fine production-costume
        # material cues can lose value separation during the 4x RimWorld
        # downsample; restore it at the actual live 192px scale without
        # altering the vanilla implementation alpha.
        post=float(self.s3.get("post_downsample_contrast",1.0))
        if abs(post-1.0)>1e-6:
            alpha=out.getchannel("A")
            out=ImageEnhance.Contrast(out).enhance(post)
            out.putalpha(alpha)
        return out

    def _tile_hunter(self, mask):
        bb=mask.getbbox(); x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        hist=self._historical("WNG_HunterCoat","Male","south",bb)
        out=self._leather(mask,hist,False)
        vest=self._poly_mask(bb,[(.34,.05),(.66,.05),(.62,.58),(.55,.84),(.45,.84),(.38,.58)],1)
        vest=ImageChops.multiply(vest,mask)
        out.alpha_composite(self._reptile(vest,hist,150))
        shoulders=[
          [(.10,.10),(.22,.055),(.32,.085),(.30,.17),(.14,.18)],
          [(.13,.16),(.25,.11),(.34,.14),(.31,.22),(.17,.23)]
        ]
        for idx,pts in enumerate(shoulders):
            for side in (pts,[(1-x,y) for x,y in pts]):
                sm=ImageChops.multiply(self._poly_mask(bb,side,1),mask)
                out.alpha_composite(self._reptile(sm,hist,160+idx))
        self._lacing(
            out,
            [(int(x0+.18*w),int(y0+.39*h)),(int(x0+.20*w),int(y0+.73*h))],
            [(int(x0+.24*w),int(y0+.39*h)),(int(x0+.26*w),int(y0+.73*h))],
            6
        )
        for i in range(5):
            self._snap(out,int(x0+.51*w),int(y0+h*(.27+i*.075)),2)
        out=self._finish(out,mask)
        # Ground/inventory tiles need slightly stronger value separation than worn art.
        out=ImageEnhance.Contrast(out).enhance(1.20)
        out.putalpha(mask)
        return out.resize((OUT,OUT),Image.Resampling.LANCZOS)


    def _chitin(self, mask, hist=None, bone=False, seed_offset=0):
        """Paint grown Wraith shell with sculpted bevels and restrained organic variation."""
        p=self.palette
        if bone:
            shadow,mid,high=p["bone_shadow"],p["bone_mid"],p["bone_high"]
        else:
            shadow,mid,high=p["shell_shadow"],p["shell_mid"],p["shell_high"]
        m=np.array(mask,dtype=np.float32)/255.0
        inside=m>.08
        dist=distance_transform_edt(inside)
        yy,xx=np.mgrid[0:HI,0:HI]
        bb=mask.getbbox()
        if not bb:
            return Image.new("RGBA",(HI,HI),(0,0,0,0))
        x0,y0,x1,y1=bb; w=max(1,x1-x0); h=max(1,y1-y0)
        lx=(xx-x0)/w; ly=(yy-y0)/h
        broad=np.exp(-(((lx-.32)/.40)**2+((ly-.28)/.65)**2))
        n1=self._noise(2.4)
        n2=self._noise(14.0)
        # organic shell should read as one grown surface, not noisy stone
        organic=.7*n1+1.15*n2
        if hist is not None:
            hl=np.array(hist.convert("L"),dtype=np.float32)
            valid=inside
            vals=hl[valid]
            if vals.size:
                lo=float(np.percentile(vals,12)); hi=float(np.percentile(vals,90))
                hn=np.clip((hl-lo)/max(8.0,hi-lo),0,1)-.5
            else:
                hn=0
        else:
            hn=0
        bevel=np.clip(dist/18,0,1)
        rim=np.clip(1-dist/9,0,1)
        lum=.28+.34*broad+.12*bevel-.12*rim+organic/42.0+.12*hn
        lum=np.clip(lum,0,1)
        lo=np.array(shadow,float); mi=np.array(mid,float); hi=np.array(high,float)
        rgb=np.empty((HI,HI,3),dtype=np.float32)
        lower=lum<.5
        t=np.clip(lum*2,0,1)
        rgb[lower]=lo+(mi-lo)*t[lower,None]
        t2=np.clip((lum-.5)*2,0,1)
        rgb[~lower]=mi+(hi-mi)*t2[~lower,None]
        # narrow moist/chitin catchlight
        spec=(np.clip((lum-.54)/.34,0,1)**2.4)*(0.4+0.6*bevel)
        rgb=np.clip(rgb+spec[...,None]*(np.array([28,33,30]) if not bone else np.array([20,20,21])),0,255)
        out=np.dstack([rgb.astype(np.uint8),(m*255).astype(np.uint8)])
        base=Image.fromarray(out,"RGBA")

        # Grown Wraith surface detail: longitudinal striation, branching grooves,
        # pitting and rubbed ridges. These are material structure, not panel shapes.
        detail=Image.new("RGBA",(HI,HI),(0,0,0,0))
        dd=ImageDraw.Draw(detail)
        rng=random.Random(73019 + int(seed_offset)*97 + x0*3 + y0*5)
        ridge_count=7 if bone else 5
        for i in range(ridge_count):
            frac=(i+1)/(ridge_count+1)
            cx=x0+frac*w
            amp=w*(.018 if bone else .026)
            pts=[]
            for j in range(15):
                t=j/14
                yy0=y0+t*h
                curve=math.sin(t*math.pi*1.4 + i*.72)*amp
                taper=(1-abs(t-.5)*1.2)
                xx0=cx+curve*taper+rng.uniform(-1.6,1.6)
                pts.append((int(xx0),int(yy0)))
            # recessed groove + narrow raised lip, like cast/grown bone/chitin
            dd.line(pts,fill=(20,22,22,105 if not bone else 80),width=max(2,int(w*.010)),joint="curve")
            lip=[(x+max(1,int(w*.006)),y) for x,y in pts]
            dd.line(lip,fill=(213,216,214,70 if not bone else 100),width=max(1,int(w*.0045)),joint="curve")

        # Small branching growth marks, sparse enough to survive RimWorld scale.
        branches=4 if bone else 3
        for i in range(branches):
            sy=y0+h*(.22+i*.16)+rng.uniform(-.025*h,.025*h)
            side=-1 if i%2==0 else 1
            sx=x0+w*(.50+rng.uniform(-.08,.08))
            ex=sx+side*w*rng.uniform(.18,.32)
            ey=sy+h*rng.uniform(.035,.085)
            midx=(sx+ex)/2+side*w*.035
            midy=(sy+ey)/2-h*.025
            dd.line([(int(sx),int(sy)),(int(midx),int(midy)),(int(ex),int(ey))],
                    fill=(29,31,31,92),width=max(2,int(w*.008)),joint="curve")
            dd.line([(int(sx+2),int(sy-1)),(int(midx+2),int(midy-1)),(int(ex+2),int(ey-1))],
                    fill=(196,199,198,45),width=max(1,int(w*.0035)),joint="curve")

        # Pores, scars and rubbed production wear.
        pore_count=max(3,int((w*h)/(HI*HI)*55))
        for _ in range(pore_count):
            px=rng.randint(x0,max(x0,x1-1)); py=rng.randint(y0,max(y0,y1-1))
            rr=rng.choice([1,1,2,2,3])
            dd.ellipse((px-rr,py-rr,px+rr,py+rr),fill=(18,19,20,rng.randint(25,65)))
        for _ in range(max(2,pore_count//4)):
            sx=rng.randint(x0,max(x0,x1-1)); sy=rng.randint(y0,max(y0,y1-1))
            ln=rng.randint(max(5,int(w*.06)),max(7,int(w*.16)))
            dd.line((sx,sy,min(x1-1,sx+ln),sy+rng.randint(-3,4)),
                    fill=(226,226,224,rng.randint(35,75)),width=1)

        detail=self._clip(detail,mask)
        base.alpha_composite(detail)
        return base

    def _membrane(self, mask, hist=None):
        p=self.palette
        return self._material(mask,p["membrane_shadow"],p["membrane_mid"],p["membrane_high"],"reptile",hist)

    def _plate(self, out, fullmask, bb, pts, hist, bone=False, seed=0):
        pm=ImageChops.multiply(self._poly_mask(bb,pts,2),fullmask)
        out.alpha_composite(self._chitin(pm,hist,bone,seed))
        return pm

    def _warrior_plate(self, out, fullmask, bb, pts, hist, bone=False, seed=0):
        """Production Wraith drone plate: grown, rounded and surface-authored.
        The control points define only the internal construction zone; the vanilla
        PowerArmor mask remains the outer silhouette authority.
        """
        return self._command_plate(out,fullmask,bb,pts,hist,bone,seed)

    def _warrior_strap(self, out, fullmask, bb, pts, width_frac=.020):
        x0,y0,x1,y1=bb; w=x1-x0
        q=[(int(x0+x*w),int(y0+y*(y1-y0))) for x,y in pts]
        lm=self._line_mask(q,max(4,w*width_frac),.8)
        lm=ImageChops.multiply(lm,fullmask)
        leather=self._membrane(lm)
        out.alpha_composite(leather)

    def _paint_warrior(self, body, direction, mask):
        bb=mask.getbbox(); x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        hist=self._historical("WNG_WarriorCarapace",body,direction,bb)
        cmd=self._historical("WNG_CommanderCarapace",body,direction,bb)

        # Screen-used drone construction: black leather/flexible under-vest first,
        # with silver hard-rubber carapace and simulated-bone armor grown over it.
        out=self._membrane(mask,hist)

        if direction=="south":
            # Upper thorax: compact overlapping shell leaves a real black central
            # leather channel instead of reading as a superhero chest plate.
            self._warrior_plate(out,mask,bb,[(.14,.14),(.29,.055),(.43,.10),(.465,.25),(.43,.41),(.31,.48),(.15,.42),(.085,.29)],hist,False,701)
            self._warrior_plate(out,mask,bb,[(.86,.14),(.71,.055),(.57,.10),(.535,.25),(.57,.41),(.69,.48),(.85,.42),(.915,.29)],hist,False,702)

            # Simulated-bone shoulder armor is layered, not one flat cap.
            for i,(l,r) in enumerate([
                ([(.025,.095),(.15,.02),(.29,.065),(.31,.15),(.17,.20),(.055,.18)],
                 [(.975,.095),(.85,.02),(.71,.065),(.69,.15),(.83,.20),(.945,.18)]),
                ([(.075,.17),(.19,.095),(.33,.13),(.335,.22),(.20,.265),(.09,.235)],
                 [(.925,.17),(.81,.095),(.67,.13),(.665,.22),(.80,.265),(.91,.235)])
            ]):
                self._warrior_plate(out,mask,bb,l,cmd,True,710+i)
                self._warrior_plate(out,mask,bb,r,cmd,True,720+i)

            # Small clavicle bones frame the black leather throat/vest.
            self._warrior_plate(out,mask,bb,[(.26,.13),(.39,.09),(.47,.17),(.445,.24),(.32,.22)],cmd,True,730)
            self._warrior_plate(out,mask,bb,[(.74,.13),(.61,.09),(.53,.17),(.555,.24),(.68,.22)],cmd,True,731)

            # Four articulated abdominal courses with membrane joints between them.
            for i,yy in enumerate((.43,.545,.66,.775)):
                self._warrior_plate(out,mask,bb,[(.18,yy),(.35,yy-.025),(.465,yy+.015),(.43,yy+.085),(.25,yy+.105),(.14,yy+.065)],hist,False,740+i)
                self._warrior_plate(out,mask,bb,[(.82,yy),(.65,yy-.025),(.535,yy+.015),(.57,yy+.085),(.75,yy+.105),(.86,yy+.065)],hist,False,750+i)

            # Separate hip/thigh guards from the abdominal shell.
            self._warrior_plate(out,mask,bb,[(.16,.82),(.34,.79),(.445,.835),(.415,.965),(.24,.97),(.10,.91)],hist,False,760)
            self._warrior_plate(out,mask,bb,[(.84,.82),(.66,.79),(.555,.835),(.585,.965),(.76,.97),(.90,.91)],hist,False,761)

            # Production fasteners/straps survive as dark construction lines.
            self._warrior_strap(out,mask,bb,[(.18,.35),(.19,.72)],.013)
            self._warrior_strap(out,mask,bb,[(.82,.35),(.81,.72)],.013)
            for i in range(4):
                self._snap(out,int(x0+.19*w),int(y0+h*(.39+i*.09)),max(2,int(w*.006)))
            self._destruct_module(out,int(x0+.50*w),int(y0+.30*h),max(5,int(.016*w)))

        elif direction=="north":
            # Layered shoulder armor + back cuirass around a visible flexible spine.
            self._warrior_plate(out,mask,bb,[(.025,.10),(.17,.025),(.32,.075),(.32,.17),(.16,.215),(.05,.18)],cmd,True,801)
            self._warrior_plate(out,mask,bb,[(.975,.10),(.83,.025),(.68,.075),(.68,.17),(.84,.215),(.95,.18)],cmd,True,802)
            self._warrior_plate(out,mask,bb,[(.12,.19),(.31,.10),(.445,.17),(.425,.39),(.24,.43),(.10,.34)],hist,False,803)
            self._warrior_plate(out,mask,bb,[(.88,.19),(.69,.10),(.555,.17),(.575,.39),(.76,.43),(.90,.34)],hist,False,804)

            # Grown vertebral armor is segmented over a dark central membrane.
            for i,yy in enumerate((.24,.355,.47,.585,.70,.815)):
                self._warrior_plate(out,mask,bb,[(.455,yy-.03),(.50,yy-.055),(.545,yy-.03),(.535,yy+.045),(.465,yy+.045)],cmd,True,810+i)

            for i,yy in enumerate((.43,.56,.69)):
                self._warrior_plate(out,mask,bb,[(.15,yy),(.34,yy-.02),(.445,yy+.025),(.405,yy+.10),(.22,yy+.115)],hist,False,820+i)
                self._warrior_plate(out,mask,bb,[(.85,yy),(.66,yy-.02),(.555,yy+.025),(.595,yy+.10),(.78,yy+.115)],hist,False,830+i)

            self._warrior_plate(out,mask,bb,[(.15,.82),(.35,.79),(.44,.84),(.41,.965),(.23,.97),(.10,.90)],hist,False,840)
            self._warrior_plate(out,mask,bb,[(.85,.82),(.65,.79),(.56,.84),(.59,.965),(.77,.97),(.90,.90)],hist,False,841)

            # One practical side-lacing line breaks perfect bilateral symmetry.
            self._lacing(
                out,
                [(int(x0+.72*w),int(y0+.39*h)),(int(x0+.715*w),int(y0+.72*h))],
                [(int(x0+.77*w),int(y0+.39*h)),(int(x0+.765*w),int(y0+.72*h))],
                6
            )

        else:
            # Side view keeps the vanilla profile while showing the real layering:
            # bone shoulder, hard-rubber thorax, flexible flank, then lower shell.
            self._warrior_plate(out,mask,bb,[(.16,.095),(.46,.025),(.74,.09),(.82,.17),(.69,.24),(.38,.255)],cmd,True,901)
            self._warrior_plate(out,mask,bb,[(.24,.22),(.50,.15),(.75,.26),(.71,.43),(.51,.47),(.27,.39)],hist,False,902)
            for i,yy in enumerate((.48,.60,.72)):
                self._warrior_plate(out,mask,bb,[(.27,yy),(.50,yy-.025),(.70,yy+.02),(.67,yy+.095),(.37,yy+.11)],hist,False,910+i)
            self._warrior_plate(out,mask,bb,[(.27,.82),(.52,.79),(.69,.84),(.64,.96),(.39,.97),(.20,.90)],hist,False,920)
            self._warrior_strap(out,mask,bb,[(.29,.38),(.30,.72)],.013)
            self._stitch(out,[(int(x0+.68*w),int(y0+.32*h)),(int(x0+.70*w),int(y0+.74*h))],max(11,.045*h))

        out=self._finish(out,mask)
        out=out.resize((OUT,OUT),Image.Resampling.LANCZOS)
        post=float(self.s3.get("post_downsample_contrast",1.0))
        if abs(post-1.0)>1e-6:
            alpha=out.getchannel("A")
            out=ImageEnhance.Contrast(out).enhance(post)
            out.putalpha(alpha)
        return out

    def _tile_warrior(self, mask):
        # Inventory/tile art uses the same authored south-facing shell composition,
        # scaled by the vanilla PowerArmor tile mask.
        out=self._paint_warrior("Male","south",mask)
        alpha=out.getchannel("A")
        out=ImageEnhance.Contrast(out).enhance(1.06)
        out.putalpha(alpha)
        return out

    def _command_plate(self, out, fullmask, bb, pts, hist, bone=False, seed=0):
        """Hand-authored Wraith command plate with crisp grown edges and ridges.
        This deliberately avoids flat polygon fills: the polygon only defines the
        anatomical construction zone; material, bevel, edge wear and ridges are painted.
        """
        # Commander plates use smoothed authored contours; the vanilla PowerArmor
        # mask still owns the garment silhouette and clips every plate.
        pm=ImageChops.multiply(self._organic_poly_mask(bb,pts,.65),fullmask)
        out.alpha_composite(self._chitin(pm,hist,bone,seed))
        arr=np.array(pm)>12
        if not arr.any():
            return pm
        dist=distance_transform_edt(arr)

        # Production-readable boundary: deep recess at the plate edge and a narrow
        # rubbed inner catch.  These survive the 4x downsample as 1–2 px accents.
        edge=np.clip((8.0-dist)/8.0,0,1)*arr
        catch=np.clip(1.0-np.abs(dist-11.0)/4.0,0,1)*arr
        high=np.array(self.palette["bone_high" if bone else "shell_high"],dtype=np.uint8)

        shade=Image.new("RGBA",(HI,HI),(0,0,0,0))
        sa=np.zeros((HI,HI,4),dtype=np.uint8)
        sa[...,:3]=np.array([10,12,11],dtype=np.uint8)
        sa[...,3]=(edge*190).astype(np.uint8)
        shade=Image.fromarray(sa,"RGBA")
        out.alpha_composite(shade)

        ca=np.zeros((HI,HI,4),dtype=np.uint8)
        ca[...,:3]=high
        ca[...,3]=(catch*105).astype(np.uint8)
        out.alpha_composite(Image.fromarray(ca,"RGBA"))

        # Historical WNG art contributes only high-frequency relief: never its
        # silhouette or colour.  This keeps established hand-painted complexity
        # while Stargate construction and vanilla geometry remain authoritative.
        hl=np.array(hist.convert("L"),dtype=np.float32)
        relief=gaussian_filter(hl,1.1)-gaussian_filter(hl,7.2)
        vals=np.abs(relief[arr])
        if vals.size:
            scale=max(4.0,float(np.percentile(vals,88)))
            relief=np.clip(relief/scale,-1,1)
            ra=np.zeros((HI,HI,4),dtype=np.uint8)
            positive=relief>=0
            dark=np.array([9,12,11],dtype=np.uint8)
            for c in range(3):
                ra[...,c]=np.where(positive,high[c],dark[c]).astype(np.uint8)
            ra[...,3]=(np.abs(relief)*74*arr).astype(np.uint8)
            out.alpha_composite(Image.fromarray(ra,"RGBA"))

        # Grown ribs follow each plate's actual long axis.  Avoid repeated
        # all-vertical grooves that read as generic procedural sci-fi panels.
        pb=pm.getbbox()
        if pb:
            px0,py0,px1,py1=pb; pw=px1-px0; ph=py1-py0
            rng=random.Random(99001+seed*131)
            detail=Image.new("RGBA",(HI,HI),(0,0,0,0))
            dd=ImageDraw.Draw(detail)
            horizontal=pw>ph*1.18
            for f in (.39,.64):
                pts2=[]
                phase=rng.uniform(-.45,.45)
                for j in range(11):
                    t=j/10
                    if horizontal:
                        xx=px0+pw*(.10+.80*t)
                        yy=py0+ph*(f+math.sin(t*math.pi*1.55+phase)*.045)
                    else:
                        xx=px0+pw*(f+math.sin(t*math.pi*1.55+phase)*.040)
                        yy=py0+ph*(.10+.80*t)
                    pts2.append((int(xx),int(yy)))
                cross=max(1,min(pw,ph))
                darkw=max(3,int(cross*.035))
                litew=max(1,int(cross*.012))
                dd.line(pts2,fill=(10,13,12,178),width=darkw,joint="curve")
                dd.line([(x-2,y-2) for x,y in pts2],
                        fill=tuple(int(v) for v in high)+(132,),width=litew,joint="curve")

            if pw*ph>6500:
                cx=(px0+px1)//2; cy=(py0+py1)//2
                side=-1 if seed%2 else 1
                branch=[(cx,cy),(int(cx+side*pw*.12),int(cy-ph*.08)),(int(cx+side*pw*.24),int(cy-ph*.02))]
                dd.line(branch,fill=(12,15,14,140),width=max(2,int(min(pw,ph)*.025)),joint="curve")
                dd.line([(x-1,y-1) for x,y in branch],
                        fill=tuple(int(v) for v in high)+(82,),width=1,joint="curve")

            detail=self._clip(detail,pm)
            out.alpha_composite(detail)
        return pm

    def _paint_commander(self, body, direction, mask):
        """Wraith commander shell: screen-used leather/reptile construction first,
        then drone-carapace reinforcement, all fitted to vanilla PowerArmor geometry.
        """
        bb=mask.getbbox(); x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        cmd=self._historical("WNG_CommanderCarapace",body,direction,bb)
        coat=self._historical("WNG_HunterCoat",body,direction,bb)

        # Flexible green-black leather remains visibly exposed between every armour
        # mass.  That exposed construction is essential to the production costume.
        out=self._leather(mask,coat,False)

        if direction=="south":
            vest=self._poly_mask(bb,[(.37,.06),(.63,.06),(.595,.25),(.575,.77),(.525,.88),(.475,.88),(.425,.77),(.405,.25)],1)
            vest=ImageChops.multiply(vest,mask)
            out.alpha_composite(self._reptile(vest,cmd,510))

            # Compact thoracic carapace with a dark central reptile channel and
            # green-leather side gaps.  No single pale slab is allowed to dominate.
            self._command_plate(out,mask,bb,[(.17,.15),(.30,.075),(.435,.145),(.44,.34),(.385,.435),(.25,.455),(.145,.34)],cmd,False,511)
            self._command_plate(out,mask,bb,[(.83,.15),(.70,.075),(.565,.145),(.56,.34),(.615,.435),(.75,.455),(.855,.34)],cmd,False,512)

            # Layered command shoulders: bone cap over grown shell under-cap.
            for flip,base_seed in ((False,520),(True,524)):
                outer=[(.035,.095),(.16,.028),(.315,.075),(.285,.17),(.09,.205)]
                inner=[(.105,.175),(.23,.11),(.365,.16),(.33,.255),(.155,.27)]
                if flip:
                    outer=[(1-x,y) for x,y in outer]
                    inner=[(1-x,y) for x,y in inner]
                self._command_plate(out,mask,bb,outer,cmd,True,base_seed)
                self._command_plate(out,mask,bb,inner,cmd,False,base_seed+1)

            # Small clavicle guards frame the neck without closing the leather gap.
            self._command_plate(out,mask,bb,[(.30,.115),(.405,.085),(.465,.15),(.445,.225),(.335,.21)],cmd,True,528)
            self._command_plate(out,mask,bb,[(.70,.115),(.595,.085),(.535,.15),(.555,.225),(.665,.21)],cmd,True,529)

            # Three separated rib/abdominal courses.  The gaps are intentional
            # leather articulation channels taken from Wraith layered costume logic.
            for i,yy in enumerate((.48,.61,.735)):
                self._command_plate(out,mask,bb,[(.22,yy),(.345,yy-.02),(.445,yy+.015),(.415,yy+.085),(.275,yy+.095)],cmd,False,540+i)
                self._command_plate(out,mask,bb,[(.78,yy),(.655,yy-.02),(.555,yy+.015),(.585,yy+.085),(.725,yy+.095)],cmd,False,550+i)

            # Lower guards remain discrete from the ribs.
            self._command_plate(out,mask,bb,[(.18,.845),(.34,.805),(.43,.84),(.405,.96),(.245,.968),(.13,.91)],cmd,False,560)
            self._command_plate(out,mask,bb,[(.82,.845),(.66,.805),(.57,.84),(.595,.96),(.755,.968),(.87,.91)],cmd,False,561)

            # Screen-used construction cues: asymmetric lacing and hidden-snap line.
            self._lacing(
                out,
                [(int(x0+.165*w),int(y0+.42*h)),(int(x0+.18*w),int(y0+.72*h))],
                [(int(x0+.225*w),int(y0+.42*h)),(int(x0+.24*w),int(y0+.72*h))],
                7
            )
            for i in range(5):
                self._snap(out,int(x0+.79*w),int(y0+h*(.43+i*.062)),max(2,int(w*.007)))
            self._stitch(out,[(int(x0+.68*w),int(y0+.45*h)),(int(x0+.70*w),int(y0+.77*h))],max(11,.043*h))

            # Local production wear: a few purposeful rubbed cuts, never cloud noise.
            wear=Image.new("RGBA",(HI,HI),(0,0,0,0)); wd=ImageDraw.Draw(wear)
            for sx,sy,ln in ((.68,.26,.075),(.71,.31,.050),(.31,.655,.060)):
                ax=int(x0+w*sx); ay=int(y0+h*sy)
                wd.line((ax,ay,ax+int(w*ln),ay-int(h*.012)),fill=(205,207,199,92),width=max(1,int(w*.004)))
                wd.line((ax+2,ay+3,ax+int(w*ln)+2,ay-int(h*.012)+3),fill=(19,22,20,110),width=max(1,int(w*.003)))
            wear=self._clip(wear,mask); out.alpha_composite(wear)

        elif direction=="north":
            spine=self._poly_mask(bb,[(.445,.12),(.555,.12),(.55,.84),(.50,.905),(.45,.84)],1)
            spine=ImageChops.multiply(spine,mask)
            out.alpha_composite(self._reptile(spine,cmd,570))

            self._command_plate(out,mask,bb,[(.035,.095),(.18,.028),(.34,.085),(.305,.19),(.095,.22)],cmd,True,571)
            self._command_plate(out,mask,bb,[(.965,.095),(.82,.028),(.66,.085),(.695,.19),(.905,.22)],cmd,True,572)
            self._command_plate(out,mask,bb,[(.13,.19),(.33,.12),(.445,.205),(.405,.35),(.21,.355)],cmd,False,573)
            self._command_plate(out,mask,bb,[(.87,.19),(.67,.12),(.555,.205),(.595,.35),(.79,.355)],cmd,False,574)

            # Segmented grown vertebrae over the reptile-backed command vest.
            for i,yy in enumerate((.28,.41,.54,.67,.79)):
                self._command_plate(out,mask,bb,[(.455,yy-.026),(.50,yy-.052),(.545,yy-.026),(.535,yy+.040),(.465,yy+.040)],cmd,True,580+i)

            for i,yy in enumerate((.43,.575,.715)):
                self._command_plate(out,mask,bb,[(.20,yy),(.34,yy-.015),(.435,yy+.02),(.405,yy+.09),(.245,yy+.10)],cmd,False,590+i)
                self._command_plate(out,mask,bb,[(.80,yy),(.66,yy-.015),(.565,yy+.02),(.595,yy+.09),(.755,yy+.10)],cmd,False,600+i)

            self._command_plate(out,mask,bb,[(.18,.845),(.36,.805),(.43,.845),(.405,.96),(.24,.968),(.13,.91)],cmd,False,610)
            self._command_plate(out,mask,bb,[(.82,.845),(.64,.805),(.57,.845),(.595,.96),(.76,.968),(.87,.91)],cmd,False,611)

            self._lacing(
                out,
                [(int(x0+.70*w),int(y0+.41*h)),(int(x0+.69*w),int(y0+.73*h))],
                [(int(x0+.755*w),int(y0+.41*h)),(int(x0+.745*w),int(y0+.73*h))],
                7
            )
            self._stitch(out,[(int(x0+.245*w),int(y0+.39*h)),(int(x0+.225*w),int(y0+.76*h))],max(11,.044*h))

        else:
            inset=self._poly_mask(bb,[(.48,.12),(.635,.10),(.66,.76),(.585,.86),(.49,.75)],1)
            inset=ImageChops.multiply(inset,mask)
            out.alpha_composite(self._reptile(inset,cmd,620))

            self._command_plate(out,mask,bb,[(.18,.10),(.46,.03),(.73,.09),(.82,.17),(.69,.245),(.37,.25)],cmd,True,621)
            self._command_plate(out,mask,bb,[(.27,.225),(.53,.17),(.75,.27),(.70,.415),(.51,.46),(.285,.39)],cmd,False,622)

            for i,yy in enumerate((.49,.625,.755)):
                self._command_plate(out,mask,bb,[(.30,yy),(.51,yy-.025),(.70,yy+.015),(.67,yy+.095),(.39,yy+.105)],cmd,False,630+i)
            self._command_plate(out,mask,bb,[(.29,.855),(.52,.81),(.67,.845),(.63,.96),(.39,.97),(.22,.91)],cmd,False,640)

            self._lacing(
                out,
                [(int(x0+.285*w),int(y0+.40*h)),(int(x0+.30*w),int(y0+.72*h))],
                [(int(x0+.35*w),int(y0+.40*h)),(int(x0+.365*w),int(y0+.72*h))],
                7
            )
            self._stitch(out,[(int(x0+.70*w),int(y0+.32*h)),(int(x0+.715*w),int(y0+.75*h))],max(11,.045*h))

        out=self._finish(out,mask)
        out=out.resize((OUT,OUT),Image.Resampling.LANCZOS)
        post=float(self.s3.get("post_downsample_contrast",1.0))
        if abs(post-1.0)>1e-6:
            alpha=out.getchannel("A")
            out=ImageEnhance.Contrast(out).enhance(post)
            out.putalpha(alpha)
        return out

    def _tile_commander(self, mask):
        # Inventory art is the same authored command shell, composed into the
        # vanilla PowerArmor tile mask and given a restrained final readability lift.
        out=self._paint_commander("Male","south",mask)
        alpha=out.getchannel("A")
        out=ImageEnhance.Contrast(out).enhance(1.06)
        out.putalpha(alpha)
        return out

    def _paint(self, body, direction, mask):
        renderer=self.s3.get("renderer",self.profile["id"])
        if renderer in ("costume_blueprint_v2","stargate_costume_illustration_v3"):
            return self._render_costume_blueprint(body,direction,mask)
        if renderer=="wraith_warrior_carapace":
            return self._paint_warrior(body,direction,mask)
        if renderer=="wraith_commander_carapace":
            return self._paint_commander(body,direction,mask)
        if renderer=="wraith_hunter_coat":
            return self._paint_hunter(body,direction,mask)
        raise RuntimeError(f"Unknown WNG apparel renderer: {renderer}")

    def _tile(self, mask):
        renderer=self.s3.get("renderer",self.profile["id"])
        if renderer in ("costume_blueprint_v2","stargate_costume_illustration_v3"):
            return self._tile_costume_blueprint(mask)
        if renderer=="wraith_warrior_carapace":
            return self._tile_warrior(mask)
        if renderer=="wraith_commander_carapace":
            return self._tile_commander(mask)
        if renderer=="wraith_hunter_coat":
            return self._tile_hunter(mask)
        raise RuntimeError(f"Unknown WNG apparel renderer: {renderer}")

    def _symmetry(self, im):
        a=np.array(im.convert("L"),dtype=np.float32)
        b=np.fliplr(a)
        denom=max(1,float(np.mean(np.abs(a))+np.mean(np.abs(b))))
        return float(1-np.mean(np.abs(a-b))/denom)

    def _qa(self, generated: dict, impl: dict):
        q=self.s3["quality"]
        rows=[]

        if self.s3.get("renderer")=="costume_blueprint_v2":
            contract=self.s3.get("garment_contract",{})
            max_piece=float(contract.get("max_single_piece_fraction",.40))
            min_base=float(contract.get("min_base_garment_visible_fraction",.32))
            max_pieces=int(contract.get("max_semantic_pieces_per_view",14))
            min_seams=int(contract.get("min_seams_per_view",1))
            for gm in self.garment_metrics:
                if gm["max_piece_fraction"]>max_piece:
                    raise RuntimeError(f"GarmentQA oversized semantic piece {gm['body']} {gm['direction']}: {gm['max_piece_fraction']:.3f}")
                if gm["base_visible_fraction"]<min_base:
                    raise RuntimeError(f"GarmentQA base clothing lost under overlays {gm['body']} {gm['direction']}: {gm['base_visible_fraction']:.3f}")
                if gm["piece_count"]>max_pieces:
                    raise RuntimeError(f"GarmentQA too many pieces/panel-like fragmentation {gm['body']} {gm['direction']}: {gm['piece_count']}")
                if gm["seam_count"]<min_seams:
                    raise RuntimeError(f"GarmentQA lacks garment construction seams {gm['body']} {gm['direction']}")
        for key,im in generated.items():
            if key=="tile":
                vm=impl["tile"].resize((OUT,OUT),Image.Resampling.LANCZOS)
            else:
                body,d=key.split("_",1)
                if d=="west":
                    vm=impl["masks"][body]["east"].resize((OUT,OUT),Image.Resampling.LANCZOS).transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                else:
                    vm=impl["masks"][body][d].resize((OUT,OUT),Image.Resampling.LANCZOS)
            ga=np.array(im.getchannel("A"))>16; va=np.array(vm)>16
            inter=(ga&va).sum(); union=(ga|va).sum()
            iou=float(inter/max(1,union))
            rgb=np.array(im)[...,:3].astype(np.float32)
            lum=.2126*rgb[...,0]+.7152*rgb[...,1]+.0722*rgb[...,2]
            contrast=float(lum[ga].std()) if ga.any() else 0
            # Count only intentional cyan-biotech chroma. Wraith shell highlights
            # are green/grey; actual biotech nodes are blue > green > red.
            biotech=(rgb[...,2]>rgb[...,1]+4)&(rgb[...,1]>rgb[...,0]+14)&(lum>105)&ga
            lumfrac=float(biotech.sum()/max(1,ga.sum()))
            sym=self._symmetry(im)
            rows.append({"key":key,"iou":iou,"contrast":contrast,"luminous_fraction":lumfrac,"symmetry":sym})
            if key!="tile" and iou<q["silhouette_iou_min"]: raise RuntimeError(f"QA silhouette {key}: {iou}")
            if lumfrac>q["max_luminous_area_fraction"]: raise RuntimeError(f"QA luminous area {key}: {lumfrac}")
            if contrast<q["min_local_contrast"]: raise RuntimeError(f"QA contrast {key}: {contrast}")
        return rows

    def run(self, preview: Path|None, workdir: Path):
        cfg=self.profile["stage_2_rimworld"]
        outdir=APPAREL_ROOT/self.profile.get("output_dir","Wraith")
        item=self.profile["item"]
        generated={}
        tile_source=None
        for body in cfg["body_types"]:
            for d in ("south","north","east"):
                im=self._paint(body,d,self.impl["masks"][body][d])
                if body=="Male" and d=="south":
                    tile_source=im.copy()
                generated[f"{body}_{d}"]=im
                if d=="south":
                    save_clean(im,outdir/f"{item}_{body}.png")
                save_clean(im,outdir/f"{item}_{body}_{d}.png")
                if d=="east":
                    west=im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                    generated[f"{body}_west"]=west
                    save_clean(west,outdir/f"{item}_{body}_west.png")

        if tile_source is None:
            raise RuntimeError("missing fitted Male/South sprite for inventory tile")
        alpha=tile_source.getchannel("A")
        bb=alpha.getbbox()
        if not bb:
            raise RuntimeError("empty fitted Male/South sprite for inventory tile")
        crop=tile_source.crop(bb)
        scale=float(cfg.get("tile_scale",.82))
        max_side=max(1,round(OUT*scale))
        ratio=min(max_side/max(1,crop.width),max_side/max(1,crop.height))
        crop=crop.resize((max(1,round(crop.width*ratio)),max(1,round(crop.height*ratio))),Image.Resampling.LANCZOS)
        tile=Image.new("RGBA",(OUT,OUT),(0,0,0,0))
        tile.alpha_composite(crop,((OUT-crop.width)//2,(OUT-crop.height)//2))
        generated["tile"]=tile
        save_clean(tile,outdir/f"{item}.png")

        if cfg.get("legacy_bare_directions"):
            save_clean(generated["Male_south"],outdir/f"{item}_south.png")
            save_clean(generated["Male_north"],outdir/f"{item}_north.png")
            save_clean(generated["Male_east"],outdir/f"{item}_east.png")
            save_clean(generated["Male_west"],outdir/f"{item}_west.png")

        qa=self._qa(generated,self.impl)
        workdir.mkdir(parents=True,exist_ok=True)
        (workdir/"synthesis_qa.json").write_text(json.dumps(qa,indent=2))
        if self.garment_metrics:
            (workdir/"garment_construction_qa.json").write_text(json.dumps(self.garment_metrics,indent=2))
        if preview:
            order=["tile","Male_south","Male_north","Male_east","Female_south","Hulk_south"]
            sh=Image.new("RGBA",(OUT*3,OUT*2),(18,16,22,255))
            for i,k in enumerate(order):
                sh.alpha_composite(generated[k],((i%3)*OUT,(i//3)*OUT))
            preview.parent.mkdir(parents=True,exist_ok=True)
            sh.save(preview,optimize=True)

def save_clean(im: Image.Image, p: Path):
    p.parent.mkdir(parents=True,exist_ok=True)
    a=np.array(im.convert("RGBA")); a[a[...,3]==0,:3]=0
    Image.fromarray(a.astype(np.uint8),"RGBA").save(p,optimize=True)

def validate_profile(profile: dict, path: Path|None=None):
    where=str(path) if path else profile.get("id","<profile>")
    for key in ("id","item","faction","stage_1_stargate","stage_2_rimworld","stage_3_synthesis"):
        if key not in profile:
            raise RuntimeError(f"{where}: missing required profile key '{key}'")
    s1=profile["stage_1_stargate"]
    rules=s1.get("design_rules",{})
    if not s1.get("sources"):
        raise RuntimeError(f"{where}: StargateReferencePass requires sources")
    if not rules.get("material_priority") or not rules.get("construction") or not rules.get("motifs") or not rules.get("avoid"):
        raise RuntimeError(f"{where}: Stargate design brief must define materials, construction, motifs and avoid rules")

    s2=profile["stage_2_rimworld"]
    if not s2.get("vanilla_family") or not s2.get("body_types"):
        raise RuntimeError(f"{where}: RimWorldImplementationPass requires vanilla family and body types")

    s3=profile["stage_3_synthesis"]
    if s3.get("historical_continuity"):
        raise RuntimeError(
            f"{where}: historical WNG apparel is forbidden as a synthesis input; "
            "use Stargate references, vanilla RimWorld geometry and non-apparel WNG quality benchmarks only"
        )
    renderer=s3.get("renderer",profile["id"])
    if renderer in ("costume_blueprint_v2","stargate_costume_illustration_v3"):
        if s3.get("painting_mode")!="raster_brush_v3":
            raise RuntimeError(f"{where}: costume_blueprint_v2 requires painting_mode='raster_brush_v3'; flat-fill synthesis is retired")
        mats=s3.get("materials",{})
        bp=s3.get("garment_blueprint")
        if not mats or not bp:
            raise RuntimeError(f"{where}: costume_blueprint_v2 requires materials and garment_blueprint")
        if bp.get("design_class") not in ("coat","uniform","robe","dress","raiment","armoured_uniform","ceremonial"):
            raise RuntimeError(f"{where}: invalid semantic garment design_class")
        if bp.get("base_material") not in mats:
            raise RuntimeError(f"{where}: base_material is not defined in materials")
        forbidden=("panel","plate","polygon","geometric_shape","armor_panel","armour_panel")
        allowed_roles={
            "lapel","collar","yoke","vest","inset","epaulette","shoulder_layer","cuff",
            "sleeve_overlay","waistband","belt","sash","skirt","coat_tail","hem","placket",
            "corset","chest_wrap","back_insert","side_insert","bib","tunic_overlay",
            "cuirass","shoulder_guard","shin_guard","thigh_guard","gauntlet"
        }
        for d in ("south","north","east"):
            if d not in bp.get("views",{}):
                raise RuntimeError(f"{where}: garment_blueprint missing '{d}' view")
            view=bp["views"][d]
            for piece in view.get("pieces",[]):
                role=str(piece.get("role","")).lower()
                name=str(piece.get("name","")).lower()
                if not role or role not in allowed_roles:
                    raise RuntimeError(f"{where}: costume piece must use a semantic garment role, got '{role}'")
                if any(x in role or x in name for x in forbidden):
                    raise RuntimeError(f"{where}: anonymous panel/plate geometry is forbidden in costume_blueprint_v2: {piece.get('name',role)}")
                if piece.get("material") not in mats:
                    raise RuntimeError(f"{where}: piece material '{piece.get('material')}' is undefined")
                if len(piece.get("points",[]))<3:
                    raise RuntimeError(f"{where}: garment piece '{piece.get('name',role)}' needs at least three authored pattern points")
            if len(view.get("pieces",[]))>int(s3.get("garment_contract",{}).get("max_semantic_pieces_per_view",14)):
                raise RuntimeError(f"{where}: too many semantic pieces in {d}; likely panel fragmentation")
        for n,spec in mats.items():
            for k in ("shadow","mid","high","kind"):
                if k not in spec:
                    raise RuntimeError(f"{where}: material '{n}' missing '{k}'")
    return profile

def load_profile(path: Path):
    return validate_profile(json.loads(path.read_text()),path)

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("profile",nargs="?",help="profile id, e.g. wraith_hunter_coat")
    ap.add_argument("--validate-profiles-only",action="store_true")
    ap.add_argument("--vanilla-dir",type=Path)
    ap.add_argument("--body-dir",type=Path)
    ap.add_argument("--workdir",type=Path,default=Path(".github/artgen/runtime"))
    ap.add_argument("--preview",type=Path)
    args=ap.parse_args()

    if args.validate_profiles_only:
        checked=[]
        for p in sorted(PROFILE_ROOT.glob("*.json")):
            load_profile(p); checked.append(p.name)
        print(json.dumps({"status":"ok","validated_profiles":checked}))
        return
    if not args.profile:
        ap.error("profile is required unless --validate-profiles-only is used")
    if args.vanilla_dir is None:
        ap.error("--vanilla-dir is required for generation")

    profile=load_profile(PROFILE_ROOT/f"{args.profile}.json")

    # ORDER IS ENFORCED: Stargate -> RimWorld -> synthesis.
    ref=StargateReferencePass(profile,args.workdir).run()
    impl=RimWorldImplementationPass(profile,args.vanilla_dir,args.body_dir).run()
    synth=SynthesisPass(profile,ref,impl)
    synth.run(args.preview,args.workdir)

    print(json.dumps({
        "status":"ok",
        "order":["StargateReferencePass","RimWorldImplementationPass","SynthesisPass"],
        "verified_stargate_sources":f"{ref.verified}/{ref.total}"
    }))

if __name__=="__main__":
    main()
