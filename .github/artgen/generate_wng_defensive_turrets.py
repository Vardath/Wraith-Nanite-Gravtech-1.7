from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

SIZE=256
OUT=Path("Textures/Things/Building/Defense/WNG")
OUT.mkdir(parents=True, exist_ok=True)

def canvas():
    return Image.new("RGBA",(SIZE,SIZE),(0,0,0,0))

def shadow(im,bbox,blur=10,alpha=85,offset=(0,8)):
    x0,y0,x1,y1=bbox
    sh=Image.new("RGBA",im.size,(0,0,0,0))
    d=ImageDraw.Draw(sh)
    dx,dy=offset
    d.ellipse((x0+dx,y0+dy,x1+dx,y1+dy),fill=(0,0,0,alpha))
    im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(blur)))

def glow(im, center, r, rgb, alpha=150):
    g=Image.new("RGBA",im.size,(0,0,0,0))
    d=ImageDraw.Draw(g)
    x,y=center
    d.ellipse((x-r*2,y-r*2,x+r*2,y+r*2),fill=(*rgb,alpha))
    im.alpha_composite(g.filter(ImageFilter.GaussianBlur(max(4,r//2))))

def wraith_base():
    im=canvas()
    shadow(im,(46,62,210,216))
    d=ImageDraw.Draw(im)
    # organic armored root/base
    outer=[(128,44),(174,58),(204,94),(212,142),(192,188),(154,214),(102,214),(64,188),(44,142),(52,94),(82,58)]
    d.polygon(outer,fill=(53,70,58,255),outline=(24,33,28,255))
    inner=[(128,62),(164,72),(188,102),(192,142),(174,176),(148,194),(108,194),(82,176),(64,142),(68,102),(92,72)]
    d.polygon(inner,fill=(83,107,86,255),outline=(38,52,43,255))
    # vein channels
    vein=(118,176,128)
    for a,b in [((128,70),(128,184)),((82,112),(174,150)),((174,112),(82,150)),((96,82),(160,182)),((160,82),(96,182))]:
        d.line((*a,*b),fill=vein,width=5)
    glow(im,(128,136),22,(91,214,132),120)
    d.ellipse((105,113,151,159),fill=(31,51,39,255),outline=(19,28,23,255),width=4)
    d.ellipse((115,123,141,149),fill=(100,218,139,255),outline=(35,76,48,255),width=3)
    # hooked roots
    for sx in (-1,1):
        pts=[(128+sx*44,170),(128+sx*68,190),(128+sx*78,210)]
        d.line(pts,fill=(44,60,49,255),width=12,joint="curve")
    return im

def wraith_top():
    im=canvas()
    d=ImageDraw.Draw(im)
    shadow(im,(62,86,194,176),8,60,(0,5))
    # sphincter mount and twin grown lobes
    d.ellipse((82,92,174,184),fill=(53,73,58,255),outline=(22,31,25,255),width=5)
    d.ellipse((98,108,158,168),fill=(91,119,94,255),outline=(42,56,45,255),width=4)
    # barrel points north by default
    d.polygon([(109,118),(119,34),(130,26),(137,118)],fill=(73,95,77,255),outline=(25,34,28,255))
    d.polygon([(137,118),(145,46),(154,38),(159,124)],fill=(63,86,69,255),outline=(25,34,28,255))
    # mineralized firing ridges
    d.line((118,40,123,112),fill=(148,177,154,255),width=4)
    d.line((149,48,151,116),fill=(148,177,154,255),width=3)
    glow(im,(125,38),9,(104,230,150),150)
    glow(im,(151,47),7,(104,230,150),130)
    return im

def asuran_base():
    im=canvas()
    shadow(im,(48,60,208,212))
    d=ImageDraw.Draw(im)
    # precise nanite composite octagon
    pts=[(86,50),(170,50),(206,86),(206,170),(170,206),(86,206),(50,170),(50,86)]
    d.polygon(pts,fill=(203,215,222,255),outline=(38,49,58,255))
    pts2=[(96,68),(160,68),(188,96),(188,160),(160,188),(96,188),(68,160),(68,96)]
    d.polygon(pts2,fill=(98,119,132,255),outline=(48,65,77,255))
    # segmented field seams
    for a,b in [((128,68),(128,188)),((68,128),(188,128)),((84,84),(172,172)),((172,84),(84,172))]:
        d.line((*a,*b),fill=(77,190,231,255),width=4)
    glow(im,(128,128),24,(81,220,255),120)
    d.ellipse((101,101,155,155),fill=(28,50,66,255),outline=(31,42,51,255),width=4)
    d.ellipse((114,114,142,142),fill=(123,235,255,255),outline=(32,88,108,255),width=3)
    # gold alignment nodes
    for x,y in [(78,78),(178,78),(78,178),(178,178)]:
        d.ellipse((x-7,y-7,x+7,y+7),fill=(196,164,84,255),outline=(67,57,35,255),width=2)
    return im

def asuran_top():
    im=canvas()
    d=ImageDraw.Draw(im)
    shadow(im,(64,86,192,178),8,60,(0,5))
    # rotating precision ring
    d.ellipse((76,90,180,194),fill=(187,201,210,255),outline=(36,48,57,255),width=5)
    d.ellipse((96,110,160,174),fill=(48,72,88,255),outline=(36,48,57,255),width=4)
    # emitter spine north
    d.polygon([(108,116),(114,38),(128,20),(142,38),(148,116)],fill=(200,214,221,255),outline=(37,49,58,255))
    d.polygon([(119,100),(122,43),(128,34),(134,43),(137,100)],fill=(46,81,100,255),outline=(31,51,62,255))
    d.line((128,40,128,102),fill=(123,234,255,255),width=5)
    glow(im,(128,36),12,(84,225,255),170)
    # lateral fins
    d.polygon([(101,126),(68,104),(75,143),(104,153)],fill=(146,165,176,255),outline=(37,49,58,255))
    d.polygon([(155,126),(188,104),(181,143),(152,153)],fill=(146,165,176,255),outline=(37,49,58,255))
    return im

arts={
    "WNG_WraithLivingTurret":wraith_base(),
    "WNG_WraithLivingTurretTop":wraith_top(),
    "WNG_AsuranAutoturret":asuran_base(),
    "WNG_AsuranAutoturretTop":asuran_top(),
}

for name,im in arts.items():
    path=OUT/f"{name}.png"
    im.save(path,optimize=True)
    with Image.open(path) as check:
        check.load()
        if check.mode!="RGBA" or check.size!=(SIZE,SIZE) or check.getchannel("A").getbbox() is None:
            raise SystemExit(f"invalid generated art: {path}")
        a=check.getchannel("A")
        edge=[]
        edge.extend(a.crop((0,0,SIZE,2)).getdata())
        edge.extend(a.crop((0,SIZE-2,SIZE,SIZE)).getdata())
        edge.extend(a.crop((0,0,2,SIZE)).getdata())
        edge.extend(a.crop((SIZE-2,0,SIZE,SIZE)).getdata())
        if any(v>8 for v in edge):
            raise SystemExit(f"alpha touches canvas edge: {path}")

print("Generated",len(arts),"defensive turret sprites.")
