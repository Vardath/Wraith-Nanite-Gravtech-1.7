using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// Narrow guards for two observed 2026-10-14 colony job failures.
    /// They do not replace workgivers, think trees, hunting selection or save data.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class JobContinuity20261014
    {
        private static bool waitFaultReported;
        private static bool taranFaultReported;

        static JobContinuity20261014()
        {
            Harmony harmony = new Harmony("vardath.zadaptive.jobcontinuity.20261014");

            // A failure in *optional* auto-attack checking must not abort the
            // pawn's ordinary Wait job, then its error-recovery Wait job.
            // Preserve all successful auto-attack checks and all non-NRE errors.
            TryInstall("idle auto-attack recovery", () =>
            {
                MethodInfo target = AccessTools.Method(typeof(JobDriver_Wait), "CheckForAutoAttack");
                MethodInfo finalizer = AccessTools.Method(typeof(JobContinuity20261014),
                    nameof(IdleAutoAttackFinalizer));
                if (target == null || finalizer == null)
                    throw new MissingMethodException("JobDriver_Wait.CheckForAutoAttack");
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
            });

            // TaranMagicFramework's Job.ExposeData postfix threw during
            // PostLoadInit of ordinary SelfShutdown / HaulToCell jobs, including
            // WNG biomass hauling. Wrap only that optional postfix and only
            // these two observed vanilla job definitions at this save stage.
            // The base game Job.ExposeData and every other mod's patch still run.
            TryInstall("Taran saved-job postload recovery", () =>
            {
                Type owner = AccessTools.TypeByName(
                    "TaranMagicFramework.JobAbility+Job_ExposeData");
                if (owner == null)
                    return; // optional dependency absent
                MethodInfo target = AccessTools.Method(owner, "Postfix");
                MethodInfo finalizer = AccessTools.Method(typeof(JobContinuity20261014),
                    nameof(TaranJobPostLoadFinalizer));
                if (target == null || finalizer == null)
                    throw new MissingMethodException("TaranMagicFramework.JobAbility+Job_ExposeData.Postfix");
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
            });
        }

        private static void TryInstall(string name, Action install)
        {
            try
            {
                install();
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Job continuity guard (" + name +
                    ") could not install; native behaviour retained: " + ex);
            }
        }

        private static Exception IdleAutoAttackFinalizer(Exception __exception)
        {
            if (!(__exception is NullReferenceException))
                return __exception;

            if (!waitFaultReported)
            {
                waitFaultReported = true;
                Log.Warning("[Z Adaptive] A conflicting idle auto-attack hook " +
                    "threw NullReferenceException. Skipping only this optional " +
                    "auto-attack check so the pawn's Wait/error-recovery job can " +
                    "start. Check VEF, Use Your Forking Gun, Milira and MVCF versions.");
            }
            return null;
        }

        private static Exception TaranJobPostLoadFinalizer(
            Exception __exception, Job __0)
        {
            if (!(__exception is NullReferenceException) ||
                Scribe.mode != LoadSaveMode.PostLoadInit)
                return __exception;

            string defName = __0?.def?.defName;
            if (defName != "SelfShutdown" && defName != "HaulToCell")
                return __exception;

            if (!taranFaultReported)
            {
                taranFaultReported = true;
                Log.Warning("[Z Adaptive] TaranMagicFramework failed to restore " +
                    "optional JobAbility metadata on a vanilla " + defName +
                    " job. Retained the base job and suppressed only the failing " +
                    "Taran postfix at PostLoadInit. Taran-specific metadata on " +
                    "that job may not have recovered.");
            }
            return null;
        }
    }
}
