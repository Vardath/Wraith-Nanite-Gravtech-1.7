using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    public static class ReplicatorBurrowerUtility
    {
        public const string BurrowerDefName = "WNG_ReplicatorBurrower";
        private const string ContainmentProjectorDefName = "WNG_ReplicatorContainmentProjector";

        // Archaeology-backed first-build tuning. These are role modifiers, not a second assimilation
        // economy: the existing atomic two-Drone commit still owns all successful assimilation.
        public const float BuildingAssimilationFactor = 0.55f;
        public const float ItemAssimilationFactor = 1.15f;

        public static bool IsBurrower(Pawn pawn)
        {
            return pawn?.def?.defName == BurrowerDefName;
        }

        public static bool CanOperate(Pawn pawn)
        {
            return IsBurrower(pawn) && pawn.Faction != Faction.OfPlayer &&
                   ReplicatorAssimilationUtility.CanAutonomouslyAssimilate(pawn);
        }

        public static float AssimilationTimeFactor(Pawn pawn, Thing target)
        {
            if (!IsBurrower(pawn) || target?.def == null)
                return 1f;
            return target.def.category == ThingCategory.Building ? BuildingAssimilationFactor : ItemAssimilationFactor;
        }

        public static bool IsStructuralBlocker(Thing thing)
        {
            ThingDef def = thing?.def;
            if (def == null || def.category != ThingCategory.Building || def.building == null || def.building.isNaturalRock)
                return false;

            // Use concrete Def fields rather than historical DefName substring guessing. Doors and
            // impassable/high-fill artificial buildings are physical access blockers; this also
            // naturally includes wall- and barricade-like structures without naming third-party Defs.
            return def.IsDoor || def.passability == Traversability.Impassable || def.fillPercent >= 0.50f;
        }

        public static bool IsActiveHostileContainmentProjector(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null || thing.Destroyed || !thing.Spawned || thing.Map != pawn.Map ||
                thing.def?.defName != ContainmentProjectorDefName || thing.Faction == pawn.Faction)
            {
                return false;
            }

            return thing.TryGetComp<CompReplicatorContainmentProjector>()?.Active == true;
        }

        public static int StructuralPriority(Thing thing)
        {
            if (thing?.def == null)
                return int.MinValue;

            int score = 0;
            if (thing.def.IsDoor)
                score += 120;
            if (thing.def.passability == Traversability.Impassable)
                score += 90;
            if (thing.def.fillPercent >= 0.50f)
                score += 45;
            if (thing.TryGetComp<CompPowerTrader>() != null || thing.TryGetComp<CompPowerBattery>() != null)
                score += 20;
            return score;
        }
    }

    /// <summary>
    /// Hostile autonomous Hunter-mass reconfiguration into the structural Burrower side-form.
    /// Historical WNG used an Armor-learned, fourteen-body threshold with slow role caps; WNGv1
    /// keeps those values only as provisional tuning while preserving the newer no-free-mass and
    /// target-first transaction rules.
    /// </summary>
    public sealed class MapComponent_ReplicatorBurrowerFormation : MapComponent
    {
        public const int FormationThreshold = 14;
        public const int FormationCheckIntervalTicks = 1800;
        public const int AdditionalBurrowerPerPopulation = 34;
        public const int MaxBurrowers = 3;

        private int nextFormationCheckTick;

        public MapComponent_ReplicatorBurrowerFormation(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextFormationCheckTick)
                return;

            nextFormationCheckTick = SafeFutureTick(now, FormationCheckIntervalTicks);
            TryFormBurrower();
        }

        private void TryFormBurrower()
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            Dictionary<string, List<Pawn>> blocksByDomain = new Dictionary<string, List<Pawn>>();

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Faction == null || pawn.Faction == Faction.OfPlayer ||
                    !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                    continue;
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

                int existing = 0;
                for (int i = 0; i < blocks.Count; i++)
                    if (ReplicatorBurrowerUtility.IsBurrower(blocks[i]))
                        existing++;

                int roleCap = Math.Min(MaxBurrowers, 1 + blocks.Count / Math.Max(1, AdditionalBurrowerPerPopulation));
                if (existing >= roleCap)
                    continue;

                Pawn source = FindHunterSource(blocks);
                if (source != null && TryConvert(source))
                    return;
            }
        }

        private Pawn FindHunterSource(List<Pawn> blocks)
        {
            Pawn source = null;
            for (int i = 0; i < blocks.Count; i++)
            {
                Pawn candidate = blocks[i];
                if (candidate?.def?.defName != "WNG_ReplicatorHunter" || candidate.Downed ||
                    ReplicatorInterferenceUtility.IsEmpDisrupted(candidate) ||
                    TemporaryAsuranIntrusionUtility.IsCommandSuppressed(candidate) ||
                    ReplicatorContainmentUtility.IsContained(map, candidate.Position) ||
                    candidate.TryGetComp<CompReplicatorAdaptation>()?.Has(ReplicatorAdaptationFlags.Armor) != true)
                {
                    continue;
                }

                if (source == null || candidate.thingIDNumber < source.thingIDNumber)
                    source = candidate;
            }
            return source;
        }

        private bool TryConvert(Pawn source)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(ReplicatorBurrowerUtility.BurrowerDefName);
            if (source == null || source.Dead || !source.Spawned || source.Map != map || source.Faction == null || kind == null)
                return false;

            Pawn burrower = null;
            try
            {
                burrower = PawnGenerator.GeneratePawn(kind, source.Faction);
                ReplicatorDomainUtility.CopyDomain(source, burrower);
                TemporaryAsuranIntrusionUtility.CopyState(source, burrower);
                ReplicatorSovereignControlUtility.CopyState(source, burrower);
                burrower.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(source.TryGetComp<CompReplicatorAdaptation>());
                burrower.TryGetComp<CompReplicatorMaterialProfile>()?.InheritFrom(source.TryGetComp<CompReplicatorMaterialProfile>());

                if (!GenPlace.TryPlaceThing(
                        burrower,
                        source.Position,
                        map,
                        ThingPlaceMode.Near,
                        null,
                        cell => !ReplicatorContainmentUtility.IsContained(map, cell)))
                {
                    if (!burrower.Destroyed)
                        burrower.Destroy(DestroyMode.Vanish);
                    return false;
                }

                source.Destroy(DestroyMode.Vanish);
                if (!source.Destroyed)
                {
                    if (!burrower.Destroyed)
                        burrower.Destroy(DestroyMode.Vanish);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Replicator Burrower formation failed: " + ex);
                if (burrower != null && !burrower.Destroyed)
                    burrower.Destroy(DestroyMode.Vanish);
                return false;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextFormationCheckTick, "wngReplicatorBurrowerNextFormationCheck", 0);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }

    /// <summary>
    /// Structural priority layer only. It never hunts pawns. An active containment projector cannot
    /// be assimilated through its own field, so an outside Burrower uses RimWorld's native melee
    /// attack against that physical countermeasure. Other reachable structural blockers use the
    /// existing atomic assimilation transaction when population room exists, otherwise native melee
    /// breaching still opens access without creating free offspring.
    /// </summary>
    public sealed class JobGiver_ReplicatorBurrowerBreach : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ReplicatorBurrowerUtility.CanOperate(pawn))
                return null;

            ReplicatorBlockExtension ext = ReplicatorAssimilationUtility.ExtensionFor(pawn);
            float radius = Math.Max(5f, ext?.assimilationSearchRadius ?? 60f);
            Thing target = FindPriorityStructure(pawn, radius);
            if (target == null)
                return null;

            bool containment = ReplicatorBurrowerUtility.IsActiveHostileContainmentProjector(pawn, target);
            int offspring = Math.Max(1, ext?.assimilationOffspringCount ?? 2);
            if (!containment && ReplicatorAssimilationUtility.IsAssimilationTarget(target, pawn) &&
                ReplicatorAssimilationUtility.HasPopulationRoom(pawn, offspring))
            {
                JobDef assimilate = DefDatabase<JobDef>.GetNamedSilentFail("WNG_ReplicatorAssimilate");
                if (assimilate != null && pawn.CanReserve(target))
                    return JobMaker.MakeJob(assimilate, target);
            }

            Job breach = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            breach.expiryInterval = 900;
            return breach;
        }

        private static Thing FindPriorityStructure(Pawn pawn, float radius)
        {
            IReadOnlyList<Thing> buildings = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            Thing best = null;
            int bestScore = int.MinValue;
            int bestDistance = int.MaxValue;
            float radiusSq = radius * radius;

            for (int i = 0; i < buildings.Count; i++)
            {
                Thing candidate = buildings[i];
                if (candidate == null || candidate.Destroyed || !candidate.Spawned || candidate == pawn ||
                    candidate.Faction == pawn.Faction || pawn.Position.DistanceToSquared(candidate.Position) > radiusSq)
                    continue;

                bool containment = ReplicatorBurrowerUtility.IsActiveHostileContainmentProjector(pawn, candidate);
                bool structural = ReplicatorBurrowerUtility.IsStructuralBlocker(candidate);
                if (!containment && !structural)
                    continue;
                if (!pawn.CanReach(candidate, PathEndMode.Touch, Danger.Deadly))
                    continue;

                int score = containment ? 10000 : ReplicatorBurrowerUtility.StructuralPriority(candidate);
                int distance = pawn.Position.DistanceToSquared(candidate.Position);
                if (best == null || score > bestScore || (score == bestScore &&
                    (distance < bestDistance || (distance == bestDistance && candidate.thingIDNumber < best.thingIDNumber))))
                {
                    best = candidate;
                    bestScore = score;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
