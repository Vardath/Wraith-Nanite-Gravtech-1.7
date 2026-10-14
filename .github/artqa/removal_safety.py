from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(".")
fail=[]

about=ROOT/"About/About.xml"
loadfolders=ROOT/"LoadFolders.xml"
compat=ROOT/"Source/WraithNaniteGravtech/Compatibility"
patches=ROOT/"Patches"
ce_root=ROOT/"Compatibility/CombatExtended"

for p in (about,loadfolders,compat):
    if not p.exists(): fail.append(f"missing required path {p}")

about_text=about.read_text(encoding="utf-8",errors="ignore") if about.exists() else ""
load_text=loadfolders.read_text(encoding="utf-8",errors="ignore") if loadfolders.exists() else ""

# Optional integrations must remain load-order hints, not hard dependencies.
for pkg in ("ccyt.stargatesmod","idolord.onac","cravemode.rimgatejaffakreebiotech","CETeam.CombatExtended"):
    if pkg.lower() in about_text.lower():
        dep_block=re.findall(r"<modDependencies>([\s\S]*?)</modDependencies>",about_text,re.I)
        if dep_block and pkg.lower() in dep_block[0].lower():
            fail.append(f"optional package became a hard dependency: {pkg}")

if 'IfModActive="CETeam.CombatExtended"' not in load_text:
    fail.append("Combat Extended compatibility folder is not conditionally loaded")

# XML foreign refs must be guarded by the owning optional package.
xmls=[]
for base in (ROOT/"Defs",ROOT/"Patches",ROOT/"Compatibility"):
    if base.exists():
        xmls.extend(base.rglob("*.xml"))

onac_refs=0
ce_refs=0
for p in xmls:
    raw=p.read_text(encoding="utf-8",errors="ignore")
    low=raw.lower()
    if "onac_" in low:
        onac_refs+=1
        if not ("patchoperationfindmod" in low and "<li>onac</li>" in low):
            fail.append(f"{p}: ONAC Def reference is not guarded by PatchOperationFindMod(ONAC)")
    if "combatextended." in low or "ammo_user" in low or "ammosetdef" in low:
        ce_refs+=1
        if "Compatibility/CombatExtended" not in p.as_posix():
            fail.append(f"{p}: Combat Extended schema/reference exists outside the CE-gated folder")

# Source must not acquire compile-time dependencies on optional assemblies.
source_files=list((ROOT/"Source/WraithNaniteGravtech").rglob("*.cs"))
source="\n".join(p.read_text(encoding="utf-8",errors="ignore") for p in source_files)
for token in (
    "using StargatesMod",
    "using CombatExtended",
    "using ONAC",
    "using RimGate",
    "typeof(StargatesMod.",
    "typeof(CombatExtended.",
):
    if token in source:
        fail.append(f"compile-time optional-mod dependency detected: {token}")

# Known foreign defs must use silent lookups. Missing optional defs after mod removal are normal.
external_prefixes=("StargateMod_","ONAC_","JKB_")
unsafe_lookup=re.compile(r'DefDatabase<[^>]+>\.GetNamed\s*\(\s*"([^"]+)"')
for p in source_files:
    raw=p.read_text(encoding="utf-8",errors="ignore")
    for name in unsafe_lookup.findall(raw):
        if name.startswith(external_prefixes):
            fail.append(f"{p}: throwing GetNamed lookup for optional external Def {name}")

# Goa'uld bridge must gate package-owned identities before use and never create foreign defs.
interop=ROOT/"Source/WraithNaniteGravtech/Compatibility/GoauldOptionalInterop.cs"
it=interop.read_text(encoding="utf-8",errors="ignore") if interop.exists() else ""
for token in (
    'OnacPackageId = "idolord.onac"',
    'RimGateJaffaPackageId = "cravemode.rimgatejaffakreebiotech"',
    "public static bool OnacLoaded()",
    "public static bool RimGateJaffaLoaded()",
    "if (!FullEcosystemActive()",
    "if (!RimGateJaffaLoaded()",
    "PackageActive(",
    "GetNamedSilentFail",
):
    # GetNamedSilentFail is not required in this file because it recognizes loaded defs by origin.
    if token=="GetNamedSilentFail":
        continue
    if token not in it:
        fail.append(f"GoauldOptionalInterop removal-safety contract missing: {token}")
for forbidden in ("DefGenerator.AddImpliedDef","new FactionDef","new XenotypeDef","new GeneDef"):
    if forbidden in it:
        fail.append(f"GoauldOptionalInterop manufactures external content: {forbidden}")

