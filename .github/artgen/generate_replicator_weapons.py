from pathlib import Path
from PIL import Image,ImageDraw
from replicator_material import block,paste,node,finish,GLOW,VIOLET
ROOT=Path(__file__).resolve().parents[2]; WEAP=ROOT/'Textures/Things/Item/Weapon/Replicator'; PROJ=ROOT/'Textures/Things/Projectile'; WEAP.mkdir(parents=True,exist_ok=True); PROJ.mkdir(parents=True,exist_ok=True); S=512

def root(c,cx=126,cy=256,seed=10):
    for i,(x,y,a,w,h) in enumerate([(cx-30,cy-58,-17,95,34),(cx-32,cy+58,17,95,34),(cx+8,cy-26,-5,83,31),(cx+8,cy+27,5,83,31)]): paste(c,block(w,h,seed+i,light=.78,damage=1.2),(x,y),a)
    paste(c,block(90,96,seed+8,light=.82,damage=1.3),(cx,cy),0); paste(c,node(7),(cx+2,cy),0,False)

def pulse():
    c=Image.new('RGBA',(S,S),(0,0,0,0)); root(c,118,256,100)
    paste(c,block(145,82,110,light=.86,damage=1.1),(224,256),0); paste(c,block(124,58,111,light=.9,damage=1.0),(332,256),0)
    for y in (208,304): paste(c,block(198,28,112+y,light=.72,damage=1.35),(248,y),0)
    d=ImageDraw.Draw(c); d.polygon([(382,229),(435,244),(456,256),(435,268),(382,283)],fill=(10,13,15,255)); d.polygon([(389,237),(431,247),(445,256),(431,265),(389,275)],fill=(65,70,73,255))
    paste(c,node(10),(448,256),0,False); return c

def artillery():
    c=Image.new('RGBA',(S,S),(0,0,0,0)); root(c,104,256,200); paste(c,block(126,98,215,light=.78,damage=1.35),(190,256),0)
    for i,y in enumerate((220,292)):
        paste(c,block(196,38,230+i,light=.9,damage=1.05),(328,y),0); paste(c,block(82,29,240+i,light=.78,damage=1.25),(430,y),0); paste(c,node(7),(469,y),0,False)
    paste(c,block(210,29,250,light=.69,damage=1.35),(305,256),0); return c

def disruptor():
    c=Image.new('RGBA',(S,S),(0,0,0,0)); root(c,118,256,300); paste(c,block(132,72,315,light=.84,damage=1.0),(214,256),0)
    paste(c,block(142,35,316,light=.85,damage=1.1),(331,215),-17); paste(c,block(142,35,317,light=.85,damage=1.1),(331,297),17)
    paste(c,node(26,VIOLET),(402,256),0,False); paste(c,node(10,GLOW),(402,256),0,False); return c

def projectile():
    import math
    c=Image.new('RGBA',(S,S),(0,0,0,0)); paste(c,node(45,VIOLET),(256,256),0,False); paste(c,node(23,GLOW),(256,256),0,False); d=ImageDraw.Draw(c)
    for a in (0,90,180,270):
        x=256+95*math.cos(math.radians(a)); y=256+95*math.sin(math.radians(a)); d.polygon([(x-12,y-7),(x+12,y),(x-12,y+7)],fill=(102,108,111,220))
    return c

finish(pulse(),WEAP/'WNG_ReplicatorPulseCaster.png'); finish(artillery(),WEAP/'WNG_ReplicatorArtilleryCaster.png'); finish(disruptor(),WEAP/'WNG_ReplicatorShieldDisruptor.png'); finish(projectile(),PROJ/'WNG_ReplicatorShieldDisruptor.png',390)
print('Generated professional Replicator weapon and projectile art.')
