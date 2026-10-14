from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict, Counter

ROOT=Path(".")
failures=[]
notes=[]

# ---------- Index pawn kinds ----------
pawnkinds={}
for base in (ROOT/"Defs"/"PawnKindDefs", ROOT/"Defs"/"ThingDefs"):
    if not base.exists(): continue
    for path in base.rglob("*.xml"):
        try:
            root=ET.parse(path).getroot()
        except Exception as exc:
            failures.append(f"{path}: XML parse failure: {exc}")
            continue
        for node in list(root):
            if node.tag!="PawnKindDef":
                continue
            name=(node.findtext("defName") or "").strip()
            if name.startswith("WNG_"):
                pawnkinds[name]=(path,node)

# ---------- Index referenced defs ----------
thingdefs=set()
xenotypes=set()
genes=set()
abilities=set()
factions=set()
backstory_categories=set()
for folder,tag,target in (
    ("Defs/ThingDefs","ThingDef",thingdefs),
    ("Defs/XenotypeDefs","XenotypeDef",xenotypes),
    ("Defs/GeneDefs","GeneDef",genes),
    ("Defs/AbilityDefs","AbilityDef",abilities),
    ("Defs/FactionDefs","FactionDef",factions),
):
    base=ROOT/folder
    if not base.exists(): continue
    for path in base.rglob("*.xml"):
        try: root=ET.parse(path).getroot()
        except: continue
        for node in list(root):
            if node.tag!=tag: continue
            name=(node.findtext("defName") or "").strip()
            if name: target.add(name)

