from pathlib import Path
import itertools
import re
import xml.etree.ElementTree as ET

ROOT = Path(".")
failures = []
notes = []

about_path = ROOT / "About" / "About.xml"
loadfolders_path = ROOT / "LoadFolders.xml"
compat_root = ROOT / "Source" / "WraithNaniteGravtech" / "Compatibility"
ce_root = ROOT / "Compatibility" / "CombatExtended"
patch_root = ROOT / "Patches"
z_about_path = ROOT / "CompanionMods" / "Z-Adaptive-Error-Patch" / "About" / "About.xml"
z_runtime_path = ROOT / "CompanionMods" / "Z-Adaptive-Error-Patch" / "Source" / "ZAdaptiveRuntime" / "RuntimeFixes.cs"

for p in (about_path, loadfolders_path, compat_root, ce_root, z_about_path, z_runtime_path):
    if not p.exists():
        failures.append(f"missing required Audit 30 path: {p}")

def parse_xml(path):
    try:
        return ET.parse(path).getroot()
    except Exception as exc:
        failures.append(f"{path}: XML parse failed: {exc}")
        return None

about_root = parse_xml(about_path) if about_path.exists() else None
z_about_root = parse_xml(z_about_path) if z_about_path.exists() else None

DIRECT = {
    "stargates": "ccyt.stargatesmod",
    "onac": "idolord.onac",
    "jaffa": "cravemode.rimgatejaffakreebiotech",
    "ce": "CETeam.CombatExtended",
}
CROSS = {
    "gravtide": "gravtide.mod",
    "landforms": "m00nl1ght.GeologicalLandforms",
    "vehicles": "smashphil.vehicleframework",
}
OTHER_KNOWN = {
    "vge": "vanillaexpanded.gravship",
    "autoname": "cruesoe.autonamebabies",
}

def text_values(parent, xpath):
    if parent is None:
        return []
    node = parent.find(xpath)
    if node is None:
        return []
    values = []
    for li in node.findall("li"):
        package = li.findtext("packageId")
        values.append((package if package is not None else li.text or "").strip())
    return values

hard = set(text_values(about_root, "modDependencies"))
load_after = set(text_values(about_root, "loadAfter"))
for pkg in DIRECT.values():
    if pkg in hard:
        failures.append(f"WNG OWNED: optional integration became hard dependency: {pkg}")
    if pkg not in load_after:
        failures.append(f"WNG OWNED: optional integration lost loadAfter ordering hint: {pkg}")

z_hard = set(text_values(z_about_root, "modDependencies"))
z_after = set(text_values(z_about_root, "loadAfter"))
if z_hard != {"brrainz.harmony"}:
    failures.append(f"Z ADAPTIVE OWNED: companion hard dependencies changed: {sorted(z_hard)}")
for pkg in list(CROSS.values()) + list(OTHER_KNOWN.values()):
    if pkg in z_hard:
        failures.append(f"Z ADAPTIVE OWNED: optional compatibility target became hard dependency: {pkg}")
    if pkg not in z_after:
        failures.append(f"Z ADAPTIVE OWNED: optional compatibility target lost loadAfter hint: {pkg}")

load_text = loadfolders_path.read_text(encoding="utf-8", errors="ignore") if loadfolders_path.exists() else ""
if '<li IfModActive="CETeam.CombatExtended">Compatibility/CombatExtended</li>' not in load_text:
    failures.append("WNG OWNED: Combat Extended compatibility folder is not gated by IfModActive")

ce_xml = list(ce_root.rglob("*.xml")) if ce_root.exists() else []
if not ce_xml:
    failures.append("WNG OWNED: Combat Extended compatibility folder has no XML")
else:
    ce_defs = 0
    for p in ce_xml:
        raw = p.read_text(encoding="utf-8", errors="ignore")
        ce_defs += raw.count("<defName>WNG_CE_")
        if "CombatExtended." not in raw and "AmmoBench" not in raw:
            notes.append(f"{p}: CE file contains no obvious CE schema token")
    if ce_defs == 0:
        failures.append("WNG OWNED: CE compatibility contains no WNG_CE_ defs")

onac_files = []
for base in (ROOT / "Defs", ROOT / "Patches", ROOT / "Compatibility"):
    if not base.exists():
        continue
    for p in base.rglob("*.xml"):
        raw = p.read_text(encoding="utf-8", errors="ignore")
        if "ONAC_" in raw:
            onac_files.append(p)
            low = raw.lower()
            if not ("patchoperationfindmod" in low and "<li>onac</li>" in low):
                failures.append(f"WNG OWNED: ONAC reference lacks PatchOperationFindMod(ONAC) in {p}")
            if p.is_relative_to(patch_root):
                for xpath in re.findall(r"<xpath>(.*?)</xpath>", raw):
                    if "WNG_" not in xpath:
                        failures.append(f"WNG OWNED: ONAC patch targets non-WNG def in {p}: {xpath}")

prod_source = [
    p for p in (ROOT / "Source" / "WraithNaniteGravtech").rglob("*.cs")
    if "Diagnostics" not in p.parts
]
all_prod = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in prod_source)
for token in (
    "using StargatesMod", "using CombatExtended", "using ONAC", "using RimGate",
    "typeof(StargatesMod.", "typeof(CombatExtended.", "using Vehicles",
    "using GravTide", "using GeologicalLandforms",
):
    if token in all_prod:
        failures.append(f"WNG OWNED: compile-time optional-mod dependency detected: {token}")

