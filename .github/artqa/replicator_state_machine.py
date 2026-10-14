from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(".")
failures=[]
notes=[]

races_path=ROOT/"Defs/ThingDefs/Races_Replicator.xml"
think_path=ROOT/"Defs/ThinkTreeDefs/ThinkTree_Replicator.xml"
jobs_path=ROOT/"Defs/JobDefs/Jobs_Replicator.xml"
faction_path=ROOT/"Defs/FactionDefs/Factions_Replicator.xml"
hier_path=ROOT/"Source/WraithNaniteGravtech/Replicators/ReplicatorHierarchy.cs"
assim_path=ROOT/"Source/WraithNaniteGravtech/Replicators/ReplicatorAssimilation.cs"
env_path=ROOT/"Source/WraithNaniteGravtech/Replicators/ReplicatorEnvironmentalAssimilation.cs"
bio_path=ROOT/"Source/WraithNaniteGravtech/Replicators/ReplicatorBiologicalPredation.cs"
threat_path=ROOT/"Source/WraithNaniteGravtech/Replicators/ReplicatorThreatResponse.cs"
swarm_path=ROOT/"Source/WraithNaniteGravtech/Replicators/ReplicatorSwarmBehavior.cs"

required_files=[races_path,think_path,jobs_path,faction_path,hier_path,assim_path,env_path,bio_path,threat_path,swarm_path]
for p in required_files:
    if not p.exists():
        failures.append(f"missing required Replicator state-machine file: {p}")

if failures:
    for f in failures: print("FAIL:",f)
    raise SystemExit(1)

races_text=races_path.read_text(encoding="utf-8",errors="ignore")
think_text=think_path.read_text(encoding="utf-8",errors="ignore")
jobs_text=jobs_path.read_text(encoding="utf-8",errors="ignore")
faction_text=faction_path.read_text(encoding="utf-8",errors="ignore")
hier=hier_path.read_text(encoding="utf-8",errors="ignore")
assim=assim_path.read_text(encoding="utf-8",errors="ignore")
env=env_path.read_text(encoding="utf-8",errors="ignore")
bio=bio_path.read_text(encoding="utf-8",errors="ignore")
threat=threat_path.read_text(encoding="utf-8",errors="ignore")
swarm=swarm_path.read_text(encoding="utf-8",errors="ignore")

# ---------- Parse Replicator race/PawnKind defs ----------
root=ET.parse(races_path).getroot()
thingdefs={}
pawnkinds={}
for node in list(root):
    name=(node.findtext("defName") or "").strip()
    if node.tag=="ThingDef" and name:
        thingdefs[name]=node
    elif node.tag=="PawnKindDef" and name:
        pawnkinds[name]=node

hier_class="WraithNaniteGravtech.CompProperties_ReplicatorHierarchy"
def hierarchy(node):
    if node is None: return None
    for li in node.findall("./comps/li"):
        if (li.get("Class") or "").strip()==hier_class:
            return {
                "upgrade":(li.findtext("upgradePawnKind") or "").strip() or None,
                "units":int((li.findtext("unitsRequired") or "2").strip()),
                "radius":float((li.findtext("assemblyRadius") or "7").strip()),
                "check":int((li.findtext("assemblyCheckTicks") or "2500").strip()),
                "split":(li.findtext("splitChildPawnKind") or "").strip() or None,
                "splitCount":int((li.findtext("splitCount") or "2").strip()),
                "delay":int((li.findtext("splitRecombineDelayTicks") or "2500").strip()),
            }
    return None

chain=[
    ("WNG_ReplicatorDrone","WNG_ReplicatorHunter",None),
    ("WNG_ReplicatorHunter","WNG_ReplicatorBulwark","WNG_ReplicatorDrone"),
    ("WNG_ReplicatorBulwark","WNG_ReplicatorTitan","WNG_ReplicatorHunter"),
    ("WNG_ReplicatorTitan","WNG_ReplicatorSiegeMass","WNG_ReplicatorBulwark"),
    ("WNG_ReplicatorSiegeMass",None,"WNG_ReplicatorTitan"),
]

for race_name,upgrade,split in chain:
    node=thingdefs.get(race_name)
    if node is None:
        failures.append(f"missing hierarchy ThingDef {race_name}")
        continue
    cfg=hierarchy(node)
    if cfg is None:
        failures.append(f"{race_name}: missing ReplicatorHierarchy comp")
        continue
    if cfg["upgrade"]!=upgrade:
        failures.append(f"{race_name}: upgrade target {cfg['upgrade']} != expected {upgrade}")
    if cfg["split"]!=split:
        failures.append(f"{race_name}: split child {cfg['split']} != expected {split}")
    if upgrade and cfg["units"]!=2:
        failures.append(f"{race_name}: upward recombination must require exactly 2 units, got {cfg['units']}")
    if split and cfg["splitCount"]!=2:
        failures.append(f"{race_name}: reverse split must emit exactly 2 children, got {cfg['splitCount']}")
    if cfg["check"] < 250:
        failures.append(f"{race_name}: assembly check interval too low ({cfg['check']})")
    if split and cfg["delay"] < 250:
        failures.append(f"{race_name}: split/recombine lock too short ({cfg['delay']})")

    kind=pawnkinds.get(race_name)
    if kind is None:
        failures.append(f"missing PawnKindDef {race_name}")
    else:
        if (kind.findtext("race") or "").strip()!=race_name:
            failures.append(f"{race_name}: PawnKind race mismatch")
        if (kind.findtext("defaultFactionDef") or "").strip()!="WNG_ReplicatorSwarm":
            failures.append(f"{race_name}: PawnKind lost WNG_ReplicatorSwarm default faction")

