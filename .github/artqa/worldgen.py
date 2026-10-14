from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
fail=[]; notes=[]

def read(path):
    p=ROOT/path
    if not p.exists():
        fail.append("missing "+path)
        return ""
    return p.read_text(encoding="utf-8",errors="ignore")

# Parse all shipped WNG runtime XML.
xmls=[]
for base in (ROOT/"Defs",ROOT/"Patches",ROOT/"Compatibility"):
    if not base.exists(): continue
    for p in base.rglob("*.xml"):
        try:
            xmls.append((p,ET.parse(p).getroot()))
        except Exception as e:
            fail.append(f"XML parse failure {p}: {e}")

# 1) Seven authored starts, with exactly two orbital starts.
scenario_path=ROOT/"Defs/ScenarioDefs/Scenarios_WNG.xml"
scenarios=ET.parse(scenario_path).getroot() if scenario_path.exists() else None
expected={
    "WNG_WraithLandfall":"surface",
    "WNG_WraithOrbit":"orbit",
    "WNG_HumanFormReplicatorLandfall":"surface",
    "WNG_HumanFormReplicatorOrbit":"orbit",
    "WNG_WraithQueenLandfall":"surface",
    "WNG_WraithCommandCell":"surface",
    "WNG_WhispersEscapees":"surface",
}
seen={}
if scenarios is None:
    fail.append("missing WNG scenario file")
else:
    for n in scenarios.findall("ScenarioDef"):
        name=(n.findtext("defName") or "").strip()
        if name: seen[name]=n
    for name,mode in expected.items():
        n=seen.get(name)
        if n is None:
            fail.append("missing scenario "+name); continue
        parts=n.find("./scenario/parts")
        if parts is None:
            fail.append(name+" has no parts"); continue
        forced=[x for x in list(parts) if x.get("Class")=="ScenPart_ForcedMap"]
        custom=[x for x in list(parts) if x.get("Class")=="WraithNaniteGravtech.ScenPart_WNGStartingGravship"]
        planet=[x for x in list(parts) if x.get("Class")=="ScenPart_PlanetLayer"]
        if mode=="orbit":
            if len(forced)!=1 or forced[0].findtext("mapGenerator")!="OrbitalRelay" or forced[0].findtext("layerDef")!="Orbit":
                fail.append(name+" lost exact Orbit/OrbitalRelay forced-map contract")
            if len(custom)!=1:
                fail.append(name+" must have exactly one WNG starter gravship part")
            if len(planet)!=1 or planet[0].findtext("layer")!="Orbit":
                fail.append(name+" lost Orbit planet-layer contract")
        else:
            if forced or custom or planet:
                fail.append(name+" surface start unexpectedly contains orbital map/gravship parts")

# Validate the new caste-specific surface roster without weakening old starts.
for name,kind,xeno,count in (
    ("WNG_WraithQueenLandfall","WNG_WraithQueen","WNG_Wraith",1),
    ("WNG_WraithCommandCell","WNG_WraithCommander","WNG_Wraith",1),
    ("WNG_WhispersEscapees","WNG_WhispersHybrid","WNG_WhispersHybrid",3),
):
    node=seen.get(name)
    if node is None: continue
    part=node.find("./scenario/parts/li[@Class='ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes']")
    if part is None:
        fail.append(name+" lacks native xenotype pawn-selection page")
        continue
    override=part.find("./overrideKinds/li")
    cnt=part.find("./xenotypeCounts/li")
    if override is None or (override.findtext("pawnKind"),override.findtext("xenotype"))!=(kind,xeno):
        fail.append(name+" uses incorrect scenario pawn kind or xenotype")
    if cnt is None or cnt.findtext("xenotype")!=xeno or cnt.findtext("count")!=str(count):
        fail.append(name+" start pawn count or xenotype mismatch")

for name in ("WNG_WraithQueenLandfall","WNG_WraithCommandCell"):
    node=seen.get(name)
    if node is None: continue
    projects={li.findtext("project") for li in node.findall("./scenario/parts/li[@Class='ScenPart_StartingResearch']")}
    required={"WNG_BiologicalCultivation","WNG_LivingWeapons","WNG_FeedingEcology","WNG_BioelectricOrgans"}
    if not required.issubset(projects):
        fail.append(name+" missing basic Wraith research: "+str(sorted(required-projects)))

cmd=seen.get("WNG_WraithCommandCell")
if cmd is not None:
    parts=cmd.findall("./scenario/parts/li")
    classes=[part.get("Class") for part in parts]
    if "WraithNaniteGravtech.ScenPart_WNGStartingCasteReinforcements" not in classes or (
        classes.index("WraithNaniteGravtech.ScenPart_WNGStartingCasteReinforcements")<=
        classes.index("ScenPart_PlayerPawnsArriveMethod")
    ):
        fail.append("Commander Warrior reinforcements must be added after initial pawn arrival")
    extra=cmd.find("./scenario/parts/li[@Class='WraithNaniteGravtech.ScenPart_WNGStartingCasteReinforcements']")
    if extra is not None and (extra.findtext("warriorKindDefName")!="WNG_WraithWarrior" or extra.findtext("warriorCount")!="2"):
        fail.append("Commander extra pawns are not exactly two Wraith Warriors")
    source=read("Source/WraithNaniteGravtech/Scenarios/WNGStartingCasteReinforcements.cs")
    for token in ("override void GenerateIntoMap(Map map)","PawnGenerator.GeneratePawn(kind, Faction.OfPlayer)",
                  "p.kindDef?.defName == \"WNG_WraithCommander\"","GenPlace.TryPlaceThing(warrior, commander.Position",
                  "Scribe_Values.Look(ref delivered"):
        if token not in source:
            fail.append("Commander reinforcement safety contract missing: "+token)

