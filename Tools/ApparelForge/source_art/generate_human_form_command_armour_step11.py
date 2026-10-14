from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageChops, ImageOps
from pathlib import Path
import numpy as np, hashlib

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/"Textures/Things/Pawn/Humanlike/Apparel/Precursor"
PREFIX="WNG_HumanFormCommandArmor"
S=6
MASTER=(192*S,192*S)

BODIES={
 "Male":{"front":(42,73,151,171),"side":(55,73,139,171)},
 "Female":{"front":(44,73,148,171),"side":(57,73,136,171)},
 "Thin":{"front":(51,73,141,171),"side":(62,73,130,171)},
 "Fat":{"front":(34,73,158,171),"side":(49,73,143,171)},
 "Hulk":{"front":(30,73,162,171),"side":(46,73,146,171)},
}

def grad(size,top,bottom):
 w,h=size
 a=np.zeros((h,w,4),dtype=np.uint8)
 for y in range(h):
  t=y/max(1,h-1)
  a[y,:,:]=[int(top[i]*(1-t)+bottom[i]*t) for i in range(4)]
 return Image.fromarray(a,"RGBA")

def pts(box,rel):
 x0,y0,x1,y1=box; w=x1-x0; h=y1-y0
 return [(int((x0+x*w)*S),int((y0+y*h)*S)) for x,y in rel]

def texture(layer,mask,seed):
 rng=np.random.default_rng(seed)
 arr=np.array(layer).astype(np.float32)
 n=rng.normal(0,1,(layer.height,layer.width)).astype(np.float32)
 n=np.clip(n,-2.2,2.2)/2.2
 m=np.array(mask).astype(np.float32)/255
 arr[...,:3]+=n[...,None]*7.0*m[...,None]
 out=Image.fromarray(np.uint8(np.clip(arr,0,255)),"RGBA")
 out.putalpha(mask)
 d=ImageDraw.Draw(out,"RGBA")
 for _ in range(18):
  x=int(rng.integers(0,out.width)); y=int(rng.integers(0,out.height))
  if mask.getpixel((x,y))<48: continue
  ln=int(rng.integers(18,55))
  d.line((x,y,x+ln,y-int(rng.integers(-6,7))),fill=(255,245,215,20),width=2)
 return out

