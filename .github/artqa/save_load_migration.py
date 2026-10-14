from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(".")
failures=[]
notes=[]

source_root=ROOT/"Source"/"WraithNaniteGravtech"
prod=[p for p in source_root.rglob("*.cs") if "Diagnostics" not in p.parts]
source_by={str(p.relative_to(ROOT)):p.read_text(encoding="utf-8",errors="ignore") for p in prod}
all_source="\n".join(source_by.values())

# 1) Inventory save/load surfaces and verify override methods call their base implementation.
method_pat=re.compile(r"public\s+override\s+void\s+(PostExposeData|ExposeData)\s*\([^)]*\)\s*\{",re.M)
expose_methods=[]
for rel,text in source_by.items():
    for m in method_pat.finditer(text):
        name=m.group(1)
        i=m.end(); depth=1; j=i
        while j<len(text) and depth:
            if text[j]=="{": depth+=1
            elif text[j]=="}": depth-=1
            j+=1
        body=text[i:j-1]
        expose_methods.append((rel,name,body))
        expected="base.PostExposeData();" if name=="PostExposeData" else "base.ExposeData();"
        if expected not in body:
            failures.append(f"{rel}: override {name} does not call {expected}")

scribe_calls=len(re.findall(r"\bScribe_(?:Values|References|Collections|Defs|Deep)\.Look\s*\(",all_source))
postload_guards=len(re.findall(r"Scribe\.mode\s*==\s*LoadSaveMode\.PostLoadInit",all_source))

# 2) Critical systems must retain explicit stable save keys.
critical={
"Source/WraithNaniteGravtech/Asurans/AsuranPatternArchive.cs":[
    'Scribe_Collections.Look(ref snapshots, "wngAsuranPatternSnapshots", LookMode.Deep)',
    'Scribe_Values.Look(ref nextBackupTick, "wngAsuranNextBackupTick", -1)',
    'snapshots.RemoveAll(IsExactQueenSnapshot)',
],
"Source/WraithNaniteGravtech/Replicators/ReplicatorHierarchy.cs":[
    '"wngHierarchyNextAssemblyTick"',
    '"wngHierarchyRecombinationLockedUntil"',
    '"wngHierarchySplitEmitted"',
    '"wngHierarchyLastKnownPosition"',
    '"wngHierarchyLastKnownFaction"',
],
"Source/WraithNaniteGravtech/Replicators/ReplicatorDomains.cs":[
    '"wngReplicatorControlAuthority"',
    '"wngReplicatorDomainId"',
    '"wngReplicatorAuthorityPawn"',
    'EnsureAutonomousSwarmFaction(parent as Pawn)',
],
"Source/WraithNaniteGravtech/Replicators/ReplicatorEnvironmentalAssimilation.cs":[
    '"wngReplicatorStrippedCells"',
    '"wngReplicatorInitialConsumableCells"',
    '"wngReplicatorStoredCellMatter"',
    'strippedCellIndices ??= new List<int>()',
],
"Source/WraithNaniteGravtech/Asurans/ReplicatorQueen.cs":[
    '"wngExactReplicatorQueen"',
    '"wngReplicatorQueenRecurringRecoveryAttemptCount"',
    'LoadSaveMode.PostLoadInit',
],
"Source/WraithNaniteGravtech/Wraith/WraithStrategicHunger.cs":[
    'LoadSaveMode.PostLoadInit',
],
"Source/WraithNaniteGravtech/Asurans/AsuranTechnologyPatternLibrary.cs":[
    '"wngAsuranTechnologyPatterns"',
    '"wngAsuranTechnologyScanTarget"',
    'LoadSaveMode.PostLoadInit',
],
"Source/WraithNaniteGravtech/Goauld/AlkeshBombingRun.cs":[
    '"wngAlkeshBombingPassResolved"',
],
"Source/WraithNaniteGravtech/Goauld/DeathGliderMission.cs":[
    '"wngDeathGliderPassResolved"',
],
}
for rel,tokens in critical.items():
    text=source_by.get(rel,"")
    if not text:
        failures.append(f"critical save/load source missing: {rel}")
        continue
    for token in tokens:
        if token not in text:
            failures.append(f"{rel}: save/load continuity token missing: {token}")

# 3) Backward compatibility bridges explicitly named by the audit plan.
fabrication=source_by.get("Source/WraithNaniteGravtech/Asurans/AsuranFabricationWork.cs","")
for token in (
    "Backward-compatibility bridge for saves created before WNG_AsuranFabrication",
    'GetNamedSilentFail(WorkTypeDefName)',
    "alwaysStartActive",
):
    if token not in fabrication:
        failures.append(f"Asuran fabrication old-save bridge missing: {token}")

settings=source_by.get("Source/WraithNaniteGravtech/WNGSettings.cs","")
for token in (
    "Preserve the original pre-rebuild save keys",
    '"lifeDrainVictimYears"',
    '"replicatorBiologicalPredationThreshold"',
):
    if token not in settings:
        failures.append(f"settings migration continuity missing: {token}")