# Five independent, caste-identifiable growth-chamber command icons.
growth=read("Source/WraithNaniteGravtech/Wraith/WraithGrowthChamber.cs")
if 'icon = CasteIcon(casteLabel)' not in growth or 'icon = CasteIcon("Commander")' not in growth:
    fail.append("Wraith gestation commands not linked to per-caste icons")
for caste in ("Hunter","Warrior","Keeper","Commander","Queen"):
    if 'ContentFinder<Texture2D>.Get("UI/WNG/GrowthChamber/Gestate'+caste+'", false)' not in growth:
        fail.append("Growth chamber source missing literal icon: "+caste)
for caste in ("Hunter","Warrior","Keeper","Commander","Queen"):
    icon=ROOT/"Textures/UI/WNG/GrowthChamber"/("Gestate"+caste+".png")
    if not icon.is_file() or icon.stat().st_size<1024:
        fail.append("missing authored growth icon: "+str(icon))
    elif icon.read_bytes()[:8]!=b"\x89PNG\r\n\x1a\n":
        fail.append("invalid PNG header: "+str(icon))

# 2) WNG itself must not globally replace map/world generators.
owned_mapgen=[]
for p,root in xmls:
    for tag in ("MapGeneratorDef","GenStepDef","WorldGenStepDef"):
        for n in root.findall(tag):
            name=(n.findtext("defName") or "").strip()
            if name.startswith("WNG_"):
                owned_mapgen.append((tag,name,str(p)))
if owned_mapgen:
    fail.append("WNG unexpectedly owns global map/world generator defs: "+repr(owned_mapgen))

# 3) Biome patch is narrow: four explicit vanilla biomes, wildAnimals only.
biome_patch=ROOT/"Patches/WNG_IratusBiomeSpawns.xml"
if not biome_patch.exists():
    fail.append("missing Iratus biome patch")
else:
    btxt=biome_patch.read_text(encoding="utf-8",errors="ignore")
    expected_biomes={"TemperateForest","TemperateSwamp","TropicalRainforest","TropicalSwamp"}
    found=set(re.findall(r'BiomeDef\[defName="([^"]+)"\]/wildAnimals',btxt))
    if found!=expected_biomes:
        fail.append(f"Iratus biome patch target set changed: {sorted(found)}")
    xpaths=re.findall(r"<xpath>(.*?)</xpath>",btxt,re.S)
    for xp in xpaths:
        xp=" ".join(xp.split())
        if "/Defs/BiomeDef" not in xp or "/wildAnimals" not in xp or 'defName="' not in xp:
            fail.append("Iratus biome patch contains broad/non-wildAnimals selector: "+xp)

# 4) General WNG patches may not broadly mutate BiomeDef, map generators, planet layers, or world generation.
for p,root in xmls:
    if "Patches" not in p.parts: continue
    raw=p.read_text(encoding="utf-8",errors="ignore")
    for xp in re.findall(r"<xpath>(.*?)</xpath>",raw,re.S):
        xp=" ".join(xp.split())
        if "BiomeDef" in xp and p.name!="WNG_IratusBiomeSpawns.xml":
            fail.append(f"{p}: unexpected BiomeDef patch selector {xp}")
        if any(tok in xp for tok in ("MapGeneratorDef","GenStepDef","WorldGenStepDef","PlanetLayerDef")):
            fail.append(f"{p}: global world/map-generation selector present: {xp}")

# 5) Site/world-map entry points are structurally present. Audit 26 owns worker behavior;
# Worldgen checks that they remain compatible with map generation.
site_defs=[]
for p,root in xmls:
    for n in root.findall("SitePartDef"):
        name=(n.findtext("defName") or "").strip()
        if name.startswith("WNG_"):
            site_defs.append((name,(n.findtext("workerClass") or "").strip(),p))
if len(site_defs)!=12:
    fail.append(f"expected 12 WNG SitePartDefs from audited world/site surface, found {len(site_defs)}")
for name,worker,p in site_defs:
    if not worker.startswith("WraithNaniteGravtech.SitePartWorker_"):
        fail.append(f"{name} has unexpected/missing site map worker {worker!r} ({p})")

# 6) Faction settlement worldgen: settlement-capable factions must have positive generation weight;
# settlement-free Replicator/Michael factions must remain zero.
factions={}
for p,root in xmls:
    for n in root.findall("FactionDef"):
        name=(n.findtext("defName") or "").strip()
        if name.startswith("WNG_"): factions[name]=n
