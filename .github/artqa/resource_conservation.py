from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
failures=[]
notes=[]

def load(path):
    try:
        return ET.parse(path).getroot()
    except Exception as exc:
        failures.append(f"{path}: XML parse failure: {exc}")
        return ET.Element("Defs")

def def_map(root, tag):
    out={}
    for node in root.findall(tag):
        name=(node.findtext("defName") or "").strip()
        if name:
            out[name]=node
    return out

rep_path=ROOT/"Defs"/"ThingDefs"/"Races_Replicator.xml"
matter_path=ROOT/"Defs"/"ThingDefs"/"Things_ReplicatorMatter.xml"
toy_path=ROOT/"Defs"/"ThingDefs"/"Race_ChildsToy.xml"
rep_root=load(rep_path)
matter_root=load(matter_path)
toy_root=load(toy_path)
rep_defs=def_map(rep_root,"ThingDef")
matter_defs=def_map(matter_root,"ThingDef")
toy_defs=def_map(toy_root,"ThingDef")

def count_map(elem):
    out={}
    if elem is None:
        return out
    for ch in list(elem):
        try: out[ch.tag]=float((ch.text or "0").strip())
        except: pass
    return out

# --- Corpse/drop contract: Replicators despawn on death and use killedLeavings/split outputs.
base=None
for node in rep_root.findall("ThingDef"):
    if node.get("Name")=="WNG_ReplicatorBlockBase":
        base=node
        break
if base is None:
    failures.append("missing WNG_ReplicatorBlockBase")
else:
    has_corpse=(base.findtext("race/hasCorpse") or "").strip().lower()
    if has_corpse!="false":
        failures.append("WNG_ReplicatorBlockBase must remain hasCorpse=false; conservation audit assumes despawn/drop rather than corpse butchering")

# Loose Blocks -> Drone -> death must remain lossy.
matter=matter_defs.get("WNG_ReplicatorMatter")
reassembly=None
if matter is None:
    failures.append("missing WNG_ReplicatorMatter")
else:
    for li in matter.findall("./comps/li"):
        if (li.get("Class") or "").endswith("CompProperties_ReplicatorMatterReassembly"):
            reassembly=li
            break
if reassembly is None:
    failures.append("WNG_ReplicatorMatter missing reassembly comp")
    consume=0
else:
    consume=int(float((reassembly.findtext("consumePerDrone") or "0").strip()))
    minimum=int(float((reassembly.findtext("minimumStack") or "0").strip()))
    if consume<=0 or minimum<=0:
        failures.append("Replicator Matter reassembly costs must be positive")
    if minimum < consume:
        failures.append(f"Replicator Matter minimumStack={minimum} is below consumePerDrone={consume}")

drone=rep_defs.get("WNG_ReplicatorDrone")
drone_drop=count_map(drone.find("killedLeavings") if drone is not None else None).get("WNG_ReplicatorMatter",0)
if drone_drop<=0:
    failures.append("WNG_ReplicatorDrone must have a positive Replicator Block killedLeavings output")
if consume and drone_drop>=consume:
    failures.append(f"Block reassembly loop is non-lossy: consumes {consume} Blocks per Drone but Drone drops {drone_drop}")

# Hierarchy reverse contract: N lower forms -> one upper form -> exactly N lower forms on genuine death.
hierarchy={}
for name,node in rep_defs.items():
    h=None
    for li in node.findall("./comps/li"):
        if (li.get("Class") or "").endswith("CompProperties_ReplicatorHierarchy"):
            h=li; break
    if h is None:
        continue
    hierarchy[name]={
        "upgrade":(h.findtext("upgradePawnKind") or "").strip(),
        "required":int(float((h.findtext("unitsRequired") or "2").strip())),
        "split":(h.findtext("splitChildPawnKind") or "").strip(),
        "splitCount":int(float((h.findtext("splitCount") or "2").strip())),
    }

reverse_edges=[]
for lower,spec in hierarchy.items():
    upper=spec["upgrade"]
    if not upper:
        continue
    upper_spec=hierarchy.get(upper)
    if upper_spec is None:
        failures.append(f"{lower}: upgrades to {upper}, but upper form lacks hierarchy split contract")
        continue
    reverse_edges.append((lower,upper,spec["required"],upper_spec["split"],upper_spec["splitCount"]))
    if upper_spec["split"] != lower:
        failures.append(f"{lower}->{upper}: reverse split child is {upper_spec['split'] or '<none>'}, expected {lower}")
    if upper_spec["splitCount"] != spec["required"]:
        failures.append(f"{lower}->{upper}: recombination consumes {spec['required']} but death split returns {upper_spec['splitCount']}")

# Terminal recoverable Block yield after repeatedly splitting/killing.
memo={}
def terminal_block_yield(name, stack=None):
    if name in memo:
        return memo[name]
    stack=stack or set()
    if name in stack:
        failures.append("hierarchy yield cycle at "+name)
        return 0
    node=rep_defs.get(name)
    if node is None:
        return 0
    spec=hierarchy.get(name)
    if spec and spec["split"]:
        val=spec["splitCount"]*terminal_block_yield(spec["split"],stack|{name})
    else:
        val=count_map(node.find("killedLeavings")).get("WNG_ReplicatorMatter",0)
    memo[name]=val
    return val

