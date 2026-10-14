# Re-run after the 2026-09-22 full Replicator professional art pass.
from pathlib import Path
from PIL import Image, ImageDraw
import json

ROOT=Path(".")
OUT=Path("qa/replicator")
OUT.mkdir(parents=True,exist_ok=True)

files=[]
for p in ROOT.rglob("*.png"):
    if "Replicator" in str(p) and not str(p).startswith("qa/"):
        files.append(p)
files=sorted(files)

rows=[]
for p in files:
    rec={"path":str(p)}
    try:
        im=Image.open(p).convert("RGBA"); im.load()
    except Exception as e:
        rec["error"]=repr(e); rows.append(rec); continue
    a=im.getchannel("A"); w,h=im.size; bbox=a.getbbox()
    edge=0
    for x in range(w):
        edge += int(a.getpixel((x,0))>0) + int(a.getpixel((x,h-1))>0)
    for y in range(1,h-1):
        edge += int(a.getpixel((0,y))>0) + int(a.getpixel((w-1,y))>0)
    if bbox:
        cx=(bbox[0]+bbox[2]-1)/2; cy=(bbox[1]+bbox[3]-1)/2
        off=[round(cx-(w-1)/2,2),round(cy-(h-1)/2,2)]
        margins=[bbox[0],bbox[1],w-bbox[2],h-bbox[3]]
    else:
        off=None; margins=None
    rec.update({"size":[w,h],"bbox":bbox,"margins":margins,"center_offset":off,"edge_alpha_pixels":edge})
    rows.append(rec)

print("WNG_REPLICATOR_AUDIT_START")
for r in rows: print(json.dumps(r,sort_keys=True))
print("WNG_REPLICATOR_AUDIT_END")
print("WNG_REPLICATOR_COUNT",len(rows))
bad=[r for r in rows if "error" in r or r.get("edge_alpha_pixels",0)>0]
print("WNG_REPLICATOR_FLAGGED",len(bad))
for r in bad: print("WNG_REPLICATOR_FLAG",json.dumps(r,sort_keys=True))

cellw,cellh=220,220
cols=5; rowsn=(len(files)+cols-1)//cols
sheet=Image.new("RGB",(cols*cellw,rowsn*cellh),(28,28,28))
d=ImageDraw.Draw(sheet)
for i,p in enumerate(files):
    im=Image.open(p).convert("RGBA")
    im.thumbnail((175,160),Image.Resampling.LANCZOS)
    x=(i%cols)*cellw; y=(i//cols)*cellh
    tmp=Image.new("RGBA",(cellw,170),(0,0,0,0))
    tmp.alpha_composite(im,((cellw-im.width)//2,(160-im.height)//2))
    sheet.paste(tmp.convert("RGB"),(x,y),None)
    label=str(p).replace("Textures/","").replace("Compatibility/CombatExtended/Textures/","CE/")
    tail=label[-34:]
    d.text((x+5,y+174),tail,fill=(255,255,255))
sheet.save(OUT/"replicator-contact-sheet.jpg",quality=92)
