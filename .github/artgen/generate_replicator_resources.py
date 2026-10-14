from pathlib import Path
from PIL import Image, ImageDraw
from replicator_material import block,paste,node,finish,GLOW
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Textures/Things/Item/Resource/Replicator'; OUT.mkdir(parents=True,exist_ok=True); S=512

def matter():
    c=Image.new('RGBA',(S,S),(0,0,0,0))
    specs=[(190,150,94,46,-19,.94),(282,151,112,48,15,.98),(354,185,84,42,-9,.92),(133,211,106,50,24,.92),(236,208,124,50,-7,1.02),(330,235,105,48,19,.97),(168,273,96,48,-18,.98),(267,273,126,52,13,1.05),(370,287,82,43,-23,.90),(116,319,88,43,11,.88),(210,337,110,48,-9,.97),(307,344,113,50,19,1.0),(394,345,75,39,-6,.86),(163,383,80,40,28,.86),(267,397,89,43,-25,.90)]
    for i,(x,y,w,h,a,s) in enumerate(specs): paste(c,block(w,h,100+i,light=.82+.04*(i%3),damage=1.25),(x,y),a)
    for i,(x,y) in enumerate([(110,259),(399,244),(119,360),(390,389),(237,122)]): paste(c,block(46,31,300+i,light=.9,damage=1.0),(x,y),(-18+9*i))
    return c

def core():
    c=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(c)
    paste(c,node(24),(256,256),0,False)
    for i,a in enumerate(range(0,360,45)):
        import math
        r=82; x=256+math.cos(math.radians(a))*r; y=256+math.sin(math.radians(a))*r
        broken=i in (1,4,6); b=block(94 if a%90==0 else 80,32,500+i,light=.86,damage=1.5)
        paste(c,b,(x,y),a+90)
        if broken:
            d=ImageDraw.Draw(c); d.line((x-18,y-16,x+16,y+13),fill=(5,7,8,230),width=5)
    for i,(x,y,w,h,a) in enumerate([(182,160,92,48,-5),(289,153,87,45,9),(354,190,68,38,18),(151,226,78,43,-4),(360,235,63,36,-15),(146,313,82,44,7),(362,315,63,37,21),(197,366,94,47,-8),(294,372,91,46,8),(349,356,59,35,-21)]):
        paste(c,block(w,h,610+i,light=.78+.05*(i%3),damage=1.6),(x,y),a)
    return c

finish(matter(),OUT/'WNG_ReplicatorMatter.png'); finish(core(),OUT/'WNG_ReplicatorCoreFragment.png')
print('Generated professional Replicator matter and fractured core art.')
