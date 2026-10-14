using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AbilityReplicatorSovereignSummon : CompProperties_AbilityEffect
    {
        public int summonCount = 10;

        public CompProperties_AbilityReplicatorSovereignSummon()
        {
            compClass = typeof(CompAbilityEffect_ReplicatorSovereignSummon);
        }
    }

    public sealed class CompProperties_AbilityReplicatorSovereignReleaseSummoned : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityReplicatorSovereignReleaseSummoned()
        {
            compClass = typeof(CompAbilityEffect_ReplicatorSovereignReleaseSummoned);
        }
    }

    /// <summary>
    /// Shared Queen / Sovereign Neural Lattice deployment contract. Summoned Drones are first given
    /// one exact autonomous pre-control domain, then sovereign authority is applied transactionally.
    /// Releasing them later therefore restores the whole batch to one coordinated feral swarm rather
    /// than inventing a second ownership state or generating replacement pawns.
    /// </summary>
    public static class ReplicatorSovereignDeploymentUtility
    {
        private const string DroneKindDefName = "WNG_ReplicatorDrone";
        private const string SwarmFactionDefName = "WNG_ReplicatorSwarm";
        private const string FeralPrefix = "summoned-feral:";

        public static bool TryResolveAuthority(Pawn controller, out ReplicatorControlAuthority authority)
        {
            authority = ReplicatorControlAuthority.Unassigned;
            if (controller == null || controller.Dead || controller.Faction == null)
                return false;

            if (ReplicatorQueenUtility.IsExactQueen(controller))
            {
                authority = ReplicatorControlAuthority.ExactQueen;
                return true;
            }

            if (SovereignLatticeUtility.HasInstalledLattice(controller))
            {
                authority = ReplicatorControlAuthority.SovereignNeuralLattice;
                return true;
            }

            return false;
        }

        public static bool SignalAvailable(Pawn controller, out string reason)
        {
            reason = null;
            if (!TryResolveAuthority(controller, out ReplicatorControlAuthority authority))
            {
                reason = "Requires the exact Replicator Queen or an implanted Sovereign Neural Lattice.";
                return false;
            }
            if (!controller.Spawned || controller.Map == null)
            {
                reason = "The sovereign controller must be physically present on a map.";
                return false;
            }
            if (authority == ReplicatorControlAuthority.SovereignNeuralLattice)
            {
                if (!SovereignLatticeUtility.SignalAvailable(controller))
                {
                    reason = "The Sovereign Neural Lattice is disrupted, contained or otherwise unable to project control.";
                    return false;
                }
            }
            else if (ReplicatorInterferenceUtility.IsEmpDisrupted(controller) ||
                     ReplicatorContainmentUtility.IsContained(controller.Map, controller.Position))
            {
                reason = "EMP disruption or active containment is blocking the Queen's sovereign lattice.";
                return false;
            }
            return true;
        }

        public static bool CanSummon(Pawn controller, int count, out string reason)
        {
            if (!SignalAvailable(controller, out reason))
                return false;

            count = Math.Max(1, count);
            if (SovereignLatticeUtility.HasInstalledLattice(controller))
            {
                int free = SovereignLatticeUtility.MaxControlledBlocks - SovereignLatticeUtility.ControlledCount(controller);
                if (free < count)
                {
                    reason = "The Sovereign Neural Lattice needs " + count + " free authority slots; only " + Math.Max(0, free) + " remain.";
                    return false;
                }
            }

            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(DroneKindDefName);
            FactionDef swarmDef = DefDatabase<FactionDef>.GetNamedSilentFail(SwarmFactionDefName);
            Faction swarm = swarmDef == null ? null : Find.FactionManager?.FirstFactionOfDef(swarmDef);
            if (kind == null || swarm == null)
            {
                reason = "The Replicator swarm definitions are unavailable.";
                return false;
            }
            return true;
        }

        public static bool TrySummon(Pawn controller, int count)
        {
            count = Math.Max(1, count);
            if (!CanSummon(controller, count, out string reason))
            {
                if (controller?.Faction == Faction.OfPlayer && !reason.NullOrEmpty())
                    Messages.Message(reason, controller, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            Map map = controller.Map;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(DroneKindDefName);
            FactionDef swarmDef = DefDatabase<FactionDef>.GetNamedSilentFail(SwarmFactionDefName);
            Faction swarm = swarmDef == null ? null : Find.FactionManager?.FirstFactionOfDef(swarmDef);
            if (map == null || kind == null || swarm == null)
                return false;

            ReplicatorControlAuthority authority = ReplicatorQueenUtility.IsExactQueen(controller)
                ? ReplicatorControlAuthority.ExactQueen
                : ReplicatorControlAuthority.SovereignNeuralLattice;
            int now = Find.TickManager?.TicksGame ?? 0;
            string feralDomain = FeralPrefix + controller.thingIDNumber + ":" + now;
            List<Pawn> staged = new List<Pawn>(count);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    Pawn drone = PawnGenerator.GeneratePawn(kind, swarm);
                    drone.TryGetComp<CompReplicatorDomain>()?.AssignAutonomousDomain(feralDomain);
                    if (!GenPlace.TryPlaceThing(drone, controller.Position, map, ThingPlaceMode.Near))
                    {
                        if (!drone.Destroyed)
                            drone.Destroy(DestroyMode.Vanish);
                        RollBack(staged);
                        return false;
                    }
                    staged.Add(drone);
                }

                // Recheck the controller after placement callbacks before authority commits.
                if (!CanSummon(controller, count, out reason))
                {
                    RollBack(staged);
                    if (controller.Faction == Faction.OfPlayer && !reason.NullOrEmpty())
                        Messages.Message(reason, controller, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                for (int i = 0; i < staged.Count; i++)
                {
                    CompReplicatorSovereignState state = staged[i].TryGetComp<CompReplicatorSovereignState>();
                    bool ok = authority == ReplicatorControlAuthority.ExactQueen
                        ? state?.TryAssignExactQueen(controller) == true
                        : state?.TryAssignSovereignNeuralLattice(controller) == true;
                    if (!ok)
                    {
                        RollBack(staged);
                        return false;
                    }
                }

                if (controller.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        controller.LabelShortCap + " has deployed " + staged.Count + " controlled Replicator Drones. They can be released together as a feral swarm from the sovereign command gizmo.",
                        controller,
                        MessageTypeDefOf.PositiveEvent,
                        false);
                }
                return true;
            }
            catch (Exception ex)
            {
                RollBack(staged);
                Log.Error("[WNG] Sovereign Replicator deployment failed; staged Drones rolled back when possible: " + ex);
                return false;
            }
        }

        private static void RollBack(List<Pawn> staged)
        {
            if (staged == null)
                return;
            for (int i = 0; i < staged.Count; i++)
            {
                Pawn pawn = staged[i];
                if (pawn == null)
                    continue;
                pawn.TryGetComp<CompReplicatorSovereignState>()?.TryReleaseToExactPriorState();
                if (!pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
            }
            staged.Clear();
        }

        public static List<Pawn> ReleasableSummoned(Pawn controller)
        {
            if (controller?.Map?.mapPawns?.AllPawnsSpawned == null)
                return new List<Pawn>();

            string prefix = FeralPrefix + controller.thingIDNumber + ":";
            return controller.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && !p.Dead && p.Spawned && ReplicatorAssimilationUtility.IsBlockReplicator(p))
                .Where(p =>
                {
                    CompReplicatorSovereignState state = p.TryGetComp<CompReplicatorSovereignState>();
                    return state?.HasRecord == true && state.ControllerPawn == controller &&
                           (state.ControlAuthority == ReplicatorControlAuthority.ExactQueen ||
                            state.ControlAuthority == ReplicatorControlAuthority.SovereignNeuralLattice) &&
                           state.PriorAuthority == ReplicatorControlAuthority.AutonomousSwarm &&
                           !state.PriorDomainId.NullOrEmpty() && state.PriorDomainId.StartsWith(prefix, StringComparison.Ordinal);
                })
                .ToList();
        }

        public static bool TryReleaseSummoned(Pawn controller)
        {
            if (!SignalAvailable(controller, out string reason))
            {
                if (controller?.Faction == Faction.OfPlayer && !reason.NullOrEmpty())
                    Messages.Message(reason, controller, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            List<Pawn> pawns = ReleasableSummoned(controller);
            if (pawns.Count == 0)
            {
                if (controller.Faction == Faction.OfPlayer)
                    Messages.Message("No controlled summoned Replicators on this map are available for feral release.", controller, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            int released = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].TryGetComp<CompReplicatorSovereignState>()?.TryReleaseToExactPriorState() == true)
                    released++;
            }

            if (controller.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    released + " summoned Replicator " + (released == 1 ? "Drone has" : "Drones have") + " been released into their shared feral swarm domain. They are no longer under colony control.",
                    controller,
                    released > 0 ? MessageTypeDefOf.CautionInput : MessageTypeDefOf.RejectInput,
                    false);
            }
            return released > 0;
        }
    }

    public sealed class CompAbilityEffect_ReplicatorSovereignSummon : CompAbilityEffect
    {
        public new CompProperties_AbilityReplicatorSovereignSummon Props => (CompProperties_AbilityReplicatorSovereignSummon)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            bool valid = ReplicatorSovereignDeploymentUtility.CanSummon(parent?.pawn, Math.Max(1, Props.summonCount), out string reason);
            if (!valid && throwMessages && parent?.pawn != null && !reason.NullOrEmpty())
                Messages.Message(reason, parent.pawn, MessageTypeDefOf.RejectInput, false);
            return valid && base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            ReplicatorSovereignDeploymentUtility.TrySummon(parent?.pawn, Math.Max(1, Props.summonCount));
        }
    }

    public sealed class CompAbilityEffect_ReplicatorSovereignReleaseSummoned : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn controller = parent?.pawn;
            bool valid = ReplicatorSovereignDeploymentUtility.SignalAvailable(controller, out string reason) &&
                         ReplicatorSovereignDeploymentUtility.ReleasableSummoned(controller).Count > 0;
            if (!valid && throwMessages && controller != null)
            {
                if (reason.NullOrEmpty())
                    reason = "No controlled summoned Replicators on this map are available for feral release.";
                Messages.Message(reason, controller, MessageTypeDefOf.RejectInput, false);
            }
            return valid && base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            ReplicatorSovereignDeploymentUtility.TryReleaseSummoned(parent?.pawn);
        }
    }
}
