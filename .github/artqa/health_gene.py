from pathlib import Path
import re
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

R=Path(".")
fail=[]

def_files=sorted((R/"Defs").rglob("*.xml"))
source_files=sorted((R/"Source/WraithNaniteGravtech").rglob("*.cs"))

# RimWorld does not require Def types to live in same-named folders. Some WNG pharmacology
# HediffDefs intentionally live beside their ThingDef/ChemicalDef, so index all shipped Def XML.
genes={}
hediffs={}
abilities=set()
for path in def_files:
    root=ET.parse(path).getroot()
    for n in list(root):
        name=(n.findtext("defName") or "").strip()
        if n.tag=="GeneDef" and name.startswith("WNG_"):
            genes[name]=(path,n)
        elif n.tag=="HediffDef" and name.startswith("WNG_"):
            hediffs[name]=(path,n)
        elif n.tag=="AbilityDef" and name:
            abilities.add(name)

# Index WNG classes in source.
source="\n".join(p.read_text(encoding="utf-8",errors="ignore") for p in source_files)
classes=set(re.findall(r"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)",source))
class_refs=[]
comp_refs=[]

for name,(path,n) in genes.items():
    gclass=(n.findtext("geneClass") or "").strip()
    if gclass.startswith("WraithNaniteGravtech."):
        short=gclass.split(".")[-1]
        class_refs.append((name,"geneClass",short,path))
        if short not in classes:
            fail.append(f"{name}: geneClass {gclass} not found in source ({path})")

    gizmo=(n.findtext("resourceGizmoType") or "").strip()
    if gizmo.startswith("WraithNaniteGravtech."):
        short=gizmo.split(".")[-1]
        class_refs.append((name,"resourceGizmoType",short,path))
        if short not in classes:
            fail.append(f"{name}: resourceGizmoType {gizmo} not found in source ({path})")

    for e in n.findall("./abilities/li"):
        a=(e.text or "").strip()
        if a.startswith("WNG_") and a not in abilities:
            fail.append(f"{name}: missing AbilityDef {a}")

    # Resource genes need the native resource metadata that their Gene_Resource implementation expects.
    if "Gene_Resource_" in gclass:
        for tag in ("resourceGizmoType","resourceLabel","resourceDescription","resourceLossPerDay"):
            if not (n.findtext(tag) or "").strip():
                fail.append(f"{name}: resource gene missing {tag}")

    for ext in n.findall("./modExtensions/li"):
        cls=(ext.get("Class") or "").strip()
        if cls.startswith("WraithNaniteGravtech."):
            short=cls.split(".")[-1]
            if short not in classes:
                fail.append(f"{name}: missing gene mod-extension class {cls}")

for name,(path,n) in hediffs.items():
    hclass=(n.findtext("hediffClass") or "").strip()
    if hclass.startswith("WraithNaniteGravtech."):
        short=hclass.split(".")[-1]
        class_refs.append((name,"hediffClass",short,path))
        if short not in classes:
            fail.append(f"{name}: hediffClass {hclass} not found in source ({path})")

    for li in n.findall("./comps/li"):
        cls=(li.get("Class") or "").strip()
        if cls.startswith("WraithNaniteGravtech."):
            short=cls.split(".")[-1]
            comp_refs.append((name,short,path))
            if short not in classes:
                fail.append(f"{name}: HediffCompProperties class {cls} not found in source ({path})")

# Custom HediffCompProperties classes must point at a real custom compClass.
for m in re.finditer(r"class\s+(HediffCompProperties_[A-Za-z0-9_]+)\s*:\s*HediffCompProperties\s*\{",source):
    props=m.group(1)
    start=m.start()
    segment=source[start:start+2400]
    cm=re.search(r"compClass\s*=\s*typeof\(([^)]+)\)",segment)
    if not cm:
        fail.append(f"{props}: no compClass assignment found")
        continue
    target=cm.group(1).split(".")[-1]
    if target not in classes:
        fail.append(f"{props}: compClass target {target} not found in source")

