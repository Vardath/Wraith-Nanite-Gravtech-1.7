from pathlib import Path
from PIL import Image
import hashlib

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Textures' / 'Things' / 'Pawn' / 'Replicator'
PROFESSIONAL_SOURCE = ROOT / '.github' / 'artsrc' / 'replicator_professional' / 'rep_professional_sources_256.zip'

if not PROFESSIONAL_SOURCE.exists():
    raise FileNotFoundError(f'Professional Replicator source bundle missing: {PROFESSIONAL_SOURCE}')
ROLES = ['Artillery','Bulwark','Burrower','Controller','Drone','Hunter','Repairer','SiegeMass','Titan']
DIRS = ['', '_north', '_east', '_south', '_west']

# The 45 sprites are committed directly to the live texture paths.
# This script is an integrity check only; it never regenerates or overwrites the artwork.
# The base Drone is the canonical six-legged Stargate-style form. Compound/specialist
# forms deliberately use different silhouettes and may use different support/leg counts.
south_hashes = {}
for role in ROLES:
    for suffix in DIRS:
        path = OUT / f'WNG_Replicator{role}{suffix}.png'
        if not path.exists():
            raise FileNotFoundError(path)
        with Image.open(path) as im:
            im.load()
            if im.size != (512,512) or im.mode != 'RGBA':
                raise RuntimeError(f'{path}: expected 512x512 RGBA, got {im.size} {im.mode}')
            alpha = im.getchannel('A')
            if alpha.getbbox() is None:
                raise RuntimeError(f'{path}: empty alpha')
            edges = [
                alpha.crop((0,0,512,1)).getextrema()[1],
                alpha.crop((0,511,512,512)).getextrema()[1],
                alpha.crop((0,0,1,512)).getextrema()[1],
                alpha.crop((511,0,512,512)).getextrema()[1],
            ]
            if any(edges):
                raise RuntimeError(f'{path}: alpha touches canvas edge {edges}')
            px = im.load()
            for y in range(512):
                for x in range(512):
                    if px[x,y][3] == 0 and px[x,y][:3] != (0,0,0):
                        raise RuntimeError(f'{path}: dirty transparent RGB at {(x,y)}')
            if suffix in ('', '_south'):
                digest = hashlib.sha256(im.tobytes()).hexdigest()
                south_hashes.setdefault(role, digest)

if len(set(south_hashes.values())) != len(ROLES):
    duplicates = {}
    for role, digest in south_hashes.items():
        duplicates.setdefault(digest, []).append(role)
    repeated = [roles for roles in duplicates.values() if len(roles) > 1]
    raise RuntimeError(f'Replicator compound forms are not visually distinct: {repeated}')

print(f'Validated {len(ROLES)*len(DIRS)} differentiated compound-form Replicator pawn sprites.')
