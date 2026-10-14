
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math, random

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Textures'/'Things'/'Pawn'/'Replicator'
OUT.mkdir(parents=True,exist_ok=True)
HI=1024
FINAL=512
RNG=random.Random(137)

EDGE=(12,16,18,255)
DEEP=(31,37,40,255)
STEEL0=(68,76,79,255)
STEEL1=(94,103,106,255)
STEEL2=(126,136,138,255)
LIGHT=(196,204,202,255)
SPEC=(232,236,232,255)
CYAN=(67,172,178,255)
CYAN_HI=(151,229,225,255)
AMBER=(183,123,61,255)
AMBER_HI=(241,188,93,255)

def module(size,tone=1,accent=None,armor=False,grooves=False,wedge=False):
    w,h=max(16,int(size[0])),max(16,int(size[1]))
    pad=max(18,int(max(w,h)*0.10))
    extra=24 if wedge else 0
    im=Image.new('RGBA',(w+2*pad+extra,h+2*pad),(0,0,0,0))
    d=ImageDraw.Draw(im)
    r=max(3,min(w,h)//10)
    d.rounded_rectangle((pad+5,pad+7,pad+w+5,pad+h+7),radius=r,fill=(0,0,0,92))
    base=[STEEL0,STEEL1,STEEL2][max(0,min(2,tone))]
    d.rounded_rectangle((pad,pad,pad+w,pad+h),radius=r,fill=EDGE)
    d.rounded_rectangle((pad+4,pad+4,pad+w-4,pad+h-4),radius=max(2,r-3),fill=base)
    d.line((pad+r,pad+5,pad+w-r,pad+5),fill=SPEC,width=3)
    d.line((pad+5,pad+r,pad+5,pad+h-r),fill=(166,175,173,230),width=2)
    d.line((pad+r,pad+h-5,pad+w-r,pad+h-5),fill=(20,25,27,250),width=5)
    d.line((pad+w-5,pad+r,pad+w-5,pad+h-r),fill=(28,34,36,245),width=4)
    if armor:
        ins=max(8,min(w,h)//7)
        d.rounded_rectangle((pad+ins,pad+ins,pad+w-ins,pad+h-ins),radius=max(2,r-3),outline=(172,181,179,215),width=3)
    if grooves:
        if w>=h:
            for off in (-h*.16,h*.16):
                y=pad+h/2+off
                d.line((pad+12,y,pad+w-12,y),fill=(42,49,51,220),width=3)
        else:
            for off in (-w*.16,w*.16):
                x=pad+w/2+off
                d.line((x,pad+12,x,pad+h-12),fill=(42,49,51,220),width=3)
    if w>48 and h>34:
        x0,x1=pad+int(w*.18),pad+int(w*.82)
        y0,y1=pad+int(h*.27),pad+int(h*.73)
        d.rounded_rectangle((x0,y0,x1,y1),radius=max(2,r//2),fill=DEEP,outline=(126,137,138,220),width=2)
        if accent:
            col,hi=(CYAN,CYAN_HI) if accent=='cyan' else (AMBER,AMBER_HI)
            cy=(y0+y1)//2
            d.rounded_rectangle((x0+9,cy-3,x1-9,cy+3),radius=2,fill=hi)
            d.ellipse(((x0+x1)//2-4,cy-4,(x0+x1)//2+4,cy+4),fill=col)
    if wedge:
        tipx=pad+w+extra-3
        d.polygon([(pad+w-5,pad+5),(tipx,pad+h/2),(pad+w-5,pad+h-5),(pad+w-20,pad+h/2)],fill=(83,92,94,255),outline=LIGHT)
        d.line((pad+w-5,pad+h/2,tipx-5,pad+h/2),fill=EDGE,width=4)
    if w>38 and h>28:
        for x,y in ((pad+10,pad+10),(pad+w-10,pad+h-10)):
            d.ellipse((x-2,y-2,x+2,y+2),fill=EDGE,outline=LIGHT,width=1)
    return im

def place(c,img,center,angle=0):
    rr=img.rotate(angle,Image.Resampling.BICUBIC,expand=True)
    c.alpha_composite(rr,(round(center[0]-rr.width/2),round(center[1]-rr.height/2)))

def chain(c,a,b,width=32,parts=2,tone=1,armor=False,grooves=False,wedge_last=False,accent_last=None):
    ax,ay=a; bx,by=b; dx,dy=bx-ax,by-ay
    L=max(1,math.hypot(dx,dy)); ux,uy=dx/L,dy/L
    ang=math.degrees(math.atan2(dy,dx))
    gap=max(1,width*.04)
    seg=(L-gap*(parts-1))/parts
    for i in range(parts):
        dist=i*(seg+gap)+seg/2
        place(c,module((seg,width),tone=tone,armor=armor,grooves=grooves,wedge=wedge_last and i==parts-1,accent=accent_last if i==parts-1 else None),(ax+ux*dist,ay+uy*dist),ang)

def joint(c,p,r,accent=False):
    x,y=p; d=ImageDraw.Draw(c)
    d.ellipse((x-r-2,y-r+3,x+r+3,y+r+7),fill=(0,0,0,85))
    d.ellipse((x-r,y-r,x+r,y+r),fill=EDGE,outline=LIGHT,width=3)
    d.ellipse((x-r*.55,y-r*.55,x+r*.55,y+r*.55),fill=CYAN if accent else (62,70,72,255),outline=CYAN_HI if accent else STEEL2,width=2)

def leg(c,hip,knee,ankle,foot,width=36,heavy=False,claw=False,accent=False):
    chain(c,hip,knee,width,2,tone=2 if heavy else 1,armor=heavy,grooves=heavy)
    joint(c,knee,int(width*.33))
    chain(c,knee,ankle,width*.86,2,tone=1,armor=heavy)
    joint(c,ankle,int(width*.27),accent=accent)
    chain(c,ankle,foot,width*.68,2,tone=0)
    vx,vy=foot[0]-ankle[0],foot[1]-ankle[1]; L=max(1,math.hypot(vx,vy)); ux,uy=vx/L,vy/L
    tip=(foot[0]+ux*(34 if claw else 20),foot[1]+uy*(34 if claw else 20))
    chain(c,foot,tip,width*.48,1,tone=2 if claw else 0,wedge_last=claw)

def mirror(left):
    return left+[tuple((HI-x,y) for x,y in pts) for pts in left]

def draw_legs(c,left,width,heavy=False,claw=False):
    specs=mirror(left)
    specs.sort(key=lambda pts:sum(y for _,y in pts)/len(pts))
    for pts in specs:
        leg(c,*pts,width=width,heavy=heavy,claw=claw)

def core(c,center,r=26,color='cyan'):
    x,y=center; col,hi=(CYAN,CYAN_HI) if color=='cyan' else (AMBER,AMBER_HI)
    glow=Image.new('L',c.size,0); gd=ImageDraw.Draw(glow)
    gd.ellipse((x-r*1.6,y-r*1.6,x+r*1.6,y+r*1.6),fill=110)
    glow=glow.filter(ImageFilter.GaussianBlur(max(6,int(r*.65))))
    lay=Image.new('RGBA',c.size,(*col[:3],0)); lay.putalpha(glow.point(lambda q:int(q*.18)))
    c.alpha_composite(lay)
    d=ImageDraw.Draw(c)
    d.ellipse((x-r,y-r,x+r,y+r),fill=EDGE,outline=LIGHT,width=3)
    d.ellipse((x-r*.62,y-r*.62,x+r*.62,y+r*.62),fill=DEEP,outline=col,width=4)
    d.ellipse((x-r*.20,y-r*.20,x+r*.20,y+r*.20),fill=hi)

def mandible(c,root,side,length=110,spread=58,width=28,heavy=False):
    x,y=root; s=1 if side>0 else -1
    elbow=(x+s*spread*.55,y+length*.48)
    tip=(x+s*spread,y+length)
    chain(c,root,elbow,width,2,tone=2 if heavy else 1,armor=heavy)
    joint(c,elbow,int(width*.30))
    chain(c,elbow,tip,width*.68,2,tone=1,wedge_last=True)
    hook=(tip[0]-s*28,tip[1]+24)
    chain(c,tip,hook,width*.45,1,tone=2,wedge_last=True)

def drone():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((448,430),(348,350),(275,320),(220,310)),((438,520),(325,515),(250,545),(205,565)),((454,612),(365,680),(300,735),(255,770))]
    draw_legs(c,left,34,False,False)
    place(c,module((190,230),1,armor=False,grooves=True),(512,520),90)
    place(c,module((154,76),2,grooves=True),(512,422),0)
    place(c,module((165,70),1,accent='cyan'),(512,505),0)
    place(c,module((142,66),0),(512,590),0)
    place(c,module((75,150),1),(428,520),90); place(c,module((75,150),1),(596,520),90)
    mandible(c,(482,620),-1,72,34,18); mandible(c,(542,620),1,72,34,18)
    core(c,(512,435),17)
    return c

def hunter():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((452,410),(345,320),(265,265),(215,230)),((438,510),(310,495),(225,520),(170,535)),((455,620),(355,700),(285,765),(235,805))]
    draw_legs(c,left,31,False,True)
    place(c,module((170,290),1,grooves=True),(512,525),90)
    place(c,module((142,70),2),(512,395),0)
    place(c,module((156,66),1,accent='cyan'),(512,468),0)
    place(c,module((145,62),0),(512,595),0)
    for s in (-1,1):
        root=(512+s*62,610); elbow=(512+s*120,670); tip=(512+s*180,735)
        chain(c,root,elbow,22,2,tone=1); joint(c,elbow,8)
        chain(c,elbow,tip,17,2,tone=2,wedge_last=True)
        chain(c,tip,(tip[0]-s*28,tip[1]+25),11,1,tone=2,wedge_last=True)
    core(c,(512,406),15)
    return c

def bulwark():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((420,390),(330,330),(265,310),(220,300)),((405,465),(300,440),(220,455),(170,465)),((405,570),(300,600),(220,635),(170,650)),((430,650),(345,720),(285,780),(245,815))]
    draw_legs(c,left,42,True,False)
    place(c,module((285,230),1,armor=True,grooves=True),(512,525),0)
    place(c,module((240,72),2,armor=True),(512,410),0)
    place(c,module((260,82),0,armor=True),(512,640),0)
    place(c,module((85,190),2,armor=True),(386,525),90)
    place(c,module((85,190),2,armor=True),(638,525),90)
    place(c,module((230,68),2,armor=True,grooves=True),(512,710),0)
    mandible(c,(455,710),-1,70,38,30,True); mandible(c,(569,710),1,70,38,30,True)
    return c

def titan():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((405,360),(305,285),(220,255),(170,240)),((385,445),(265,415),(180,430),(125,440)),((385,570),(260,610),(175,650),(120,675)),((415,665),(325,745),(255,810),(210,855))]
    draw_legs(c,left,50,True,True)
    place(c,module((300,300),1,armor=True,grooves=True),(512,520),90)
    for y,w,t in [(365,250,2),(455,280,1),(555,290,1),(655,270,0)]:
        place(c,module((w,78),t,armor=True,grooves=True,accent='cyan' if y==455 else None),(512,y),0)
    place(c,module((105,225),2,armor=True),(358,525),90)
    place(c,module((105,225),2,armor=True),(666,525),90)
    mandible(c,(450,680),-1,145,78,38,True); mandible(c,(574,680),1,145,78,38,True)
    core(c,(512,390),21)
    return c

def siegemass():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((385,350),(290,280),(215,255),(165,245)),((365,420),(250,390),(170,400),(115,410)),((360,505),(235,505),(150,530),(95,545)),((370,590),(250,625),(165,665),(110,690)),((400,665),(315,745),(250,805),(210,850))]
    draw_legs(c,left,52,True,True)
    place(c,module((365,290),1,armor=True,grooves=True),(512,525),0)
    for x in (395,512,629):
        for y,t in ((405,2),(510,1),(615,0)):
            place(c,module((105,94),t,armor=True,grooves=True,accent='cyan' if x==512 and y==510 else None),(x,y),0)
    place(c,module((335,86),2,armor=True,grooves=True),(512,710),0)
    for s in (-1,1): mandible(c,(512+s*82,715),s,145,88,40,True)
    core(c,(512,705),25,'amber')
    return c

def controller():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((430,450),(315,365),(230,335),(180,325)),((430,600),(320,690),(245,755),(205,795))]
    draw_legs(c,left,38,False,False)
    place(c,module((225,245),1,grooves=True),(512,530),90)
    place(c,module((185,78),2,accent='cyan'),(512,420),0)
    place(c,module((180,72),0),(512,635),0)
    core(c,(512,470),34)
    for sx in (-1,1):
        for yy,dy in ((450,-105),(520,-45)):
            root=(512+sx*88,yy); tip=(512+sx*170,yy+dy)
            chain(c,root,tip,17,3,tone=1,accent_last='cyan'); joint(c,tip,8,accent=True)
    chain(c,(445,360),(579,360),20,3,tone=1); core(c,(512,360),15)
    return c

