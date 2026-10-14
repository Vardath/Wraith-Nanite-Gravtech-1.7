from pathlib import Path
from PIL import Image, ImageFile

ImageFile.LOAD_TRUNCATED_IMAGES = True
PATHS=[
 Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldGravFieldProjector.png"),
 Path("Textures/Things/Building/Precursor/Gravship/WNG_AsuranGravFieldExtender.png"),
]

for p in PATHS:
    im=Image.open(p).convert("RGBA")
    im.load()
    # Rewrite the recovered pixel stream as a fresh standards-compliant PNG.
    im.save(p,format="PNG",optimize=True)
    chk=Image.open(p).convert("RGBA")
    chk.load()
    if chk.getchannel("A").getbbox() is None:
        raise RuntimeError(f"{p}: recovered image has empty alpha")
    print("repaired",p,chk.size,chk.getchannel("A").getbbox())
