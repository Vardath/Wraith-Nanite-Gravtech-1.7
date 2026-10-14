from __future__ import annotations
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageEnhance
import math, random, json
import numpy as np
import torch
import torch.nn.functional as F
from torchvision.models import vgg19, VGG19_Weights

ROOT=Path(__file__).resolve().parents[3]
OUT=Path("/tmp/wng-step5-neural")
OUT.mkdir(parents=True,exist_ok=True)
SIZE=384
DEVICE=torch.device("cpu")

STYLE_DIRS=[
 ROOT/"ArtSource/Apparel/wraith_commander_carapace",
 ROOT/"ArtSource/Apparel/wraith_warrior_carapace",
 ROOT/"ArtSource/Apparel/wraith_hunter_coat",
 ROOT/"ArtSource/Apparel/wraith_queen_raiment",
]

def mask(view):
    s=SIZE
    m=Image.new("L",(s,s),0); d=ImageDraw.Draw(m)
    if view in ("south","north"):
        pts=[(92,97),(123,74),(158,64),(192,68),(226,64),(261,74),(292,97),
             (281,148),(273,210),(262,264),(239,305),(212,321),(172,321),(145,305),(122,264),(111,210),(103,148)]
        d.polygon(pts,fill=255)
        d.ellipse((96,70,288,161),fill=255)
        d.polygon(pts,fill=255)
        d.ellipse((165,65,219,116),fill=0)
    else:
        pts=[(136,92),(160,74),(188,67),(216,74),(244,92),(263,117),(259,176),(252,231),(238,279),(218,309),(192,321),(166,309),(146,284),(132,247),(124,198),(122,147),(126,115)]
        d.polygon(pts,fill=255)
        d.ellipse((132,72,250,158),fill=255)
        d.polygon(pts,fill=255)
        d.ellipse((170,68,211,112),fill=0)
    return m.filter(ImageFilter.GaussianBlur(.8))

