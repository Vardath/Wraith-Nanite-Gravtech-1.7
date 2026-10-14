"""Restore the damaged original Precursor settlement-wall PNG.

This concerns the standalone /Structure/WNG_PrecursorWall.png seen in the WNG
gallery, not the 16-variant linked /Gravship/WNG_PrecursorHull_Atlas.png.
The original has high-entropy coloured corruption at y=211..244 while its
intact upper half and valid existing art are symmetric. Reconstruct only the
damaged lower 45 rows from the intact corresponding upper border.
Do not resize, repaint, recolour or change the world-linked wall atlas.
"""
from pathlib import Path
import sys
from PIL import Image

# The replacement fallback is authored from the reviewed native wall master.
# The historical row-mirroring repair must never rewrite those new pixels.
root = Path(__file__).resolve().parents[2]
if (root / "ArtSource/Walls/Precursor/master.png").exists():
    print("Historical Precursor row repair superseded by Tools/WallArt/assemble_wall_art.mjs; authored fallback preserved.")
    sys.exit(0)

path = Path("Textures/Things/Building/Precursor/Structure/WNG_PrecursorWall.png")
with Image.open(path) as file:
    source = file.convert("RGBA")
if source.size != (256, 256):
    raise ValueError(f"unexpected original Precursor wall dimensions: {source.size}")

# The corrupted source has almost all pixels unexpectedly opaque on y=244.
# The correctly restored sprite must have a transparent margin there.
if any(source.getpixel((x, 244))[3] > 0 for x in range(256)):
    result = source.copy()
    # Preserve the original art unmodified through y=210. Only rebuild the
    # damaged lower border from the matching clean upper half.
    for y in range(211, 256):
        for x in range(256):
            result.putpixel((x, y), source.getpixel((x, 255 - y)))
    result.save(path, format="PNG", optimize=True)
    print("Restored damaged lower frame of", path)
else:
    print("Original Precursor wall PNG is already restored:", path)

with Image.open(path) as final:
    final.verify()
with Image.open(path) as final:
    final = final.convert("RGBA")
    assert final.size == (256, 256), "repaired sprite changed dimensions"
    assert all(final.getpixel((x, 244))[3] == 0 for x in range(256)), "corrupt row remains"
    assert all(final.getpixel((x, y)) == final.getpixel((x, 255 - y))
               for y in range(211, 256) for x in range(256)), "lower border not restored"
print("Verified: 256x256 Precursor wall art, intact original top, transparent margins")