adapt=source_by.get("Source/WraithNaniteGravtech/Replicators/ReplicatorAdaptation.cs","")
if "Retain the already-issued WNGv1 save key" not in adapt or '"wngReplicatorShieldEvidenceCount"' not in adapt:
    failures.append("Replicator adaptation old-save evidence key is no longer preserved")

adapt_hist=source_by.get("Source/WraithNaniteGravtech/Replicators/ReplicatorAdaptationHistory.cs","")
if "Preserve the historical save keys" not in adapt_hist:
    failures.append("Replicator adaptation-history migration bridge missing")

glider=source_by.get("Source/WraithNaniteGravtech/Goauld/GoauldDeathGliderStrike.cs","")
if "Preserve old saves that already contain a hostile player-Def Glider" not in glider:
    failures.append("Death Glider old-save player/NPC split compatibility guard missing")

starting_gate=source_by.get("Source/WraithNaniteGravtech/Compatibility/StartingMapStargate.cs","")
if "Do not retrofit old saves with a free gate" not in starting_gate:
    failures.append("starting-map Stargate old-save retrofit guard missing")

# 4) Defs referenced by migration bridges still exist.
def_index=set()
for base in (ROOT/"Defs",ROOT/"Compatibility"):
    if not base.exists(): continue
    for p in base.rglob("*.xml"):
        try: root=ET.parse(p).getroot()
        except Exception: continue
        for n in root.iter():
            name=(n.findtext("defName") or "").strip() if len(n) else ""
            if name: def_index.add(name)

for name in (
    "WNG_AsuranFabrication","WNG_DoAsuranFabrication","WNG_AsuranPatternArchive",
    "WNG_ReplicatorSwarm","WNG_ReplicatorDrone","WNG_ReplicatorHunter",
    "WNG_ReplicatorBulwark","WNG_ReplicatorTitan","WNG_ReplicatorSiegeMass",
):
    if name not in def_index:
        failures.append(f"migration-critical Def missing: {name}")

# 5) Faction continuity: Replicator old saves/direct spawns must repair the hidden swarm faction.
domains=source_by.get("Source/WraithNaniteGravtech/Replicators/ReplicatorDomains.cs","")
for token in (
    "public static Faction EnsureSwarmFaction()",
    "public static Faction EnsureAutonomousSwarmFaction(Pawn pawn)",
    "Find.FactionManager",
    "FirstFactionOfDef",
):
    if token not in domains:
        failures.append(f"Replicator faction migration path missing: {token}")

# 6) Collection/reference restoration. Flag mutable collections serialized with Scribe_Collections
# in an override where there is no PostLoadInit/null normalization anywhere in the method.
collection_risk=[]
for rel,name,body in expose_methods:
    if "Scribe_Collections.Look" not in body:
        continue
    if "PostLoadInit" not in body and "??" not in body and "new List" not in body and "new Dictionary" not in body and "new HashSet" not in body:
        collection_risk.append(rel)
# These are review warnings, not automatic defects: some fields are initialized at declaration
# and Scribe preserves a non-null instance on missing legacy keys.
notes.extend("collection review: "+x for x in sorted(set(collection_risk)))

# 7) Current WorkType bridge still targets the actual current def/workgiver pair.
wt=(ROOT/"Defs/WorkTypeDefs/WorkTypes_AsuranFabrication.xml").read_text(encoding="utf-8",errors="ignore")
wg=(ROOT/"Defs/WorkGiverDefs/WorkGivers_AsuranFabrication.xml").read_text(encoding="utf-8",errors="ignore")
if "<defName>WNG_AsuranFabrication</defName>" not in wt or "<workType>WNG_AsuranFabrication</workType>" not in wg:
    failures.append("Asuran fabrication migration bridge no longer matches current WorkType/WorkGiver defs")

print("=== D159 SAVE / LOAD / MIGRATION AUDIT ===")
print(f" - Production C# files scanned: {len(prod)}")
print(f" - ExposeData/PostExposeData overrides checked: {len(expose_methods)}")
print(f" - Scribe Look calls inventoried: {scribe_calls}")
print(f" - PostLoadInit normalization guards inventoried: {postload_guards}")
print(f" - Critical persistent systems checked: {len(critical)}")
print(" - Old Asuran work-type bridge: checked")
print(" - Replicator domain/faction old-save repair: checked")
print(" - Pattern Archive stale-Queen migration: checked")
print(" - Replicator hierarchy/ecology persistence: checked")
print(" - Exact shuttle attack-pass persistence: checked")
print(" - Settings/adaptation historical keys: checked")
print(" - Legacy Death Glider and starting-Stargate compatibility guards: checked")

if notes:
    print(f" - Collection review warnings (non-fatal): {len(notes)}")

if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)

print("PASS: WNG save/load serialization and explicit migration bridges are structurally coherent.")
