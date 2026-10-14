from pathlib import Path
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
failures=[]
notes=[]

files=[
    ROOT/"Defs/ThingDefs/Things_WraithGravEngine.xml",
    ROOT/"Defs/Gravships/Gravship_FamilySystems.xml",
    ROOT/"Defs/Gravships/Gravship_Goauld.xml",
    ROOT/"Defs/Gravships/Gravship_VanillaParity.xml",
    ROOT/"Defs/Gravships/Gravship_FamilySupport.xml",
    ROOT/"Defs/Gravships/Gravship_AncientVacuumShield.xml",
]

defs={}
for path in files:
    if not path.exists():
        failures.append(f"missing gravship def file: {path}")
        continue
    root=ET.parse(path).getroot()
    for node in list(root):
        name=(node.findtext("defName") or "").strip()
        if name:
            defs[name]=(path,node)

families={
    "Wraith": {
        "engine":"WNG_WraithGravEngine",
        "console_candidates":{"WNG_OrganicPilotNode"},
        "place_worker":"WraithNaniteGravtech.PlaceWorker_RequireWraithGravshipSubstructure",
    },
    "Asuran": {
        "engine":"WNG_AsuranGravEngine",
        "console_candidates":{"WNG_PrecursorPilotConsole"},
        "place_worker":"WraithNaniteGravtech.PlaceWorker_RequireAsuranGravshipSubstructure",
    },
    "Goauld": {
        "engine":"WNG_GoauldGravEngine",
        "console_candidates":{"WNG_GoauldPeltac"},
        "place_worker":"WraithNaniteGravtech.PlaceWorker_RequireGoauldGravshipSubstructure",
    },
}

facility_classes={
    "CompProperties_GravshipFacility",
    "WraithNaniteGravtech.CompProperties_WNGGravshipFacility",
    "WraithNaniteGravtech.CompProperties_WNGPilotConsole",
    "WraithNaniteGravtech.CompProperties_WNGFuelTankFacility",
    "WraithNaniteGravtech.CompProperties_WNGThruster",
}

def comps(node):
    return node.findall("./comps/li")

def cls(li):
    return (li.get("Class") or "").strip()

def family_of(node):
    for comp in comps(node):
        f=(comp.findtext("family") or "").strip()
        if f in families:
            return f
    return None

def linkable(engine):
    return {(li.text or "").strip() for li in engine.findall(".//linkableFacilities/li") if (li.text or "").strip()}

for family,cfg in families.items():
    ename=cfg["engine"]
    if ename not in defs:
        failures.append(f"{family}: missing grav engine {ename}")
        continue

    epath,engine=defs[ename]
    links=linkable(engine)
    if not links:
        failures.append(f"{family}: {ename} has empty linkableFacilities")

    # Grav engines themselves use Odyssey's grav-engine/minified placement path and
    # do not need the family's ordinary same-substructure PlaceWorker.
    consoles=[name for name in cfg["console_candidates"] if name in defs]
    if not consoles:
        failures.append(f"{family}: no pilot-console/navigation-control def found from {sorted(cfg['console_candidates'])}")
        continue

    for cname in consoles:
        cpath,console=defs[cname]
        cclasses={cls(x) for x in comps(console)}
        if not (cclasses & facility_classes):
            failures.append(f"{family}: {cname} has no gravship facility comp")
        if cname not in links:
            failures.append(f"{family}: {cname} is not listed in {ename}.linkableFacilities")

        workers={(li.text or "").strip() for li in console.findall("./placeWorkers/li") if (li.text or "").strip()}
        if cfg["place_worker"] not in workers:
            failures.append(f"{family}: {cname} missing same-family substructure place worker")

        fam=family_of(console)
        if fam and fam!=family:
            failures.append(f"{family}: {cname} declares mismatched family {fam}")

        for comp in comps(console):
            if cls(comp) in facility_classes:
                req=(comp.findtext("requiresLOS") or "true").strip().lower()
                if req!="false":
                    failures.append(f"{family}: {cname} facility link still requires LOS")
                dist=(comp.findtext("maxDistance") or "").strip()
                if dist:
                    try:
                        if float(dist)<200:
                            failures.append(f"{family}: {cname} facility range {dist} is too short for gravship linking")
                    except ValueError:
                        failures.append(f"{family}: {cname} has invalid maxDistance={dist!r}")

    # Every same-family network facility in the audited files must be registered on the engine.
    for name,(path,node) in defs.items():
        f=family_of(node)
        if f!=family:
            continue
        cclasses={cls(x) for x in comps(node)}
        if not (cclasses & facility_classes):
            continue
        # Engine itself need not be in its own link list.
        if name==ename:
            continue
        if name not in links:
            failures.append(f"{family}: network facility {name} missing from {ename}.linkableFacilities")

