from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
DIR = ROOT / "Textures/Things/Building/Goauld/Gravship"
BASE = "WNG_GoauldSmallSublightDrive"
MASTER = DIR / f"{BASE}_north.png"

def clean_transparent_rgb(img):
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                px[x, y] = (0, 0, 0, 0)
    return img

def validate(path):
    with Image.open(path) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (512, 512):
            raise RuntimeError(f"{path}: expected 512x512 RGBA, got {check.size} {check.mode}")
        alpha = check.getchannel("A")
        if alpha.getbbox() is None:
            raise RuntimeError(f"{path}: empty alpha")
        edges = [
            alpha.crop((0, 0, 512, 1)).getextrema()[1],
            alpha.crop((0, 511, 512, 512)).getextrema()[1],
            alpha.crop((0, 0, 1, 512)).getextrema()[1],
            alpha.crop((511, 0, 512, 512)).getextrema()[1],
        ]
        if any(edges):
            raise RuntimeError(f"{path}: alpha touches canvas edge: {edges}")

with Image.open(MASTER) as src:
    src.load()
    north = clean_transparent_rgb(src)
if north.size == (384, 384):
    north = north.resize((512, 512), Image.Resampling.LANCZOS)
elif north.size != (512, 512):
    raise RuntimeError(f"Unexpected master size: {north.size}")
north = clean_transparent_rgb(north)

views = {
    f"{BASE}.png": north,
    f"{BASE}_north.png": north,
    f"{BASE}_east.png": north.transpose(Image.Transpose.ROTATE_270),
    f"{BASE}_south.png": north.transpose(Image.Transpose.ROTATE_180),
    f"{BASE}_west.png": north.transpose(Image.Transpose.ROTATE_90),
}

for name, img in views.items():
    out = DIR / name
    clean_transparent_rgb(img).save(out, "PNG", optimize=True)

for name in views:
    validate(DIR / name)

print("Finalized Goa'uld small sublight drive: 5 clean 512x512 RGBA sprites")