yield_rows={n:terminal_block_yield(n) for n in rep_defs if n.startswith("WNG_Replicator")}

# Controller can be formed from Hunter first, Bulwark second. It must not drop more Blocks
# than the least-mass source can eventually yield.
controller_yield=yield_rows.get("WNG_ReplicatorController",0)
hunter_yield=yield_rows.get("WNG_ReplicatorHunter",0)
bulwark_yield=yield_rows.get("WNG_ReplicatorBulwark",0)
source_floor=min(v for v in (hunter_yield,bulwark_yield) if v>0) if hunter_yield>0 and bulwark_yield>0 else 0
if source_floor and controller_yield>source_floor:
    failures.append(f"Controller formation can amplify Blocks: controller drop={controller_yield}, minimum source terminal yield={source_floor}")

# Child's Toy has no corpse; butcherProducts are therefore not an active normal-death salvage path.
toy=toy_defs.get("WNG_ChildsToy")
if toy is not None:
    if (toy.findtext("race/hasCorpse") or "").strip().lower()!="false":
        failures.append("WNG_ChildsToy must remain hasCorpse=false")
    if toy.find("butcherProducts") is not None:
        notes.append("WNG_ChildsToy has butcherProducts metadata but hasCorpse=false, so normal death does not create a butcherable corpse")

# Recipe conversion graph and Block-specific sinks.
recipe_nodes=[]
block_consumers=[]
block_producers=[]
slurry_producers=[]
for path in sorted((ROOT/"Defs"/"RecipeDefs").glob("*.xml")):
    root=load(path)
    for node in root.findall("RecipeDef"):
        name=(node.findtext("defName") or "").strip()
        if not name:
            continue
        inputs=defaultdict(float)
        for li in node.findall("./ingredients/li"):
            count=float((li.findtext("count") or "0").strip() or 0)
            for e in li.findall("./filter/thingDefs/li"):
                d=(e.text or "").strip()
                if d: inputs[d]+=count
        products=count_map(node.find("products"))
        recipe_nodes.append((name,path,dict(inputs),products))
        if inputs.get("WNG_ReplicatorMatter",0)>0:
            block_consumers.append((name,inputs["WNG_ReplicatorMatter"],products))
        if products.get("WNG_ReplicatorMatter",0)>0:
            block_producers.append((name,products["WNG_ReplicatorMatter"],dict(inputs)))
        if products.get("WNG_AsuranNaniteSlurry",0)>0:
            slurry_producers.append((name,products["WNG_AsuranNaniteSlurry"],dict(inputs)))

if block_producers:
    for name,count,inputs in block_producers:
        failures.append(f"{name}: recipe produces {count} Replicator Blocks; Blocks should enter via explicit drops/story/Asuran reserve source, not a closed crafting loop")

expected_block_sinks={
    "WNG_ReprocessReplicatorMatter":10,
    "WNG_StabilizeAsuranNaniteSlurry":10,
    "WNG_MakeChildsToy":10,
}
by_name={n:(p,i,o) for n,p,i,o in recipe_nodes}
for name,count in expected_block_sinks.items():
    if name not in by_name:
        failures.append(f"missing expected Block-consuming recipe {name}")
    else:
        actual=by_name[name][1].get("WNG_ReplicatorMatter",0)
        if actual!=count:
            failures.append(f"{name}: consumes {actual} Blocks, expected {count}")

# No recipe may close slurry back into Replicator Blocks.
for name,path,inputs,products in recipe_nodes:
    if inputs.get("WNG_AsuranNaniteSlurry",0)>0 and products.get("WNG_ReplicatorMatter",0)>0:
        failures.append(f"{name}: slurry-to-Block crafting closes a material amplification loop")

# Deconstruction refund multipliers may not exceed 100%.
high_refunds=[]
for path in sorted((ROOT/"Defs").rglob("*.xml")):
    try: root=ET.parse(path).getroot()
    except: continue
    for e in root.iter("resourcesFractionWhenDeconstructed"):
        try: v=float((e.text or "0").strip())
        except: continue
        if v>1.0:
            high_refunds.append((path,v))
for path,v in high_refunds:
    failures.append(f"{path}: resourcesFractionWhenDeconstructed={v} exceeds 1.0")

