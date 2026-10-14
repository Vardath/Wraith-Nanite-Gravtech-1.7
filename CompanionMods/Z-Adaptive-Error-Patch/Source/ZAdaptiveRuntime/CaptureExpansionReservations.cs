using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// Source-informed Capture Expansion 1.6 compatibility.
    ///
    /// The workshop source's JobDriver_RestrainPrisonerToBed unconditionally
    /// invokes Prisoner.ClearAllReservations() before claiming a prisoner.
    /// When several wardens repeatedly acquire this task it can invalidate
    /// other jobs' reservations, causing the observed CE_RestrainPrisonerToBed
    /// ten-jobs-in-one-tick storm. Its WorkGiver does not check whether the
    /// prisoner itself is reservable before producing a job.
    ///
    /// Preserve the vanilla reservation contract: never clear another job's
    /// claim, and do not offer a task that cannot reserve its own prisoner.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CaptureExpansionReservations
    {
        private static bool reportedWorkGiver;
        private static bool reportedReservationConflict;
        private static bool reportedFallback;
        private static int installedGuards;

        static CaptureExpansionReservations()
        {
            Type giverType = AccessTools.TypeByName(
                "CaptureExpansion.WorkGiver_Warden_RestrainPrisonerToBed");
            Type driverType = AccessTools.TypeByName(
                "CaptureExpansion.JobDriver_RestrainPrisonerToBed");
            if (giverType == null || driverType == null)
            {
                Log.Warning("[Z Adaptive] Capture Expansion restraint guard not installed: " +
                    "expected workgiver/driver type unavailable. Check the installed " +
                    "Capture Expansion assembly and version.");
                return;
            }

            Harmony harmony = new Harmony("vardath.zadaptive.captureexpansion.reservations");
            TryInstall("restrain-prisoner job selection", () =>
            {
                MethodInfo target = AccessTools.Method(giverType, "JobOnThing",
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                MethodInfo postfix = AccessTools.Method(typeof(CaptureExpansionReservations),
                    nameof(RequirePrisonerReservation));
                if (target != null && postfix != null)
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                    installedGuards++;
                }
                else
                    Log.Warning("[Z Adaptive] Capture Expansion restraint JobOnThing signature unavailable.");
            });

            TryInstall("restrain-prisoner job reservations", () =>
            {
                MethodInfo target = AccessTools.Method(driverType,
                    "TryMakePreToilReservations", new[] { typeof(bool) });
                MethodInfo prefix = AccessTools.Method(typeof(CaptureExpansionReservations),
                    nameof(ReserveWithoutClearingOtherJobs));
                if (target != null && prefix != null)
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                    installedGuards++;
                }
                else
                    Log.Warning("[Z Adaptive] Capture Expansion restraint reservation signature unavailable.");
            });
            Log.Message("[Z Adaptive] Capture Expansion restraint guards installed: " +
                installedGuards + "/2 (job selection / reservation).");
        }

        private static void TryInstall(string name, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Capture Expansion " + name +
                    " guard could not install; upstream behaviour is unchanged: " + ex);
            }
        }

        private static void RequirePrisonerReservation(
            Pawn pawn, Thing t, ref Job __result)
        {
            // The workgiver's enumerated Thing can differ from the job's actual
            // targets. The log establishes CE targetA=prisoner, targetB=sleeping
            // spot; validate those targets, not the enumerated t argument.
            if (__result?.def?.defName != "CE_RestrainPrisonerToBed" || pawn == null)
                return;

            try
            {
                Pawn prisoner = __result.targetA.Thing as Pawn;
                Thing bed = __result.targetB.Thing;
                if (prisoner == null || bed == null || !pawn.Spawned ||
                    !prisoner.Spawned || !bed.Spawned ||
                    pawn.Map != prisoner.Map || pawn.Map != bed.Map ||
                    !pawn.CanReserve(prisoner, 1, -1, null, false) ||
                    !pawn.CanReserve(bed, 1, 0, null, false))
                {
                    // An impossible assignment must not be handed to the job
                    // tracker and then reissued ten times during the same tick.
                    __result = null;
                    if (!reportedWorkGiver)
                    {
                        reportedWorkGiver = true;
                        Log.Warning("[Z Adaptive] Capture Expansion: declined an " +
                            "unreservable prisoner/sleeping-spot restraint job. " +
                            "Other work and valid restraint assignments remain available.");
                    }
                }
            }
            catch (Exception ex)
            {
                WarnFallback(ex);
                // Fail open when optional mod APIs change.
            }
        }

        private static bool ReserveWithoutClearingOtherJobs(
            JobDriver __instance, bool errorOnFailed, ref bool __result)
        {
            try
            {
                Pawn warden = __instance?.pawn;
                Job job = __instance?.job;
                Pawn prisoner = job?.targetA.Thing as Pawn;
                Building_Bed bed = job?.targetB.Thing as Building_Bed;
                if (warden == null || prisoner == null || bed == null ||
                    !warden.Spawned || !prisoner.Spawned || !bed.Spawned ||
                    warden.Map != prisoner.Map || warden.Map != bed.Map)
                {
                    __result = false;
                    return false;
                }

                // Do NOT call Prisoner.ClearAllReservations(): it can destroy
                // unrelated wardens' and prisoner job claims.
                if (!warden.CanReserve(prisoner, 1, -1, null, false) ||
                    !warden.CanReserve(bed, bed.SleepingSlotsCount, 0, null, false))
                {
                    __result = false;
                    if (!reportedReservationConflict)
                    {
                        reportedReservationConflict = true;
                        Log.Message("[Z Adaptive] Capture Expansion: refused a " +
                            "contested restraint reservation instead of clearing " +
                            "another job's reservations.");
                    }
                    return false;
                }

                if (!warden.Reserve(prisoner, job, 1, -1, null, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                // Normal RimWorld job termination releases reservations even
                // when the subsequent bed claim cannot be obtained.
                __result = warden.Reserve(
                    bed, job, bed.SleepingSlotsCount, 0, null, errorOnFailed);
                return false;
            }
            catch (Exception ex)
            {
                WarnFallback(ex);
                // API mismatch: preserve the mod's unmodified reservation code.
                return true;
            }
        }

        private static void WarnFallback(Exception ex)
        {
            if (reportedFallback) return;
            reportedFallback = true;
            Log.Warning("[Z Adaptive] Capture Expansion guard failed open: " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }
}