settlement_free={"WNG_ReplicatorSwarm","WNG_MichaelsExperiments"}
for name,n in factions.items():
    weight=float((n.findtext("settlementGenerationWeight") or "1").strip())
    if name in settlement_free:
        if weight!=0:
            fail.append(name+" should remain settlement-free")
    elif name in {
        "WNG_WraithSableBrood","WNG_WraithCinderCourt","WNG_WraithVeiledHive","WNG_WraithPaleCovenant",
        "WNG_PrecursorCollective","WNG_HumanFormEnclave"
    } and weight<=0:
        fail.append(name+" lost settlement generation weight")

# 7) Orbital starter implementation retains safe validation/fallback and exact family lifecycle.
starter=read("Source/WraithNaniteGravtech/Scenarios/WNGStartingGravship.cs")
for token in (
    "if (!ModsConfig.OdysseyActive)",
    "SpawnFallbackDropPods",
    "ResolveAndValidate(out problem)",
    "SpawnVanillaGravshipFallback",
    "ValidateLiveStarterShip",
    "GenStep_ReserveGravshipArea.SetStartSpot",
    "GravshipPlacementUtility.ClearAreaForGravship",
    "RelinkFamilyFacilities",
    "TryPlaceStarterThing",
    "if (!TryPlaceStarterThing(pawn",
    "if (!TryPlaceStarterThing(piece",
):
    if token not in starter:
        fail.append("orbital starter lost safety/lifecycle contract: "+token)

# Starter pawns/cargo must not use one-shot unchecked placement calls: failed placement can
# otherwise silently remove an exact starting pawn or split-off inventory stack from the scenario.
for forbidden in (
    "GenPlace.TryPlaceThing(pawn, cell, map, ThingPlaceMode.Near);",
    "GenPlace.TryPlaceThing(piece, shelf.OccupiedRect().RandomCell, map, ThingPlaceMode.Near);",
):
    if forbidden in starter:
        fail.append("orbital starter retains unchecked placement path: "+forbidden)

# 8) Optional CE scenario patches may add ammo only to the seven WNG scenarios.
ce=ROOT/"Compatibility/CombatExtended/Patches/Scenarios_WNG.xml"
if ce.exists():
    ctxt=ce.read_text(encoding="utf-8",errors="ignore")
    targets=set(re.findall(r'ScenarioDef\[defName="([^"]+)"\]/scenario/parts',ctxt))
    if not targets.issubset(set(expected)):
        fail.append("CE scenario compatibility patches non-WNG scenarios: "+repr(sorted(targets)))

# 9) Known live-stack worldgen race fixes are carried by the separately installable Z Adaptive companion.
zsrc=read("CompanionMods/Z-Adaptive-Error-Patch/Source/ZAdaptiveRuntime/RuntimeFixes.cs")
zabout=read("CompanionMods/Z-Adaptive-Error-Patch/About/About.xml")
zreadme=read("CompanionMods/Z-Adaptive-Error-Patch/README.md")
for token in (
    "PatchVehicleFrameworkGravTide",
    "VehiclePathingReadyPrefix",
    "RecalculatePerceivedPathCostAt",
    "PatchGeologicalLandformsGravTide",
    "GeologicalLandforms.Patches.Patch_RimWorld_WeatherEvent_LightningStrike",
    "GeologicalLandforms.Patches.Patch_Verse_MapPlantGrowthRateCalculator",
    "GravTide.WeatherEvent_LightningStrike_FireEvent_Patch",
    "GravTide.BuildFor_Patch",
    "TileForMapCompat",
):
    if token not in zsrc:
        fail.append("Z Adaptive worldgen compatibility contract missing: "+token)
for pkg in ("gravtide.mod","m00nl1ght.GeologicalLandforms","smashphil.vehicleframework"):
    if pkg not in zabout:
        fail.append("Z Adaptive loadAfter missing "+pkg)
for phrase in ("Vehicle Framework × GravTide","Geological Landforms × GravTide"):
    if phrase not in zreadme:
        fail.append("Z Adaptive README lost documented worldgen guard "+phrase)

print("=== D158 WORLDGEN AUDIT ===")
print(f" - Runtime XML files parsed: {len(xmls)}")
print(f" - WNG player scenarios checked: {len(expected)} (5 surface, 2 orbit)")
print(f" - WNG custom MapGenerator/GenStep/WorldGenStep defs: {len(owned_mapgen)}")
print(" - Iratus biome spawn targets: 4 exact vanilla biomes")
print(f" - WNG SitePart map-generation entry points: {len(site_defs)}")
print(f" - WNG faction defs inspected: {len(factions)}")
print(" - Orbital starter validation/fallback lifecycle: checked")
print(" - CE scenario patch scope: checked")
print(" - Z Adaptive Vehicle Framework/GravTide and Geological Landforms/GravTide guards: checked")

if fail:
    print("\nFAILURES:")
    for x in fail: print(" -",x)
    raise SystemExit(1)
print("PASS: WNG world/map-generation surfaces are narrowly scoped and the known live-stack worldgen compatibility guards are present.")
