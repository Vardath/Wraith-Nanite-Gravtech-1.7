from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path

ROOT=Path("Textures")
APP=ROOT/"Things/Pawn/Humanlike/Apparel/Precursor"
GOA=ROOT/"Things/Building/Goauld/Gravship"
PRE=ROOT/"Things/Building/Precursor/Gravship"
SH=ROOT/"Things/Building/Precursor/Shuttle"
UI=ROOT/"UI/WNG/Build"
for p in (APP,GOA,PRE,SH,UI): p.mkdir(parents=True,exist_ok=True)

def canvas(n): return Image.new("RGBA",(n,n),(0,0,0,0))
def center_alpha(im):
    b=im.getchannel("A").getbbox()
    if not b: return im
    cx=(b[0]+b[2])/2; cy=(b[1]+b[3])/2
    dx=round(im.width/2-cx); dy=round(im.height/2-cy)
    out=Image.new("RGBA",im.size,(0,0,0,0)); out.alpha_composite(im,(dx,dy)); return out
def down(im,n): return center_alpha(im.resize((n,n),Image.Resampling.LANCZOS))
def line(d,pts,fill,w): d.line(pts,fill=fill,width=w,joint="curve")
def glow(base,shape_fn,col=(55,220,255),blur=16,alpha=170):
    g=Image.new("RGBA",base.size,(0,0,0,0)); gd=ImageDraw.Draw(g)
    shape_fn(gd,col+(alpha,))
    g=g.filter(ImageFilter.GaussianBlur(blur)); base.alpha_composite(g)

def personal_shield():
    S=1024; im=canvas(S); d=ImageDraw.Draw(im)
    d.rounded_rectangle((300,390,724,650),radius=120,fill=(22,29,36,255),outline=(120,140,154,255),width=22)
    d.rounded_rectangle((350,430,674,610),radius=84,fill=(52,62,72,255),outline=(180,197,205,255),width=10)
    d.polygon([(512,345),(620,440),(590,600),(512,682),(434,600),(404,440)],fill=(31,41,51,255),outline=(194,210,218,255))
    d.polygon([(512,410),(563,462),(548,563),(512,605),(476,563),(461,462)],fill=(18,70,92,255))
    glow(im,lambda gd,c: gd.ellipse((455,455,569,569),fill=c),(40,220,255),22,180)
    d=ImageDraw.Draw(im)
    d.ellipse((468,468,556,556),fill=(36,185,224,255),outline=(182,247,255,255),width=8)
    d.ellipse((493,493,531,531),fill=(212,255,255,255))
    for x in (370,654):
        glow(im,lambda gd,c,x=x: gd.ellipse((x-24,488,x+24,536),fill=c),(40,220,255),10,130)
        d.ellipse((x-14,498,x+14,526),fill=(99,236,255,255))
    line(d,[(512,420),(512,360)],(84,228,255,255),10)
    line(d,[(512,604),(512,668)],(84,228,255,255),10)
    line(d,[(456,512),(400,512)],(84,228,255,255),7)
    line(d,[(568,512),(624,512)],(84,228,255,255),7)
    return down(im,256)

def goauld_projector():
    S=1536; im=canvas(S); d=ImageDraw.Draw(im)
    d.rounded_rectangle((270,300,1266,1236),radius=120,fill=(20,17,15,255),outline=(88,55,25,255),width=34)
    d.rounded_rectangle((330,360,1206,1176),radius=90,fill=(42,31,20,255),outline=(159,102,39,255),width=24)
    glow(im,lambda gd,c: gd.ellipse((430,430,1106,1106),outline=c,width=90),(255,151,33),34,150)
    d=ImageDraw.Draw(im)
    d.ellipse((460,460,1076,1076),fill=(13,16,18,255),outline=(188,119,41,255),width=34)
    d.ellipse((555,555,981,981),fill=(45,28,17,255),outline=(223,149,61,255),width=26)
    d.ellipse((640,640,896,896),fill=(17,16,15,255),outline=(126,79,32,255),width=20)
    emits=[(768,310,768,520),(768,1016,768,1226),(310,768,520,768),(1016,768,1226,768)]
    for x1,y1,x2,y2 in emits:
        line(d,[(x1,y1),(x2,y2)],(38,28,19,255),90)
        line(d,[(x1,y1),(x2,y2)],(197,128,45,255),30)
        glow(im,lambda gd,c,a=(x1,y1,x2,y2): gd.line([(a[0],a[1]),(a[2],a[3])],fill=c,width=24),(255,150,30),20,155)
    glow(im,lambda gd,c: gd.ellipse((678,678,858,858),fill=c),(255,150,30),28,190)
    d=ImageDraw.Draw(im)
    d.ellipse((695,695,841,841),fill=(228,126,25,255),outline=(255,222,134,255),width=12)
    d.ellipse((735,735,801,801),fill=(255,241,185,255))
    for xy in [(420,420,510,500),(1026,420,1116,500),(420,1036,510,1116),(1026,1036,1116,1116)]:
        d.rounded_rectangle(xy,radius=18,fill=(11,12,13,255),outline=(144,89,34,255),width=10)
    return down(im,512)

