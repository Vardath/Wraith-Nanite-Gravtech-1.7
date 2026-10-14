from pathlib import Path
from PIL import Image, ImageFile
ImageFile.LOAD_TRUNCATED_IMAGES = True

ROOT=Path("Textures/Things/Building/Precursor/Shuttle")
NAMES=[
"WNG_AsuranQueenRecoveryCarrier.png",
"WNG_AsuranQueenRecoveryCarrier_north.png",
"WNG_AsuranQueenRecoveryCarrier_east.png",
"WNG_AsuranQueenRecoveryCarrier_south.png",
"WNG_AsuranQueenRecoveryCarrier_west.png",
]

def center_rgba(p):
    im=Image.open(p).convert("RGBA")
    alpha=im.getchannel("A")
    bbox=alpha.getbbox()
    if not bbox:
        raise ValueError(f"{p}: empty alpha")
    cx=(bbox[0]+bbox[2]-1)/2
    cy=(bbox[1]+bbox[3]-1)/2
    tx=round((im.width-1)/2-cx)
    ty=round((im.height-1)/2-cy)
    out=Image.new("RGBA",im.size,(0,0,0,0))
    out.alpha_composite(im,(tx,ty))
    # If recovered pixels touch the canvas edge, scale the whole authored facing inward.
    # This removes clipped/halo-prone borders without rotating or redesigning the view.
    b2=out.getchannel("A").getbbox()
    if b2 and (b2[0] < 12 or b2[1] < 12 or im.width-b2[2] < 12 or im.height-b2[3] < 12):
        bw, bh = b2[2]-b2[0], b2[3]-b2[1]
        scale=min(224/bw,224/bh,1.0)
        crop=out.crop(b2)
        nw=max(1,round(crop.width*scale))
        nh=max(1,round(crop.height*scale))
        crop=crop.resize((nw,nh),Image.Resampling.LANCZOS)
        out=Image.new("RGBA",im.size,(0,0,0,0))
        out.alpha_composite(crop,((im.width-nw)//2,(im.height-nh)//2))
    px=out.load()
    for y in range(out.height):
        for x in range(out.width):
            r,g,b,a=px[x,y]
            if a==0 and (r or g or b):
                px[x,y]=(0,0,0,0)
    out.save(p,format="PNG",optimize=True)

for n in NAMES:
    p=ROOT/n
    center_rgba(p)
    Image.open(p).verify()
print("Centered and fully decoded all five Asuran recovery-carrier sprites without rotating authored facings.")
