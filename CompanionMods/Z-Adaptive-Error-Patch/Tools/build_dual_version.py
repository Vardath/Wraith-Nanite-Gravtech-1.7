#!/usr/bin/env python3
"""Package Z Adaptive as mutually exclusive RimWorld 1.6/1.7 loads.

Legacy XML compatibility fixes are 1.6-only. The 1.7 runtime is
independently compiled and uses optional guarded Harmony hooks.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
import hashlib
import json
import shutil
import sys
import zipfile

ROOT = Path(__file__).resolve().parent.parent
STAGE = ROOT / "out" / "stage"
MOD = STAGE / "Z Adaptive Error Patch"
ZIP = ROOT / "out" / "Z-Adaptive-Error-Patch-1.6-and-1.7.zip"
if STAGE.exists():
    shutil.rmtree(STAGE)
MOD.mkdir(parents=True, exist_ok=True)
(MOD / "About").mkdir(parents=True, exist_ok=True)
source_meta = ROOT / "About" / "About.xml"
metadata = ET.parse(source_meta)
versions = metadata.getroot().find("supportedVersions")
if versions is None:
    raise SystemExit("Z Adaptive metadata missing supportedVersions")
if "1.7" not in [li.text for li in versions.findall("li")]:
    ET.SubElement(versions, "li").text = "1.7"
description = metadata.getroot().find("description")
if description is not None and description.text:
    description.text = description.text.replace("RimWorld 1.6 modlist", "RimWorld 1.6/1.7 modlist")
metadata.write(MOD / "About" / "About.xml", encoding="utf-8", xml_declaration=True)
ET.parse(MOD / "About" / "About.xml")
textures = ROOT / "Textures"
if textures.is_dir():
    shutil.copytree(textures, MOD / "Textures", dirs_exist_ok=True)

load_folders = '''<?xml version="1.0" encoding="utf-8"?>
<loadFolders>
  <v1.6>
    <li>/</li>
    <li>1.6</li>
  </v1.6>
  <v1.7>
    <li>/</li>
    <li>1.7</li>
  </v1.7>
</loadFolders>
'''
(MOD / "LoadFolders.xml").write_text(load_folders, encoding="utf-8")
ET.parse(MOD / "LoadFolders.xml")

# Quarantine the 1.6-only XML patch collection. It cannot execute on 1.7.
patches = ROOT / "Patches"
assert patches.is_dir(), "Legacy patch collection missing"
shutil.copytree(patches, MOD / "1.6" / "Patches")
assert not (MOD / "Patches").exists()
legacy_count = len(list((MOD / "1.6" / "Patches").rglob("*.xml")))
assert legacy_count >= 10, "Missing 1.6 legacy repairs"
for file in (MOD / "1.6" / "Patches").rglob("*.xml"):
    ET.parse(file)

dlls = {}
for version in ("1.6", "1.7"):
    dll = ROOT / version / "Assemblies" / "ZAdaptiveRuntime.dll"
    if not dll.is_file():
        raise SystemExit(f"Missing compiled {version} Z Adaptive DLL: {dll}")
    data = dll.read_bytes()
    assert data[:2] == b"MZ", f"{version} assembly is not PE/CLR"
    path = MOD / version / "Assemblies" / "ZAdaptiveRuntime.dll"
    path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(dll, path)
    dlls[version] = hashlib.sha256(data).hexdigest()

# Only individually verified 1.7 XML patches may load on Waylith.
# Never copy the legacy 1.6 patch folder into the 1.7 load path.
v17 = ET.parse(MOD / "LoadFolders.xml").getroot().find("v1.7")
active17 = {(li.text or "").strip() for li in v17.findall("li")}
assert active17 == {"/", "1.7"}
verified_17 = {"KindredSmeltCompat17.xml"}
source_17 = ROOT / "1.7" / "Patches"
actual_17 = {p.name for p in source_17.glob("*.xml")} if source_17.is_dir() else set()
assert actual_17 == verified_17, f"Unexpected/missing 1.7 patches: {sorted(actual_17 ^ verified_17)}"
shutil.copytree(source_17, MOD / "1.7" / "Patches")
for file in (MOD / "1.7" / "Patches").rglob("*.xml"):
    ET.parse(file)
assert {p.name for p in (MOD / "1.7" / "Patches").glob("*.xml")} == verified_17
assert (MOD / "1.6" / "Patches").is_dir()
assert not (MOD / "Assemblies").exists(), "Global assembly would load in both versions"

ZIP.parent.mkdir(parents=True, exist_ok=True)
if ZIP.exists():
    ZIP.unlink()
with zipfile.ZipFile(ZIP, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=8) as z:
    for p in sorted(MOD.rglob("*")):
        if p.is_file():
            z.write(p, p.relative_to(STAGE).as_posix())
with zipfile.ZipFile(ZIP) as z:
    assert z.testzip() is None
    paths = z.namelist()
    assert len(paths) >= legacy_count + 5
    assert sum(n.endswith("ZAdaptiveRuntime.dll") for n in paths) == 2
print(json.dumps({
    "zip":str(ZIP),
    "file_count":len(paths),
    "legacy_1_6_patches":legacy_count,
    "z_adaptive_dll_sha256":dlls,
    "waylith_unverified_xml_excluded":True
},indent=2))
