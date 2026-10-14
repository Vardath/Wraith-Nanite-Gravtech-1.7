from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
failures=[]
notes=[]

# ---------- Index WNG source classes ----------
source_files=[p for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs") if "Diagnostics" not in p.parts]
source="\n".join(p.read_text(encoding="utf-8",errors="ignore") for p in source_files)
class_names=set(re.findall(r"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)",source))

# ---------- WorkType / WorkGiver defs ----------
worktypes={}
workgivers={}
for folder,tag,target in (
    ("Defs/WorkTypeDefs","WorkTypeDef",worktypes),
    ("Defs/WorkGiverDefs","WorkGiverDef",workgivers),
):
    base=ROOT/folder
    if not base.exists():
        continue
    for path in base.rglob("*.xml"):
        root=ET.parse(path).getroot()
        for node in list(root):
            if node.tag!=tag:
                continue
            name=(node.findtext("defName") or "").strip()
            if name.startswith("WNG_"):
                target[name]=(path,node)

if "WNG_AsuranFabrication" not in worktypes:
    failures.append("WNG_AsuranFabrication WorkTypeDef missing")
if "WNG_DoAsuranFabrication" not in workgivers:
    failures.append("WNG_DoAsuranFabrication WorkGiverDef missing")

for name,(path,node) in workgivers.items():
    cls=(node.findtext("giverClass") or "").strip()
    if cls.startswith("WraithNaniteGravtech."):
        short=cls.split(".")[-1]
        if short not in class_names:
            failures.append(f"{name}: giverClass {cls} does not resolve to a WNG source class")

    worktype=(node.findtext("workType") or "").strip()
    if worktype.startswith("WNG_") and worktype not in worktypes:
        failures.append(f"{name}: missing WorkTypeDef {worktype}")

# ---------- Asuran fabrication contract ----------
if "WNG_AsuranFabrication" in worktypes:
    path,node=worktypes["WNG_AsuranFabrication"]
    if (node.findtext("visible") or "").strip().lower()!="false":
        failures.append("WNG_AsuranFabrication must remain hidden")
    if (node.findtext("alwaysStartActive") or "").strip().lower()!="true":
        failures.append("WNG_AsuranFabrication must remain alwaysStartActive")
    if (node.findtext("requireCapableColonist") or "").strip().lower()!="true":
        failures.append("WNG_AsuranFabrication must require a capable colonist")

if "WNG_DoAsuranFabrication" in workgivers:
    path,node=workgivers["WNG_DoAsuranFabrication"]
    users=[(x.text or "").strip() for x in node.findall("./fixedBillGiverDefs/li") if (x.text or "").strip()]
    expected_users=["TableSculpting"]
    if users!=expected_users:
        failures.append(f"WNG_DoAsuranFabrication fixedBillGiverDefs must be exactly {expected_users}, found {users}")
    if (node.findtext("workType") or "").strip()!="WNG_AsuranFabrication":
        failures.append("WNG_DoAsuranFabrication must use WNG_AsuranFabrication work type")

fabrication_source=(ROOT/"Source/WraithNaniteGravtech/Asurans/AsuranFabricationWork.cs").read_text(encoding="utf-8",errors="ignore")
required_source=(
    "public sealed class WorkGiver_DoAsuranFabrication : WorkGiver_DoBill",
    "if (!AsuranCollectiveUtility.IsNaniteSynthetic(pawn))",
    "return null;",
    "return base.JobOnThing(pawn, thing, forced);",
    "pawn.workSettings == null",
    "pawn.WorkTypeIsDisabled(workType)",
    "pawn.workSettings.GetPriority(workType) == 0",
    "pawn.workSettings.SetPriority(workType, Pawn_WorkSettings.DefaultPriority)",
)
for token in required_source:
    if token not in fabrication_source:
        failures.append(f"Asuran fabrication safety contract missing source token: {token}")

# The bridge must not alter vanilla work types or ordinary pawns.
forbidden_source=(
    "DefDatabase<WorkTypeDef>.AllDefsListForReading",
    "pawn.workSettings.SetPriority(WorkTypeDefOf.",
    "WorkTypeDefOf.",
    
)
for token in forbidden_source:
    if token in fabrication_source:
        failures.append(f"Asuran fabrication bridge contains broad/vanilla work mutation: {token}")

# ---------- Recipe gating ----------
required_worktype_recipes=[]
ungated_table_sculpting=[]
for path in (ROOT/"Defs/RecipeDefs").rglob("*.xml"):
    root=ET.parse(path).getroot()
    for node in list(root):
        if node.tag!="RecipeDef":
            continue
        name=(node.findtext("defName") or "").strip()
        if not (name.startswith("WNG_") or name.startswith("Make_WNG_")):
            continue
        required=(node.findtext("requiredGiverWorkType") or "").strip()
        users=[(x.text or "").strip() for x in node.findall("./recipeUsers/li") if (x.text or "").strip()]
        if required=="WNG_AsuranFabrication":
            required_worktype_recipes.append((name,path,users))
            if users!=["TableSculpting"]:
                failures.append(f"{name}: Asuran-gated sculpting recipe must route exactly to TableSculpting, found {users}")
        elif "TableSculpting" in users:
            failures.append(f"{name}: unexpected ungated WNG recipe on vanilla TableSculpting")

# Only the three Asuran trap/reliquary sculpture recipes should use the Asuran-only work type.
expected_gated={"Make_WNG_AsuranSleeperStatue","Make_WNG_AsuranFeederStatue","Make_WNG_AsuranReplicatorReliquary"}
actual_gated={name for name,_,_ in required_worktype_recipes}
if actual_gated!=expected_gated:
    failures.append(f"Asuran-only recipe gate changed: expected {sorted(expected_gated)}, found {sorted(actual_gated)}")


# ---------- All WNG worktable bill assignment ----------
# Vanilla requires a WorkGiver_DoBill.fixedBillGiverDefs route in addition to
# each Building_WorkTable's inspector tab and RecipeDef.recipeUsers.
production_giver = workgivers.get("WNG_DoProductionBills")
expected_production_benches = [
    "WNG_LivingForge", "WNG_WraithGrowthChamber", "WNG_PrecursorFabricator"
]
if production_giver is None:
    failures.append("WNG_DoProductionBills: missing vanilla bill-work assignment")
else:
    _, production = production_giver
    if (production.findtext("giverClass") or "").strip() != "WorkGiver_DoBill":
        failures.append("WNG_DoProductionBills must use vanilla WorkGiver_DoBill")
    if (production.findtext("workType") or "").strip() != "Crafting":
        failures.append("WNG_DoProductionBills must be reachable through ordinary Crafting priority")
    users = [(item.text or "").strip() for item in production.findall("./fixedBillGiverDefs/li")]
    if users != expected_production_benches:
        failures.append(f"WNG production bill giver must cover {expected_production_benches}, got {users}")

# Confirm the exact targets remain real vanilla-style bill workplaces;
# do not accidentally treat beds, gestators or ordinary buildings as bills.
worktables = {}
for path in (ROOT/"Defs/ThingDefs").rglob("*.xml"):
    root = ET.parse(path).getroot()
    for node in root.findall("./ThingDef"):
        name = (node.findtext("defName") or "").strip()
        if name in expected_production_benches:
            worktables[name] = (path, node)
for name in expected_production_benches:
    found = worktables.get(name)
    if found is None:
        failures.append(f"Production bench ThingDef missing: {name}")
        continue
    path, node = found
    if (node.findtext("thingClass") or "").strip() != "Building_WorkTable":
        failures.append(f"{name}: must use vanilla Building_WorkTable ({path})")
    if (node.findtext("hasInteractionCell") or "").lower() != "true":
        failures.append(f"{name}: missing reachable interaction cell ({path})")
    tabs = [x.text for x in node.findall("./inspectorTabs/li")]
    if "ITab_Bills" not in tabs:
        failures.append(f"{name}: missing Bills tab ({path})")

# ---------- JobDef / JobDriver integrity ----------
jobdefs={}
for path in (ROOT/"Defs/JobDefs").rglob("*.xml"):
    root=ET.parse(path).getroot()
    for node in list(root):
        if node.tag!="JobDef":
            continue
        name=(node.findtext("defName") or "").strip()
        if not name.startswith("WNG_"):
            continue
        driver=(node.findtext("driverClass") or "").strip()
        jobdefs[name]=(path,driver)
        if not driver.startswith("WraithNaniteGravtech."):
            failures.append(f"{name}: JobDef does not use a WNG driverClass ({driver})")
        elif driver.split(".")[-1] not in class_names:
            failures.append(f"{name}: driverClass {driver} missing from source")

# Production source references to WNG JobDefs should resolve.
job_lookup_names=set(re.findall(
    r'DefDatabase\s*<\s*JobDef\s*>\s*\.\s*GetNamed(?:SilentFail)?\s*\(\s*"(WNG_[^"]+)"',
    source
))
for name in sorted(job_lookup_names):
    if name not in jobdefs:
        failures.append(f"production source looks up missing JobDef {name}")

# ---------- ThinkTree custom JobGiver integrity ----------
thinktree_refs=[]
thinktree_path=ROOT/"Defs/ThinkTreeDefs"
if thinktree_path.exists():
    for path in thinktree_path.rglob("*.xml"):
        root=ET.parse(path).getroot()
        for elem in root.iter():
            cls=(elem.get("Class") or "").strip()
            if cls.startswith("WraithNaniteGravtech.JobGiver_"):
                thinktree_refs.append((path,cls))
                if cls.split(".")[-1] not in class_names:
                    failures.append(f"{path}: ThinkTree references missing {cls}")

# Guard against WorkGiver/WorkType XML patches that could hijack vanilla work globally.
for base in (ROOT/"Patches",ROOT/"Compatibility"):
    if not base.exists(): continue
    for path in base.rglob("*.xml"):
        raw=path.read_text(encoding="utf-8",errors="ignore")
        if "WorkGiverDef" in raw or "WorkTypeDef" in raw:
            if "WNG_" not in raw:
                failures.append(f"{path}: patches WorkGiver/WorkType without WNG scoping")

# ---------- 2026-10-14 colony job continuity regression ----------
# Child's Toy legitimately performs ordinary hauling using the vanilla mech
# think tree. A removed optional MAP dependency must never inject an
# unresolvable class into that shared vanilla tree again.
toy_root=ET.parse(ROOT/"Defs/ThingDefs/Race_ChildsToy.xml").getroot()
toy=next((n for n in toy_root.findall("./ThingDef")
          if (n.findtext("defName") or "").strip()=="WNG_ChildsToy"),None)
if toy is None:
    failures.append("Child's Toy mech definition missing")
else:
    if (toy.findtext("./race/thinkTreeMain") or "").strip()!="Mechanoid":
        failures.append("Child's Toy must retain the vanilla Mechanoid think tree")
    if [(n.text or "").strip() for n in toy.findall("./race/mechEnabledWorkTypes/li")]!=["Hauling"]:
        failures.append("Child's Toy must retain vanilla mech hauling work")

companion=ROOT/"CompanionMods/Z-Adaptive-Error-Patch"
for patch in (companion/"Patches").rglob("*.xml"):
    raw=patch.read_text(encoding="utf-8",errors="ignore")
    if "MAP_MechanoidMechanitor.JobGiver_MechanoidMechanitorRecreation" in raw:
        failures.append(f"{patch}: dead optional MAP class can destroy vanilla Mechanoid think tree")

continuity=companion/"Source/ZAdaptiveRuntime/JobContinuity20261014.cs"
if not continuity.exists():
    failures.append("Missing narrowly scoped 2026-10-14 job-continuity guard")
else:
    guard=continuity.read_text(encoding="utf-8",errors="ignore")
    for signature in (
        "CheckForAutoAttack", "IdleAutoAttackFinalizer",
        "JobAbility+Job_ExposeData", "TaranJobPostLoadFinalizer",
        "LoadSaveMode.PostLoadInit", '"SelfShutdown"', '"HaulToCell"',
    ):
        if signature not in guard:
            failures.append(f"Job-continuity guard lost required narrow signature: {signature}")

print("=== D142 WORKGIVER / WORKTYPE AUDIT ===")
print(f" - WNG WorkTypeDefs: {len(worktypes)}")
print(f" - WNG WorkGiverDefs: {len(workgivers)}")
print(f" - Asuran-gated recipes: {len(required_worktype_recipes)}")
print(f" - WNG JobDefs checked: {len(jobdefs)}")
print(f" - WNG JobDef runtime lookups checked: {len(job_lookup_names)}")
print(f" - Custom ThinkTree JobGiver references checked: {len(thinktree_refs)}")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: WNG work routing is scoped to intended pawns/benches and all WNG job drivers resolve.")
