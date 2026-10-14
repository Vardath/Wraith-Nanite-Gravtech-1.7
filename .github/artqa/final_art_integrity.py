from pathlib import Path
from PIL import Image
import xml.etree.ElementTree as ET
import sys

ROOT=Path(".")
errors=[]
pngs=sorted([p for p in ROOT.rglob("*.png") if ".git" not in p.parts and p.as_posix() != ".github/artgen/assets/replicator_master.png"])

# 1) Every PNG must be a real, fully decodable PNG and must not contain a transfer-truncation marker.
for p in pngs:
    data=p.read_bytes()
    if b"ELLIPSIZATIO" in data or b"[... ELLIPSIZATION ...]" in data:
        errors.append(f"TRUNCATION_MARKER {p}")
        continue
    try:
        im=Image.open(p)
        if im.format != "PNG":
            errors.append(f"NOT_PNG {p} format={im.format}")
        im.load()
    except Exception as e:
        errors.append(f"PNG_DECODE {p}: {e}")

# 2) All XML must parse.
xmls=sorted([p for p in ROOT.rglob("*.xml") if ".git" not in p.parts])
for p in xmls:
    try:
        ET.parse(p)
    except Exception as e:
        errors.append(f"XML_PARSE {p}: {e}")

# 3) Required gravship door directional sets.
door_roots=[
    Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldGravshipDoor"),
    Path("Textures/Things/Building/Wraith/Gravship/WNG_WraithGravshipDoor"),
    Path("Textures/Things/Building/Precursor/Gravship/WNG_AsuranGravshipDoor"),
]
for root in door_roots:
    for suf in ["", "_north", "_east", "_south", "_west"]:
        p=Path(str(root)+suf+".png")
        if not p.exists():
            errors.append(f"MISSING_DOOR_DIRECTION {p}")

# 4) Known final edge-clean assets must have no visible alpha on canvas edge.
edge_clean=[
    Path("Textures/Things/Building/Goauld/Gravship/WNG_GoauldGravFieldProjector.png"),
    Path("Textures/Things/Building/Precursor/Gravship/WNG_AsuranGravFieldExtender.png"),
    Path("Textures/Things/Projectile/WNG/WNG_ProjectileHeavyBio.png"),
    Path("Textures/Things/Projectile/WNG/WNG_ProjectileLivingCarbine.png"),
    Path("Textures/Things/Projectile/WNG/WNG_ProjectilePrecursorPulse.png"),
    Path("Textures/Things/Projectile/WNG/WNG_ProjectileStunStaff.png"),
    Path("Textures/Things/Projectile/WNG/WNG_ProjectileStunner.png"),
    Path("Textures/Things/Building/Precursor/Shuttle/WNG_AsuranQueenRecoveryCarrier.png"),
    Path("Textures/Things/Building/Precursor/Shuttle/WNG_AsuranQueenRecoveryCarrier_north.png"),
    Path("Textures/Things/Building/Precursor/Shuttle/WNG_AsuranQueenRecoveryCarrier_east.png"),
    Path("Textures/Things/Building/Precursor/Shuttle/WNG_AsuranQueenRecoveryCarrier_south.png"),
    Path("Textures/Things/Building/Precursor/Shuttle/WNG_AsuranQueenRecoveryCarrier_west.png"),
]
for p in edge_clean:
    if not p.exists():
        errors.append(f"MISSING_EDGE_CLEAN_TARGET {p}")
        continue
    im=Image.open(p).convert("RGBA"); im.load()
    a=im.getchannel("A"); w,h=im.size
    edge=0
    for x in range(w):
        edge += int(a.getpixel((x,0))>0)+int(a.getpixel((x,h-1))>0)
    for y in range(1,h-1):
        edge += int(a.getpixel((0,y))>0)+int(a.getpixel((w-1,y))>0)
    if edge:
        errors.append(f"EDGE_ALPHA {p}: {edge}")

# 5) Legacy CE icon files must stay absent because current CE defs use resource art.
dead_ce=[
    Path("Compatibility/CombatExtended/Textures/Things/Item/Ammo/Replicator/WNG_ReplicatorChargeCell.png"),
    Path("Compatibility/CombatExtended/Textures/Things/Item/Ammo/Wraith/WNG_WraithBiochargePack.png"),
]
for p in dead_ce:
    if p.exists():
        errors.append(f"DEAD_CE_ASSET_RETURNED {p}")

# 6) Key reviewed-family counts. These make accidental deletion obvious.
ui=[p for p in Path("Textures/UI/WNG").rglob("*.png")]
apparel=[p for p in Path("Textures/Things/Pawn/Humanlike/Apparel").rglob("*.png")]
rep=[p for p in pngs if "Replicator" in str(p)]
print("WNG_FINAL_PNG_COUNT",len(pngs))
print("WNG_FINAL_XML_COUNT",len(xmls))
print("WNG_FINAL_UI_COUNT",len(ui))
print("WNG_FINAL_APPAREL_COUNT",len(apparel))
print("WNG_FINAL_REPLICATOR_RELATED_COUNT",len(rep))

if len(ui) < 41: errors.append(f"UI_COUNT regressed below audited floor=41 got={len(ui)}")
if len(apparel) < 217: errors.append(f"APPAREL_COUNT regressed below audited floor=217 got={len(apparel)}")

print("WNG_FINAL_ART_ERRORS",len(errors))
for e in errors:
    print("WNG_FINAL_ART_ERROR",e)
if errors:
    sys.exit(1)

print("WNG_FINAL_ART_INTEGRITY GREEN")
