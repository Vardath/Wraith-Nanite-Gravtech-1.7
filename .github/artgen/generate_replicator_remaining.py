from pathlib import Path
from PIL import Image,ImageDraw,ImageFilter
import math,random

ROOT=Path(__file__).resolve().parents[2]
CONT=ROOT/'Textures/Things/Building/Replicator/Containment'; CONT.mkdir(parents=True,exist_ok=True)
RUIN=ROOT/'Textures/Things/Building/Replicator/Ruins'; RUIN.mkdir(parents=True,exist_ok=True)
ADAPT=ROOT/'Textures/Things/Pawn/Replicator/Adaptation'; ADAPT.mkdir(parents=True,exist_ok=True)
UI=ROOT/'Textures/UI/WNG/Build'; UI.mkdir(parents=True,exist_ok=True)
S=512
MET=(82,93,95,255); MET2=(122,134,135,255); DARK=(24,31,33,255); EDGE=(9,14,15,255); HI=(176,189,187,255)
CY=(48,220,199,255); CY2=(116,255,232,255); AMB=(236,170,65,255)

def glow(base,box,color=CY,blur=10,a=130,ellipse=False):
    m=Image.new('L',base.size,0); d=ImageDraw.Draw(m)
    if ellipse:d.ellipse(box,fill=255)
    else:d.rounded_rectangle(box,radius=max(2,int((box[3]-box[1])/2)),fill=255)
    b=m.filter(ImageFilter.GaussianBlur(blur)); l=Image.new('RGBA',base.size,(*color[:3],a)); l.putalpha(b.point(lambda x:int(x*a/255))); base.alpha_composite(l)

def bevel(d,box,r=10,fill=MET):
    x0,y0,x1,y1=map(int,box); d.rounded_rectangle((x0,y0,x1,y1),radius=r,fill=EDGE); d.rounded_rectangle((x0+4,y0+4,x1-4,y1-4),radius=max(3,r-3),fill=fill)
    d.line((x0+r,y0+5,x1-r,y0+5),fill=HI,width=3); d.line((x0+r,y1-5,x1-r,y1-5),fill=(18,24,26,255),width=4)

