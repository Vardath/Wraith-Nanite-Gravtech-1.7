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
    /// Detect invalid previously generated/saved RJW jobs at reservation time.
    /// Does not invent interaction definitions or rewrite active save data.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RJWExistingInvalidJobsGuard
    {
        private static bool warned;
        private static bool probeFailed;

        static RJWExistingInvalidJobsGuard()
        {
            try
            {
                Type propsType = AccessTools.TypeByName("rjw.SexProps");
                Type finderType = AccessTools.TypeByName(
                    "rjw.Modules.Interactions.SexInteractionFinder");
                MethodInfo finder = finderType == null || propsType == null ? null :
                    AccessTools.Method(finderType, "FindInteractions",
                        new[] { propsType });
                if (finder == null)
                    return;

                Harmony harmony = new Harmony(
                    "vardath.zadaptive.rjw.savedinvalidjobs");
                foreach (string typeName in new[] {
                    "rjw.JobDriver_JoinInBed",
                    "rjw.JobDriver_SexQuick",
                    "rjw.JobDriver_Masturbate"
                })
                {
                    Type driverType = AccessTools.TypeByName(typeName);
                    MethodInfo target = driverType == null ? null :
                        AccessTools.DeclaredMethod(driverType,
                            "TryMakePreToilReservations",
                            new[] { typeof(bool) });
                    if (target == null) continue;
                    MethodInfo prefix = AccessTools.Method(
                        typeof(RJWExistingInvalidJobsGuard), nameof(Guard));
                    // Harmony injects this constant source type alongside the
                    // driver's standard base members. All reflection is optional.
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] RJW saved-job reservation guard could " +
                    "not be installed; original behaviour retained: " + ex);
            }
        }

        private static bool Guard(JobDriver __instance, ref bool __result)
        {
            if (__instance?.pawn == null || __instance.job?.def == null)
                return true;

            string jobName = __instance.job.def.defName;
            bool self = jobName == "RJW_Masturbate";
            if (!self && jobName != "Quickie" && jobName != "JoinInBed")
                return true;

            Pawn actor = __instance.pawn;
            Pawn partner = self ? actor : __instance.job.targetA.Thing as Pawn;
            if (partner == null || partner.Dead || !partner.Spawned)
            {
                // Self-directed jobs do not require another spawned pawn.
                if (!self)
                {
                    __result = false;
                    return false;
                }
            }

            try
            {
                Type propsType = AccessTools.TypeByName("rjw.SexProps");
                Type finderType = AccessTools.TypeByName(
                    "rjw.Modules.Interactions.SexInteractionFinder");
                MethodInfo finder = finderType == null || propsType == null ? null :
                    AccessTools.Method(finderType, "FindInteractions",
                        new[] { propsType });
                if (finder == null)
                    return true;

                object props = Activator.CreateInstance(propsType, actor, partner);
                IEnumerable found = finder.Invoke(null, new[] { props }) as IEnumerable;
                if (found == null) return true;
                IEnumerator e = found.GetEnumerator();
                bool valid = e.MoveNext();
                (e as IDisposable)?.Dispose();
                if (valid) return true;

                __result = false;
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[Z Adaptive] An existing RJW intimate-job " +
                        "reservation was rejected because its pawn pair has " +
                        "no compatible SexInteraction. Original pawn, " +
                        "health and equipment were not altered.");
                }
                return false;
            }
            catch (Exception ex)
            {
                if (!probeFailed)
                {
                    probeFailed = true;
                    Log.Warning("[Z Adaptive] Existing RJW job probe failed " +
                        "open; original job reservation retained: " + ex);
                }
                return true;
            }
        }
    }
}
