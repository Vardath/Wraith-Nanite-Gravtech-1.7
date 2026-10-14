from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
failures=[]
notes=[]

shuttle_files=[
    ROOT/"Defs/ThingDefs/Shuttle_WraithDart.xml",
    ROOT/"Defs/ThingDefs/Shuttle_WraithStrikeCraft.xml",
    ROOT/"Defs/ThingDefs/Shuttle_WraithCruiser.xml",
    ROOT/"Defs/ThingDefs/Shuttle_PuddleJumper.xml",
    ROOT/"Defs/ThingDefs/Shuttle_Goauld.xml",
    ROOT/"Defs/ThingDefs/Shuttle_WraithStrategic_NPC.xml",
    ROOT/"Defs/ThingDefs/Shuttle_PuddleJumper_NPC.xml",
    ROOT/"Defs/ThingDefs/Shuttle_Goauld_NPC.xml",
    ROOT/"Defs/ThingDefs/Things_ReplicatorQueenRecovery.xml",
]

thingdefs={}
transportdefs={}
worlddefs={}
for path in shuttle_files:
    if not path.exists():
        failures.append(f"missing shuttle file {path}")
        continue
    root=ET.parse(path).getroot()
    for node in list(root):
        name=(node.findtext("defName") or "").strip()
        if not name:
            continue
        if node.tag=="ThingDef": thingdefs[name]=(path,node)
        elif node.tag=="TransportShipDef": transportdefs[name]=(path,node)
        elif node.tag=="WorldObjectDef": worlddefs[name]=(path,node)

def parse_pair(text):
    if not text: return None
    m=re.fullmatch(r"\(\s*([0-9.]+)\s*,\s*([0-9.]+)\s*\)",text.strip())
    if not m: return None
    return float(m.group(1)),float(m.group(2))

def comp_classes(node):
    return [(li.get("Class") or "").strip() for li in node.findall("./comps/li")]

crafts={}
skyfallers={}
for name,(path,node) in thingdefs.items():
    cls=(node.findtext("thingClass") or "").strip()
    if cls=="Building_PassengerShuttle":
        crafts[name]=(path,node)
    if cls in ("PassengerShuttleIncoming","PassengerShuttleLeaving","WraithNaniteGravtech.Skyfaller_WNGWraithDartLeaving"):
        skyfallers[name]=(path,node)

# Every passenger shuttle needs coherent geometry and native shuttle stack.
for name,(path,node) in crafts.items():
    size=parse_pair(node.findtext("size"))
    draw=parse_pair(node.findtext("graphicData/drawSize"))
    if not size:
        failures.append(f"{name}: missing/invalid size ({path})")
    if not draw:
        failures.append(f"{name}: missing/invalid graphic drawSize ({path})")
    if size and draw:
        # draw size may intentionally exceed footprint slightly, but extreme mismatch usually means bad art scale.
        for axis,(s,d) in enumerate(zip(size,draw)):
            if d < s*0.70 or d > s*1.60:
                failures.append(f"{name}: drawSize {draw} is implausible for footprint {size} ({path})")
    classes=comp_classes(node)
    if "CompProperties_Shuttle" not in classes:
        failures.append(f"{name}: passenger shuttle missing CompProperties_Shuttle ({path})")
    if not any(c=="CompProperties_Launchable" or c.endswith("AncientPuddleJumperLaunchable") for c in classes):
        failures.append(f"{name}: passenger shuttle missing launchable comp ({path})")
    if "CompProperties_Transporter" not in classes:
        failures.append(f"{name}: passenger shuttle missing CompProperties_Transporter ({path})")

    # Refuelable is required for player craft and strategic craft except special queen recovery carrier.
    never=(node.findtext("building/neverBuildable") or "").strip().lower()=="true"
    if name!="WNG_AsuranQueenRecoveryCarrier" and "CompProperties_Refuelable" not in classes:
        failures.append(f"{name}: shuttle missing Refuelable comp ({path})")

# TransportShipDef links must resolve and geometry endpoints must exist.
for name,(path,node) in transportdefs.items():
    ship=(node.findtext("shipThing") or "").strip()
    incoming=(node.findtext("arrivingSkyfaller") or "").strip()
    leaving=(node.findtext("leavingSkyfaller") or "").strip()
    world=(node.findtext("worldObject") or "").strip()
    if ship and ship not in thingdefs:
        failures.append(f"{name}: shipThing {ship} does not resolve")
    if incoming and incoming not in thingdefs and incoming!="PassengerShuttleIncoming":
        failures.append(f"{name}: arrivingSkyfaller {incoming} does not resolve")
    if leaving and leaving not in thingdefs and leaving!="PassengerShuttleLeaving":
        failures.append(f"{name}: leavingSkyfaller {leaving} does not resolve")
    if world and world not in worlddefs and world!="PassengerShuttle":
        failures.append(f"{name}: worldObject {world} does not resolve")

