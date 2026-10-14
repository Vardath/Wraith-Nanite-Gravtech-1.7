from pathlib import Path
from PIL import Image
import json

roots = [
    Path("Textures/Things/Building/Goauld/Shuttle"),
    Path("Textures/Things/Building/Precursor/Shuttle"),
    Path("Textures/Things/Building/Wraith/Shuttle"),
]

rows=[]
for root in roots:
    if not root.exists():
        continue
    for p in sorted(root.glob("*.png")):
        try:
            im=Image.open(p).convert("RGBA")
        except Exception as e:
            rows.append({"path":str(p),"error":repr(e)})
            continue
        w,h=im.size
        px=im.load()
        xs=[]; ys=[]; edge_alpha=0; semi=0; semi_light=0; transparent_light=0
        for y in range(h):
            for x in range(w):
                r,g,b,a=px[x,y]
                if a>0:
                    xs.append(x); ys.append(y)
                    if x==0 or y==0 or x==w-1 or y==h-1:
                        edge_alpha+=1
                    if 0<a<255:
                        semi+=1
                        if max(r,g,b)>180:
                            semi_light+=1
                elif max(r,g,b)>180:
                    transparent_light+=1
        if xs:
            bbox=[min(xs),min(ys),max(xs)+1,max(ys)+1]
            cx=(bbox[0]+bbox[2]-1)/2
            cy=(bbox[1]+bbox[3]-1)/2
            dx=round(cx-(w-1)/2,2)
            dy=round(cy-(h-1)/2,2)
            margins=[bbox[0],bbox[1],w-bbox[2],h-bbox[3]]
        else:
            bbox=None; dx=dy=None; margins=None
        rows.append({
            "path":str(p),
            "size":[w,h],
            "bbox":bbox,
            "margins":margins,
            "center_offset":[dx,dy],
            "edge_alpha_pixels":edge_alpha,
            "semi_alpha_pixels":semi,
            "semi_light_pixels":semi_light,
            "transparent_light_pixels":transparent_light,
        })

print("WNG_SHIP_ALPHA_AUDIT_START")
for r in rows:
    print(json.dumps(r,sort_keys=True))
print("WNG_SHIP_ALPHA_AUDIT_END")

# post-padding audit checkpoint 2026-09-22
