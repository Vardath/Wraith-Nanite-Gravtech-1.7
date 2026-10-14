from PIL import Image,ImageDraw,ImageFilter
from pathlib import Path

ROOT=Path("Textures/UI/WNG")
for p in (ROOT/"Abilities",ROOT/"Genes",ROOT/"Xenotypes"): p.mkdir(parents=True,exist_ok=True)
S=512

def C(): return Image.new("RGBA",(S,S),(0,0,0,0))
def glow(im,fn,col,blur=12,a=150):
    g=C(); d=ImageDraw.Draw(g); fn(d,col+(a,)); g=g.filter(ImageFilter.GaussianBlur(blur)); im.alpha_composite(g)
def ring(d,col=(110,150,175,220),r=198,w=10,gaps=False):
    if gaps:
        for a0,a1 in [(18,72),(108,162),(198,252),(288,342)]: d.arc((256-r,256-r,256+r,256+r),a0,a1,fill=col,width=w)
    else: d.ellipse((256-r,256-r,256+r,256+r),outline=col,width=w)
def save(rel,im):
    p=ROOT/rel; p.parent.mkdir(parents=True,exist_ok=True)
    out=im.resize((256,256),Image.Resampling.LANCZOS)
    out.save(p,optimize=True)
    a=out.getchannel("A")
    assert a.getbbox() and a.getextrema()[0]==0,p

def ancient_base():
    im=C(); ring(ImageDraw.Draw(im),(90,142,168,190),190,8,True); return im
def wraith_base():
    im=C(); ring(ImageDraw.Draw(im),(91,55,96,180),190,8,True); return im
def nanite_base():
    im=C(); ring(ImageDraw.Draw(im),(105,150,170,180),186,7,True); return im

def head(d,x=256,y=268,scale=1,col=(25,24,31,255),outline=(127,90,135,220)):
    d.ellipse((x-70*scale,y-112*scale,x+70*scale,y+28*scale),fill=col,outline=outline,width=max(3,int(6*scale)))
    d.polygon([(x-48*scale,y+10*scale),(x+48*scale,y+10*scale),(x+76*scale,y+90*scale),(x-76*scale,y+90*scale)],fill=col,outline=outline)