# Specialists must still be real block-state participants and split down to lower forms.
specialists={
    "WNG_ReplicatorRepairer":"WNG_ReplicatorDrone",
    "WNG_ReplicatorBurrower":"WNG_ReplicatorDrone",
    "WNG_ReplicatorArtillery":"WNG_ReplicatorHunter",
}
for name,split in specialists.items():
    cfg=hierarchy(thingdefs.get(name))
    if cfg is None:
        failures.append(f"{name}: specialist lost hierarchy comp")
    elif cfg["split"]!=split or cfg["splitCount"]!=2:
        failures.append(f"{name}: specialist split contract changed ({cfg})")

# ---------- Base ecology/reproduction contract ----------
base_match=re.search(r'<ThingDef Name="WNG_ReplicatorBlockBase"[^>]*>([\s\S]*?)</ThingDef>',races_text)
base=base_match.group(1) if base_match else ""
for token,label in (
    ("<assimilationTicks>300</assimilationTicks>","base assimilation timing"),
    ("<assimilationOffspringCount>2</assimilationOffspringCount>","two-Drone environmental reproduction"),
    ("<hostilePopulationCap>120</hostilePopulationCap>","hostile population cap fallback"),
    ("<assimilationSearchRadius>60</assimilationSearchRadius>","assimilation search radius"),
    ("<selfDefenseTicks>900</selfDefenseTicks>","self-defense memory"),
    ("<swarmAlertTicks>1800</swarmAlertTicks>","swarm alert duration"),
    ("<swarmResponseChance>0.24</swarmResponseChance>","bounded local response chance"),
    ("<swarmResponseRadius>40</swarmResponseRadius>","local distress response radius"),
):
    if token not in base:
        failures.append(f"Replicator base lost {label}: expected {token}")

# Autonomous faction remains hostile by design.
for token,label in (
    ("<permanentEnemy>true</permanentEnemy>","permanent-enemy hostility"),
    ("<hostileToFactionlessHumanlikes>true</hostileToFactionlessHumanlikes>","factionless-humanlike hostility"),
):
    if token not in faction_text:
        failures.append(f"Replicator faction lost {label}")

# ---------- ThinkTree priority/order ----------
tree_root=ET.parse(think_path).getroot()
trees={}
for node in list(tree_root):
    if node.tag=="ThinkTreeDef":
        name=(node.findtext("defName") or "").strip()
        trees[name]=ET.tostring(node,encoding="unicode")

core_order=[
    "JobGiver_ReplicatorDefense",
    "JobGiver_ReplicatorAssimilateStructuralCell",
    "JobGiver_ReplicatorAssimilate",
    "JobGiver_ReplicatorAssimilateCell",
    "JobGiver_ReplicatorAssimilateBiological",
    "JobGiver_ReplicatorBiologicalHunt",
]
for tree_name in ("WNG_ReplicatorAutonomous","WNG_ReplicatorController","WNG_ReplicatorRepairer","WNG_ReplicatorBurrower"):
    text=trees.get(tree_name)
    if not text:
        failures.append(f"missing Replicator ThinkTree {tree_name}")
        continue
    positions=[]
    for name in core_order:
        pos=text.find(name)
        if pos<0:
            failures.append(f"{tree_name}: missing state node {name}")
        positions.append(pos)
    if all(x>=0 for x in positions) and positions!=sorted(positions):
        failures.append(f"{tree_name}: ecology priority order changed; defense/material stripping must precede biological predation")
    if tree_name=="WNG_ReplicatorRepairer":
        rp=text.find("JobGiver_ReplicatorRepair")
        if rp<0 or rp>text.find("JobGiver_ReplicatorAssimilateStructuralCell"):
            failures.append("Repairer no longer prioritizes repair before assimilation")
    if tree_name=="WNG_ReplicatorBurrower":
        bp=text.find("JobGiver_ReplicatorBurrowerBreach")
        if bp<0 or bp>text.find("JobGiver_ReplicatorAssimilateStructuralCell"):
            failures.append("Burrower no longer prioritizes breach role before ordinary assimilation")

