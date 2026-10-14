from pathlib import Path
from PIL import Image

ROOTS = [
    Path("Textures/Things/Building/Goauld/Shuttle"),
    Path("Textures/Things/Building/Precursor/Shuttle"),
    Path("Textures/Things/Building/Wraith/Shuttle"),
]

def sanitize_matte(path):
    try:
        im = Image.open(path).convert("RGBA")
        im.load()
    except Exception:
        return False
    px = im.load()
    changed = False
    for y in range(im.height):
        for x in range(im.width):
            r,g,b,a = px[x,y]
            if a <= 8 and (r or g or b):
                px[x,y] = (0,0,0,a)
                changed = True
    if changed:
        im.save(path, format="PNG", optimize=True)
    return changed

def build_rotations(master_path, base_stem, master_facing):
    master = Image.open(master_path).convert("RGBA")
    master.load()
    # Counter-clockwise quarter-turns needed from master facing to target.
    order = ["north","west","south","east"]
    mi = order.index(master_facing)
    targets = {}
    for facing in ["north","east","south","west"]:
        ti = order.index(facing)
        turns = (ti - mi) % 4
        out = master
        for _ in range(turns):
            out = out.transpose(Image.Transpose.ROTATE_90)
        targets[facing] = out
    folder = master_path.parent
    for facing,img in targets.items():
        p = folder / f"{base_stem}_{facing}.png"
        img.save(p, format="PNG", optimize=True)
    targets["north"].save(folder / f"{base_stem}.png", format="PNG", optimize=True)

# Reconstruct only craft families whose surviving repo facing is valid and whose art
# is intentionally rotation-equivalent. Do NOT do this to the authored Asuran carrier.
build_rotations(
    Path("Textures/Things/Building/Goauld/Shuttle/WNG_GoauldDeathGlider_east.png"),
    "WNG_GoauldDeathGlider",
    "east",
)
build_rotations(
    Path("Textures/Things/Building/Precursor/Shuttle/WNG_PuddleJumper_south.png"),
    "WNG_PuddleJumper",
    "south",
)

changed = 0
for root in ROOTS:
    if not root.exists():
        continue
    for p in sorted(root.glob("*.png")):
        if sanitize_matte(p):
            changed += 1

print(f"Ship alpha repair complete; matte-cleaned {changed} readable PNGs.")
