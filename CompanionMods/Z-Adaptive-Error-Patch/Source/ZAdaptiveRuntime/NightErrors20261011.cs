using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZAdaptiveRuntime
{
    // Isolated, optional 2026-10-11 compatibility repairs.
    // These are intentionally not WNG gameplay patches: all targets are third-party or vanilla.
    [StaticConstructorOnStartup]
    public static class NightErrors20261011
    {
        private static bool settlementReported;
        private static bool vehicleReported;
        private static bool magicReported;
        private static bool restraintReported;
        private static readonly Dictionary<Pawn, RestraintAttempt> restraintAttempts =
            new Dictionary<Pawn, RestraintAttempt>();

        private struct RestraintAttempt
        {
            public int Tick;
            public Thing Prisoner;
            public Thing Bed;
        }

        static NightErrors20261011()
        {
            Harmony harmony = new Harmony("vardath.zadaptive.nighterrors.20261011");
            Install("settlement material", () =>
            {
                MethodInfo getter = AccessTools.PropertyGetter(typeof(Settlement), "Material");
                if (getter != null)
                    harmony.Patch(getter, finalizer: new HarmonyMethod(
                        AccessTools.Method(typeof(NightErrors20261011), nameof(SettlementMaterialFinalizer))));
            });
            Install("invalid null GenSpawn", () =>
            {
                // Vehicle Framework dereferences newThing in its spawn prefix.
                // A third-party caller must never send null into this overload.
                MethodInfo target = AccessTools.Method(typeof(GenSpawn), "Spawn",
                    new[] { typeof(Thing), typeof(IntVec3), typeof(Map),
                        typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) });
                if (target != null)
                    harmony.Patch(target, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(NightErrors20261011), nameof(NullSpawnPrefix)))
                        { priority = Priority.First });
            });
            Install("Vehicle Framework non-vehicle spawn", () =>
            {
                Type owner = AccessTools.TypeByName("Vehicles.Patch_Construction");
                MethodInfo target = owner == null ? null : AccessTools.Method(owner, "RegisterThingSpawned");
                if (target != null)
                    harmony.Patch(target, finalizer: new HarmonyMethod(
                        AccessTools.Method(typeof(NightErrors20261011), nameof(VehicleSpawnFinalizer))));
            });
            Install("RimWorld of Magic attraction impact", () =>
            {
                Type owner = AccessTools.TypeByName("TorannMagic.Projectile_Attraction");
                MethodInfo target = owner == null ? null : AccessTools.Method(owner, "Impact",
                    new[] { typeof(Thing), typeof(bool) });
                if (target != null)
                    harmony.Patch(target, finalizer: new HarmonyMethod(
                        AccessTools.Method(typeof(NightErrors20261011), nameof(AttractionImpactFinalizer))));
            });
            Install("Capture Expansion dedicated reservation guards", () =>
            {
                // Explicitly initialize the optional Capture Expansion hook.
                // Some static-constructor discovery passes do not initialize
                // every class in a companion runtime assembly.
                RuntimeHelpers.RunClassConstructor(typeof(CaptureExpansionReservations).TypeHandle);
            });
            Install("CE restraint job retry", () =>
            {
                // RimWorld 1.6 JobGiver_Work derives from ThinkNode, not
                // ThinkNode_JobGiver: its concrete override returns ThinkResult.
                // The previous TryGiveJob patch could never install.
                MethodInfo target = AccessTools.Method(typeof(JobGiver_Work),
                    nameof(JobGiver_Work.TryIssueJobPackage),
                    new[] { typeof(Pawn), typeof(JobIssueParams) });
                MethodInfo postfix = AccessTools.Method(typeof(NightErrors20261011),
                    nameof(RestraintJobPostfix));
                if (target == null || postfix == null)
                {
                    Log.Warning("[Z Adaptive] CE restraint repeat-job throttle could not locate " +
                        "RimWorld 1.6 JobGiver_Work.TryIssueJobPackage(Pawn, JobIssueParams).");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                Log.Message("[Z Adaptive] Installed CE restraint repeat-job throttle on " +
                    "JobGiver_Work.TryIssueJobPackage (RimWorld 1.6 ThinkResult).");
            });
        }

        private static void Install(string name, Action install)
        {
            try { install(); }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Night-errors hook " + name + " was not installed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Log: Settlement.get_Material -> MaterialPool.MatFrom ->
        // ContentFinder.Get(null) -> Dictionary.TryGetValue(null).
        // Preserve normal faction/material rendering; use a visible, non-null fallback
        // only for the specific invalid texture-path exception.
        private static Exception SettlementMaterialFinalizer(
            Exception __exception, ref Material __result)
        {
            if (!(__exception is ArgumentNullException) ||
                !string.Equals(((__exception as ArgumentNullException)?.ParamName), "key",
                    StringComparison.Ordinal))
                return __exception;
            try
            {
                __result = new Material(ShaderDatabase.Transparent) { mainTexture = Texture2D.whiteTexture };
                if (__result == null)
                    return __exception;
                if (!settlementReported)
                {
                    settlementReported = true;
                    Log.Warning("[Z Adaptive] A settlement has a null icon texture path; " +
                        "used a fallback material rather than crashing world rendering. " +
                        "Check Custom Base Framework and the settlement/faction definitions.");
                }
                return null;
            }
            catch { return __exception; }
        }

        // The vehicle-construction prefix accesses newThing.def before vanilla's
        // GenSpawn can reject an invalid null object. Return null for null inputs;
        // never prevent spawning a real thing.
        private static bool NullSpawnPrefix(Thing newThing, ref Thing __result)
        {
            if (newThing != null)
                return true;
            __result = null;
            if (!vehicleReported)
            {
                vehicleReported = true;
                Log.Warning("[Z Adaptive] Rejected a null GenSpawn.Spawn request before " +
                    "Vehicle Framework could dereference it. Check third-party launch/flying-object sources.");
            }
            return false;
        }

        // The Vehicle Framework prefix is designed for vehicle construction but is
        // invoked for ordinary GenSpawn.Spawn calls, including Wizardry's flying objects.
        // If its own prefix throws for a *non-vehicle* thing, resume vanilla GenSpawn.
        // Vehicle spawns and all other exception types remain under the framework.
        private static Exception VehicleSpawnFinalizer(
            Exception __exception, Thing newThing, Map map, ref bool __result)
        {
            if (!(__exception is NullReferenceException) ||
                newThing == null || map == null || map.Disposed)
                return __exception;
            Type vehicle = AccessTools.TypeByName("Vehicles.VehiclePawn");
            if (vehicle == null || vehicle.IsInstanceOfType(newThing))
                return __exception;

            __result = true;
            if (!vehicleReported)
            {
                vehicleReported = true;
                Log.Warning("[Z Adaptive] Vehicle Framework's spawn prefix threw while spawning " +
                    "a non-vehicle (" + newThing.def?.defName + "); continuing vanilla spawn. " +
                    "Vehicle spawning is untouched.");
            }
            return null;
        }

        // An incompatible target from Attraction's launched-object conversion throws
        // InvalidCastException. End the already-impacting projectile to stop repeating
        // the same broken cast every tick; do not touch normal attraction impacts.
        private static Exception AttractionImpactFinalizer(
            Exception __exception, Thing __instance)
        {
            if (!(__exception is InvalidCastException))
                return __exception;
            try
            {
                if (__instance != null && __instance.Spawned && !__instance.Destroyed)
                    __instance.Destroy(DestroyMode.Vanish);
            }
            catch { return __exception; }
            if (!magicReported)
            {
                magicReported = true;
                Log.Warning("[Z Adaptive] Removed a broken RimWorld of Magic attraction " +
                    "projectile after a target conversion InvalidCastException. " +
                    "Its invalid impact is cancelled; normal spell projectiles are unaffected.");
            }
            return null;
        }

        // The reported CE_RestrainPrisonerToBed jobs repeatedly target the SAME prisoner
        // and sleeping spot for several pawns. Throttle ONLY identical failed assignments
        // per worker/target pair, leaving distinct prisoners/beds and all other jobs alone.
        // The first attempt always proceeds; another is allowed after three seconds.
        private static void RestraintJobPostfix(Pawn pawn, ref ThinkResult __result)
        {
            Job job = __result.Job;
            if (pawn == null || job == null || job.def == null ||
                job.def.defName != "CE_RestrainPrisonerToBed")
                return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            Thing prisoner = job.targetA.Thing;
            Thing bed = job.targetB.Thing;
            RestraintAttempt previous;
            if (restraintAttempts.TryGetValue(pawn, out previous) &&
                previous.Prisoner == prisoner && previous.Bed == bed &&
                tick >= previous.Tick && tick - previous.Tick < 180)
            {
                __result = ThinkResult.NoJob;
                if (!restraintReported)
                {
                    restraintReported = true;
                    Log.Warning("[Z Adaptive] Suppressed recursive CE_RestrainPrisonerToBed " +
                        "assignment for the same prisoner/bed; retry allowed after 180 ticks.");
                }
                return;
            }
            if (restraintAttempts.Count > 1024)
                restraintAttempts.Clear();
            restraintAttempts[pawn] = new RestraintAttempt
            {
                Tick = tick,
                Prisoner = prisoner,
                Bed = bed
            };
        }
    }
}