def front(box):
 c=Image.new("RGBA",MASTER,(0,0,0,0))
 m=Image.new("L",MASTER,0); d=ImageDraw.Draw(m)
 d.polygon(pts(box,[(.20,.02),(.80,.02),(.92,.16),(1,.31),(.93,.48),(.88,.63),(.92,.87),(.71,.96),(.50,.90),(.29,.96),(.08,.87),(.12,.63),(.07,.48),(0,.31),(.08,.16)]),fill=255)
 x0,y0,x1,y1=box; w=x1-x0; h=y1-y0
 d.ellipse((int((x0-.03*w)*S),int((y0+.06*h)*S),int((x0+.38*w)*S),int((y0+.37*h)*S)),fill=255)
 d.ellipse((int((x1-.38*w)*S),int((y0+.06*h)*S),int((x1+.03*w)*S),int((y0+.37*h)*S)),fill=255)
 m=m.filter(ImageFilter.GaussianBlur(3))

 base=grad(MASTER,(43,40,37,255),(16,17,18,255)); base.putalpha(m); c.alpha_composite(base)
 def poly(rel,fill,outline=(82,61,31,255),wid=6):
  z=Image.new("RGBA",MASTER,(0,0,0,0)); q=ImageDraw.Draw(z)
  p=pts(box,rel); q.polygon(p,fill=fill); q.line(p+[p[0]],fill=outline,width=wid,joint="curve"); return z

 ivory=(225,218,200,255); ivory2=(184,177,163,255); red=(126,25,29,255)
 for rel,col in [
  ([(.19,.11),(.38,.04),(.48,.17),(.42,.43),(.18,.34)],ivory),
  ([(.81,.11),(.62,.04),(.52,.17),(.58,.43),(.82,.34)],ivory),
  ([(.34,.19),(.50,.12),(.66,.19),(.61,.48),(.50,.61),(.39,.48)],(208,201,186,255)),
  ([(.14,.38),(.34,.31),(.38,.70),(.25,.83),(.12,.70)],ivory2),
  ([(.86,.38),(.66,.31),(.62,.70),(.75,.83),(.88,.70)],ivory2),
  ([(.25,.67),(.50,.58),(.75,.67),(.68,.93),(.50,.85),(.32,.93)],(201,192,174,255)),
 ]: c.alpha_composite(poly(rel,col))
 for rel in [
  [(.44,.18),(.56,.18),(.58,.49),(.50,.58),(.42,.49)],
  [(.24,.18),(.34,.12),(.39,.26),(.32,.48),(.22,.39)],
  [(.76,.18),(.66,.12),(.61,.26),(.68,.48),(.78,.39)],
  [(.37,.63),(.50,.58),(.63,.63),(.58,.85),(.50,.80),(.42,.85)]
 ]: c.alpha_composite(poly(rel,red,(86,39,29,255),5))

 gold=Image.new("RGBA",MASTER,(0,0,0,0)); g=ImageDraw.Draw(gold)
 def ln(rel,w=10,col=(221,171,77,238)): g.line(pts(box,rel),fill=col,width=w,joint="curve")
 ln([(.50,.07),(.50,.60)],12)
 ln([(.17,.16),(.35,.07),(.50,.18),(.65,.07),(.83,.16)],12)
 ln([(.19,.36),(.37,.46),(.50,.61),(.63,.46),(.81,.36)],8)
 ln([(.30,.69),(.50,.60),(.70,.69)],10)
 ln([(.34,.91),(.50,.81),(.66,.91)],7)
 ln([(.18,.14),(.11,.22),(.20,.30)],10)
 ln([(.82,.14),(.89,.22),(.80,.30)],10)
 c.alpha_composite(gold)

 glow=Image.new("RGBA",MASTER,(0,0,0,0)); q=ImageDraw.Draw(glow)
 cx=(box[0]+box[2])/2*S; yy=(box[1]+.31*(box[3]-box[1]))*S; r=15
 q.ellipse((cx-r*2,yy-r*2,cx+r*2,yy+r*2),fill=(255,72,35,90))
 glow=glow.filter(ImageFilter.GaussianBlur(18)); c.alpha_composite(glow)
 q=ImageDraw.Draw(c)
 q.polygon(pts(box,[(.475,.25),(.525,.25),(.545,.34),(.50,.39),(.455,.34)]),fill=(181,39,29,255),outline=(237,191,86,255))
 q.line(pts(box,[(.50,.27),(.50,.36)]),fill=(255,164,82,255),width=6)

 hi=Image.new("RGBA",MASTER,(0,0,0,0)); h=ImageDraw.Draw(hi)
 h.line(pts(box,[(.18,.12),(.38,.05),(.48,.17)]),fill=(255,249,232,145),width=5)
 h.line(pts(box,[(.82,.12),(.62,.05),(.52,.17)]),fill=(255,249,232,145),width=5)
 h.line(pts(box,[(.16,.39),(.33,.32),(.37,.69)]),fill=(255,244,218,95),width=4)
 h.line(pts(box,[(.84,.39),(.67,.32),(.63,.69)]),fill=(255,244,218,95),width=4)
 c.alpha_composite(hi)
 c.putalpha(ImageChops.multiply(c.getchannel("A"),m))
 return texture(c,m,11011)

