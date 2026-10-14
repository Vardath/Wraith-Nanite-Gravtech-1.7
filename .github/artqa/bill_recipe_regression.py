from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(".")
failures = []
warnings = []
notes = []

def fail(msg):
    failures.append(msg)

def warn(msg):
    warnings.append(msg)

def note(msg):
    notes.append(msg)

# ---------- C# recipe availability audit ----------
cs_files = list((ROOT / "Source").rglob("*.cs"))
availability_methods = []
classes = {}

class_pat = re.compile(r"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([^\n\r{]+)")
method_pat = re.compile(r"public\s+override\s+bool\s+AvailableOnNow\s*\(\s*Thing\s+thing\s*,\s*BodyPartRecord\s+part\s*=\s*null\s*\)")

def method_body(text, start):
    brace = text.find("{", start)
    if brace < 0:
        return None, -1
    depth = 0
    for i in range(brace, len(text)):
        ch = text[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace + 1:i], i
    return None, -1

for path in cs_files:
    text = path.read_text(encoding="utf-8", errors="ignore")
    class_matches = list(class_pat.finditer(text))
    for cm in class_matches:
        classes[cm.group(1)] = (path, cm.group(2).strip())

    for mm in method_pat.finditer(text):
        body, end = method_body(text, mm.end())
        if body is None:
            fail(f"Could not parse AvailableOnNow body in {path}")
            continue

        containing = None
        for cm in class_matches:
            if cm.start() < mm.start():
                containing = cm
            else:
                break
        cls = containing.group(1) if containing else "<unknown>"
        base = containing.group(2).strip() if containing else "<unknown>"
        availability_methods.append((path, cls, base, body))

        base_idx = body.find("base.AvailableOnNow")
        pawn_decl = body.find("Pawn pawn = thing as Pawn")
        pawn_guard_match = re.search(r"if\s*\(\s*pawn\s*==\s*null\b", body)
        thing_guard_match = re.search(r"if\s*\(\s*thing\s*==\s*null\b", body)
        pawn_guard = pawn_guard_match.start() if pawn_guard_match else -1
        thing_guard = thing_guard_match.start() if thing_guard_match else -1

        # Every WNG availability override that delegates to vanilla must reject an
        # invalid target before the vanilla call. This protects recipe enumeration.
        if base_idx >= 0:
            explicit_guard = (
                (pawn_decl >= 0 and pawn_guard >= 0 and pawn_guard < base_idx) or
                (thing_guard >= 0 and thing_guard < base_idx)
            )
            if not explicit_guard:
                fail(f"{cls} in {path} calls base.AvailableOnNow before an explicit null/type guard")

        # Surgery workers must always have an explicit pawn guard even when helper
        # methods happen to tolerate null today.
        if any(tok in base for tok in ("Recipe_Surgery", "Recipe_InstallArtificialBodyPart")):
            if pawn_decl < 0 or pawn_guard < 0:
                fail(f"{cls} in {path} lacks explicit Pawn/null guard")

# Direct production RecipeWorker subclasses are the highest-risk bill-menu path.
production_workers = []
for cls, (path, base) in sorted(classes.items()):
    if re.search(r"(^|\.)RecipeWorker\b", base) and "Recipe_Surgery" not in base:
        production_workers.append((cls, path, base))

expected_production_workers = set()
found_production = {x[0] for x in production_workers}
missing = expected_production_workers - found_production
if missing:
    fail("Missing expected production RecipeWorker class(es): " + ", ".join(sorted(missing)))

# Make sure every direct production worker actually owns its availability behavior.
avail_classes = {cls for _, cls, _, _ in availability_methods}
for cls, path, base in production_workers:
    if cls not in avail_classes:
        fail(f"Production RecipeWorker {cls} in {path} has no audited AvailableOnNow override")

note(f"Enumerated {len(availability_methods)} WNG AvailableOnNow override(s)")
note("Production RecipeWorkers: " + ", ".join(sorted(found_production)))

