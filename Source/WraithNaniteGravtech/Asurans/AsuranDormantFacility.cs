using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_DormantAsuranCore : CompProperties
    {
        public int graceTicks = 900;
        public float triggerRadius = 14f;
        public int minimumDefenders = 2;
        public int maximumDefenders = 4;

        public CompProperties_DormantAsuranCore()
        {
            compClass = typeof(CompDormantAsuranCore);
        }
    }

    public sealed class CompDormantAsuranCore : ThingComp
    {
        private bool activated;
        private int activationReadyTick;

        private CompProperties_DormantAsuranCore Props => (CompProperties_DormantAsuranCore)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && activationReadyTick <= 0)
                activationReadyTick = SafeFutureTick(Find.TickManager?.TicksGame ?? 0, Props.graceTicks);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (activated || parent?.Spawned != true || parent.Map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (activationReadyTick <= 0)
                activationReadyTick = SafeFutureTick(now, Props.graceTicks);
            if (now < activationReadyTick)
                return;

            float radiusSq = Math.Max(0f, Props.triggerRadius) * Math.Max(0f, Props.triggerRadius);
            bool approached = parent.Map.mapPawns.FreeColonistsSpawned
                .Any(p => p != null && !p.Dead && p.Position.DistanceToSquared(parent.Position) <= radiusSq);
            if (!approached)
                return;

            // Commit one-shot state before any spawn/presentation work. Reloads or presentation
            // failures therefore cannot duplicate the defensive cadre.
            activated = true;
            SpawnFiniteDefenders();
        }

        private void SpawnFiniteDefenders()
        {
            Map map = parent.Map;
            if (map == null)
                return;

            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail("WNG_PrecursorCollective");
            Faction faction = factionDef == null ? null : Find.FactionManager?.FirstFactionOfDef(factionDef);
            PawnKindDef humanForm = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_HumanFormReplicator");
            PawnKindDef soldier = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_PrecursorSoldier");
            if (faction == null || humanForm == null)
            {
                Log.Warning("[WNG] Dormant Asuran core activated but its hostile Lattice faction/pawn kind was unavailable.");
                return;
            }

            int min = Math.Max(1, Props.minimumDefenders);
            int max = Math.Max(min, Props.maximumDefenders);
            int count = Rand.RangeInclusive(min, max);
            Lord lord = LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, parent.Position, 60000), map);

            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = soldier != null && i == count - 1 && count >= 3 ? soldier : humanForm;
                Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(parent.Position, map, 10);
                GenSpawn.Spawn(pawn, cell, map);
                lord.AddPawn(pawn);
            }

            Find.LetterStack.ReceiveLetter(
                "Dormant Asuran facility awakened",
                "The central pattern core has awakened a finite Lattice defensive cadre. The core itself remains a sealed relic: it does not provide pattern storage, reconstruction or power.",
                LetterDefOf.ThreatSmall,
                parent);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref activated, "wngDormantAsuranActivated", false);
            Scribe_Values.Look(ref activationReadyTick, "wngDormantAsuranReadyTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                activationReadyTick = Math.Max(0, activationReadyTick);
        }
    }

    public sealed class SitePartWorker_AsuranDormantFacility : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null) return;

            BuildFacility(map);
            PlaceBuilding(map, "WNG_AsuranDormantPatternCore", map.Center);
            PlaceBuilding(map, "WNG_AsuranDormantRelay", map.Center + new IntVec3(-4, 0, -3));
            PlaceBuilding(map, "WNG_AsuranDormantRelay", map.Center + new IntVec3(4, 0, 3));
            PlaceBuilding(map, "WNG_AsuranDormantReconstructionPlinth", map.Center + new IntVec3(-4, 0, 3));
            PlaceBuilding(map, "WNG_AsuranDormantReconstructionPlinth", map.Center + new IntVec3(4, 0, -3));

            PlaceThing(map, "WNG_RecoveredPrecursorPulseRifle", map.Center + new IntVec3(0, 0, 4));
            PlaceStack(map, "Plasteel", map.Center + new IntVec3(-2, 0, 4), Rand.RangeInclusive(18, 32));
            PlaceStack(map, "ComponentIndustrial", map.Center + new IntVec3(2, 0, 4), Rand.RangeInclusive(1, 3));
        }

        private static void BuildFacility(Map map)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_PrecursorWall");
            ThingDef door = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_PrecursorDoor");
            if (wall == null) return;

            const int radius = 8;
            for (int x = -radius; x <= radius; x++)
            {
                SpawnStructure(map, x == 0 && door != null ? door : wall, map.Center + new IntVec3(x, 0, -radius));
                SpawnStructure(map, wall, map.Center + new IntVec3(x, 0, radius));
            }
            for (int z = -radius + 1; z < radius; z++)
            {
                SpawnStructure(map, wall, map.Center + new IntVec3(-radius, 0, z));
                SpawnStructure(map, wall, map.Center + new IntVec3(radius, 0, z));
            }
        }

        private static void SpawnStructure(Map map, ThingDef def, IntVec3 cell)
        {
            if (def == null || !cell.InBounds(map) || cell.GetFirstBuilding(map) != null) return;
            GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
        }

        private static void PlaceBuilding(Map map, string defName, IntVec3 preferred)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }

        private static void PlaceThing(Map map, string defName, IntVec3 preferred)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }

        private static void PlaceStack(Map map, string defName, IntVec3 preferred, int count)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Max(1, Math.Min(count, def.stackLimit));
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }
    }

    public sealed class IncidentWorker_AsuranDormantFacilityDiscovery : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            parms?.target is Map map && map.IsPlayerHome &&
            DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_AsuranDormantFacility") != null;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_AsuranDormantFacility");
            if (map == null || !map.IsPlayerHome || part == null) return false;

            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, 9, 30, allowCaravans: false)) return false;
            float points = Math.Max(350f, parms.points > 0f ? parms.points : StorytellerUtility.DefaultSiteThreatPointsNow());
            Site created = SiteMaker.MakeSite(part, tile, faction: null, ifHostileThenMustRemainHostile: false, threatPoints: points);
            if (created == null) return false;

            created.customLabel = "Dormant Asuran Facility";
            Find.WorldObjects.Add(created);
            Find.LetterStack.ReceiveLetter(
                "Dormant Asuran facility located",
                "Survey data reveals an unusually intact, clearly Asuran facility. Its central pattern core appears dark, but approaching it may not be safe.",
                LetterDefOf.NeutralEvent,
                created);
            return true;
        }
    }
}
