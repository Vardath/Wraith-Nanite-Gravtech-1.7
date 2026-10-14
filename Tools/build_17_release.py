#!/usr/bin/env python3
"""Build a ready-to-install dual-runtime WNG 1.6/1.7 ZIP.

This packages runtime files only, with mutually exclusive, version-scoped
managed assemblies. No source files, debug PDB or companion mods are included.
"""
from pathlib import Path
import hashlib
import json
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "Wraith-Nanite-Gravtech-1.7-dual-version.zip"
MOD = "Wraith-Nanite-Gravtech"
ROOT_DIRS = ["About", "Compatibility", "Defs", "Patches", "Sounds", "Textures"]
ROOT_FILES = ["LoadFolders.xml", "PublishedFileId.txt"]
paths = {}
for directory in ROOT_DIRS:
    for p in sorted((ROOT / directory).rglob("*")):
        if p.is_file():
            paths[p.relative_to(ROOT).as_posix()] = p
for file in ROOT_FILES:
    paths[file] = ROOT / file

for version in ("1.6", "1.7"):
    dll = ROOT / version / "Assemblies/WraithNaniteGravtech.dll"
    manifest = ROOT / version / "Assemblies/WraithNaniteGravtech.buildinfo.json"
    if not dll.is_file() or not manifest.is_file():
        raise SystemExit(f"Missing {version} compiled DLL and/or source manifest")
    digest = hashlib.sha256(dll.read_bytes()).hexdigest()
    actual = json.loads(manifest.read_text(encoding="utf-8"))
    if actual.get("dll_sha256") != digest:
        raise SystemExit(f"{version} DLL does not match its recorded SHA256")
    paths[f"{version}/Assemblies/WraithNaniteGravtech.dll"] = dll
    paths[f"{version}/Assemblies/WraithNaniteGravtech.buildinfo.json"] = manifest
    print(f"WNG_RUNTIME_{version} sha256={digest} manifest_source={actual.get('source_commit')}")

xml = ET.parse(ROOT / "LoadFolders.xml").getroot()
if xml.find("v1.6") is None or xml.find("v1.7") is None:
    raise SystemExit("LoadFolders.xml lacks both v1.6 and v1.7 branches")
for ver in ("1.6", "1.7"):
    folders = [(li.text or "").strip() for li in xml.find("v"+ver).findall("li") if li.get("IfModActive") is None]
    if folders != ["/", ver]:
        raise SystemExit(f"Unsupported/mixed load order for {ver}: {folders}")
metadata = ET.parse(ROOT / "About/About.xml").getroot()
support = {x.text for x in metadata.findall("./supportedVersions/li")}
if not {"1.6", "1.7"}.issubset(support):
    raise SystemExit("Both 1.6 and 1.7 must be declared")

with zipfile.ZipFile(OUTPUT, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=8) as archive:
    for relative, source in sorted(paths.items()):
        archive.write(source, f"{MOD}/{relative}")
with zipfile.ZipFile(OUTPUT) as archive:
    if archive.testzip() is not None:
        raise SystemExit("ZIP corruption")
print(f"WNG_DUAL_VERSION_PACKAGE {OUTPUT}")
print(f"WNG_DUAL_VERSION_FILES {len(paths)}")
