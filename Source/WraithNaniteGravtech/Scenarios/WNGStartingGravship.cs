using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.SketchGen;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Replaces Odyssey's hard-coded steel starter gravship for WNG orbital scenarios.
    /// The ship is a compact, fully connected family craft: immediately flyable, but without
    /// late-game shields, weapons, scanners, heavy drives, extenders or other progression systems.
    /// </summary>
    public sealed class ScenPart_WNGStartingGravship : ScenPart
    {
        public WNGGravshipFamily family = WNGGravshipFamily.Wraith;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref family, "family", WNGGravshipFamily.Wraith);
        }

        public override string Summary(Scenario scen)
        {
            if (family == WNGGravshipFamily.Wraith)
                return "Start aboard a complete Wraith living gravship.";
            if (family == WNGGravshipFamily.Asuran)
                return "Start aboard a complete Asuran gravship.";
            return "Start aboard a family gravship.";
        }

        public override void GenerateIntoMap(Map map)
        {
            if (Find.GameInitData == null)
                return;

            List<Thing> startingItems = GatherStartingItems();

            if (!ModsConfig.OdysseyActive)
            {
                Log.Error("[WNG] A WNG orbital scenario was started without Odyssey active. Falling back to Odyssey-style drop placement.");
                SpawnFallbackDropPods(map, startingItems);
                return;
            }

            WNGStarterGravshipSpec spec = WNGStarterGravshipSpec.For(family);
            if (spec == null)
            {
                Log.Error("[WNG] Family orbital starter validation failed: unsupported family " + family +
                          ". Falling back to Odyssey's vanilla starter gravship.");
                SpawnVanillaGravshipFallback(map, startingItems);
                return;
            }

            string problem;
            if (!spec.ResolveAndValidate(out problem))
            {
                Log.Error("[WNG] Family orbital starter validation failed: " + problem +
                          ". Falling back to Odyssey's vanilla starter gravship.");
                SpawnVanillaGravshipFallback(map, startingItems);
                return;
            }

            Sketch sketch = BuildFamilyStarterSketch(spec);
            SpawnSketchWithContents(spec, sketch, map, startingItems);
        }

        public override void PostMapGenerate(Map map)
        {
            if (Find.GameInitData == null || !ModsConfig.OdysseyActive)
                return;

            WNGStarterGravshipSpec spec = WNGStarterGravshipSpec.For(family);
            string problem;
            if (spec != null && spec.ResolveAndValidate(out problem))
                ValidateLiveStarterShip(spec, map);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode() ^ family.GetHashCode();
        }

        private static List<Thing> GatherStartingItems()
        {
            List<Thing> items = new List<Thing>();

            foreach (ScenPart part in Find.Scenario.AllParts)
                items.AddRange(part.PlayerStartingThings());

            foreach (Pawn pawn in Find.GameInitData.startingAndOptionalPawns)
            {
                foreach (ThingDefCount possession in Find.GameInitData.startingPossessions[pawn])
                    items.Add(StartingPawnUtility.GenerateStartingPossession(possession));
            }

            return items;
        }

        private static Sketch BuildFamilyStarterSketch(WNGStarterGravshipSpec spec)
        {
            Sketch sketch = new Sketch();

            // This is Odyssey's proven 10x9 starter footprint.  Keeping its shape means the two
            // small drives retain the same safe exterior/exclusion geometry as the vanilla craft.
            for (int z = 0; z < WNGStarterGravshipSpec.BaseStructure.Length; z++)
            {
                int[] row = WNGStarterGravshipSpec.BaseStructure[z];
                for (int x = 0; x < row.Length; x++)
                {
                    int cellType = row[x];
                    if (cellType == 0)
                        continue;

                    IntVec3 cell = new IntVec3(x, 0, z);
                    sketch.AddTerrain(spec.Substructure, cell);

                    if (cellType == 2)
                        sketch.AddThing(spec.Hull, cell, Rot4.North);
                    else if (cellType == 3)
                        sketch.AddThing(spec.Door, cell, Rot4.North);

                    // Family power is intentionally ship-wide.  WNG conduits are real non-edifice
                    // Conduits-layer networks, so they coexist beneath hull walls and equipment.
                    sketch.AddThing(spec.PowerConduit, cell, Rot4.North);
                }
            }

            // Spawn the engine before facilities, exactly as Odyssey's own resolver does.
            sketch.AddThing(
                spec.Engine,
                new IntVec3(7, 0, 4),
                Rot4.North,
                stuff: null,
                stackCount: 1,
                quality: null,
                hitPoints: null,
                wipeIfCollides: true,
                spawnOrder: 0.5f);

            // Flight-critical systems use the same geometry as Odyssey's starter craft.
            sketch.AddThing(spec.FuelTank, new IntVec3(6, 0, 1), Rot4.North);
            sketch.AddThing(spec.FuelTank, new IntVec3(6, 0, 6), Rot4.North);
            sketch.AddThing(spec.Thruster, new IntVec3(0, 0, 1), Rot4.East);
            sketch.AddThing(spec.Thruster, new IntVec3(0, 0, 7), Rot4.East);
            sketch.AddThing(spec.Pilot, new IntVec3(4, 0, 4), Rot4.East);

            // Only starter-grade family support.  No shields, weapons, scanners, extenders,
            // signal jammers, fuel optimizers or heavy drives are granted here.
            sketch.AddThing(spec.PowerGenerator, new IntVec3(2, 0, 2), Rot4.North);
            sketch.AddThing(spec.Atmosphere, new IntVec3(2, 0, 6), Rot4.North);
            sketch.AddThing(spec.NavigationAssist, new IntVec3(4, 0, 1), Rot4.North);

            // One physical family fuel loop joins both tanks to both drives.
            HashSet<IntVec3> fuelCells = new HashSet<IntVec3>();
            AddFuelLine(fuelCells, new IntVec3(0, 0, 1), new IntVec3(6, 0, 1));
            AddFuelLine(fuelCells, new IntVec3(0, 0, 7), new IntVec3(6, 0, 7));
            AddFuelLine(fuelCells, new IntVec3(6, 0, 1), new IntVec3(6, 0, 7));

            foreach (IntVec3 cell in fuelCells)
                sketch.AddThing(spec.FuelConduit, cell, Rot4.North);

            return sketch;
        }

        private static void AddFuelLine(HashSet<IntVec3> cells, IntVec3 from, IntVec3 to)
        {
            int x = from.x;
            int z = from.z;
            cells.Add(new IntVec3(x, 0, z));

            while (x != to.x)
            {
                x += Math.Sign(to.x - x);
                cells.Add(new IntVec3(x, 0, z));
            }

            while (z != to.z)
            {
                z += Math.Sign(to.z - z);
                cells.Add(new IntVec3(x, 0, z));
            }
        }

        private static void SpawnSketchWithContents(
            WNGStarterGravshipSpec spec,
            Sketch sketch,
            Map map,
            List<Thing> startingItems)
        {
            sketch.Rotate(Rot4.Random);

            HashSet<IntVec3> occupied = new HashSet<IntVec3>();
            foreach (IntVec3 c in sketch.OccupiedRect.Cells)
                occupied.Add(c - sketch.OccupiedCenter);

            List<CellRect> usedRects = MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");
            map.regionAndRoomUpdater.Enabled = true;

            IntVec3 startSpot = MapGenerator.PlayerStartSpot;
            if (!MapGenerator.PlayerStartSpotValid)
            {
                GenStep_ReserveGravshipArea.SetStartSpot(map, occupied, usedRects);
                startSpot = MapGenerator.PlayerStartSpot;
            }

            GravshipPlacementUtility.ClearAreaForGravship(map, startSpot, occupied);

            List<Thing> spawned = new List<Thing>();
            sketch.Spawn(
                map,
                startSpot,
                Faction.OfPlayer,
                Sketch.SpawnPosType.OccupiedCenter,
                Sketch.SpawnMode.Normal,
                wipeIfCollides: true,
                forceTerrainAffordance: true,
                clearEdificeWhereFloor: true,
                spawnedThings: spawned,
                dormant: false,
                buildRoofsInstantly: true);

            IntVec3 offset = startSpot - sketch.OccupiedCenter;
            CellRect shipRect = sketch.OccupiedRect.MovedBy(offset);
            usedRects.Add(shipRect);

            InitializeFamilyShip(spec, spawned);
            PlaceStartingPawns(spec, map, shipRect);
            PlaceStartingItems(spec, map, shipRect, startingItems);
            UnfogFamilyDoors(spec, spawned);
            MarkShipHomeArea(spec, map, shipRect);

            // Facilities spawn after the engine, but one final relink guarantees that every
            // family component sees the complete finished craft regardless of entity spawn order.
            RelinkFamilyFacilities(spec, spawned);
        }

        private static void InitializeFamilyShip(WNGStarterGravshipSpec spec, List<Thing> spawned)
        {
            foreach (Thing thing in spawned)
            {
                if (thing.def.CanHaveFaction && thing.Faction != Faction.OfPlayer)
                    thing.SetFactionDirect(Faction.OfPlayer);

                CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                if (refuelable != null)
                    refuelable.Refuel(refuelable.Props.fuelCapacity);

                CompFlickable flickable = thing.TryGetComp<CompFlickable>();
                if (flickable != null)
                    flickable.ResetToOn();

                Building_GravEngine engine = thing as Building_GravEngine;
                if (engine != null && engine.def == spec.Engine)
                    engine.silentlyActivate = true;
            }
        }

        private static void RelinkFamilyFacilities(WNGStarterGravshipSpec spec, List<Thing> spawned)
        {
            Building_GravEngine engine = spawned
                .OfType<Building_GravEngine>()
                .FirstOrDefault(e => e.def == spec.Engine);

            if (engine == null)
                return;

            CompAffectedByFacilities affected = engine.TryGetComp<CompAffectedByFacilities>();
            affected?.Notify_ThingChanged();

            foreach (Thing thing in spawned)
            {
                CompGravshipFacility facility = thing.TryGetComp<CompGravshipFacility>();
                if (facility != null)
                    facility.Notify_ThingChanged();
            }

            affected?.Notify_ThingChanged();
        }

        private static void PlaceStartingPawns(WNGStarterGravshipSpec spec, Map map, CellRect shipRect)
        {
            List<IntVec3> available = UsableInteriorCells(spec, map, shipRect)
                .InRandomOrder()
                .ToList();

            foreach (Pawn pawn in Find.GameInitData.startingAndOptionalPawns)
            {
                IntVec3 cell;
                if (available.Count == 0)
                {
                    cell = shipRect.CenterCell;
                }
                else
                {
                    cell = available[available.Count - 1];
                    available.RemoveAt(available.Count - 1);
                }

                if (!TryPlaceStarterThing(pawn, cell, map, shipRect))
                    Log.Error("[WNG] Could not place starting pawn " + pawn.Name + " anywhere on the generated starter map.");
            }
        }

        private static void PlaceStartingItems(
            WNGStarterGravshipSpec spec,
            Map map,
            CellRect shipRect,
            List<Thing> startingItems)
        {
            List<IntVec3> cargoCells = UsableInteriorCells(spec, map, shipRect)
                .OrderBy(c => c.x)
                .ThenBy(c => c.z)
                .ToList();

            if (cargoCells.Count == 0)
                cargoCells.Add(shipRect.CenterCell);

            int cargoIndex = 0;
            foreach (Thing startingItem in startingItems)
            {
                if (startingItem == null || startingItem.Destroyed)
                    continue;

                if (startingItem.def.CanHaveFaction)
                    startingItem.SetFactionDirect(Faction.OfPlayer);

                int remaining = startingItem.stackCount;
                int safety = 256;

                while (remaining > 0 && safety-- > 0)
                {
                    int count = Math.Min(startingItem.def.stackLimit, remaining);
                    Thing piece = startingItem.SplitOff(count);
                    remaining -= piece.stackCount;

                    IntVec3 target = cargoCells[cargoIndex % cargoCells.Count];
                    cargoIndex++;

                    if (!TryPlaceStarterThing(piece, target, map, shipRect))
                    {
                        Log.Warning("[WNG] Could not place starter cargo " + piece.LabelCap +
                                    " anywhere on the generated starter map.");
                    }
                }
            }
        }

        private static bool TryPlaceStarterThing(Thing thing, IntVec3 preferred, Map map, CellRect shipRect)
        {
            if (thing == null || thing.Destroyed || map == null)
                return false;

            if (GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near))
                return true;

            IntVec3 fallback;
            if (shipRect.TryRandomElement(c => c.InBounds(map) && c.Standable(map), out fallback) &&
                GenPlace.TryPlaceThing(thing, fallback, map, ThingPlaceMode.Near))
                return true;

            IntVec3 center = map.Center;
            return center.InBounds(map) &&
                   GenPlace.TryPlaceThing(thing, center, map, ThingPlaceMode.Near);
        }

        private static IEnumerable<IntVec3> UsableInteriorCells(
            WNGStarterGravshipSpec spec,
            Map map,
            CellRect shipRect)
        {
            foreach (IntVec3 cell in shipRect)
            {
                if (!cell.InBounds(map) ||
                    map.terrainGrid.FoundationAt(cell) != spec.Substructure ||
                    !cell.Standable(map))
                    continue;

                bool blocked = false;
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                {
                    if (thing.def == spec.PowerConduit || thing.def == spec.FuelConduit)
                        continue;

                    if (thing.def.category == ThingCategory.Building &&
                        thing.def.passability != Traversability.Standable)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                    yield return cell;
            }
        }

        private static void UnfogFamilyDoors(WNGStarterGravshipSpec spec, List<Thing> spawned)
        {
            foreach (Thing thing in spawned)
            {
                if (thing.def == spec.Door)
                    MapGenerator.rootsToUnfog.AddRange(GenAdj.CellsAdjacentCardinal(thing));
            }
        }

        private static void MarkShipHomeArea(WNGStarterGravshipSpec spec, Map map, CellRect shipRect)
        {
            foreach (IntVec3 cell in shipRect)
            {
                if (cell.InBounds(map) && map.terrainGrid.FoundationAt(cell) == spec.Substructure)
                    map.areaManager.Home[cell] = true;
            }
        }

        private static void ValidateLiveStarterShip(WNGStarterGravshipSpec spec, Map map)
        {
            List<Thing> engines = map.listerThings.ThingsOfDef(spec.Engine)
                .Where(t => t.Faction == Faction.OfPlayer)
                .ToList();

            if (engines.Count != 1)
            {
                Log.Error("[WNG] Orbital starter integrity check: expected one " + spec.Engine.defName +
                          ", found " + engines.Count + ".");
                return;
            }

            Building_GravEngine engine = engines[0] as Building_GravEngine;
            if (engine == null)
            {
                Log.Error("[WNG] Orbital starter integrity check: family engine is not a Building_GravEngine.");
                return;
            }

            Thing pilotThing = map.listerThings.ThingsOfDef(spec.Pilot)
                .FirstOrDefault(t => t.Faction == Faction.OfPlayer);
            CompGravshipFacility pilot = pilotThing?.TryGetComp<CompGravshipFacility>();
            if (pilot == null || pilot.engine != engine || !pilot.LinkedBuildings.Contains(engine))
                Log.Error("[WNG] Orbital starter integrity check: pilot control is not linked to its family grav engine.");

            List<Thing> drives = map.listerThings.ThingsOfDef(spec.Thruster)
                .Where(t => t.Faction == Faction.OfPlayer)
                .ToList();
            if (drives.Count != 2)
                Log.Error("[WNG] Orbital starter integrity check: expected two starter drives, found " + drives.Count + ".");

            foreach (Thing drive in drives)
            {
                CompGravshipFacility facility = drive.TryGetComp<CompGravshipFacility>();
                if (facility == null || facility.engine != engine || !facility.LinkedBuildings.Contains(engine))
                    Log.Error("[WNG] Orbital starter integrity check: a family drive is not linked to the family engine.");

                if (!WNGFamilyFuelUtility.HasRoleConnection(drive, spec.Family, WNGFuelEndpointRole.Tank))
                    Log.Error("[WNG] Orbital starter integrity check: a family drive has no connected fuel tank.");
            }

            foreach (Thing tank in map.listerThings.ThingsOfDef(spec.FuelTank)
                .Where(t => t.Faction == Faction.OfPlayer))
            {
                CompRefuelable fuel = tank.TryGetComp<CompRefuelable>();
                if (fuel == null || !fuel.HasFuel)
                    Log.Error("[WNG] Orbital starter integrity check: a starter fuel tank is empty.");
                if (!WNGFamilyFuelUtility.HasRoleConnection(tank, spec.Family, WNGFuelEndpointRole.Thruster))
                    Log.Error("[WNG] Orbital starter integrity check: a starter fuel tank has no connected drive.");
            }

            Thing generator = map.listerThings.ThingsOfDef(spec.PowerGenerator)
                .FirstOrDefault(t => t.Faction == Faction.OfPlayer);
            if (generator == null)
            {
                Log.Error("[WNG] Orbital starter integrity check: family power generator missing.");
            }
            else
            {
                List<CompWNGFamilyPowerNode> network =
                    WNGFamilyPowerUtility.NetworkNodes(generator, spec.Family);
                float supply = WNGFamilyPowerUtility.NetworkSupply(network);
                float demand = WNGFamilyPowerUtility.NetworkDemand(network);
                if (network.Count == 0 || supply + 0.01f < demand)
                    Log.Error("[WNG] Orbital starter integrity check: family power network is under-supplied (" +
                              supply.ToString("0") + " / " + demand.ToString("0") + " W).");
            }

            CompAffectedByFacilities affected = engine.TryGetComp<CompAffectedByFacilities>();
            if (affected == null)
                Log.Error("[WNG] Orbital starter integrity check: family engine lost CompAffectedByFacilities.");
        }

        private static void SpawnVanillaGravshipFallback(Map map, List<Thing> startingItems)
        {
            SketchResolveParams parms = default(SketchResolveParams);
            parms.sketch = new Sketch();
            Sketch sketch = RimWorld.SketchGen.SketchGen.Generate(SketchResolverDefOf.Gravship, parms);
            sketch.Rotate(Rot4.Random);

            HashSet<IntVec3> occupied = new HashSet<IntVec3>();
            foreach (IntVec3 c in sketch.OccupiedRect.Cells)
                occupied.Add(c - sketch.OccupiedCenter);

            List<CellRect> usedRects = MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");
            map.regionAndRoomUpdater.Enabled = true;
            IntVec3 startSpot = MapGenerator.PlayerStartSpot;

            if (!MapGenerator.PlayerStartSpotValid)
            {
                GenStep_ReserveGravshipArea.SetStartSpot(map, occupied, usedRects);
                startSpot = MapGenerator.PlayerStartSpot;
            }

            GravshipPlacementUtility.ClearAreaForGravship(map, startSpot, occupied);
            List<Thing> spawned = new List<Thing>();
            sketch.Spawn(
                map, startSpot, Faction.OfPlayer, Sketch.SpawnPosType.OccupiedCenter,
                Sketch.SpawnMode.Normal, true, true, true, spawned, false, true);

            IntVec3 offset = startSpot - sketch.OccupiedCenter;
            CellRect rect = sketch.OccupiedRect.MovedBy(offset);
            usedRects.Add(rect);

            foreach (Pawn pawn in Find.GameInitData.startingAndOptionalPawns)
            {
                IntVec3 cell;
                if (!rect.TryRandomElement(c => c.Standable(map) && (c.GetTerrain(map)?.IsSubstructure ?? false), out cell))
                    cell = rect.CenterCell;
                TryPlaceStarterThing(pawn, cell, map, rect);
            }

            foreach (Thing item in startingItems)
            {
                if (item.def.CanHaveFaction)
                    item.SetFactionDirect(Faction.OfPlayer);

                int remaining = item.stackCount;
                int safety = 99;
                while (remaining > 0 && safety-- > 0)
                {
                    Thing shelf;
                    IntVec3 target = rect.CenterCell;
                    if (spawned.Where(t => t.def == ThingDefOf.Shelf || t.def == ThingDefOf.ShelfSmall)
                        .TryRandomElement(out shelf))
                    {
                        target = shelf.OccupiedRect().RandomCell;
                    }

                    Thing piece = item.SplitOff(Math.Min(item.def.stackLimit, remaining));
                    remaining -= piece.stackCount;
                    TryPlaceStarterThing(piece, target, map, rect);
                }
            }

            foreach (Thing thing in spawned)
            {
                if (thing.def == ThingDefOf.Door)
                    MapGenerator.rootsToUnfog.AddRange(GenAdj.CellsAdjacentCardinal(thing));

                CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                if (refuelable != null)
                    refuelable.Refuel(refuelable.Props.fuelCapacity);

                Building_GravEngine engine = thing as Building_GravEngine;
                if (engine != null)
                    engine.silentlyActivate = true;
            }

            foreach (IntVec3 cell in rect)
            {
                if (cell.GetTerrain(map) == TerrainDefOf.Substructure)
                    map.areaManager.Home[cell] = true;
            }
        }

        private static void SpawnFallbackDropPods(Map map, List<Thing> startingItems)
        {
            List<List<Thing>> groups = new List<List<Thing>>();
            foreach (Pawn pawn in Find.GameInitData.startingAndOptionalPawns)
                groups.Add(new List<Thing> { pawn });

            if (groups.Count == 0)
            {
                // Defensive fallback for unusual custom scenarios with cargo but no starting pawn.
                // Preserve the exact starting items instead of returning before they enter a drop pod.
                if (startingItems == null || startingItems.Count == 0)
                    return;
                groups.Add(new List<Thing>());
            }

            int index = 0;
            foreach (Thing item in startingItems)
            {
                if (item.def.CanHaveFaction)
                    item.SetFactionDirect(Faction.OfPlayer);
                groups[index].Add(item);
                index = (index + 1) % groups.Count;
            }

            DropPodUtility.DropThingGroupsNear(
                MapGenerator.PlayerStartSpot,
                map,
                groups,
                110,
                Find.GameInitData.QuickStarted,
                leaveSlag: true,
                canRoofPunch: true,
                forbid: true,
                allowFogged: false);
        }
    }

    internal sealed class WNGStarterGravshipSpec
    {
        internal static readonly int[][] BaseStructure =
        {
            new[] { 0, 2, 2, 2, 3, 2, 2, 2, 2, 0 },
            new[] { 1, 1, 1, 1, 1, 1, 1, 1, 2, 0 },
            new[] { 2, 1, 1, 1, 1, 1, 1, 1, 2, 2 },
            new[] { 2, 1, 1, 1, 1, 1, 1, 1, 1, 2 },
            new[] { 2, 1, 1, 1, 1, 1, 1, 1, 1, 2 },
            new[] { 2, 1, 1, 1, 1, 1, 1, 1, 1, 2 },
            new[] { 2, 1, 1, 1, 1, 1, 1, 1, 2, 2 },
            new[] { 1, 1, 1, 1, 1, 1, 1, 1, 2, 0 },
            new[] { 0, 2, 2, 2, 3, 2, 2, 2, 2, 0 }
        };

        internal WNGGravshipFamily Family;
        internal string SubstructureName;
        internal string HullName;
        internal string DoorName;
        internal string EngineName;
        internal string FuelTankName;
        internal string ThrusterName;
        internal string PilotName;
        internal string PowerGeneratorName;
        internal string PowerConduitName;
        internal string FuelConduitName;
        internal string AtmosphereName;
        internal string NavigationAssistName;

        internal TerrainDef Substructure;
        internal ThingDef Hull;
        internal ThingDef Door;
        internal ThingDef Engine;
        internal ThingDef FuelTank;
        internal ThingDef Thruster;
        internal ThingDef Pilot;
        internal ThingDef PowerGenerator;
        internal ThingDef PowerConduit;
        internal ThingDef FuelConduit;
        internal ThingDef Atmosphere;
        internal ThingDef NavigationAssist;

        internal static WNGStarterGravshipSpec For(WNGGravshipFamily family)
        {
            if (family == WNGGravshipFamily.Wraith)
            {
                return new WNGStarterGravshipSpec
                {
                    Family = family,
                    SubstructureName = "WNG_WraithGravshipSubstructure",
                    HullName = "WNG_OrganicGravshipHull",
                    DoorName = "WNG_WraithGravshipDoor",
                    EngineName = "WNG_WraithGravEngine",
                    FuelTankName = "WNG_WraithFuelBladder",
                    ThrusterName = "WNG_OrganicThruster",
                    PilotName = "WNG_OrganicPilotNode",
                    PowerGeneratorName = "WNG_GravshipMetabolicHeart",
                    PowerConduitName = "WNG_WraithNeuralConduit",
                    FuelConduitName = "WNG_WraithFuelConduit",
                    AtmosphereName = "WNG_WraithAtmosphereOrgan",
                    NavigationAssistName = "WNG_WraithNavigationCortex"
                };
            }

            if (family == WNGGravshipFamily.Asuran)
            {
                return new WNGStarterGravshipSpec
                {
                    Family = family,
                    SubstructureName = "WNG_AsuranGravshipSubstructure",
                    HullName = "WNG_PrecursorGravshipHull",
                    DoorName = "WNG_AsuranGravshipDoor",
                    EngineName = "WNG_AsuranGravEngine",
                    FuelTankName = "WNG_AsuranFuelCell",
                    ThrusterName = "WNG_PrecursorVectorThruster",
                    PilotName = "WNG_PrecursorPilotConsole",
                    PowerGeneratorName = "WNG_AsuranPowerCell",
                    PowerConduitName = "WNG_AsuranPowerConduit",
                    FuelConduitName = "WNG_AsuranFuelConduit",
                    AtmosphereName = "WNG_AsuranAtmosphereRegulator",
                    NavigationAssistName = "WNG_AsuranFlightSubmind"
                };
            }

            return null;
        }

        internal bool ResolveAndValidate(out string problem)
        {
            problem = null;

            Substructure = DefDatabase<TerrainDef>.GetNamedSilentFail(SubstructureName);
            Hull = DefDatabase<ThingDef>.GetNamedSilentFail(HullName);
            Door = DefDatabase<ThingDef>.GetNamedSilentFail(DoorName);
            Engine = DefDatabase<ThingDef>.GetNamedSilentFail(EngineName);
            FuelTank = DefDatabase<ThingDef>.GetNamedSilentFail(FuelTankName);
            Thruster = DefDatabase<ThingDef>.GetNamedSilentFail(ThrusterName);
            Pilot = DefDatabase<ThingDef>.GetNamedSilentFail(PilotName);
            PowerGenerator = DefDatabase<ThingDef>.GetNamedSilentFail(PowerGeneratorName);
            PowerConduit = DefDatabase<ThingDef>.GetNamedSilentFail(PowerConduitName);
            FuelConduit = DefDatabase<ThingDef>.GetNamedSilentFail(FuelConduitName);
            Atmosphere = DefDatabase<ThingDef>.GetNamedSilentFail(AtmosphereName);
            NavigationAssist = DefDatabase<ThingDef>.GetNamedSilentFail(NavigationAssistName);

            if (Substructure == null || Hull == null || Door == null || Engine == null ||
                FuelTank == null || Thruster == null || Pilot == null || PowerGenerator == null ||
                PowerConduit == null || FuelConduit == null || Atmosphere == null ||
                NavigationAssist == null)
            {
                problem = "one or more family starter defs are missing";
                return false;
            }

            if (!Substructure.IsSubstructure)
            {
                problem = SubstructureName + " is not an Odyssey substructure";
                return false;
            }

            if (Engine.thingClass == null || !typeof(Building_GravEngine).IsAssignableFrom(Engine.thingClass))
            {
                problem = EngineName + " is not a real Odyssey grav engine";
                return false;
            }

            CompProperties_AffectedByFacilities affected =
                Engine.GetCompProperties<CompProperties_AffectedByFacilities>();
            if (affected?.linkableFacilities == null)
            {
                problem = EngineName + " has no facility-link contract";
                return false;
            }

            ThingDef[] facilities =
            {
                FuelTank, Thruster, Pilot, PowerGenerator, PowerConduit,
                FuelConduit, Atmosphere, NavigationAssist
            };
            foreach (ThingDef facility in facilities)
            {
                if (!affected.linkableFacilities.Contains(facility))
                {
                    problem = EngineName + " does not link to " + facility.defName;
                    return false;
                }
            }

            CompProperties_WNGFuelEndpoint tankFuel =
                FuelTank.GetCompProperties<CompProperties_WNGFuelEndpoint>();
            CompProperties_WNGFuelEndpoint driveFuel =
                Thruster.GetCompProperties<CompProperties_WNGFuelEndpoint>();
            CompProperties_WNGFuelEndpoint conduitFuel =
                FuelConduit.GetCompProperties<CompProperties_WNGFuelEndpoint>();
            if (tankFuel?.family != Family || tankFuel.role != WNGFuelEndpointRole.Tank ||
                driveFuel?.family != Family || driveFuel.role != WNGFuelEndpointRole.Thruster ||
                conduitFuel?.family != Family || conduitFuel.role != WNGFuelEndpointRole.Conduit)
            {
                problem = "family fuel-network roles are not internally consistent";
                return false;
            }

            CompProperties_WNGFamilyPowerNode generatorPower =
                PowerGenerator.GetCompProperties<CompProperties_WNGFamilyPowerNode>();
            CompProperties_WNGFamilyPowerNode conduitPower =
                PowerConduit.GetCompProperties<CompProperties_WNGFamilyPowerNode>();
            if (generatorPower?.family != Family || generatorPower.role != WNGFamilyPowerRole.Generator ||
                conduitPower?.family != Family || conduitPower.role != WNGFamilyPowerRole.Conduit)
            {
                problem = "family power-network roles are not internally consistent";
                return false;
            }

            if (FuelTank.GetCompProperties<CompProperties_Refuelable>() == null ||
                PowerGenerator.GetCompProperties<CompProperties_Refuelable>() == null)
            {
                problem = "starter fuel or family power source cannot be initialized";
                return false;
            }

            return true;
        }
    }
}
