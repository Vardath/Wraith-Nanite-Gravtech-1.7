from pathlib import Path
from PIL import Image, ImageDraw

ROOT=Path("Textures/Things/Pawn/Humanlike/Apparel")
OUT=Path("qa/apparel_contact_sheets")
OUT.mkdir(parents=True,exist_ok=True)

families={}
for p in sorted(ROOT.rglob("*.png")):
    stem=p.stem
    base=stem
    for suf in ["_Male_north","_Male_south","_Male_east","_Male_west","_Female_north","_Female_south","_Female_east","_Female_west","_Thin_north","_Thin_south","_Thin_east","_Thin_west","_Fat_north","_Fat_south","_Fat_east","_Fat_west","_Hulk_north","_Hulk_south","_Hulk_east","_Hulk_west","_Male","_Female","_Thin","_Fat","_Hulk","_north","_south","_east","_west"]:
        if base.endswith(suf):
            base=base[:-len(suf)]
            break
    families.setdefault(base,[]).append(p)

for base, files in sorted(families.items()):
    cell=220
    cols=5
    rows=(len(files)+cols-1)//cols
    sheet=Image.new("RGBA",(cols*cell,rows*cell),(28,28,28,255))
    d=ImageDraw.Draw(sheet)
    for i,p in enumerate(files):
        im=Image.open(p).convert("RGBA")
        im.thumbnail((180,165),Image.Resampling.LANCZOS)
        x=(i%cols)*cell
        y=(i//cols)*cell
        px=x+(cell-im.width)//2
        py=y+8+(165-im.height)//2
        sheet.alpha_composite(im,(px,py))
        label=p.stem.replace(base,"").lstrip("_") or "base"
        d.text((x+5,y+180),label,fill=(255,255,255,255))
    sheet.convert("RGB").save(OUT/f"{base}.jpg",quality=92)
print("Generated",len(families),"apparel family contact sheets")
