from pathlib import Path
import re

ROOT=Path(".")
SRC=ROOT/"Source"/"WraithNaniteGravtech"
failures=[]
notes=[]

files=[p for p in SRC.rglob("*.cs") if "Diagnostics" not in p.parts]
source={p:p.read_text(encoding="utf-8",errors="ignore") for p in files}
joined="\n".join(source.values())

# Dev/God state must never be forced by WNG.
for label,pat in {
    "forces God Mode on": r"DebugSettings\.godMode\s*=\s*true",
    "forces God Mode off": r"DebugSettings\.godMode\s*=\s*false",
    "forces Dev Mode on": r"Prefs\.DevMode\s*=\s*true",
    "forces Dev Mode off": r"Prefs\.DevMode\s*=\s*false",
}.items():
    if re.search(pat,joined):
        failures.append(label)

# Normal construction state must never be rewritten for developer convenience.
for label,pat in {
    "costList assignment/clear": r"\.costList\s*=(?!=)|\.costList\s*\.\s*Clear\s*\(",
    "WorkToBuild assignment": r"WorkToBuild[^\n]{0,100}=\s*",
    "research prerequisite assignment/clear": r"researchPrerequisites\s*=(?!=)|researchPrerequisites\s*\.\s*Clear\s*\(",
    "designation category assignment": r"\.designationCategory\s*=\s*",
    "blueprint def assignment": r"\.blueprintDef\s*=\s*",
    "frame def assignment": r"\.frameDef\s*=\s*",
}.items():
    hits=[str(p) for p,t in source.items() if re.search(pat,t)]
    if hits:
        failures.append(f"runtime normal-construction mutation ({label}): {', '.join(hits)}")

routing_path=SRC/"UI"/"WNGArchitectRouting.cs"
routing=source.get(routing_path,"")
required_routing=[
    "public sealed class Designator_Build_WNGGodMode : Designator_Build",
    "public override bool Visible => DebugSettings.godMode || base.Visible;",
    "if (!DebugSettings.godMode)",
    "return base.CanDesignateCell(c);",
    "base.DesignateSingleCell(c);",
    "new Designator_Build_WNGGodMode(def)",
]
for token in required_routing:
    if token not in routing:
        failures.append("God Mode routing contract missing: "+token)

# God Mode checks must be isolated to Architect routing in production code.
god_files=[str(p) for p,t in source.items() if "DebugSettings.godMode" in t]
if god_files != [str(routing_path)]:
    failures.append("production DebugSettings.godMode use escaped Architect routing: "+", ".join(god_files))

# Direct developer spawning is intentionally a separate contract.
access_path=SRC/"UI"/"WNGGameplayAccessibility.cs"
access=source.get(access_path,"")
required_access=[
    'def.defName.StartsWith("WNG_", StringComparison.Ordinal)',
    'def.category != ThingCategory.Item && def.category != ThingCategory.Building',
    'def.forceDebugSpawnable = true;',
]
for token in required_access:
    if token not in access:
        failures.append("direct-debug-spawn contract missing: "+token)

# Accessibility class may only expose physical WNG defs; it may not alter economy/research/building semantics.
for forbidden in (
    "costList","WorkToBuild","researchPrerequisites","researchPrerequisite",
    "designationCategory","blueprintDef","frameDef","DebugSettings.godMode","Prefs.DevMode"
):
    if forbidden in access:
        failures.append("WNGGameplayAccessibility touches forbidden normal-play field/state: "+forbidden)

# forceDebugSpawnable mutation is allowed only in the accessibility class.
debug_spawn_writers=[]
for p,t in source.items():
    if re.search(r"forceDebugSpawnable\s*=\s*",t):
        debug_spawn_writers.append(str(p))
if debug_spawn_writers != [str(access_path)]:
    failures.append("forceDebugSpawnable writers are not isolated to WNGGameplayAccessibility: "+", ".join(debug_spawn_writers))

# No custom developer designator that bypasses God Mode should exist.
for forbidden in ("WNGDev","DevModeDesignator","WithScopedGodMode","PlaceDirectly","SpawnInsteadOfBlueprint"):
    hits=[str(p) for p,t in source.items() if forbidden in t]
    if hits:
        failures.append(f"unapproved developer-construction bypass token {forbidden}: {', '.join(hits)}")

# In-game audit diagnostic retired after audit completion; static CI checks remain.

if failures:
    print("\nFAILURES:")
    for f in failures: print(" -",f)
    raise SystemExit(1)

print("PASS: WNG keeps normal construction, God Mode and direct developer spawning as three separate paths.")
