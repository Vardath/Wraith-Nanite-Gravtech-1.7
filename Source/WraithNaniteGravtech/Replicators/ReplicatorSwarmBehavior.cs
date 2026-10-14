using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    public enum ReplicatorSwarmPosture
    {
        Harvest = 0,
        Breach = 1,
        Consolidate = 2,
        SuppressionBreak = 3,
        Recovery = 4
    }

    /// <summary>
    /// Restored map-local Replicator doctrine layer. Postures change only environmental-material
    /// priorities and recombination cadence. They never create a proactive biological-pawn target
    /// path; self-defense, bounded retaliation and last-resort starvation remain the only current
    /// biological combat routes.
    /// </summary>
    public sealed class MapComponent_ReplicatorSwarmBehavior : MapComponent
    {
        public const int EvaluationIntervalTicks = 300;
        public const int PressureWindowTicks = 3000;
        public const int PressureEventsForBreach = 3;
        public const int BreachDurationTicks = 12000;
        public const int ConsolidatePopulation = 18;
        public const float ConsolidateAssemblyFactor = 0.84f;
        public const int SuppressionMinimumDisabled = 3;
        public const int SuppressionPercentThreshold = 30;
        public const int RecoveryHighWaterPopulation = 12;
        public const int RecoveryPopulationThreshold = 6;
        public const int RecoveryDurationTicks = 12000;

        private int recentPressureEvents;
        private int lastPressureTick = -999999;
        private int breachUntilTick;
        private int nextEvaluationTick;
        private int peakHostileBlockPopulation;
        private int lastObservedBlockPopulation;
        private int recoveryUntilTick;
        private ReplicatorSwarmPosture cachedPosture = ReplicatorSwarmPosture.Harvest;

        public MapComponent_ReplicatorSwarmBehavior(Map map) : base(map) { }

        public ReplicatorSwarmPosture CurrentPosture
        {
            get
            {
                RefreshIfNeeded();
                return cachedPosture;
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public void RegisterDefensePressure(Pawn victim, Pawn attacker)
        {
            if (victim == null ||
                attacker == null ||
                victim.Map != map ||
                attacker.Map != map ||
                victim.Faction == null ||
                victim.Faction == Faction.OfPlayer ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(victim))
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if ((long)now - lastPressureTick > PressureWindowTicks)
                recentPressureEvents = 0;

            lastPressureTick = now;
            recentPressureEvents++;

            if (recentPressureEvents < PressureEventsForBreach)
                return;

            bool entering = now >= breachUntilTick;
            breachUntilTick = Math.Max(
                breachUntilTick,
                SafeFutureTick(now, BreachDurationTicks));
            recentPressureEvents = 0;
            nextEvaluationTick = 0;

            if (entering && map.IsPlayerHome)
            {
                try
                {
                    Messages.Message(
                        "Replicator behavior shift: sustained resistance has pushed the swarm into a breach posture. It will prioritize anti-Replicator devices, defenses, power and access structures while leaving uninvolved biological pawns to the existing self-defense/starvation rules.",
                        victim,
                        MessageTypeDefOf.ThreatSmall,
                        historical: false);
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Replicator breach posture committed but presentation failed: " +
                        ex.Message);
                }
            }
        }

        public float AssemblyFactorFor(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Faction == Faction.OfPlayer ||
                CurrentPosture != ReplicatorSwarmPosture.Consolidate)
            {
                return 1f;
            }

            return ConsolidateAssemblyFactor;
        }

        /// <summary>
        /// Returns a reachable ordinary assimilation target preferred by the current doctrine.
        /// The supplied validator is the canonical current assimilation validator, so this layer
        /// cannot make natural rock, pawns, same-faction structures or contained technology legal.
        /// </summary>
        public Thing FindPriorityAssimilationTarget(
            Pawn pawn,
            Predicate<Thing> canonicalValidator,
            TraverseParms traverse,
            float radius)
        {
            if (pawn == null ||
                pawn.Map != map ||
                canonicalValidator == null)
            {
                return null;
            }

            ReplicatorSwarmPosture posture = CurrentPosture;
            switch (posture)
            {
                case ReplicatorSwarmPosture.SuppressionBreak:
                    return FirstReachableByTier(
                        pawn,
                        canonicalValidator,
                        traverse,
                        radius,
                        IsAntiReplicatorSuppressionInfrastructure,
                        IsPowerInfrastructure,
                        IsAccessInfrastructure);

                case ReplicatorSwarmPosture.Breach:
                    return FirstReachableByTier(
                        pawn,
                        canonicalValidator,
                        traverse,
                        radius,
                        IsAntiReplicatorSuppressionInfrastructure,
                        IsDefensiveInfrastructure,
                        IsPowerInfrastructure,
                        IsAccessInfrastructure);

                case ReplicatorSwarmPosture.Recovery:
                    // Current D108 explicitly excludes natural rock/mineables from ordinary
                    // assimilation until yield semantics are decided. Recovery therefore seeks
                    // loose haulable mass, not historical mineable-rock behavior.
                    return ClosestReachable(
                        pawn,
                        ThingRequestGroup.HaulableEver,
                        canonicalValidator,
                        traverse,
                        radius);

                case ReplicatorSwarmPosture.Consolidate:
                    return FindAdvancedTechnologyTarget(
                        pawn,
                        canonicalValidator,
                        traverse,
                        radius);

                case ReplicatorSwarmPosture.Harvest:
                default:
                    return ClosestReachable(
                        pawn,
                        ThingRequestGroup.HaulableEver,
                        canonicalValidator,
                        traverse,
                        radius);
            }
        }

        private Thing FirstReachableByTier(
            Pawn pawn,
            Predicate<Thing> canonicalValidator,
            TraverseParms traverse,
            float radius,
            params Predicate<Thing>[] tiers)
        {
            if (tiers == null)
                return null;

            for (int i = 0; i < tiers.Length; i++)
            {
                Predicate<Thing> tier = tiers[i];
                if (tier == null)
                    continue;

                Predicate<Thing> validator =
                    thing => canonicalValidator(thing) && tier(thing);

                Thing target = ClosestReachable(
                    pawn,
                    ThingRequestGroup.BuildingArtificial,
                    validator,
                    traverse,
                    radius);
                if (target != null)
                    return target;
            }

            return null;
        }

        private Thing FindAdvancedTechnologyTarget(
            Pawn pawn,
            Predicate<Thing> canonicalValidator,
            TraverseParms traverse,
            float radius)
        {
            Predicate<Thing> advanced = delegate(Thing thing)
            {
                if (!canonicalValidator(thing))
                    return false;

                ReplicatorAdaptationEvidence evidence =
                    ReplicatorAdaptationUtility.EvidenceFrom(thing);
                ReplicatorAdaptationFlags nonMaterial =
                    evidence.flags & ~ReplicatorAdaptationFlags.Material;
                return nonMaterial != ReplicatorAdaptationFlags.None;
            };

            Thing item = ClosestReachable(
                pawn,
                ThingRequestGroup.HaulableEver,
                advanced,
                traverse,
                radius);
            Thing building = ClosestReachable(
                pawn,
                ThingRequestGroup.BuildingArtificial,
                advanced,
                traverse,
                radius);
            return Closer(pawn, item, building);
        }

        private Thing ClosestReachable(
            Pawn pawn,
            ThingRequestGroup group,
            Predicate<Thing> validator,
            TraverseParms traverse,
            float radius)
        {
            List<Thing> things = map.listerThings.ThingsInGroup(group);
            if (things == null || things.Count == 0)
                return null;

            return GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                map,
                things,
                PathEndMode.Touch,
                traverse,
                radius,
                validator);
        }

        private static Thing Closer(Pawn pawn, Thing first, Thing second)
        {
            if (first == null)
                return second;
            if (second == null)
                return first;

            return pawn.Position.DistanceToSquared(first.Position) <=
                   pawn.Position.DistanceToSquared(second.Position)
                ? first
                : second;
        }

        private static bool IsAntiReplicatorSuppressionInfrastructure(Thing thing)
        {
            string name = thing?.def?.defName ?? string.Empty;
            return name == "WNG_ReplicatorContainmentProjector" ||
                   name == "WNG_ReplicatorSuppressionEmitter";
        }

        private static bool IsDefensiveInfrastructure(Thing thing)
        {
            if (thing?.def == null)
                return false;

            string name = thing.def.defName ?? string.Empty;
            return thing.def.building?.turretGunDef != null ||
                   name.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPowerInfrastructure(Thing thing)
        {
            if (thing?.def == null)
                return false;

            string name = thing.def.defName ?? string.Empty;
            return thing.TryGetComp<CompPowerTrader>() != null ||
                   thing.TryGetComp<CompPowerBattery>() != null ||
                   name.IndexOf("Generator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Battery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Power", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Reactor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAccessInfrastructure(Thing thing)
        {
            string name = thing?.def?.defName ?? string.Empty;
            return thing is Building_Door ||
                   name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Gate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Barrier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshIfNeeded()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextEvaluationTick)
                return;

            nextEvaluationTick = SafeFutureTick(now, EvaluationIntervalTicks);
            List<Pawn> hostileBlocks = HostileBlocks();
            int population = hostileBlocks.Count;

            if (population <= 0)
            {
                peakHostileBlockPopulation = 0;
                lastObservedBlockPopulation = 0;
                recoveryUntilTick = 0;
                cachedPosture = ReplicatorSwarmPosture.Harvest;
                return;
            }

            peakHostileBlockPopulation =
                Math.Max(peakHostileBlockPopulation, population);

            if (lastObservedBlockPopulation > RecoveryPopulationThreshold &&
                population <= RecoveryPopulationThreshold &&
                peakHostileBlockPopulation >= RecoveryHighWaterPopulation)
            {
                recoveryUntilTick = Math.Max(
                    recoveryUntilTick,
                    SafeFutureTick(now, RecoveryDurationTicks));
            }

            lastObservedBlockPopulation = population;

            if (SuppressionBreakRequired(hostileBlocks))
            {
                cachedPosture = ReplicatorSwarmPosture.SuppressionBreak;
                return;
            }

            if (now < breachUntilTick)
            {
                cachedPosture = ReplicatorSwarmPosture.Breach;
                return;
            }

            if (now < recoveryUntilTick)
            {
                cachedPosture = ReplicatorSwarmPosture.Recovery;
                return;
            }

            cachedPosture =
                IsMatureCoordinatedSwarm(hostileBlocks)
                    ? ReplicatorSwarmPosture.Consolidate
                    : ReplicatorSwarmPosture.Harvest;
        }

        private List<Pawn> HostileBlocks()
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            List<Pawn> result = new List<Pawn>();

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (pawn == null ||
                    pawn.Dead ||
                    !pawn.Spawned ||
                    pawn.Faction == null ||
                    Faction.OfPlayer == null ||
                    !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                {
                    continue;
                }

                if (pawn.Faction.HostileTo(Faction.OfPlayer) ||
                    Faction.OfPlayer.HostileTo(pawn.Faction))
                {
                    result.Add(pawn);
                }
            }

            return result;
        }

        private bool SuppressionBreakRequired(List<Pawn> hostileBlocks)
        {
            if (hostileBlocks == null || hostileBlocks.Count == 0)
                return false;

            int disabled = 0;
            for (int i = 0; i < hostileBlocks.Count; i++)
            {
                Pawn pawn = hostileBlocks[i];
                if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn) ||
                    ReplicatorContainmentUtility.IsContained(map, pawn.Position))
                {
                    disabled++;
                }
            }

            if (disabled < SuppressionMinimumDisabled)
                return false;

            return disabled * 100 >=
                   hostileBlocks.Count * SuppressionPercentThreshold;
        }

        private bool IsMatureCoordinatedSwarm(List<Pawn> hostileBlocks)
        {
            if (hostileBlocks == null ||
                hostileBlocks.Count < ConsolidatePopulation)
            {
                return false;
            }

            MapComponent_ReplicatorCoordination coordination =
                map.GetComponent<MapComponent_ReplicatorCoordination>();
            if (coordination == null)
                return false;

            for (int i = 0; i < hostileBlocks.Count; i++)
            {
                Pawn pawn = hostileBlocks[i];
                if (coordination.HasFunctioningControllerFor(pawn))
                    return true;
            }

            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref recentPressureEvents,
                "wngReplicatorPosturePressureEvents",
                0);
            Scribe_Values.Look(
                ref lastPressureTick,
                "wngReplicatorPostureLastPressureTick",
                -999999);
            Scribe_Values.Look(
                ref breachUntilTick,
                "wngReplicatorPostureBreachUntil",
                0);
            Scribe_Values.Look(
                ref peakHostileBlockPopulation,
                "wngReplicatorPosturePeakPopulation",
                0);
            Scribe_Values.Look(
                ref lastObservedBlockPopulation,
                "wngReplicatorPostureLastPopulation",
                0);
            Scribe_Values.Look(
                ref recoveryUntilTick,
                "wngReplicatorPostureRecoveryUntil",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                recentPressureEvents =
                    Math.Max(
                        0,
                        Math.Min(
                            PressureEventsForBreach - 1,
                            recentPressureEvents));

                int now = Find.TickManager?.TicksGame ?? 0;
                if (lastPressureTick < -999999)
                    lastPressureTick = -999999;
                else if (lastPressureTick > now)
                    lastPressureTick = now;

                breachUntilTick = Math.Max(0, breachUntilTick);
                peakHostileBlockPopulation =
                    Math.Max(0, peakHostileBlockPopulation);
                lastObservedBlockPopulation =
                    Math.Max(0, lastObservedBlockPopulation);
                recoveryUntilTick = Math.Max(0, recoveryUntilTick);
                nextEvaluationTick = 0;
                cachedPosture = ReplicatorSwarmPosture.Harvest;
            }
        }
    }
}
