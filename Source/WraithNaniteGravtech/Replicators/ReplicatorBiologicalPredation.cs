using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Starvation fallback for autonomous hostile block Replicators.
    ///
    /// The physical map remains authoritative first food. Living prey is hard-locked until the
    /// map-scoped consumption tracker confirms that at least 95% of the initially consumable
    /// environment has been stripped. Player-owned or command-suppressed bodies never enter this branch.
    /// </summary>
    public static class ReplicatorBiologicalPredationUtility
    {
        private const string DroneKindDefName = "WNG_ReplicatorDrone";

        public static bool IsSyntheticNanitePawn(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   AsuranCollectiveUtility.IsNaniteSynthetic(pawn);
        }

        public static bool IsEnvironmentallyStarved(Pawn hunter)
        {
            if (!ReplicatorAssimilationUtility.CanAutonomouslyAssimilate(hunter) ||
                hunter?.Map == null ||
                hunter.Faction == null ||
                Faction.OfPlayer == null ||
                !hunter.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            MapComponent_ReplicatorConsumption consumption =
                hunter.Map.GetComponent<MapComponent_ReplicatorConsumption>();
            return consumption?.BiologicalPredationUnlocked == true;
        }

        public static bool CanStarvationPredate(Pawn hunter)
        {
            return IsEnvironmentallyStarved(hunter);
        }

        public static bool IsConvertiblePrey(
            Pawn prey,
            Pawn hunter,
            bool requireDowned)
        {
            if (prey == null ||
                hunter == null ||
                prey == hunter ||
                prey.Dead ||
                prey.Destroyed ||
                !prey.Spawned ||
                prey.Map != hunter.Map)
            {
                return false;
            }

            if (prey.Faction != null && prey.Faction == hunter.Faction)
                return false;

            if (ReplicatorAssimilationUtility.IsBlockReplicator(prey))
                return false;

            if (requireDowned && !prey.Downed)
                return false;
            if (!requireDowned && prey.Downed)
                return false;

            bool biological =
                prey.RaceProps?.IsFlesh == true &&
                prey.RaceProps?.IsMechanoid != true;
            bool synthetic = IsSyntheticNanitePawn(prey);
            if (!biological && !synthetic)
                return false;

            return !ReplicatorContainmentUtility.BlocksAssimilation(
                hunter,
                prey);
        }

        public static Pawn FindClosestPrey(
            Pawn hunter,
            bool requireDowned)
        {
            if (!CanStarvationPredate(hunter))
                return null;

            return hunter.Map.mapPawns.AllPawnsSpawned
                .Where(prey =>
                    IsConvertiblePrey(
                        prey,
                        hunter,
                        requireDowned) &&
                    hunter.CanReach(
                        prey,
                        PathEndMode.Touch,
                        Danger.Deadly) &&
                    hunter.CanReserve(
                        prey,
                        1,
                        -1,
                        null,
                        false))
                .OrderBy(prey => PreyScore(hunter, prey))
                .ThenBy(prey => prey.thingIDNumber)
                .FirstOrDefault();
        }

        public static int OffspringYield(Pawn prey)
        {
            if (prey == null)
                return 1;

            if (IsSyntheticNanitePawn(prey) ||
                prey.RaceProps?.Humanlike == true)
            {
                return 2;
            }

            return prey.BodySize >= 0.60f ? 2 : 1;
        }

        private static float PreyScore(Pawn hunter, Pawn prey)
        {
            float score =
                hunter.Position.DistanceToSquared(prey.Position);

            if (IsSyntheticNanitePawn(prey))
                score -= 36f;
            else if (prey.RaceProps?.Humanlike == true)
                score -= 18f;

            return score;
        }

        private static int AvailableOffspringSlots(
            Pawn parent,
            int requestedChildren)
        {
            ReplicatorBlockExtension ext =
                ReplicatorAssimilationUtility.ExtensionFor(parent);
            if (parent?.Map == null ||
                parent.Faction == null ||
                ext == null)
            {
                return 0;
            }

            requestedChildren =
                Math.Max(1, Math.Min(2, requestedChildren));

            return ReplicatorAssimilationUtility.AvailablePopulationSlots(
                parent,
                requestedChildren);
        }

        public static bool TryCommitBiologicalAssimilation(
            Pawn parent,
            Pawn prey)
        {
            if (!CanStarvationPredate(parent) ||
                !IsConvertiblePrey(
                    prey,
                    parent,
                    requireDowned: true))
            {
                return false;
            }

            int requestedChildren = OffspringYield(prey);
            int toSpawn =
                AvailableOffspringSlots(
                    parent,
                    requestedChildren);

            PawnKindDef droneKind =
                DefDatabase<PawnKindDef>.GetNamedSilentFail(
                    DroneKindDefName);
            if ((toSpawn > 0 && droneKind == null) ||
                parent.Map == null ||
                parent.Faction == null)
            {
                return false;
            }

            Map map = parent.Map;
            IntVec3 origin = prey.Position;
            List<Pawn> staged = new List<Pawn>(toSpawn);

            try
            {
                for (int i = 0; i < toSpawn; i++)
                {
                    if (AvailableOffspringSlots(parent, 1) < 1)
                        break;

                    Pawn child =
                        PawnGenerator.GeneratePawn(
                            droneKind,
                            parent.Faction);
                    if (child == null)
                    {
                        Rollback(staged);
                        return false;
                    }

                    ReplicatorDomainUtility.CopyDomain(
                        parent,
                        child);
                    TemporaryAsuranIntrusionUtility.CopyState(
                        parent,
                        child);
                    ReplicatorSovereignControlUtility.CopyState(
                        parent,
                        child);
                    child.TryGetComp<CompReplicatorAdaptation>()
                        ?.InheritFrom(
                            parent.TryGetComp<CompReplicatorAdaptation>());

                    if (!GenPlace.TryPlaceThing(
                            child,
                            origin,
                            map,
                            ThingPlaceMode.Near))
                    {
                        if (!child.Destroyed)
                            child.Destroy(DestroyMode.Vanish);
                        Rollback(staged);
                        return false;
                    }

                    staged.Add(child);
                }

                // Matter remains first priority even if the map changed while the conversion job
                // was running. A newly reachable environmental target aborts before prey consumption.
                if (!IsEnvironmentallyStarved(parent) ||
                    !IsConvertiblePrey(
                        prey,
                        parent,
                        requireDowned: true))
                {
                    Rollback(staged);
                    return false;
                }

                bool targetCommitted = false;
                Corpse corpse = null;
                try
                {
                    prey.Kill(null);
                    corpse = prey.Corpse;
                    targetCommitted =
                        prey.Dead || prey.Destroyed;
                }
                catch (Exception ex)
                {
                    corpse = prey.Corpse;
                    targetCommitted =
                        prey.Dead || prey.Destroyed;
                    if (!targetCommitted)
                    {
                        Log.Warning(
                            "[WNG] Biological Replicator assimilation could not run normal pawn death for " +
                            prey.LabelShort + ": " +
                            ex.GetType().Name + ": " +
                            ex.Message);
                    }
                    else
                    {
                        Log.Warning(
                            "[WNG] Biological Replicator assimilation death bookkeeping threw after commit for " +
                            prey.LabelShort + ": " +
                            ex.GetType().Name + ": " +
                            ex.Message);
                    }
                }

                if (!targetCommitted &&
                    !prey.Destroyed)
                {
                    try
                    {
                        prey.Destroy(DestroyMode.Vanish);
                        targetCommitted = prey.Destroyed;
                    }
                    catch (Exception ex)
                    {
                        targetCommitted = prey.Destroyed;
                        Log.Warning(
                            "[WNG] Biological Replicator fallback body dismantling failed" +
                            (targetCommitted
                                ? " after commit: "
                                : " before commit: ") +
                            ex.Message);
                    }
                }

                if (!targetCommitted)
                {
                    Rollback(staged);
                    return false;
                }

                int deferredOffspring = Math.Max(0, requestedChildren - staged.Count);
                if (deferredOffspring > 0)
                {
                    map.GetComponent<MapComponent_ReplicatorConsumption>()
                        ?.AddStoredCellMatter(deferredOffspring * MapComponent_ReplicatorConsumption.CellMatterPerDrone);
                }

                // Once the exact prey is irreversibly dead/destroyed the offspring are committed.
                // Corpse removal is best-effort cleanup of the consumed body; a presentation/cleanup
                // exception must never delete already-committed exact Replicators.
                if (corpse != null &&
                    !corpse.Destroyed)
                {
                    try
                    {
                        corpse.Destroy(DestroyMode.Vanish);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[WNG] Biological Replicator assimilation committed but corpse cleanup failed: " +
                            ex);
                    }
                }

                try
                {
                    SoundDef sound =
                        DefDatabase<SoundDef>.GetNamedSilentFail(
                            "WNG_ReplicatorAssimilate");
                    sound?.PlayOneShot(
                        new TargetInfo(
                            origin,
                            map));
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Biological Replicator assimilation committed but sound failed: " +
                        ex.Message);
                }

                return true;
            }
            catch (Exception ex)
            {
                Rollback(staged);
                Log.Error(
                    "[WNG] Biological Replicator assimilation failed before prey commit; staged offspring rolled back: " +
                    ex);
                return false;
            }
        }

        private static void Rollback(List<Pawn> pawns)
        {
            if (pawns == null)
                return;

            foreach (Pawn pawn in pawns)
            {
                if (pawn != null &&
                    !pawn.Destroyed)
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }
            }

            pawns.Clear();
        }
    }

    public sealed class JobGiver_ReplicatorAssimilateBiological :
        ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ReplicatorBiologicalPredationUtility
                    .CanStarvationPredate(pawn))
            {
                return null;
            }

            Pawn target =
                ReplicatorBiologicalPredationUtility
                    .FindClosestPrey(
                        pawn,
                        requireDowned: true);
            if (target == null)
                return null;

            JobDef def =
                DefDatabase<JobDef>.GetNamedSilentFail(
                    "WNG_ReplicatorAssimilateBiological");
            return def == null
                ? null
                : JobMaker.MakeJob(def, target);
        }
    }

    public sealed class JobGiver_ReplicatorBiologicalHunt :
        ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ReplicatorBiologicalPredationUtility
                    .CanStarvationPredate(pawn))
            {
                return null;
            }

            Pawn prey =
                ReplicatorBiologicalPredationUtility
                    .FindClosestPrey(
                        pawn,
                        requireDowned: false);
            if (prey == null)
                return null;

            Job job =
                JobMaker.MakeJob(
                    JobDefOf.AttackMelee,
                    prey);
            job.expiryInterval = 1800;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    public sealed class JobDriver_ReplicatorAssimilateBiological :
        JobDriver
    {
        private Pawn TargetPawn => job.targetA.Pawn;

        public override bool TryMakePreToilReservations(
            bool errorOnFailed)
        {
            return TargetPawn != null &&
                   pawn.Reserve(
                       TargetPawn,
                       job,
                       1,
                       -1,
                       null,
                       errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() =>
                !ReplicatorBiologicalPredationUtility
                    .IsConvertiblePrey(
                        TargetPawn,
                        pawn,
                        requireDowned: true));

            this.FailOn(() =>
                !ReplicatorAssimilationUtility
                    .CanAutonomouslyAssimilate(pawn));

            yield return Toils_Goto.GotoThing(
                TargetIndex.A,
                PathEndMode.Touch);

            ReplicatorBlockExtension ext =
                ReplicatorAssimilationUtility.ExtensionFor(pawn);
            int baseDuration =
                Math.Max(
                    90,
                    ext?.assimilationTicks ?? 300);
            float adaptationFactor =
                pawn.TryGetComp<CompReplicatorAdaptationEffects>()
                    ?.AssimilationTimeFactor ?? 1f;
            float coordinationFactor =
                pawn.Map
                    ?.GetComponent<MapComponent_ReplicatorCoordination>()
                    ?.AssimilationFactorFor(pawn) ?? 1f;
            int duration =
                Math.Max(
                    240,
                    (int)Math.Round(
                        baseDuration *
                        1.45f *
                        adaptationFactor *
                        coordinationFactor));

            Toil work =
                ToilMaker.MakeToil(
                    "WNG_ReplicatorAssimilateBiological");
            work.defaultCompleteMode =
                ToilCompleteMode.Delay;
            work.defaultDuration = duration;
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.FailOnCannotTouch(
                TargetIndex.A,
                PathEndMode.Touch);
            yield return work;

            Toil finish =
                ToilMaker.MakeToil(
                    "WNG_ReplicatorAssimilateBiologicalFinish");
            finish.defaultCompleteMode =
                ToilCompleteMode.Instant;
            finish.initAction = delegate
            {
                Pawn target = TargetPawn;
                if (target != null)
                {
                    ReplicatorBiologicalPredationUtility
                        .TryCommitBiologicalAssimilation(
                            pawn,
                            target);
                }
            };
            yield return finish;
        }
    }
}
