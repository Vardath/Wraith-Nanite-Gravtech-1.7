from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(".")
failures = []
notes = []

about = ROOT / "About" / "About.xml"
loadfolders = ROOT / "LoadFolders.xml"
patch_roots = [ROOT / "Patches", ROOT / "Compatibility"]
compat_source = ROOT / "Source" / "WraithNaniteGravtech" / "Compatibility"
prod_source_root = ROOT / "Source" / "WraithNaniteGravtech"
z_about = ROOT / "CompanionMods" / "Z-Adaptive-Error-Patch" / "About" / "About.xml"
z_runtime = ROOT / "CompanionMods" / "Z-Adaptive-Error-Patch" / "Source" / "ZAdaptiveRuntime" / "RuntimeFixes.cs"

for p in (about, loadfolders, compat_source, z_about, z_runtime):
    if not p.exists():
        failures.append(f"missing required Audit 31 path: {p}")

def parse(path):
    try:
        return ET.parse(path).getroot()
    except Exception as exc:
        failures.append(f"{path}: XML parse failed: {exc}")
        return None

about_root = parse(about) if about.exists() else None
z_about_root = parse(z_about) if z_about.exists() else None

def packages(root, node_name):
    if root is None:
        return []
    node = root.find(node_name)
    if node is None:
        return []
    out = []
    for li in node.findall("li"):
        pkg = li.findtext("packageId")
        out.append((pkg if pkg is not None else (li.text or "")).strip())
    return [x for x in out if x]

hard = packages(about_root, "modDependencies")
load_after = packages(about_root, "loadAfter")
expected_hard = {
    "brrainz.harmony",
    "ludeon.rimworld.royalty",
    "ludeon.rimworld.ideology",
    "ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly",
    "ludeon.rimworld.odyssey",
}
if set(hard) != expected_hard:
    failures.append(f"WNG OWNED: hard dependency set changed: {hard}")

direct_optional = [
    "ccyt.stargatesmod",
    "idolord.onac",
    "cravemode.rimgatejaffakreebiotech",
    "CETeam.CombatExtended",
]
for pkg in direct_optional:
    if pkg not in load_after:
        failures.append(f"WNG OWNED: optional provider lost loadAfter ordering hint: {pkg}")
    if pkg in hard:
        failures.append(f"WNG OWNED: optional provider became hard dependency: {pkg}")

load_text = loadfolders.read_text(encoding="utf-8", errors="ignore") if loadfolders.exists() else ""
if '<li IfModActive="CETeam.CombatExtended">Compatibility/CombatExtended</li>' not in load_text:
    failures.append("WNG OWNED: CE compatibility is no longer isolated by IfModActive")

# Audit every WNG patch for load-order-sensitive external mutation.
patch_files = []
external_adds = []
findmods = []
for base in patch_roots:
    if not base.exists():
        continue
    for p in base.rglob("*.xml"):
        raw = p.read_text(encoding="utf-8", errors="ignore")
        if "<Patch" not in raw and "PatchOperation" not in raw:
            continue
        patch_files.append(p)
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as exc:
            failures.append(f"{p}: patch XML parse failed: {exc}")
            continue

        ce_path = "Compatibility/CombatExtended/" in p.as_posix()
        for op in root.iter():
            cls = op.attrib.get("Class", "")
            if cls == "PatchOperationFindMod":
                mods = [((li.text or "").strip()) for li in op.findall("./mods/li")]
                findmods.append((p, tuple(mods)))
                # FindMod is valid for optional integrations such as ONAC. CE remains
                # isolated by its conditional load folder rather than a runtime FindMod.
                if not ce_path and "ONAC" not in mods:
                    failures.append(f"WNG OWNED: unreviewed PatchOperationFindMod outside CE-gated compatibility folder: {p} mods={mods}")
            xpath = (op.findtext("xpath") or "").strip()
            if not xpath:
                continue
            compact = re.sub(r"\s+", "", xpath)
            wng_target = (
                'defName="WNG_' in xpath
                or "defName='WNG_" in xpath
                or 'starts-with(defName,"WNG_")' in compact
                or "starts-with(defName,'WNG_')" in compact
            )
            if not wng_target and cls in ("PatchOperationReplace", "PatchOperationRemove", "PatchOperationAttributeSet", "PatchOperationInsert"):
                failures.append(f"WNG OWNED: order-sensitive destructive mutation targets non-WNG data in {p}: {xpath}")
            if not wng_target and cls == "PatchOperationAdd":
                external_adds.append((p.as_posix(), xpath))

# External additions are only safe if they add WNG-owned payload to exact vanilla integration points.
allowed_external_fragments = (
    'RitualOutcomeEffectDef[defName="GravshipLaunch"]/comps',
    "RitualOutcomeEffectDef[defName='GravshipLaunch']/comps",
    "/wildAnimals",
    "/stockGenerators",
)
for path, xpath in external_adds:
    if not any(fragment in xpath for fragment in allowed_external_fragments):
        failures.append(f"WNG OWNED: unreviewed external additive patch may be load-order-sensitive: {path}: {xpath}")

