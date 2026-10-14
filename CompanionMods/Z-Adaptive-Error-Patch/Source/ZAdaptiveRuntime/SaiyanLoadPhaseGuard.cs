using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// SaiyanMod's HediffSet.DirtyCache postfix can award ability XP while
    /// Scribe is resolving saved Pawn/Hediff cross-references. Taran Magic
    /// AbilityClass.OnLevelGained immediately dereferences compAbilities, but
    /// CompAbilities.PostLoadInit has not necessarily restored that link.
    /// Skip only the Saiyan XP-side-effect hook during serialization, never
    /// ordinary gameplay XP, level ups, or health updates.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class SaiyanLoadPhaseGuard
    {
        private static bool loggedSkip;

        static SaiyanLoadPhaseGuard()
        {
            try
            {
                Type owner = AccessTools.TypeByName("SaiyanMod.DirtyCache_Patch");
                MethodInfo target = owner == null ? null : AccessTools.Method(owner, "Postfix");
                MethodInfo guard = AccessTools.Method(typeof(SaiyanLoadPhaseGuard),
                    nameof(AllowSaiyanHediffXP));
                if (target == null || guard == null)
                    return;

                var harmony = new Harmony("vardath.zadaptive.saiyan.loadphase");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(guard) { priority = Priority.First });
                Log.Message("[Z Adaptive] Saiyan health-cache XP load-phase guard installed.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Saiyan load-phase guard not installed; " +
                    "original behavior preserved: " + ex);
            }
        }

        private static bool AllowSaiyanHediffXP()
        {
            if (Scribe.mode == LoadSaveMode.Inactive)
                return true;
            if (!loggedSkip)
            {
                loggedSkip = true;
                Log.Message("[Z Adaptive] Skipped Saiyan hediff-based ability XP " +
                    "during save serialization/cross-reference restoration. " +
                    "Normal gameplay XP is unaffected.");
            }
            return false;
        }
    }
}
