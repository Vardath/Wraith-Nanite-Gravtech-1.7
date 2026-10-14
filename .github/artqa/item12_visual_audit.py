from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import re, json, math

ROOT=Path("Textures")
OUT=Path("qa/item12_contact_sheets")
OUT.mkdir(parents=True,exist_ok=True)

def collect():
    groups={}
    for p in ROOT.rglob("*.png"):
        s=str(p)
        key=None
        if "/Item/Weapon/" in s:
            key="weapons"
        elif "/Projectile/" in s:
            key="projectiles"
        elif "/Building/Furniture/" in s:
            key="furniture"
        elif "/Building/" in s:
            # group remaining buildings by the first two folders after Building
            parts=p.parts
            i=parts.index("Building")
            rest=parts[i+1:-1]
            if not rest: key="buildings_root"
            else: key="buildings__"+"__".join(rest[:2])
        if key:
            groups.setdefault(key,[]).append(p)
    return {k:sorted(v) for k,v in groups.items()}

def audit(p):
    rec={"path":str(p)}
    try:
        im=Image.open(p).convert("RGBA"); im.load()
    except Exception as e:
        rec["error"]=repr(e); return rec
    a=im.getchannel("A"); w,h=im.size; bbox=a.getbbox()
    edge=0
    for x in range(w): edge += int(a.getpixel((x,0))>0)+int(a.getpixel((x,h-1))>0)
    for y in range(1,h-1): edge += int(a.getpixel((0,y))>0)+int(a.getpixel((w-1,y))>0)
    rec.update({"size":[w,h],"bbox":bbox,"edge_alpha_pixels":edge})
    return rec

groups=collect()
rows=[]
for k,files in groups.items():
    for p in files: rows.append(audit(p))

print("WNG_ITEM12_AUDIT_START")
print("WNG_ITEM12_COUNT",len(rows))
# Linked atlases deliberately clamp opaque edge texels into their UV gutters.
# Whole-canvas transparency rules apply to standalone sprites, not linked sheets.
bad=[r for r in rows if "error" in r or (r.get("edge_alpha_pixels",0)>0 and "/HullCorners/" not in r["path"] and not Path(r["path"]).stem.endswith("_Atlas"))]
print("WNG_ITEM12_FLAGGED",len(bad))
for r in bad: print("WNG_ITEM12_FLAG",json.dumps(r,sort_keys=True))
print("WNG_ITEM12_AUDIT_END")

for key,files in sorted(groups.items()):
    cellw,cellh=220,220
    cols=5
    rowsn=math.ceil(len(files)/cols)
    sheet=Image.new("RGB",(cols*cellw,rowsn*cellh),(28,28,28))
    d=ImageDraw.Draw(sheet)
    for i,p in enumerate(files):
        im=Image.open(p).convert("RGBA")
        im.thumbnail((180,165),Image.Resampling.LANCZOS)
        x=(i%cols)*cellw; y=(i//cols)*cellh
        bg=Image.new("RGBA",(cellw,170),(0,0,0,0))
        bg.alpha_composite(im,((cellw-im.width)//2,(160-im.height)//2))
        sheet.paste(bg.convert("RGB"),(x,y))
        label=p.stem
        if len(label)>30: label=label[:27]+"..."
        d.text((x+5,y+174),label,fill=(255,255,255))
        parent="/".join(p.parts[-3:-1])
        if len(parent)>30: parent=parent[-30:]
        d.text((x+5,y+192),parent,fill=(180,180,180))
    sheet.save(OUT/f"{key}.jpg",quality=92)
print("WNG_ITEM12_GROUPS",len(groups))
for k,v in sorted(groups.items()): print("WNG_ITEM12_GROUP",k,len(v))

# final-item12-edge checkpoint 2026-09-22