def repairer():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((438,455),(325,380),(245,350),(195,340)),((438,610),(335,695),(265,755),(220,795))]
    draw_legs(c,left,34,False,False)
    place(c,module((190,225),1,grooves=True),(512,530),90)
    place(c,module((150,72),2),(512,430),0); core(c,(512,475),20)
    for s in (-1,1):
        root=(512+s*66,555); elbow=(512+s*120,620); hand=(512+s*165,675)
        chain(c,root,elbow,20,2,tone=1); joint(c,elbow,9,accent=True)
        chain(c,elbow,hand,15,2,tone=0,accent_last='cyan')
        for d in (-18,0,18): chain(c,hand,(hand[0]+s*40,hand[1]+d+16),8,1,tone=2,wedge_last=True)
    return c

def burrower():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((425,420),(315,345),(235,320),(180,315)),((405,530),(285,525),(200,555),(145,575)),((435,635),(340,720),(270,785),(220,825))]
    draw_legs(c,left,40,True,False)
    place(c,module((220,260),1,armor=True,grooves=True),(512,540),90)
    for y,w,t in [(415,205,2),(480,220,1),(600,205,0)]: place(c,module((w,65),t,armor=True,grooves=True),(512,y),0)
    place(c,module((290,58),2,armor=True,grooves=True),(512,405),0)
    mandible(c,(455,645),-1,135,70,32,True); mandible(c,(569,645),1,135,70,32,True)
    place(c,module((115,170),2,armor=True,grooves=True,wedge=True),(512,735),90)
    return c

