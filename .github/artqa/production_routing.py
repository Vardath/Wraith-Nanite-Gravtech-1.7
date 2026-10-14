from pathlib import Path
import collections, sys
import xml.etree.ElementTree as ET

failures=[]
warnings=[]
notes=[]

roots=[]
for base in (Path("Defs"), Path("Compatibility")):
    if not base.exists(): continue
    for p in base.rglob("*.xml"):
        raw=p.read_text(encoding="utf-8",errors="ignore")
        if "<Patch" in raw or "PatchOperation" in raw:
            continue
        try: roots.append((p,ET.fromstring(raw)))
        except ET.ParseError as exc: failures.append(f"{p}: XML parse failure: {exc}")

by_type=collections.defaultdict(dict)
for path,root in roots:
    for node in list(root):
        name=(node.findtext("defName") or "").strip()
        if name:
            by_type[node.tag][name]=(path,node)

thingdefs=by_type["ThingDef"]
recipes=by_type["RecipeDef"]

allowed_external_users={
    "Human"
}

# Intentional additive vanilla workbench routes. These are the only external
# production users WNG is permitted to add; every other external route fails QA.
approved_external_routes= {
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

def external_route_allowed(recipe,user):
    return user in allowed_external_users or user in approved_external_routes.get(recipe,set())

external_usage=collections.defaultdict(list)
wng_usage=collections.defaultdict(list)

# RecipeDefs: validate every production route.
for name,(path,node) in recipes.items():
    if "WNG" not in name:
        continue
    users=[(x.text or "").strip() for x in node.findall("./recipeUsers/li") if (x.text or "").strip()]
    products=node.find("./products")
    surgery=node.find("addsHediff") is not None or node.find("removesHediff") is not None or node.get("ParentName","").startswith("Surgery")

    if products is not None and not users:
        failures.append(f"{name}: physical production RecipeDef has no recipeUsers ({path})")

    for user in users:
        if user.startswith("WNG_"):
            wng_usage[user].append((name,path))
            if user not in thingdefs:
                failures.append(f"{name}: missing WNG recipe user ThingDef {user} ({path})")
        else:
            external_usage[user].append((name,path))
            if not external_route_allowed(name,user):
                failures.append(f"{name}: unapproved external recipe user {user} ({path})")

    # Inherit=False is dangerous on external/vanilla routing because it can erase inherited users.
    ru=node.find("./recipeUsers")
    if ru is not None and (ru.get("Inherit") or "").lower()=="false":
        ext=[u for u in users if not u.startswith("WNG_")]
        if ext:
            failures.append(f"{name}: recipeUsers Inherit=False targets external user(s) {ext} ({path})")

    # Production recipes with ingredients should bind filters explicitly.
    if node.findall("./ingredients/li") and node.find("./fixedIngredientFilter") is None and not surgery:
        warnings.append(f"{name}: production recipe has ingredients without fixedIngredientFilter ({path})")

# ThingDef recipeMaker implied recipes.
for name,(path,node) in thingdefs.items():
    if not name.startswith("WNG_"):
        continue
    maker=node.find("./recipeMaker")
    if maker is None or (maker.get("IsNull") or "").lower()=="true":
        continue
    failures.append(f"{name}: live recipeMaker is forbidden; use an explicit WNG RecipeDef ({path})")
    users=[(x.text or "").strip() for x in maker.findall("./recipeUsers/li") if (x.text or "").strip()]
    if not users:
        failures.append(f"{name}: recipeMaker has no recipeUsers ({path})")
    for user in users:
        if user.startswith("WNG_"):
            wng_usage[user].append((name+" [recipeMaker]",path))
            if user not in thingdefs:
                failures.append(f"{name}: recipeMaker targets missing WNG ThingDef {user} ({path})")
        else:
            external_usage[user].append((name+" [recipeMaker]",path))
            if not external_route_allowed(name+" [recipeMaker]",user):
                failures.append(f"{name}: recipeMaker targets unapproved external user {user} ({path})")
    ru=maker.find("./recipeUsers")
    if ru is not None and (ru.get("Inherit") or "").lower()=="false":
        ext=[u for u in users if not u.startswith("WNG_")]
        if ext:
            failures.append(f"{name}: recipeMaker recipeUsers Inherit=False targets external user(s) {ext} ({path})")

# Any WNG workstation used by recipes must be a building-like ThingDef.
for user,refs in wng_usage.items():
    path,node=thingdefs[user]
    category=(node.findtext("category") or "").strip()
    parent=node.get("ParentName","")
    thingclass=(node.findtext("thingClass") or "").strip()
    buildingish = category=="Building" or "Building" in parent or "WorkTable" in parent or "Building" in thingclass
    if not buildingish:
        failures.append(f"{user}: used as recipe workstation but does not look building/worktable-like ({path})")

# Vanilla workbench integration must remain additive and exactly bounded by the allowlist.
seen_external_routes=set()
for user,refs in external_usage.items():
    for recipe,path in refs:
        if user=="Human":
            continue
        seen_external_routes.add((recipe,user))
        if not external_route_allowed(recipe,user):
            failures.append(f"{recipe}: unapproved external workstation route {user} ({path})")

for recipe,users in approved_external_routes.items():
    for user in users:
        if (recipe,user) not in seen_external_routes:
            failures.append(f"Missing approved vanilla workstation route: {recipe} -> {user}")

notes.append(f"Parsed {len(recipes)} RecipeDefs and {len(thingdefs)} ThingDefs")
notes.append(f"WNG workstations referenced: {len(wng_usage)}")
for user in sorted(external_usage):
    notes.append(f"{user}: {len(external_usage[user])} WNG recipe route(s)")

print("=== D134 PRODUCTION ROUTING AUDIT ===")
for n in notes: print(" -",n)
if warnings:
    print("\nWARNINGS:")
    for w in warnings: print(" -",w)
if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)
print("PASS: production routing is additive, explicitly bounded, and all WNG/approved vanilla workstations resolve.")
