from pathlib import Path
from PIL import Image

TARGETS = [
    (Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldGravFieldProjector.png"), 224),
    (Path("Textures/Things/Building/Precursor/Gravship/WNG_AsuranGravFieldExtender.png"), 224),
    (Path("Textures/Things/Projectile/WNG/WNG_ProjectileHeavyBio.png"), 112),
    (Path("Textures/Things/Projectile/WNG/WNG_ProjectileLivingCarbine.png"), 112),
    (Path("Textures/Things/Projectile/WNG/WNG_ProjectilePrecursorPulse.png"), 112),
    (Path("Textures/Things/Projectile/WNG/WNG_ProjectileStunStaff.png"), 112),
    (Path("Textures/Things/Projectile/WNG/WNG_ProjectileStunner.png"), 112),
]

for p, limit in TARGETS:
    im = Image.open(p).convert("RGBA")
    im.load()
    a = im.getchannel("A")
    bbox = a.getbbox()
    if not bbox:
        raise RuntimeError(f"{p}: empty alpha")
    crop = im.crop(bbox)
    scale = min(limit / crop.width, limit / crop.height, 1.0)
    nw = max(1, round(crop.width * scale))
    nh = max(1, round(crop.height * scale))
    if (nw, nh) != crop.size:
        crop = crop.resize((nw, nh), Image.Resampling.LANCZOS)
    out = Image.new("RGBA", im.size, (0,0,0,0))
    out.alpha_composite(crop, ((im.width - nw)//2, (im.height - nh)//2))

    # Zero hidden RGB under transparent pixels.
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            r,g,b,aa = px[x,y]
            if aa == 0:
                px[x,y] = (0,0,0,0)

    out.save(p, "PNG", optimize=True)

    # Full decode plus hard edge-alpha assertion.
    chk = Image.open(p).convert("RGBA")
    chk.load()
    aa = chk.getchannel("A")
    w,h = chk.size
    edge = 0
    for x in range(w):
        edge += int(aa.getpixel((x,0)) > 0)
        edge += int(aa.getpixel((x,h-1)) > 0)
    for y in range(1,h-1):
        edge += int(aa.getpixel((0,y)) > 0)
        edge += int(aa.getpixel((w-1,y)) > 0)
    if edge:
        raise RuntimeError(f"{p}: edge alpha still {edge}")
    print("clean", p, "bbox", aa.getbbox(), "edge", edge)
