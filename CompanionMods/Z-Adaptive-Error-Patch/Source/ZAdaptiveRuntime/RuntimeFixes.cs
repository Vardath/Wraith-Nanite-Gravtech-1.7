using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Verse;
using Verse.Grammar;

namespace ZAdaptiveRuntime
{
    [StaticConstructorOnStartup]
    public static class ZAdaptiveRuntimeBootstrap
    {
        private static Type vehiclePathingSystemType;
        private static bool vehiclePathingDeferredLogged;
        private static bool vehiclePathingGuardFailureLogged;
        private static bool autoNameBabiesSuppressedLogged;
        private static bool scrollMentalStateTargetGuardLogged;
        private static bool invisibilityCorpseGuardLogged;
        private static bool graphicRequestNullPathLogged;
        private static bool knownGraphicAliasLogged;
        private static bool firePanicEnumerationSuppressedLogged;
        private static bool invalidEquipmentDropSuppressedLogged;
        private static bool psychicShockTargetGuardLogged;
        private static bool ideologyDeityTypeBridgeLogged;
        private static bool ideologyDeityNameBridgeLogged;
        private static bool ideologyNullRulePackLogged;
        private static bool invalidVgeThingRequestLogged;
        private static bool giddyUpUninitializedDeathLogged;
        private static bool invalidRoleApparelTipLogged;
        private static bool obeliskLetterGuardLogged;
        private static bool obeliskLetterPostfixDisabled;
        private static bool invalidTemperatureCellGuardLogged;
        private static MethodInfo giddyUpStorageGetter;
        private static readonly FieldInfo GiddyUpHealthPawnField = AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");
        private static readonly List<Thing> EmptyVgeThingList = new List<Thing>(0);

        static ZAdaptiveRuntimeBootstrap()
        {
            var harmony = new Harmony("vardath.adaptiveerrorpatch.runtime");
            Log.Message("[Z Adaptive] Runtime bootstrap active; independent compatibility hooks initializing.");
            TryInstallRuntimeHook("PatchStaticAtlas", () => PatchStaticAtlas(harmony));
            TryInstallRuntimeHook("PatchGraphicSingleAtlasInsertion", () => PatchGraphicSingleAtlasInsertion(harmony));
            TryInstallRuntimeHook("PatchGraphicRequestNullPath", () => PatchGraphicRequestNullPath(harmony));
            TryInstallRuntimeHook("PatchGraphicInitRequests", () => PatchGraphicInitRequests(harmony));
            TryInstallRuntimeHook("PatchInvalidEquipmentDrops", () => PatchInvalidEquipmentDrops(harmony));
            TryInstallRuntimeHook("PatchFireDefinitiveEdition", () => PatchFireDefinitiveEdition(harmony));
            TryInstallRuntimeHook("PatchPsychicShockTarget", () => PatchPsychicShockTarget(harmony));
            TryInstallRuntimeHook("PatchIdeologyGrammar", () => PatchIdeologyGrammar(harmony));
            TryInstallRuntimeHook("PatchVanillaGravshipExpanded", () => PatchVanillaGravshipExpanded(harmony));
            TryInstallRuntimeHook("PatchVehicleFrameworkGravTide", () => PatchVehicleFrameworkGravTide(harmony));
            TryInstallRuntimeHook("PatchGeologicalLandformsGravTide", () => PatchGeologicalLandformsGravTide(harmony));
            TryInstallRuntimeHook("PatchScrollMentalStateTarget", () => PatchScrollMentalStateTarget(harmony));
            TryInstallRuntimeHook("PatchInvisibilityPsychology", () => PatchInvisibilityPsychology(harmony));
            TryInstallRuntimeHook("PatchAutoNameBabies", () => PatchAutoNameBabies(harmony));
            TryInstallRuntimeHook("PatchVgeGravEngineListerGuard", () => PatchVgeGravEngineListerGuard(harmony));
            TryInstallRuntimeHook("PatchGiddyUpDeathInitGuard", () => PatchGiddyUpDeathInitGuard(harmony));
            TryInstallRuntimeHook("PatchMissingIdeoApparelTipGuard", () => PatchMissingIdeoApparelTipGuard(harmony));
            TryInstallRuntimeHook("WorldgenDiagnostics", () => PatchWorldGenerationDiagnostics(harmony));
            TryInstallRuntimeHook("MAPSameIdeoInit", () => PatchMechanoidMechanitorIdeoInitialization(harmony));
            TryInstallRuntimeHook("ObeliskLetterUI", () => PatchObeliskLetterChoices(harmony));
            TryInstallRuntimeHook("GridCellTemperatureBounds", () => PatchGridCellTemperatureBounds(harmony));
        }

        private static void TryInstallRuntimeHook(string name, Action install)
        {
            try { install(); }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Patch install failed (" + name +
                    "); remaining independent patches will still initialize: " + ex);
            }
        }