def finish(im,path,target=440,size=512):
    box=im.getchannel('A').getbbox()
    if not box:raise RuntimeError(path)
    crop=im.crop(box); sc=target/max(crop.width,crop.height)
    if abs(sc-1)>.01:crop=crop.resize((round(crop.width*sc),round(crop.height*sc)),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(size,size),(0,0,0,0)); out.alpha_composite(crop,((size-crop.width)//2,(size-crop.height)//2)); px=out.load()
    for y in range(size):
        for x in range(size):
            if px[x,y][3]==0:px[x,y]=(0,0,0,0)
    a=out.getchannel('A'); edges=[a.crop((0,0,size,1)).getextrema()[1],a.crop((0,size-1,size,size)).getextrema()[1],a.crop((0,0,1,size)).getextrema()[1],a.crop((size-1,0,size,size)).getextrema()[1]]
    if any(edges):raise RuntimeError((path,edges))
    out.save(path,'PNG',optimize=True); chk=Image.open(path); chk.load()
    if chk.mode!='RGBA' or chk.size!=(size,size):raise RuntimeError(path)

def projector(direction='north'):
    c=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(c)
    outer=[(156,116),(356,116),(396,156),(396,356),(356,396),(156,396),(116,356),(116,156)]; inner=[(172,130),(340,130),(382,172),(382,340),(340,382),(172,382),(130,340),(130,172)]
    d.polygon(outer,fill=EDGE); d.polygon(inner,fill=(58,68,70,255)); d.line(inner+[inner[0]],fill=HI,width=3)
    for x in [178,240,302]:
        for y in [178,240,302]:bevel(d,(x-26,y-22,x+26,y+22),6,DARK)
    for x,y in [(163,163),(349,163),(163,349),(349,349)]:
        bevel(d,(x-31,y-31,x+31,y+31),9,MET2); d.rounded_rectangle((x-18,y-18,x+18,y+18),radius=5,fill=DARK,outline=(120,132,132,255),width=2); glow(c,(x-8,y-8,x+8,y+8),CY,7,110,True); ImageDraw.Draw(c).ellipse((x-5,y-5,x+5,y+5),fill=CY2)
    glow(c,(190,190,322,322),CY,20,100,True); d=ImageDraw.Draw(c); d.ellipse((194,194,318,318),fill=EDGE,outline=HI,width=4); d.ellipse((210,210,302,302),fill=(18,26,28,255),outline=CY,width=8); d.ellipse((236,236,276,276),fill=(39,49,51,255),outline=CY2,width=3)
    bevel(d,(205,342,307,386),8,MET); d.rounded_rectangle((222,352,290,375),radius=4,fill=DARK); glow(c,(237,359,275,367),CY,5,80); ImageDraw.Draw(c).rounded_rectangle((241,360,271,366),radius=2,fill=CY)
    ang={'north':180,'east':90,'south':0,'west':270}[direction]
    return c.rotate(ang,Image.Resampling.BICUBIC,expand=False) if ang else c

def wall_husk():
    c=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(c); rng=random.Random(4)
    pts=[(92,206),(420,206),(432,231),(414,304),(380,318),(326,298),(286,322),(224,305),(180,323),(122,302),(82,268)]; d.polygon(pts,fill=(53,58,58,255),outline=EDGE); d.line([(105,219),(403,219)],fill=(160,164,160,255),width=4); d.line([(102,292),(391,300)],fill=(25,29,29,255),width=5)
    for x in range(122,396,44):
        h=rng.choice([20,26,32]); d.rectangle((x,201,x+22,201+h),fill=(0,0,0,0)); d.rectangle((x+5,295-h,x+27,314),fill=(0,0,0,0))
    for x in range(135,390,48):d.line((x,239,x+18,282),fill=(112,115,111,255),width=5)
    return c

def machine_husk():
    c=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(c); bevel(d,(105,112,407,400),24,(64,69,69,255)); d.rounded_rectangle((151,155,361,355),radius=17,fill=(0,0,0,0),outline=(18,22,23,255),width=12)
    for y in [173,337]:d.rounded_rectangle((150,y-10,362,y+10),radius=5,fill=(118,122,118,255),outline=EDGE,width=2)
    for x in [170,342]:d.rounded_rectangle((x-10,160,x+10,350),radius=5,fill=(100,105,103,255),outline=EDGE,width=2)
    for x in [205,256,307]:
        for y in [205,256,307]:d.rounded_rectangle((x-13,y-10,x+13,y+10),radius=3,fill=(15,18,19,255),outline=(100,105,104,255),width=2)
    d.polygon([(352,181),(407,159),(407,289),(374,278),(361,239)],fill=(0,0,0,0)); d.line([(353,183),(374,215),(361,239),(374,278)],fill=(126,129,124,255),width=4); return c

def scar():
    c=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(c); rng=random.Random(11); d.ellipse((66,171,446,344),fill=(50,50,47,105),outline=(84,84,80,150),width=3)
    for x,y,a in [(125,234,-12),(171,278,7),(219,213,16),(269,290,-8),(316,224,-18),(365,274,12),(407,231,-7)]:
        im=Image.new('RGBA',(74,44),(0,0,0,0)); dd=ImageDraw.Draw(im); dd.rounded_rectangle((10,10,64,34),radius=4,fill=(24,27,27,220),outline=(100,102,98,230),width=2); im=im.rotate(a,Image.Resampling.BICUBIC,expand=True); c.alpha_composite(im,(int(x-im.width/2),int(y-im.height/2)))
    d=ImageDraw.Draw(c)
    for _ in range(30):
        x=rng.randint(94,418); y=rng.randint(195,326); r=rng.randint(2,5); d.rectangle((x-r,y-r,x+r,y+r),fill=rng.choice([(95,95,90,230),(62,64,61,230),(125,121,111,220)]))
    d.arc((86,182,425,334),15,170,fill=(138,139,132,170),width=4); return c

def adaptation(kind):
    c=Image.new('RGBA',(256,256),(0,0,0,0)); d=ImageDraw.Draw(c); d.rounded_rectangle((31,31,225,225),radius=38,fill=EDGE); d.rounded_rectangle((39,39,217,217),radius=33,fill=(57,66,68,255),outline=HI,width=3)
    for x,y in [(63,63),(193,63),(63,193),(193,193)]:d.rounded_rectangle((x-18,y-12,x+18,y+12),radius=5,fill=MET,outline=EDGE,width=2)
    if kind=='Armor':
        d.polygon([(128,62),(187,88),(176,162),(128,202),(80,162),(69,88)],fill=(108,119,120,255),outline=HI); d.polygon([(128,78),(169,96),(160,151),(128,178),(96,151),(87,96)],fill=DARK,outline=(145,155,155,255)); d.line((128,80,128,177),fill=CY,width=5)
    elif kind=='Grav':
        glow(c,(67,67,189,189),CY,14,90,True); d=ImageDraw.Draw(c); d.ellipse((74,74,182,182),fill=DARK,outline=CY,width=8); d.ellipse((105,105,151,151),fill=(58,68,70,255),outline=CY2,width=4); d.polygon([(128,73),(113,96),(143,96)],fill=CY2); d.polygon([(128,183),(113,160),(143,160)],fill=CY2)
    elif kind=='Power':
        glow(c,(80,80,176,176),AMB,14,110,True); d=ImageDraw.Draw(c); d.ellipse((86,86,170,170),fill=DARK,outline=AMB,width=7); d.polygon([(137,72),(106,130),(129,130),(117,184),(154,117),(132,117)],fill=(255,211,94,255))
    elif kind=='Ranged':
        bevel(d,(61,101,171,155),10,MET); d.polygon([(164,104),(209,119),(225,128),(209,137),(164,152)],fill=EDGE); d.polygon([(170,113),(203,122),(214,128),(203,134),(170,143)],fill=(55,65,67,255)); glow(c,(198,118,228,138),CY,9,100); ImageDraw.Draw(c).ellipse((207,123,221,137),fill=CY2)
    elif kind=='Shield':
        glow(c,(61,61,195,195),CY,17,95,True); d=ImageDraw.Draw(c); d.ellipse((68,68,188,188),fill=(20,27,29,255),outline=CY2,width=8); d.ellipse((91,91,165,165),fill=(52,62,64,255),outline=(145,155,155,255),width=3); d.arc((76,76,180,180),205,330,fill=(255,255,255,220),width=5)
    return c

for dr in ['north','east','south','west']:finish(projector(dr),CONT/f'WNG_ReplicatorContainmentProjector_{dr}.png')
finish(projector('north'),CONT/'WNG_ReplicatorContainmentProjector.png'); finish(projector('south'),UI/'WNG_ReplicatorContainmentProjector.png',210,256)
finish(wall_husk(),RUIN/'WNG_ReplicatorStrippedWallHusk.png',430); finish(machine_husk(),RUIN/'WNG_ReplicatorScouredMachineHusk.png',440); finish(scar(),RUIN/'WNG_ReplicatorConsumptionScar.png',450)
for k in ['Armor','Grav','Power','Ranged','Shield']:finish(adaptation(k),ADAPT/f'WNG_ReplicatorAdapt_{k}.png',210,256)
print('Generated remaining Replicator containment, ruins, adaptation and build-icon art.')
