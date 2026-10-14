from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

ROOT=Path(".")
failures=[]
notes=[]

# ---------- Source-side CompProperties -> ThingComp pairing ----------
src_files=[p for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs") if "Diagnostics" not in p.parts]
source="\n".join(p.read_text(encoding="utf-8",errors="ignore") for p in src_files)
class_names=set(re.findall(r"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)",source))

pairings={}
for p in src_files:
    text=p.read_text(encoding="utf-8",errors="ignore")
    # Per-class scan, robust to block and expression-bodied constructors.
    for m in re.finditer(r"class\s+(CompProperties_[A-Za-z0-9_]+)\b",text):
        prop=m.group(1)
        start=m.start()
        nxt=text.find("\n    public ",m.end())
        segment=text[start:nxt if nxt>start else min(len(text),start+5000)]
        mm=re.search(r"compClass\s*=\s*typeof\((Comp[A-Za-z0-9_]+)\)",segment)
        if mm:
            pairings[prop]=mm.group(1)

native_runtime_comps={"CompPilotConsole"}
for prop,comp in sorted(pairings.items()):
    if comp not in class_names and comp not in native_runtime_comps:
        failures.append(f"{prop} assigns missing ThingComp class {comp}")

# ---------- XML def / inheritance index ----------
named={}
defs=[]
for folder in ("Defs/ThingDefs","Defs/Gravships"):
    base=ROOT/folder
    if not base.exists(): continue
    for path in base.rglob("*.xml"):
        try:
            root=ET.parse(path).getroot()
        except Exception as exc:
            failures.append(f"{path}: XML parse failure: {exc}")
            continue
        for node in list(root):
            if node.tag!="ThingDef": continue
            if node.get("Name"): named[node.get("Name")]=node
            defs.append((path,node))

def chain(node):
    out=[node]; seen=set(); parent=node.get("ParentName")
    while parent and parent in named and parent not in seen:
        seen.add(parent); node=named[parent]; out.append(node); parent=node.get("ParentName")
    return out

def effective_comps(node):
    # RimWorld inheritance: nearest comps with Inherit=False replace ancestors;
    # otherwise child lists append to ancestor lists.
    out=[]
    for current in reversed(chain(node)):
        comps=current.find("comps")
        if comps is None: continue
        if (comps.get("Inherit") or "").lower()=="false":
            out=[]
        out.extend(list(comps))
    return out

def comp_class(li):
    return (li.get("Class") or "").strip()

buildings=[]
custom_comp_refs=0
duplicate_comp_defs=[]
transporters=launchables=refuelables=power_comps=facility_comps=0

# Multiple comp classes are sometimes intentionally allowed; these should remain rare and explicit.
allowed_duplicate_classes={
    "CompProperties_AffectedByFacilities",
    "WraithNaniteGravtech.CompProperties_WNGGravshipFacility",
}

for path,node in defs:
    name=(node.findtext("defName") or "").strip()
    if not name.startswith("WNG_"): continue

    category=(node.findtext("category") or "").strip()
    parent=" ".join((x.get("ParentName") or "") for x in chain(node))
    building_like=(category=="Building" or "Building" in parent or node.find("building") is not None)
    if not building_like:
        continue
    buildings.append((path,node,name))

    comps=effective_comps(node)
    classes=[comp_class(li) for li in comps if comp_class(li)]

    counts=Counter(classes)
    for cls,n in counts.items():
        if n>1 and cls not in allowed_duplicate_classes:
            duplicate_comp_defs.append((name,cls,n,path))

    for cls in classes:
        if cls.startswith("WraithNaniteGravtech."):
            custom_comp_refs+=1
            short=cls.split(".")[-1]
            if short not in class_names:
                failures.append(f"{name}: missing custom CompProperties class {cls} ({path})")

    if "CompProperties_Transporter" in classes:
        transporters+=1
        if not any(c=="CompProperties_Launchable" or c.endswith("AncientPuddleJumperLaunchable") for c in classes):
            # Transport-only structures can exist; only shuttle-like defs must launch.
            if any(tok in name for tok in ("Shuttle","Dart","Alkesh","Glider","Jumper","Carrier")):
                failures.append(f"{name}: shuttle/craft has transporter but no launchable comp ({path})")

    if any(c=="CompProperties_Launchable" or c.endswith("AncientPuddleJumperLaunchable") for c in classes):
        launchables+=1
        if "CompProperties_Transporter" not in classes and any(tok in name for tok in ("Shuttle","Dart","Alkesh","Glider","Jumper","Carrier")):
            failures.append(f"{name}: shuttle/craft has launchable but no transporter comp ({path})")

    if "CompProperties_Refuelable" in classes:
        refuelables+=1
        fuel=next((li for li in comps if comp_class(li)=="CompProperties_Refuelable"),None)
        cap=(fuel.findtext("fuelCapacity") or "").strip() if fuel is not None else ""
        try:
            if not cap or float(cap)<=0:
                failures.append(f"{name}: refuelable comp has non-positive fuelCapacity={cap!r} ({path})")
        except ValueError:
            failures.append(f"{name}: invalid fuelCapacity={cap!r} ({path})")

    if any(c in ("CompProperties_Power","CompProperties_PowerTrader","CompProperties_PowerPlant") or "PowerProxy" in c or "VacuumEnergyPower" in c for c in classes):
        power_comps+=1

    if any("Facility" in c for c in classes):
        facility_comps+=1

for name,cls,n,path in duplicate_comp_defs:
    failures.append(f"{name}: duplicate effective comp class {cls} x{n} ({path})")

# ---------- Specific high-risk comp-family contracts ----------
# Gravship pilot consoles use Odyssey's native CompPilotConsole facility contract.
# The grav engine owns CompAffectedByFacilities and reverse-registers the console.
for path,node,name in buildings:
    if "PilotConsole" not in name:
        continue
    comps_list=effective_comps(node)
    native_pilot=any(
        comp_class(li)=="CompProperties_GravshipFacility"
        and (li.findtext("compClass") or "").strip()=="CompPilotConsole"
        for li in comps_list
    )
    legacy_wrapper=any(
        comp_class(li).endswith("CompProperties_WNGPilotConsole")
        for li in comps_list
    )
    if not (native_pilot or legacy_wrapper):
        failures.append(f"{name}: pilot console missing native CompPilotConsole facility comp ({path})")

# Grav engines should expose the matching family facility endpoint.
for path,node,name in buildings:
    if "GravEngine" not in name or "Minified" in name or "Seed" in name:
        continue
    classes=[comp_class(li) for li in effective_comps(node) if comp_class(li)]
    if not any("GravshipFacility" in c or "GravEngine" in c or "AffectedByFacilities" in c for c in classes):
        failures.append(f"{name}: grav-engine building has no gravship facility/connectivity comp ({path})")

# Custom CompProperties should generally point at a concrete runtime comp.
unpaired=[]
for prop in sorted(c for c in class_names if c.startswith("CompProperties_")):
    # AbilityEffect/native subclasses may rely on inherited compClass and are not building ThingComps.
    if prop.startswith("CompProperties_Ability"):
        continue
    if prop not in pairings and prop.startswith("CompProperties_WNG"):
        unpaired.append(prop)
if unpaired:
    notes.append("WNG CompProperties with inherited/indirect compClass: "+", ".join(unpaired))

print("=== D143 BUILDING COMP AUDIT ===")
print(f" - WNG building-like ThingDefs audited: {len(buildings)}")
print(f" - Custom comp XML references checked: {custom_comp_refs}")
print(f" - Source CompProperties->ThingComp pairings found: {len(pairings)}")
print(f" - Transporter-bearing buildings: {transporters}")
print(f" - Launchable buildings: {launchables}")
print(f" - Refuelable buildings: {refuelables}")
print(f" - Power-bearing/proxy buildings: {power_comps}")
print(f" - Facility-bearing buildings: {facility_comps}")
if notes:
    print("\nNOTES:")
    for n in notes: print(" -",n)
if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)
print("PASS: WNG building comp stacks and custom CompProperties/ThingComp pairings are structurally valid.")
