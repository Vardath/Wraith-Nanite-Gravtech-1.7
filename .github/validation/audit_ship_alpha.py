from pathlib import Path
from PIL import Image, ImageFilter
import math

ROOTS = [
    Path("Textures/Things/Building/Goauld/Shuttle"),
    Path("Textures/Things/Building/Precursor/Shuttle"),
    Path("Textures/Things/Building/Wraith/Shuttle"),
]

def bright(rgb):
    r,g,b = rgb
    return max(rgb) >= 220 and (r+g+b)/3 >= 190

flagged = []
count = 0

for root in ROOTS:
    if not root.exists():
        continue
    for p in sorted(root.glob("*.png")):
        count += 1
        try:
            im = Image.open(p).convert("RGBA")
        except Exception as exc:
            issues = [f"CORRUPT_OR_UNREADABLE {type(exc).__name__}: {exc}"]
            flagged.append((str(p), issues, None))
            print(f"{p}: {'; '.join(issues)}")
            continue
        w,h = im.size
        alpha = im.getchannel("A")
        bbox = alpha.getbbox()
        issues = []

        if bbox is None:
            issues.append("EMPTY")
            flagged.append((str(p), issues, None))
            continue

        l,t,r,b = bbox
        margins = (l,t,w-r,h-b)
        cx = (l+r)/2
        cy = (t+b)/2
        offx = abs(cx - w/2) / w
        offy = abs(cy - h/2) / h

        if min(margins) <= 1:
            issues.append(f"EDGE_TOUCH margins={margins}")
        if offx > 0.045 or offy > 0.045:
            issues.append(f"OFF_CENTER dx={offx:.3f} dy={offy:.3f}")

        # Detect bright matte RGB just outside visible alpha, which can bleed as a halo
        # under bilinear texture filtering even when those pixels are fully transparent.
        solid = alpha.point(lambda a: 255 if a > 20 else 0)
        near = solid.filter(ImageFilter.MaxFilter(5))
        px = im.load()
        ap = alpha.load()
        np = near.load()
        fringe = 0
        near_count = 0
        for y in range(h):
            for x in range(w):
                if np[x,y] and ap[x,y] <= 8:
                    near_count += 1
                    if bright(px[x,y][:3]):
                        fringe += 1
        fringe_ratio = fringe / near_count if near_count else 0
        if fringe >= 8 and fringe_ratio > 0.02:
            issues.append(f"BRIGHT_MATTE fringe={fringe}/{near_count} ({fringe_ratio:.3f})")

        # Any visible alpha on the literal image border is almost always wrong for craft.
        border_alpha = 0
        for x in range(w):
            border_alpha += alpha.getpixel((x,0)) > 0
            border_alpha += alpha.getpixel((x,h-1)) > 0
        for y in range(1,h-1):
            border_alpha += alpha.getpixel((0,y)) > 0
            border_alpha += alpha.getpixel((w-1,y)) > 0
        if border_alpha:
            issues.append(f"BORDER_ALPHA pixels={border_alpha}")

        print(f"{p}: size={w}x{h} bbox={bbox} margins={margins} center=({offx:.3f},{offy:.3f}) fringe={fringe_ratio:.3f} {'; '.join(issues) if issues else 'OK'}")
        if issues:
            flagged.append((str(p), issues, bbox))

print(f"\nAUDITED={count} FLAGGED={len(flagged)}")
for p, issues, bbox in flagged:
    print("FLAG", p, "|", " | ".join(issues))

# This is an audit/reporting check, not an automatic art mutator.
# It exits successfully so findings can be reviewed before deliberate replacements.
