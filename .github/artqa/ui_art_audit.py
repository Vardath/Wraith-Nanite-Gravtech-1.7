from pathlib import Path
from PIL import Image
from collections import deque
import json

ROOT = Path("Textures/UI/WNG")
rows = []

def components(mask, w, h):
    seen = set()
    comps = []
    pix = mask.load()
    for y in range(h):
        for x in range(w):
            if pix[x, y] <= 24 or (x, y) in seen:
                continue
            q = deque([(x, y)])
            seen.add((x, y))
            n = 0
            while q:
                cx, cy = q.popleft()
                n += 1
                for nx, ny in ((cx-1,cy),(cx+1,cy),(cx,cy-1),(cx,cy+1)):
                    if 0 <= nx < w and 0 <= ny < h and (nx, ny) not in seen and pix[nx, ny] > 24:
                        seen.add((nx, ny))
                        q.append((nx, ny))
            comps.append(n)
    return sorted(comps, reverse=True)

for p in sorted(ROOT.rglob("*.png")):
    try:
        im = Image.open(p).convert("RGBA")
        im.load()
    except Exception as e:
        rows.append({"path": str(p), "error": repr(e)})
        continue
    w, h = im.size
    a = im.getchannel("A")
    bbox = a.getbbox()
    alpha = list(a.getdata())
    nonzero = sum(v > 0 for v in alpha)
    opaque = sum(v == 255 for v in alpha)
    edge = sum(a.getpixel((x,0)) > 0 or a.getpixel((x,h-1)) > 0 for x in range(w))
    edge += sum(a.getpixel((0,y)) > 0 or a.getpixel((w-1,y)) > 0 for y in range(1,h-1))
    corners = [a.getpixel((0,0)), a.getpixel((w-1,0)), a.getpixel((0,h-1)), a.getpixel((w-1,h-1))]
    if bbox:
        cx = (bbox[0] + bbox[2] - 1) / 2
        cy = (bbox[1] + bbox[3] - 1) / 2
        off = [round(cx - (w-1)/2, 2), round(cy - (h-1)/2, 2)]
        margins = [bbox[0], bbox[1], w-bbox[2], h-bbox[3]]
    else:
        off = None
        margins = None
    comps = components(a, w, h)
    stray = sum(1 for n in comps[1:] if n >= 4)
    panel = (min(corners) > 200) or (bbox == (0,0,w,h) and opaque/(w*h) > 0.80)
    rows.append({
        "path": str(p), "size": [w,h], "bbox": bbox, "margins": margins,
        "center_offset": off, "edge_alpha_pixels": edge,
        "nonzero_ratio": round(nonzero/(w*h),4),
        "opaque_ratio": round(opaque/(w*h),4),
        "corner_alpha": corners, "panel_suspect": panel,
        "component_count": len(comps), "stray_components_ge4": stray,
        "largest_components": comps[:5],
    })

print("WNG_UI_AUDIT_START")
for row in rows:
    print(json.dumps(row, sort_keys=True))
print("WNG_UI_AUDIT_END")

# post-Ancient-icon repair audit 2026-09-22
