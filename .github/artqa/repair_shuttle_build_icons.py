from pathlib import Path
from PIL import Image, ImageFile
ImageFile.LOAD_TRUNCATED_IMAGES = True

ROOT=Path("Textures/UI/WNG/Build")
FILES=["WNG_PuddleJumper.png","WNG_AsuranQueenRecoveryCarrier.png"]

def normalize(p):
    im=Image.open(p).convert("RGBA")
    im.load()
    a=im.getchannel("A")
    bbox=a.getbbox()
    if not bbox:
        raise ValueError(f"{p}: empty alpha")
    cx=(bbox[0]+bbox[2]-1)/2
    cy=(bbox[1]+bbox[3]-1)/2
    tx=round((im.width-1)/2-cx)
    ty=round((im.height-1)/2-cy)
    out=Image.new("RGBA",im.size,(0,0,0,0))
    out.alpha_composite(im,(tx,ty))
    b2=out.getchannel("A").getbbox()
    if b2 and (b2[0] < 12 or b2[1] < 12 or im.width-b2[2] < 12 or im.height-b2[3] < 12):
        bw, bh=b2[2]-b2[0], b2[3]-b2[1]
        scale=min(224/bw,224/bh,1.0)
        crop=out.crop(b2)
        nw=max(1,round(crop.width*scale)); nh=max(1,round(crop.height*scale))
        crop=crop.resize((nw,nh),Image.Resampling.LANCZOS)
        out=Image.new("RGBA",im.size,(0,0,0,0))
        out.alpha_composite(crop,((im.width-nw)//2,(im.height-nh)//2))
    out.save(p,format="PNG",optimize=True)
    chk=Image.open(p); chk.load()

for n in FILES:
    normalize(ROOT/n)
print("Recovered Puddle Jumper icon and centred both shuttle build icons.")
