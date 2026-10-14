from pathlib import Path
import argparse
import hashlib
import re
import xml.etree.ElementTree as ET

EXPECTED_ID = "3808100707"
EXPECTED_NAME = "Wraith & Nanite Gravtech"
EXPECTED_AUTHOR = "Vardath"
EXPECTED_PACKAGE_ID = "vardath.wraithnanitegravtech"
EXPECTED_VERSION = "1.6"
REQUIRED_DLC = {
    "ludeon.rimworld.royalty",
    "ludeon.rimworld.ideology",
    "ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly",
    "ludeon.rimworld.odyssey",
}
FORBIDDEN_LINK_SUFFIXES = {
    ".url", ".webloc", ".website", ".lnk", ".desktop", ".html", ".htm", ".mht", ".mhtml"
}

parser = argparse.ArgumentParser()
parser.add_argument("--github-root", required=True)
parser.add_argument("--steam-root", required=True)
parser.add_argument("--workshop-description", default="STEAM-WORKSHOP-DESCRIPTION.txt")
args = parser.parse_args()

github_root = Path(args.github_root)
steam_root = Path(args.steam_root)
description_path = Path(args.workshop_description)
failures = []
notes = []

def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def file_map(root):
    out = {}
    for p in sorted(root.rglob("*")):
        if p.is_symlink():
            failures.append(f"{root}: symlink is not allowed in Steam/runtime payload: {p.relative_to(root)}")
            continue
        if p.is_file():
            rel = p.relative_to(root).as_posix()
            out[rel] = (p.stat().st_size, sha256(p))
            if p.suffix.lower() in FORBIDDEN_LINK_SUFFIXES:
                failures.append(f"{root}: web/link shortcut file not allowed in upload payload: {rel}")
    return out

if not github_root.is_dir():
    failures.append(f"missing GitHub package root: {github_root}")
if not steam_root.is_dir():
    failures.append(f"missing Steam upload root: {steam_root}")

github_files = file_map(github_root) if github_root.is_dir() else {}
steam_files = file_map(steam_root) if steam_root.is_dir() else {}

missing_from_steam = sorted(set(github_files) - set(steam_files))
extra_in_steam = sorted(set(steam_files) - set(github_files))
if missing_from_steam:
    failures.append("Steam upload missing GitHub runtime files: " + ", ".join(missing_from_steam))
if extra_in_steam:
    failures.append("Steam upload has extra files: " + ", ".join(extra_in_steam))

changed = []
for rel in sorted(set(github_files) & set(steam_files)):
    if github_files[rel] != steam_files[rel]:
        changed.append(rel)
if changed:
    failures.append("Steam/GitHub byte mismatch: " + ", ".join(changed))

# PublishedFileId must be stable and identical in both accepted RimWorld locations.
def check_ids(root, label):
    values = {}
    for rel in ("PublishedFileId.txt", "About/PublishedFileId.txt"):
        path = root / rel
        if not path.exists():
            failures.append(f"{label}: missing {rel}")
            continue
        value = path.read_text(encoding="utf-8-sig").strip()
        values[rel] = value
        if not re.fullmatch(r"[1-9][0-9]+", value):
            failures.append(f"{label}: {rel} is not a positive numeric Workshop ID: {value!r}")
        if value != EXPECTED_ID:
            failures.append(f"{label}: {rel}={value!r}, expected fixed WNG Workshop ID {EXPECTED_ID}")
    if len(values) == 2 and len(set(values.values())) != 1:
        failures.append(f"{label}: root/About PublishedFileId values disagree: {values}")
    return values

github_ids = check_ids(github_root, "GitHub package") if github_root.is_dir() else {}
steam_ids = check_ids(steam_root, "Steam upload") if steam_root.is_dir() else {}

# About metadata must identify this exact mod and supported RimWorld version.
about = steam_root / "About" / "About.xml"
if not about.exists():
    failures.append("Steam upload missing About/About.xml")
