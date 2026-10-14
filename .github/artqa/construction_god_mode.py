from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(".")
failures=[]
notes=[]

# ---------- XML inheritance model for WNG buildables ----------
named={}
defs=[]

for folder in ("Defs/ThingDefs","Defs/Gravships","Defs/TerrainDefs"):
    base=ROOT/folder
    if not base.exists():
        continue
    for path in base.rglob("*.xml"):
        try:
            root=ET.parse(path).getroot()
        except Exception as exc:
            failures.append(f"{path}: XML parse failure: {exc}")
            continue
        for node in list(root):
            if node.tag not in ("ThingDef","TerrainDef"):
                continue
            if node.get("Name"):
                named[node.get("Name")]=node
            defs.append((path,node))

def chain(node):
    out=[node]
    seen=set()
    parent=node.get("ParentName")
    while parent and parent in named and parent not in seen:
        seen.add(parent)
        node=named[parent]
        out.append(node)
        parent=node.get("ParentName")
    return out

def inherited_elem(node,xpath):
    for current in chain(node):
        found=current.find(xpath)
        if found is not None:
            return found
    return None

def inherited_text(node,xpath):
    e=inherited_elem(node,xpath)
    return (e.text or "").strip() if e is not None and e.text else None

def positive_list(elem):
    if elem is None:
        return False
    found=False
    for child in list(elem):
        txt=(child.text or "").strip()
        if not txt:
            continue
        try:
            if float(txt)>0:
                found=True
        except ValueError:
            failures.append(f"non-numeric cost entry {child.tag}={txt!r}")
    return found

vanilla_work_inheritance = {
    "WNG_PrecursorLumen": "LampBase",
    "WNG_PrecursorWallLumen": "LampBase",
    "WNG_WraithLumenOrgan": "LampBase",
    "WNG_WraithWallLumen": "LampBase",
}

buildables=[]
for path,node in defs:
    name=(node.findtext("defName") or "").strip()
    if not name.startswith("WNG_"):
        continue
    category=inherited_text(node,"designationCategory")
    if not category:
        continue
    buildables.append((path,node,name,category))

    never=(inherited_text(node,"building/neverBuildable") or "").lower()
    if never=="true":
        failures.append(f"{name}: appears in Architect category {category} but is neverBuildable ({path})")

    costs=inherited_elem(node,"costList")
    if not positive_list(costs):
        failures.append(f"{name}: no positive effective construction cost ({path})")

    work=inherited_text(node,"statBases/WorkToBuild")
    if work is None:
        expected_parent = vanilla_work_inheritance.get(name)
        if expected_parent is None or node.get("ParentName") != expected_parent:
            failures.append(f"{name}: no effective WorkToBuild and no audited vanilla-parent inheritance ({path})")
        else:
            notes.append(f"{name}: WorkToBuild intentionally inherited from vanilla {expected_parent}")
    else:
        try:
            if float(work)<=0:
                failures.append(f"{name}: non-positive WorkToBuild={work} ({path})")
        except ValueError:
            failures.append(f"{name}: invalid WorkToBuild={work!r} ({path})")

    research=inherited_elem(node,"researchPrerequisites")
    research_single=inherited_text(node,"researchPrerequisite")
    research_items=[] if research is None else [(x.text or "").strip() for x in research.findall("li") if (x.text or "").strip()]
    if not research_items and not research_single:
        failures.append(f"{name}: player-buildable but has no effective research gate ({path})")

    if node.tag=="ThingDef":
        category_value=(inherited_text(node,"category") or "")
        # Buildings should create real blueprint/frame construction in normal play.
        # A ThingDef with a designation category but Item category is suspicious.
        if category_value=="Item":
            failures.append(f"{name}: Architect-visible ThingDef is category Item, not a constructed building ({path})")

