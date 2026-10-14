from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageChops
from bs4 import BeautifulSoup
import argparse, hashlib, io, json, math, random
import numpy as np
import requests
import cairosvg
from scipy.ndimage import gaussian_filter, distance_transform_edt

ROOT = Path(__file__).resolve().parents[2]
PROFILE_ROOT = ROOT / "Tools/ArtStudio/profiles"
APPAREL_ROOT = ROOT / "Textures/Things/Pawn/Humanlike/Apparel"
OUT = 192
SCALE = 6
HI = OUT * SCALE
UA = "WNG-Apparel-Studio/1.0"

@dataclass
class ReferenceReport:
    verified: int
    total: int
    pages: list[dict]

class StargateReferencePass:
    def __init__(self, profile: dict, workdir: Path):
        self.p = profile
        self.cfg = profile["stargate"]
        self.workdir = workdir
        self.s = requests.Session()
        self.s.headers["User-Agent"] = UA

    @staticmethod
    def text(html: str) -> str:
        soup = BeautifulSoup(html, "html.parser")
        for t in soup(["script","style","noscript"]):
            t.extract()
        return " ".join(soup.stripped_strings)

    def run(self) -> ReferenceReport:
        rows=[]; verified=0
        for src in self.cfg["sources"]:
            row={"url":src["url"],"role":src["role"],"terms":{}, "ok":False}
            try:
                r=self.s.get(src["url"],timeout=20)
                row["status"]=r.status_code
                row["sha256"]=hashlib.sha256(r.content).hexdigest()
                r.raise_for_status()
                txt=self.text(r.text)
                low=txt.lower()
                for term in src.get("required_terms",[]):
                    row["terms"][term]=term.lower() in low
                row["ok"]=len(txt)>500 and (any(row["terms"].values()) if row["terms"] else True)
                if row["ok"]: verified += 1
            except Exception as e:
                row["error"]=str(e)[:250]
            rows.append(row)
        minimum=int(self.cfg.get("minimum_live_sources",3))
        if verified < minimum:
            raise RuntimeError(f"StargateReferencePass failed: {verified}/{len(rows)} live, need {minimum}")
        self.workdir.mkdir(parents=True,exist_ok=True)
        rep=ReferenceReport(verified,len(rows),rows)
        (self.workdir/"reference_report.json").write_text(json.dumps(rep.__dict__,indent=2))
        (self.workdir/"design_brief.json").write_text(json.dumps({
            "item":self.p["item"],
            "faction":self.p["faction"],
            "materials":self.cfg["design"]["materials"],
            "construction":self.cfg["design"]["construction"],
            "motifs":self.cfg["design"]["motifs"],
            "avoid":self.cfg["design"]["avoid"],
        },indent=2))
        return rep