def base_content(view):
    m=mask(view)
    yy,xx=np.mgrid[0:SIZE,0:SIZE]
    a=np.array(m)/255.0
    # soft cloth lighting
    cx=.42 if view=="east" else .45
    key=np.exp(-(((xx/SIZE)-cx)/.45)**2-(((yy/SIZE)-.33)/.50)**2)
    shade=.16*np.clip((yy/SIZE)-.60,0,1)+.06*np.clip((xx/SIZE)-.80,0,1)
    lum=np.clip(.30+.44*key-shade,0,1)
    # Asuran medium silver-blue cloth, deliberately not white
    lo=np.array([18,24,29.]); mid=np.array([78,91,98.]); hi=np.array([143,155,160.])
    rgb=np.where((lum[...,None]<.5),lo+(mid-lo)*(lum[...,None]*2),mid+(hi-mid)*((lum[...,None]-.5)*2))
    arr=np.zeros((SIZE,SIZE,4),np.uint8);arr[...,:3]=np.clip(rgb,0,255);arr[...,3]=(a*255).astype(np.uint8)
    im=Image.fromarray(arr,"RGBA")
    d=ImageDraw.Draw(im)

    # Garment details are seam/tailoring cues, not hard plates.
    if view=="south":
        # charcoal inner technical vest
        vest=[(146,105),(238,105),(230,260),(218,294),(192,306),(166,294),(154,260)]
        d.polygon(vest,fill=(24,31,36,205))
        # shoulder cloth yoke blended softly
        y=Image.new("RGBA",(SIZE,SIZE),(0,0,0,0)); yd=ImageDraw.Draw(y)
        yd.polygon([(82,94),(126,69),(192,78),(258,69),(302,94),(270,121),(225,132),(192,134),(159,132),(114,121)],fill=(70,85,92,165))
        y=y.filter(ImageFilter.GaussianBlur(6)); y.putalpha(ImageChops.multiply(y.getchannel("A"),m)); im=Image.alpha_composite(im,y); d=ImageDraw.Draw(im)
        for x in (139,245):
            d.line([(x,121),(x-4 if x<192 else x+4,270)],fill=(119,132,136,100),width=2)
        d.arc((165,65,219,116),180,360,fill=(132,144,147,130),width=3)
        for yv in (132,160,188,216):
            d.ellipse((195,yv,198,yv+3),fill=(126,137,139,130))
        d.line([(126,287),(192,302),(258,287)],fill=(111,123,127,100),width=2)
        # restrained cyan thread
        d.line([(142,150),(140,240)],fill=(44,87,94,80),width=1)
        d.line([(242,150),(244,240)],fill=(44,87,94,65),width=1)
    elif view=="north":
        vest=[(151,112),(233,112),(228,264),(216,295),(192,306),(168,295),(156,264)]
        d.polygon(vest,fill=(24,31,36,200))
        y=Image.new("RGBA",(SIZE,SIZE),(0,0,0,0)); yd=ImageDraw.Draw(y)
        yd.polygon([(82,94),(126,69),(192,78),(258,69),(302,94),(270,121),(225,132),(192,135),(159,132),(114,121)],fill=(68,83,90,160))
        y=y.filter(ImageFilter.GaussianBlur(6)); y.putalpha(ImageChops.multiply(y.getchannel("A"),m)); im=Image.alpha_composite(im,y); d=ImageDraw.Draw(im)
        d.line([(142,123),(146,271)],fill=(117,130,134,95),width=2)
        d.line([(242,123),(238,271)],fill=(117,130,134,95),width=2)
        d.line([(127,288),(192,302),(257,288)],fill=(108,121,125,95),width=2)
    else:
        vest=[(168,106),(238,107),(243,257),(222,295),(184,274)]
        d.polygon(vest,fill=(24,31,36,200))
        y=Image.new("RGBA",(SIZE,SIZE),(0,0,0,0)); yd=ImageDraw.Draw(y)
        yd.polygon([(126,91),(161,70),(224,78),(259,98),(246,121),(204,130),(152,121)],fill=(68,83,90,160))
        y=y.filter(ImageFilter.GaussianBlur(5)); y.putalpha(ImageChops.multiply(y.getchannel("A"),m)); im=Image.alpha_composite(im,y); d=ImageDraw.Draw(im)
        d.line([(151,122),(156,270)],fill=(116,129,133,95),width=2)
        d.line([(226,124),(229,260)],fill=(111,125,129,90),width=2)
    # cloth wear / stitch noise
    rng=random.Random(420+len(view))
    for _ in range(40):
        x=rng.randint(105,278); yv=rng.randint(105,292)
        if m.getpixel((x,yv))>100:
            ln=rng.randint(2,8)
            d.line((x,yv,x+ln,yv+rng.choice([-1,0,1])),fill=(180,186,185,rng.randint(8,22)),width=1)
    im.putalpha(m)
    return im

def style_image(view):
    imgs=[]
    for d in STYLE_DIRS:
        p=d/f"master_{view}.png"
        if p.exists():
            im=Image.open(p).convert("RGBA")
            bb=im.getchannel("A").getbbox()
            if not bb: continue
            crop=im.crop(bb).convert("L").resize((SIZE,SIZE),Image.Resampling.LANCZOS)
            imgs.append(np.array(crop,dtype=np.float32)/255.)
    if not imgs:
        raise RuntimeError("no accepted style masters")
    # combine accepted materials in grayscale so palette does not transfer
    a=np.mean(imgs,axis=0)
    a=np.clip((a-a.mean())/(a.std()+1e-6)*.17+.48,0,1)
    rgb=np.repeat(a[...,None],3,axis=2)
    return Image.fromarray((rgb*255).astype(np.uint8),"RGB")

