from pathlib import Path
from PIL import Image, ImageDraw
import math

ROOT=Path("Textures/UI/WNG")
OUT=Path("qa/ui")
OUT.mkdir(parents=True,exist_ok=True)
files=sorted(ROOT.rglob("*.png"))

cellw,cellh=220,220
cols=5
rows=math.ceil(len(files)/cols)
sheet=Image.new("RGB",(cols*cellw,rows*cellh),(28,28,28))
d=ImageDraw.Draw(sheet)

for i,p in enumerate(files):
    im=Image.open(p).convert("RGBA")
    im.load()
    im.thumbnail((175,160),Image.Resampling.LANCZOS)
    x=(i%cols)*cellw; y=(i//cols)*cellh
    tile=Image.new("RGBA",(cellw,170),(0,0,0,0))
    tile.alpha_composite(im,((cellw-im.width)//2,(160-im.height)//2))
    sheet.paste(tile.convert("RGB"),(x,y))
    rel=str(p.relative_to(ROOT))
    label=rel if len(rel)<=32 else rel[-32:]
    d.text((x+5,y+174),label,fill=(255,255,255))
sheet.save(OUT/"ui-contact-sheet.jpg",quality=94)
print("WNG_UI_CONTACT_COUNT",len(files))