def artillery():
    c=Image.new('RGBA',(HI,HI),(0,0,0,0))
    left=[((415,395),(320,325),(245,300),(195,290)),((395,470),(275,445),(190,455),(135,465)),((395,575),(275,610),(190,645),(135,665)),((425,650),(335,730),(265,795),(220,835))]
    draw_legs(c,left,42,True,False)
    place(c,module((255,255),1,armor=True,grooves=True),(512,535),90)
    place(c,module((220,76),2,armor=True),(512,420),0)
    place(c,module((230,74),0,armor=True),(512,650),0)
    place(c,module((90,180),2,armor=True),(380,535),90); place(c,module((90,180),2,armor=True),(644,535),90)
    core(c,(512,515),20,'amber')
    for sx in (-1,1):
        x=512+sx*42
        chain(c,(x,620),(x,745),29,3,tone=1,armor=True,grooves=True)
        chain(c,(x,745),(x,815),22,2,tone=2,accent_last='amber')
        core(c,(x,830),12,'amber')
    place(c,module((155,40),1,armor=True,accent='amber'),(512,705),0)
    return c

ROLES={'Drone':drone,'Hunter':hunter,'Bulwark':bulwark,'Titan':titan,'SiegeMass':siegemass,'Controller':controller,'Repairer':repairer,'Burrower':burrower,'Artillery':artillery}
TARGET={'Drone':400,'Hunter':416,'Bulwark':430,'Titan':442,'SiegeMass':448,'Controller':415,'Repairer':405,'Burrower':422,'Artillery':435}

