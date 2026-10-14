from pathlib import Path
import re

ROOT = Path(".")
failures = []

source_files = [
    p for p in (ROOT / "Source" / "WraithNaniteGravtech").rglob("*.cs")
    if "Diagnostics" not in p.parts
]
texts = {p: p.read_text(encoding="utf-8", errors="ignore") for p in source_files}

allowed_path = ROOT / "Source" / "WraithNaniteGravtech" / "Gravships" / "GravshipFamilySystems.cs"
allowed = texts.get(allowed_path, "")

required_contracts = (
    'using HarmonyLib;',
    'new Harmony("vardath.wraithnanitegravtech.gravship-native-bridge")',
    'harmony.PatchAll(typeof(WNGGravshipVanillaBridgeBootstrap).Assembly);',
    'WNGVGEVanillaLaunchIsolation.TryInstall(harmony);',
    'VanillaGravshipExpanded.GravshipUtility_PreLaunchConfirmation_Patch',
    'VanillaGravshipExpanded.Building_GravEngine_ConsumeFuel_Patch',
    'VanillaGravshipExpanded.Dialog_BeginRitual_DrawExtraRitualOutcomeDescriptions_Patch',
    'VanillaGravshipExpanded.Dialog_BeginRitual_ShowRitualBeginWindow_Patch',
    'VanillaGravshipExpanded.SettlementProximityGoodwillUtility_CheckConfirmSettle_Patch',
    'VanillaGravshipExpanded.RitualBehaviorWorker_GravshipLaunch_TryExecuteOn_Patch',
    'VanillaGravshipExpanded.RitualOutcomeEffectWorker_GravshipLaunch_Apply_Patch',
    'return engine is Building_WNGGravEngine;',
    '[HarmonyPatch(typeof(RitualObligationTargetWorker_GravshipLaunch),',
    'nameof(RitualObligationTargetWorker_GravshipLaunch.GetTargets))]',
    'internal static class WNGRitualObligationTargetWorkerGravshipLaunchPatch',
    '[HarmonyPatch(typeof(Gravship), "AddThing")]',
    'internal static class WNGGravshipAddThingPilotConsolePatch',
    'thing.TryGetComp<CompPilotConsole_WNGFamily>() != null',
    '___pilotConsole = building;',
    '[HarmonyPatch(typeof(SectionLayer_GravshipHull),',
    'nameof(SectionLayer_GravshipHull.BakeGravshipIndoorMesh))]',
    'internal static class WNGGravshipBakeIndoorHullMaskPatch',
    '__result.AddRange(SectionLayer_WNGFamilyGravshipHull.BakeGravshipIndoorMesh(map, bounds, center));',
)
for token in required_contracts:
    if token not in allowed:
        failures.append(f"reviewed gravship Harmony contract missing: {token}")

# Exactly three native production postfix targets are reviewed.
if allowed.count("[HarmonyPatch") != 3:
    failures.append(f"reviewed gravship bridge must contain exactly 3 HarmonyPatch attributes, found {allowed.count('[HarmonyPatch')}")
if allowed.count("Postfix(") != 3:
    failures.append(f"reviewed gravship bridge must contain exactly 3 postfixes, found {allowed.count('Postfix(')}")
if allowed.count("Prefix(") != 0:
    failures.append(f"reviewed gravship bridge must contain no prefixes, found {allowed.count('Prefix(')}")
for forbidden in ("HarmonyPrefix", "HarmonyTranspiler", "HarmonyFinalizer", "ReversePatcher", "PatchProcessor"):
    if forbidden in allowed:
        failures.append(f"reviewed gravship bridge contains forbidden Harmony mechanism: {forbidden}")

# No other production file may use Harmony or alternate runtime interception.
markers = (
    r"\busing\s+HarmonyLib\s*;",
    r"\[\s*HarmonyPatch\b",
    r"\bnew\s+Harmony\s*\(",
    r"\.PatchAll\s*\(",
    r"\.Patch\s*\(",
    r"\.Unpatch(?:All)?\s*\(",
    r"\bHarmonyMethod\b",
    r"\bHarmonyLib\.",
)
for path, text in texts.items():
    if path == allowed_path:
        continue
    for pattern in markers:
        if re.search(pattern, text):
            failures.append(f"{path}: unreviewed production Harmony usage detected ({pattern})")

dangerous_runtime_patch_patterns = {
    "RuntimeHelpers.PrepareMethod detour": r"RuntimeHelpers\.PrepareMethod\s*\(",
    "function pointer replacement": r"GetFunctionPointer\s*\(",
    "unsafe method detour": r"Marshal\.WriteIntPtr\s*\(",
    "MonoMod runtime detour": r"MonoMod\.RuntimeDetour|HookEndpointManager|new\s+Hook\s*\(",
}
for path, text in texts.items():
    for label, pattern in dangerous_runtime_patch_patterns.items():
        if re.search(pattern, text):
            failures.append(f"{path}: alternate runtime interception detected ({label})")

print("=== D136 HARMONY BLAST-RADIUS AUDIT ===")
print(f" - Production C# files scanned: {len(source_files)}")
print(" - Reviewed native patch targets: 3")
print("   * RitualObligationTargetWorker_GravshipLaunch.GetTargets (postfix only)")
print("   * Gravship.AddThing (postfix only)")
print("   * SectionLayer_GravshipHull.BakeGravshipIndoorMesh (postfix only; add WNG-family hull masks)")
print(" - Optional VGE isolation: guards only VGE launch-sequence patch methods for Building_WNGGravEngine")
print(" - Purpose: preserve Odyssey's native launch path and family hull interior masks")
if failures:
    print("\nFAILURES:")
    for failure in failures:
        print(" -", failure)
    raise SystemExit(1)

print("PASS: Harmony use is limited to reviewed native postfix bridges and bounded optional VGE launch isolation.")
