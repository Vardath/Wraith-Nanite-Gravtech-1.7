using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    public sealed class SitePartWorker_WraithCloningInstallation : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null) return;

            BuildSealedShell(map);
            for (int i = 0; i < 6; i++)
            {
                int x = i < 3 ? -4 : 4;
                int z = -4 + (i % 3) * 4;
                PlaceRuin(map, "WNG_DeadWraithGestationCocoon", map.Center + new IntVec3(x, 0, z), Rand.Range(0.30f, 0.62f));
            }
            PlaceRuin(map, "WNG_CollapsedCloneNutrientManifold", map.Center, 0.38f);
            PlaceRuin(map, "WNG_DrainedLifeForceDonorDais", map.Center + new IntVec3(0, 0, 4), 0.42f);
            PlaceStack(map, "WNG_Biomass", map.Center + new IntVec3(0, 0, -4), Rand.RangeInclusive(70, 120));
            PlaceThing(map, "WNG_WraithStunStaff", map.Center + new IntVec3(2, 0, 4));

            if (Rand.Chance(0.34f))
                SpawnReclamationParty(map);
        }

        private static void BuildSealedShell(Map map)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithLivingWall");
            ThingDef door = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithMembraneDoor");
            if (wall == null) return;
            const int radius = 8;
            for (int x = -radius; x <= radius; x++)
            {
                TryStructure(map, x == 0 && door != null ? door : wall, map.Center + new IntVec3(x, 0, -radius));
                TryStructure(map, wall, map.Center + new IntVec3(x, 0, radius));
            }
            for (int z = -radius + 1; z < radius; z++)
            {
                TryStructure(map, wall, map.Center + new IntVec3(-radius, 0, z));
                TryStructure(map, wall, map.Center + new IntVec3(radius, 0, z));
            }
        }

        private static void TryStructure(Map map, ThingDef def, IntVec3 cell)
        {
            if (def == null || !cell.InBounds(map) || cell.GetFirstBuilding(map) != null) return;
            Thing thing = ThingMaker.MakeThing(def);
            GenSpawn.Spawn(thing, cell, map);
            thing.HitPoints = Math.Max(1, (int)(thing.MaxHitPoints * Rand.Range(0.48f, 0.82f)));
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
            Thing thing = ThingMaker.MakeThing(def); thing.stackCount = Math.Max(1, Math.Min(count, def.stackLimit));
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
        }

        private static void PlaceThing(Map map, string defName, IntVec3 preferred)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
        }

        private static void SpawnReclamationParty(Map map)
        {
            if (Faction.OfPlayer == null) return;
            List<Faction> lineages = WraithLineageUtility.ActiveLineages().Where(f => f != null && !f.defeated && f != Faction.OfPlayer && f.HostileTo(Faction.OfPlayer)).ToList();
            if (lineages.Count == 0) return;

            Faction faction = lineages.RandomElement();
            PawnKindDef keeper = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithKeeper");
            PawnKindDef hunter = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithHunter");
            PawnKindDef warrior = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithWarrior");
            if (keeper == null || hunter == null || warrior == null) return;

            Lord lord = LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, map.Center, 60000), map);
            int count = Rand.RangeInclusive(3, 5);
            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = i == 0 ? keeper : (i % 2 == 0 ? warrior : hunter);
                Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 16), map);
                lord.AddPawn(pawn);
            }
        }
    }

    public sealed class IncidentWorker_WraithCloningInstallationDiscovery : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            parms?.target is Map map && map.IsPlayerHome && DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_WraithCloningInstallation") != null;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_WraithCloningInstallation");
            if (map == null || !map.IsPlayerHome || part == null) return false;

            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, 9, 28, allowCaravans: false)) return false;
            float points = Math.Max(300f, parms.points > 0f ? parms.points : StorytellerUtility.DefaultSiteThreatPointsNow());
            Site created = SiteMaker.MakeSite(part, tile, faction: null, ifHostileThenMustRemainHostile: false, threatPoints: points);
            if (created == null) return false;

            created.customLabel = "Abandoned Wraith Cloning Installation";
            Find.WorldObjects.Add(created);
            Find.LetterStack.ReceiveLetter("Abandoned Wraith cloning installation located",
                "A sealed biological installation has been located. Its gestation machinery appears dead, but finite biomass and a Wraith field specimen may still be recoverable.",
                LetterDefOf.NeutralEvent, created);
            return true;
        }
    }
}