        // Player(20261010-011531).log, 10:24:42: Obelisk Control's
        // ExistingObeliskLetterLinks.Postfix throws NRE while vanilla draws
        // ChoiceLetter UI. Wrap only this optional mod postfix, never the
        // global OnGUI pipeline, so unrelated UI exceptions remain visible.
        private static void PatchObeliskLetterChoices(Harmony harmony)
        {
            Type patch = AccessTools.TypeByName("ObeliskControl.ExistingObeliskLetterLinks");
            if (patch == null)
                return;

            MethodInfo target = AccessTools.Method(patch, "Postfix");
            MethodInfo prefix = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(ObeliskLetterLinksShouldRun));
            MethodInfo finalizer = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(ObeliskLetterLinksFinalizer));
            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning("[Z Adaptive] Obelisk Control letter-link patch signature changed; leaving upstream behaviour unchanged.");
                return;
            }

            harmony.Patch(target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                finalizer: new HarmonyMethod(finalizer));
        }

        private static bool ObeliskLetterLinksShouldRun()
        {
            // Disabling this optional postfix after its first confirmed failure
            // avoids repeating the same exception and expensive GUI work
            // on every subsequent letter-window redraw.
            return !obeliskLetterPostfixDisabled;
        }

        private static Exception ObeliskLetterLinksFinalizer(Exception __exception)
        {
            if (!(__exception is NullReferenceException))
                return __exception;

            obeliskLetterPostfixDisabled = true;
            if (!obeliskLetterGuardLogged)
            {
                obeliskLetterGuardLogged = true;
                Log.Warning("[Z Adaptive] Disabled Obelisk Control's failing optional letter-link postfix " +
                    "after its first null exception; further UI redraws bypass that routine. " +
                    "Vanilla letter choices remain available.");
            }

            // Preserve every unrelated exception and other UI patch.
            return null;
        }

        // Player(20261010-011531).log, 10:23:54 and 11:03:36:
        // GridCellTemperature.Access.GenTemperature_TryGetTemperatureForCell
        // indexes a private float[] before performing the vanilla map bounds
        // check, corrupting baby-safety job execution on an invalid cell.
        // Skip ONLY GridCellTemperature's prefix for invalid map/cell inputs
        // and let RimWorld's original bounds-aware temperature query run.
        // Never substitute ambient temperature on any valid cell.
        private static void PatchGridCellTemperatureBounds(Harmony harmony)
        {
            Type patch = AccessTools.TypeByName(
                "GridCellTemperature.Access.GenTemperature_TryGetTemperatureForCell");
            if (patch == null)
                return;

            MethodInfo target = AccessTools.Method(patch, "Prefix");
            MethodInfo prefix = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(GridTemperatureBadCellPrefix));
            if (target == null || prefix == null)
            {
                Log.Warning("[Z Adaptive] GridCellTemperature prefix changed; no compatibility bounds guard installed.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
        }

        private static bool GridTemperatureBadCellPrefix(
            IntVec3 c, Map map, ref float tempResult, ref bool __result)
        {
            if (map != null && c.InBounds(map))
                return true;

            if (!invalidTemperatureCellGuardLogged)
            {
                invalidTemperatureCellGuardLogged = true;
                Log.Warning("[Z Adaptive] GridCellTemperature received an invalid map cell; " +
                    "delegating temperature resolution to vanilla's safe bounds check.");
            }

            tempResult = 21f;
            // This is the return value of the GridCellTemperature Harmony
            // prefix itself. True instructs Harmony to run the vanilla
            // GenTemperature.TryGetTemperatureForCell implementation.
            __result = true;
            return false;
        }

        // Diagnostic only. Preserve the original exception and terrain/grid data.
        private static void PatchWorldGenerationDiagnostics(Harmony harmony)
        {
            MethodInfo generate = AccessTools.Method(typeof(WorldGenerator), "GenerateWorld");
            MethodInfo reach = AccessTools.Method(typeof(WorldReachability), "InvalidateAllFields");
            MethodInfo begin = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap), nameof(WorldgenStartInfo));
            MethodInfo failure = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap), nameof(WorldReachabilityFaultInfo));
            if (generate != null && begin != null)
                harmony.Patch(generate, prefix: new HarmonyMethod(begin));
            if (reach != null && failure != null)
                harmony.Patch(reach, finalizer: new HarmonyMethod(failure));
        }

        private static void WorldgenStartInfo()
        {
            Log.Message("[Z Adaptive] Worldgen start: " + MemoryStat());
        }

        private static Exception WorldReachabilityFaultInfo(Exception __exception)
        {
            if (__exception != null)
                Log.Error("[Z Adaptive] World reachability threw (exception preserved): " +
                    __exception + "; " + MemoryStat());
            return __exception;
        }

        private static string MemoryStat()
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    return "process private=" + (process.PrivateMemorySize64 / (1024L * 1024L)) +
                        " MiB; managed=" + (GC.GetTotalMemory(false) / (1024L * 1024L)) + " MiB";
            }
            catch (Exception ex) { return "memory counters unavailable: " + ex.GetType().Name; }
        }


        // [MAP] Mechanoid Mechanitor may recalculate faction goodwill during
        // new-game setup before Faction.OfPlayer or ideology trackers are ready.
        // RimWorld GoodwillSituationWorker_SameIdeo assumes they are non-null.
        // Preserve the *native* goodwill formula whenever both factions have
        // valid trackers; return zero only when ideology comparisons are impossible.
        private static void PatchMechanoidMechanitorIdeoInitialization(Harmony harmony)
        {
            if (AccessTools.TypeByName(
                "MAP_MechanoidMechanitor.Scenarios.MechanoidMechanitorPermanentEnemyCache_InitialRelationsApplied_Patch") == null)
                return;

            MethodInfo target = AccessTools.Method(typeof(GoodwillSituationWorker_SameIdeo),
                nameof(GoodwillSituationWorker_SameIdeo.GetNaturalGoodwillOffset),
                new[] { typeof(Faction) });
            MethodInfo prefix = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(MechanoidMechanitorSameIdeoPrefix));
            if (target != null && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        private static bool MechanoidMechanitorSameIdeoPrefix(Faction other, ref int __result)
        {
            if (Find.IdeoManager != null &&
                Faction.OfPlayer != null &&
                Faction.OfPlayer.ideos != null &&
                other != null &&
                other.ideos != null)
                return true;

            // No valid ideology relationship exists at this moment. The
            // appropriate neutral offset is zero, not a new faction hostility.
            __result = 0;
            return false;
        }

        // 2026-10-09 Player.log: VGE GravEngineTracker.GetGravEngine_ListerThings
        // repeatedly sends an invalid def to ListerThings.ThingsOfDef, generating
        // 34k+ invalid ThingRequest exceptions and cascading memory pressure.
        // The upstream implementation also queries engine.minifiedDef without a
        // null check. Keep all valid engine lookups and boarding behavior intact.
        private static void PatchVgeGravEngineListerGuard(Harmony harmony)
        {
            Type tracker = AccessTools.TypeByName("VanillaGravshipExpanded.GravEngineTracker");
            if (tracker == null)
                return;

            MethodInfo target = AccessTools.Method(tracker, "GetGravEngine_ListerThings",
                new[] { typeof(Map) });
            MethodInfo original = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef),
                new[] { typeof(ThingDef) });
            MethodInfo replacement = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(SafeVgeGravEngineThingsOfDef));
            MethodInfo transpiler = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(VgeGravEngineListerTranspiler));

            if (target == null || original == null || replacement == null || transpiler == null)
            {
                Log.Warning("[Z Adaptive] VGE grav-engine invalid-def guard could not resolve its exact method; leaving upstream behavior intact.");
                return;
            }

            try
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] VGE grav-engine invalid-def guard failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static IEnumerable<CodeInstruction> VgeGravEngineListerTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef),
                new[] { typeof(ThingDef) });
            MethodInfo safe = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(SafeVgeGravEngineThingsOfDef));
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (original != null && safe != null && instruction.Calls(original))
                {
                    // Retain labels and exception blocks attached to the original IL.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = safe;
                    replacements++;
                }
                yield return instruction;
            }

            if (replacements == 0)
                Log.Warning("[Z Adaptive] VGE grav-engine lookup changed; no ThingsOfDef call was replaced.");
        }

        private static List<Thing> SafeVgeGravEngineThingsOfDef(ListerThings lister, ThingDef def)
        {
            // Undefined ThingRequests cannot be passed to ListerThings.ThingsMatching.
            // Returning an empty list means only the invalid *candidate* is skipped;
            // the remaining grav engines and world-object fallbacks are still searched.
            if (def == null || ThingRequest.ForDef(def).group == ThingRequestGroup.Undefined)
            {
                if (!invalidVgeThingRequestLogged)
                {
                    invalidVgeThingRequestLogged = true;
                    Log.Warning("[Z Adaptive] Skipped a missing/unlistable Vanilla Gravship Expanded grav-engine or minified def instead of throwing Invalid ThingRequest during pawn AI.");
                }
                return EmptyVgeThingList;
            }

            return lister != null ? lister.ThingsOfDef(def) : EmptyVgeThingList;
        }

        // Giddy-Up 2's SetDead postfix omits the ExtendedDataStorage.Singleton
        // null check already present in its MakeDowned postfix. Pawns can die
        // during new-world initialization before that world component exists.
        private static void PatchGiddyUpDeathInitGuard(Harmony harmony)
        {
            Type patchType = AccessTools.TypeByName("GiddyUp.Harmony.Patch_SetDead");
            Type storageType = AccessTools.TypeByName("GiddyUp.ExtendedDataStorage");
            if (patchType == null || storageType == null)
                return;

            MethodInfo target = AccessTools.Method(patchType, "Postfix",
                new[] { typeof(Pawn_HealthTracker) });
            giddyUpStorageGetter = AccessTools.PropertyGetter(storageType, "Singleton");
            MethodInfo prefix = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(GiddyUpSetDeadStorageReadyPrefix));
            if (target == null || prefix == null || giddyUpStorageGetter == null)
            {
                Log.Warning("[Z Adaptive] Giddy-Up death-init guard could not resolve storage accessor; no patch installed.");
                return;
            }

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Giddy-Up death-init guard failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool GiddyUpSetDeadStorageReadyPrefix(Pawn_HealthTracker __0)
        {
            if (__0 == null)
                return false;

            // Pawn_HealthTracker.pawn is not public in the RimWorld 1.6 reference
            // assembly. Read the same instance field reflectively and fail open
            // if upstream layout changes.
            if (GiddyUpHealthPawnField != null && GiddyUpHealthPawnField.GetValue(__0) == null)
                return false;

            try
            {
                if (giddyUpStorageGetter?.Invoke(null, null) != null)
                    return true;

                if (!giddyUpUninitializedDeathLogged)
                {
                    giddyUpUninitializedDeathLogged = true;
                    Log.Message("[Z Adaptive] Deferred Giddy-Up's optional death dismount before its world storage was initialized.");
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Giddy-Up readiness probe failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
                return true;
            }
        }

        // A generated role with no matching required-apparel records can cause
        // Precept_Role.GetTip -> AllApparelRequirementLabels -> Enumerable.First
        // to throw every GUI frame. Only provide a fallback tooltip for this
        // exact LINQ failure; ideology roles and apparel requirements are unchanged.
        private static void PatchMissingIdeoApparelTipGuard(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(Precept_Role), "GetTip", Type.EmptyTypes);
            MethodInfo finalizer = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(MissingIdeoApparelTipFinalizer));
            if (target != null && finalizer != null)
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
        }

        private static Exception MissingIdeoApparelTipFinalizer(
            Exception __exception, ref string __result)
        {
            if (__exception is InvalidOperationException &&
                string.Equals(__exception.Message, "Sequence contains no elements",
                    StringComparison.Ordinal))
            {
                __result = "An ideology role requires apparel that is unavailable in the current mod configuration.";
                if (!invalidRoleApparelTipLogged)
                {
                    invalidRoleApparelTipLogged = true;
                    Log.Warning("[Z Adaptive] A role apparel tooltip had no matching apparel; showed a diagnostic tooltip instead of repeatedly throwing.");
                }
                return null;
            }
            return __exception;
        }

        private static void PatchStaticAtlas(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(StaticTextureAtlas), "CalcRectsForAtlasNew");
            MethodInfo postfix = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap), nameof(StaticAtlasPostfix));
            if (target != null && postfix != null)
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            else
                Log.Warning("[Z Adaptive] Could not locate StaticTextureAtlas.CalcRectsForAtlasNew; mip-allocation guard not applied.");
        }

        private static void PatchGraphicSingleAtlasInsertion(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(Graphic_Single), nameof(Graphic_Single.TryInsertIntoAtlas));
            MethodInfo prefix = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap), nameof(GraphicSingleAtlasPrefix));
            if (target != null && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        private static void PatchGraphicRequestNullPath(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(GraphicDatabase),
                "Get",
                new[] { typeof(GraphicRequest) });
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(GraphicRequestNullPathPrefix));

            if (target != null && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Could not install the null GraphicRequest path guard.");
        }

        private static void GraphicRequestNullPathPrefix(ref GraphicRequest req)
        {
            NormalizeGraphicRequest(ref req);
        }

        private static void GraphicMultiRequestNullPathPrefix(ref GraphicRequest req)
        {
            NormalizeGraphicRequest(ref req);
            // Graphic_Multi.Init calls ContentFinder for directional textures using req.path.
            // A supplied req.texture does not make a null path safe for Graphic_Multi.
            if (string.IsNullOrWhiteSpace(req.path))
            {
                req.path = "ZAdaptive/Placeholder";
                if (!graphicRequestNullPathLogged)
                {
                    graphicRequestNullPathLogged = true;
                    Log.Warning("[Z Adaptive] Replaced a null Graphic_Multi path with the bundled directional placeholder.");
                }
            }
        }

        private static void PatchGraphicInitRequests(Harmony harmony)
        {
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(GraphicRequestNullPathPrefix));

            MethodInfo singleInit = AccessTools.Method(
                typeof(Graphic_Single),
                "Init",
                new[] { typeof(GraphicRequest) });
            MethodInfo multiInit = AccessTools.Method(
                typeof(Graphic_Multi),
                "Init",
                new[] { typeof(GraphicRequest) });

            if (prefix == null)
                return;

            if (singleInit != null)
                harmony.Patch(singleInit, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Could not install the Graphic_Single request guard.");

            // A Graphic_Multi still resolves directional texture paths even when a caller
            // supplies req.texture. Its Init needs a stricter missing-path guard than Single.
            MethodInfo multiPrefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(GraphicMultiRequestNullPathPrefix));
            if (multiInit != null && multiPrefix != null)
                harmony.Patch(multiInit, prefix: new HarmonyMethod(multiPrefix));
            else
                Log.Warning("[Z Adaptive] Could not install the Graphic_Multi request guard.");
        }

        private static void NormalizeGraphicRequest(ref GraphicRequest req)
        {
            // Def-time patching cannot catch every generated/inherited graphic request. Guard the
            // final Graphic.Init boundary as well, which covers construction-ghost graphics and
            // dynamically generated requests seen in the live 1.6 stack.
            if (req.texture == null && string.IsNullOrWhiteSpace(req.path))
            {
                req.path = "ZAdaptive/Placeholder";
                if (!graphicRequestNullPathLogged)
                {
                    graphicRequestNullPathLogged = true;
                    Log.Warning("[Z Adaptive] Replaced a null GraphicRequest path with the bundled placeholder texture.");
                }
                return;
            }

            string replacement = null;
            switch (req.path)
            {
                case "Things/Pawn/Animal/Megascarab/MegascarabPack":
                    replacement = "Things/Pawn/Animal/Megascarab/Megascarab";
                    break;
                case "Things/Pawn/Animal/Spelopede/SpelopedePack":
                    replacement = "Things/Pawn/Animal/Spelopede/Spelopede";
                    break;
                case "BMT_Bees/Things/Animal/Bees/BeeSwarmPack":
                    replacement = "BMT_Bees/Things/Animal/Bees/BeeSwarm";
                    break;
                case "Things/Pawn/Animal/Creamgrub/CreamgrubPack":
                    replacement = "Things/Pawn/Animal/Creamgrub/Creamgrub";
                    break;
                case "BMT_Caverns/Things/Animal/Jellypot/JellypotPack":
                    replacement = "BMT_Caverns/Things/Animal/Jellypot/Jellypot";
                    break;
            }

            if (replacement == null)
                return;

            req.path = replacement;
            if (!knownGraphicAliasLogged)
            {
                knownGraphicAliasLogged = true;
                Log.Warning("[Z Adaptive] Redirected an obsolete animal '*Pack' texture request to its live 1.6 texture stem.");
            }
        }

        private static void PatchInvalidEquipmentDrops(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(Pawn_EquipmentTracker),
                "TryDropEquipment",
                new[]
                {
                    typeof(ThingWithComps),
                    typeof(ThingWithComps).MakeByRefType(),
                    typeof(IntVec3),
                    typeof(bool)
                });
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(InvalidEquipmentDropPrefix));

            if (target != null && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Could not install the invalid equipment-drop guard.");
        }

        private static bool InvalidEquipmentDropPrefix(
            Pawn_EquipmentTracker __instance,
            IntVec3 __2,
            ref bool __result)
        {
            // Several mods can request an equipment drop while a pawn is being transferred,
            // despawned, or otherwise has IntVec3.Invalid as its current cell. Vanilla cannot
            // place an item at that coordinate; allowing the call only produces an error loop.
            if (__2.IsValid)
                return true;

            __result = false;
            if (!invalidEquipmentDropSuppressedLogged)
            {
                invalidEquipmentDropSuppressedLogged = true;
                Log.Warning("[Z Adaptive] Rejected an equipment drop at IntVec3.Invalid during a pawn/map transition.");
            }
            return false;
        }

        private static void PatchFireDefinitiveEdition(Harmony harmony)
        {
            Type firePanicType = AccessTools.TypeByName("FireDefinitiveEdition.MapComponent_FirePanic");
            if (firePanicType == null)
                return;

            MethodInfo target = AccessTools.Method(firePanicType, "MapComponentTick");
            MethodInfo finalizer = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(FirePanicTickFinalizer));

            if (target != null && target.DeclaringType == firePanicType && finalizer != null)
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
            else
                Log.Warning("[Z Adaptive] Fire Definitive Edition is loaded but its FirePanic tick guard could not be installed.");
        }

        private static Exception FirePanicTickFinalizer(Exception __exception)
        {
            if (__exception is InvalidOperationException &&
                __exception.Message != null &&
                __exception.Message.IndexOf("Collection was modified", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!firePanicEnumerationSuppressedLogged)
                {
                    firePanicEnumerationSuppressedLogged = true;
                    Log.Warning("[Z Adaptive] Fire Definitive Edition modified its FirePanic collection while enumerating it; aborted that tick safely instead of propagating the exception.");
                }
                return null;
            }

            return __exception;
        }

        private static void PatchPsychicShockTarget(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(CompTargetEffect_PsychicShock),
                "DoEffectOn",
                new[] { typeof(Pawn), typeof(Thing) });
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(PsychicShockTargetPrefix));

            if (target != null && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Could not install the PsychicShock non-pawn target guard.");
        }

        private static bool PsychicShockTargetPrefix(Thing __1)
        {
            // CompTargetEffect_PsychicShock assumes its Thing target is a Pawn and casts directly.
            // In the live 1.6 stack a target-effect verb can reach this method with a non-pawn
            // target, producing a per-tick InvalidCastException loop. Preserve vanilla behavior
            // for valid pawn targets and reject only the incompatible target type.
            if (__1 is Pawn)
                return true;

            if (!psychicShockTargetGuardLogged)
            {
                psychicShockTargetGuardLogged = true;
                Log.Warning("[Z Adaptive] Suppressed PsychicShock on a non-pawn target to prevent CompTargetEffect_PsychicShock InvalidCastException loops.");
            }
            return false;
        }

        private static void PatchIdeologyGrammar(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(NameGenerator),
                nameof(NameGenerator.GenerateName),
                new[]
                {
                    typeof(GrammarRequest),
                    typeof(Predicate<string>),
                    typeof(bool),
                    typeof(string),
                    typeof(string)
                });
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(IdeologyGrammarPrefix));

            if (target != null && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Could not install the RimWorld 1.6 ideology grammar bridge guard.");
        }

        private static void IdeologyGrammarPrefix(ref GrammarRequest request, string rootKeyword)
        {
            if (rootKeyword != "r_deityType" && rootKeyword != "r_deityName")
                return;

            // A missing/migrated culture naming RulePackDef can leave a null
            // entry in a GrammarRequest.Includes list. RimWorld 1.6 HasRule()
            // dereferences each entry directly; our previous bridge called it
            // before checking this input and caused a faction-worldgen NRE.
            // Remove only invalid null entries, leaving every surviving pack
            // and the generated ideology's naming grammar intact.
            int nullPacks = request.Includes.RemoveAll(pack => pack == null);
            if (nullPacks > 0 && !ideologyNullRulePackLogged)
            {
                ideologyNullRulePackLogged = true;
                Log.Warning("[Z Adaptive] Dropped null deity naming RulePackDef reference(s) before HasRule to protect faction world generation.");
            }

            // RimWorld 1.6 deity-type grammar can request memeConceptDef while generated meme packs
            // provide only memeConcept. Bridge the two only when the requested root is r_deityType,
            // the destination symbol is genuinely absent, and the source symbol is actually present.
            // This preserves all upstream/custom content and only supplies the missing grammar edge.
            if (rootKeyword == "r_deityType" &&
                !request.HasRule("memeConceptDef") &&
                request.HasRule("memeConcept"))
            {
                request.Rules.Add(new Rule_String("memeConceptDef", "[memeConcept]"));
                if (!ideologyDeityTypeBridgeLogged)
                {
                    ideologyDeityTypeBridgeLogged = true;
                    Log.Message("[Z Adaptive] Added the missing memeConceptDef -> memeConcept bridge for RimWorld 1.6 deity-type generation.");
                }
            }

            // Some custom cultures assign a person-name RulePackDef as their deity-name maker.
            // If that pack supplies r_name but not the r_deityName root expected by Ideology,
            // reuse the pack's own generated name rather than inventing or replacing any content.
            if (rootKeyword == "r_deityName" &&
                !request.HasRule("r_deityName") &&
                request.HasRule("r_name"))
            {
                request.Rules.Add(new Rule_String("r_deityName", "[r_name]"));
                if (!ideologyDeityNameBridgeLogged)
                {
                    ideologyDeityNameBridgeLogged = true;
                    Log.Message("[Z Adaptive] Bridged r_deityName to an existing r_name rule for a custom ideology deity namer.");
                }
            }
        }

        private static void PatchVanillaGravshipExpanded(Harmony harmony)
        {
            Type landingBaseType = AccessTools.TypeByName("VanillaGravshipExpanded.LandingStructureBase");
            Type landingType = AccessTools.TypeByName("VanillaGravshipExpanded.LandingStructure");
            if (landingBaseType == null && landingType == null)
                return;

            // Current VGE performs the chroma-key Material.color read in
            // LandingStructureBase.RenderAndSaveTexture.  Older builds used a DoCapture path,
            // so keep the fallback for backwards compatibility with the user's long-lived stack.
            MethodInfo capturePath = landingBaseType == null
                ? null
                : AccessTools.Method(landingBaseType, "RenderAndSaveTexture");
            if (capturePath == null && landingType != null)
                capturePath = AccessTools.Method(landingType, "DoCapture");

            MethodInfo transpiler = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap), nameof(VgeDoCaptureTranspiler));
            if (capturePath != null && transpiler != null)
                harmony.Patch(capturePath, transpiler: new HarmonyMethod(transpiler));
            else
                Log.Warning("[Z Adaptive] Vanilla Gravship Expanded is loaded but its landing capture render path could not be patched.");
        }

        private static void PatchVehicleFrameworkGravTide(Harmony harmony)
        {
            Type pathingHelperType = AccessTools.TypeByName("Vehicles.PathingHelper");
            Type gravTideType = AccessTools.TypeByName("GravTide.TidalPainter");
            if (pathingHelperType == null || gravTideType == null)
                return;

            vehiclePathingSystemType = AccessTools.TypeByName("Vehicles.VehiclePathingSystem");
            MethodInfo target = AccessTools.Method(
                pathingHelperType,
                "RecalculatePerceivedPathCostAt",
                new[] { typeof(IntVec3), typeof(Map) });
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(VehiclePathingReadyPrefix));

            if (target != null && prefix != null && vehiclePathingSystemType != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Vehicle Framework + GravTide detected, but the early-map vehicle pathing guard could not be installed.");
        }

        private static void PatchGeologicalLandformsGravTide(Harmony harmony)
        {
            Type gravLightningPatchType = AccessTools.TypeByName("GravTide.WeatherEvent_LightningStrike_FireEvent_Patch");
            // Current GravTide declares the patch as a nested class; preserve the
            // legacy flat-name fallback for older installed versions.
            Type gravGrowthPatchType = AccessTools.TypeByName("GravTide.SeabedPlantGrowth+BuildFor_Patch")
                ?? AccessTools.TypeByName("GravTide.BuildFor_Patch");
            Type landformsLightningPatchType = AccessTools.TypeByName("GeologicalLandforms.Patches.Patch_RimWorld_WeatherEvent_LightningStrike");
            Type landformsGrowthPatchType = AccessTools.TypeByName("GeologicalLandforms.Patches.Patch_Verse_MapPlantGrowthRateCalculator");

            if ((gravLightningPatchType == null && gravGrowthPatchType == null) ||
                (landformsLightningPatchType == null && landformsGrowthPatchType == null))
                return;

            PatchGeologicalLandformsGravTideLightning(
                harmony,
                gravLightningPatchType,
                landformsLightningPatchType);

            PatchGeologicalLandformsGravTidePlantGrowth(
                harmony,
                gravGrowthPatchType,
                landformsGrowthPatchType);
        }

        private static void PatchGeologicalLandformsGravTideLightning(
            Harmony harmony,
            Type gravLightningPatchType,
            Type landformsLightningPatchType)
        {
            if (gravLightningPatchType == null || landformsLightningPatchType == null)
                return;

            // Preserved native Geological Landforms and GravTide lightning patches.
            // The Geological Landforms FireEvent prefix accesses map.TileInfo directly.
            // GravTide's generated seabed is a PocketMap with no world tile. Intercept
            // *only that prefix method*, not WeatherEvent_LightningStrike.FireEvent:
            // ordinary landform lightning and the GravTide targeting prefixes are left
            // intact. For linked pocket maps use the parent surface tile's hilliness.
            MethodInfo target = AccessTools.Method(landformsLightningPatchType,
                "FireEvent", new[] { typeof(Map) });
            MethodInfo guard = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap),
                nameof(GeologicalLandformsLightningPrefix));
            if (target == null || guard == null)
            {
                Log.Warning("[Z Adaptive] Geological Landforms pocket-map lightning guard unavailable; original patches preserved.");
                return;
            }

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(guard));
                Log.Message("[Z Adaptive] Guarded Geological Landforms lightning hilliness lookup on linked pocket maps without replacing the upstream FireEvent patch.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Geological Landforms pocket-map lightning guard failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void PatchGeologicalLandformsGravTidePlantGrowth(
            Harmony harmony,
            Type gravGrowthPatchType,
            Type landformsGrowthPatchType)
        {
            if (gravGrowthPatchType == null || landformsGrowthPatchType == null)
                return;

            try
            {
                MethodInfo gravPrefix = AccessTools.Method(gravGrowthPatchType, "Prefix");
                MethodInfo landformsTranspiler = AccessTools.Method(landformsGrowthPatchType, "BuildFor_Transpiler");
                MethodInfo compatTranspiler = AccessTools.Method(
                    typeof(ZAdaptiveRuntimeBootstrap),
                    nameof(GeologicalLandformsTileCompatTranspiler));

                if (gravPrefix == null || landformsTranspiler == null || compatTranspiler == null)
                {
                    Log.Warning("[Z Adaptive] Geological Landforms + GravTide plant-growth compatibility targets were not all found.");
                    return;
                }

                // GravTide 2026.10.09 uses SeaPlace.SurfaceTileOf(map) in this
                // prefix, providing a valid world tile even on an underwater
                // pocket map. Check the actual method IL before assuming this
                // remains true for a future or older GravTide version.
                Type seaPlace = AccessTools.TypeByName("GravTide.SeaPlace");
                MethodInfo surfaceTile = seaPlace == null ? null : AccessTools.Method(
                    seaPlace, "SurfaceTileOf", new[] { typeof(Map) });
                if (surfaceTile != null && CallsExactStaticMethod(gravPrefix, surfaceTile))
                {
                    Log.Message("[Z Adaptive] Geological Landforms + GravTide: native surface-tile handling verified; skipped redundant growth transpiler.");
                    return;
                }

                // Geological Landforms already protects the vanilla BuildFor path. GravTide can
                // skip that original method from its own Prefix, so apply the same tile substitution
                // only inside GravTide's override. Do not replace or unpatch Geological Landforms'
                // own transpiler: leaving the upstream patch intact avoids a destructive-patch
                // conflict while still covering the path that would otherwise bypass it.
                HarmonyMethod compat = new HarmonyMethod(compatTranspiler);
                harmony.Patch(gravPrefix, transpiler: compat);

                Log.Message("[Z Adaptive] Installed Geological Landforms + GravTide pocket-map compatibility on the GravTide override path.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Geological Landforms + GravTide plant-growth compatibility failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Inspect direct call metadata without invoking a foreign mod method.
        // If IL is unavailable or its call layout changed, preserve the older
        // compatibility transpiler rather than assuming native support.
        private static bool CallsExactStaticMethod(MethodInfo caller, MethodInfo callee)
        {
            if (caller == null || callee == null || caller.Module != callee.Module)
                return false;

            try
            {
                byte[] il = caller.GetMethodBody()?.GetILAsByteArray();
                if (il == null)
                    return false;

                byte[] token = BitConverter.GetBytes(callee.MetadataToken);
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x28 && il[i] != 0x6F)
                        continue;
                    if (il[i + 1] == token[0] && il[i + 2] == token[1] &&
                        il[i + 3] == token[2] && il[i + 4] == token[3])
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] GravTide growth-call probe failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
            return false;
        }

        // Harmony's __0 binds to the original GL prefix method's first parameter;
        // using ___map here would incorrectly request an instance field from GL's
        // static patch class. Returning false skips only GL's vulnerable prefix body.
        private static bool GeologicalLandformsLightningPrefix(Map __0, ref bool __result)
        {
            if (__0 == null || __0.Tile >= 0 ||
                !(__0.Parent is PocketMapParent parent) ||
                parent.sourceMap == null || parent.sourceMap.Tile < 0)
                return true;

            // Preserve GL's exact 30% chance on an impassable surface, as though
            // its original code had read the parent map instead of the invalid pocket.
            __result = parent.sourceMap.TileInfo.hilliness != Hilliness.Impassable ||
                       Rand.Value < 0.3f;
            return false;
        }

        private static IEnumerable<CodeInstruction> GeologicalLandformsTileCompatTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getTile = AccessTools.PropertyGetter(typeof(Map), nameof(Map.Tile));
            MethodInfo safeTile = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(TileForMapCompat));

            foreach (CodeInstruction instruction in instructions)
            {
                if (getTile != null && safeTile != null && instruction.Calls(getTile))
                {
                    // Mutate the existing instruction so Harmony labels/exception blocks remain
                    // attached to the exact IL position.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = safeTile;
                }

                yield return instruction;
            }
        }

        private static PlanetTile TileForMapCompat(Map map)
        {
            return map.Tile < 0 &&
                   map.Parent is PocketMapParent parent &&
                   parent.sourceMap != null &&
                   parent.sourceMap.Tile >= 0
                ? parent.sourceMap.Tile
                : map.Tile;
        }

        private static void PatchScrollMentalStateTarget(Harmony harmony)
        {
            Type scrollType = AccessTools.TypeByName("RomyScrolls.CompTargetEffect_ScrollGiveMentalState");
            if (scrollType == null)
                return;

            MethodInfo target = AccessTools.Method(
                scrollType,
                "DoEffectOn",
                new[] { typeof(Pawn), typeof(Thing) });
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(ScrollMentalStateTargetPrefix));

            // Do not accidentally patch an inherited generic target-effect method if a future
            // Scrolls release removes or renames this concrete override.
            if (target != null && target.DeclaringType == scrollType && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Scrolls mental-state target effect was detected but its concrete DoEffectOn override could not be guarded.");
        }

        private static bool ScrollMentalStateTargetPrefix(Thing __1)
        {
            if (__1 is Pawn)
                return true;

            if (!scrollMentalStateTargetGuardLogged)
            {
                scrollMentalStateTargetGuardLogged = true;
                Log.Warning("[Z Adaptive] Suppressed a Scrolls mental-state effect on a non-pawn target; this prevents CompTargetEffect_ScrollGiveMentalState from invalidly casting Thing to Pawn.");
            }
            return false;
        }

        private static void PatchInvisibilityPsychology(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(InvisibilityUtility),
                nameof(InvisibilityUtility.IsPsychologicallyInvisible),
                new[] { typeof(Pawn) });
            MethodInfo prefix = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(InvisibilityPsychologyPrefix));

            if (target != null && prefix != null)
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            else
                Log.Warning("[Z Adaptive] Could not install the dead/null-mind invisibility render guard.");
        }

        private static bool InvisibilityPsychologyPrefix(Pawn pawn, ref bool __result)
        {
            // Corpses can retain an invisibility hediff after the pawn mindState has been torn down.
            // Vanilla HediffComp_Invisibility dereferences mindState while the corpse is rendered,
            // which can abort DynamicDrawManager and leave later spawn/despawn operations occurring
            // inside a draw pass. A dead pawn has no gameplay reason to remain psychologically
            // invisible, so fail visible without touching living-pawn invisibility behavior.
            if (pawn != null && !pawn.Dead && pawn.mindState != null)
                return true;

            __result = false;
            if (!invisibilityCorpseGuardLogged)
            {
                invisibilityCorpseGuardLogged = true;
                Log.Warning("[Z Adaptive] Bypassed psychological invisibility for a dead/null-mind pawn during rendering to prevent HediffComp_Invisibility null-reference draw failures.");
            }
            return false;
        }

        private static void PatchAutoNameBabies(Harmony harmony)
        {
            Type babyNamerType = AccessTools.TypeByName("AutoNameBabies.BabyNamer");
            if (babyNamerType == null)
                return;

            MethodInfo target = AccessTools.Method(babyNamerType, "NameUnnamedPlayerBabies");
            MethodInfo finalizer = AccessTools.Method(
                typeof(ZAdaptiveRuntimeBootstrap),
                nameof(AutoNameBabiesFinalizer));

            if (target != null && finalizer != null)
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
            else
                Log.Warning("[Z Adaptive] Auto Name Babies is loaded but its new-game naming guard could not be installed.");
        }

        private static bool VehiclePathingReadyPrefix(Map map)
        {
            // GravTide paints and replaces terrain during map initialization. Vehicle Framework's
            // TerrainGrid hook immediately asks its cached VehiclePathingSystem to refresh costs,
            // but that map component may not exist yet. Skipping those pre-initialization refreshes
            // is safe because Vehicle Framework builds its path grids from the finished terrain once
            // VehiclePathingSystem is constructed.
            if (map == null || map.Disposed)
                return false;

            try
            {
                List<MapComponent> components = map.components;
                if (components != null)
                {
                    for (int i = 0; i < components.Count; i++)
                    {
                        MapComponent component = components[i];
                        if (component != null && vehiclePathingSystemType.IsInstanceOfType(component))
                            return true;
                    }
                }

                if (!vehiclePathingDeferredLogged)
                {
                    vehiclePathingDeferredLogged = true;
                    Log.Message("[Z Adaptive] Deferred Vehicle Framework terrain path-cost refresh until VehiclePathingSystem is initialized (GravTide map setup).");
                }
                return false;
            }
            catch (Exception ex)
            {
                // If the compatibility probe itself ever becomes stale, fail open and preserve the
                // upstream behavior rather than disabling vehicle pathing.
                if (!vehiclePathingGuardFailureLogged)
                {
                    vehiclePathingGuardFailureLogged = true;
                    Log.Warning("[Z Adaptive] Vehicle Framework + GravTide pathing readiness probe failed open: " + ex.GetType().Name + ": " + ex.Message);
                }
                return true;
            }
        }

        private static Exception AutoNameBabiesFinalizer(Exception __exception)
        {
            if (__exception is InvalidOperationException &&
                __exception.Message != null &&
                __exception.Message.IndexOf("Collection was modified", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!autoNameBabiesSuppressedLogged)
                {
                    autoNameBabiesSuppressedLogged = true;
                    Log.Warning("[Z Adaptive] Auto Name Babies modified its pawn collection while iterating during Game.FinalizeInit; suppressed that optional naming pass so new-game initialization can continue.");
                }
                return null;
            }

            return __exception;
        }

        private static bool IsGravshipChromaKey(Material material)
        {
            return material != null &&
                (material.name == "GravshipChromaKey" ||
                 (material.shader != null && material.shader.name == "Custom/Gravship chroma key"));
        }

        private static bool GraphicSingleAtlasPrefix(Graphic_Single __instance)
        {
            try
            {
                Material mat = __instance?.MatSingle;
                if (IsGravshipChromaKey(mat) && !mat.HasProperty("_MainTex"))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Gravship chroma-key atlas guard failed safely: " + ex.GetType().Name + ": " + ex.Message);
            }
            return true;
        }

        private static void StaticAtlasPostfix(StaticTextureAtlas __instance)
        {
            try
            {
                FieldInfo field = AccessTools.Field(typeof(StaticTextureAtlas), "colorTexture");
                Texture2D current = field?.GetValue(__instance) as Texture2D;
                if (current == null)
                    return;

                int maxAxis = Math.Max(current.width, current.height);
                int requiredMipCount = Mathf.FloorToInt(Mathf.Log(Math.Max(1f, maxAxis / 512f), 2f)) + 1;
                requiredMipCount = Math.Max(1, requiredMipCount);
                if (current.mipmapCount >= requiredMipCount)
                    return;

                Texture2D replacement = new Texture2D(
                    current.width,
                    current.height,
                    current.graphicsFormat,
                    requiredMipCount,
                    TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels);
                replacement.name = current.name;
                replacement.filterMode = current.filterMode;
                replacement.wrapMode = current.wrapMode;
                replacement.anisoLevel = current.anisoLevel;
                replacement.mipMapBias = current.mipMapBias;
                field.SetValue(__instance, replacement);
                UnityEngine.Object.DestroyImmediate(current);
                Log.Message($"[Z Adaptive] Corrected static texture atlas mip allocation to {requiredMipCount} level(s) for {replacement.width}x{replacement.height} atlas.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Static atlas mip-allocation guard failed safely: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static IEnumerable<CodeInstruction> VgeDoCaptureTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getColor = AccessTools.PropertyGetter(typeof(Material), nameof(Material.color));
            MethodInfo safeColor = AccessTools.Method(typeof(ZAdaptiveRuntimeBootstrap), nameof(SafeMaterialColor));

            foreach (CodeInstruction instruction in instructions)
            {
                if (getColor != null && safeColor != null && instruction.Calls(getColor))
                {
                    yield return new CodeInstruction(OpCodes.Call, safeColor);
                    continue;
                }
                yield return instruction;
            }
        }

        public static Color SafeMaterialColor(Material material)
        {
            if (material == null)
                return Color.clear;

            if (IsGravshipChromaKey(material) && !material.HasProperty("_Color"))
                return Color.clear;

            return material.color;
        }
    }
}
