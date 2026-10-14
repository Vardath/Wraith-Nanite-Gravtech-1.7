from pathlib import Path
import base64
import zlib

# The professional Asuran placeable implementation is stored as a compressed
# source payload so the deterministic art generator remains fully reproducible
# inside the repository without committing generated source assets externally.
payload_path = Path(__file__).with_name("asuran_placeable_family_impl.zlib.b64")
source = zlib.decompress(base64.b64decode(payload_path.read_text(encoding="utf-8").strip())).decode("utf-8")

# Surface flecks belong on solid Asuran material, never in the transparent
# margin or soft ground shadow. Constrain the deterministic wear pass to
# near-opaque pixels before executing the generator.
old = '''def metal_texture(im, mask=None, amount=12):
    # restrained deterministic flecks, like painted RimWorld surface wear
    d=ImageDraw.Draw(im)
'''
new = '''def metal_texture(im, mask=None, amount=12):
    # restrained deterministic flecks, like painted RimWorld surface wear
    if mask is None:
        mask = im.getchannel("A").point(lambda a: 255 if a > 180 else 0)
    d=ImageDraw.Draw(im)
'''
if old not in source:
    raise RuntimeError("Asuran art payload no longer matches the expected surface-texture implementation")
source = source.replace(old, new, 1)

exec(compile(source, str(Path(__file__).with_name("asuran_placeable_family_impl.py")), "exec"), globals(), globals())