# ---------- Runtime/source anti-bypass checks ----------
source_files=[p for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs") if "Diagnostics" not in p.parts]
source="\n".join(p.read_text(encoding="utf-8",errors="ignore") for p in source_files)

for label,pat in {
    "forces God Mode on": r"DebugSettings\.godMode\s*=\s*true",
    "forces Dev Mode path": r"Prefs\.DevMode\s*=\s*true",
    "rewrites construction costs": r"\.costList\s*=(?!=)|costList\s*\.\s*Clear\s*\(",
    "rewrites WorkToBuild": r"WorkToBuild\s*=|statBases[^\n]*WorkToBuild\s*=",
    "clears research prerequisites": r"researchPrerequisites\s*\.\s*Clear\s*\(",
    "forces zero work": r"WorkToBuild[^\n]{0,80}=\s*0(?:f)?\b",
}.items():
    if re.search(pat,source):
        failures.append(f"runtime construction bypass detected: {label}")

routing_path=ROOT/"Source"/"WraithNaniteGravtech/UI/WNGArchitectRouting.cs"
routing=routing_path.read_text(encoding="utf-8",errors="ignore")

required=[
    "Designator_Build_WNGGodMode",
    "public override bool Visible => DebugSettings.godMode || base.Visible;",
    "if (!DebugSettings.godMode)",
    "return base.CanDesignateCell(c);",
    "base.DesignateSingleCell(c);",
    "new Designator_Build_WNGGodMode(def)",
    "UpgradeNativeWNGDesignators",
]
for token in required:
    if token not in routing:
        failures.append(f"Architect/God Mode contract missing token: {token}")

for forbidden in (
    "DebugSettings.godMode = true",
    "Prefs.DevMode",
    "WithScopedGodMode",
    "Designator_Build_WNGDev",
):
    if forbidden in routing:
        failures.append(f"Architect routing contains forbidden implicit bypass: {forbidden}")

access_path=ROOT/"Source"/"WraithNaniteGravtech/UI/WNGGameplayAccessibility.cs"
access=access_path.read_text(encoding="utf-8",errors="ignore")
if "def.forceDebugSpawnable = true" not in access:
    failures.append("direct dev spawning contract missing forceDebugSpawnable=true")
if "def.category != ThingCategory.Item && def.category != ThingCategory.Building" not in access:
    failures.append("forceDebugSpawnable is not constrained to physical WNG defs")

# Debug spawning must not change normal resource/build work semantics.
for token in ("costList","WorkToBuild","researchPrerequisites","designationCategory"):
    if token in access:
        failures.append(f"WNGGameplayAccessibility unexpectedly mutates/mentions normal construction field {token}")

# ---------- Blueprint/frame and minification sanity ----------
minified_count=0
for path,node,name,category in buildables:
    if node.tag!="ThingDef":
        continue
    minified=inherited_text(node,"minifiedDef")
    if minified:
        minified_count+=1
        if minified!="MinifiedThing" and not minified.startswith("WNG_"):
            notes.append(f"{name}: custom/external minifiedDef={minified}")

    passability=inherited_text(node,"passability")
    size=inherited_text(node,"size")
    # Building defs can omit size/passability through inheritance; note only.
    if size is None:
        notes.append(f"{name}: size inherited/defaulted")
    if passability is None:
        notes.append(f"{name}: passability inherited/defaulted")

print("=== D140 CONSTRUCTION / GOD MODE AUDIT ===")
print(f" - WNG Architect buildables audited: {len(buildables)}")
print(f" - Buildables with minifiedDef: {minified_count}")
print(f" - Production C# files scanned for construction bypasses: {len(source_files)}")
print(" - Normal mode contract: finite costs/work + research + vanilla Designator_Build path")
print(" - God Mode contract: explicit DebugSettings.godMode gate only")
print(" - Dev spawn contract: forceDebugSpawnable only; no normal-cost mutation")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: WNG keeps normal construction finite while preserving explicit God Mode/direct-dev spawning.")
