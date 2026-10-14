using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Data-driven tuning shared by autonomous block Replicator forms.
    /// Player-control behavior is intentionally not defined here.
    /// </summary>
    public sealed class ReplicatorBlockExtension : DefModExtension
    {
        public int assimilationTicks = 300;
        public int assimilationOffspringCount = 2;
        public int hostilePopulationCap = 120;
        public float assimilationSearchRadius = 60f;
    }

    public static class ReplicatorAssimilationUtility
    {
        private const string DroneKindDefName = "WNG_ReplicatorDrone";
        private const string ReplicatorMatterDefName = "WNG_ReplicatorMatter";
        private const string ReplicatorCoreFragmentDefName = "WNG_ReplicatorCoreFragment";

        public static ReplicatorBlockExtension ExtensionFor(Pawn pawn)
        {
            return pawn?.def?.GetModExtension<ReplicatorBlockExtension>();
        }

        public static bool IsBlockReplicator(Pawn pawn)
        {
            return ExtensionFor(pawn) != null;
        }

        public static bool CanAutonomouslyAssimilate(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Map == null)
                return false;

            // A genuinely autonomous block always belongs to the hidden permanent-enemy swarm.
            // Repair direct spawns and old saves before deciding whether autonomous ecology may run.
            ReplicatorDomainUtility.EnsureAutonomousSwarmFaction(pawn);
            if (pawn.Faction == null)
                return false;

            if (pawn.Faction == Faction.OfPlayer)
                return false;

            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn) ||
                TemporaryAsuranIntrusionUtility.IsCommandSuppressed(pawn))
                return false;

            if (pawn.Spawned && ReplicatorContainmentUtility.IsContained(pawn.Map, pawn.Position))
                return false;

            return ExtensionFor(pawn) != null;
        }

        public static bool IsAssimilationTarget(Thing target, Pawn pawn)
        {
            if (!CanAutonomouslyAssimilate(pawn) || target == null || target.Destroyed || !target.Spawned || target == pawn)
                return false;

            if (target is Pawn || target is Corpse)
                return false;

            ThingDef def = target.def;
            if (def == null || !def.destroyable)
                return false;

            string defName = def.defName;
            if (defName == ReplicatorMatterDefName || defName == ReplicatorCoreFragmentDefName)
                return false;

            // Never consume a structure belonging to the same Replicator faction.
            if (target.Faction != null && target.Faction == pawn.Faction)
                return false;

            // A live containment field protects both a Replicator inside it and material/technology
            // stored inside it. The utility rechecks live power state, so power loss immediately
            // removes this suppression without a separate cached flag.
            if (ReplicatorContainmentUtility.BlocksAssimilation(pawn, target))
                return false;

            // Autonomous block Replicators consume the physical map before turning on biology:
            // loose items, buildings (including walls, natural rock and collapsed-rock piles),
            // and plants are valid mass. Roof-support destruction is intercepted after commit so
            // consuming collapsed rock cannot recursively create another collapse pile.
            if (def.category == ThingCategory.Item)
                return target.stackCount > 0;

            return def.category == ThingCategory.Building ||
                   def.category == ThingCategory.Plant;
        }

        /// <summary>
        /// High-tier Asuran/Precursor hardware is deliberately more dangerous to leave exposed to a
        /// hungry block swarm. Recognition is narrow: WNG-owned Asuran/Precursor ThingDefs at Ultra
        /// tech level only. The object must still pass every ordinary assimilation, reservation,
        /// containment and faction rule before this classification matters.
        /// </summary>
        public static bool IsHighTierAsuranTechnology(Thing target)
        {
            ThingDef def = target?.def;
            if (def == null || def.techLevel < TechLevel.Ultra)
                return false;

            string defName = def.defName ?? string.Empty;
            return defName.StartsWith("WNG_Asuran", StringComparison.Ordinal) ||
                   defName.StartsWith("WNG_Precursor", StringComparison.Ordinal);
        }

        public static float AssimilationTimeFactorFor(Thing target)
        {
            return IsHighTierAsuranTechnology(target) ? 0.55f : 1f;
        }

        /// <summary>
        /// Canonical reachable environmental assimilation target used by both ordinary matter
        /// consumption and the starvation-biological fallback. Keeping this search in one place
        /// guarantees living prey can never outrank a target the current environmental ecology
        /// would actually consume.
        /// </summary>
        public static Thing FindClosestAssimilationTarget(Pawn pawn)
        {
            if (!CanAutonomouslyAssimilate(pawn))
                return null;

            ReplicatorBlockExtension ext = ExtensionFor(pawn);
            if (ext == null)
                return null;

            Predicate<Thing> validator = delegate(Thing thing)
            {
                return IsAssimilationTarget(thing, pawn) && pawn.CanReserve(thing);
            };

            float radius = Math.Max(5f, ext.assimilationSearchRadius);
            TraverseParms traverse = TraverseParms.For(pawn);

            CompReplicatorAdaptation adaptation =
                pawn.TryGetComp<CompReplicatorAdaptation>();
            ReplicatorAdaptationFlags unresolvedHistory =
                adaptation == null
                    ? ReplicatorAdaptationFlags.None
                    : adaptation.HistoricalInterests & ~adaptation.Learned;

            if (unresolvedHistory != ReplicatorAdaptationFlags.None)
            {
                Predicate<Thing> historicalValidator = delegate(Thing thing)
                {
                    return validator(thing) &&
                           adaptation.HistoricalInterestMatches(
                               ReplicatorAdaptationUtility.EvidenceFrom(thing));
                };

                Thing historicalItem =
                    GenClosest.ClosestThing_Global_Reachable(
                        pawn.Position,
                        pawn.Map,
                        pawn.Map.listerThings.ThingsInGroup(
                            ThingRequestGroup.HaulableEver),
                        PathEndMode.Touch,
                        traverse,
                        radius,
                        historicalValidator);

                Thing historicalBuilding =
                    GenClosest.ClosestThing_Global_Reachable(
                        pawn.Position,
                        pawn.Map,
                        pawn.Map.listerThings.ThingsInGroup(
                            ThingRequestGroup.BuildingArtificial),
                        PathEndMode.Touch,
                        traverse,
                        radius,
                        historicalValidator);

                if (historicalItem != null ||
                    historicalBuilding != null)
                {
                    if (historicalItem == null)
                        return historicalBuilding;
                    if (historicalBuilding == null)
                        return historicalItem;

                    return pawn.Position.DistanceToSquared(
                               historicalItem.Position) <=
                           pawn.Position.DistanceToSquared(
                               historicalBuilding.Position)
                        ? historicalItem
                        : historicalBuilding;
                }
            }

            MapComponent_ReplicatorSwarmBehavior swarmBehavior =
                pawn.Map.GetComponent<MapComponent_ReplicatorSwarmBehavior>();
            ReplicatorSwarmPosture posture =
                swarmBehavior?.CurrentPosture ?? ReplicatorSwarmPosture.Harvest;

            // Immediate survival doctrine outranks high-value technology: a swarm under active
            // suppression or sustained defensive pressure first opens the infrastructure trapping it.
            if (posture == ReplicatorSwarmPosture.SuppressionBreak ||
                posture == ReplicatorSwarmPosture.Breach)
            {
                Thing emergencyTarget =
                    swarmBehavior?.FindPriorityAssimilationTarget(
                        pawn,
                        validator,
                        traverse,
                        radius);
                if (emergencyTarget != null)
                    return emergencyTarget;
            }

            // Cross-lattice danger: after any explicit historical reacquisition target, prefer
            // reachable high-tier Asuran/Precursor hardware over generic steel, furniture or stock.
            Predicate<Thing> highTierValidator = delegate(Thing thing)
            {
                return validator(thing) && IsHighTierAsuranTechnology(thing);
            };

            Thing highTierItem =
                GenClosest.ClosestThing_Global_Reachable(
                    pawn.Position,
                    pawn.Map,
                    pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver),
                    PathEndMode.Touch,
                    traverse,
                    radius,
                    highTierValidator);

            Thing highTierBuilding =
                GenClosest.ClosestThing_Global_Reachable(
                    pawn.Position,
                    pawn.Map,
                    pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial),
                    PathEndMode.Touch,
                    traverse,
                    radius,
                    highTierValidator);

            if (highTierItem != null || highTierBuilding != null)
            {
                if (highTierItem == null)
                    return highTierBuilding;
                if (highTierBuilding == null)
                    return highTierItem;

                return pawn.Position.DistanceToSquared(highTierItem.Position) <=
                       pawn.Position.DistanceToSquared(highTierBuilding.Position)
                    ? highTierItem
                    : highTierBuilding;
            }

            // Outside emergency Breach/SuppressionBreak, preserve #65's high-tier Asuran
            // priority, then apply Harvest/Recovery/Consolidate material doctrine before falling
            // back to the ordinary nearest-target search.
            if (posture != ReplicatorSwarmPosture.SuppressionBreak &&
                posture != ReplicatorSwarmPosture.Breach)
            {
                Thing postureTarget =
                    swarmBehavior?.FindPriorityAssimilationTarget(
                        pawn,
                        validator,
                        traverse,
                        radius);
                if (postureTarget != null)
                    return postureTarget;
            }

            // Final ecology search deliberately uses every spawned Thing. The validator keeps
            // biological pawns/corpses out until the 95% map-consumption threshold while allowing
            // plants, natural rock, constructed walls, loose items and every other physical target.
            return GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                pawn.Map,
                pawn.Map.listerThings.AllThings,
                PathEndMode.Touch,
                traverse,
                radius,
                validator);
        }

        public static int CountHostileBlocks(Map map)
        {
            if (map == null)
                return 0;

            int count = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!IsBlockReplicator(pawn) || pawn.Faction == null || Faction.OfPlayer == null)
                    continue;

                if (pawn.Faction.HostileTo(Faction.OfPlayer))
                    count++;
            }
            return count;
        }

        public static int HostilePopulationCap(ReplicatorBlockExtension ext)
        {
            // The Def value remains a safe load-time fallback; once ModSettings exist, the player's
            // current global cap is authoritative for every hostile block-Replicator creation path.
            return WNGMod.Settings != null
                ? WNGSettingsUtility.HostileReplicatorCap
                : Math.Max(1, ext?.hostilePopulationCap ?? 120);
        }

        public static int AvailablePopulationSlots(Pawn parent, int requestedOffspring)
        {
            ReplicatorBlockExtension ext = ExtensionFor(parent);
            if (parent?.Map == null || ext == null || requestedOffspring <= 0)
                return 0;

            if (Faction.OfPlayer != null &&
                parent.Faction != null &&
                parent.Faction.HostileTo(Faction.OfPlayer))
            {
                int available = Math.Max(0, HostilePopulationCap(ext) - CountHostileBlocks(parent.Map));
                return Math.Min(requestedOffspring, available);
            }

            return requestedOffspring;
        }

        public static bool HasPopulationRoom(Pawn parent, int offspringCount)
        {
            return AvailablePopulationSlots(parent, offspringCount) >= offspringCount;
        }

        public static bool TryCommitAssimilation(Pawn parent, Thing target)
        {
            if (!IsAssimilationTarget(target, parent))
                return false;

            ReplicatorBlockExtension ext = ExtensionFor(parent);
            if (ext == null)
                return false;

            int offspringCount = Math.Max(1, ext.assimilationOffspringCount);
            int offspringToSpawn = AvailablePopulationSlots(parent, offspringCount);

            PawnKindDef droneKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(DroneKindDefName);
            if ((offspringToSpawn > 0 && droneKind == null) || parent.Map == null || parent.Faction == null)
                return false;

            Map map = parent.Map;
            IntVec3 origin = target.Position;
            ReplicatorAdaptationEvidence adaptationEvidence = ReplicatorAdaptationUtility.EvidenceFrom(target);
            CompReplicatorMaterialProfile parentMaterial =
                parent.TryGetComp<CompReplicatorMaterialProfile>();
            ReplicatorMaterialGrade offspringMaterialGrade =
                WNGSettingsUtility.ReplicatorMaterialAdaptationEnabled
                    ? ReplicatorMaterialProfileUtility.GradeFromSource(target)
                    : (parentMaterial?.Grade ?? ReplicatorMaterialGrade.Standard);
            bool consumedHighTierAsuranTechnology = IsHighTierAsuranTechnology(target);
            string consumedTechnologyLabel = consumedHighTierAsuranTechnology ? target.LabelCap : null;
            List<Pawn> staged = new List<Pawn>(offspringToSpawn);
            HashSet<IntVec3> pendingRoofCollapsesBefore =
                ReplicatorEnvironmentalAssimilationUtility.CapturePendingRoofCollapses(map);

            try
            {
                // Population limits cap simultaneous bodies, not consumption. If the swarm is at
                // its body cap, the target is still eaten and its mass is banked for later Drone creation.
                for (int i = 0; i < offspringToSpawn; i++)
                {
                    if (AvailablePopulationSlots(parent, 1) < 1)
                        break;

                    Pawn child = PawnGenerator.GeneratePawn(droneKind, parent.Faction);
                    ReplicatorDomainUtility.CopyDomain(parent, child);
                    TemporaryAsuranIntrusionUtility.CopyState(parent, child);
                    ReplicatorSovereignControlUtility.CopyState(parent, child);
                    child.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(parent.TryGetComp<CompReplicatorAdaptation>());
                    child.TryGetComp<CompReplicatorMaterialProfile>()?.SetGrade(offspringMaterialGrade);
                    if (!GenPlace.TryPlaceThing(child, origin, map, ThingPlaceMode.Near))
                    {
                        if (!child.Destroyed)
                            child.Destroy(DestroyMode.Vanish);
                        Rollback(staged);
                        return false;
                    }
                    staged.Add(child);
                }

                // The real environmental target is the commit point. If it changed while the job was
                // running, do not exchange it for offspring.
                if (!IsAssimilationTarget(target, parent))
                {
                    Rollback(staged);
                    return false;
                }

                try
                {
                    target.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    // If RimWorld actually completed destruction before throwing, conservation says the
                    // staged offspring are now the committed result. Otherwise roll them back.
                    if (!target.Destroyed)
                    {
                        Rollback(staged);
                        Log.Error("[WNG] Replicator assimilation target destruction failed before commit: " + ex);
                        return false;
                    }
                    Log.Error("[WNG] Replicator assimilation target reported an exception after destruction; keeping committed offspring: " + ex);
                }

                if (!target.Destroyed)
                {
                    Rollback(staged);
                    return false;
                }

                // Destroying walls/natural rock uses vanilla roof-holder logic, which schedules
                // unsupported roofs to collapse. Convert only the newly scheduled roofs caused
                // by this assimilation into direct roof consumption before thick roof can spawn
                // CollapsedRocks. Pre-existing collapse events are left untouched.
                ReplicatorEnvironmentalAssimilationUtility.AssimilateNewlyUnsupportedRoofs(
                    map,
                    pendingRoofCollapsesBefore);

                int deferredOffspring = Math.Max(0, offspringCount - staged.Count);
                if (deferredOffspring > 0)
                {
                    map.GetComponent<MapComponent_ReplicatorConsumption>()
                        ?.AddStoredCellMatter(deferredOffspring * MapComponent_ReplicatorConsumption.CellMatterPerDrone);
                }

                // Adaptation is post-commit knowledge. A learning/share failure must never roll back
                // already-committed offspring after the real environmental target is gone.
                try
                {
                    ReplicatorAdaptationUtility.ShareEvidence(parent, adaptationEvidence);
                }
                catch (Exception ex)
                {
                    Log.Error("[WNG] Replicator assimilation committed but adaptation sharing failed: " + ex);
                }

                if (consumedHighTierAsuranTechnology && map.IsPlayerHome)
                {
                    try
                    {
                        Messages.Message(
                            "Replicators have rapidly assimilated " + consumedTechnologyLabel +
                            ". The swarm learned only the real technology traits present on the consumed object, but high-tier Asuran hardware was processed substantially faster than ordinary material.",
                            parent,
                            MessageTypeDefOf.ThreatSmall,
                            historical: false);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[WNG] High-tier Asuran assimilation committed but warning presentation failed: " + ex.Message);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Rollback(staged);
                Log.Error("[WNG] Replicator assimilation transaction failed and staged offspring were rolled back: " + ex);
                return false;
            }
        }

        private static void Rollback(List<Pawn> pawns)
        {
            if (pawns == null)
                return;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && !pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
            }
            pawns.Clear();
        }
    }

    public sealed class JobDriver_ReplicatorAssimilate : JobDriver
    {
        private Thing Target => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Target != null && pawn.Reserve(Target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Target == null || Target.Destroyed || !Target.Spawned ||
                              !ReplicatorAssimilationUtility.IsAssimilationTarget(Target, pawn));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            ReplicatorBlockExtension ext = ReplicatorAssimilationUtility.ExtensionFor(pawn);
            int baseDuration = Math.Max(90, ext?.assimilationTicks ?? 300);
            float adaptationFactor = pawn.TryGetComp<CompReplicatorAdaptationEffects>()?.AssimilationTimeFactor ?? 1f;
            float coordinationFactor = pawn.Map?.GetComponent<MapComponent_ReplicatorCoordination>()?.AssimilationFactorFor(pawn) ?? 1f;
            float specialistFactor = ReplicatorBurrowerUtility.AssimilationTimeFactor(pawn, Target);
            float highTierAsuranFactor = ReplicatorAssimilationUtility.AssimilationTimeFactorFor(Target);
            int duration = Math.Max(
                60,
                (int)Math.Round(
                    baseDuration *
                    adaptationFactor *
                    coordinationFactor *
                    specialistFactor *
                    highTierAsuranFactor));

            Toil work = ToilMaker.MakeToil("WNG_ReplicatorAssimilate");
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = duration;
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            yield return work;

            Toil finish = ToilMaker.MakeToil("WNG_ReplicatorAssimilateFinish");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            finish.initAction = delegate
            {
                Thing target = Target;
                if (target != null)
                    ReplicatorAssimilationUtility.TryCommitAssimilation(pawn, target);
            };
            yield return finish;
        }
    }

    public sealed class JobGiver_ReplicatorAssimilate : ThinkNode_JobGiver
    {
        // Failed global searches are costly when dozens of Replicators repeatedly
        // ask the think tree for work. A short negative-only delay preserves any
        // acquired target, and requires no pawn-graph/save serialization.
        private static readonly Dictionary<int, int> nextSearchAfterFailure = new Dictionary<int, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ReplicatorAssimilationUtility.CanAutonomouslyAssimilate(pawn))
                return null;

            if (ReplicatorAssimilationUtility.ExtensionFor(pawn) == null)
                return null;

            // The population cap limits bodies, not appetite. Avoid repeating a
            // *failed* multi-pass global path search in every AI evaluation.
            int now = Find.TickManager?.TicksGame ?? 0;
            int nextAllowed;
            if (nextSearchAfterFailure.TryGetValue(pawn.thingIDNumber, out nextAllowed) &&
                now >= 0 && now < nextAllowed)
                return null;
            Thing target = ReplicatorAssimilationUtility.FindClosestAssimilationTarget(pawn);
            if (target == null)
            {
                if (nextSearchAfterFailure.Count > 2048)
                    nextSearchAfterFailure.Clear();
                nextSearchAfterFailure[pawn.thingIDNumber] = now + 120;
                return null;
            }
            nextSearchAfterFailure.Remove(pawn.thingIDNumber);

            JobDef jobDef =
                DefDatabase<JobDef>.GetNamedSilentFail("WNG_ReplicatorAssimilate");
            return jobDef == null
                ? null
                : JobMaker.MakeJob(jobDef, target);
        }
    }
}
