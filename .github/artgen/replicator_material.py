from PIL import Image, ImageDraw, ImageFilter
import math, random
S=512
GLOW=(184,224,248,255); VIOLET=(184,140,245,255); AMBER=(238,183,89,255)

def block(w,h,seed=0,light=1.0,damage=1.0,corner=None):
    w=max(8,int(w)); h=max(8,int(h)); c=max(3,int(corner if corner is not None else min(w,h)//8))
    im=Image.new('RGBA',(w,h),(0,0,0,0)); mask=Image.new('L',(w,h),0); ImageDraw.Draw(mask).rounded_rectangle((2,2,w-3,h-3),radius=c,fill=255)
    tex=Image.new('RGBA',(w,h)); p=tex.load(); rnd=random.Random(seed)
    for y in range(h):
        ey=abs(y/(max(1,h-1))-.5)*2
        for x in range(w):
            ex=abs(x/(max(1,w-1))-.5)*2; edge=max(ex,ey)
            base=(88-42*edge)*light; streak=7*math.sin(x*.08+seed%9)+3*math.sin(y*.16+seed%5); grain=rnd.randint(-8,8)
            v=max(15,min(155,int(base+streak+grain))); p[x,y]=(v,min(165,v+4),min(170,v+6),255)
    tex.putalpha(mask); im.alpha_composite(tex); d=ImageDraw.Draw(im)
    d.rounded_rectangle((2,2,w-3,h-3),radius=c,outline=(11,14,16,255),width=max(3,min(w,h)//18))
    d.line((c+3,5,w-c-5,5),fill=(210,213,214,175),width=max(2,min(w,h)//28)); d.line((5,c+3,5,h-c-5),fill=(145,151,154,95),width=2)
    d.line((c+4,h-6,w-c-6,h-6),fill=(5,7,8,190),width=max(2,min(w,h)//23))
    rnd=random.Random(seed*97+31)
    for _ in range(max(2,int((w+h)/65*damage))):
        x=rnd.randint(7,max(7,w-8)); y=rnd.randint(7,max(7,h-8)); ln=rnd.randint(7,max(8,min(30,w//2)))
        col=(218,218,214,rnd.randint(35,85)) if rnd.random()<.55 else (4,6,7,rnd.randint(60,115))
        d.line((x,y,min(w-7,x+ln),y+rnd.randint(-2,2)),fill=col,width=1)
    if w>42 and h>30:
        sy=int(h*.61); d.line((10,sy,w-11,sy),fill=(12,15,16,150),width=2); d.line((11,sy+2,w-12,sy+2),fill=(136,141,142,48),width=1)
    return im

def paste(canvas,obj,center,angle=0,shadow=True):
    if angle: obj=obj.rotate(angle,Image.Resampling.BICUBIC,expand=True)
    x=int(center[0]-obj.width/2); y=int(center[1]-obj.height/2)
    if shadow:
        a=obj.getchannel('A'); sh=Image.new('RGBA',obj.size,(0,0,0,0)); sh.putalpha(a.filter(ImageFilter.GaussianBlur(5)).point(lambda q:int(q*.35))); canvas.alpha_composite(sh,(x+6,y+8))
    canvas.alpha_composite(obj,(x,y))

def node(radius=10,color=GLOW):
    s=radius*6; im=Image.new('RGBA',(s,s),(0,0,0,0)); cx=s//2
    m=Image.new('L',(s,s),0); d=ImageDraw.Draw(m); d.ellipse((cx-radius*2,cx-radius*2,cx+radius*2,cx+radius*2),fill=160)
    halo=Image.new('RGBA',(s,s),(*color[:3],0)); halo.putalpha(m.filter(ImageFilter.GaussianBlur(radius*1.25))); im.alpha_composite(halo)
    d=ImageDraw.Draw(im); d.ellipse((cx-radius-4,cx-radius-4,cx+radius+4,cx+radius+4),fill=(13,16,18,255),outline=(175,181,184,255),width=3)
    d.ellipse((cx-radius,cx-radius,cx+radius,cx+radius),fill=color); d.ellipse((cx-radius//2,cx-radius//2,cx+radius//2,cx+radius//2),fill=(243,250,255,245))
    return im

def panel(canvas,box,seed=0,light=1.0,damage=1.0):
    x0,y0,x1,y1=map(int,box); paste(canvas,block(x1-x0,y1-y0,seed,light,damage),(x0+(x1-x0)/2,y0+(y1-y0)/2),0)

def finish(im,path,target=448,size=512):
    box=im.getchannel('A').getbbox()
    if not box: raise RuntimeError(f'{path}: empty alpha')
    crop=im.crop(box); sc=min(target/crop.width,target/crop.height,1)
    if sc<1: crop=crop.resize((max(1,round(crop.width*sc)),max(1,round(crop.height*sc))),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(size,size),(0,0,0,0)); out.alpha_composite(crop,((size-crop.width)//2,(size-crop.height)//2))
    px=out.load()
    for y in range(size):
        for x in range(size):
            if px[x,y][3]==0: px[x,y]=(0,0,0,0)
    a=out.getchannel('A'); edges=[a.crop((0,0,size,1)).getextrema()[1],a.crop((0,size-1,size,size)).getextrema()[1],a.crop((0,0,1,size)).getextrema()[1],a.crop((size-1,0,size,size)).getextrema()[1]]
    if any(edges): raise RuntimeError(f'{path}: edge alpha {edges}')
    out.save(path,'PNG',optimize=True)
    with Image.open(path) as chk:
        chk.load()
        if chk.mode!='RGBA' or chk.size!=(size,size): raise RuntimeError(f'{path}: expected {size}x{size} RGBA')
