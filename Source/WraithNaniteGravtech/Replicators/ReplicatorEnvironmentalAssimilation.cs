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
    /// Tracks the physical stripping of a map by autonomous block Replicators.
    /// A cell counts as consumed only after its roof/floor/ground layer has been processed.
    /// Biological predation remains locked until at least 95% of the map's initially consumable
    /// cells have been stripped.
    /// </summary>
    public sealed class MapComponent_ReplicatorConsumption : MapComponent
    {
        public const float BiologicalPredationThreshold = 0.95f;
        public const int CellMatterPerDrone = 4;

        private List<int> strippedCellIndices = new List<int>();
        private HashSet<int> strippedLookup = new HashSet<int>();
        private int initialConsumableCellCount = -1;
        private int storedCellMatter;

        public MapComponent_ReplicatorConsumption(Map map) : base(map)
        {
        }

        public int InitialConsumableCellCount
        {
            get
            {
                EnsureInitialCount();
                return initialConsumableCellCount;
            }
        }

        public int StrippedCellCount => strippedLookup?.Count ?? 0;

        public float StrippedFraction
        {
            get
            {
                int initial = InitialConsumableCellCount;
                return initial <= 0 ? 1f : Math.Min(1f, StrippedCellCount / (float)initial);
            }
        }

        public bool BiologicalPredationUnlocked =>
            WNGSettingsUtility.ReplicatorTerrainAssimilationEnabled &&
            InitialConsumableCellCount > 0 &&
            StrippedFraction >= WNGSettingsUtility.ReplicatorBiologicalPredationThreshold;

        public bool IsStripped(IntVec3 cell)
        {
            if (!cell.InBounds(map))
                return true;
            EnsureLookup();
            return strippedLookup.Contains(map.cellIndices.CellToIndex(cell));
        }

        public void MarkStripped(IntVec3 cell)
        {
            if (!cell.InBounds(map))
                return;

            EnsureInitialCount();
            EnsureLookup();
            int index = map.cellIndices.CellToIndex(cell);
            if (strippedLookup.Add(index))
                strippedCellIndices.Add(index);
        }

        public void AddStoredCellMatter(int amount)
        {
            if (amount > 0)
                storedCellMatter = Math.Max(0, storedCellMatter + amount);
        }

        public bool CanMaterializeDrone => storedCellMatter >= CellMatterPerDrone;

        public bool SpendDroneMatter()
        {
            if (!CanMaterializeDrone)
                return false;
            storedCellMatter -= CellMatterPerDrone;
            return true;
        }

        private void EnsureInitialCount()
        {
            if (initialConsumableCellCount >= 0)
                return;

            int count = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                bool isVoid = terrain == null ||
                              string.Equals(terrain.defName, "Space", StringComparison.OrdinalIgnoreCase);
                if (!isVoid || map.roofGrid.Roofed(cell) || cell.GetThingList(map).Any(IsPhysicalEnvironmentThing))
                    count++;
            }

            initialConsumableCellCount = Math.Max(1, count);
        }

        private static bool IsPhysicalEnvironmentThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing is Pawn || thing is Corpse)
                return false;
            if (thing.def == null)
                return false;

            ThingCategory category = thing.def.category;
            return category == ThingCategory.Item ||
                   category == ThingCategory.Building ||
                   category == ThingCategory.Plant;
        }

        private void EnsureLookup()
        {
            if (strippedLookup == null)
                strippedLookup = new HashSet<int>();
            if (strippedLookup.Count == 0 && strippedCellIndices != null && strippedCellIndices.Count > 0)
                strippedLookup.UnionWith(strippedCellIndices);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref strippedCellIndices, "wngReplicatorStrippedCells", LookMode.Value);
            Scribe_Values.Look(ref initialConsumableCellCount, "wngReplicatorInitialConsumableCells", -1);
            Scribe_Values.Look(ref storedCellMatter, "wngReplicatorStoredCellMatter", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                strippedCellIndices ??= new List<int>();
                strippedLookup = new HashSet<int>(strippedCellIndices);
                storedCellMatter = Math.Max(0, storedCellMatter);
            }
        }
    }

    public static class ReplicatorEnvironmentalAssimilationUtility
    {
        // Throttle negative tile searches only. This preserves any available
        // structural/terrain target while limiting repeated expensive searches
        // on fully consumed maps populated by many hostile Replicators.
        private static readonly Dictionary<int, int> nextEmptyStructuralScan = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> nextEmptyTerrainScan = new Dictionary<int, int>();

        public static bool IsConsumableCell(Pawn pawn, IntVec3 cell)
        {
            if (!ReplicatorAssimilationUtility.CanAutonomouslyAssimilate(pawn) ||
                pawn?.Map == null ||
                !cell.InBounds(pawn.Map))
            {
                return false;
            }

            Map map = pawn.Map;
            MapComponent_ReplicatorConsumption state = map.GetComponent<MapComponent_ReplicatorConsumption>();
            if (state == null || state.IsStripped(cell))
                return false;

            if (ReplicatorContainmentUtility.IsContained(map, cell))
                return false;

            // Roofing outranks physical Things on the same cell. If a Replicator ate a
            // roof-holding wall/rock first, vanilla would schedule unsupported roofs to collapse
            // and thick rock roof would spawn CollapsedRocks. The swarm consumes the roof first.
            if (WNGSettingsUtility.ReplicatorRoofAssimilationEnabled)
            {
                if (map.roofGrid.Roofed(cell))
                    return true;
            }

            // Once the roof is gone, physical Things outrank floor/foundation/ground substrate.
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (ReplicatorAssimilationUtility.IsAssimilationTarget(things[i], pawn))
                    return false;
            }

            if (!WNGSettingsUtility.ReplicatorTerrainAssimilationEnabled)
                return false;

            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            bool isVoid = terrain == null ||
                          string.Equals(terrain.defName, "Space", StringComparison.OrdinalIgnoreCase);

            return map.terrainGrid.CanRemoveTopLayerAt(cell) ||
                   map.terrainGrid.CanRemoveFoundationAt(cell) ||
                   !isVoid;
        }

        /// <summary>
        /// Snapshot vanilla's pending roof-collapse buffer before a Replicator destroys a support.
        /// Any new cells vanilla marks as unsupported because of that destruction are converted
        /// into direct roof assimilation before the collapse resolver can spawn rubble.
        /// </summary>
        public static HashSet<IntVec3> CapturePendingRoofCollapses(Map map)
        {
            if (map?.roofCollapseBuffer?.CellsMarkedToCollapse == null)
                return new HashSet<IntVec3>();

            return new HashSet<IntVec3>(map.roofCollapseBuffer.CellsMarkedToCollapse);
        }

        public static int AssimilateNewlyUnsupportedRoofs(
            Map map,
            HashSet<IntVec3> pendingBefore)
        {
            if (!WNGSettingsUtility.ReplicatorRoofAssimilationEnabled ||
                map?.roofCollapseBuffer?.CellsMarkedToCollapse == null)
                return 0;

            pendingBefore ??= new HashSet<IntVec3>();
            List<IntVec3> pending = map.roofCollapseBuffer.CellsMarkedToCollapse;
            int assimilated = 0;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                IntVec3 cell = pending[i];
                if (pendingBefore.Contains(cell))
                    continue;

                if (cell.InBounds(map) && map.roofGrid.Roofed(cell))
                {
                    // SetRoof(null) removes constructed, thin-rock and thick-rock roofs directly.
                    // It deliberately bypasses RoofCollapserImmediate, so RoofRockThick cannot
                    // create CollapsedRocks as a side effect of Replicator feeding.
                    map.roofGrid.SetRoof(cell, null);
                    FilthMaker.RemoveAllFilth(cell, map);
                    assimilated++;
                }

                pending.RemoveAt(i);
            }

            return assimilated;
        }

        public static bool HasStructuralLayer(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
                return false;

            // Roofs and removable top terrain layers are the visible built-environment layers
            // the swarm should strip even while ordinary objects still remain elsewhere.
            return (WNGSettingsUtility.ReplicatorRoofAssimilationEnabled &&
                    map.roofGrid.Roofed(cell)) ||
                   (WNGSettingsUtility.ReplicatorTerrainAssimilationEnabled &&
                    (map.terrainGrid.CanRemoveTopLayerAt(cell) ||
                     map.terrainGrid.CanRemoveFoundationAt(cell)));
        }

        public static IntVec3 FindClosestConsumableCell(Pawn pawn, bool structuralLayersOnly = false)
        {
            if (!ReplicatorAssimilationUtility.CanAutonomouslyAssimilate(pawn) || pawn?.Map == null)
                return IntVec3.Invalid;

            int now = Find.TickManager?.TicksGame ?? 0;
            Dictionary<int, int> negative = structuralLayersOnly
                ? nextEmptyStructuralScan : nextEmptyTerrainScan;
            int nextAllowed;
            if (negative.TryGetValue(pawn.thingIDNumber, out nextAllowed) &&
                now >= 0 && now < nextAllowed)
                return IntVec3.Invalid;

            ReplicatorBlockExtension ext = ReplicatorAssimilationUtility.ExtensionFor(pawn);
            float radius = Math.Max(5f, ext?.assimilationSearchRadius ?? 60f);

            IntVec3 best = IntVec3.Invalid;
            float bestDistance = float.MaxValue;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, radius, useCenter: true))
            {
                // Geometric distance is cheap; do not run terrain/roof tests and
                // a pathfinder search for candidates worse than an existing hit.
                float distance = pawn.Position.DistanceToSquared(cell);
                if (distance >= bestDistance)
                    continue;
                if (!IsConsumableCell(pawn, cell))
                    continue;
                if (structuralLayersOnly && !HasStructuralLayer(pawn.Map, cell))
                    continue;
                if (!pawn.CanReach(cell, PathEndMode.Touch, Danger.Deadly))
                    continue;

                if (distance < bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                    if (distance <= 1f)
                        break;
                }
            }

            if (!best.IsValid)
            {
                if (negative.Count > 2048)
                    negative.Clear();
                negative[pawn.thingIDNumber] = now + 120;
            }
            else
                negative.Remove(pawn.thingIDNumber);
            return best;
        }

        public static bool TryCommitCellAssimilation(Pawn pawn, IntVec3 cell)
        {
            if (!IsConsumableCell(pawn, cell) || pawn?.Map == null)
                return false;

            Map map = pawn.Map;
            MapComponent_ReplicatorConsumption state = map.GetComponent<MapComponent_ReplicatorConsumption>();
            if (state == null)
                return false;

            try
            {
                bool removedRoof =
                    WNGSettingsUtility.ReplicatorRoofAssimilationEnabled &&
                    map.roofGrid.Roofed(cell);
                if (removedRoof)
                    map.roofGrid.SetRoof(cell, null);

                bool removedStructuralTerrain = false;

                if (WNGSettingsUtility.ReplicatorTerrainAssimilationEnabled)
                {
                    if (map.terrainGrid.CanRemoveTopLayerAt(cell))
                    {
                        map.terrainGrid.RemoveTopLayer(cell, doLeavings: false);
                        removedStructuralTerrain = true;
                    }

                    // Odyssey gravship substructure (including WNG family substructures) lives in the
                    // foundation grid, not the ordinary top/under terrain grid. Consume that layer too.
                    if (map.terrainGrid.CanRemoveFoundationAt(cell))
                    {
                        map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                        removedStructuralTerrain = true;
                    }

                    if (!removedStructuralTerrain)
                    {
                        TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                        bool isVoid = terrain == null ||
                                      string.Equals(terrain.defName, "Space", StringComparison.OrdinalIgnoreCase);
                        if (!isVoid && terrain != TerrainDefOf.Gravel)
                        {
                            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
                            removedStructuralTerrain = true;
                        }
                    }
                }

                if (!removedRoof && !removedStructuralTerrain)
                    return false;

                FilthMaker.RemoveAllFilth(cell, map);
                state.MarkStripped(cell);
                state.AddStoredCellMatter(1);

                while (state.CanMaterializeDrone &&
                       ReplicatorAssimilationUtility.HasPopulationRoom(pawn, 1))
                {
                    if (!TrySpawnDroneFromCellMatter(pawn, cell))
                        break;
                    if (!state.SpendDroneMatter())
                        break;
                }

                try
                {
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_ReplicatorAssimilate")
                        ?.PlayOneShot(new TargetInfo(cell, map));
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Replicator substrate-assimilation sound failed after commit: " + ex.Message);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Replicator substrate assimilation failed: " + ex);
                return false;
            }
        }

        private static bool TrySpawnDroneFromCellMatter(Pawn parent, IntVec3 origin)
        {
            if (parent?.Map == null || parent.Faction == null ||
                !ReplicatorAssimilationUtility.HasPopulationRoom(parent, 1))
            {
                return false;
            }

            PawnKindDef droneKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_ReplicatorDrone");
            if (droneKind == null)
                return false;

            Pawn child = null;
            try
            {
                child = PawnGenerator.GeneratePawn(droneKind, parent.Faction);
                if (child == null)
                    return false;

                ReplicatorDomainUtility.CopyDomain(parent, child);
                TemporaryAsuranIntrusionUtility.CopyState(parent, child);
                ReplicatorSovereignControlUtility.CopyState(parent, child);
                child.TryGetComp<CompReplicatorAdaptation>()
                    ?.InheritFrom(parent.TryGetComp<CompReplicatorAdaptation>());
                child.TryGetComp<CompReplicatorMaterialProfile>()
                    ?.SetGrade(parent.TryGetComp<CompReplicatorMaterialProfile>()?.Grade ?? ReplicatorMaterialGrade.Standard);

                if (!GenPlace.TryPlaceThing(child, origin, parent.Map, ThingPlaceMode.Near))
                {
                    if (!child.Destroyed)
                        child.Destroy(DestroyMode.Vanish);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (child != null && !child.Destroyed)
                    child.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Replicator cell-matter Drone creation failed: " + ex);
                return false;
            }
        }
    }

    public sealed class JobGiver_ReplicatorAssimilateStructuralCell : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            IntVec3 cell = ReplicatorEnvironmentalAssimilationUtility.FindClosestConsumableCell(
                pawn,
                structuralLayersOnly: true);
            if (!cell.IsValid)
                return null;

            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("WNG_ReplicatorAssimilateCell");
            return def == null ? null : JobMaker.MakeJob(def, cell);
        }
    }

    public sealed class JobGiver_ReplicatorAssimilateCell : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            IntVec3 cell = ReplicatorEnvironmentalAssimilationUtility.FindClosestConsumableCell(pawn);
            if (!cell.IsValid)
                return null;

            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("WNG_ReplicatorAssimilateCell");
            return def == null ? null : JobMaker.MakeJob(def, cell);
        }
    }

    public sealed class JobDriver_ReplicatorAssimilateCell : JobDriver
    {
        private IntVec3 TargetCell => job.targetA.Cell;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() =>
                !ReplicatorEnvironmentalAssimilationUtility.IsConsumableCell(pawn, TargetCell));

            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);

            ReplicatorBlockExtension ext = ReplicatorAssimilationUtility.ExtensionFor(pawn);
            int baseDuration = Math.Max(90, ext?.assimilationTicks ?? 300);
            float adaptationFactor =
                pawn.TryGetComp<CompReplicatorAdaptationEffects>()?.AssimilationTimeFactor ?? 1f;
            float coordinationFactor =
                pawn.Map?.GetComponent<MapComponent_ReplicatorCoordination>()?.AssimilationFactorFor(pawn) ?? 1f;
            int duration = Math.Max(90, (int)Math.Round(baseDuration * 0.65f * adaptationFactor * coordinationFactor));

            Toil work = ToilMaker.MakeToil("WNG_ReplicatorAssimilateCell");
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = duration;
            yield return work;

            Toil finish = ToilMaker.MakeToil("WNG_ReplicatorAssimilateCellFinish");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            finish.initAction = delegate
            {
                ReplicatorEnvironmentalAssimilationUtility.TryCommitCellAssimilation(pawn, TargetCell);
            };
            yield return finish;
        }
    }
}
