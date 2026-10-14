from pathlib import Path
import collections
import re
import xml.etree.ElementTree as ET

ROOT = Path(".")
failures = []
warnings = []
notes = []
classes = collections.Counter()
mutation_targets = collections.Counter()

patch_paths = []
for base in (ROOT / "Patches", ROOT / "Compatibility"):
    if not base.exists():
        continue
    for p in base.rglob("*.xml"):
        raw = p.read_text(encoding="utf-8", errors="ignore")
        if "<Patch" in raw or "PatchOperation" in raw:
            patch_paths.append(p)

def fail(msg):
    failures.append(msg)

def warn(msg):
    warnings.append(msg)

def is_wng_target(xpath):
    compact = re.sub(r"\s+", "", xpath)
    return (
        'defName="WNG_' in xpath
        or "defName='WNG_" in xpath
        or 'starts-with(defName,"WNG_")' in compact
        or "starts-with(defName,'WNG_')" in compact
    )

def normalize(xpath):
    return re.sub(r"\s+", "", xpath or "")

# External additive integration points that WNG intentionally extends.
# Anything else touching a non-WNG def must be reviewed and allow-listed.
def allowed_external_add(xpath, value_text):
    compact = normalize(xpath)

    if (
        'RitualOutcomeEffectDef[defName="GravshipLaunch"]/comps' in compact
        or "RitualOutcomeEffectDef[defName='GravshipLaunch']/comps" in compact
    ):
        return (
            "WNG_WraithNavigationCortex" in value_text
            or "WNG_AsuranFlightSubmind" in value_text
            or "WNG_GoauldNavigationCrystal" in value_text
        )

    if "BiomeDef[" in compact and compact.endswith("/wildAnimals"):
        allowed_biomes = (
            'defName="TemperateForest"',
            'defName="TemperateSwamp"',
            'defName="TropicalRainforest"',
            'defName="TropicalSwamp"',
        )
        return any(x in xpath for x in allowed_biomes) and "WNG_Iratus" in value_text

    if "TraderKindDef[" in compact and compact.endswith("/stockGenerators"):
        return "WraithNaniteGravtech.StockGenerator_" in value_text

    return False

# These are container/control operations, not direct mutations by themselves.
control_classes = {
    "PatchOperationSequence",
    "PatchOperationConditional",
    "PatchOperationFindMod",
    "PatchOperationTest",
}

# Standard mutators we understand. Unknown patch operation classes become review failures,
# except CE's own gun converter which is separately scoped to WNG defNames below.
known_mutators = {
    "PatchOperationAdd",
    "PatchOperationReplace",
    "PatchOperationRemove",
    "PatchOperationAttributeSet",
    "PatchOperationInsert",
    "PatchOperationSetName",
}

broad_roots = {
    "Defs/*", "/Defs/*",
    "Defs/ThingDef", "/Defs/ThingDef",
    "Defs/RecipeDef", "/Defs/RecipeDef",
    "Defs/ResearchProjectDef", "/Defs/ResearchProjectDef",
    "Defs/DesignationCategoryDef", "/Defs/DesignationCategoryDef",
}

sensitive_segments = (
    "/recipes", "/recipeUsers", "/designationCategory", "/comps",
    "/researchPrerequisites", "/researchPrerequisite", "/costList",
    "/thingCategories", "/statBases", "/verbs", "/modExtensions",
)

