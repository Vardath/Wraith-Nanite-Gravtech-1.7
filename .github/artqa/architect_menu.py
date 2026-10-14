from pathlib import Path
import collections
import xml.etree.ElementTree as ET

ROOT=Path(".")
failures=[]
notes=[]

family_defs={
    "WNG_WraithArchitect":"Wraith",
    "WNG_AsuranArchitect":"Asuran",
    "WNG_GoauldArchitect":"Goauld",
}
category_path=ROOT/"Defs/DesignationCategoryDefs/WNG_Architect.xml"
root=ET.parse(category_path).getroot()
cats={n.findtext("defName"):n for n in list(root)}

for name in family_defs:
    if name not in cats:
        failures.append(f"missing WNG family Architect category {name}")

# Index abstract parents and concrete WNG buildables.
named={}
defs=[]
for folder in ("Defs/ThingDefs","Defs/Gravships","Defs/TerrainDefs"):
    base=ROOT/folder
    if not base.exists(): continue
    for path in base.rglob("*.xml"):
        try:
            r=ET.parse(path).getroot()
        except Exception as exc:
            failures.append(f"{path}: XML parse failure: {exc}")
            continue
        for node in list(r):
            if node.tag not in ("ThingDef","TerrainDef"): continue
            if node.get("Name"): named[node.get("Name")]=node
            defs.append((path,node))

def chain(node):
    out=[node]; seen=set(); parent=node.get("ParentName")
    while parent and parent in named and parent not in seen:
        seen.add(parent); node=named[parent]; out.append(node); parent=node.get("ParentName")
    return out

def inherited_text(node,xpath):
    for current in chain(node):
        e=current.find(xpath)
        if e is not None and e.text:
            return e.text.strip()
    return None

def art_path(node):
    return inherited_text(node,"graphicData/texPath") or inherited_text(node,"uiIconPath") or inherited_text(node,"texturePath") or ""

def expected_family(name,art):
    blob=(name+" "+art).lower()
    if "goauld" in blob or "alkesh" in blob or "/goauld/" in blob or "terrain/goauld/" in blob:
        return "Goauld"
    if any(x in blob for x in ("wraith","hive","living","bioelectric")) or "/wraith/" in blob or "terrain/wraith/" in blob:
        return "Wraith"
    if any(x in blob for x in ("asuran","precursor","humanform")) or "/precursor/" in blob or "terrain/precursor/" in blob:
        return "Asuran"
    return None

buildables=[]
family_expected=collections.Counter()
native_categories=collections.Counter()
no_family=[]

for path,node in defs:
    name=(node.findtext("defName") or "").strip()
    if not name.startswith("WNG_"): continue
    native=inherited_text(node,"designationCategory")
    if not native: continue
    buildables.append((path,node,name,native))
    native_categories[native]+=1

    if native in family_defs or native=="WNG_Architect":
        failures.append(f"{name}: destructively assigned to WNG family category {native} ({path})")

    fam=expected_family(name,art_path(node))
    if fam:
        family_expected[fam]+=1
    else:
        no_family.append(name)

    # Architect-visible things need a usable icon source.
    if node.tag=="ThingDef":
        tex=inherited_text(node,"graphicData/texPath")
        icon=inherited_text(node,"uiIconPath")
        if not tex and not icon:
            failures.append(f"{name}: Architect-visible ThingDef lacks graphicData/texPath and uiIconPath ({path})")
    elif node.tag=="TerrainDef":
        tex=inherited_text(node,"texturePath")
        if not tex:
            failures.append(f"{name}: Architect-visible TerrainDef lacks texturePath ({path})")

routing=(ROOT/"Source/WraithNaniteGravtech/UI/WNGArchitectRouting.cs").read_text(encoding="utf-8",errors="ignore")
required=(
    "if (resolved.Any(d => d is Designator_Build build && build.PlacingDef == def))",
    "resolved.Add(new Designator_Build_WNGGodMode(def));",
    "resolved.SortBy(d => d.Order);",
    "UpgradeNativeWNGDesignators",
    "FamilyCategoryFor",
    "IsWNGGravshipBuildable",
    "DebugSettings.godMode || base.Visible",
    "if (!DebugSettings.godMode)",
    "return base.CanDesignateCell(c);",
)
for token in required:
    if token not in routing:
        failures.append(f"Architect routing contract missing: {token}")

for forbidden in (
    "def.designationCategory =",
    "resolved.Clear(",
    "resolvedDesignators = new",
    "DebugSettings.godMode = true",
    "Prefs.DevMode",
):
    if forbidden in routing:
        failures.append(f"Architect routing contains destructive/global mutation: {forbidden}")

# Static duplicate guard: family category defs themselves must be unique.
seen=set()
for n in list(root):
    name=(n.findtext("defName") or "").strip()
    if name in seen:
        failures.append(f"duplicate DesignationCategoryDef {name}")
    seen.add(name)

print("=== D141 ARCHITECT MENU AUDIT ===")
print(f" - WNG Architect-visible buildables: {len(buildables)}")
print(" - Native categories: "+", ".join(f"{k}={v}" for k,v in sorted(native_categories.items())))
print(" - Expected family copies: "+", ".join(f"{k}={v}" for k,v in sorted(family_expected.items())))
print(f" - Buildables intentionally outside family classifier: {len(no_family)}")
if no_family:
    print(" - Non-family examples: "+", ".join(no_family[:20]))
if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)
print("PASS: WNG Architect routing preserves native categories and safely adds family/God-Mode views.")
