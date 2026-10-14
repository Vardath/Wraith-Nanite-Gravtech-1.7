using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ChildsToyOverseerSubject : CompProperties_OverseerSubject
    {
        public int uncontrolledTicksToFeral = 60000;
        public int replicationWorkTicks = 300;
        public int replicationOffspringCount = 2;

        public CompProperties_ChildsToyOverseerSubject()
        {
            compClass = typeof(CompChildsToyOverseerSubject);
        }
    }

    /// <summary>
    /// Child's Toy is a real colony mech while controlled by a vanilla Mechanitor. It deliberately
    /// replaces vanilla's random "same mech changes faction" feral roll with WNG's exact contract:
    /// one continuous configured delay without effective bandwidth/control converts the Toy into a real hostile
    /// Replicator Drone. The vanilla Overseer relation/state remains authoritative for control.
    /// </summary>
    public sealed class CompChildsToyOverseerSubject : CompOverseerSubject
    {
        private const string DroneKindDefName = "WNG_ReplicatorDrone";
        private const string SwarmFactionDefName = "WNG_ReplicatorSwarm";
        private const string ReplicateJobDefName = "WNG_ChildsToyReplicate";

        private int uncontrolledTicks;

        private CompProperties_ChildsToyOverseerSubject ToyProps =>
            (CompProperties_ChildsToyOverseerSubject)props;

        public int UncontrolledTicks => uncontrolledTicks;
        public int FeralThreshold => WNGMod.Settings != null
            ? WNGSettingsUtility.ChildsToyFeralDelayTicks
            : Math.Max(1, ToyProps.uncontrolledTicksToFeral);

        public override void CompTick()
        {
            // Intentionally do NOT call CompOverseerSubject.CompTick(). Vanilla's base implementation
            // performs a random MTB feral conversion that keeps the same mech race. WNG instead owns
            // the exact settings-driven Toy -> Replicator Drone conversion below while retaining native State.
            if (Parent == null || Parent.Destroyed || Parent.Dead)
                return;

            if (State == OverseerSubjectState.Overseen)
            {
                // "Left uncontrolled for a day" is continuous, not cumulative. Regaining effective
                // Mechanitor control resets the danger window.
                uncontrolledTicks = 0;
                return;
            }

            if (!Parent.Spawned || Parent.Map == null || Parent.Faction != Faction.OfPlayer)
                return;

            if (uncontrolledTicks < int.MaxValue)
                uncontrolledTicks++;

            if (uncontrolledTicks >= FeralThreshold)
                TryConvertToHostileDrone(false);
        }

        public bool TryConvertToHostileDrone(bool force)
        {
            Pawn toy = Parent;
            Map map = toy?.Map;
            if (toy == null || map == null || toy.Destroyed || toy.Dead || !toy.Spawned ||
                toy.Faction != Faction.OfPlayer || (!force && State == OverseerSubjectState.Overseen))
                return false;

            PawnKindDef droneKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(DroneKindDefName);
            FactionDef swarmDef = DefDatabase<FactionDef>.GetNamedSilentFail(SwarmFactionDefName);
            Faction swarmFaction = swarmDef == null ? null : Find.FactionManager?.FirstFactionOfDef(swarmDef);
            if (droneKind == null || swarmFaction == null)
                return false;

            Pawn drone = null;
            try
            {
                drone = PawnGenerator.GeneratePawn(droneKind, swarmFaction);
                drone.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(toy.TryGetComp<CompReplicatorAdaptation>());

                if (!GenPlace.TryPlaceThing(drone, toy.Position, map, ThingPlaceMode.Near))
                {
                    if (!drone.Destroyed)
                        drone.Destroy(DestroyMode.Vanish);
                    return false;
                }

                Pawn oldOverseer = toy.GetOverseer();
                try
                {
                    toy.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    if (!toy.Destroyed)
                    {
                        if (!drone.Destroyed)
                            drone.Destroy(DestroyMode.Vanish);
                        Log.Error("[WNG] Child's Toy feral conversion failed before source consumption: " + ex);
                        return false;
                    }

                    Log.Error("[WNG] Child's Toy reported an exception after feral conversion commit; keeping the hostile Drone: " + ex);
                }

                // RimWorld normally cleans destroyed-pawn relations itself; this is a bounded cleanup
                // for an overseer still holding the exact old Toy reference after the conversion.
                if (oldOverseer?.mechanitor != null)
                {
                    oldOverseer.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, toy);
                    oldOverseer.mechanitor.Notify_BandwidthChanged();
                }
                return true;
            }
            catch (Exception ex)
            {
                if (drone != null && !drone.Destroyed)
                    drone.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Child's Toy feral conversion failed; staged Drone rolled back when possible: " + ex);
                return false;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (Parent?.Faction != Faction.OfPlayer)
                yield break;

            Command_Target command = new Command_Target
            {
                defaultLabel = "Consume and make two toys",
                defaultDesc = "Order this controlled Child's Toy to consume a chosen physical item or artificial building and reorganize it into two new Child's Toys. Both offspring must fit under the same Mechanitor's bandwidth before the target is consumed. Uncontrolled Toys go feral after one day.",
                icon = TexCommand.Attack,
                targetingParams = ChildsToyUtility.ReplicationTargetingParameters(Parent),
                action = target =>
                {
                    Thing thing = target.Thing;
                    Pawn overseer = Parent.GetOverseer();
                    string reason;
                    if (!CanIssueReplication(out reason) || !ChildsToyUtility.IsReplicationTarget(thing, Parent))
                    {
                        if (!reason.NullOrEmpty())
                            Messages.Message(reason, Parent, MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }

                    JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(ReplicateJobDefName);
                    if (jobDef == null || overseer == null)
                        return;

                    Parent.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, thing, overseer), JobTag.Misc);
                }
            };

            string disabledReason;
            if (!CanIssueReplication(out disabledReason))
                command.Disable(disabledReason);

            yield return command;

            Command_Action feral = new Command_Action
            {
                defaultLabel = "Go feral",
                defaultDesc = "Permanently sever colony control and convert this Child's Toy into a hostile Replicator Drone immediately. The released Drone is a genuine feral Replicator and may attack the colony as well as the colony's enemies.",
                icon = TexCommand.Attack,
                action = delegate
                {
                    if (!TryConvertToHostileDrone(true) && Parent != null && !Parent.Destroyed)
                        Messages.Message("The Child's Toy could not complete feral conversion.", Parent, MessageTypeDefOf.RejectInput, false);
                }
            };
            if (Parent == null || Parent.Destroyed || Parent.Dead || !Parent.Spawned)
                feral.Disable("Child's Toy is unavailable.");
            yield return feral;
        }

        private bool CanIssueReplication(out string reason)
        {
            reason = null;
            Pawn toy = Parent;
            if (toy == null || toy.Destroyed || toy.Dead || !toy.Spawned || toy.Faction != Faction.OfPlayer)
            {
                reason = "Child's Toy is unavailable.";
                return false;
            }

            Pawn overseer = toy.GetOverseer();
            if (overseer?.mechanitor == null || State != OverseerSubjectState.Overseen)
            {
                reason = "Requires active Mechanitor control.";
                return false;
            }

            if (!overseer.mechanitor.CanControlMechs.Accepted)
            {
                reason = "The Mechanitor cannot currently control mechs.";
                return false;
            }

            int offspring = Math.Max(1, ToyProps.replicationOffspringCount);
            float bandwidthEach = Math.Max(0f, toy.GetStatValue(StatDefOf.BandwidthCost));
            if ((float)overseer.mechanitor.UsedBandwidth + bandwidthEach * offspring > (float)overseer.mechanitor.TotalBandwidth)
            {
                reason = "Insufficient Mechanitor bandwidth for both new Child's Toys.";
                return false;
            }

            return true;
        }

        public override string CompInspectStringExtra()
        {
            Pawn toy = Parent;
            if (toy?.Faction != Faction.OfPlayer)
                return null;

            Pawn overseer = toy.GetOverseer();
            string control = overseer == null ? "Overseer: none" : "Overseer: " + overseer.LabelShort;
            if (State == OverseerSubjectState.Overseen)
                return control + "\nChild's Toy: controlled";

            int remaining = Math.Max(0, FeralThreshold - uncontrolledTicks);
            return control + "\nChild's Toy uncontrolled: " + uncontrolledTicks.ToStringTicksToPeriod(allowSeconds: true, shortForm: true) +
                   "\nFeral conversion in: " + remaining.ToStringTicksToPeriod(allowSeconds: true, shortForm: true);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref uncontrolledTicks, "wngChildsToyUncontrolledTicks", 0);
        }
    }

    public static class ChildsToyUtility
    {
        private const string ToyKindDefName = "WNG_ChildsToy";
        private const string ReplicatorMatterDefName = "WNG_ReplicatorMatter";
        private const string ReplicatorCoreFragmentDefName = "WNG_ReplicatorCoreFragment";

        public static TargetingParameters ReplicationTargetingParameters(Pawn toy)
        {
            return new TargetingParameters
            {
                canTargetLocations = false,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetBuildings = true,
                canTargetItems = true,
                canTargetAnimals = false,
                canTargetHumans = false,
                canTargetMechs = false,
                canTargetPlants = false,
                canTargetEntities = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = info => IsReplicationTarget(info.Thing, toy)
            };
        }

        public static bool IsReplicationTarget(Thing target, Pawn toy)
        {
            if (toy == null || target == null || target == toy || target.Destroyed || !target.Spawned ||
                target.Map != toy.Map || target is Pawn || target.def == null || !target.def.destroyable)
                return false;

            if (target.def.defName == ReplicatorMatterDefName || target.def.defName == ReplicatorCoreFragmentDefName)
                return false;

            if (target.def.category == ThingCategory.Item)
                return target.stackCount > 0;

            return target.def.category == ThingCategory.Building && target.def.building != null && !target.def.building.isNaturalRock;
        }

        public static bool HasBandwidthForOffspring(Pawn toy, Pawn overseer, int offspringCount)
        {
            if (toy == null || overseer?.mechanitor == null || offspringCount <= 0 ||
                toy.GetOverseer() != overseer || toy.OverseerSubject?.State != OverseerSubjectState.Overseen)
                return false;

            float bandwidthEach = Math.Max(0f, toy.GetStatValue(StatDefOf.BandwidthCost));
            return (float)overseer.mechanitor.UsedBandwidth + bandwidthEach * offspringCount <=
                   (float)overseer.mechanitor.TotalBandwidth;
        }

        public static bool TryCommitReplication(Pawn parent, Pawn overseer, Thing target, int offspringCount)
        {
            if (parent == null || parent.Destroyed || parent.Dead || parent.Map == null || parent.Faction != Faction.OfPlayer ||
                !IsReplicationTarget(target, parent) || !HasBandwidthForOffspring(parent, overseer, offspringCount))
                return false;

            PawnKindDef toyKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(ToyKindDefName);
            if (toyKind == null)
                return false;

            Map map = parent.Map;
            IntVec3 origin = target.Position;
            ReplicatorAdaptationEvidence evidence = ReplicatorAdaptationUtility.EvidenceFrom(target);
            List<Pawn> staged = new List<Pawn>(offspringCount);

            try
            {
                for (int i = 0; i < offspringCount; i++)
                {
                    Pawn child = PawnGenerator.GeneratePawn(toyKind, Faction.OfPlayer);
                    child.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(parent.TryGetComp<CompReplicatorAdaptation>());
                    if (!GenPlace.TryPlaceThing(child, origin, map, ThingPlaceMode.Near))
                    {
                        if (!child.Destroyed)
                            child.Destroy(DestroyMode.Vanish);
                        Rollback(staged, overseer);
                        return false;
                    }
                    staged.Add(child);
                }

                // Single-threaded recheck immediately before binding control. This also catches any
                // state change caused by placement callbacks before the environmental target commits.
                if (!IsReplicationTarget(target, parent) || !HasBandwidthForOffspring(parent, overseer, offspringCount))
                {
                    Rollback(staged, overseer);
                    return false;
                }

                for (int i = 0; i < staged.Count; i++)
                {
                    Pawn child = staged[i];
                    overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, child);
                    overseer.mechanitor.AssignPawnControlGroup(child, MechWorkModeDefOf.Work);
                }
                overseer.mechanitor.Notify_BandwidthChanged();

                for (int i = 0; i < staged.Count; i++)
                {
                    if (!overseer.mechanitor.ControlledPawns.Contains(staged[i]))
                    {
                        Rollback(staged, overseer);
                        return false;
                    }
                }

                // The real target is the commit point. Nothing is consumed until both exact offspring
                // exist, are player-faction, and are genuinely controlled by the same Mechanitor.
                try
                {
                    target.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    if (!target.Destroyed)
                    {
                        Rollback(staged, overseer);
                        Log.Error("[WNG] Child's Toy replication target destruction failed before commit: " + ex);
                        return false;
                    }
                    Log.Error("[WNG] Child's Toy replication target reported an exception after commit; keeping controlled offspring: " + ex);
                }

                if (!target.Destroyed)
                {
                    Rollback(staged, overseer);
                    return false;
                }

                // The successful consumption teaches the parent Toy; the two offspring inherit the
                // resulting knowledge so a later feral conversion cannot erase what the lineage learned.
                parent.TryGetComp<CompReplicatorAdaptation>()?.Learn(evidence.flags);
                for (int i = 0; i < staged.Count; i++)
                    staged[i].TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(parent.TryGetComp<CompReplicatorAdaptation>());

                return true;
            }
            catch (Exception ex)
            {
                Rollback(staged, overseer);
                Log.Error("[WNG] Child's Toy targeted replication failed; staged offspring rolled back when possible: " + ex);
                return false;
            }
        }

        private static void Rollback(List<Pawn> pawns, Pawn overseer)
        {
            if (pawns == null)
                return;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null)
                    continue;

                overseer?.relations?.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, pawn);
                if (!pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
            }
            overseer?.mechanitor?.Notify_BandwidthChanged();
            pawns.Clear();
        }
    }

    public sealed class JobDriver_ChildsToyReplicate : JobDriver
    {
        private Thing Target => job.targetA.Thing;
        private Pawn Overseer => job.targetB.Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Target != null && pawn.Reserve(Target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Target == null || Target.Destroyed || !Target.Spawned ||
                              !ChildsToyUtility.IsReplicationTarget(Target, pawn) ||
                              Overseer == null || pawn.GetOverseer() != Overseer ||
                              pawn.OverseerSubject?.State != OverseerSubjectState.Overseen);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            CompChildsToyOverseerSubject toyComp = pawn.TryGetComp<CompChildsToyOverseerSubject>();
            int duration = Math.Max(60, toyComp == null ? 300 : ((CompProperties_ChildsToyOverseerSubject)toyComp.props).replicationWorkTicks);
            int offspring = Math.Max(1, toyComp == null ? 2 : ((CompProperties_ChildsToyOverseerSubject)toyComp.props).replicationOffspringCount);

            Toil work = ToilMaker.MakeToil("WNG_ChildsToyReplicateWork");
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = duration;
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            yield return work;

            Toil finish = ToilMaker.MakeToil("WNG_ChildsToyReplicateFinish");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            finish.initAction = delegate
            {
                if (Target != null && Overseer != null)
                    ChildsToyUtility.TryCommitReplication(pawn, Overseer, Target, offspring);
            };
            yield return finish;
        }
    }
}