# ---------- Guard against global bill/recipe monkey-patching ----------
source_text = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in cs_files if "Diagnostics" not in p.parts)
forbidden_source = {
    "Harmony patch of Bill*": r"HarmonyPatch\s*\(\s*typeof\s*\(\s*Bill",
    "Harmony patch of Recipe*": r"HarmonyPatch\s*\(\s*typeof\s*\(\s*Recipe",
    "Harmony patch of Building_WorkTable": r"HarmonyPatch\s*\(\s*typeof\s*\(\s*Building_WorkTable",
    "Harmony patch of ITab_Bills": r"HarmonyPatch\s*\(\s*typeof\s*\(\s*ITab_Bills",
    "global AllRecipes access": r"\bAllRecipes\b",
    "runtime recipeUsers clear": r"recipeUsers\s*\.\s*Clear\s*\(",
    "runtime recipeUsers assignment": r"recipeUsers\s*=",
    "runtime recipes clear": r"\.recipes\s*\.\s*Clear\s*\(",
}
for label, pat in forbidden_source.items():
    if re.search(pat, source_text):
        fail(f"Unsafe global recipe/bill interference detected: {label}")

# ---------- XML/patch non-interference ----------
representative = {
    "ElectricStove", "FueledStove", "HandTailoringBench", "ElectricTailoringBench",
    "FabricationBench", "DrugLab", "ElectricSmelter", "TableSculpting", "TableMachining", "MechGestator", "AmmoBench"
}

patch_files = []
for base in (ROOT / "Patches", ROOT / "Compatibility"):
    if not base.exists():
        continue
    for path in base.rglob("*.xml"):
        raw = path.read_text(encoding="utf-8", errors="ignore")
        if "<Patch" not in raw and "PatchOperation" not in raw:
            continue
        patch_files.append(path)
        for xpath in re.findall(r"<xpath>(.*?)</xpath>|xpath\s*=\s*[\"'](.*?)[\"']", raw, flags=re.S):
            expr = next((x for x in xpath if x), "")
            compact = re.sub(r"\s+", "", expr)
            if "RecipeDef" in compact:
                fail(f"{path} patches RecipeDef data directly: {expr.strip()}")
            for bench in representative:
                if bench in compact and ("recipes" in compact or "recipeUsers" in compact or "ThingDef" in compact):
                    fail(f"{path} patches representative bench {bench}: {expr.strip()}")

# Parse all runtime def XML, excluding patch-operation documents.
def_roots = []
for base in (ROOT / "Defs", ROOT / "Compatibility"):
    if not base.exists():
        continue
    for path in base.rglob("*.xml"):
        raw = path.read_text(encoding="utf-8", errors="ignore")
        if "<Patch" in raw or "PatchOperation" in raw:
            continue
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as exc:
            fail(f"XML parse failure {path}: {exc}")
            continue
        def_roots.append((path, root))

allowed_external_users = {
    "Human",
}

# Intentional additive WNG integration with vanilla workbenches.
# These routes are allowed only for the named WNG recipes; all other external
# recipe-user additions remain regression failures.
approved_external_routes = {
    "WNG_MakeChildsToy": {"MechGestator"},
    "WNG_Make_KassaConcentrate": {"DrugLab"},
    "WNG_Make_KassaDistillate": {"DrugLab"},
    "WNG_Make_Roshna": {"DrugLab"},
    "WNG_Make_IratusParalytic": {"DrugLab"},
    "WNG_MakeIratusPerfume": {"DrugLab"},
    "WNG_Make_IratusQueenRestorative": {"DrugLab"},
    "WNG_Make_HybridStabiliserDose": {"DrugLab"},
    "WNG_Make_HoffanSerum": {"DrugLab"},
    "WNG_Make_WraithRetrovirusDose": {"DrugLab"},
    "WNG_Make_WraithSuppressionDose": {"DrugLab"},
    "WNG_Make_FeedingIndependenceVector": {"DrugLab"},
    "WNG_Make_VitalResistanceDose": {"DrugLab"},
    "WNG_Make_RefinedWraithEnzyme": {"DrugLab"},
    "WNG_Make_WraithEnzymeWeaningSerum": {"DrugLab"},
    "WNG_Make_WhispersAcousticNode": {"DrugLab"},
    "WNG_Make_WhispersMistGland": {"DrugLab"},
    "WNG_RenderWraithEnzymeFromCorpse": {"DrugLab"},
    "WNG_Make_NishtaCanister": {"TableMachining"},
    "WNG_ReprocessReplicatorMatter": {"ElectricSmelter"},
    "WNG_DestroyReplicatorCoreFragment": {"ElectricSmelter"},
    "Make_WNG_AsuranSleeperStatue": {"TableSculpting"},
    "Make_WNG_AsuranFeederStatue": {"TableSculpting"},
    "Make_WNG_AsuranReplicatorReliquary": {"TableSculpting"},
    "WNG_CE_Make_ReplicatorPulseCell": {"AmmoBench"},
    "WNG_CE_Make_ReplicatorDisruptorCell": {"AmmoBench"},
    "WNG_CE_Make_ReplicatorArtilleryCell": {"AmmoBench"},
}