else:
    try:
        root = ET.parse(about).getroot()
        name = (root.findtext("name") or "").strip()
        author = (root.findtext("author") or "").strip()
        package_id = (root.findtext("packageId") or "").strip()
        versions = {(li.text or "").strip() for li in root.findall("./supportedVersions/li")}
        deps = {(li.findtext("packageId") or "").strip() for li in root.findall("./modDependencies/li")}
        desc = (root.findtext("description") or "").strip()
        if name != EXPECTED_NAME:
            failures.append(f"About name={name!r}, expected {EXPECTED_NAME!r}")
        if author != EXPECTED_AUTHOR:
            failures.append(f"About author={author!r}, expected {EXPECTED_AUTHOR!r}")
        if package_id != EXPECTED_PACKAGE_ID:
            failures.append(f"About packageId={package_id!r}, expected {EXPECTED_PACKAGE_ID!r}")
        if EXPECTED_VERSION not in versions:
            failures.append(f"About supportedVersions missing {EXPECTED_VERSION}; found {sorted(versions)}")
        missing_dlc = sorted(REQUIRED_DLC - deps)
        if missing_dlc:
            failures.append("About modDependencies missing required DLC: " + ", ".join(missing_dlc))
        if not desc:
            failures.append("About description is empty")
    except Exception as exc:
        failures.append(f"About/About.xml parse failure: {exc}")

for rel in ("About/Preview.png", "About/ModIcon.png"):
    if not (steam_root / rel).is_file():
        failures.append(f"Steam upload missing {rel}")

# Repository Workshop description is upload metadata, not runtime content.
if not description_path.exists():
    failures.append(f"missing Workshop description source: {description_path}")
else:
    try:
        description = description_path.read_text(encoding="utf-8")
    except Exception as exc:
        failures.append(f"Workshop description is not valid UTF-8 text: {exc}")
        description = ""

    if "\x00" in description:
        failures.append("Workshop description contains NUL bytes")
    for bad in ("file://", "javascript:", "data:text/html"):
        if bad.lower() in description.lower():
            failures.append(f"Workshop description contains unsafe/local link scheme: {bad}")

    # Balance the BBCode containers WNG uses. [*] is self-contained.
    token_re = re.compile(r"\[(/?)(h1|h2|list|b|i)\]", re.I)
    stack = []
    for m in token_re.finditer(description):
        closing, tag = m.group(1), m.group(2).lower()
        if not closing:
            stack.append(tag)
        elif not stack or stack.pop() != tag:
            failures.append(f"Workshop description has unbalanced/misnested BBCode near {m.group(0)}")
            break
    if stack:
        failures.append("Workshop description has unclosed BBCode tags: " + ", ".join(stack))

# Steam runtime folder must not accidentally include repository promo/upload metadata.
for rel in (
    "STEAM-WORKSHOP-DESCRIPTION.txt",
    "STEAM-PROMO-SHORTLIST.md",
    "artupgrades.md",
):
    if (steam_root / rel).exists():
        failures.append(f"Steam runtime upload accidentally contains repository-only metadata: {rel}")

print("=== D173 STEAM PACKAGING AUDIT ===")
print(" - expected Workshop ID:", EXPECTED_ID)
print(" - GitHub runtime files:", len(github_files))
print(" - Steam runtime files:", len(steam_files))
print(" - missing from Steam:", len(missing_from_steam))
print(" - extra in Steam:", len(extra_in_steam))
print(" - byte mismatches:", len(changed))
print(" - GitHub PublishedFileId values:", github_ids)
print(" - Steam PublishedFileId values:", steam_ids)
print(" - Workshop description source:", description_path)

if notes:
    print("\nNOTES:")
    for note in notes:
        print(" -", note)

if failures:
    print("\nFAILURES:")
    for failure in failures:
        print(" -", failure)
    raise SystemExit(1)

print("PASS: Steam upload runtime is byte-identical to the tested GitHub package, Workshop IDs/metadata are consistent, and no link/source-only packaging junk is present.")