# Targeted add/remove lifecycle contracts for genes which own auxiliary Hediffs.
targeted={
    "Gene_Resource_LifeForce":[
        "public override void PostRemove()",
        "CleanupOwnedHediffs();",
        '"WNG_LifeForceStarved"',
        '"WNG_LifeForceTorpor"',
        '"WNG_WraithHibernating"',
    ],
    "Gene_EMPSensitiveNanites":[
        "public override void PostAdd()",
        "AddReceiverIfMissing();",
        "public override void PostRemove()",
        'RemoveOwnedHediff("WNG_NaniteEMPDisruption")',
    ],
    "Gene_AsuranCollectiveLink":[
        "public override void PostAdd()",
        "RefreshState();",
        "public override void PostRemove()",
        "RemoveState();",
    ],
    "Gene_WhispersPredator":[
        "public override void PostAdd()",
        "EnsureSensoryState();",
        "public override void PostRemove()",
        "RemoveSensoryState();",
        "WhispersFogUtility.IntervalTicks",
        "WhispersFogUtility.EmitPredatoryFog(pawn)",
    ],
}
for cls,tokens in targeted.items():
    pos=source.find("class "+cls)
    if pos<0:
        fail.append(f"missing targeted gene class {cls}")
        continue
    seg=source[pos:pos+9000]
    for token in tokens:
        if token not in seg:
            fail.append(f"{cls}: lifecycle contract missing {token}")

# The cultured implant is an always-on emitter sharing the native hybrid's
# fog utility, never a castable Ability. The obsolete Def remains for save migration.
mist_source=(R/"Source/WraithNaniteGravtech/Anomaly/WhispersHybrid.cs").read_text(encoding="utf-8")
implant_source=(R/"Source/WraithNaniteGravtech/Anomaly/WhispersImplants.cs").read_text(encoding="utf-8")
for token in (
    "IntervalTicks = 120",
    "Radius = 10f",
    "GenRadial.RadialCellsAround(origin, Radius, true)",
    "GasType.BlindSmoke",
    "cell == origin ? 30 : 16",
):
    if token not in mist_source:
        fail.append(f"WhispersFogUtility: missing continuous native mist contract {token}")
for token in (
    "class Hediff_WhispersMistGland : Hediff_Implant",
    "public override void TickInterval(int delta)",
    "pawn.IsHashIntervalTick(WhispersFogUtility.IntervalTicks, delta)",
    "WhispersFogUtility.EmitPredatoryFog(pawn)",
    "RemoveLegacyMistAbility();",
    "public override void PostRemoved()",
):
    if token not in implant_source:
        fail.append(f"Whispers mist gland: missing passive/legacy migration contract {token}")
if "GainAbility(" in implant_source:
    fail.append("Whispers mist gland must never grant a castable ability")

# Gene-dependent Tick paths must tolerate dead/missing pawns instead of dereferencing stale trackers.
for cls in ("Gene_Resource_LifeForce","Gene_WraithRegeneration","Gene_Resource_NaniteReserve","Gene_NaniteReconstruction","Gene_AsuranCollectiveLink","Gene_WhispersPredator"):
    pos=source.find("class "+cls)
    if pos<0: continue
    seg=source[pos:pos+12000]
    if "TickInterval" in seg and ("pawn == null" not in seg and "pawn?.health" not in seg):
        fail.append(f"{cls}: TickInterval has no obvious pawn/null lifecycle guard")

# WNG requires Biotech for genes. Anomaly-specific Whispers content is protected by the hard Anomaly dependency.
about=(R/"About/About.xml").read_text(encoding="utf-8",errors="ignore")
for package in ("ludeon.rimworld.biotech","ludeon.rimworld.anomaly"):
    if f"<packageId>{package}</packageId>" not in about:
        fail.append(f"About.xml missing required DLC dependency {package}")

# Source GeneDef/HediffDef literal references must resolve when WNG-owned.
for typ,defs in (("GeneDef",genes),("HediffDef",hediffs)):
    pat=re.compile(rf'DefDatabase<{typ}>\.GetNamed(?:SilentFail)?\(\s*"([^"]+)"')
    for p in source_files:
        text=p.read_text(encoding="utf-8",errors="ignore")
        for ref in pat.findall(text):
            if ref.startswith("WNG_") and ref not in defs:
                fail.append(f"{p}: literal {typ} reference {ref} does not resolve")

print("=== D155 HEALTH / GENE AUDIT ===")
print(" - WNG GeneDefs:",len(genes))
print(" - WNG HediffDefs:",len(hediffs))
print(" - WNG custom gene/hediff class refs checked:",len(class_refs))
print(" - WNG custom HediffCompProperties XML refs checked:",len(comp_refs))
print(" - Ability references from WNG genes checked")
print(" - Gene-owned Hediff add/remove cleanup contracts checked")
print(" - Resource-gene metadata and lifecycle guards checked")
print(" - Biotech/Anomaly hard-DLC dependency coverage checked")
print(" - Literal WNG GeneDef/HediffDef source references checked")
if fail:
    print("FAILURES:")
    for x in fail: print(" -",x)
    raise SystemExit(1)
print("PASS: WNG gene, hediff, resource and custom-comp health contracts are structurally coherent.")