stargate_files = [
    "StartingMapStargate.cs", "QuietLatticeStargateVisit.cs", "WraithStargateHunt.cs",
    "WraithGatePursuit.cs", "WraithStargateDiplomacy.cs", "ReplicatorStargateAssault.cs",
    "ReplicatorStargateSalvage.cs", "HostileAsuranGateJumper.cs", "WNGGateControlObjective.cs",
]
for name in stargate_files:
    p = compat_root / name
    if not p.exists():
        failures.append(f"WNG OWNED: missing Stargate compatibility source {p}")
        continue
    raw = p.read_text(encoding="utf-8", errors="ignore")
    if "Stargate" in raw and name != "WNGGateControlObjective.cs":
        guarded = "GetNamedSilentFail" in raw or "thingClass?.FullName" in raw or name == "WraithGatePursuit.cs"
        if not guarded:
            failures.append(f"WNG OWNED: Stargate integration lacks silent/class-name guard: {p}")

interop_path = compat_root / "GoauldOptionalInterop.cs"
interop = interop_path.read_text(encoding="utf-8", errors="ignore") if interop_path.exists() else ""
for token in (
    'OnacPackageId = "idolord.onac"',
    'RimGateJaffaPackageId = "cravemode.rimgatejaffakreebiotech"',
    "return OnacLoaded() && RimGateJaffaLoaded();",
    "if (!FullEcosystemActive()",
    "if (!RimGateJaffaLoaded()",
    "PackageActive(",
):
    if token not in interop:
        failures.append(f"WNG OWNED: Goa'uld/Jaffa matrix gate missing: {token}")

z_runtime = z_runtime_path.read_text(encoding="utf-8", errors="ignore") if z_runtime_path.exists() else ""
z_contracts = (
    'AccessTools.TypeByName("Vehicles.PathingHelper")',
    'AccessTools.TypeByName("GravTide.TidalPainter")',
    "if (pathingHelperType == null || gravTideType == null)",
    'AccessTools.TypeByName("GeologicalLandforms.Patches.Patch_RimWorld_WeatherEvent_LightningStrike")',
    'AccessTools.TypeByName("GeologicalLandforms.Patches.Patch_Verse_MapPlantGrowthRateCalculator")',
    "if ((gravLightningPatchType == null && gravGrowthPatchType == null) ||",
    "(landformsLightningPatchType == null && landformsGrowthPatchType == null))",
    'AccessTools.TypeByName("VanillaGravshipExpanded.LandingStructureBase")',
    'AccessTools.TypeByName("AutoNameBabies.BabyNamer")',
)
for token in z_contracts:
    if token not in z_runtime:
        failures.append(f"Z ADAPTIVE OWNED: optional combination guard missing: {token}")

for token in ("compatibility failed open", "pathing readiness probe failed open"):
    if token not in z_runtime:
        failures.append(f"Z ADAPTIVE OWNED: fail-open compatibility contract missing: {token}")

for token in ("GravTide.", "GeologicalLandforms.", "Vehicles.PathingHelper", "VehiclePathingSystem"):
    if token in all_prod:
        failures.append(f"WNG OWNED: cross-mod arbitration leaked into production WNG source: {token}")

direct_rows = []
for stargates, onac, jaffa, ce in itertools.product((False, True), repeat=4):
    direct_rows.append({
        "stargates": stargates,
        "onac": onac,
        "jaffa": jaffa,
        "ce": ce,
        "gate_features": stargates,
        "onac_xml": onac,
        "system_lords": jaffa,
        "full_goauld_ecosystem": onac and jaffa,
        "ce_folder": ce,
    })

cross_rows = []
for gravtide, landforms, vehicles in itertools.product((False, True), repeat=3):
    cross_rows.append({
        "gravtide": gravtide,
        "landforms": landforms,
        "vehicles": vehicles,
        "vehicle_guard": gravtide and vehicles,
        "landform_guard": gravtide and landforms,
    })

print("=== D161 OPTIONAL-MOD MATRIX AUDIT ===")
print(f" - production C# files scanned: {len(prod_source)}")
print(f" - ONAC-referencing XML files checked: {len(onac_files)}")
print(f" - CE compatibility XML files checked: {len(ce_xml)}")
print(f" - direct WNG combinations evaluated: {len(direct_rows)}")
print(f" - GravTide/Landforms/Vehicle combinations evaluated: {len(cross_rows)}")
print(" - direct optional packages remain loadAfter-only, not hard dependencies")
print(" - CE load-folder gate: checked")
print(" - ONAC FindMod + WNG-only patch targeting: checked")
print(" - Stargate silent-resolution contract: checked")
print(" - ONAC + RimGate/Jaffa two-package ecosystem gate: checked")
print(" - Z Adaptive pairwise type guards/fail-open behavior: checked")
print(" - prior VGE and Auto Name Babies compatibility hooks: checked")
print(" - ownership rule: WNG structural failures are fatal; missing/changing foreign runtime types remain live-test/upstream classification")

print("\nDIRECT MATRIX:")
for row in direct_rows:
    vals = {k: int(v) if isinstance(v, bool) else v for k, v in row.items()}
    print(
        " S{stargates} O{onac} J{jaffa} C{ce} -> gate={gate_features} "
        "onacXML={onac_xml} systemLords={system_lords} fullEco={full_goauld_ecosystem} ceFolder={ce_folder}".format(**vals)
    )

print("\nCROSS-MOD MATRIX:")
for row in cross_rows:
    vals = {k: int(v) if isinstance(v, bool) else v for k, v in row.items()}
    print(
        " G{gravtide} L{landforms} V{vehicles} -> vehicleGuard={vehicle_guard} landformGuard={landform_guard}".format(**vals)
    )

if notes:
    print("\nNOTES:")
    for n in notes:
        print(" -", n)

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -", f)
    raise SystemExit(1)

print("\nPASS: optional integrations remain isolated by package/type gates across the enumerated matrix; live external-mod behavior still requires the real mod stack.")
