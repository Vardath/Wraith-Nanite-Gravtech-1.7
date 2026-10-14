from pathlib import Path
from PIL import Image

MAP={
 "Textures/Things/Item/Weapon/Precursor/WNG_AncientDrone.png":"Textures/UI/WNG/AncientDrone.png",
 "Textures/Things/Item/Weapon/Precursor/WNG_RecoveredAncientDrone.png":"Textures/UI/WNG/RecoveredAncientDrone.png",
 "Textures/Things/Item/Resource/Precursor/WNG_RecoveredVacuumEnergyModule.png":"Textures/UI/WNG/RecoveredVacuumModule.png",
}

def make_icon(src_path,dst_path):
    im=Image.open(src_path).convert("RGBA")
    im.load()
    a=im.getchannel("A")
    bbox=a.getbbox()
    if not bbox:
        raise ValueError(f"{src_path}: empty alpha")
    crop=im.crop(bbox)
    target=184
    scale=min(target/crop.width,target/crop.height)
    nw=max(1,round(crop.width*scale)); nh=max(1,round(crop.height*scale))
    crop=crop.resize((nw,nh),Image.Resampling.LANCZOS)
    out=Image.new("RGBA",(256,256),(0,0,0,0))
    out.alpha_composite(crop,((256-nw)//2,(256-nh)//2))
    # Guarantee fully transparent pixels carry no light matte RGB.
    px=out.load()
    for y in range(256):
        for x in range(256):
            r,g,b,a=px[x,y]
            if a==0:
                px[x,y]=(0,0,0,0)
    Path(dst_path).parent.mkdir(parents=True,exist_ok=True)
    out.save(dst_path,format="PNG",optimize=True)
    chk=Image.open(dst_path).convert("RGBA"); chk.load()
    bb=chk.getchannel("A").getbbox()
    if not bb:
        raise ValueError(f"{dst_path}: generated empty icon")
    if bb[0]==0 or bb[1]==0 or bb[2]==256 or bb[3]==256:
        raise ValueError(f"{dst_path}: generated icon touches canvas edge: {bb}")

for src,dst in MAP.items():
    make_icon(src,dst)
print("Derived three transparent Ancient/recovered UI icons from authoritative in-repo item art.")
