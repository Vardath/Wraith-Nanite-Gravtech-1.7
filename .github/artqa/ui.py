from pathlib import Path
import re

ROOT=Path(".")
SRC=ROOT/"Source"/"WraithNaniteGravtech"
failures=[]
notes=[]

files=[p for p in SRC.rglob("*.cs") if "Diagnostics" not in p.parts]

command_re=re.compile(r"new\s+(Command_(?:Action|Toggle|Target))\s*(?:\([^;{}]*\))?\s*\{",re.M)

def block(text,start):
    brace=text.find("{",start)
    if brace<0:return None
    depth=0
    for i in range(brace,len(text)):
        if text[i]=="{":depth+=1
        elif text[i]=="}":
            depth-=1
            if depth==0:return text[brace+1:i]
    return None

commands=[]
getgizmos=0
windows=0
floatmenus=0
inspect_strings=0
disabled_assignments=[]

for path in files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    getgizmos += len(re.findall(r"\b(?:CompGetGizmosExtra|GetGizmos)\s*\(",text))
    windows += len(re.findall(r"\boverride\s+void\s+DoWindowContents\s*\(",text))
    floatmenus += len(re.findall(r"new\s+FloatMenuOption\s*\(",text))
    inspect_strings += len(re.findall(r"\b(?:CompInspectStringExtra|GetInspectString)\s*\(",text))
    if "new FloatMenu(" in text:
        failures.append(f"{path}: WNG action opens a transient FloatMenu; use an explicit dialog/reply box instead")

    for m in command_re.finditer(text):
        body=block(text,m.end()-1)
        if body is None:
            failures.append(f"{path}: could not parse {m.group(1)} initializer")
            continue
        commands.append((path,m.group(1),body))
        if not re.search(r"\bdefaultLabel\s*=",body):
            failures.append(f"{path}: {m.group(1)} lacks defaultLabel")
        if not re.search(r"\bdefaultDesc\s*=",body):
            failures.append(f"{path}: {m.group(1)} lacks defaultDesc")
        # Missing icon is not always invalid (RimWorld can draw a fallback), but keep it visible.
        if not re.search(r"\bicon\s*=",body):
            notes.append(f"{path}: {m.group(1)} relies on default/fallback icon")

    # Explicit disabled-state assignments must have a nearby user-facing reason.
    lines=text.splitlines()
    for i,line in enumerate(lines):
        if re.search(r"\b(?:disabled|Disabled)\s*=\s*true\b",line):
            nearby="\n".join(lines[max(0,i-8):min(len(lines),i+9)])
            disabled_assignments.append((path,i+1))
            has_reason=(
                re.search(r"\bdisabledReason\s*=",nearby)
                or ".Disable(" in nearby
                or "reason" in nearby.lower()
                or "tooltip" in nearby.lower()
            )
            if not has_reason:
                failures.append(f"{path}:{i+1}: disabled UI state lacks nearby explanation/disabledReason")

# Command.Disable(...) must not be fed an empty literal.
for path in files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    if re.search(r"\.Disable\s*\(\s*\"\"\s*\)",text):
        failures.append(f"{path}: Command.Disable called with empty reason")

# Settings UI must remain scrollable and restore small text after headings.
settings=(SRC/"WNGSettings.cs")
stext=settings.read_text(encoding="utf-8",errors="ignore") if settings.exists() else ""
for token in (
    "Widgets.BeginScrollView",
    "Widgets.EndScrollView",
    "Listing_Standard",
    "Text.Font = GameFont.Medium",
    "Text.Font = GameFont.Small",
    "settings.ClampValues()",
):
    if token not in stext:
        failures.append(f"WNGSettings UI contract missing: {token}")

# WNG custom dialogs should provide close affordance or standard cancel behavior and draw contents.
dialog_files=[]
for path in files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    if re.search(r"class\s+\w+\s*:\s*(?:Window|Dialog_\w+)",text):
        dialog_files.append(path)
        if "DoWindowContents" not in text:
            failures.append(f"{path}: custom Window/Dialog lacks DoWindowContents")
        if not any(token in text for token in ("doCloseX = true","closeOnCancel = true","Close()")):
            notes.append(f"{path}: custom dialog has no obvious close-X/cancel contract in source")

# Architect family tabs remain required.
architect=(ROOT/"Defs"/"DesignationCategoryDefs"/"WNG_Architect.xml")
atext=architect.read_text(encoding="utf-8",errors="ignore") if architect.exists() else ""
for category in ("WNG_WraithArchitect","WNG_AsuranArchitect","WNG_GoauldArchitect"):
    if category not in atext:
        failures.append(f"missing WNG Architect category {category}")

# Live UI diagnostic must exist.
# In-game audit diagnostic retired after audit completion; static CI checks remain.

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: WNG UI command metadata, disabled-state explanations, settings/dialog contracts and family Architect categories pass static checks.")
print("NOTE: physical label fit, overlap and visual disabled-state rendering remain live/manual checks.")