def finish(img,target):
    box=img.getchannel('A').getbbox()
    if not box: raise RuntimeError('empty alpha')
    crop=img.crop(box); sc=min(target/crop.width,target/crop.height)
    crop=crop.resize((round(crop.width*sc),round(crop.height*sc)),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(FINAL,FINAL),(0,0,0,0)); out.alpha_composite(crop,((FINAL-crop.width)//2,(FINAL-crop.height)//2))
    px=out.load()
    for y in range(FINAL):
        for x in range(FINAL):
            if px[x,y][3]==0: px[x,y]=(0,0,0,0)
    return out

for role,fn in ROLES.items():
    south=finish(fn(),TARGET[role])
    views={'south':south,'north':south.transpose(Image.Transpose.ROTATE_180),'east':south.transpose(Image.Transpose.ROTATE_90),'west':south.transpose(Image.Transpose.ROTATE_270)}
    south.save(OUT/f'WNG_Replicator{role}.png','PNG',optimize=True)
    for d,im in views.items(): im.save(OUT/f'WNG_Replicator{role}_{d}.png','PNG',optimize=True)
    for suffix in ['', '_north','_east','_south','_west']:
        p=OUT/f'WNG_Replicator{role}{suffix}.png'
        with Image.open(p) as chk:
            chk.load()
            if chk.mode!='RGBA' or chk.size!=(512,512): raise RuntimeError(p)
            a=chk.getchannel('A')
            if not a.getbbox(): raise RuntimeError('empty '+str(p))
            if any([a.crop((0,0,512,1)).getextrema()[1],a.crop((0,511,512,512)).getextrema()[1],a.crop((0,0,1,512)).getextrema()[1],a.crop((511,0,512,512)).getextrema()[1]]): raise RuntimeError('edge alpha '+str(p))
print('Generated 45 differentiated Replicator pawn sprites.')