def wraith_face(im,mode):
    d=ImageDraw.Draw(im); head(d)
    green=(90,236,158); purple=(167,110,224)
    for off in (-52,-26,0,26,52): d.arc((150+off//2,120,362+off//2,315),205,335,fill=purple+(200,),width=7)
    d.line((256,155,256,275),fill=green+(255,),width=9)
    if mode=="beckon":
        for r in (125,160): d.arc((256-r,256-r,256+r,256+r),205,335,fill=green+(220,),width=10)
        for x in (115,397): d.polygon([(x,256),(x+(-25 if x>256 else 25),240),(x+(-25 if x>256 else 25),272)],fill=green+(255,))
    elif mode=="burden":
        for y in (315,350,385): d.line((180,y,332,y),fill=purple+(230,),width=11)
        d.polygon([(256,425),(225,390),(287,390)],fill=green+(255,))
    elif mode=="focus":
        for r in (42,72,102): d.arc((256-r,256-r,256+r,256+r),200,340,fill=green+(220,),width=7)
        d.ellipse((246,246,266,266),fill=(210,255,230,255))
    elif mode=="mist":
        for cx,cy,r in [(190,345,46),(250,330,58),(320,350,48),(225,380,42),(292,385,45)]: d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(101,121,131,120),outline=(150,190,198,150),width=4)
    return im

def asuran_lattice():
    im=ancient_base(); d=ImageDraw.Draw(im); cyan=(55,222,255)
    pts=[(256,92),(360,180),(330,330),(256,410),(182,330),(152,180)]
    d.polygon(pts,fill=(36,49,60,255),outline=(173,202,217,255))
    for a,b in [((256,110),(256,390)),((170,190),(330,330)),((342,190),(182,330))]: d.line((a,b),fill=cyan+(235,),width=8)
    glow(im,lambda gd,c:gd.ellipse((220,220,292,292),fill=c),cyan,14,150)
    d=ImageDraw.Draw(im); d.ellipse((232,232,280,280),fill=(193,253,255,255))
    return im

def neural():
    im=ancient_base(); d=ImageDraw.Draw(im); cyan=(58,221,255)
    d.polygon([(256,90),(300,160),(286,230),(356,176),(392,220),(326,294),(302,402),(256,432),(210,402),(186,294),(120,220),(156,176),(226,230),(212,160)],fill=(42,53,64,255),outline=(184,202,213,255))
    glow(im,lambda gd,c:gd.ellipse((212,212,300,300),fill=c),cyan,15,150)
    d=ImageDraw.Draw(im); d.ellipse((228,228,284,284),fill=(83,231,255,255))
    return im

def collective():
    im=nanite_base(); d=ImageDraw.Draw(im); cyan=(55,218,255,255)
    nodes=[(256,150),(160,250),(352,250),(210,354),(302,354)]
    for a,b in [(0,1),(0,2),(1,3),(2,4),(3,4),(1,2)]: d.line((nodes[a],nodes[b]),fill=cyan,width=9)
    for x,y in nodes:
        d.ellipse((x-28,y-28,x+28,y+28),fill=(42,57,67,255),outline=(185,235,245,255),width=6)
        d.ellipse((x-10,y-10,x+10,y+10),fill=(121,245,255,255))
    return im

def nanite_crystal(kind):
    im=nanite_base(); d=ImageDraw.Draw(im); cyan=(55,218,255,255)
    if kind=="body":
        pts=[(256,88),(340,170),(322,310),(256,420),(190,310),(172,170)]
        d.polygon(pts,fill=(55,67,78,255),outline=(203,218,226,255)); d.line((256,110,256,398),fill=cyan,width=10)
    elif kind=="precursor":
        pts=[(256,90),(392,210),(350,380),(256,430),(162,380),(120,210)]
        d.polygon(pts,fill=(67,78,88,255),outline=(222,230,235,255)); d.polygon([(256,142),(328,224),(306,330),(256,370),(206,330),(184,224)],fill=(23,97,126,255),outline=(109,237,255,255))
    elif kind=="reconstruction":
        for pts in [[(256,96),(322,218),(190,218)],[(150,300),(270,250),(220,390)],[(362,300),(242,250),(292,390)]]: d.polygon(pts,fill=(56,70,80,255),outline=(195,214,223,255))
        glow(im,lambda gd,c:gd.polygon([(256,178),(326,310),(186,310)],fill=c),(55,218,255),14,130)
    elif kind=="reserve":
        d.rounded_rectangle((190,98,322,414),radius=46,fill=(53,66,77,255),outline=(203,217,225,255),width=10)
        d.rectangle((212,156,300,354),fill=(20,92,119,255)); glow(im,lambda gd,c:gd.rectangle((220,170,292,344),fill=c),(55,218,255),18,110)
    return im

def hybrid():
    im=C(); d=ImageDraw.Draw(im); ring(d,(125,100,150,180),190,8,True)
    d.polygon([(256,95),(350,180),(326,360),(256,418)],fill=(43,54,64,255),outline=(190,208,218,255)); d.line((256,115,256,390),fill=(57,222,255,255),width=8)
    for r in (70,105,140): d.arc((256-r,256-r,256+r,256+r),115,245,fill=(93,212,145,220),width=7)
    d.line((256,145,220,250,256,360),fill=(164,103,190,230),width=8)
    return im

def wraith_gene(kind):
    im=wraith_base(); d=ImageDraw.Draw(im); green=(82,225,142,255)
    if kind in ("hemogenic","lifeforce"):
        body=[(256,105),(340,170),(360,300),(305,405),(256,438),(207,405),(152,300),(172,170)]
        d.polygon(body,fill=(59,31,50,255),outline=(125,72,116,255)); glow(im,lambda gd,c:gd.ellipse((205,180,307,330),fill=c),(82,225,142),20,150)
        d=ImageDraw.Draw(im); d.ellipse((224,200,288,312),fill=(70,183,112,255),outline=(159,246,191,255),width=6)
        if kind=="lifeforce":
            for r in (112,145): d.arc((256-r,256-r,256+r,256+r),300,60,fill=green,width=8)
    elif kind=="predator":
        d.polygon([(120,180),(210,145),(256,225),(302,145),(392,180),(330,260),(360,355),(280,320),(256,410),(232,320),(152,355),(182,260)],fill=(59,31,50,255),outline=(136,79,130,255)); d.ellipse((225,230,287,292),fill=(59,197,117,255),outline=(190,255,216,255),width=6)
    elif kind=="other":
        for cx,cy,r in [(195,205,58),(315,205,58),(255,315,68)]:
            d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(69,35,61,255),outline=(142,80,137,255),width=8); d.ellipse((cx-15,cy-15,cx+15,cy+15),fill=green)
    elif kind=="regen":
        for cx,cy,r in [(170,320,55),(220,270,62),(280,250,72),(345,285,62),(300,355,55)]:
            d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(70,38,64,255),outline=(142,83,136,255),width=7); d.ellipse((cx-11,cy-11,cx+11,cy+11),fill=green)
    return im

def xenotype(kind):
    if kind=="nanite": return nanite_crystal("precursor")
    if kind=="hybrid": return hybrid()
    im=wraith_base(); d=ImageDraw.Draw(im); head(d,scale=1.08)
    if kind=="humanized":
        d.arc((150,112,362,318),195,345,fill=(190,140,220,255),width=8); d.line((256,145,256,285),fill=(156,230,255,255),width=8)
    else:
        d.line((256,145,256,285),fill=(82,225,142,255),width=10)
        for r in (75,112): d.arc((256-r,256-r,256+r,256+r),205,335,fill=(145,80,145,230),width=7)
    return im

save("Abilities/AsuranLatticeIntrusion.png",asuran_lattice())
save("Abilities/NeuralInterface.png",neural())
for n,k in [("WraithBeckon","beckon"),("WraithBurden","burden"),("WraithFocus","focus"),("WraithMist","mist")]: save(f"Abilities/{n}.png",wraith_face(wraith_base(),k))
save("AncientAffinity.png",asuran_lattice())
save("Genes/AsuranCollectiveLink.png",collective())
save("Genes/HybridInstability.png",hybrid())
save("Genes/NaniteBody.png",nanite_crystal("body"))
save("Genes/NanitePrecursor.png",nanite_crystal("precursor"))
save("Genes/NaniteReconstruction.png",nanite_crystal("reconstruction"))
save("Genes/NaniteReserve.png",nanite_crystal("reserve"))
for n,k in [("WhispersPredator","predator"),("WraithHemogenic","hemogenic"),("WraithLifeForce","lifeforce"),("WraithOtherTraits","other"),("WraithRegeneration","regen")]: save(f"Genes/{n}.png",wraith_gene(k))
save("Xenotypes/HumanizedWraith.png",xenotype("humanized"))
save("Xenotypes/NanitePrecursor.png",xenotype("nanite"))
save("Xenotypes/WhispersHybrid.png",xenotype("hybrid"))
save("Xenotypes/Wraith.png",xenotype("wraith"))

print("UI symbols generated: 22")
