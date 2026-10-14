from pathlib import Path
from PIL import Image, ImageEnhance
import hashlib
import io
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / ".github" / "artsrc" / "replicator_professional" / "rep_professional_sources_256.zip"
OUT = ROOT / "Textures" / "Things" / "Pawn" / "Replicator"
OUT.mkdir(parents=True, exist_ok=True)

ROLES = [
    "Drone", "Hunter", "Bulwark", "Titan", "SiegeMass",
    "Controller", "Repairer", "Burrower", "Artillery",
]

def clean_transparent_rgb(im):
    im = im.convert("RGBA")
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            if px[x, y][3] == 0:
                px[x, y] = (0, 0, 0, 0)
    return im

def validate(path):
    with Image.open(path) as im:
        im.load()
        if im.mode != "RGBA" or im.size != (512, 512):
            raise RuntimeError(f"{path}: expected 512x512 RGBA, got {im.mode} {im.size}")
        a = im.getchannel("A")
        if a.getbbox() is None:
            raise RuntimeError(f"{path}: empty alpha")
        edges = [
            a.crop((0, 0, 512, 1)).getextrema()[1],
            a.crop((0, 511, 512, 512)).getextrema()[1],
            a.crop((0, 0, 1, 512)).getextrema()[1],
            a.crop((511, 0, 512, 512)).getextrema()[1],
        ]
        if any(edges):
            raise RuntimeError(f"{path}: alpha touches canvas edge {edges}")
        px = im.load()
        for y in range(512):
            for x in range(512):
                if px[x, y][3] == 0 and px[x, y][:3] != (0, 0, 0):
                    raise RuntimeError(f"{path}: dirty transparent RGB at {(x, y)}")

with zipfile.ZipFile(SOURCE, "r") as z:
    names = set(z.namelist())
    expected = {f"{role}_256.png" for role in ROLES}
    missing = sorted(expected - names)
    if missing:
        raise RuntimeError(f"Professional Replicator source bundle missing: {missing}")

    south_hashes = {}
    for role in ROLES:
        raw = z.read(f"{role}_256.png")
        with Image.open(io.BytesIO(raw)) as src:
            src.load()
            if src.size != (256, 256):
                raise RuntimeError(f"{role}: professional source must be 256x256, got {src.size}")
            south = clean_transparent_rgb(src)
            south = south.resize((512, 512), Image.Resampling.LANCZOS)
            # A very restrained sharpen restores edge definition after the 2x resize
            # without turning the painted forms back into diagram-like line art.
            south = ImageEnhance.Sharpness(south).enhance(1.08)
            south = clean_transparent_rgb(south)

        views = {
            "south": south,
            "north": south.transpose(Image.Transpose.ROTATE_180),
            "east": south.transpose(Image.Transpose.ROTATE_90),
            "west": south.transpose(Image.Transpose.ROTATE_270),
        }

        south.save(OUT / f"WNG_Replicator{role}.png", "PNG", optimize=True)
        for direction, image in views.items():
            image.save(OUT / f"WNG_Replicator{role}_{direction}.png", "PNG", optimize=True)

        for suffix in ("", "_north", "_east", "_south", "_west"):
            validate(OUT / f"WNG_Replicator{role}{suffix}.png")

        south_hashes[role] = hashlib.sha256(
            (OUT / f"WNG_Replicator{role}_south.png").read_bytes()
        ).hexdigest()

if len(set(south_hashes.values())) != len(ROLES):
    raise RuntimeError("Professional Replicator compound forms are not all visually distinct.")

print("Installed and validated 45 professional Replicator pawn sprites (9 distinct compound forms).")