# Seeded Wraith grav-engine maturation contract: the real minified engine must be
# placed and confirmed before the living host is consumed. Unlike Living Forge
# maturation, the grav-engine host intentionally retains the historical Vanish cleanup.
wraith_engine = defs.get("WNG_WraithGravEngine")
if wraith_engine is not None:
    minified_def = (wraith_engine[1].findtext("minifiedDef") or "").strip()
    if minified_def != "MinifiedGravEngine":
        failures.append(f"Wraith: WNG_WraithGravEngine minifiedDef is {minified_def!r}, expected 'MinifiedGravEngine'")

grav_seed_source_path = ROOT/"Source/WraithNaniteGravtech/Wraith/WraithGravEngine.cs"
living_tech_source_path = ROOT/"Source/WraithNaniteGravtech/Wraith/WraithLivingTechnology.cs"
if not grav_seed_source_path.exists():
    failures.append("missing seeded Wraith grav-engine source")
else:
    grav_seed_source = grav_seed_source_path.read_text(encoding="utf-8", errors="ignore")
    start = grav_seed_source.find("class Hediff_GravEngineSeedIncubation")
    end = grav_seed_source.find("class CompProperties_CorpseGravEngineIncubation", start)
    host_segment = grav_seed_source[start:end if end >= 0 else None] if start >= 0 else ""
    if not host_segment:
        failures.append("Wraith: could not isolate Hediff_GravEngineSeedIncubation")
    else:
        prepare_i = host_segment.find("TryPrepareMinifiedGravEngine")
        place_i = host_segment.find("TryPlacePrepared(minified, pawn.Position, pawn.Map, out reason)")
        commit_i = host_segment.find("outputCommitted = true;", place_i)
        cleanup_i = host_segment.find("TryCleanupCommittedHost();", commit_i)
        vanish_i = host_segment.find("pawn.Destroy(DestroyMode.Vanish)")
        if min(prepare_i, place_i, commit_i, cleanup_i, vanish_i) < 0:
            failures.append("Wraith: seeded grav-engine host maturation is missing prepare/place/commit/vanish lifecycle steps")
        elif not (prepare_i < place_i < commit_i < cleanup_i):
            failures.append("Wraith: seeded grav-engine host can commit/cleanup before the minified engine placement transaction")
        if "pawn.Kill(" in host_segment:
            failures.append("Wraith: seeded grav-engine host cleanup changed from Vanish to pawn.Kill; keep the historical despawn contract")

if not living_tech_source_path.exists():
    failures.append("missing Wraith living-technology output utility source")
else:
    living_tech_source = living_tech_source_path.read_text(encoding="utf-8", errors="ignore")
    for token in ("GenPlace.TryPlaceThing(minified, position, map, ThingPlaceMode.Near)",
                  "!minified.Spawned",
                  "minified.Map != map"):
        if token not in living_tech_source:
            failures.append(f"Wraith: minified living-technology placement lacks committed-spawn guard {token}")

# Cross-family contamination: an engine must not advertise another family's facilities.
family_by_def={}
for name,(path,node) in defs.items():
    fam=family_of(node)
    if fam:
        family_by_def[name]=fam

for family,cfg in families.items():
    ename=cfg["engine"]
    if ename not in defs:
        continue
    links=linkable(defs[ename][1])
    for target in links:
        tfam=family_by_def.get(target)
        if tfam and tfam!=family:
            failures.append(f"{family}: {ename} cross-links {target} from family {tfam}")

# Substructure placement must be family-specific for key hull/door/console/engine components.
key_tokens=("PilotConsole","PilotNode","Peltac","FlightSubmind","NavigationCrystal","GravshipDoor","VacBarrier")
for name,(path,node) in defs.items():
    fam=family_of(node)
    if fam not in families:
        continue
    if not any(tok in name for tok in key_tokens):
        continue
    expected=families[fam]["place_worker"]
    workers={(li.text or "").strip() for li in node.findall("./placeWorkers/li") if (li.text or "").strip()}
    if expected not in workers:
        failures.append(f"{fam}: key component {name} missing {expected}")

print("=== D144 GRAVSHIP CONNECTIVITY AUDIT ===")
for family,cfg in families.items():
    if cfg["engine"] in defs:
        links=linkable(defs[cfg["engine"]][1])
        print(f" - {family}: engine={cfg['engine']}, linkableFacilities={len(links)}")
print(f" - Gravship defs indexed: {len(defs)}")
print(f" - Family-tagged network defs: {len(family_by_def)}")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: Wraith, Asuran and Goa'uld grav engines, pilot controls and family facilities are registered consistently.")