# Transactional production/replacement contracts in C#.
contracts={
    "Source/WraithNaniteGravtech/Replicators/ReplicatorMatterReassembly.cs":[
        "GenPlace.TryPlaceThing", "parent.stackCount -= cost", "Destroy(DestroyMode.Vanish)"
    ],
    "Source/WraithNaniteGravtech/Replicators/ReplicatorHierarchy.cs":[
        "GenPlace.TryPlaceThing", "donor.Destroy(DestroyMode.Vanish)", "splitEmitted"
    ],
    "Source/WraithNaniteGravtech/Replicators/ReplicatorCoordination.cs":[
        "GenPlace.TryPlaceThing", "source.Destroy(DestroyMode.Vanish)"
    ],
    "Source/WraithNaniteGravtech/Replicators/ChildsToy.cs":[
        "GenPlace.TryPlaceThing", "target.Destroy(DestroyMode.Vanish)", "Rollback("
    ],
    "Source/WraithNaniteGravtech/Asurans/AsuranReplicatorMatterFabrication.cs":[
        "stack.stackCount", "GenPlace.TryPlaceThing", "reserve.TrySpend(cost)", "Destroy(DestroyMode.Vanish)"
    ],
}
for rel,tokens in contracts.items():
    p=ROOT/rel
    text=p.read_text(encoding="utf-8",errors="ignore") if p.exists() else ""
    if not text:
        failures.append(f"missing conservation-critical source file {rel}")
        continue
    for token in tokens:
        if token not in text:
            failures.append(f"{rel}: missing conservation transaction token {token}")

# Wraith resource grants/refunds must commit physical stacks before accounting them as restored.
wraith_ecology_path=ROOT/"Source"/"WraithNaniteGravtech"/"Wraith"/"WraithHiveEcology.cs"
wraith_ecology=wraith_ecology_path.read_text(encoding="utf-8",errors="ignore") if wraith_ecology_path.exists() else ""
for token in (
    "public static void SpawnResource",
    "TrySpawnResource(map, near, def, count);",
    "internal static bool TrySpawnResource",
    "bool placed = GenPlace.TryPlaceThing",
    "thing.ParentHolder == null",
    "remaining -= batch",
    "return true;",
):
    if token not in wraith_ecology:
        failures.append(f"Wraith resource placement/refund contract missing: {token}")
if "remaining -= thing.stackCount;\n                GenPlace.TryPlaceThing" in wraith_ecology:
    failures.append("Wraith SpawnResource still accounts a stack before confirming physical placement")

# The Asuran source is intentionally reserve-driven. Its default must be nonzero and output exact.
settings=(ROOT/"Source"/"WraithNaniteGravtech/WNGSettings.cs").read_text(encoding="utf-8",errors="ignore")
ability=(ROOT/"Defs"/"AbilityDefs"/"Abilities_HumanForm.xml")
aroot=load(ability)
adef=None
for n in aroot.findall("AbilityDef"):
    if (n.findtext("defName") or "").strip()=="WNG_AsuranFabricateReplicatorBlocks":
        adef=n; break
if adef is None:
    failures.append("missing WNG_AsuranFabricateReplicatorBlocks")
else:
    props=None
    for li in adef.findall("./comps/li"):
        if (li.get("Class") or "").endswith("CompProperties_AbilityAsuranReplicatorMatter"):
            props=li; break
    blocks=int(float((props.findtext("blockCount") or "0").strip())) if props is not None else 0
    if blocks!=25:
        failures.append(f"Asuran Block fabrication output={blocks}, expected 25")
m=re.search(r"asuranBlockFabricationCost\s*=\s*([0-9.]+)f",settings)
default_cost=float(m.group(1)) if m else -1
if default_cost<=0:
    failures.append(f"Asuran Block fabrication default Nanite Reserve cost must be positive; found {default_cost}")
if 'reserve.TrySpend(cost)' not in (ROOT/"Source"/"WraithNaniteGravtech/Asurans/AsuranReplicatorMatterFabrication.cs").read_text(encoding="utf-8",errors="ignore"):
    failures.append("Asuran Block fabrication does not spend reserve transactionally")
if '"Nanite Reserve cost to create 25 Replicator Blocks"' in settings and ",\n                0f,\n                1f," in settings:
    notes.append("Asuran fabrication slider permits an explicit 0% player override; this is intentional mod-setting behavior, not the default economy")

print("=== D169 RESOURCE CONSERVATION / EXPLOIT AUDIT ===")
print(f" - loose Block reassembly cost: {consume}")
print(f" - Drone Block death drop: {drone_drop}")
print(f" - hierarchy reversible upgrade edges checked: {len(reverse_edges)}")
print(f" - Controller terminal Block drop: {controller_yield}")
print(f" - minimum Controller source terminal Block yield: {source_floor}")
print(f" - Block-consuming production recipes: {len(block_consumers)}")
print(f" - Block-producing production recipes: {len(block_producers)}")
print(f" - slurry-producing recipes: {len(slurry_producers)}")
print(f" - deconstruction multipliers above 100%: {len(high_refunds)}")
print(f" - Asuran fabrication default reserve cost: {default_cost:.2f}")

print("\nREPLICATOR TERMINAL BLOCK YIELDS:")
for name in sorted(yield_rows):
    if yield_rows[name]>0:
        print(f" - {name}: {yield_rows[name]:g}")

if notes:
    print("\nNOTES:")
    for n in notes: print(" -",n)

if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)

print("PASS: audited WNG Block/reassembly/hierarchy/controller/recipe transactions contain no unclassified net-positive Replicator Block loop.")
print("NOTE: environmental Replicator replication and player-configured Asuran reserve-to-Block fabrication are intentional external resource-source mechanics; live tests must verify exact one-time source consumption.")