def external_route_allowed(recipe, user):
    return user in allowed_external_users or user in approved_external_routes.get(recipe, set())

bench_additions = {b: [] for b in representative}
custom_worker_refs = []

for path, root in def_roots:
    for node in list(root):
        defname = node.findtext("defName") or ""
        if node.tag == "RecipeDef":
            wc = (node.findtext("workerClass") or "").strip()
            if wc.startswith("WraithNaniteGravtech."):
                custom_worker_refs.append((defname, wc.split(".")[-1], path))
            for li in node.findall("./recipeUsers/li"):
                user = (li.text or "").strip()
                if user in bench_additions:
                    bench_additions[user].append((defname, path))
                if user and not user.startswith("WNG_") and not external_route_allowed(defname, user):
                    fail(f"WNG RecipeDef {defname} targets unapproved external recipe user {user} ({path})")

        if node.tag == "ThingDef":
            maker = node.find("./recipeMaker")
            if maker is not None and (maker.get("IsNull") or "").lower() != "true":
                for li in maker.findall("./recipeUsers/li"):
                    user = (li.text or "").strip()
                    if user in bench_additions:
                        bench_additions[user].append((defname + " [recipeMaker]", path))
                    if user and not user.startswith("WNG_") and not external_route_allowed(defname + " [recipeMaker]", user):
                        fail(f"WNG ThingDef {defname} recipeMaker targets unapproved external recipe user {user} ({path})")

# Vanilla workbench integration is additive and bounded by the explicit allowlist.
seen_external_routes = set()
for bench in sorted(representative):
    for recipe, path in bench_additions[bench]:
        seen_external_routes.add((recipe, bench))
        if not external_route_allowed(recipe, bench):
            fail(f"WNG recipe {recipe} has unapproved additive route to {bench} ({path})")
    note(f"{bench}: {len(bench_additions[bench])} approved additive WNG recipe(s)")

for recipe, users in approved_external_routes.items():
    for user in users:
        if user in representative and (recipe, user) not in seen_external_routes:
            fail(f"Missing approved vanilla workbench route: {recipe} -> {user}")

# Every WNG custom worker referenced by a RecipeDef must resolve to source.
class_names = set(classes)
for recipe, cls, path in custom_worker_refs:
    if cls not in class_names:
        fail(f"{recipe} references missing custom worker {cls} ({path})")

# The two production workers referenced from workbench recipes must remain audited.
for recipe, cls, path in custom_worker_refs:
    if cls.startswith("RecipeWorker_") and cls not in avail_classes:
        fail(f"Workbench custom worker {cls} for {recipe} is not covered by availability audit")

# ---------- Complete RecipeDef / recipeMaker enumeration ----------
recipe_def_count = 0
recipe_maker_count = 0
recipe_maker_inherited_routes = []
custom_recipe_worker_refs = []
external_inherit_false = []