weights=VGG19_Weights.IMAGENET1K_V1
net=vgg19(weights=weights).features.eval().to(DEVICE)
for p in net.parameters(): p.requires_grad_(False)
norm_mean=torch.tensor([0.485,0.456,0.406],device=DEVICE).view(1,3,1,1)
norm_std=torch.tensor([0.229,0.224,0.225],device=DEVICE).view(1,3,1,1)
layers={1,6,11,20,29}

def prep(im):
    a=np.array(im.convert("RGB"),dtype=np.float32)/255.
    t=torch.from_numpy(a).permute(2,0,1).unsqueeze(0).to(DEVICE)
    return t

def feats(x):
    x=(x-norm_mean)/norm_std
    out=[]
    for i,l in enumerate(net):
        x=l(x)
        if i in layers: out.append(x)
        if i>=29: break
    return out

def gram(f):
    b,c,h,w=f.shape
    z=f.view(c,h*w)
    return z@z.t()/(c*h*w)

def stylize(view):
    content=base_content(view)
    style=style_image(view)
    ct=prep(content)
    st=prep(style)
    cf=feats(ct)
    sf=feats(st)
    sg=[gram(f).detach() for f in sf]
    x=ct.clone().requires_grad_(True)
    opt=torch.optim.Adam([x],lr=.035)
    alpha=torch.from_numpy((np.array(content.getchannel("A"),dtype=np.float32)/255.)).to(DEVICE).view(1,1,SIZE,SIZE)
    for step in range(170):
        opt.zero_grad(set_to_none=True)
        xf=feats(x)
        c_loss=F.mse_loss(xf[2],cf[2])
        s_loss=sum(F.mse_loss(gram(a),b) for a,b in zip(xf,sg))
        tv=((x[:,:,:,1:]-x[:,:,:,:-1])**2).mean()+((x[:,:,1:,:]-x[:,:,:-1,:])**2).mean()
        loss=5.0*c_loss+180.0*s_loss+0.002*tv
        loss.backward()
        # Keep transparent/background pixels from wandering.
        if x.grad is not None:
            x.grad*=alpha
        opt.step()
        with torch.no_grad():
            x.clamp_(0,1)
    arr=(x.detach().squeeze(0).permute(1,2,0).cpu().numpy()*255).astype(np.uint8)
    # Re-impose Asuran palette using stylized luminance only.
    lum=.2126*arr[...,0]+.7152*arr[...,1]+.0722*arr[...,2]
    lum=np.clip((lum-lum.min())/(lum.max()-lum.min()+1e-6),0,1)
    lo=np.array([10,15,19.]); mid=np.array([69,82,89.]); hi=np.array([151,161,164.])
    rgb=np.where((lum[...,None]<.5),lo+(mid-lo)*(lum[...,None]*2),mid+(hi-mid)*((lum[...,None]-.5)*2))
    out=np.zeros((SIZE,SIZE,4),np.uint8);out[...,:3]=np.clip(rgb,0,255);out[...,3]=np.array(content.getchannel("A"))
    im=Image.fromarray(out,"RGBA")
    # Blend back a little authored garment information so details stay readable.
    authored=base_content(view)
    im=Image.blend(im,authored,.28)
    im.putalpha(content.getchannel("A"))
    im=ImageEnhance.Contrast(im).enhance(1.08)
    im.putalpha(content.getchannel("A"))
    return im

for v in ("south","north","east"):
    im=stylize(v)
    im.save(OUT/f"master_{v}.png",optimize=True)
    im.resize((192,192),Image.Resampling.LANCZOS).save(OUT/f"preview_{v}.png",optimize=True)

sheet=Image.new("RGBA",(192*3,192),(18,18,18,255))
for i,v in enumerate(("south","north","east")):
    sheet.alpha_composite(Image.open(OUT/f"preview_{v}.png"),(i*192,0))
sheet.save(OUT/"contact-sheet.png",optimize=True)
print(json.dumps({"status":"ok","out":str(OUT)}))
