from pathlib import Path
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
fail=[]

# Index every shipped Def regardless of folder.
by_type=defaultdict(dict)
for base in (ROOT/"Defs", ROOT/"Compatibility"):
    if not base.exists():
        continue
    for path in base.rglob("*.xml"):
        try:
            root=ET.parse(path).getroot()
        except Exception as exc:
            fail.append(f"{path}: XML parse failure: {exc}")
            continue
        for node in list(root):
            name=(node.findtext("defName") or "").strip()
            if name:
                by_type[node.tag][name]=(path,node)

factions=by_type["FactionDef"]
pawnkinds=by_type["PawnKindDef"]
traders=by_type["TraderKindDef"]
thingdefs=by_type["ThingDef"]
xenotypes=by_type["XenotypeDef"]

expected={
    "WNG_WraithSableBrood":{"family":"wraith","permanent":True,"settles":True,"groups":{"Combat","Settlement"}},
    "WNG_WraithCinderCourt":{"family":"wraith","permanent":False,"settles":True,"groups":{"Combat","Peaceful","Settlement"}},
    "WNG_WraithVeiledHive":{"family":"wraith","permanent":False,"settles":True,"groups":{"Combat","Peaceful","Settlement"}},
    "WNG_WraithPaleCovenant":{"family":"wraith","permanent":False,"settles":True,"groups":{"Combat","Peaceful","Settlement"}},
    "WNG_PrecursorCollective":{"family":"asuran","permanent":True,"settles":True,"groups":{"Combat","Settlement"}},
    "WNG_HumanFormEnclave":{"family":"asuran","permanent":False,"settles":True,"groups":{"Combat","Peaceful","Settlement"}},
    "WNG_ReplicatorSwarm":{"family":"replicator","permanent":True,"settles":False,"groups":{"Combat"}},
    "WNG_MichaelsExperiments":{"family":"whispers","permanent":True,"settles":False,"groups":set()},
}

def bool_text(node,tag,default=False):
    raw=(node.findtext(tag) or "").strip().lower()
    return default if raw=="" else raw=="true"

def float_text(node,tag,default=0.0):
    raw=(node.findtext(tag) or "").strip()
    try:return float(raw) if raw else default
    except:return default

def group_map(node):
    out={}
    for li in node.findall("./pawnGroupMakers/li"):
        kind=(li.findtext("kindDef") or "").strip()
        if not kind: continue
        opts=[]
        options=li.find("options")
        if options is not None:
            for e in list(options):
                opts.append(e.tag)
        out[kind]=opts
    return out

def family_ok(family,kind):
    if family=="wraith":
        return kind.startswith("WNG_Wraith") and kind!="WNG_WraithQueen" or kind=="WNG_WraithQueen"
    if family=="asuran":
        return kind.startswith("WNG_HumanForm") or kind.startswith("WNG_Precursor")
    if family=="replicator":
        return kind.startswith("WNG_Replicator") and kind!="WNG_ReplicatorQueenChild"
    if family=="whispers":
        return kind=="WNG_WhispersHybrid"
    return False

