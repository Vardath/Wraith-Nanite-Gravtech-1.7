using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// Worldbuilder 1.6 preview cancellation calls Thread.Abort() in its CanDoNext
    /// prefix. RimWorld 1.7 uses .NET 10, where Thread.Abort throws
    /// PlatformNotSupportedException (Player 2026-10-17, 15:31:27).
    ///
    /// Worldbuilder already checks a volatile shouldCancelGeneration flag at
    /// boundaries between preview-generation steps. Do not run full worldgen
    /// until that worker has actually exited and restored Current.* state.
    /// Replace ONLY that Worldbuilder prefix, retaining the other mods' patches.
    /// </summary>
    internal static class WorldbuilderPreviewCancellation17
    {
        private static FieldInfo previewThreadField;
        private static FieldInfo requestCancelField;
        private static FieldInfo previewWorldField;
        private static FieldInfo generatingWorldField;
        private static FieldInfo settingsField;
        private static FieldInfo overhaulEnabledField;
        private static bool pendingGenerate;
        private static bool cancelNoticeLogged;

        internal static void Install(Harmony harmony)
        {
            // This fault is specific to 1.7's managed runtime. Preserve 1.6's
            // existing Worldbuilder implementation and mod compatibility.
            if (VersionControl.CurrentMajor < 1 || VersionControl.CurrentMinor < 7)
                return;

            Type wb = AccessTools.TypeByName("Worldbuilder.WorldbuilderMod");
            Type nextPatch = AccessTools.TypeByName("Worldbuilder.Page_CreateWorldParams_CanDoNext_Patch");
            Type previewPatch = AccessTools.TypeByName("Worldbuilder.Page_CreateWorldParams_DoWindowContents_Patch");
            if (wb == null || nextPatch == null || previewPatch == null)
                return;

            MethodInfo originalPrefix = AccessTools.Method(nextPatch, "Prefix", Type.EmptyTypes);
            MethodInfo next = AccessTools.Method(typeof(Page_CreateWorldParams), "CanDoNext", Type.EmptyTypes);
            MethodInfo refresh = AccessTools.Method(previewPatch, "HandlePreviewAutoGeneration");
            MethodInfo guard = AccessTools.Method(typeof(WorldbuilderPreviewCancellation17), nameof(GuardWorldGenerate));
            MethodInfo holdRefresh = AccessTools.Method(typeof(WorldbuilderPreviewCancellation17), nameof(SkipPreviewRestartWhileGenerating));
            previewThreadField = AccessTools.Field(previewPatch, "thread");
            requestCancelField = AccessTools.Field(previewPatch, "shouldCancelGeneration");
            previewWorldField = AccessTools.Field(previewPatch, "threadedWorld");
            generatingWorldField = AccessTools.Field(previewPatch, "generatingWorld");
            settingsField = AccessTools.Field(wb, "settings");
            Type settingsType = settingsField?.FieldType;
            overhaulEnabledField = settingsType == null ? null : AccessTools.Field(settingsType, "enablePlanetGenOverhaul");
            if (originalPrefix == null || next == null || refresh == null ||
                guard == null || holdRefresh == null || previewThreadField == null ||
                requestCancelField == null || previewWorldField == null ||
                generatingWorldField == null || settingsField == null || overhaulEnabledField == null)
            {
                Log.Warning("[Z Adaptive] Worldbuilder preview cancellation signatures differ; leaving original mod patch unchanged.");
                return;
            }

            var patchInfo = Harmony.GetPatchInfo(next);
            bool upstreamPrefixInstalled = false;
            if (patchInfo != null)
                foreach (var p in patchInfo.Prefixes)
                    if (p.PatchMethod == originalPrefix)
                        upstreamPrefixInstalled = true;

            if (!upstreamPrefixInstalled)
            {
                Log.Warning("[Z Adaptive] Worldbuilder CanDoNext prefix not found; cooperative cancellation not installed.");
                return;
            }

            // Install replacements first. Only then remove the single unsafe
            // Worldbuilder prefix (never other mods' CanDoNext patches).
            harmony.Patch(next, prefix: new HarmonyMethod(guard) { priority = Priority.First });
            harmony.Patch(refresh, prefix: new HarmonyMethod(holdRefresh) { priority = Priority.First });
            harmony.Unpatch(next, originalPrefix);
            Log.Message("[Z Adaptive] Worldbuilder 1.7 preview: replaced Thread.Abort with cooperative cancellation and a generation safety gate.");
        }

        private static bool IsOverhaulEnabled()
        {
            object settings = settingsField.GetValue(null);
            return settings != null && (bool)overhaulEnabledField.GetValue(settings);
        }

        private static bool GuardWorldGenerate(ref bool __result)
        {
            if (!IsOverhaulEnabled())
                return true;

            Thread worker = previewThreadField.GetValue(null) as Thread;
            if (worker != null && worker.IsAlive)
            {
                // The preview worker can be in a lengthy worldgen step. Its
                // existing volatile cancellation flag is checked at boundaries.
                // Blocking the original CanDoNext prevents concurrent writes
                // to Current.CreatingWorld and Current.Game.World.
                requestCancelField.SetValue(null, true);
                pendingGenerate = true;
                __result = false;
                if (!cancelNoticeLogged)
                {
                    cancelNoticeLogged = true;
                    Log.Warning("[Z Adaptive] Worldbuilder: cancelled the preview cooperatively; wait for it to stop, then click Generate again. Full world generation was not started concurrently.");
                }
                return false;
            }

            // Equivalent to Worldbuilder's original Prefix cleanup, but only
            // after the preview has stopped (never orphan a live worker).
            previewThreadField.SetValue(null, null);
            previewWorldField.SetValue(null, null);
            generatingWorldField.SetValue(null, false);
            requestCancelField.SetValue(null, false);
            pendingGenerate = false;
            return true;
        }

        private static bool SkipPreviewRestartWhileGenerating()
        {
            if (!pendingGenerate)
                return true;

            Thread worker = previewThreadField.GetValue(null) as Thread;
            if (worker != null && worker.IsAlive)
                requestCancelField.SetValue(null, true);

            // Otherwise Worldbuilder would immediately restart a cancelled
            // preview in HandlePreviewAutoGeneration, leaving Generate unable
            // to progress even on the second click.
            return false;
        }
    }
}