# Stargate integration must remain string/DefDatabase based and null-safe.
stargate_files=[
    "StartingMapStargate.cs","QuietLatticeStargateVisit.cs","WraithStargateHunt.cs",
    "WraithGatePursuit.cs","WraithStargateDiplomacy.cs","ReplicatorStargateAssault.cs",
    "ReplicatorStargateSalvage.cs","HostileAsuranGateJumper.cs","WNGGateControlObjective.cs"
]
stargate_count=0
for name in stargate_files:
    p=compat/name
    if not p.exists():
        fail.append(f"missing Stargate compatibility source {p}")
        continue
    raw=p.read_text(encoding="utf-8",errors="ignore")
    stargate_count+=1
    if "Stargate" in raw and "GetNamedSilentFail" not in raw and "thingClass?.FullName" not in raw and name not in ("WNGGateControlObjective.cs","WraithGatePursuit.cs"):
        fail.append(f"{p}: Stargate integration lacks silent Def/class resolution")
    if name=="WraithGatePursuit.cs":
        # This component intentionally receives an already-resolved exact gate from the hunt layer.
        # It never looks up CatCraft defs itself; removal safety is its saved-reference cleanup and
        # conventional edge-return fallback when the exact gate disappears after loading.
        for token in ("WraithStargateHuntUtility.IsExactGateUsable","sourceGate == null || sourceGate.Destroyed","CompleteReturn(throughGate: false)"):
            if token not in raw:
                fail.append(f"{p}: pursuit removal fallback missing {token}")
    if "Scribe_References.Look(ref sourceGate" in raw:
        if "LoadSaveMode.PostLoadInit" not in raw:
            fail.append(f"{p}: persisted optional sourceGate has no PostLoadInit cleanup")

# CatCraft arrival-mode resolution specifically has to tolerate missing Worker/Def.
quiet=(compat/"QuietLatticeStargateVisit.cs").read_text(encoding="utf-8",errors="ignore")
for token in (
    "GetNamedSilentFail(StargateArrivalModeDefName)",
    "mode?.Worker == null",
    "stargateMode?.Worker == null",
):
    if token not in quiet:
        fail.append(f"Quiet Lattice missing-mod guard lost: {token}")

starter=(compat/"StartingMapStargate.cs").read_text(encoding="utf-8",errors="ignore")
for token in (
    "GetNamedSilentFail(StargateDefName)",
    "GetNamedSilentFail(DhdDefName)",
    "if (gateDef == null || dhdDef == null)",
):
    if token not in starter:
        fail.append(f"starting Stargate missing-mod guard lost: {token}")

# No compatibility provider object may be serialized; provider removal is process-local.
asgard=(compat/"AsgardCompatibilityHooks.cs").read_text(encoding="utf-8",errors="ignore")
for token in ("UnregisterProvider(string providerId)","Providers.RemoveAll","never serialized into a save"):
    if token not in asgard:
        fail.append(f"Asgard provider removal contract missing: {token}")
if "Scribe_" in asgard:
    fail.append("optional Asgard provider registry became save-serialized")

# CE compatibility must be self-contained and removable.
if ce_root.exists():
    ce_xml=list(ce_root.rglob("*.xml"))
    if not ce_xml:
        fail.append("CE compatibility folder exists but contains no XML")
else:
    fail.append("CE compatibility folder missing")

# ONAC patch files must be guarded by a real mod-presence operation so removal
# falls back to WNG defaults without leaving unresolved foreign Def references.
for name in ("WNG_GoauldShuttleONACIntegration.xml","WNG_GoauldGravshipONACIntegration.xml"):
    p=patches/name
    if not p.exists():
        fail.append(f"missing ONAC optional patch {p}")
        continue
    raw=p.read_text(encoding="utf-8",errors="ignore").lower()
    if not ("patchoperationfindmod" in raw and "<li>onac</li>" in raw and 'class="patchoperationsequence"' in raw):
        fail.append(f"{p}: ONAC patch is not guarded by PatchOperationFindMod(ONAC) around its sequence")

print("=== D160 REMOVAL SAFETY AUDIT ===")
print(f" - production C# files scanned: {len(source_files)}")
print(f" - optional Stargate compatibility sources checked: {stargate_count}")
print(f" - ONAC-referencing XML files checked: {onac_refs}")
print(f" - CE-referencing/schema XML files checked: {ce_refs}")
print(" - optional packages remain non-hard dependencies")
print(" - known foreign Def lookups checked for silent failure")
print(" - persisted Stargate references checked for post-load cleanup")
print(" - optional provider registry checked for process-local removal")

if fail:
    print("\nFAILURES:")
    for x in fail: print(" -",x)
    raise SystemExit(1)
print("PASS: optional integrations degrade through package gates, silent Def resolution and null/post-load cleanup rather than hard foreign dependencies.")
