from pathlib import Path
from PIL import Image

FAMILIES = [
    (Path("Textures/Things/Building/Goauld/Gravship"), "WNG_GoauldGravshipDoor"),
    (Path("Textures/Things/Building/Wraith/Gravship"), "WNG_WraithGravshipDoor"),
    (Path("Textures/Things/Building/Precursor/Gravship"), "WNG_AsuranGravshipDoor"),
]

for folder, stem in FAMILIES:
    north_path = folder / f"{stem}_north.png"
    if not north_path.exists():
        raise FileNotFoundError(north_path)
    north = Image.open(north_path).convert("RGBA")
    if north.size != (128, 128):
        raise ValueError(f"{north_path}: expected 128x128, got {north.size}")

    variants = {
        "": north,
        "_north": north,
        "_east": north.transpose(Image.Transpose.ROTATE_270),
        "_south": north.transpose(Image.Transpose.ROTATE_180),
        "_west": north.transpose(Image.Transpose.ROTATE_90),
    }
    for suffix, image in variants.items():
        out = folder / f"{stem}{suffix}.png"
        image.save(out, format="PNG", optimize=True)

print("Finalized professional Goa'uld, Wraith, and Asuran cardinal door art.")