# backstory category names are free-form, but inventory WNG-owned category use for consistency.
for path in (ROOT/"Defs"/"BackstoryDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except: continue
    for node in list(root):
        for cat in node.findall("./spawnCategories/li"):
            if cat.text: backstory_categories.add(cat.text.strip())

# ---------- Structural pawn-kind validation ----------
humanlike=[]
wraith=[]
asuran=[]
mechanical=[]
animal=[]
for name,(path,node) in sorted(pawnkinds.items()):
    race=(node.findtext("race") or "").strip()
    parent=(node.get("ParentName") or "").strip()

    if race=="Human" or parent in ("WNG_WraithPawnBase","WNG_NaniteHumanoidBase"):
        humanlike.append(name)
    elif "Mechanoid" in parent or "Replicator" in name or name=="WNG_ChildsToy":
        mechanical.append(name)
    elif parent=="AnimalKindBase" or "Iratus" in name:
        animal.append(name)

    if name.startswith("WNG_Wraith") or name=="WNG_PlayerWraithHunter":
        wraith.append(name)
    if name in {
        "WNG_PrecursorEngineer","WNG_PrecursorSoldier","WNG_PrecursorCommander",
        "WNG_HumanFormReplicator","WNG_HumanFormInfiltrator","WNG_PlayerHumanFormReplicator",
        "WNG_HumanFormCopy","WNG_ReplicatorQueenChild"
    }:
        asuran.append(name)

    min_age=node.findtext("minGenerationAge")
    max_age=node.findtext("maxGenerationAge")
    if min_age and max_age:
        try:
            lo=float(min_age); hi=float(max_age)
            if lo<0 or hi<lo:
                failures.append(f"{name}: invalid generation age range {lo}..{hi} ({path})")
        except ValueError:
            failures.append(f"{name}: non-numeric generation age range ({path})")

    fixed_gender=(node.findtext("fixedGender") or "").strip()
    if fixed_gender and fixed_gender not in ("Male","Female","None"):
        failures.append(f"{name}: invalid fixedGender {fixed_gender!r} ({path})")

    xenotype_nodes=node.findall("./xenotypeSet/xenotypeChances/*")
    for x in xenotype_nodes:
        xname=x.tag.strip()
        if xname.startswith("WNG_") and xname not in xenotypes:
            failures.append(f"{name}: missing xenotype {xname} ({path})")

    for apparel in node.findall("./apparelRequired/li"):
        aname=(apparel.text or "").strip()
        if aname.startswith("WNG_") and aname not in thingdefs:
            failures.append(f"{name}: required apparel {aname} missing ({path})")

    for ability in node.findall("./abilities/li"):
        aname=(ability.text or "").strip()
        if aname.startswith("WNG_") and aname not in abilities:
            failures.append(f"{name}: ability {aname} missing ({path})")

    faction=(node.findtext("defaultFactionDef") or "").strip()
    if faction.startswith("WNG_") and faction not in factions:
        failures.append(f"{name}: default faction {faction} missing ({path})")

    for cat in node.findall(".//backstoryFiltersOverride//categories/li"):
        cname=(cat.text or "").strip()
        if cname.startswith(("Wraith","Asuran")) and cname not in backstory_categories:
            failures.append(f"{name}: backstory category {cname} has no matching WNG backstory pool ({path})")

# ---------- Canonical generation contracts ----------
expected={
    "WNG_WraithHunter":{"x":"WNG_Wraith","apparel":"WNG_WraithHunterCoat"},
    "WNG_WraithWarrior":{"x":"WNG_Wraith","apparel":"WNG_WraithWarriorCarapace"},
    "WNG_WraithCommander":{"x":"WNG_Wraith","apparel":"WNG_WraithCommanderCarapace"},
    "WNG_WraithKeeper":{"x":"WNG_Wraith","apparel":"WNG_WraithKeeperMantle"},
    "WNG_WraithQueen":{"x":"WNG_Wraith","apparel":"WNG_WraithQueenRaiment","gender":"Female"},
    "WNG_PlayerWraithHunter":{"x":"WNG_Wraith","apparel":"WNG_WraithHunterCoat"},
    "WNG_PrecursorEngineer":{"x":"WNG_NanitePrecursor","apparel":"WNG_HumanFormUniform"},
    "WNG_PrecursorSoldier":{"x":"WNG_NanitePrecursor","apparel":"WNG_PrecursorFieldArmor"},
    "WNG_PrecursorCommander":{"x":"WNG_NanitePrecursor","apparel":"WNG_PrecursorCommandArmor"},
    "WNG_HumanFormReplicator":{"x":"WNG_HumanFormReplicator","apparel":"WNG_HumanFormCombatArmor"},
    "WNG_PlayerHumanFormReplicator":{"x":"WNG_HumanFormReplicator","apparel":"WNG_HumanFormUniform"},
    "WNG_HumanFormCopy":{"x":"WNG_NanitePrecursor","apparel":"WNG_HumanFormUniform"},
    "WNG_ReplicatorQueenChild":{"x":"WNG_HumanFormReplicator","apparel":"WNG_HumanFormUniform","gender":"Female","age":"13"},
}

def first_xenotype(node):
    xs=node.findall("./xenotypeSet/xenotypeChances/*")
    return xs[0].tag if xs else None

def apparel_list(node):
    return [(x.text or "").strip() for x in node.findall("./apparelRequired/li") if (x.text or "").strip()]

# manually inherit from the known abstract bases for canonical children
bases={}
for name,(path,node) in pawnkinds.items():
    pass
for path in (ROOT/"Defs"/"PawnKindDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except: continue
    for node in list(root):
        if node.tag=="PawnKindDef" and node.get("Name"):
            bases[node.get("Name")]=node

def inherited_node_value(node,xpath):
    cur=node; seen=set()
    while cur is not None:
        val=cur.findtext(xpath)
        if val is not None and str(val).strip():
            return str(val).strip()
        parent=cur.get("ParentName")
        if not parent or parent in seen or parent not in bases: break
        seen.add(parent); cur=bases[parent]
    return None

def inherited_xenotype(node):
    cur=node; seen=set()
    while cur is not None:
        xs=cur.findall("./xenotypeSet/xenotypeChances/*")
        if xs: return xs[0].tag
        parent=cur.get("ParentName")
        if not parent or parent in seen or parent not in bases: break
        seen.add(parent); cur=bases[parent]
    return None

for name,contract in expected.items():
    if name not in pawnkinds:
        failures.append(f"canonical PawnKind missing: {name}")
        continue
    path,node=pawnkinds[name]
    x=inherited_xenotype(node)
    if x!=contract["x"]:
        failures.append(f"{name}: expected xenotype {contract['x']}, found {x}")
    apps=apparel_list(node)
    if contract.get("apparel") and contract["apparel"] not in apps:
        failures.append(f"{name}: missing required apparel {contract['apparel']}")
    if contract.get("gender") and inherited_node_value(node,"fixedGender")!=contract["gender"]:
        failures.append(f"{name}: expected fixed gender {contract['gender']}")
    if contract.get("age"):
        lo=inherited_node_value(node,"minGenerationAge")
        hi=inherited_node_value(node,"maxGenerationAge")
        try:
            expected_year=int(contract["age"])
            lo_f=float(lo); hi_f=float(hi)
            if int(lo_f)!=expected_year or int(hi_f)!=expected_year or lo_f<0 or hi_f<lo_f:
                failures.append(
                    f"{name}: expected generation entirely within biological age {expected_year}, found {lo}..{hi}"
                )
        except (TypeError,ValueError):
            failures.append(f"{name}: invalid generation age contract {lo}..{hi}")

# Human-form combat kinds need weapon route; infiltrator intentionally does not.
for name in ("WNG_PrecursorEngineer","WNG_PrecursorSoldier","WNG_PrecursorCommander","WNG_HumanFormReplicator","WNG_PlayerHumanFormReplicator"):
    if name not in pawnkinds: continue
    _,node=pawnkinds[name]
    tags=[(x.text or "").strip() for x in node.findall("./weaponTags/li") if (x.text or "").strip()]
    money=(node.findtext("weaponMoney") or "").strip()
    if not tags or not money:
        failures.append(f"{name}: combat-capable human-form pawn lacks weapon tags/money")

# Queen child must never generate armed.
if "WNG_ReplicatorQueenChild" in pawnkinds:
    _,node=pawnkinds["WNG_ReplicatorQueenChild"]
    if (node.findtext("weaponMoney") or "").strip() not in ("0","0.0"):
        failures.append("WNG_ReplicatorQueenChild must generate with weaponMoney=0")

# ---------- Generation source safety ----------
src_files=[p for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs") if "Diagnostics" not in p.parts]
generation_calls=[]
for path in src_files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    for m in re.finditer(r"PawnGenerator\.GeneratePawn\s*\(([^;\n]+)",text):
        generation_calls.append((path,m.group(1).strip()))

# Every explicit WNG PawnKind lookup used near generation must resolve.
lookup_re=re.compile(r'DefDatabase\s*<\s*PawnKindDef\s*>\s*\.\s*GetNamed(?:SilentFail)?\s*\(\s*"(WNG_[^"]+)"')
for path in src_files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    for name in lookup_re.findall(text):
        if name not in pawnkinds:
            failures.append(f"{path}: source references missing PawnKindDef {name}")

# Generation helpers must not create naked Asuran roles by stripping apparel after generation.
for path in src_files:
    text=path.read_text(encoding="utf-8",errors="ignore")
    if "PawnGenerator.GeneratePawn" not in text:
        continue
    if re.search(r"WornApparel\s*\.\s*Clear|apparel\s*\.\s*DestroyAll|Remove\s*\(.*WornApparel",text):
        failures.append(f"{path}: generation path contains destructive apparel clearing")

# Ensure explicit role-apparel restorer matches canonical Asuran roles.
apparel_src=(ROOT/"Source/WraithNaniteGravtech/Asurans/AsuranDefaultApparel.cs").read_text(encoding="utf-8",errors="ignore")
for name,contract in expected.items():
    if not name.startswith(("WNG_Precursor","WNG_HumanForm","WNG_PlayerHumanForm","WNG_ReplicatorQueen")):
        continue
    if contract.get("apparel") and name!="WNG_HumanFormInfiltrator":
        if name not in apparel_src or contract["apparel"] not in apparel_src:
            failures.append(f"Asuran default-apparel restorer does not cover {name} -> {contract['apparel']}")

print("=== D146 PAWN GENERATION AUDIT ===")
print(f" - WNG PawnKinds indexed: {len(pawnkinds)}")
print(f" - Humanlike WNG kinds: {len(humanlike)}")
print(f" - Wraith canonical kinds: {len(wraith)}")
print(f" - Asuran/human-form canonical kinds: {len(asuran)}")
print(f" - Replicator/mech-like kinds: {len(mechanical)}")
print(f" - Animal/Iratus kinds: {len(animal)}")
print(f" - Production PawnGenerator.GeneratePawn call sites: {len(generation_calls)}")

if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)

print("PASS: WNG PawnKind generation contracts, xenotypes, apparel, weapons, ages and source references are structurally coherent.")