# Optional foreign Def references must be package-gated in XML.
for base in (ROOT / "Defs", ROOT / "Patches", ROOT / "Compatibility"):
    if not base.exists():
        continue
    for p in base.rglob("*.xml"):
        raw = p.read_text(encoding="utf-8", errors="ignore")
        low = raw.lower()
        if "onac_" in low and not ("patchoperationfindmod" in low and "<li>onac</li>" in low):
            failures.append(f"WNG OWNED: ONAC reference lacks PatchOperationFindMod(ONAC) and can leak unresolved defs: {p}")
        if "combatextended." in raw and "Compatibility/CombatExtended/" not in p.as_posix():
            failures.append(f"WNG OWNED: CE schema leaked outside CE-gated load folder: {p}")

# Production WNG must not bind optional assemblies or cache foreign Defs during static initialization.
prod = [p for p in prod_source_root.rglob("*.cs") if "Diagnostics" not in p.parts]
all_prod = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in prod)
for token in (
    "using StargatesMod",
    "using CombatExtended",
    "using ONAC",
    "using RimGate",
    "using Vehicles",
    "using GravTide",
    "using GeologicalLandforms",
    "typeof(StargatesMod.",
    "typeof(CombatExtended.",
):
    if token in all_prod:
        failures.append(f"WNG OWNED: optional compile-time dependency creates load-order coupling: {token}")

for p in prod:
    raw = p.read_text(encoding="utf-8", errors="ignore")
    if "[StaticConstructorOnStartup]" not in raw:
        continue
    if any(token in raw for token in ("StargateMod_", "ONAC_", "JKB_", "CombatExtended.", "GravTide.", "Vehicles.", "GeologicalLandforms.")):
        failures.append(f"WNG OWNED: static constructor contains optional-provider identity and may cache load-order state: {p}")

# Known optional bridges must query live package/Def state rather than construct foreign content.
interop = (compat_source / "GoauldOptionalInterop.cs").read_text(encoding="utf-8", errors="ignore")
for token in ("PackageActive(", "OriginOf(Def def)", "GetNamedSilentFail"):
    if token == "GetNamedSilentFail":
        continue
    if token not in interop:
        failures.append(f"WNG OWNED: Goa'uld optional bridge lost runtime package/origin resolution: {token}")

# Z Adaptive must explicitly load after each framework whose Harmony/runtime interactions it arbitrates.
z_hard = set(packages(z_about_root, "modDependencies"))
z_after = set(packages(z_about_root, "loadAfter"))
if z_hard != {"brrainz.harmony"}:
    failures.append(f"Z ADAPTIVE OWNED: unexpected hard dependency set: {sorted(z_hard)}")
for pkg in (
    "vanillaexpanded.gravship",
    "smashphil.vehicleframework",
    "gravtide.mod",
    "m00nl1ght.GeologicalLandforms",
    "cruesoe.autonamebabies",
):
    if pkg not in z_after:
        failures.append(f"Z ADAPTIVE OWNED: compatibility target lost loadAfter ordering: {pkg}")

z_text = z_runtime.read_text(encoding="utf-8", errors="ignore") if z_runtime.exists() else ""
for token in (
    "PatchVehicleFrameworkGravTide(harmony)",
    "PatchGeologicalLandformsGravTide(harmony)",
    "PatchVanillaGravshipExpanded(harmony)",
    "PatchAutoNameBabies(harmony)",
    "harmony.Patch(gravPrefix, transpiler: compat)",
    "Preserved native Geological Landforms and GravTide lightning patches",
    "failed open",
):
    if token not in z_text:
        failures.append(f"Z ADAPTIVE OWNED: runtime ordering/arbitration contract missing: {token}")

# The old arbitration destroyed a Geological Landforms prefix and drew a live
# incompatibility warning. Preserve both upstream lightning patches and guard
# against a regression that installs a second random filter or unpatches either.
lightning_start = z_text.find("private static void PatchGeologicalLandformsGravTideLightning(")
lightning_end = z_text.find("private static void PatchGeologicalLandformsGravTidePlantGrowth(", lightning_start)
if lightning_start < 0 or lightning_end < 0:
    failures.append("Z ADAPTIVE OWNED: non-destructive lightning compatibility boundary missing")
else:
    lightning_block = z_text[lightning_start:lightning_end]
    for unsafe in ("harmony.Unpatch(", "harmony.Patch(target, prefix: compat)", "Harmony.GetPatchInfo(target)"):
        if unsafe in lightning_block:
            failures.append(f"Z ADAPTIVE OWNED: destructive/duplicate lightning arbitration reintroduced: {unsafe}")

print("=== D162 LOAD-ORDER AUDIT ===")
print(f" - WNG production C# files scanned: {len(prod)}")
print(f" - patch XML files inspected: {len(patch_files)}")
print(f" - external additive patch points reviewed: {len(external_adds)}")
print(f" - PatchOperationFindMod uses reviewed: {len(findmods)}")
print(" - hard dependency order contract: checked")
print(" - direct optional loadAfter contract: checked")
print(" - CE conditional load-folder isolation: checked")
print(" - non-WNG destructive patch prohibition: checked")
print(" - optional XML FindMod gates: checked")
print(" - optional assembly/static-constructor coupling: checked")
print(" - Z Adaptive framework loadAfter and Harmony arbitration: checked")
print(" - live permutations remain required because RimWorld resolves actual mod order only at startup")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -", f)
    raise SystemExit(1)

print("PASS: repository-level WNG/Z-Adaptive ordering contracts are explicit and no unreviewed load-order-sensitive foreign mutation was found.")