def asuran_extender():
    S=1024; im=canvas(S); d=ImageDraw.Draw(im)
    d.rounded_rectangle((174,174,850,850),radius=116,fill=(27,34,42,255),outline=(112,132,146,255),width=22)
    d.rounded_rectangle((225,225,799,799),radius=92,fill=(49,60,70,255),outline=(187,201,209,255),width=10)
    petals=[[(512,260),(620,358),(575,458),(512,430),(449,458),(404,358)],
            [(764,512),(666,620),(566,575),(594,512),(566,449),(666,404)],
            [(512,764),(404,666),(449,566),(512,594),(575,566),(620,666)],
            [(260,512),(358,404),(458,449),(430,512),(458,575),(358,620)]]
    for pts in petals: d.polygon(pts,fill=(77,91,104,255),outline=(198,211,219,255))
    glow(im,lambda gd,c: gd.ellipse((360,360,664,664),fill=c),(45,220,255),24,165)
    d=ImageDraw.Draw(im)
    d.ellipse((388,388,636,636),fill=(22,55,71,255),outline=(95,234,255,255),width=18)
    d.ellipse((448,448,576,576),fill=(48,202,235,255),outline=(205,255,255,255),width=10)
    d.ellipse((485,485,539,539),fill=(224,255,255,255))
    for p in [((512,388),(512,290)),((636,512),(734,512)),((512,636),(512,734)),((388,512),(290,512))]:
        line(d,[p[0],p[1]],(74,225,250,255),8)
    return down(im,256)

def puddle_north():
    S=1536; im=canvas(S); d=ImageDraw.Draw(im)
    hull=[(768,170),(900,250),(980,395),(1015,620),(1015,920),(980,1140),(900,1285),(768,1366),(636,1285),(556,1140),(521,920),(521,620),(556,395),(636,250)]
    d.polygon(hull,fill=(56,67,78,255),outline=(13,19,24,255))
    line(d,hull+[hull[0]],(178,194,204,255),22)
    left=[(548,480),(430,590),(402,760),(430,930),(548,1040),(605,935),(585,760),(605,585)]
    right=[(1536-x,y) for x,y in left]
    for pts in (left,right):
        d.polygon(pts,fill=(34,44,55,255),outline=(118,144,160,255))
        inner=[(int(x+(768-x)*.15),y) for x,y in pts]
        d.line(inner+[inner[0]],fill=(53,194,232,170),width=6,joint="curve")
    canopy=[(768,218),(852,290),(846,470),(768,540),(690,470),(684,290)]
    d.polygon(canopy,fill=(17,60,85,255),outline=(104,213,245,255))
    glow(im,lambda gd,c: gd.polygon([(768,255),(820,305),(814,438),(768,485),(722,438),(716,305)],fill=c),(48,190,255),16,90)
    d=ImageDraw.Draw(im)
    for y,w in [(610,180),(725,205),(840,205),(955,180),(1070,145)]:
        line(d,[(768-w,y),(768+w,y)],(22,28,34,255),14)
        line(d,[(768-w+18,y-6),(768+w-18,y-6)],(107,124,135,255),4)
    line(d,[(768,540),(768,1260)],(128,153,168,255),7)
    glow(im,lambda gd,c: gd.rounded_rectangle((686,1230,850,1284),radius=22,fill=c),(45,210,255),18,125)
    d=ImageDraw.Draw(im); d.rounded_rectangle((700,1238,836,1276),radius=15,fill=(69,225,255,255))
    return down(im,512)

