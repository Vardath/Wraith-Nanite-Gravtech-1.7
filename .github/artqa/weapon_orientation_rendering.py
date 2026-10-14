from pathlib import Path
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT=Path(".")
failures=[]
notes=[]

weapon_defs={}
for path in (ROOT/"Defs"/"ThingDefs").rglob("*.xml"):
    try:
        root=ET.parse(path).getroot()
    except Exception as exc:
        failures.append(f"{path}: XML parse failure: {exc}")
        continue
    for node in list(root):
        if node.tag!="ThingDef":
            continue
        name=(node.findtext("defName") or "").strip()
        if not name.startswith("WNG_"):
            continue
        cats=[(x.text or "").strip() for x in node.findall("./thingCategories/li") if (x.text or "").strip()]
        if "WeaponsRanged" in cats or "WeaponsMelee" in cats:
            weapon_defs[name]=(path,node,"ranged" if "WeaponsRanged" in cats else "melee")

if not weapon_defs:
    failures.append("No WNG weapon ThingDefs were found")

# Def-level render contract.
for name,(path,node,kind) in sorted(weapon_defs.items()):
    graphic=node.find("graphicData")
    if graphic is None:
        failures.append(f"{name}: no graphicData ({path})")
        continue

    tex=(graphic.findtext("texPath") or "").strip()
    gclass=(graphic.findtext("graphicClass") or "").strip()
    if not tex:
        failures.append(f"{name}: no graphic texPath ({path})")
    if not gclass:
        failures.append(f"{name}: no graphicClass ({path})")

    # All current hand-held WNG weapons intentionally use Graphic_Single. This lets
    # RimWorld's native equipment renderer rotate/flip the same authored weapon sprite.
    if gclass!="Graphic_Single":
        failures.append(f"{name}: hand-held weapon graphicClass changed to {gclass!r}; expected Graphic_Single")

    # Verify the authored texture actually exists in the checked-out release tree.
    if tex:
        candidates=[
            ROOT/"Textures"/(tex+".png"),
            ROOT/"Textures"/(tex+"_north.png"),
            ROOT/"Textures"/(tex+"_east.png"),
            ROOT/"Textures"/(tex+"_south.png"),
            ROOT/"Textures"/(tex+"_west.png"),
        ]
        if not any(p.exists() for p in candidates):
            failures.append(f"{name}: no texture file found for {tex}")

    draw=(graphic.findtext("drawSize") or "").strip()
    if draw:
        raw=draw.strip("()")
        try:
            parts=[float(x.strip()) for x in raw.split(",")]
            vals=parts if len(parts)>1 else [parts[0],parts[0]]
            if min(vals)<=0 or max(vals)>3.5:
                failures.append(f"{name}: implausible weapon drawSize {draw}")
        except Exception:
            failures.append(f"{name}: invalid drawSize {draw!r}")

    angle=(node.findtext("equippedAngleOffset") or "").strip()
    if angle:
        try:
            a=float(angle)
            if a < -360 or a > 360:
                failures.append(f"{name}: equippedAngleOffset outside sane range: {a}")
        except ValueError:
            failures.append(f"{name}: invalid equippedAngleOffset {angle!r}")

    # Ranged weapons need a usable shooting verb/projectile.
    if kind=="ranged":
        verbs=node.findall("./verbs/li")
        if not verbs:
            failures.append(f"{name}: ranged weapon has no verb")
        elif not any((v.findtext("defaultProjectile") or "").strip() for v in verbs):
            failures.append(f"{name}: ranged weapon has no defaultProjectile")

    # Melee weapons need at least one tool.
    if kind=="melee" and not node.findall("./tools/li"):
        failures.append(f"{name}: melee weapon has no tools")

# Historical backwards-rifle regression: these authored sprites require the 180° hold correction.
for name in ("WNG_PrecursorPulseRifle","WNG_RecoveredPrecursorPulseRifle"):
    item=weapon_defs.get(name)
    if item is None:
        failures.append(f"historical orientation weapon missing: {name}")
        continue
    node=item[1]
    if (node.findtext("equippedAngleOffset") or "").strip()!="180":
        failures.append(f"{name}: backwards-weapon regression; equippedAngleOffset must remain 180")

# Production code must not globally override RimWorld equipment rendering.
production=[p for p in (ROOT/"Source"/"WraithNaniteGravtech").rglob("*.cs") if "Diagnostics" not in p.parts]
source="\n".join(p.read_text(encoding="utf-8",errors="ignore") for p in production)
for token in (
    "DrawEquipmentAiming",
    "PawnRenderer",
    "PawnRenderUtility",
    "equipmentDrawDistanceFactor",
):
    if token in source:
        failures.append(f"production source contains custom equipment-render path token {token}; review required")

# Combat Extended patches must not erase the historical precursor hold correction.
ce=ROOT/"Compatibility"/"CombatExtended"/"Patches"/"Weapons_Precursor.xml"
if ce.exists():
    text=ce.read_text(encoding="utf-8",errors="ignore")
    # CE conversion is allowed; static contract only guards against an explicit offset reset.
    if "<equippedAngleOffset>0</equippedAngleOffset>" in text:
        failures.append("Combat Extended compatibility explicitly resets precursor equippedAngleOffset to 0")

counts=defaultdict(int)
for _,(_,_,kind) in weapon_defs.items():
    counts[kind]+=1

print("=== D148 WEAPON ORIENTATION / RENDERING AUDIT ===")
print(f" - WNG handheld weapon defs audited: {len(weapon_defs)}")
print(f" - Ranged weapons: {counts['ranged']}")
print(f" - Melee weapons: {counts['melee']}")
print(" - Historical precursor rifle 180-degree hold correction: guarded")
print(" - Production custom equipment-render overrides: none expected")

if failures:
    print("\nFAILURES:")
    for f in failures:
        print(" -",f)
    raise SystemExit(1)

print("PASS: weapon render defs, textures, categories and known hold-angle regressions are structurally valid.")
