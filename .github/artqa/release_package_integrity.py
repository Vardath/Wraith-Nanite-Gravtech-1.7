from pathlib import Path
from PIL import Image
import sys, wave
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(".")
errors = []

required_top = {
    "About", "Assemblies", "Compatibility", "Defs", "Patches",
    "Sounds", "Textures", "LoadFolders.xml", "PublishedFileId.txt"
}
actual_top = {p.name for p in root.iterdir()}
missing_top = sorted(required_top - actual_top)
extra_top = sorted(actual_top - required_top)
if missing_top:
    errors.append("missing runtime top-level entries: " + ", ".join(missing_top))
if extra_top:
    errors.append("unexpected top-level entries in installable runtime: " + ", ".join(extra_top))

unexpected_archives = sorted(str(p.relative_to(root)) for p in root.rglob("*.zip"))
if unexpected_archives:
    errors.append("nested ZIPs in installable runtime: " + ", ".join(unexpected_archives))

# Source/debug/repository-only files must not leak into the user package.
forbidden_dirs = {"Source", ".github", "Docs", "CompanionMods", ".git", "__pycache__", "obj", "bin"}
for p in root.rglob("*"):
    if p.is_dir() and p.name in forbidden_dirs:
        errors.append("source/dev-only directory in installable runtime: " + str(p.relative_to(root)))

forbidden_suffixes = {".cs", ".csproj", ".sln", ".pdb", ".py", ".pyc", ".yml", ".yaml"}
for p in root.rglob("*"):
    if p.is_file() and p.suffix.lower() in forbidden_suffixes:
        errors.append("source/debug-only file in installable runtime: " + str(p.relative_to(root)))

# XML must parse in the package users actually install.
xmls = sorted(root.rglob("*.xml"))
for p in xmls:
    try:
        ET.parse(p)
    except Exception as exc:
        errors.append(f"XML_PARSE {p.relative_to(root)}: {exc}")

# Every shipped PNG must strictly verify and fully decode.
pngs = sorted(root.rglob("*.png"))
for p in pngs:
    try:
        with Image.open(p) as im:
            if im.format != "PNG":
                errors.append(f"NOT_PNG {p.relative_to(root)} format={im.format}")
                continue
            im.verify()
        with Image.open(p) as im:
            im.load()
    except Exception as exc:
        errors.append(f"PNG_DECODE {p.relative_to(root)}: {exc}")

# Every shipped WAV must be structurally readable.
wavs = sorted(root.rglob("*.wav"))
for p in wavs:
    try:
        with wave.open(str(p), "rb") as w:
            if w.getnchannels() < 1 or w.getframerate() < 1 or w.getnframes() < 1:
                errors.append(f"WAV_EMPTY {p.relative_to(root)}")
    except Exception as exc:
        errors.append(f"WAV_DECODE {p.relative_to(root)}: {exc}")

# Compiled DLL and source-parity manifest must be present.
dll = root / "Assemblies" / "WraithNaniteGravtech.dll"
buildinfo = root / "Assemblies" / "WraithNaniteGravtech.buildinfo.json"
if not buildinfo.exists():
    errors.append("missing Assemblies/WraithNaniteGravtech.buildinfo.json")
if not dll.exists():
    errors.append("missing Assemblies/WraithNaniteGravtech.dll")
else:
    try:
        if dll.read_bytes()[:2] != b"MZ":
            errors.append("WraithNaniteGravtech.dll is not a PE image")
    except Exception as exc:
        errors.append(f"DLL_READ: {exc}")

# Build a texture-family index. Graphic_Multi refs may resolve to directional suffixes.
textures = root / "Textures"
texture_stems = set()
if textures.exists():
    for p in textures.rglob("*.png"):
        stem = p.relative_to(textures).as_posix()[:-4]
        texture_stems.add(stem)
        for suffix in ("_north", "_south", "_east", "_west"):
            if stem.endswith(suffix):
                texture_stems.add(stem[:-len(suffix)])

def is_wng_texture_ref(ref: str) -> bool:
    return (
        ref.startswith("UI/WNG/")
        or "WNG_" in ref
        or ref.startswith("WraithNaniteGravtech/")
    )

# Validate every WNG-owned art reference in the packaged XML, not just source.
art_tags = {
    "texPath", "iconPath", "uiIconPath", "wornGraphicPath",
    "factionIconPath", "backgroundPath", "uiIcon"
}
missing_refs = []
for p in xmls:
    try:
        doc = ET.parse(p).getroot()
    except Exception:
        continue
    for elem in doc.iter():
        if elem.tag not in art_tags or not elem.text:
            continue
        ref = elem.text.strip()
        if not ref or not is_wng_texture_ref(ref):
            continue
        if ref not in texture_stems:
            missing_refs.append((str(p.relative_to(root)), ref))
for src, ref in missing_refs:
    errors.append(f"MISSING_TEXTURE_REF {src}: {ref}")

print("WNG_RELEASE_PACKAGE_ROOT", root)
print("WNG_RELEASE_XML_COUNT", len(xmls))
print("WNG_RELEASE_PNG_COUNT", len(pngs))
print("WNG_RELEASE_WAV_COUNT", len(wavs))
print("WNG_RELEASE_TEXTURE_REF_ERRORS", len(missing_refs))
print("WNG_RELEASE_TOP_LEVEL", ",".join(sorted(actual_top)))
print("WNG_RELEASE_PACKAGE_ERRORS", len(errors))
for err in errors:
    print("WNG_RELEASE_PACKAGE_ERROR", err)

if errors:
    raise SystemExit(1)
print("WNG_RELEASE_PACKAGE_INTEGRITY GREEN")
