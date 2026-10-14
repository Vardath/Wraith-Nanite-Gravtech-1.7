from pathlib import Path
from PIL import Image, ImageDraw, ImageChops

WORLD = Path("Textures/Things/Building/Precursor/Shuttle")
STEM = "WNG_AsuranQueenRecoveryCarrier"
UI = Path("Textures/UI/WNG/Build/WNG_AsuranQueenRecoveryCarrier.png")

def clean_transparent_rgb(image):
    image = image.convert("RGBA")
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if a == 0:
                px[x, y] = (0, 0, 0, 0)
    return image

def center_visible(image):
    image = clean_transparent_rgb(image)
    alpha = image.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        raise RuntimeError("empty alpha")
    cx = (bbox[0] + bbox[2]) / 2
    cy = (bbox[1] + bbox[3]) / 2
    dx = round(128 - cx)
    dy = round(128 - cy)
    if dx or dy:
        canvas = Image.new("RGBA", image.size, (0,0,0,0))
        canvas.alpha_composite(image, (dx, dy))
        image = canvas
    return clean_transparent_rgb(image)

base_path = WORLD / f"{STEM}.png"
north_path = WORLD / f"{STEM}_north.png"
east_path = WORLD / f"{STEM}_east.png"
south_path = WORLD / f"{STEM}_south.png"
west_path = WORLD / f"{STEM}_west.png"

# Base/north have been restored from the first clean white-carrier commit.
for path in (base_path, north_path):
    with Image.open(path) as src:
        src.load()
        image = center_visible(src)
    image.save(path, "PNG", optimize=True)

# East reconstruction:
# - preserve the still-clean authored east-facing upper section;
# - replace only the corrupted lower field with opposite-side hull geometry
#   from the approved west side view;
# - this is deliberately NOT a rotation of the north/top master.
with Image.open(east_path) as src:
    src.load()
    authored_east = src.convert("RGBA")
with Image.open(west_path) as src:
    src.load()
    west = src.convert("RGBA")

reconstructed = west.transpose(Image.Transpose.FLIP_LEFT_RIGHT)

# Corruption begins abruptly below ~104 px. Preserve full authored east through
# row 96, then feather the handoff through row 106.
mask = Image.new("L", (256,256), 0)
draw = ImageDraw.Draw(mask)
draw.rectangle((0,0,255,96), fill=255)
for y in range(97,107):
    draw.line((0,y,255,y), fill=int(255 * (107-y) / 10))
mask = ImageChops.multiply(mask, authored_east.getchannel("A"))
reconstructed = Image.composite(authored_east, reconstructed, mask)
reconstructed = center_visible(reconstructed)
reconstructed.save(east_path, "PNG", optimize=True)

# Full-decode and validate all approved world facings without rewriting the
# good south/west artwork.
for path in (base_path, north_path, east_path, south_path, west_path):
    with Image.open(path) as im:
        im.load()
        rgba = im.convert("RGBA")
        if rgba.size != (256,256):
            raise RuntimeError(f"{path}: expected 256x256, got {rgba.size}")
        alpha = rgba.getchannel("A")
        bbox = alpha.getbbox()
        if bbox is None:
            raise RuntimeError(f"{path}: empty alpha")
        edges = [
            alpha.crop((0,0,256,1)).getextrema()[1],
            alpha.crop((0,255,256,256)).getextrema()[1],
            alpha.crop((0,0,1,256)).getextrema()[1],
            alpha.crop((255,0,256,256)).getextrema()[1],
        ]
        if any(edges):
            raise RuntimeError(f"{path}: alpha touches canvas edge")
        cx = (bbox[0] + bbox[2]) / 2
        cy = (bbox[1] + bbox[3]) / 2
        if abs(cx-128) > 3 or abs(cy-128) > 3:
            raise RuntimeError(f"{path}: visible mass not centred: {bbox}")

with Image.open(UI) as icon:
    icon.load()
    rgba = icon.convert("RGBA")
    if rgba.size != (256,256) or rgba.getchannel("A").getbbox() is None:
        raise RuntimeError("Carrier build icon failed decode/alpha validation")

print("Repaired Asuran recovery carrier: restored base/north, reconstructed authored east, preserved south/west/icon.")