# Incoming/leaving defs must preserve usable PassengerShuttle curves and geometry.
for name,(path,node) in skyfallers.items():
    size=parse_pair(node.findtext("size"))
    draw=parse_pair(node.findtext("graphicData/drawSize"))
    if not size:
        failures.append(f"{name}: skyfaller missing/invalid size")
    if not draw:
        failures.append(f"{name}: skyfaller missing/invalid drawSize")
    sky=node.find("skyfaller")
    if sky is None:
        failures.append(f"{name}: skyfaller data missing")
    else:
        for curve in ("rotationCurve","zPositionCurve","speedCurve"):
            if len(sky.findall(f"./{curve}/points/li"))<2:
                failures.append(f"{name}: {curve} has fewer than 2 points")

# For custom craft families, incoming/leaving geometry should stay near landed geometry.
pairs=[
    ("WNG_WraithDart","WNG_WraithDartIncoming","WNG_WraithDartLeaving"),
    ("WNG_WraithStrikeCraft","WNG_WraithStrikeCraftIncoming","WNG_WraithStrikeCraftLeaving"),
    ("WNG_WraithCruiser","WNG_WraithCruiserIncoming","WNG_WraithCruiserLeaving"),
    ("WNG_PuddleJumper","WNG_PuddleJumperIncoming","WNG_PuddleJumperLeaving"),
    ("WNG_AlkeshTransport","WNG_AlkeshIncoming","WNG_AlkeshLeaving"),
    ("WNG_GoauldDeathGlider","WNG_GoauldDeathGliderIncoming","WNG_GoauldDeathGliderLeaving"),
]
for landed,incoming,leaving in pairs:
    if landed not in thingdefs:
        failures.append(f"missing landed craft {landed}")
        continue
    lp=thingdefs[landed][1]
    lsize=parse_pair(lp.findtext("size"))
    ldraw=parse_pair(lp.findtext("graphicData/drawSize"))
    for other in (incoming,leaving):
        if other not in thingdefs:
            failures.append(f"{landed}: missing geometry peer {other}")
            continue
        op=thingdefs[other][1]
        osize=parse_pair(op.findtext("size"))
        odraw=parse_pair(op.findtext("graphicData/drawSize"))
        if lsize and osize and lsize!=osize:
            failures.append(f"{landed}: footprint {lsize} differs from {other} {osize}")
        if ldraw and odraw and any(abs(a-b)>0.01 for a,b in zip(ldraw,odraw)):
            failures.append(f"{landed}: drawSize {ldraw} differs from {other} {odraw}")

# Explicit regression contract: Al'kesh and Death Glider must remain player-visible real craft.
for name in ("WNG_AlkeshTransport","WNG_GoauldDeathGlider"):
    if name not in thingdefs:
        failures.append(f"{name}: missing player craft def")
        continue
    node=thingdefs[name][1]
    if (node.findtext("building/neverBuildable") or "").strip().lower()=="true":
        failures.append(f"{name}: regressed to neverBuildable")
    if (node.findtext("designationCategory") or "").strip()=="":
        failures.append(f"{name}: no Architect designation category")
    if (node.findtext("forceDebugSpawnable") or "").strip().lower()!="true":
        failures.append(f"{name}: not direct-debug spawnable")

# Exact-craft mission code must keep the physical craft through attack passes.
death_path=ROOT/"Source/WraithNaniteGravtech/Goauld/DeathGliderMission.cs"
alkesh_path=ROOT/"Source/WraithNaniteGravtech/Goauld/AlkeshBombingRun.cs"

if not death_path.exists():
    failures.append(f"missing shuttle mission source {death_path}")
else:
    death=death_path.read_text(encoding="utf-8",errors="ignore")
    for token in ("CompTransporter","CompLaunchable","TryBeginPhysicalPass","LandExactCraft"):
        if token not in death:
            failures.append(f"{death_path}: exact-craft sortie missing {token}")

if not alkesh_path.exists():
    failures.append(f"missing shuttle mission source {alkesh_path}")
else:
    alkesh=alkesh_path.read_text(encoding="utf-8",errors="ignore")
    # The Al'kesh same-map bombing pass deliberately does not call CompLaunchable:
    # it nests the exact landed shuttle into a transient Skyfaller and returns the
    # same Thing to the map. Requiring CompLaunchable here would reject the intended
    # physical-pass implementation.
    for token in ("CompTransporter","TryBeginPhysicalPass","innerContainer.TryAdd(craft)","LandExactCraft"):
        if token not in alkesh:
            failures.append(f"{alkesh_path}: exact-craft bombing pass missing {token}")

# Authored art should remain directional and present.
texture_root=ROOT/"Textures"
for name,(path,node) in crafts.items():
    tex=(node.findtext("graphicData/texPath") or "").strip()
    if not tex:
        failures.append(f"{name}: no texPath")
        continue
    candidates=[texture_root/(tex+".png"), texture_root/(tex+"_south.png")]
    if not any(p.exists() for p in candidates):
        failures.append(f"{name}: no matching shuttle texture found for {tex}")

print("=== D145 VEHICLE / SHUTTLE GEOMETRY AUDIT ===")
print(f" - Passenger shuttles audited: {len(crafts)}")
print(f" - TransportShipDefs audited: {len(transportdefs)}")
print(f" - Incoming/leaving skyfallers audited: {len(skyfallers)}")
print(f" - Landed/incoming/leaving geometry families checked: {len(pairs)}")
if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)
print("PASS: shuttle footprints, render scale, launch/transport plumbing and transport-def links are coherent.")