# JobDefs for the three ecology phases must resolve.
for job,driver in (
    ("WNG_ReplicatorAssimilate","JobDriver_ReplicatorAssimilate"),
    ("WNG_ReplicatorAssimilateCell","JobDriver_ReplicatorAssimilateCell"),
    ("WNG_ReplicatorAssimilateBiological","JobDriver_ReplicatorAssimilateBiological"),
):
    if f"<defName>{job}</defName>" not in jobs_text or driver not in jobs_text:
        failures.append(f"missing Replicator job/driver contract {job} -> {driver}")

# ---------- 95% transition and matter-first rules ----------
required_env_tokens=(
    "public const float BiologicalPredationThreshold = 0.95f;",
    "InitialConsumableCellCount > 0",
    "StrippedFraction >= WNGSettingsUtility.ReplicatorBiologicalPredationThreshold",
    "map.roofGrid.SetRoof(cell, null)",
    "map.terrainGrid.RemoveTopLayer(cell, doLeavings: false)",
    "map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel)",
    "public const int CellMatterPerDrone = 4;",
)
for token in required_env_tokens:
    if token not in env:
        failures.append(f"environmental state-machine contract missing: {token}")

# Thing assimilation remains non-biological and excludes own faction.
for token in (
    "if (target is Pawn || target is Corpse)",
    "if (target.Faction != null && target.Faction == pawn.Faction)",
    "def.category == ThingCategory.Item",
    "def.category == ThingCategory.Building",
    "def.category == ThingCategory.Plant",
    "AvailablePopulationSlots(parent, offspringCount)",
    "PawnGenerator.GeneratePawn(droneKind, parent.Faction)",
    "Rollback(staged)",
):
    if token not in assim:
        failures.append(f"ordinary assimilation/reproduction contract missing: {token}")

# Biological transition is hostile-only, threshold-gated, reachable/reservable and rechecks
# matter-first eligibility at commit time.
for token in (
    "hunter.Faction.HostileTo(Faction.OfPlayer)",
    "consumption?.BiologicalPredationUnlocked == true",
    "hunter.CanReach(",
    "hunter.CanReserve(",
    "if (prey.Faction != null && prey.Faction == hunter.Faction)",
    "if (ReplicatorAssimilationUtility.IsBlockReplicator(prey))",
    "if (!IsEnvironmentallyStarved(parent)",
    "Rollback(staged)",
    "PawnGenerator.GeneratePawn(",
):
    if token not in bio:
        failures.append(f"biological-predation transition contract missing: {token}")

# ---------- Upward/downward transition transactional safety ----------
for token in (
    "ReplicatorDomainUtility.SameDomain(candidate, leader)",
    "GenPlace.TryPlaceThing(",
    "donor.Destroy(DestroyMode.Vanish)",
    "Notify_Killed",
    "TryEmitDeathSplit",
    "LockRecombination(Props.splitRecombineDelayTicks)",
    "splitEmitted",
    "Scribe_Values.Look(ref nextAssemblyTick",
    "Scribe_Values.Look(ref recombinationLockedUntilTick",
    "Scribe_Values.Look(ref splitEmitted",
):
    if token not in hier:
        failures.append(f"hierarchy transition safety/persistence contract missing: {token}")

# ---------- Local distress response / pathing / cleanup ----------
for token in (
    "RegisterAttack(",
    "swarmResponseChance",
    "swarmResponseRadius",
    "TryGetSelfDefenseTarget",
    "TryGetRetaliationTarget",
    "responder.CanReach(alert.aggressor, PathEndMode.Touch, Danger.Deadly)",
    "alerts.RemoveAt(i)",
    "JobGiver_ReplicatorDefense",
    "victim.Map.GetComponent<MapComponent_ReplicatorSwarmBehavior>()?.RegisterDefensePressure",
):
    if token not in threat:
        failures.append(f"threat/distress response contract missing: {token}")

for token in (
    "PressureEventsForBreach = 3",
    "BreachDurationTicks = 12000",
    "RegisterDefensePressure",
    "ReplicatorSwarmPosture.Breach",
    "ReplicatorSwarmPosture.Harvest",
):
    if token not in swarm:
        failures.append(f"swarm posture state-machine contract missing: {token}")

print("=== D150 REPLICATOR STATE-MACHINE AUDIT ===")
print(" - Core hierarchy: Drone -> Hunter -> Bulwark -> Titan -> Siege Mass")
print(" - Reverse hierarchy: Siege Mass -> 2 Titan -> 2 Bulwark -> 2 Hunter -> 2 Drone")
print(f" - Core hierarchy ThingDefs checked: {len(chain)}")
print(f" - Specialist split states checked: {len(specialists)}")
print(f" - Autonomous/specialist ThinkTrees checked: 4")
print(" - Ecology priority: defense -> structural material -> objects -> cells -> biological")
print(" - Biological transition threshold: 95% stripped-map gate")
print(" - Environmental reproduction: 2 Drone bodies per successful ordinary assimilation, bounded by population cap")
print(" - Local distress response: personal + bounded swarm retaliation")
print(" - Transaction rollback, path reachability/reservation and state persistence contracts checked")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: Replicator hierarchy, ecology transition, reproduction, hostility, distress response, pathing and cleanup contracts are internally coherent.")
