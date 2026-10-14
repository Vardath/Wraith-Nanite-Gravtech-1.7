using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    public sealed class SitePartWorker_WraithRuinedLaboratory : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null) return;

            BuildBreachedShell(map);
            PlaceRuin(map, "WNG_RupturedWraithGrowthVat", map.Center + new IntVec3(-2, 0, 1), 0.32f);
            PlaceRuin(map, "WNG_CollapsedWraithFeedingRig", map.Center + new IntVec3(3, 0, -1), 0.28f);
            PlaceStack(map, "WNG_Biomass", map.Center + new IntVec3(-4, 0, -3), Rand.RangeInclusive(45, 80));
            PlaceThing(map, "WNG_WraithStunner", map.Center + new IntVec3(4, 0, 3));

            if (Rand.Chance(0.38f))
                SpawnRemnants(map);
        }

        private static void BuildBreachedShell(Map map)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithLivingWall");
            if (wall == null) return;

            const int radius = 7;
            for (int x = -radius; x <= radius; x++)
            {
                TryWall(map, wall, map.Center + new IntVec3(x, 0, -radius), x);
                TryWall(map, wall, map.Center + new IntVec3(x, 0, radius), x + 3);
            }
            for (int z = -radius + 1; z < radius; z++)
            {
                TryWall(map, wall, map.Center + new IntVec3(-radius, 0, z), z + 1);
                TryWall(map, wall, map.Center + new IntVec3(radius, 0, z), z + 4);
            }
        }

        private static void TryWall(Map map, ThingDef wall, IntVec3 cell, int breachSeed)
        {
            if (Math.Abs(breachSeed) % 5 == 0 || !cell.InBounds(map) || cell.GetFirstBuilding(map) != null)
                return;
            Thing thing = ThingMaker.MakeThing(wall);
            GenSpawn.Spawn(thing, cell, map);
            thing.HitPoints = Math.Max(1, (int)(thing.MaxHitPoints * Rand.Range(0.35f, 0.72f)));
        }

        private static void PlaceRuin(Map map, string defName, IntVec3 preferred, float health)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def);
            if (GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near))
                thing.HitPoints = Math.Max(1, (int)(thing.MaxHitPoints * health));
            else if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
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

        private static void PlaceThing(Map map, string defName, IntVec3 preferred)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }

        private static void SpawnRemnants(Map map)
        {
            if (Faction.OfPlayer == null) return;
            List<Faction> lineages = WraithLineageUtility.ActiveLineages()
                .Where(f => f != null && !f.defeated && f != Faction.OfPlayer && f.HostileTo(Faction.OfPlayer))
                .ToList();
            if (lineages.Count == 0) return;

            Faction faction = lineages.RandomElement();
            PawnKindDef hunter = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithHunter");
            PawnKindDef warrior = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithWarrior");
            if (hunter == null || warrior == null) return;

            Lord lord = LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, map.Center, 50000), map);
            int count = Rand.RangeInclusive(2, 4);
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(i == count - 1 && count > 2 ? warrior : hunter, faction);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 14);
                GenSpawn.Spawn(pawn, cell, map);
                lord.AddPawn(pawn);
            }
        }
    }

    public sealed class IncidentWorker_WraithRuinedLaboratoryDiscovery : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return parms?.target is Map map && map.IsPlayerHome &&
                DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_WraithRuinedLaboratory") != null;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_WraithRuinedLaboratory");
            if (map == null || !map.IsPlayerHome || part == null) return false;

            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, 8, 26, allowCaravans: false)) return false;
            float points = Math.Max(250f, parms.points > 0f ? parms.points : StorytellerUtility.DefaultSiteThreatPointsNow());
            Site created = SiteMaker.MakeSite(part, tile, faction: null, ifHostileThenMustRemainHostile: false, threatPoints: points);
            if (created == null) return false;

            created.customLabel = "Abandoned Wraith Laboratory";
            Find.WorldObjects.Add(created);
            Find.LetterStack.ReceiveLetter("Abandoned Wraith laboratory located",
                "Long-range biological traces point to a breached, factionless Wraith laboratory. Its machinery is dead, but finite biomass and field specimens may remain.",
                LetterDefOf.NeutralEvent, created);
            return true;
        }
    }
}
