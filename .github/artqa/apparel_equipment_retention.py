from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
failures=[]
notes=[]

# ---------- Index ThingDefs and PawnKindDefs ----------
thingdefs={}
named_thingdefs={}
for path in (ROOT/"Defs"/"ThingDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except Exception as exc:
        failures.append(f"{path}: XML parse failure: {exc}")
        continue
    for node in list(root):
        if node.tag!="ThingDef": continue
        if node.get("Name"):
            named_thingdefs[node.get("Name")]=node
        name=(node.findtext("defName") or "").strip()
        if name:
            thingdefs[name]=(path,node)

pawnkinds={}
named_pawnkinds={}
for path in (ROOT/"Defs"/"PawnKindDefs").rglob("*.xml"):
    try: root=ET.parse(path).getroot()
    except Exception as exc:
        failures.append(f"{path}: XML parse failure: {exc}")
        continue
    for node in list(root):
        if node.tag!="PawnKindDef": continue
        if node.get("Name"):
            named_pawnkinds[node.get("Name")]=node
        name=(node.findtext("defName") or "").strip()
        if name.startswith("WNG_"):
            pawnkinds[name]=(path,node)

def pawn_chain(node):
    out=[node]; seen=set(); parent=node.get("ParentName")
    while parent and parent in named_pawnkinds and parent not in seen:
        seen.add(parent); node=named_pawnkinds[parent]; out.append(node); parent=node.get("ParentName")
    return out

def inherited_list(node,xpath):
    # nearest explicit list wins only when Inherit=False; otherwise merge ancestors.
    out=[]
    for cur in reversed(pawn_chain(node)):
        holder=cur.find(xpath)
        if holder is None: continue
        if (holder.get("Inherit") or "").lower()=="false":
            out=[]
        out.extend([(x.text or "").strip() for x in list(holder) if (x.text or "").strip()])
    return out

# ThingDef apparel inheritance for body groups/layers/graphic path.
def thing_chain(node):
    out=[node]; seen=set(); parent=node.get("ParentName")
    while parent and parent in named_thingdefs and parent not in seen:
        seen.add(parent); node=named_thingdefs[parent]; out.append(node); parent=node.get("ParentName")
    return out

def thing_inherited_list(node,xpath):
    out=[]
    for cur in reversed(thing_chain(node)):
        holder=cur.find(xpath)
        if holder is None: continue
        if (holder.get("Inherit") or "").lower()=="false":
            out=[]
        out.extend([(x.text or "").strip() for x in list(holder) if (x.text or "").strip()])
    return out

def thing_inherited_text(node,xpath):
    for cur in thing_chain(node):
        v=cur.findtext(xpath)
        if v and v.strip(): return v.strip()
    return None

# ---------- Canonical required gear ----------
expected_apparel={
    "WNG_WraithHunter":"WNG_WraithHunterCoat",
    "WNG_WraithWarrior":"WNG_WraithWarriorCarapace",
    "WNG_WraithCommander":"WNG_WraithCommanderCarapace",
    "WNG_WraithKeeper":"WNG_WraithKeeperMantle",
    "WNG_WraithQueen":"WNG_WraithQueenRaiment",
    "WNG_PlayerWraithHunter":"WNG_WraithHunterCoat",
    "WNG_PrecursorEngineer":"WNG_HumanFormUniform",
    "WNG_PrecursorSoldier":"WNG_PrecursorFieldArmor",
    "WNG_PrecursorCommander":"WNG_PrecursorCommandArmor",
    "WNG_HumanFormReplicator":"WNG_HumanFormCombatArmor",
    "WNG_PlayerHumanFormReplicator":"WNG_HumanFormUniform",
    "WNG_HumanFormCopy":"WNG_HumanFormUniform",
    "WNG_ReplicatorQueenChild":"WNG_HumanFormUniform",
}

required_apparel_defs=set()
weapon_tag_requirements=defaultdict(set)

for kind,apparel in expected_apparel.items():
    if kind not in pawnkinds:
        failures.append(f"missing canonical PawnKind {kind}")
        continue
    path,node=pawnkinds[kind]
    required=inherited_list(node,"apparelRequired")
    if apparel not in required:
        failures.append(f"{kind}: required apparel {apparel} not present")
    required_apparel_defs.add(apparel)

    tags=inherited_list(node,"weaponTags")
    for tag in tags:
        weapon_tag_requirements[kind].add(tag)

# Queen child is deliberately unarmed.
weapon_tag_requirements.pop("WNG_ReplicatorQueenChild",None)
weapon_tag_requirements.pop("WNG_HumanFormCopy",None)

# ---------- Apparel structural compatibility ----------
for apparel in sorted(required_apparel_defs):
    if apparel not in thingdefs:
        failures.append(f"required apparel def missing: {apparel}")
        continue
    path,node=thingdefs[apparel]
    groups=thing_inherited_list(node,"apparel/bodyPartGroups")
    layers=thing_inherited_list(node,"apparel/layers")
    worn=thing_inherited_text(node,"apparel/wornGraphicPath")
    if not groups:
        failures.append(f"{apparel}: no bodyPartGroups ({path})")
    if not layers:
        failures.append(f"{apparel}: no apparel layers ({path})")
    if not worn:
        failures.append(f"{apparel}: no wornGraphicPath ({path})")
    if thing_inherited_text(node,"thingClass") not in (None,"Apparel"):
        # Most ApparelBase children inherit Apparel implicitly; an explicit incompatible class is bad.
        cls=thing_inherited_text(node,"thingClass")
        if cls and "Apparel" not in cls:
            failures.append(f"{apparel}: incompatible thingClass {cls} ({path})")

# ---------- Weapon tags must have at least one matching concrete weapon ----------
weapons_by_tag=defaultdict(list)
for name,(path,node) in thingdefs.items():
    tags=thing_inherited_list(node,"weaponTags")
    if not tags: continue
    category=thing_inherited_text(node,"category")
    # weapon ThingDefs generally inherit category Item and define verbs/equippable.
    if node.find("verbs") is None and thing_inherited_text(node,"thingClass") not in ("ThingWithComps",):
        # still allow inherited weapon bases; tag itself is enough to inventory candidates.
        pass
    for tag in tags:
        weapons_by_tag[tag].append(name)

for kind,tags in sorted(weapon_tag_requirements.items()):
    if not tags:
        continue
    if not any(weapons_by_tag.get(tag) for tag in tags):
        failures.append(f"{kind}: none of its weaponTags resolve to a current WNG weapon: {sorted(tags)}")

# ---------- Retention implementation contract ----------
retention_path=ROOT/"Source/WraithNaniteGravtech/Equipment/WNGSignatureGearRetention.cs"
if not retention_path.exists():
    failures.append("WNGSignatureGearRetention.cs missing")
    retention=""
else:
    retention=retention_path.read_text(encoding="utf-8",errors="ignore")

required_tokens=(
    "SpawnRetentionTicks = 1800",
    "kindName.StartsWith(\"WNG_\"",
    "bool withinSpawnWindow",
    "ReconcileRequiredApparel(pawn, withinSpawnWindow)",
    "ReconcileSignatureWeapon(pawn, withinSpawnWindow)",
    "worn == null && withinSpawnWindow",
    "FindDroppedApparel",
    "pawn.apparel.Wear(worn, true)",
    "if (!withinSpawnWindow || pawn.equipment == null)",
    "FindDroppedSignatureWeapon",
    "weapon.DeSpawn()",
    "pawn.equipment.AddEquipment(weapon)",
    "GenPlace.TryPlaceThing(weapon",
    "pawn.outfits?.forcedHandler?.SetForced(apparel, true)",
    "Scribe_Collections.Look",
)
for token in required_tokens:
    if token not in retention:
        failures.append(f"signature gear retention contract missing: {token}")

# Retention may recover exact dropped gear, but must never manufacture replacements or
# permanently force weapons/apparel after the short spawn window.
for token in (
    "ThingMaker.MakeThing(",
    "PawnGenerator.GeneratePawn(",
    "DestroyAll(",
    "WornApparel.Clear(",
    "equipment.DestroyAllEquipment(",
    "SetForced(weapon",
):
    if token in retention:
        failures.append(f"signature retention contains destructive/manufacturing behavior: {token}")

# ---------- Living Wraith equipment maturation must not discard/destroy equipment ----------
maturation_path=ROOT/"Source/WraithNaniteGravtech/Wraith/WraithLivingEquipment.cs"
maturation=maturation_path.read_text(encoding="utf-8",errors="ignore") if maturation_path.exists() else ""
if not maturation:
    failures.append("Wraith living equipment maturation source missing")
else:
    for token in ("Destroy(", "DeSpawn(", "TryDrop", "Remove("):
        if token in maturation:
            failures.append(f"Wraith living-equipment maturation unexpectedly mutates ownership: {token}")

# ---------- Generation/reconstruction paths ----------
default_apparel=ROOT/"Source/WraithNaniteGravtech/Asurans/AsuranDefaultApparel.cs"
default_text=default_apparel.read_text(encoding="utf-8",errors="ignore") if default_apparel.exists() else ""
for kind,apparel in expected_apparel.items():
    if kind.startswith(("WNG_Precursor","WNG_HumanForm","WNG_PlayerHumanForm","WNG_ReplicatorQueen")):
        if kind not in default_text or apparel not in default_text:
            failures.append(f"Asuran fallback apparel map missing {kind} -> {apparel}")

# Reconstruction may deliberately strip generated random gear, but must immediately restore role apparel.
for rel in (
    "Source/WraithNaniteGravtech/Asurans/NeuralInterface.cs",
    "Source/WraithNaniteGravtech/Asurans/AsuranPatternArchive.cs",
):
    p=ROOT/rel
    text=p.read_text(encoding="utf-8",errors="ignore") if p.exists() else ""
    if "StripGeneratedGear(copy)" in text and "AsuranDefaultApparelUtility.EnsureRoleApparel(copy)" not in text:
        failures.append(f"{rel}: strips generated gear without restoring role apparel")

# WNG does not own external Goa'uld/Jaffa pawn gear. The optional bridge must not mutate it.
interop=ROOT/"Source/WraithNaniteGravtech/Compatibility/GoauldOptionalInterop.cs"
interop_text=interop.read_text(encoding="utf-8",errors="ignore") if interop.exists() else ""
for token in ("apparel.Remove","WornApparel.Clear","DestroyAllEquipment","TryDropEquipment"):
    if token in interop_text:
        failures.append(f"Goa'uld/Jaffa optional interop mutates external equipment: {token}")

print("=== D147 APPAREL / EQUIPMENT RETENTION AUDIT ===")
print(f" - Canonical pawn/apparel contracts: {len(expected_apparel)}")
print(f" - Required apparel defs checked: {len(required_apparel_defs)}")
print(f" - Pawn kinds with signature weapon tags: {len(weapon_tag_requirements)}")
print(f" - Weapon tags with current candidates: {sum(1 for k,v in weapons_by_tag.items() if v)}")
print(" - Goa'uld/Jaffa gear ownership: external and mutation-free")

if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)

print("PASS: WNG signature apparel/weapons are structurally wearable, recoverable during spawn retention, and not permanently forced or destroyed.")