def queen_north():
    S=1536; im=canvas(S); d=ImageDraw.Draw(im)
    hull=[(768,155),(865,250),(925,390),(1000,535),(1120,665),(1155,768),(1120,871),(1000,1001),(925,1146),(865,1286),(768,1381),(671,1286),(611,1146),(536,1001),(416,871),(381,768),(416,665),(536,535),(611,390),(671,250)]
    d.polygon(hull,fill=(31,39,47,255),outline=(12,18,23,255))
    line(d,hull+[hull[0]],(162,181,191,255),20)
    for side in (-1,1):
        pts=[(768+side*75,390),(768+side*205,470),(768+side*315,625),(768+side*330,770),(768+side*285,925),(768+side*165,1010),(768+side*105,850),(768+side*125,650)]
        d.polygon(pts,fill=(50,61,72,255),outline=(124,153,170,255))
        line(d,[(768+side*105,430),(768+side*220,560),(768+side*290,705),(768+side*270,880)],(51,202,235,255),7)
        line(d,[(768+side*120,690),(768+side*250,820)],(51,202,235,255),5)
    d.polygon([(768,215),(842,390),(824,1110),(768,1320),(712,1110),(694,390)],fill=(53,65,75,255),outline=(184,199,208,255))
    glow(im,lambda gd,c: gd.rounded_rectangle((720,390,816,1120),radius=46,fill=c),(48,220,255),22,110)
    d=ImageDraw.Draw(im)
    d.rounded_rectangle((733,410,803,1098),radius=34,fill=(18,74,94,255),outline=(116,236,255,255),width=9)
    for y in [510,665,820,975]:
        d.ellipse((747,y-20,789,y+20),fill=(84,230,255,255),outline=(208,255,255,255),width=4)
    line(d,[(768,250),(768,365)],(121,239,255,255),6)
    line(d,[(768,1135),(768,1280)],(121,239,255,255),6)
    return down(im,512)

def save_family(base,im):
    variants={"":im,"_north":im,
              "_east":center_alpha(im.rotate(-90,resample=Image.Resampling.BICUBIC,expand=False)),
              "_south":center_alpha(im.rotate(180,resample=Image.Resampling.BICUBIC,expand=False)),
              "_west":center_alpha(im.rotate(90,resample=Image.Resampling.BICUBIC,expand=False))}
    for suf,v in variants.items(): v.save(SH/f"{base}{suf}.png",optimize=True)

def save_icon(path,src):
    b=src.getchannel("A").getbbox(); crop=src.crop(b)
    scale=min(220/crop.width,220/crop.height)
    resized=crop.resize((max(1,int(crop.width*scale)),max(1,int(crop.height*scale))),Image.Resampling.LANCZOS)
    icon=canvas(256); icon.alpha_composite(resized,((256-resized.width)//2,(256-resized.height)//2)); icon.save(path,optimize=True)

personal_shield().save(APP/"WNG_PrecursorPersonalShield.png",optimize=True)
goauld_projector().save(GOA/"WNG_GoauldGravFieldProjector.png",optimize=True)
asuran_extender().save(PRE/"WNG_AsuranGravFieldExtender.png",optimize=True)

puddle=puddle_north(); queen=queen_north()
save_family("WNG_PuddleJumper",puddle)
save_family("WNG_AsuranQueenRecoveryCarrier",queen)
save_icon(UI/"WNG_PuddleJumper.png",puddle)
save_icon(UI/"WNG_AsuranQueenRecoveryCarrier.png",queen)

for p in [APP/"WNG_PrecursorPersonalShield.png",GOA/"WNG_GoauldGravFieldProjector.png",PRE/"WNG_AsuranGravFieldExtender.png",*sorted(SH.glob("WNG_PuddleJumper*.png")),*sorted(SH.glob("WNG_AsuranQueenRecoveryCarrier*.png")),UI/"WNG_PuddleJumper.png",UI/"WNG_AsuranQueenRecoveryCarrier.png"]:
    im=Image.open(p).convert("RGBA")
    assert im.getchannel("A").getbbox(), p
    assert im.getchannel("A").getextrema()[0]==0, p
    print(p,im.size,im.getchannel("A").getbbox())
