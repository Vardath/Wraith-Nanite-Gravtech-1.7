from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(".")
fail=[]

recipe_nodes=[]
for path in (ROOT/"Defs"/"RecipeDefs").rglob("*.xml"):
    try:
        root=ET.parse(path).getroot()
    except Exception as exc:
        fail.append(f"{path}: XML parse failed: {exc}")
        continue
    for node in list(root):
        if node.tag!="RecipeDef":
            continue
        name=(node.findtext("defName") or "").strip()
        if not name.startswith("WNG_"):
            continue
        parent=(node.get("ParentName") or "").strip()
        worker=(node.findtext("workerClass") or "").strip()
        if parent.startswith("Surgery") or worker.startswith("WraithNaniteGravtech.Recipe_") or worker.startswith("WraithNaniteGravtech.Anomaly.Recipe_"):
            # Narrow worker-based detection to actual known surgery worker source below.
            recipe_nodes.append((path,node,name,parent,worker))

# Index WNG research + hediff refs.
research=set()
for path in (ROOT/"Defs"/"ResearchProjectDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except: continue
    for node in list(root):
        if node.tag=="ResearchProjectDef":
            n=(node.findtext("defName") or "").strip()
            if n: research.add(n)

hediffs=set()
for path in (ROOT/"Defs"/"HediffDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except: continue
    for node in list(root):
        if node.tag=="HediffDef":
            n=(node.findtext("defName") or "").strip()
            if n: hediffs.add(n)

source_files=list((ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs"))
source_by_path={p:p.read_text(encoding="utf-8",errors="ignore") for p in source_files}
all_source="\n".join(source_by_path.values())

class_pat=re.compile(r"\b(?:public\s+)?(?:sealed\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([A-Za-z0-9_\.<>]+)")
class_base={}
class_file={}
for p,text in source_by_path.items():
    for cls,base in class_pat.findall(text):
        class_base[cls]=base
        class_file[cls]=p

def is_surgery_worker(worker):
    if not worker:
        return False
    cls=worker.rsplit(".",1)[-1]
    seen=set()
    while cls and cls not in seen:
        seen.add(cls)
        base=class_base.get(cls,"")
        if base in ("Recipe_Surgery","Recipe_InstallArtificialBodyPart"):
            return True
        if base.startswith("Recipe_"):
            cls=base.rsplit(".",1)[-1]
        else:
            break
    return False

# Re-filter worker-only false positives.
surgeries=[]
for row in recipe_nodes:
    path,node,name,parent,worker=row
    if parent.startswith("Surgery") or is_surgery_worker(worker):
        surgeries.append(row)

if not surgeries:
    fail.append("No WNG surgery RecipeDefs found")

custom=0
vanilla_worker=0
with_prereq=0
install=0
remove=0
flesh=0

for path,node,name,parent,worker in surgeries:
    if parent=="SurgeryFlesh": flesh+=1
    if parent.startswith("SurgeryInstall"): install+=1
    if parent.startswith("SurgeryRemove"): remove+=1

    prereq=(node.findtext("researchPrerequisite") or "").strip()
    if prereq:
        with_prereq+=1
        if prereq.startswith("WNG_") and prereq not in research:
            fail.append(f"{name}: missing WNG research prerequisite {prereq} ({path})")

    for tag in ("addsHediff","removesHediff"):
        h=(node.findtext(tag) or "").strip()
        if h.startswith("WNG_") and h not in hediffs:
            fail.append(f"{name}: missing {tag} HediffDef {h} ({path})")

    # Fixed body-part names should not be empty.
    for li in node.findall("./appliedOnFixedBodyParts/li"):
        if not (li.text or "").strip():
            fail.append(f"{name}: empty appliedOnFixedBodyParts entry ({path})")

    if worker:
        cls=worker.rsplit(".",1)[-1]
        if cls not in class_base:
            fail.append(f"{name}: custom worker class not found in source: {worker}")
            continue
        if not is_surgery_worker(worker):
            fail.append(f"{name}: custom Recipe_ worker is not a surgery class: {worker}")
            continue
        custom+=1
        text=source_by_path[class_file[cls]]
        m=re.search(r"class\s+"+re.escape(cls)+r"\s*:[\s\S]*?public\s+override\s+bool\s+AvailableOnNow\s*\(\s*Thing\s+thing\s*,\s*BodyPartRecord\s+part\s*=\s*null\s*\)\s*\{([\s\S]*?)\n\s*\}",text)
        if not m:
            fail.append(f"{name}: {cls} lacks explicit AvailableOnNow override")
            continue
        body=m.group(1)
        pawn_decl=body.find("Pawn pawn = thing as Pawn")
        null_guard=min([x for x in (body.find("pawn == null"),body.find("pawn is null")) if x>=0] or [-1])
        base_call=body.find("base.AvailableOnNow")
        if pawn_decl<0 or null_guard<0:
            fail.append(f"{name}: {cls} lacks explicit Thing->Pawn/null availability guard")
        if base_call>=0 and null_guard>=0 and base_call<null_guard:
            fail.append(f"{name}: {cls} calls base.AvailableOnNow before null guard")
    else:
        vanilla_worker+=1

# Global-operation-menu blast radius: surgery source must not mutate RecipeDef collections.
surgery_source_paths={class_file[w.rsplit(".",1)[-1]] for _,_,_,_,w in surgeries if w and w.rsplit(".",1)[-1] in class_file}
for p in sorted(surgery_source_paths):
    text=source_by_path[p]
    for token in (
        "DefDatabase<RecipeDef>.AllDefsListForReading.Clear",
        ".recipeUsers.Clear(",
        ".recipes.Clear(",
        "AllRecipes.Clear(",
        "DefDatabase<RecipeDef>.Remove",
    ):
        if token in text:
            fail.append(f"{p}: surgery implementation mutates global recipe collections via {token}")

# Every WNG custom surgery worker referenced by XML should be counted; no orphan worker that derives surgery.
xml_workers={w.rsplit(".",1)[-1] for _,_,_,_,w in surgeries if w}
for cls,base in class_base.items():
    if cls.startswith("Recipe_"):
        cur=cls
        seen=set()
        is_surg=False
        while cur and cur not in seen:
            seen.add(cur)
            b=class_base.get(cur,"")
            if b in ("Recipe_Surgery","Recipe_InstallArtificialBodyPart"):
                is_surg=True; break
            cur=b.rsplit(".",1)[-1] if b.startswith("Recipe_") else ""
        if is_surg and cls not in xml_workers:
            # Allow no orphan production surgery classes: dead code can drift and later get reconnected incorrectly.
            fail.append(f"orphan custom surgery worker not referenced by WNG RecipeDef: {cls} ({class_file[cls]})")

# Cross-check the COMPLETE physical implant pipeline rather than merely accepting
# syntactically valid surgery XML. The installed surgery must be visible on human
# pawns, accept the manufactured object, and target the same hediff/body part.
physical_implants={}
for path in (ROOT/"Defs"/"ThingDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except Exception as exc:
        fail.append(f"{path}: implant item XML parse failed: {exc}")
        continue
    for node in root.findall("ThingDef"):
        name=(node.findtext("defName") or "").strip()
        categories={(li.text or "").strip() for li in node.findall("./thingCategories/li")}
        if name.startswith("WNG_") and "BodyPartsBionic" in categories:
            if name in physical_implants:
                fail.append(f"{name}: duplicate physical implant item")
            physical_implants[name]=(path,node)
            texture=(node.findtext("./graphicData/texPath") or "").strip()
            if not texture or not (ROOT/"Textures"/(texture+".png")).is_file():
                fail.append(f"{name}: missing implant texture {texture} ({path})")

all_recipes={}
product_outputs=set()
for path in (ROOT/"Defs"/"RecipeDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except Exception as exc:
        fail.append(f"{path}: implant recipe XML parse failed: {exc}")
        continue
    for node in root.findall("RecipeDef"):
        name=(node.findtext("defName") or "").strip()
        if not name: continue
        if name in all_recipes:
            fail.append(f"{name}: duplicate surgery/production recipe")
        all_recipes[name]=(path,node)
        product_outputs.update(child.tag for child in node.findall("./products/*"))

if len(physical_implants)<15:
    fail.append(f"physical WNG implant inventory unexpectedly small: {len(physical_implants)} (expected 15)")
for name,(item_path,item_node) in sorted(physical_implants.items()):
    if name not in hediffs:
        fail.append(f"{name}: physical implant has no matching WNG HediffDef")
    if name not in product_outputs:
        fail.append(f"{name}: physical implant has no manufacturing recipe")

    install=[(path,n) for path,n in all_recipes.values()
             if (n.findtext("addsHediff") or "").strip()==name
             and (n.get("ParentName") or "").startswith("SurgeryInstall")]
    removal=[(path,n) for path,n in all_recipes.values()
             if (n.findtext("removesHediff") or "").strip()==name
             and (n.get("ParentName") or "").startswith("SurgeryRemove")]
    if len(install)!=1 or len(removal)!=1:
        fail.append(f"{name}: expected exactly one install and remove surgery, got {len(install)}/{len(removal)}")
        continue
    install_path,install_node=install[0]
    remove_path,remove_node=removal[0]

    installed_parts=[(li.text or "").strip() for li in install_node.findall("./appliedOnFixedBodyParts/li")]
    removal_parts=[(li.text or "").strip() for li in remove_node.findall("./appliedOnFixedBodyParts/li")]
    if not installed_parts or installed_parts!=removal_parts:
        fail.append(f"{name}: install/remove body parts do not match ({installed_parts} vs {removal_parts})")

    for action,node,p in (("install",install_node,install_path),("remove",remove_node,remove_path)):
        users={(li.text or "").strip() for li in node.findall("./recipeUsers/li")}
        if "Human" not in users:
            fail.append(f"{name}: {action} surgery missing Human recipe user ({p})")
    if install_node.find("researchPrerequisite") is not None:
        fail.append(f"{name}: implant surgery is research-gated despite owned implant being available")

    ingredient_defs={(li.text or "").strip() for li in install_node.findall(".//ingredients/li/filter/thingDefs/li")}
    fixed_defs={(li.text or "").strip() for li in install_node.findall("./fixedIngredientFilter/thingDefs/li")}
    if name not in ingredient_defs or name not in fixed_defs:
        fail.append(f"{name}: install surgery missing physical implant ingredient or its filter")

print(f" - Physical implant items with verified surgery/material/texture paths: {len(physical_implants)}")

# The native hybrid and physical mist gland must share gas output. All pawns
# standing in the fog get a temporary sight penalty, and the real BlindSmoke
# maintains its vanilla incoming/outgoing projectile hit-chance reduction.
mist_code=(ROOT/"Source/WraithNaniteGravtech/Anomaly/WhispersHybrid.cs").read_text(encoding="utf-8")
implant_code=(ROOT/"Source/WraithNaniteGravtech/Anomaly/WhispersImplants.cs").read_text(encoding="utf-8")
mist_hediffs=(ROOT/"Defs/HediffDefs/Hediffs_WhispersHybrid.xml")
mist_xml=ET.parse(mist_hediffs).getroot()
mist=next((h for h in mist_xml.findall("HediffDef")
           if h.findtext("defName")=="WNG_WhispersMistObscured"),None)
if mist is None or mist.findtext("./stages/li/capMods/li/postFactor")!="0.65":
    fail.append("Whispers mist eyesight penalty missing or incorrectly configured")
if mist is None or mist.findtext("./comps/li/disappearsAfterTicks")!="180~180":
    fail.append("Whispers mist eyesight penalty fails to clear after leaving")
for needle in ("map.gasGrid.AddGas(cell, GasType.BlindSmoke",
               "map.gasGrid.DensityAt(affected.Position, GasType.BlindSmoke)",
               "affected.health.AddHediff(visionDef)",
               "SetDuration(SightRefreshTicks)"):
    if needle not in mist_code:
        fail.append(f"Whispers fog lacks required gas/exposure mechanism: {needle}")
if "WhispersFogUtility.EmitPredatoryFog(pawn)" not in implant_code or "GainAbility(" in implant_code:
    fail.append("Whispers implant must use passive shared emission, not castable mist")

print("=== D154 SURGERY AUDIT ===")
print(f" - WNG surgery RecipeDefs enumerated: {len(surgeries)}")
print(f" - Custom surgery workers: {custom}")
print(f" - Vanilla/inherited surgery workers: {vanilla_worker}")
print(f" - Flesh procedures: {flesh}")
print(f" - Install procedures: {install}")
print(f" - Remove procedures: {remove}")
print(f" - Procedures with explicit research prerequisite: {with_prereq}")
print(" - Custom worker null/type guard ordering: checked")
print(" - WNG research/Hediff cross-references: checked")
print(" - Global operation-menu mutation patterns: checked")

if fail:
    print("FAILURES:")
    for x in fail: print(" -",x)
    raise SystemExit(1)

print("PASS: WNG surgery defs/workers are structurally safe and cannot poison global operation-menu enumeration through null/type availability failures.")