# Canonical FactionDef structure, settlement generation, raid/visitor/trader surfaces.
for name,contract in expected.items():
    entry=factions.get(name)
    if entry is None:
        fail.append(f"missing WNG FactionDef {name}")
        continue
    path,node=entry

    if bool_text(node,"permanentEnemy")!=contract["permanent"]:
        fail.append(f"{name}: permanentEnemy changed")

    weight=float_text(node,"settlementGenerationWeight")
    groups=group_map(node)
    group_names=set(groups)

    if contract["settles"]:
        if weight<=0:
            fail.append(f"{name}: settlement-capable faction has non-positive settlementGenerationWeight")
        if "Settlement" not in group_names:
            fail.append(f"{name}: settlement-capable faction lacks Settlement pawn group")
        if not (node.findtext("settlementNameMaker") or "").strip():
            fail.append(f"{name}: settlement-capable faction lacks settlementNameMaker")
        if not (node.findtext("settlementTexturePath") or "").strip():
            fail.append(f"{name}: settlement-capable faction lacks settlementTexturePath")
    else:
        if weight!=0:
            fail.append(f"{name}: non-settlement faction has settlementGenerationWeight={weight}")
        if "Settlement" in group_names:
            fail.append(f"{name}: non-settlement faction exposes a Settlement pawn group")

    missing_groups=contract["groups"]-group_names
    if missing_groups:
        fail.append(f"{name}: missing expected pawn groups {sorted(missing_groups)}")

    if bool_text(node,"canStageAttacks") and "Combat" not in group_names:
        fail.append(f"{name}: canStageAttacks=true but no Combat pawn group")
    if bool_text(node,"raidsForbidden") and bool_text(node,"canStageAttacks"):
        fail.append(f"{name}: raidsForbidden=true conflicts with canStageAttacks=true")

    basic=(node.findtext("basicMemberKind") or "").strip()
    if not basic or basic not in pawnkinds:
        fail.append(f"{name}: basicMemberKind {basic or '<missing>'} does not resolve")
    elif not family_ok(contract["family"],basic):
        fail.append(f"{name}: basicMemberKind {basic} leaks across faction family boundary")

    for leader in node.findall("./fixedLeaderKinds/li"):
        k=(leader.text or "").strip()
        if k and k not in pawnkinds:
            fail.append(f"{name}: missing fixedLeaderKind {k}")
        elif k and not family_ok(contract["family"],k):
            fail.append(f"{name}: fixedLeaderKind {k} leaks across faction family boundary")

    for group,options in groups.items():
        for kind in options:
            if kind not in pawnkinds:
                fail.append(f"{name}: {group} group references missing PawnKindDef {kind}")
            elif not family_ok(contract["family"],kind):
                fail.append(f"{name}: {group} group contains cross-faction PawnKind {kind}")

    # Every WNG trader kind referenced by the faction must resolve.
    for tag in ("caravanTraderKinds","visitorTraderKinds","baseTraderKinds"):
        for e in node.findall(f"./{tag}/li"):
            t=(e.text or "").strip()
            if t.startswith("WNG_") and t not in traders:
                fail.append(f"{name}: {tag} references missing TraderKindDef {t}")

# Explicit faction identity contracts that prevent cross-faction leakage.
sable=factions.get("WNG_WraithSableBrood")
michael=factions.get("WNG_MichaelsExperiments")
rep=factions.get("WNG_ReplicatorSwarm")
quiet=factions.get("WNG_HumanFormEnclave")

if sable and not bool_text(sable[1],"permanentEnemy"):
    fail.append("Sable Brood must remain permanentEnemy")
if michael:
    n=michael[1]
    if not bool_text(n,"hidden") or not bool_text(n,"raidsForbidden"):
        fail.append("Michael's Experiments must remain hidden and raidsForbidden")
if rep:
    n=rep[1]
    if bool_text(n,"humanlikeFaction",True):
        fail.append("Replicator Swarm must remain non-humanlike")
    if bool_text(n,"canMakeRandomly"):
        fail.append("Replicator Swarm must not be randomly duplicated")
if quiet:
    n=quiet[1]
    expected_trader="WNG_AsuranArtifactExchange"
    for tag in ("caravanTraderKinds","visitorTraderKinds","baseTraderKinds"):
        vals=[(e.text or "").strip() for e in n.findall(f"./{tag}/li")]
        if expected_trader not in vals:
            fail.append(f"Quiet Lattice lost {expected_trader} from {tag}")

# Trader stock references must resolve and remain Asuran-themed where WNG-owned.
for tname,(path,node) in traders.items():
    if not tname.startswith("WNG_"):
        continue
    for thing in node.findall(".//thingDef"):
        ref=(thing.text or "").strip()
        if ref.startswith("WNG_") and ref not in thingdefs:
            fail.append(f"{tname}: stock generator references missing ThingDef {ref}")

# Faction-generated humanlike pawn gear must preserve family identity.
# This does not require every peaceful pawn to be armed; it checks declared WNG apparel/weapon tags.
weapon_candidates=defaultdict(list)
for tname,(path,node) in thingdefs.items():
    for tag in node.findall("./weaponTags/li"):
        val=(tag.text or "").strip()
        if val:
            weapon_candidates[val].append(tname)