def back(box):
 im=front(box)
 z=Image.new("RGBA",MASTER,(0,0,0,0)); d=ImageDraw.Draw(z)
 d.polygon(pts(box,[(.39,.14),(.61,.14),(.63,.62),(.50,.80),(.37,.62)]),fill=(188,181,167,248))
 d.polygon(pts(box,[(.46,.16),(.54,.16),(.56,.66),(.50,.76),(.44,.66)]),fill=(118,24,29,255))
 d.line(pts(box,[(.50,.16),(.50,.76)]),fill=(225,175,79,255),width=12)
 d.line(pts(box,[(.18,.17),(.34,.08),(.50,.20),(.66,.08),(.82,.17)]),fill=(227,177,83,238),width=11)
 im.alpha_composite(z)
 return im

def side(box,mirror=False):
 c=Image.new("RGBA",MASTER,(0,0,0,0)); m=Image.new("L",MASTER,0); d=ImageDraw.Draw(m)
 d.polygon(pts(box,[(.22,.03),(.72,.05),(.94,.20),(.90,.48),(.78,.69),(.82,.90),(.57,.97),(.16,.86),(.05,.59),(.08,.27)]),fill=255)
 x0,y0,x1,y1=box; w=x1-x0; h=y1-y0
 d.ellipse((int((x0+.03*w)*S),int((y0+.07*h)*S),int((x0+.58*w)*S),int((y0+.41*h)*S)),fill=255)
 m=m.filter(ImageFilter.GaussianBlur(3))
 base=grad(MASTER,(43,40,37,255),(16,17,18,255)); base.putalpha(m); c.alpha_composite(base)
 def poly(rel,fill):
  z=Image.new("RGBA",MASTER,(0,0,0,0)); q=ImageDraw.Draw(z); p=pts(box,rel)
  q.polygon(p,fill=fill); q.line(p+[p[0]],fill=(82,61,31,255),width=6,joint="curve"); return z
 c.alpha_composite(poly([(.13,.14),(.49,.04),(.73,.17),(.64,.36),(.25,.38)],(222,215,199,255)))
 c.alpha_composite(poly([(.18,.38),(.68,.32),(.78,.66),(.58,.86),(.19,.72)],(187,180,166,255)))
 c.alpha_composite(poly([(.26,.48),(.63,.42),(.66,.69),(.38,.78)],(126,25,30,255)))
 c.alpha_composite(poly([(.11,.25),(.38,.14),(.48,.28),(.34,.43),(.12,.40)],(201,193,177,255)))
 z=Image.new("RGBA",MASTER,(0,0,0,0)); q=ImageDraw.Draw(z)
 q.line(pts(box,[(.19,.18),(.48,.08),(.69,.20),(.61,.42),(.72,.62),(.49,.82)]),fill=(225,176,83,240),width=12,joint="curve")
 q.line(pts(box,[(.24,.50),(.57,.45),(.66,.57)]),fill=(224,171,78,225),width=7,joint="curve")
 c.alpha_composite(z); c.putalpha(ImageChops.multiply(c.getchannel("A"),m)); c=texture(c,m,11012)
 return ImageOps.mirror(c) if mirror else c

def ds(im): return im.resize((192,192),Image.Resampling.LANCZOS)

files=[]
male=None
for body in ["Male","Female","Thin","Fat","Hulk"]:
 f=BODIES[body]["front"]; s=BODIES[body]["side"]
 south=ds(front(f)); north=ds(back(f)); east=ds(side(s)); west=ds(side(s,True))
 if body=="Male": male=south.copy()
 for name,im in [
  (f"{PREFIX}_{body}.png",south),(f"{PREFIX}_{body}_south.png",south),
  (f"{PREFIX}_{body}_north.png",north),(f"{PREFIX}_{body}_east.png",east),(f"{PREFIX}_{body}_west.png",west)
 ]:
  p=OUT/name; im.save(p,optimize=True); files.append(p)
male.save(OUT/f"{PREFIX}.png",optimize=True); files.append(OUT/f"{PREFIX}.png")
assert len(files)==26
print("generated",len(files),"Step 11 textures")
