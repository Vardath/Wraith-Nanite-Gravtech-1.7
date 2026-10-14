using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    public static class ReplicatorRepairerUtility
    {
        public const string RepairerDefName = "WNG_ReplicatorRepairer";
        public const string RepairJobDefName = "WNG_ReplicatorRepairAlly";

        public static bool IsRepairer(Pawn pawn)
        {
            return pawn?.def?.defName == RepairerDefName;
        }

        public static bool SameCurrentRepairDomain(Pawn a, Pawn b)
        {
            return ReplicatorDomainUtility.SameDomain(a, b);
        }

        public static float RepairableSeverity(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
                return 0f;

            float total = 0f;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff_Injury injury = hediffs[i] as Hediff_Injury;
                if (injury != null && !injury.IsPermanent() && injury.Severity > 0f)
                    total += injury.Severity;
            }
            return total;
        }

        public static bool IsSuppressed(Pawn repairer)
        {
            if (repairer == null || repairer.Dead || !repairer.Spawned || repairer.Map == null)
                return true;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(repairer))
                return true;
            return ReplicatorContainmentUtility.IsContained(repairer.Map, repairer.Position);
        }

        public static bool IsValidRepairTarget(Pawn repairer, Pawn target, bool requireReachAndReservation)
        {
            if (repairer == null || target == null || target == repairer || target.Dead || !target.Spawned ||
                repairer.Map == null || target.Map != repairer.Map || !ReplicatorAssimilationUtility.IsBlockReplicator(target) ||
                !SameCurrentRepairDomain(repairer, target) || RepairableSeverity(target) <= 0f)
            {
                return false;
            }

            // Powered containment is a countermeasure to active specialist support as well as to the
            // hostile source. A Repairer outside the field cannot project repairs through it.
            if (ReplicatorContainmentUtility.IsContained(repairer.Map, target.Position))
                return false;

            if (!requireReachAndReservation)
                return true;

            return repairer.CanReach(target, PathEndMode.Touch, Danger.Deadly) &&
                   repairer.CanReserve(target, 1, -1, null, false);
        }
    }

    /// <summary>
    /// Hostile autonomous formation of Repairer side-forms. The values are provisional archaeology-
    /// backed first-build tuning, not permanent design law: old working evidence used a twelve-body
    /// threshold, 1,800-tick checks, a cap that grows slowly with swarm size, and Power knowledge.
    /// Formation consumes existing Hunter/Bulwark mass and never creates a free body.
    /// </summary>
    public sealed class MapComponent_ReplicatorRepairerFormation : MapComponent
    {
        public const int FormationThreshold = 12;
        public const int FormationCheckIntervalTicks = 1800;
        public const int AdditionalRepairerPerPopulation = 30;
        public const int MaxRepairers = 3;

        private int nextFormationCheckTick;

        public MapComponent_ReplicatorRepairerFormation(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextFormationCheckTick)
                return;

            nextFormationCheckTick = SafeFutureTick(now, FormationCheckIntervalTicks);
            TryFormRepairer();
        }

        private void TryFormRepairer()
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            Dictionary<string, List<Pawn>> blocksByDomain = new Dictionary<string, List<Pawn>>();

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Faction == null || pawn.Faction == Faction.OfPlayer ||
                    !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                {
                    continue;
                }

                if (Faction.OfPlayer != null && !pawn.Faction.HostileTo(Faction.OfPlayer))
                    continue;

                string domainId = ReplicatorDomainUtility.DomainId(pawn);
                if (string.IsNullOrEmpty(domainId))
                    continue;

                if (!blocksByDomain.TryGetValue(domainId, out List<Pawn> list))
                {
                    list = new List<Pawn>();
                    blocksByDomain.Add(domainId, list);
                }
                list.Add(pawn);
            }

            foreach (KeyValuePair<string, List<Pawn>> pair in blocksByDomain)
            {
                List<Pawn> blocks = pair.Value;
                if (blocks.Count < FormationThreshold)
                    continue;

                int existingRepairers = 0;
                for (int i = 0; i < blocks.Count; i++)
                    if (ReplicatorRepairerUtility.IsRepairer(blocks[i]))
                        existingRepairers++;

                int roleCap = Math.Min(MaxRepairers, 1 + blocks.Count / Math.Max(1, AdditionalRepairerPerPopulation));
                if (existingRepairers >= roleCap)
                    continue;

                Pawn source = FindSource(blocks, "WNG_ReplicatorHunter") ?? FindSource(blocks, "WNG_ReplicatorBulwark");
                if (source != null && TryConvertToRepairer(source))
                    return; // one specialist formation per bounded map check
            }
        }

        private Pawn FindSource(List<Pawn> blocks, string defName)
        {
            Pawn source = null;
            for (int i = 0; i < blocks.Count; i++)
            {
                Pawn candidate = blocks[i];
                if (candidate?.def?.defName != defName || candidate.Downed ||
                    ReplicatorInterferenceUtility.IsEmpDisrupted(candidate) ||
                    TemporaryAsuranIntrusionUtility.IsCommandSuppressed(candidate) ||
                    ReplicatorContainmentUtility.IsContained(map, candidate.Position) ||
                    candidate.TryGetComp<CompReplicatorAdaptation>()?.Has(ReplicatorAdaptationFlags.Power) != true)
                {
                    continue;
                }

                if (source == null || candidate.thingIDNumber < source.thingIDNumber)
                    source = candidate;
            }
            return source;
        }

        private bool TryConvertToRepairer(Pawn source)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(ReplicatorRepairerUtility.RepairerDefName);
            if (source == null || source.Dead || !source.Spawned || source.Map != map || source.Faction == null || kind == null)
                return false;

            Pawn repairer = null;
            try
            {
                repairer = PawnGenerator.GeneratePawn(kind, source.Faction);
                ReplicatorDomainUtility.CopyDomain(source, repairer);
                TemporaryAsuranIntrusionUtility.CopyState(source, repairer);
                ReplicatorSovereignControlUtility.CopyState(source, repairer);
                repairer.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(source.TryGetComp<CompReplicatorAdaptation>());
                repairer.TryGetComp<CompReplicatorMaterialProfile>()?.InheritFrom(source.TryGetComp<CompReplicatorMaterialProfile>());

                if (!GenPlace.TryPlaceThing(
                        repairer,
                        source.Position,
                        map,
                        ThingPlaceMode.Near,
                        null,
                        cell => !ReplicatorContainmentUtility.IsContained(map, cell)))
                {
                    if (!repairer.Destroyed)
                        repairer.Destroy(DestroyMode.Vanish);
                    return false;
                }

                source.Destroy(DestroyMode.Vanish);
                if (!source.Destroyed)
                {
                    if (!repairer.Destroyed)
                        repairer.Destroy(DestroyMode.Vanish);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Replicator Repairer formation failed: " + ex);
                if (repairer != null && !repairer.Destroyed)
                    repairer.Destroy(DestroyMode.Vanish);
                return false;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextFormationCheckTick, "wngReplicatorRepairerNextFormationCheck", 0);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }

    public sealed class JobGiver_ReplicatorRepair : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ReplicatorRepairerUtility.IsRepairer(pawn) || pawn.Faction == Faction.OfPlayer ||
                ReplicatorRepairerUtility.IsSuppressed(pawn))
            {
                return null;
            }

            Pawn best = null;
            float bestSeverity = 0f;
            int bestDistance = int.MaxValue;
            IReadOnlyList<Pawn> spawned = pawn.Map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn candidate = spawned[i];
                if (!ReplicatorRepairerUtility.IsValidRepairTarget(pawn, candidate, requireReachAndReservation: true))
                    continue;

                float severity = ReplicatorRepairerUtility.RepairableSeverity(candidate);
                int distance = pawn.Position.DistanceToSquared(candidate.Position);
                if (best == null || severity > bestSeverity + 0.001f ||
                    (Math.Abs(severity - bestSeverity) <= 0.001f &&
                     (distance < bestDistance || (distance == bestDistance && candidate.thingIDNumber < best.thingIDNumber))))
                {
                    best = candidate;
                    bestSeverity = severity;
                    bestDistance = distance;
                }
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(ReplicatorRepairerUtility.RepairJobDefName);
            return best == null || jobDef == null ? null : JobMaker.MakeJob(jobDef, best);
        }
    }

    public sealed class JobDriver_ReplicatorRepairAlly : JobDriver
    {
        public const int RepairWorkTicks = 180;
        public const float RepairAmount = 10f;

        private Pawn TargetPawn => job.targetA.Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return TargetPawn != null && pawn.Reserve(TargetPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => ReplicatorRepairerUtility.IsSuppressed(pawn));
            this.FailOn(() => !ReplicatorRepairerUtility.IsValidRepairTarget(pawn, TargetPawn, requireReachAndReservation: false));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil work = ToilMaker.MakeToil("WNG_ReplicatorRepairWork");
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = RepairWorkTicks;
            work.WithProgressBarToilDelay(TargetIndex.A);
            yield return work;

            Toil finish = ToilMaker.MakeToil("WNG_ReplicatorRepairFinish");
            finish.initAction = () =>
            {
                Pawn target = TargetPawn;
                if (!ReplicatorRepairerUtility.IsValidRepairTarget(pawn, target, requireReachAndReservation: false))
                    return;

                float remaining = RepairAmount;
                List<Hediff_Injury> injuries = target.health.hediffSet.hediffs
                    .OfType<Hediff_Injury>()
                    .Where(injury => injury != null && !injury.IsPermanent() && injury.Severity > 0f)
                    .OrderByDescending(injury => injury.Severity)
                    .ToList();

                for (int i = 0; i < injuries.Count && remaining > 0f; i++)
                {
                    Hediff_Injury injury = injuries[i];
                    float amount = Math.Min(remaining, injury.Severity);
                    injury.Heal(amount);
                    remaining -= amount;
                }
            };
            yield return finish;
        }
    }
}