class RimWorldGeometry:
    def __init__(self, profile: dict, vanilla_dir: Path, body_dir: Path):
        self.p=profile
        self.cfg=profile["rimworld"]
        self.vanilla_dir=vanilla_dir
        self.body_dir=body_dir

    @staticmethod
    def bbox(mask: Image.Image):
        a=np.array(mask)>8
        ys,xs=np.nonzero(a)
        if not len(xs): raise RuntimeError("empty mask")
        return xs.min(),ys.min(),xs.max()+1,ys.max()+1

    def svg_mask(self, path: Path):
        raw=cairosvg.svg2png(url=str(path),output_width=HI,output_height=HI)
        return Image.open(io.BytesIO(raw)).convert("RGBA").getchannel("A")

    def body_alpha(self, body: str, direction: str):
        p=self.body_dir/f"Naked_{body}_{direction}.png"
        if not p.exists(): raise FileNotFoundError(p)
        return Image.open(p).convert("RGBA").resize((HI,HI),Image.Resampling.LANCZOS).getchannel("A")

    def deform(self, base: Image.Image, body: str, direction: str):
        male=self.body_alpha("Male",direction)
        targ=self.body_alpha(body,direction)
        mb=self.bbox(male); tb=self.bbox(targ); ab=self.bbox(base)
        sx=(tb[2]-tb[0])/max(1,(mb[2]-mb[0]))
        sy=(tb[3]-tb[1])/max(1,(mb[3]-mb[1]))
        crop=base.crop(ab)
        crop=crop.resize((max(1,round(crop.width*sx)),max(1,round(crop.height*sy))),Image.Resampling.LANCZOS)
        mcx=(mb[0]+mb[2])/2; mcy=(mb[1]+mb[3])/2
        tcx=(tb[0]+tb[2])/2; tcy=(tb[1]+tb[3])/2
        acx=(ab[0]+ab[2])/2; acy=(ab[1]+ab[3])/2
        out=Image.new("L",(HI,HI),0)
        out.paste(crop,(round(acx+(tcx-mcx)-crop.width/2),round(acy+(tcy-mcy)-crop.height/2)))
        return out

    def run(self):
        family=self.cfg["family"]
        bases={}
        for d in ("south","north","east"):
            p=self.vanilla_dir/f"{family}_Male_{d}.svg"
            if not p.exists(): raise FileNotFoundError(p)
            bases[d]=self.svg_mask(p)
        masks={}
        for body in self.cfg["body_types"]:
            masks[body]={}
            for d in ("south","north","east"):
                m=self.deform(bases[d],body,d)
                if bool(self.cfg.get("cover_body",False)):
                    m=ImageChops.lighter(m,self.body_alpha(body,d))
                masks[body][d]=m
        src=masks["Male"]["south"]
        b=src.getbbox(); crop=src.crop(b)
        scale=float(self.cfg.get("tile_scale",.78))
        crop=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.LANCZOS)
        tile=Image.new("L",(HI,HI),0)
        tile.paste(crop,((HI-crop.width)//2,(HI-crop.height)//2))
        return {"masks":masks,"tile":tile}

def chaikin(points, passes=4):
    pts=[tuple(map(float,p)) for p in points]
    for _ in range(passes):
        out=[]
        for i,a in enumerate(pts):
            b=pts[(i+1)%len(pts)]
            out.append((.75*a[0]+.25*b[0],.75*a[1]+.25*b[1]))
            out.append((.25*a[0]+.75*b[0],.25*a[1]+.75*b[1]))
        pts=out
    return pts

class RasterStudio:
    def __init__(self, profile: dict, geometry: dict):
        self.p=profile
        self.cfg=profile["paint"]
        self.geometry=geometry
        self.seed=int(hashlib.sha256(profile["id"].encode()).hexdigest()[:8],16)

    def normpts(self, bb, points):
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        return [(x0+x*w,y0+y*h) for x,y in points]

    def region_mask(self, outer, points, feather=.7, irregularity=0.0, seed=0):
        bb=outer.getbbox()
        pts=chaikin(self.normpts(bb,points),4)
        if irregularity and pts:
            cx=sum(x for x,_ in pts)/len(pts); cy=sum(y for _,y in pts)/len(pts)
            amp=float(irregularity)*min(bb[2]-bb[0],bb[3]-bb[1])
            rng=random.Random(self.seed+seed*431)
            p1=rng.uniform(0,math.tau); p2=rng.uniform(0,math.tau)
            n=len(pts); varied=[]
            for i,(x,y) in enumerate(pts):
                dx=x-cx; dy=y-cy; mag=max(1.0,math.hypot(dx,dy))
                wave=.62*math.sin(math.tau*i/n*3+p1)+.28*math.sin(math.tau*i/n*7+p2)
                wave+=rng.uniform(-.10,.10)
                off=amp*wave
                varied.append((x+(dx/mag)*off,y+(dy/mag)*off))
            pts=varied
        m=Image.new("L",(HI,HI),0)
        ImageDraw.Draw(m).polygon([(round(x),round(y)) for x,y in pts],fill=255)
        if feather: m=m.filter(ImageFilter.GaussianBlur(feather))
        return ImageChops.multiply(m,outer)

    def curve_field(self, bb, curves):
        field=np.zeros((HI,HI),np.float32)
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        for c in curves or []:
            pts=self.normpts(bb,c["points"])
            line=Image.new("L",(HI,HI),0)
            width=max(2,round(float(c.get("width",.012))*min(w,h)))
            ImageDraw.Draw(line).line([(round(x),round(y)) for x,y in pts],fill=255,width=width,joint="curve")
            a=np.array(line,np.float32)/255
            core=gaussian_filter(a,max(.8,width*.18))
            shoulder=gaussian_filter(a,max(1.6,width*.90))
            field += float(c.get("height",c.get("strength",.6)))*(shoulder-core*.55)
        return np.clip(field,-2,2)

    def material(self, mask, name, curves=None, seed=0):
        spec=self.cfg["materials"][name]
        kind=spec["kind"]
        lo=np.array(spec["shadow"],np.float32)
        mid=np.array(spec["mid"],np.float32)
        hi=np.array(spec["high"],np.float32)
        rough=float(spec.get("roughness",.6))
        m=np.array(mask,np.float32)/255
        inside=m>.08
        if not inside.any(): return Image.new("RGBA",(HI,HI),(0,0,0,0))
        bb=mask.getbbox()
        dist=distance_transform_edt(inside)
        d95=max(2,float(np.percentile(dist[inside],95)))
        crown=np.sqrt(np.clip(dist/d95,0,1))
        authored=self.curve_field(bb,curves)
        height=crown*float(spec.get("body",.58))+authored*.30

        # restrained, paint-like material breakup: no cloud noise drives structure
        rng=np.random.default_rng(self.seed+seed*101)
        n=rng.normal(0,1,(HI,HI))
        low=gaussian_filter(n,15.0); low=(low-low.mean())/(low.std()+1e-6)
        fine=gaussian_filter(n,2.0); fine=(fine-fine.mean())/(fine.std()+1e-6)
        height += low*float(spec.get("surface_height",.012))

        gy,gx=np.gradient(gaussian_filter(height,1.4))
        ns=float(spec.get("normal_strength",2.0))
        nx=-gx*ns; ny=-gy*ns; nz=np.ones_like(nx)
        ln=np.sqrt(nx*nx+ny*ny+nz*nz)+1e-6
        nx/=ln; ny/=ln; nz/=ln
        L=np.array([-.48,-.60,.64],np.float32); L/=np.linalg.norm(L)
        diff=np.clip(nx*L[0]+ny*L[1]+nz*L[2],0,1)

        yy,xx=np.mgrid[0:HI,0:HI]
        x0,y0,x1,y1=bb; w=max(1,x1-x0); h=max(1,y1-y0)
        ly=(yy-y0)/h
        edge=np.clip(1-dist/12,0,1)
        lum=np.clip(.34+.49*diff+.11*(1-ly)-edge*.10+low*.012+fine*.004,0,1)

        rgb=np.empty((HI,HI,3),np.float32)
        lower=lum<.5
        t=np.clip(lum/.5,0,1)
        rgb[lower]=lo+(mid-lo)*t[lower,None]
        t2=np.clip((lum-.5)/.5,0,1)
        rgb[~lower]=mid+(hi-mid)*t2[~lower,None]

        half=np.clip(nz+.12*nx-.08*ny,0,1)
        power=float(spec.get("spec_power",7))
        strength=float(spec.get("spec_strength",.25))*(1-rough*.55)
        sp=(half**power)*strength
        tint=np.array(spec.get("specular_tint",[24,24,24]),np.float32)
        rgb=np.clip(rgb+sp[...,None]*tint,0,255)

        out=Image.fromarray(np.dstack([rgb.astype(np.uint8),(m*255).astype(np.uint8)]),"RGBA")
        self.paint_texture(out,mask,name,seed)
        out.putalpha(mask)
        return out

    def paint_texture(self, out, mask, name, seed):
        spec=self.cfg["materials"][name]
        kind=spec["kind"]
        bb=mask.getbbox(); x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        rng=random.Random(self.seed+seed*317)
        layer=Image.new("RGBA",(HI,HI),(0,0,0,0)); d=ImageDraw.Draw(layer)
        sh=tuple(spec["shadow"]); hi=tuple(spec["high"])

        if kind=="leather":
            for _ in range(max(16,round(w*h/(HI*HI)*160))):
                x=rng.randint(x0,x1-1); y=rng.randint(y0,y1-1)
                ln=max(5,round(w*rng.uniform(.025,.085)))
                dy=rng.randint(-4,4)
                stroke=max(2,round(SCALE*.42),round(w*.0038))
                d.line((x,y,min(x1-1,x+ln),y+dy),fill=hi+(rng.randint(20,48),),width=stroke)
                if rng.random()<.62:
                    d.line((x+2,y+2,min(x1-1,x+ln)+2,y+dy+2),fill=sh+(rng.randint(24,52),),width=stroke)

        elif kind=="reptile":
            step=max(18,round(w*.072)); row=0
            for y in range(y0-step,y1+step,round(step*.58)):
                off=step//2 if row%2 else 0
                for x in range(x0-step,x1+step,step):
                    cx=x+off+rng.randint(-2,2); cy=y+rng.randint(-2,2)
                    rx=max(4,round(step*.40)); ry=max(3,round(step*.25))
                    d.arc((cx-rx,cy-ry,cx+rx,cy+ry),190,350,fill=sh+(145,),width=max(2,round(step*.09)))
                    d.arc((cx-rx+2,cy-ry+2,cx+rx-2,cy+ry-2),15,165,fill=hi+(62,),width=max(1,round(step*.035)))
                row+=1

        elif kind=="fabric":
            # Engineered woven cloth: fine cross-weave, subtle directional nap,
            # soft compression marks. No hard panel fill or luminous outline.
            step=max(8,round(w*.026))
            for y in range(y0,y1,step):
                d.line((x0,y,x1,y),fill=hi+(rng.randint(12,28),),width=1)
            for x in range(x0,x1,step):
                d.line((x,y0,x,y1),fill=sh+(rng.randint(10,24),),width=1)
            for _ in range(max(12,round(w*h/(HI*HI)*95))):
                x=rng.randint(x0,x1-1); y=rng.randint(y0,y1-1)
                ln=max(5,round(w*rng.uniform(.018,.055)))
                dy=rng.randint(-2,2)
                d.line((x,y,min(x1-1,x+ln),y+dy),fill=hi+(rng.randint(10,24),),width=1)

        elif kind=="rubber":
            for i in range(5):
                fx=(i+1)/6
                pts=[]
                for j in range(14):
                    t=j/13
                    x=x0+w*(fx+math.sin(t*math.pi*1.3+i*.5)*.018)
                    y=y0+h*(.12+.76*t)
                    pts.append((round(x),round(y)))
                d.line(pts,fill=sh+(78,),width=max(2,round(w*.007)),joint="curve")
                d.line([(x-1,y-2) for x,y in pts],fill=hi+(34,),width=max(1,round(w*.0025)),joint="curve")
            for _ in range(3):
                y=rng.randint(round(y0+h*.20),round(y0+h*.70))
                d.arc((round(x0+w*.18),y-round(h*.03),round(x0+w*.78),y+round(h*.03)),
                      195,340,fill=(190,205,199,32),width=max(2,round(SCALE*.34),round(w*.008)))

        elif kind=="bone":
            for i in range(8):
                fx=(i+1)/9
                pts=[]
                for j in range(12):
                    t=j/11
                    x=x0+w*(fx+math.sin(t*math.pi*1.5+i*.8)*.013)
                    y=y0+h*(.08+.84*t)
                    pts.append((round(x),round(y)))
                d.line(pts,fill=sh+(76,),width=max(2,round(w*.005)),joint="curve")
                d.line([(x-1,y-1) for x,y in pts],fill=hi+(40,),width=max(1,round(w*.0025)),joint="curve")

        layer=ImageChops.multiply(layer,Image.merge("RGBA",(mask,mask,mask,mask)))
        out.alpha_composite(layer)

        # Hand-rubbed edge wear: sparse directional catches, never a continuous outline.
        dist=distance_transform_edt(np.array(mask)>16)
        ring=(dist>1)&(dist<6)
        ys,xs=np.nonzero(ring)
        if len(xs):
            wear=Image.new("RGBA",(HI,HI),(0,0,0,0))
            wd=ImageDraw.Draw(wear)
            for _ in range(min(26,max(6,len(xs)//900))):
                k=rng.randrange(len(xs)); x=int(xs[k]); y=int(ys[k])
                if rng.random()<.58:
                    wd.line((x,y,x+rng.randint(3,10),y+rng.randint(-2,3)),fill=hi+(rng.randint(18,42),),width=max(2,round(SCALE*.34)))
            out.alpha_composite(wear)

    def prop_glaze(self, out, piece, seed=0, strength=.5):
        """Broken clear-cover reflections for screen-used Wraith cast-rubber armour."""
        bb=piece.getbbox()
        if not bb: return
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        rng=random.Random(self.seed+seed*577)
        layer=Image.new("RGBA",(HI,HI),(0,0,0,0))
        wash=Image.new("L",(HI,HI),0); wd=ImageDraw.Draw(wash)
        wd.ellipse((round(x0-w*.12),round(y0-h*.16),round(x0+w*.82),round(y0+h*.75)),
                   fill=round(25*strength))
        wash=ImageChops.multiply(wash.filter(ImageFilter.GaussianBlur(max(4,round(min(w,h)*.055)))),piece)
        tint=Image.new("RGBA",(HI,HI),(160,178,172,0)); tint.putalpha(wash)
        layer.alpha_composite(tint)
        d=ImageDraw.Draw(layer)
        for i in range(3):
            yy=round(y0+h*(.20+i*.22)+rng.uniform(-.02,.02)*h)
            xa=round(x0+w*(.17+rng.uniform(-.02,.025)))
            xb=round(x0+w*(.70+rng.uniform(-.025,.04)))
            d.arc((xa,yy-round(h*.045),xb,yy+round(h*.045)),198,338,
                  fill=(219,229,225,round(17+27*strength)),
                  width=max(1,round(min(w,h)*.006)))
        out.alpha_composite(ImageChops.multiply(layer,Image.merge("RGBA",(piece,piece,piece,piece))).filter(ImageFilter.GaussianBlur(.45)))

    def hard_edge_wear(self, out, piece, material_name, seed=0):
        """Sparse rubbed/chipped prop edges; never a continuous bright outline."""
        spec=self.cfg["materials"][material_name]
        if spec["kind"] not in ("rubber","bone"): return
        a=np.array(piece)>20
        dist=distance_transform_edt(a)
        ys,xs=np.nonzero((dist>1)&(dist<8))
        if not len(xs): return
        rng=random.Random(self.seed+seed*619)
        hi=tuple(spec["high"]); sh=tuple(spec["shadow"])
        layer=Image.new("RGBA",(HI,HI),(0,0,0,0)); d=ImageDraw.Draw(layer)
        count=max(7,min(30,len(xs)//650))
        for i in range(count):
            k=rng.randrange(len(xs)); x=int(xs[k]); y=int(ys[k])
            if i%4:
                d.line((x,y,x+rng.randint(2,8),y+rng.randint(-2,2)),
                       fill=hi+(rng.randint(16,40),),width=1)
            else:
                rr=rng.choice([1,1,2])
                d.ellipse((x-rr,y-rr,x+rr,y+rr),fill=sh+(rng.randint(35,70),))
        out.alpha_composite(ImageChops.multiply(layer,Image.merge("RGBA",(piece,piece,piece,piece))))

    def contact_shadow(self, outer, piece, strength=62, radius=7, offset=(2,3)):
        sh=piece.filter(ImageFilter.GaussianBlur(radius*1.25))
        moved=Image.new("L",(HI,HI),0); moved.paste(sh,offset)
        moved=ImageChops.multiply(moved,outer)
        col=Image.new("RGBA",(HI,HI),(0,0,0,0))
        col.putalpha(moved.point(lambda v: int(v*strength/255)))
        return col

    def seam(self, out, bb, spec):
        pts=self.normpts(bb,spec["points"])
        d=ImageDraw.Draw(out)
        width=max(1,round(float(spec.get("width",.004))*min(bb[2]-bb[0],bb[3]-bb[1])))
        dark=tuple(spec.get("shadow",[5,7,6]))+(int(spec.get("alpha",150)),)
        light=tuple(spec.get("highlight",[70,78,72]))+(70,)
        d.line([(round(x),round(y)) for x,y in pts],fill=dark,width=width+1,joint="curve")
        d.line([(round(x-1),round(y-1)) for x,y in pts],fill=light,width=max(1,width//2),joint="curve")
        if spec.get("stitch"):
            step=max(10,round(float(spec.get("spacing",.035))*(bb[3]-bb[1])))
            a,b=pts[0],pts[-1]
            length=max(1,math.dist(a,b)); n=max(2,round(length/step))
            for i in range(1,n):
                t=i/n; x=a[0]*(1-t)+b[0]*t; y=a[1]*(1-t)+b[1]*t
                d.line((round(x-2),round(y),round(x+2),round(y)),fill=(115,108,98,105),width=1)

    def closures(self, out, bb, closures):
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        d=ImageDraw.Draw(out)
        for c in closures or []:
            typ=c["type"]
            if typ=="snaps":
                a,b=c["from"],c["to"]; count=c.get("count",4)
                for i in range(count):
                    t=(i+.5)/count
                    x=round(x0+(a[0]*(1-t)+b[0]*t)*w); y=round(y0+(a[1]*(1-t)+b[1]*t)*h)
                    r=max(2,round(min(w,h)*c.get("radius",.004)))
                    d.ellipse((x-r,y-r,x+r,y+r),fill=(30,32,31,235),outline=(100,101,97,120),width=1)
            elif typ=="lacing":
                l0,l1=c["left"]; r0,r1=c["right"]; pairs=c.get("pairs",5)
                for i in range(pairs):
                    t=(i+.5)/pairs
                    lx=x0+(l0[0]*(1-t)+l1[0]*t)*w; ly=y0+(l0[1]*(1-t)+l1[1]*t)*h
                    rx=x0+(r0[0]*(1-t)+r1[0]*t)*w; ry=y0+(r0[1]*(1-t)+r1[1]*t)*h
                    d.line((round(lx),round(ly),round(rx),round(ry)),fill=(7,7,8,210),width=max(1,round(w*.004)))
            elif typ=="segmented_belt":
                a,b=c.get("from",[.24,.625]),c.get("to",[.76,.625])
                count=max(3,int(c.get("count",7)))
                p0=(x0+a[0]*w,y0+a[1]*h); p1=(x0+b[0]*w,y0+b[1]*h)
                band=max(4,round(min(w,h)*c.get("width",.020)))
                d.line((round(p0[0]),round(p0[1]),round(p1[0]),round(p1[1])),
                       fill=(7,9,9,230),width=band)
                for i in range(count):
                    t=(i+.5)/count
                    x=round(p0[0]*(1-t)+p1[0]*t); y=round(p0[1]*(1-t)+p1[1]*t)
                    sw=max(5,round(abs(p1[0]-p0[0])/count*.58)); sh=max(3,round(band*.68))
                    d.rounded_rectangle((x-sw//2,y-sh//2,x+sw//2,y+sh//2),
                        radius=max(1,sh//3),fill=(23,29,26,235),outline=(77,84,79,145),width=1)
                bx=round((p0[0]+p1[0])*.5); by=round((p0[1]+p1[1])*.5); rr=max(3,round(band*.55))
                d.rounded_rectangle((bx-rr,by-rr,bx+rr,by+rr),radius=max(1,rr//3),
                    fill=(14,17,16,245),outline=(105,108,103,165),width=max(1,rr//3))
            elif typ=="self_destruct":
                px,py=c.get("position",[.50,.31])
                x=round(x0+px*w); y=round(y0+py*h); r=max(4,round(min(w,h)*c.get("radius",.018)))
                d.ellipse((x-r*1.5,y-r*1.2,x+r*1.5,y+r*1.2),fill=(5,7,7,235),outline=(44,50,48,130),width=1)
                cover=Image.new("RGBA",(HI,HI),(0,0,0,0)); cd=ImageDraw.Draw(cover)
                cd.ellipse((x-r,y-r*.8,x+r,y+r*.8),fill=(120,145,137,26),outline=(175,190,184,58),width=1)
                out.alpha_composite(cover.filter(ImageFilter.GaussianBlur(max(1,r//6))))
                cr=max(2,r//3)
                d=ImageDraw.Draw(out)
                d.ellipse((x-cr,y-cr,x+cr,y+cr),fill=(35,61,51,210),outline=(9,15,13,220),width=1)

    def wear(self, out, bb, cfg, seed):
        if not cfg: return
        x0,y0,x1,y1=bb; w=x1-x0; h=y1-y0
        rng=random.Random(self.seed+seed*509)
        d=ImageDraw.Draw(out)
        count=int(cfg.get("count",6))
        for _ in range(count):
            x=round(x0+rng.uniform(*cfg.get("x_range",[.1,.9]))*w)
            y=round(y0+rng.uniform(*cfg.get("y_range",[.15,.93]))*h)
            ln=max(4,round(w*rng.uniform(.025,.07)))
            d.line((x,y,min(x1-1,x+ln),y+rng.randint(-3,3)),fill=tuple(cfg.get("highlight",[100,105,100]))+(rng.randint(20,48),),width=1)

    def paint(self, mask, view_name, seed_base=0):
        view=self.cfg["views"][view_name]
        bb=mask.getbbox()
        out=self.material(mask,self.cfg["base_material"],view.get("folds",[]),seed_base+1)
        union=Image.new("L",(HI,HI),0)

        for i,p in enumerate(view["components"]):
            pm=self.region_mask(mask,p["contour"],float(p.get("feather",.7)),
                                float(p.get("edge_irregularity",0)),seed_base+20+i)
            union=ImageChops.lighter(union,pm)
            out.alpha_composite(self.contact_shadow(mask,pm,int(p.get("shadow",58)),int(p.get("shadow_radius",7)),tuple(p.get("shadow_offset",[2,3]))))
            layer=self.material(pm,p["material"],p.get("relief",[]),seed_base+20+i)
            out.alpha_composite(layer)
            if p.get("clear_cover"):
                self.prop_glaze(out,pm,seed_base+20+i,float(p.get("clear_cover_strength",.5)))
            self.hard_edge_wear(out,pm,p["material"],seed_base+20+i)
            if p.get("mirror"):
                mir=pm.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                out.alpha_composite(self.contact_shadow(mask,mir,int(p.get("shadow",58)),int(p.get("shadow_radius",7)),(-2,int(p.get("shadow_offset",[2,3])[1]))))
                relief=[]
                for c in p.get("relief",[]):
                    cc=dict(c); cc["points"]=[[1-x,y] for x,y in c["points"]]; relief.append(cc)
                out.alpha_composite(self.material(mir,p["material"],relief,seed_base+120+i))
                if p.get("clear_cover"):
                    self.prop_glaze(out,mir,seed_base+120+i,float(p.get("clear_cover_strength",.5)))
                self.hard_edge_wear(out,mir,p["material"],seed_base+120+i)
                union=ImageChops.lighter(union,mir)

        for s in view.get("seams",[]): self.seam(out,bb,s)
        self.closures(out,bb,view.get("closures",[]))
        self.wear(out,bb,view.get("wear"),seed_base+300)

        # Whole-sprite AO only; no bright outline.
        m=np.array(mask)>16
        dist=distance_transform_edt(m)
        a=np.array(out,np.float32)
        rgb=a[...,:3]
        edge=np.clip(1-dist/8,0,1)
        rgb=np.clip(rgb-edge[...,None]*7,0,255)
        a[...,:3]=rgb; a[...,3]=np.array(mask)
        out=Image.fromarray(a.astype(np.uint8),"RGBA")
        out.putalpha(mask)
        small=out.resize((OUT,OUT),Image.Resampling.LANCZOS)
        alpha=small.getchannel("A")
        small=ImageEnhance.Contrast(small).enhance(float(self.cfg.get("final_contrast",1.025)))
        small=small.filter(ImageFilter.UnsharpMask(
            radius=float(self.cfg.get("final_unsharp_radius",.62)),
            percent=int(self.cfg.get("final_unsharp_percent",58)),
            threshold=int(self.cfg.get("final_unsharp_threshold",2))
        ))
        small.putalpha(alpha)
        return small

    def run(self, outdir: Path, workdir: Path):
        outdir.mkdir(parents=True,exist_ok=True)
        item=self.p["item"]
        generated={}
        tile_source=None

        for body,dirs in self.geometry["masks"].items():
            for d in ("south","north","east"):
                stable=int(hashlib.sha256((body+d).encode()).hexdigest()[:6],16)%400
                im=self.paint(dirs[d],d,1000+stable)
                if body=="Male" and d=="south":
                    tile_source=im.copy()
                im.save(outdir/f"{item}_{body}_{d}.png")
                generated[f"{item}_{body}_{d}.png"]=im
                if d=="east":
                    west=im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                    west.save(outdir/f"{item}_{body}_west.png")
                    generated[f"{item}_{body}_west.png"]=west
            # compatibility body alias
            generated[f"{item}_{body}.png"]=generated[f"{item}_{body}_south.png"].copy()
            generated[f"{item}_{body}.png"].save(outdir/f"{item}_{body}.png")

        for d in ("south","north","east","west"):
            src=generated[f"{item}_Male_{d}.png"].copy()
            src.save(outdir/f"{item}_{d}.png")
            generated[f"{item}_{d}.png"]=src

        if tile_source is None:
            raise RuntimeError("missing fitted Male/South sprite for inventory tile")
        alpha=tile_source.getchannel("A")
        bb=alpha.getbbox()
        if not bb:
            raise RuntimeError("empty fitted Male/South sprite for inventory tile")
        crop=tile_source.crop(bb)
        scale=float(self.p["rimworld"].get("tile_scale",.78))
        max_side=max(1,round(OUT*scale))
        ratio=min(max_side/max(1,crop.width),max_side/max(1,crop.height))
        crop=crop.resize((max(1,round(crop.width*ratio)),max(1,round(crop.height*ratio))),Image.Resampling.LANCZOS)
        tile=Image.new("RGBA",(OUT,OUT),(0,0,0,0))
        tile.alpha_composite(crop,((OUT-crop.width)//2,(OUT-crop.height)//2))
        tile.save(outdir/f"{item}.png")
        generated[f"{item}.png"]=tile

        self.qa(generated)
        self.contact_sheet(generated,workdir/"contact-sheet.png")
        return generated

    def qa(self, generated):
        required=30
        if len(generated)!=required:
            raise RuntimeError(f"QA: expected {required} live PNGs, got {len(generated)}")
        for name,im in generated.items():
            a=np.array(im)
            m=a[...,3]>16
            if not m.any(): raise RuntimeError(f"QA empty {name}")
            rgb=a[...,:3].astype(np.float32)
            lum=.2126*rgb[...,0]+.7152*rgb[...,1]+.0722*rgb[...,2]
            vals=lum[m]
            if vals.std()<13: raise RuntimeError(f"QA flat contrast {name}: {vals.std():.1f}")
            if (vals>185).mean()>.015: raise RuntimeError(f"QA too bright/cartoon {name}: {(vals>185).mean():.3f}")
            # detect luminous sticker/ring edges
            mm=m.astype(np.uint8)
            dist=distance_transform_edt(mm)
            edge=(dist>0)&(dist<=2)
            inner=dist>5
            if edge.any() and inner.any():
                delta=float(lum[edge].mean()-lum[inner].mean())
                if delta>18: raise RuntimeError(f"QA bright edge ring {name}: {delta:.1f}")
        print(json.dumps({"status":"ok","files":len(generated)},indent=2))

    def contact_sheet(self, g, path: Path):
        names=[
            self.p["item"]+".png",
            self.p["item"]+"_Male_south.png",
            self.p["item"]+"_Male_north.png",
            self.p["item"]+"_Male_east.png",
            self.p["item"]+"_Female_south.png",
            self.p["item"]+"_Hulk_south.png",
        ]
        sheet=Image.new("RGBA",(OUT*3,OUT*2),(22,22,22,255))
        for i,n in enumerate(names):
            sheet.alpha_composite(g[n],((i%3)*OUT,(i//3)*OUT))
        path.parent.mkdir(parents=True,exist_ok=True)
        sheet.save(path)

def validate_profile(p):
    for k in ("id","item","faction","stargate","rimworld","paint"):
        if k not in p: raise RuntimeError(f"missing {k}")
    raw=json.dumps(p).lower()
    forbidden=("historical_continuity","hist_ref","historical apparel","old wng clothing")
    for x in forbidden:
        if x in raw: raise RuntimeError(f"forbidden historical apparel input marker: {x}")
    if p["paint"].get("engine")!="raster_studio_v1":
        raise RuntimeError("profile must use raster_studio_v1")
    if len(p["stargate"].get("sources",[]))<3:
        raise RuntimeError("need at least three Stargate sources")
    return p

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("profile",nargs="?")
    ap.add_argument("--vanilla-dir",type=Path)
    ap.add_argument("--body-dir",type=Path)
    ap.add_argument("--workdir",type=Path,default=Path("/tmp/wng-artstudio"))
    ap.add_argument("--validate-profiles-only",action="store_true")
    args=ap.parse_args()

    if args.validate_profiles_only:
        checked=[]
        for f in sorted(PROFILE_ROOT.glob("*.json")):
            validate_profile(json.loads(f.read_text())); checked.append(f.name)
        print(json.dumps({"status":"ok","profiles":checked},indent=2))
        return
    if not args.profile or args.vanilla_dir is None or args.body_dir is None:
        ap.error("profile, --vanilla-dir and --body-dir are required")

    p=validate_profile(json.loads((PROFILE_ROOT/f"{args.profile}.json").read_text()))
    ref=StargateReferencePass(p,args.workdir).run()
    geo=RimWorldGeometry(p,args.vanilla_dir,args.body_dir).run()
    outdir=APPAREL_ROOT/p.get("output_dir","Wraith")
    RasterStudio(p,geo).run(outdir,args.workdir)

if __name__=="__main__":
    main()
