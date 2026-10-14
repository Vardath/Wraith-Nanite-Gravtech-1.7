from pathlib import Path
import argparse
import shutil
import zipfile

ROOT = Path(".")
ROOT_NAME = "Wraith-Nanite-Gravtech"

RUNTIME_DIRS = (
    "About",
    "Compatibility",
    "Defs",
    "Patches",
    "Sounds",
    "Textures",
)
RUNTIME_FILES = (
    "LoadFolders.xml",
    "PublishedFileId.txt",
)

parser = argparse.ArgumentParser()
parser.add_argument("--zip", dest="zip_path", default="Wraith-Nanite-Gravtech-1.6-release.zip")
parser.add_argument("--stage", dest="stage_path", default="release-stage")
args = parser.parse_args()

stage_root = Path(args.stage_path)
mod_root = stage_root / ROOT_NAME
zip_path = Path(args.zip_path)

if stage_root.exists():
    shutil.rmtree(stage_root)
if zip_path.exists():
    zip_path.unlink()

mod_root.mkdir(parents=True, exist_ok=True)

for name in RUNTIME_DIRS:
    src = ROOT / name
    if not src.exists():
        raise SystemExit(f"Missing required runtime directory: {name}")
    shutil.copytree(src, mod_root / name)

for name in RUNTIME_FILES:
    src = ROOT / name
    if not src.exists():
        raise SystemExit(f"Missing required runtime file: {name}")
    shutil.copy2(src, mod_root / name)

# Assemblies are intentionally selective: users receive the compiled DLL, not PDB/debug artifacts.
dll = ROOT / "Assemblies" / "WraithNaniteGravtech.dll"
buildinfo = ROOT / "Assemblies" / "WraithNaniteGravtech.buildinfo.json"
if not dll.exists():
    raise SystemExit("Missing compiled Assemblies/WraithNaniteGravtech.dll")
if not buildinfo.exists():
    raise SystemExit("Missing Assemblies/WraithNaniteGravtech.buildinfo.json; refuse to package an unproven DLL")
(mod_root / "Assemblies").mkdir(parents=True, exist_ok=True)
shutil.copy2(dll, mod_root / "Assemblies" / dll.name)
shutil.copy2(buildinfo, mod_root / "Assemblies" / buildinfo.name)

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
    for p in sorted(mod_root.rglob("*")):
        if p.is_file():
            arc = Path(ROOT_NAME) / p.relative_to(mod_root)
            zf.write(p, arc.as_posix())

print(f"WNG_RELEASE_BUILT {zip_path}")
print(f"WNG_RELEASE_ROOT {ROOT_NAME}")
print(f"WNG_RELEASE_FILE_COUNT {sum(1 for p in mod_root.rglob('*') if p.is_file())}")
