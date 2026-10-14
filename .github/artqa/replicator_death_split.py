from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(".")
failures=[]

races_path=ROOT/"Defs/ThingDefs/Races_Replicator.xml"
hier_path=ROOT/"Source/WraithNaniteGravtech/Replicators/ReplicatorHierarchy.cs"
if not races_path.exists(): failures.append(f"missing {races_path}")
if not hier_path.exists(): failures.append(f"missing {hier_path}")
if failures:
    for f in failures: print("FAIL:",f)
    raise SystemExit(1)

root=ET.parse(races_path).getroot()
thingdefs={}
pawnkinds={}
for node in list(root):
    name=(node.findtext("defName") or "").strip()
    if node.tag=="ThingDef" and name:
        thingdefs[name]=node
    elif node.tag=="PawnKindDef" and name:
        pawnkinds[name]=node

HIER="WraithNaniteGravtech.CompProperties_ReplicatorHierarchy"
def split_cfg(node):
    if node is None: return None
    for li in node.findall("./comps/li"):
        if (li.get("Class") or "").strip()==HIER:
            return {
                "upgrade":(li.findtext("upgradePawnKind") or "").strip() or None,
                "split":(li.findtext("splitChildPawnKind") or "").strip() or None,
                "count":int((li.findtext("splitCount") or "2").strip()),
                "delay":int((li.findtext("splitRecombineDelayTicks") or "2500").strip()),
            }
    return None

# "Correct children" means the configured physical hierarchy. Drone and Controller
# have no death split; every configured higher/specialist form must emit exactly two.
expected={
    "WNG_ReplicatorDrone":None,
    "WNG_ReplicatorHunter":"WNG_ReplicatorDrone",
    "WNG_ReplicatorBulwark":"WNG_ReplicatorHunter",
    "WNG_ReplicatorTitan":"WNG_ReplicatorBulwark",
    "WNG_ReplicatorSiegeMass":"WNG_ReplicatorTitan",
    "WNG_ReplicatorRepairer":"WNG_ReplicatorDrone",
    "WNG_ReplicatorBurrower":"WNG_ReplicatorDrone",
    "WNG_ReplicatorArtillery":"WNG_ReplicatorHunter",
    "WNG_ReplicatorController":None,
}

configured_splits=0
for parent,child in expected.items():
    node=thingdefs.get(parent)
    if node is None:
        failures.append(f"missing Replicator ThingDef {parent}")
        continue
    cfg=split_cfg(node)

    if child is None:
        # Drone may retain hierarchy only for upward recombination; Controller has no hierarchy comp.
        if cfg is not None and cfg["split"] is not None:
            failures.append(f"{parent}: terminal/non-splitting form unexpectedly has split child {cfg['split']}")
        continue

    configured_splits+=1
    if cfg is None:
        failures.append(f"{parent}: missing hierarchy comp for configured death split")
        continue
    if cfg["split"]!=child:
        failures.append(f"{parent}: split child {cfg['split']} != expected {child}")
    if cfg["count"]!=2:
        failures.append(f"{parent}: split count {cfg['count']} != 2")
    if cfg["delay"]<250:
        failures.append(f"{parent}: split-born recombination lock too short ({cfg['delay']})")
    if child not in pawnkinds:
        failures.append(f"{parent}: split child PawnKindDef {child} missing")

hier=hier_path.read_text(encoding="utf-8",errors="ignore")

# Genuine death owns the split. Ordinary weapon damage (melee, bullet, explosion, fire)
# reaches Pawn.Kill -> Notify_Killed. Direct dev/engine KillFinalize destruction has one
# fallback. The splitEmitted latch prevents the same death from producing children twice.
required_tokens=(
    "public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)",
    "TryEmitDeathSplit(prevMap);",
    "public override void PostDestroy(DestroyMode mode, Map previousMap)",
    "mode == DestroyMode.KillFinalize && !splitEmitted",
    "TryEmitDeathSplit(previousMap);",
    "if (splitEmitted || string.IsNullOrEmpty(Props.splitChildPawnKind) || map == null)",
    "splitEmitted = true;",
    "int count = Math.Max(1, Props.splitCount);",
    "PawnGenerator.GeneratePawn(childKind, faction)",
    "GenPlace.TryPlaceThing(child, origin, map, ThingPlaceMode.Near)",
    "LockRecombination(Props.splitRecombineDelayTicks)",
    "child.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(inheritedAdaptation)",
    "child.TryGetComp<CompReplicatorMaterialProfile>()?.InheritFrom(inheritedMaterial)",
    "child.TryGetComp<CompReplicatorDomain>()?.CopyFrom(inheritedDomain)",
    "Scribe_Values.Look(ref splitEmitted",
)
for token in required_tokens:
    if token not in hier:
        failures.append(f"death/split implementation contract missing: {token}")

# Recombination is a non-death state transition. It must consume donors using Vanish
# so the very same hierarchy comp cannot treat each donor as a genuine death and split it.
if "donor.Destroy(DestroyMode.Vanish)" not in hier:
    failures.append("upward recombination no longer consumes donor bodies with non-death Vanish")

# No generic despawn/map-removal hook may emit death children. Moving a pawn between map,
# holder and world state is not a kill. This protects shuttles/gates/map unload from spawning
# duplicate children during non-death transfer.
for forbidden in (
    "public override void PostDeSpawn",
    "public override void Notify_MapRemoved",
):
    pos=hier.find(forbidden)
    if pos>=0:
        segment=hier[pos:pos+1600]
        if "TryEmitDeathSplit" in segment:
            failures.append(f"non-death lifecycle hook {forbidden} emits a Replicator death split")

# splitEmitted must be checked both in the splitter and the KillFinalize fallback.
if hier.count("splitEmitted") < 6:
    failures.append("splitEmitted duplicate-prevention latch appears incomplete")

# Presentation is only allowed after committed children exist.
commit=hier.find("committedChildren.Add(child)")
vfx=hier.find("PlaySplitVfxFailSoft(map, origin, committedChildren)")
if commit<0 or vfx<0 or vfx<commit:
    failures.append("split VFX is no longer driven by successfully committed physical children")

print("=== D151 REPLICATOR DEATH / SPLIT AUDIT ===")
print(f" - Replicator forms with explicit expected death behavior: {len(expected)}")
print(f" - Forms configured to split on genuine death: {configured_splits}")
print(" - Genuine Pawn.Kill/Notify_Killed split hook: checked")
print(" - Direct KillFinalize fallback: checked")
print(" - Exactly-once splitEmitted latch: checked")
print(" - Split-child domain/adaptation/material inheritance: checked")
print(" - Split-born recombination delay: checked")
print(" - Upward recombination non-death Vanish path: checked")
print(" - Despawn/map-removal non-death isolation: checked")

if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)

print("PASS: configured Replicator death splits are exactly-once, physical, inherited, and isolated from non-death removal/recombination paths.")
