using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// Compatibility for RJW 1.6 interaction selection failures observed in
    /// Player-prev(7).log. No sex act is synthesized and valid jobs are unchanged.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RJWImagoCompatibility
    {
        private static MethodInfo findInteractions;
        private static Type sexPropsType;
        private static bool missingInteractionLogged;
        private static bool probeFailureLogged;

        static RJWImagoCompatibility()
        {
            try
            {
                Type finder = AccessTools.TypeByName("rjw.Modules.Interactions.SexInteractionFinder");
                sexPropsType = AccessTools.TypeByName("rjw.SexProps");
                if (finder == null || sexPropsType == null)
                    return;

                findInteractions = AccessTools.Method(finder, "FindInteractions",
                    new[] { sexPropsType });
                if (findInteractions == null)
                {
                    Log.Warning("[Z Adaptive] RJW 1.6 interaction resolver not found; " +
                        "no RJW compatibility patches were installed.");
                    return;
                }

                var harmony = new Harmony("vardath.zadaptive.rjw.interactionguards");
                PatchGiver(harmony, "rjw.JobGiver_Masturbate");
                PatchGiver(harmony, "rjw.JobGiver_DoQuickie");
                PatchGiver(harmony, "rjw.JobGiver_JoinInBed");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] RJW 1.6 interaction-availability hook could " +
                    "not initialize; the original RJW logic is unchanged: " + ex);
            }
        }

        private static void PatchGiver(Harmony harmony, string className)
        {
            Type giver = AccessTools.TypeByName(className);
            MethodInfo target = giver == null ? null :
                AccessTools.Method(giver, "TryGiveJob", new[] { typeof(Pawn) });
            MethodInfo postfix = AccessTools.Method(typeof(RJWImagoCompatibility),
                nameof(RejectUnavailableRJWInteraction));
            if (target != null && postfix != null)
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        private static void RejectUnavailableRJWInteraction(Pawn pawn, ref Job __result)
        {
            Job job = __result;
            if (pawn == null || job?.def == null || findInteractions == null)
                return;

            bool solo = job.def.defName == "RJW_Masturbate";
            bool quickie = job.def.defName == "Quickie";
            bool joinInBed = job.def.defName == "JoinInBed";
            if (!solo && !quickie && !joinInBed)
                return;

            Pawn partner = solo ? pawn : job.targetA.Thing as Pawn;
            if (partner == null)
            {
                if (quickie || joinInBed)
                    __result = null;
                return;
            }

            try
            {
                // RJW SelectSextype presently returns SexProps even when
                // ChooseInteraction found zero possibilities. Its subsequent
                // SexInteraction(null) constructor throws every time the job starts.
                // Test the *real RJW* suitability constraints at job assignment
                // rather than creating a fictitious interaction as a fallback.
                object props = Activator.CreateInstance(sexPropsType, pawn, partner);
                object available = findInteractions.Invoke(null, new[] { props });
                IEnumerable matches = available as IEnumerable;
                if (matches == null)
                    return; // Unknown upstream API: preserve original behavior.

                IEnumerator iterator = matches.GetEnumerator();
                bool any = iterator.MoveNext();
                (iterator as IDisposable)?.Dispose();
                if (any)
                    return;

                __result = null;
                if (!missingInteractionLogged)
                {
                    missingInteractionLogged = true;
                    Log.Warning("[Z Adaptive] RJW 1.6 generated an " +
                        (solo ? "RJW_Masturbate" : (joinInBed ? "JoinInBed" : "Quickie")) +
                        " job without any permitted SexInteraction. The invalid job " +
                        "was declined; all supported interactions remain available.");
                }
            }
            catch (Exception ex)
            {
                // An obsolete RJW API must not disable all activities. Fail open,
                // with one diagnostic to help identify changed future builds.
                if (!probeFailureLogged)
                {
                    probeFailureLogged = true;
                    Log.Warning("[Z Adaptive] RJW suitability probe failed open; " +
                        "upstream activity selection was left intact: " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }
}