for fname,contract in expected.items():
    entry=factions.get(fname)
    if not entry: continue
    _,fnode=entry
    kinds=set()
    basic=(fnode.findtext("basicMemberKind") or "").strip()
    if basic:kinds.add(basic)
    for opts in group_map(fnode).values(): kinds.update(opts)

    for kind in sorted(kinds):
        pk=pawnkinds.get(kind)
        if pk is None: continue
        _,node=pk
        for a in node.findall("./apparelRequired/li"):
            ref=(a.text or "").strip()
            if ref.startswith("WNG_") and ref not in thingdefs:
                fail.append(f"{fname}/{kind}: required apparel {ref} is missing")
        tags=[(x.text or "").strip() for x in node.findall("./weaponTags/li") if (x.text or "").strip()]
        if tags and contract["family"] in ("wraith","asuran"):
            if not any(weapon_candidates.get(tag) for tag in tags):
                fail.append(f"{fname}/{kind}: declared weapon tags have no WNG weapon candidates: {tags}")

# Key source routes must bind to the intended faction and not substitute a neighboring WNG faction.
source_contracts={
    "Source/WraithNaniteGravtech/Wraith/WraithLineagePolitics.cs":[
        "WNG_WraithSableBrood","WNG_WraithCinderCourt","WNG_WraithVeiledHive","WNG_WraithPaleCovenant"
    ],
    "Source/WraithNaniteGravtech/Wraith/WraithPragmaticDiplomacy.cs":[
        "faction.def?.permanentEnemy == true",
        "WraithLineageUtility.SableBroodDefName"
    ],
    "Source/WraithNaniteGravtech/Asurans/HumanFormInfiltration.cs":[
        "WNG_PrecursorCollective"
    ],
    "Source/WraithNaniteGravtech/Compatibility/QuietLatticeStargateVisit.cs":[
        "WNG_HumanFormEnclave"
    ],
    "Source/WraithNaniteGravtech/Replicators/ReplicatorDomains.cs":[
        "WNG_ReplicatorSwarm","EnsureAutonomousSwarmFaction"
    ],
    "Source/WraithNaniteGravtech/Anomaly/WhispersHybrid.cs":[
        "WNG_MichaelsExperiments","WNG_WhispersHybrid"
    ],
    "Source/WraithNaniteGravtech/Compatibility/WraithStargateDiplomacy.cs":[
        "WNG_WraithVeiledHive","WNG_WraithPaleCovenant","PawnGroupKindDefOf.Peaceful"
    ],
}
for rel,tokens in source_contracts.items():
    p=ROOT/rel
    if not p.exists():
        fail.append(f"missing faction routing source {rel}")
        continue
    s=p.read_text(encoding="utf-8",errors="ignore")
    for token in tokens:
        if token not in s:
            fail.append(f"{rel}: faction routing contract missing {token}")

# Wraith politics may initialize mutable inter-lineage relations once, but must not continually
# overwrite player/NPC goodwill after initialization.
politics=(ROOT/"Source/WraithNaniteGravtech/Wraith/WraithLineagePolitics.cs").read_text(encoding="utf-8",errors="ignore")
for token in (
    "relationsInitialized",
    "if (relationsInitialized || Find.TickManager == null)",
    'Scribe_Values.Look(ref relationsInitialized, "wngWraithLineageRelationsInitialized", false)',
):
    if token not in politics:
        fail.append("Wraith goodwill initialization persistence contract missing: "+token)

print("=== D156 FACTION AUDIT ===")
print(f" - WNG FactionDefs checked: {len(expected)}")
print(" - Settlement-capable factions: 6")
print(" - Intentional no-settlement factions: 2")
print(" - Wraith lineages: 4")
print(" - Human-form/Asuran factions: 2")
print(" - Autonomous Replicator factions: 1")
print(" - Hidden Michael-hybrid ownership factions: 1")
print(f" - WNG TraderKindDefs indexed: {sum(1 for x in traders if x.startswith('WNG_'))}")
print(" - Settlement, raid, peaceful visitor, trader, pawn-family, gear and goodwill-routing contracts checked")

if fail:
    print("\nFAILURES:")
    for x in fail: print(" -",x)
    raise SystemExit(1)

print("PASS: WNG faction identities, settlement/raid surfaces, trader references and pawn-family routing are structurally coherent.")