for path in sorted(patch_paths):
    raw = path.read_text(encoding="utf-8", errors="ignore")
    try:
        root = ET.fromstring(raw)
    except ET.ParseError as exc:
        fail(f"{path}: XML parse failure: {exc}")
        continue

    # CE compatibility is gated by LoadFolders.xml, so custom CE operations are legal only here.
    is_ce_path = "Compatibility/CombatExtended/" in path.as_posix()

    for op in root.iter():
        cls = op.attrib.get("Class", "")
        if not cls or ("PatchOperation" not in cls and not cls.startswith("CombatExtended.")):
            continue

        classes[cls] += 1
        xpath = (op.findtext("xpath") or "").strip()
        compact = normalize(xpath)
        value = op.find("value")
        value_text = ET.tostring(value, encoding="unicode") if value is not None else ""

        # Validate control/gating operations.
        if cls in control_classes:
            if cls == "PatchOperationFindMod":
                mods = " ".join((li.text or "") for li in op.findall("./mods/li"))
                if not mods.strip():
                    fail(f"{path}: PatchOperationFindMod has no mod names")
            continue

        # CE's custom converter has no xpath. Its explicit defName must be WNG-owned.
        if cls == "CombatExtended.PatchOperationMakeGunCECompatible":
            target = (op.findtext("defName") or "").strip()
            mutation_targets["CE gun conversion"] += 1
            if not is_ce_path:
                fail(f"{path}: CE converter appears outside the CE-gated compatibility folder")
            if not target.startswith("WNG_"):
                fail(f"{path}: CE converter targets non-WNG def {target}")
            continue

        if cls not in known_mutators:
            fail(f"{path}: unknown/unreviewed patch operation class {cls}")
            continue

        if not xpath:
            fail(f"{path}: {cls} has no xpath")
            continue

        mutation_targets[cls] += 1

        # No operation may target a whole Def collection/root.
        if compact in broad_roots:
            fail(f"{path}: dangerously broad XPath: {xpath}")

        # Broad selectors are legal only when the selected def namespace is explicitly WNG.
        if re.search(r"Defs/(ThingDef|RecipeDef|ResearchProjectDef|DesignationCategoryDef)(?:/|$)", compact):
            if "[" not in compact and not is_wng_target(xpath):
                fail(f"{path}: unbounded Def selector: {xpath}")

        wng_target = is_wng_target(xpath)

        # Removing definitions/data is too destructive for WNG patch files.
        if cls == "PatchOperationRemove":
            fail(f"{path}: destructive PatchOperationRemove is forbidden: {xpath}")
            continue

        # Replacements/attribute changes/inserts may only touch WNG-owned defs.
        if cls in {"PatchOperationReplace", "PatchOperationAttributeSet", "PatchOperationInsert", "PatchOperationSetName"}:
            if not wng_target:
                fail(f"{path}: {cls} may only mutate WNG-owned defs: {xpath}")

        # Additions to external defs require a narrow allow-listed integration point.
        if cls == "PatchOperationAdd" and not wng_target:
            if not allowed_external_add(xpath, value_text):
                fail(f"{path}: unapproved external PatchOperationAdd target: {xpath}")

        # Recipe ownership is never patched on external defs.
        if ("RecipeDef" in compact or "/recipes" in compact or "/recipeUsers" in compact) and not wng_target:
            fail(f"{path}: external recipe ownership/data patch is forbidden: {xpath}")

        # Sensitive list/property mutations on external defs require explicit allow-list.
        if not wng_target and any(seg in compact for seg in sensitive_segments):
            if cls != "PatchOperationAdd" or not allowed_external_add(xpath, value_text):
                fail(f"{path}: sensitive external mutation is not allow-listed: {xpath}")

    # Optional foreign references need a real runtime patch gate.
    # MayRequire on a top-level PatchOperation is ignored by vanilla RimWorld and can
    # leak unresolved ONAC ThingDefs into WNG costLists when ONAC is absent.
    if "ONAC_" in raw:
        has_findmod_gate = (
            "PatchOperationFindMod" in raw
            and "<li>ONAC</li>" in raw
        )
        if not has_findmod_gate:
            fail(f"{path}: ONAC references must be protected by PatchOperationFindMod(ONAC); top-level MayRequire is unsafe")
        if 'MayRequire="idolord.onac"' in raw:
            fail(f"{path}: unsafe top-level MayRequire ONAC gate detected; use PatchOperationFindMod(ONAC)")

    if "CombatExtended." in raw and not is_ce_path:
        fail(f"{path}: Combat Extended references exist outside CE-gated Compatibility/CombatExtended")

notes.append(f"Audited {len(patch_paths)} patch XML files")
notes.append("Operation classes: " + ", ".join(f"{k}={v}" for k, v in sorted(classes.items())))
notes.append("Mutation classes: " + ", ".join(f"{k}={v}" for k, v in sorted(mutation_targets.items())))

print("=== D133 PATCH BLAST-RADIUS AUDIT ===")
for n in notes:
    print(" -", n)
if warnings:
    print("\nWARNINGS:")
    for w in warnings:
        print(" -", w)
if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -", f)
    raise SystemExit(1)

print("PASS: every patch mutation is WNG-scoped or a narrow allow-listed additive integration.")