for path, root in def_roots:
    for node in list(root):
        defname = node.findtext("defName") or ""

        if node.tag == "RecipeDef":
            recipe_def_count += 1

            wc = (node.findtext("workerClass") or "").strip()
            if wc.startswith("WraithNaniteGravtech."):
                custom_recipe_worker_refs.append((defname, wc, path))

            users_node = node.find("./recipeUsers")
            users = [x.text.strip() for x in node.findall("./recipeUsers/li") if x.text and x.text.strip()]
            if users_node is not None and (users_node.get("Inherit") or "").lower() == "false":
                external = [u for u in users if not u.startswith("WNG_") and u != "Human"]
                if external:
                    external_inherit_false.append((defname, path, external))

            physical_product = node.find("./products") is not None
            surgery_like = (
                (node.get("ParentName") or "").startswith("Surgery")
                or node.find("./addsHediff") is not None
                or node.find("./removesHediff") is not None
                or node.find("./appliedOnFixedBodyParts") is not None
            )
            if physical_product and not users and not surgery_like:
                fail(f"{defname} in {path} makes a physical product but has no recipeUsers")

        elif node.tag == "ThingDef":
            maker = node.find("./recipeMaker")
            if maker is None or (maker.get("IsNull") or "").lower() == "true":
                continue

            recipe_maker_count += 1
            users_node = maker.find("./recipeUsers")
            users = [x.text.strip() for x in maker.findall("./recipeUsers/li") if x.text and x.text.strip()]

            if users_node is not None and (users_node.get("Inherit") or "").lower() == "false":
                external = [u for u in users if not u.startswith("WNG_")]
                if external:
                    external_inherit_false.append((defname + " [recipeMaker]", path, external))

            if not users:
                parent = (node.get("ParentName") or "").strip()
                if parent:
                    recipe_maker_inherited_routes.append((defname, parent, path))
                else:
                    fail(f"{defname} in {path} has recipeMaker but no recipeUsers and no parent to inherit routing from")

# A WNG recipe must never erase an external bench's inherited user list.
for owner, path, external in external_inherit_false:
    fail(f"{owner} in {path} uses recipeUsers Inherit=False on external user(s): {external}")

# All custom recipe worker classes referenced by RecipeDefs must exist in source.
for recipe, fqcn, path in custom_recipe_worker_refs:
    cls = fqcn.split(".")[-1]
    if cls not in class_names:
        fail(f"{recipe} in {path} references missing custom recipe worker {fqcn}")

note(f"Enumerated {recipe_def_count} RecipeDef(s) and {recipe_maker_count} ThingDef recipeMaker block(s)")
if recipe_maker_inherited_routes:
    note(
        "recipeMaker blocks intentionally inheriting workstation routing: "
        + ", ".join(f"{name} <- {parent}" for name, parent, _ in recipe_maker_inherited_routes)
    )

# Lifecycle/type-safety classification for every availability override.
for path, cls, base, body in availability_methods:
    if "RecipeWorker" in base and "Recipe_Surgery" not in base:
        # Production workers may receive null, unspawned, destroyed, or non-building probes.
        # Null must be harmless; Map/Faction access must remain null-safe.
        if "thing.Map." in body or "thing.Faction." in body:
            fail(f"{cls} in {path} directly dereferences Map/Faction in production availability")
    else:
        # Surgery-style workers must reject non-pawn probes before touching pawn state/base logic.
        pawn_decl = body.find("Pawn pawn = thing as Pawn")
        if pawn_decl < 0:
            fail(f"{cls} in {path} does not type-check Thing as Pawn")

# ---------- Report ----------
print("=== WNG BILL / RECIPE REGRESSION AUDIT ===")
print("Checks:")
print("  1. Enumerate all WNG AvailableOnNow overrides and null/non-pawn guards")
print("  2. Preserve vanilla recipe ownership while allowing bounded additive WNG bench routes")
print("  3. Preserve third-party bench enumeration (no global bill/recipe hooks)")
print("  4. Custom worker failure paths cannot reach vanilla availability before target validation")
print("  5. Representative bench routing audit")

if notes:
    print("\nDETAILS:")
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

print("\nPASS: WNG bill/recipe regression audit found no unsafe recipe-menu interference.")
