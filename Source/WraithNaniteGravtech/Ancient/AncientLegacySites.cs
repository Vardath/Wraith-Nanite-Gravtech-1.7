using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    internal static class AncientLegacySiteUtility
    {
        public static bool CanCreate(string sitePartDefName)
        {
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail(sitePartDefName);
            if (part == null) return false;
            return !Find.WorldObjects.AllWorldObjects.OfType<Site>()
                .Any(site => site?.parts != null && site.parts.Any(p => p?.def == part));
        }

        public static bool TryCreate(
            IncidentParms parms,
            string sitePartDefName,
            string customLabel,
            string letterLabel,
            string letterText,
            float minimumThreatPoints,
            int minTileDistance = 8,
            int maxTileDistance = 30)
        {
            Map map = parms?.target as Map;
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail(sitePartDefName);
            if (map == null || !map.IsPlayerHome || part == null || !CanCreate(sitePartDefName))
                return false;

            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, minTileDistance, maxTileDistance, allowCaravans: false))
                return false;

            float points = Math.Max(
                minimumThreatPoints,
                parms.points > 0f ? parms.points : StorytellerUtility.DefaultSiteThreatPointsNow());

            Site created = SiteMaker.MakeSite(part, tile, faction: null, ifHostileThenMustRemainHostile: false, threatPoints: points);
            if (created == null)
                return false;

            created.customLabel = customLabel;
            Find.WorldObjects.Add(created);
            Find.LetterStack.ReceiveLetter(letterLabel, letterText, LetterDefOf.NeutralEvent, created);
            return true;
        }

        public static void BuildRoom(Map map, IntVec3 center, int radiusX, int radiusZ)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_PrecursorWall");
            ThingDef door = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_PrecursorDoor");
            if (map == null || wall == null) return;

            for (int x = -radiusX; x <= radiusX; x++)
            {
                SpawnStructure(map, wall, center + new IntVec3(x, 0, radiusZ));
                ThingDef south = x == 0 && door != null ? door : wall;
                SpawnStructure(map, south, center + new IntVec3(x, 0, -radiusZ));
            }

            for (int z = -radiusZ + 1; z < radiusZ; z++)
            {
                SpawnStructure(map, wall, center + new IntVec3(-radiusX, 0, z));
                SpawnStructure(map, wall, center + new IntVec3(radiusX, 0, z));
            }
        }

        public static Thing PlaceThing(Map map, string defName, IntVec3 preferred)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (map == null || def == null) return null;

            Thing thing = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near))
            {
                if (!thing.Destroyed)
                    thing.Destroy(DestroyMode.Vanish);
                return null;
            }

            return thing;
        }

        public static void PlaceStack(Map map, string defName, IntVec3 preferred, int count)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (map == null || def == null) return;
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Max(1, Math.Min(count, def.stackLimit));
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }

        public static void PlaceDamagedThing(Map map, string defName, IntVec3 preferred, float minHealthFraction, float maxHealthFraction)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (map == null || def == null) return;

            Thing thing = ThingMaker.MakeThing(def);
            float min = Math.Max(0.05f, Math.Min(1f, minHealthFraction));
            float max = Math.Max(min, Math.Min(1f, maxHealthFraction));
            thing.HitPoints = Math.Max(1, Math.Min(thing.MaxHitPoints, (int)Math.Round(thing.MaxHitPoints * Rand.Range(min, max))));

            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }

        private static void SpawnStructure(Map map, ThingDef def, IntVec3 cell)
        {
            if (map == null || def == null || !cell.InBounds(map) || cell.GetFirstBuilding(map) != null)
                return;
            GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
        }
    }

    public sealed class IncidentWorker_PrecursorLaboratoryDiscovery : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            parms?.target is Map map && map.IsPlayerHome &&
            AncientLegacySiteUtility.CanCreate("WNG_PrecursorLaboratorySite");

        protected override bool TryExecuteWorker(IncidentParms parms) =>
            AncientLegacySiteUtility.TryCreate(
                parms,
                "WNG_PrecursorLaboratorySite",
                "Open Precursor Laboratory",
                "Precursor laboratory located",
                "Survey traces lead to an exposed Ancient-era laboratory. The site appears abandoned, but surviving evidence may clarify how the precursor systems in this region were used.",
                275f);
    }

    public sealed class IncidentWorker_PrecursorVaultDiscovery : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            parms?.target is Map map && map.IsPlayerHome &&
            AncientLegacySiteUtility.CanCreate("WNG_PrecursorVaultSite");

        protected override bool TryExecuteWorker(IncidentParms parms) =>
            AncientLegacySiteUtility.TryCreate(
                parms,
                "WNG_PrecursorVaultSite",
                "Sealed Precursor Vault",
                "Sealed precursor vault located",
                "A buried precursor vault has been isolated from surrounding ruins. Its intact shell suggests that at least some damaged high-energy containment evidence may remain inside.",
                325f);
    }

    public sealed class IncidentWorker_DeceptiveSurveyAnnexDiscovery : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            parms?.target is Map map && map.IsPlayerHome &&
            AncientLegacySiteUtility.CanCreate("WNG_DeceptiveSurveyAnnexSite");

        protected override bool TryExecuteWorker(IncidentParms parms) =>
            AncientLegacySiteUtility.TryCreate(
                parms,
                "WNG_DeceptiveSurveyAnnexSite",
                "Ancient Survey Annex",
                "Ancient survey annex located",
                "An intact survey annex has been located under shallow debris. Its architecture appears Ancient, and the surviving data core is quiet. The site may not be as straightforward as it looks.",
                350f);
    }

    public sealed class SitePartWorker_PrecursorLaboratory : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null) return;

            AncientLegacySiteUtility.BuildRoom(map, map.Center, 7, 5);
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorTable", map.Center);
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorFormChair", map.Center + new IntVec3(-2, 0, 0));
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorFormChair", map.Center + new IntVec3(2, 0, 0));
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorBedsideConsole", map.Center + new IntVec3(0, 0, 2));
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorLumen", map.Center + new IntVec3(0, 0, -2));

            // Restored dead environmental evidence. These props deliberately have no working
            // research, control, drone, containment, shield or power components.
            AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_RuinedAncientDiagnosticConsole", map.Center + new IntVec3(-4, 0, -2), 0.38f, 0.70f);
            AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_FracturedAncientContainmentCradle", map.Center + new IntVec3(3, 0, -2), 0.32f, 0.62f);
            AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_InertAncientDroneRack", map.Center + new IntVec3(-4, 0, 1), 0.40f, 0.72f);
            AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_CollapsedAncientFieldProjector", map.Center + new IntVec3(4, 0, 2), 0.28f, 0.58f);

            // Evidence, not a free functional Ancient weapon/power system.
            AncientLegacySiteUtility.PlaceThing(map, "WNG_RecoveredAncientDrone", map.Center + new IntVec3(-3, 0, 2));
            AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_AncientShieldHarmonicRelic", map.Center + new IntVec3(3, 0, 1), 0.52f, 0.78f);
            AncientLegacySiteUtility.PlaceStack(map, "Plasteel", map.Center + new IntVec3(3, 0, 2), Rand.RangeInclusive(14, 26));
            AncientLegacySiteUtility.PlaceStack(map, "ComponentIndustrial", map.Center + new IntVec3(3, 0, -2), Rand.RangeInclusive(1, 2));
        }
    }

    public sealed class SitePartWorker_PrecursorVault : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null) return;

            AncientLegacySiteUtility.BuildRoom(map, map.Center, 5, 5);
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorTableSmall", map.Center + new IntVec3(0, 0, 2));
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorWallLumen", map.Center + new IntVec3(-3, 0, 3));

            // A smaller set of the same dead evidence survives in the sealed vault.
            AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_FracturedAncientContainmentCradle", map.Center + new IntVec3(-2, 0, -2), 0.50f, 0.76f);
            AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_CollapsedAncientFieldProjector", map.Center + new IntVec3(2, 0, -2), 0.48f, 0.74f);

            // Degraded containment evidence remains inert; no free vacuum-energy module.
            AncientLegacySiteUtility.PlaceThing(map, "WNG_RecoveredVacuumEnergyModule", map.Center);
            if (Rand.Chance(0.45f))
                AncientLegacySiteUtility.PlaceDamagedThing(map, "WNG_AncientShieldHarmonicRelic", map.Center + new IntVec3(1, 0, 2), 0.68f, 0.94f);
            AncientLegacySiteUtility.PlaceStack(map, "Gold", map.Center + new IntVec3(2, 0, 1), Rand.RangeInclusive(8, 16));
            AncientLegacySiteUtility.PlaceStack(map, "ComponentSpacer", map.Center + new IntVec3(-2, 0, 1), 1);
        }
    }

    public sealed class CompProperties_DeceptiveSurveyAnnexCore : CompProperties
    {
        public int initialGraceTicks = 600;
        public float warningRadius = 14f;
        public float activationRadius = 7f;
        public int minimumWarningTicks = 600;
        public int checkIntervalTicks = 120;

        public CompProperties_DeceptiveSurveyAnnexCore()
        {
            compClass = typeof(CompDeceptiveSurveyAnnexCore);
        }
    }

    /// <summary>
    /// Two-stage retained survey-annex trap. The apparently Ancient matrix first exposes an
    /// anomalous pattern harmonic at a wider approach radius. Only after a minimum warning window
    /// can a closer approach wake the finite hidden Asuran reconstruction cadre. Site threat points
    /// choose the finite cadre composition; the trap remains one-shot and save-persistent.
    /// </summary>
    public sealed class CompDeceptiveSurveyAnnexCore : ThingComp
    {
        private bool warningIssued;
        private bool revealed;
        private int warningTick = -1;
        private int revealReadyTick = -1;
        private float configuredThreatPoints = 700f;

        private CompProperties_DeceptiveSurveyAnnexCore Props =>
            (CompProperties_DeceptiveSurveyAnnexCore)props;

        public void ConfigureThreat(float threatPoints)
        {
            configuredThreatPoints = Math.Max(400f, threatPoints);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && revealReadyTick < 0)
                revealReadyTick = SafeFutureTick(Find.TickManager?.TicksGame ?? 0, Props.initialGraceTicks);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (revealed || parent?.Spawned != true || parent.Map == null || Find.TickManager == null)
                return;

            int interval = Math.Max(30, Props.checkIntervalTicks);
            if (!parent.IsHashIntervalTick(interval))
                return;

            int now = Find.TickManager.TicksGame;
            if (revealReadyTick < 0)
                revealReadyTick = SafeFutureTick(now, Props.initialGraceTicks);
            if (now < revealReadyTick)
                return;

            if (!warningIssued)
            {
                if (AnyFreeColonistWithin(Props.warningRadius))
                    IssueWarning(now);
                return;
            }

            int minimumWarning = Math.Max(60, Props.minimumWarningTicks);
            if (now < SafeFutureTick(warningTick, minimumWarning))
                return;

            if (AnyFreeColonistWithin(Props.activationRadius))
                RevealLattice();
        }

        private bool AnyFreeColonistWithin(float radius)
        {
            if (parent?.Map == null)
                return false;

            float radiusSq = Math.Max(0f, radius) * Math.Max(0f, radius);
            return parent.Map.mapPawns.FreeColonistsSpawned.Any(
                p => p != null && !p.Dead && p.Position.DistanceToSquared(parent.Position) <= radiusSq);
        }

        private void IssueWarning(int now)
        {
            if (warningIssued || revealed)
                return;

            // Commit warning state before presentation so a UI/fleck failure cannot repeatedly
            // retrigger the wider warning boundary.
            warningIssued = true;
            warningTick = Math.Max(0, now);

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Anomalous precursor signal",
                    "The central survey matrix is not behaving like the surrounding Ancient hardware. A faint pattern harmonic has appeared beneath the expected control geometry. It has not reconstructed anything. Destroy the matrix or withdraw from the inner chamber if you do not want to find out what is hidden beneath the precursor shell.",
                    LetterDefOf.NeutralEvent,
                    parent);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Deceptive survey-annex warning committed but presentation failed: " + ex.Message);
            }
        }

        private void RevealLattice()
        {
            if (revealed || parent?.Map == null)
                return;

            Map map = parent.Map;
            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail("WNG_PrecursorCollective");
            Faction faction = factionDef == null ? null : Find.FactionManager?.FirstFactionOfDef(factionDef);
            PawnKindDef baseKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_HumanFormReplicator");
            PawnKindDef soldier = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_PrecursorSoldier");
            PawnKindDef commander = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_PrecursorCommander");
            if (faction == null || baseKind == null)
            {
                Log.Warning("[WNG] Deceptive survey annex armed but its hostile Lattice faction/pawn kind was unavailable.");
                return;
            }

            List<PawnKindDef> kinds = new List<PawnKindDef> { baseKind, baseKind };
            if (configuredThreatPoints >= 900f && soldier != null)
                kinds.Add(soldier);
            if (configuredThreatPoints >= 1650f && commander != null)
                kinds.Add(commander);

            Lord lord = LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, parent.Position, 60000), map);
            List<Pawn> spawned = new List<Pawn>(kinds.Count);

            try
            {
                foreach (PawnKindDef kind in kinds)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                    IntVec3 cell = CellFinder.RandomClosewalkCellNear(parent.Position, map, 7);
                    GenSpawn.Spawn(pawn, cell, map);
                    lord.AddPawn(pawn);
                    spawned.Add(pawn);
                }

                // Physical cadre exists before the one-shot state commits.
                revealed = true;
            }
            catch (Exception ex)
            {
                foreach (Pawn pawn in spawned)
                {
                    if (pawn != null && !pawn.Destroyed)
                        pawn.Destroy(DestroyMode.Vanish);
                }

                Log.Error("[WNG] Deceptive survey-annex reveal rolled back before commit: " + ex);
                return;
            }

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Ancient ruin revealed as Asuran trap",
                    "The hidden pattern layer has opened. The apparent precursor survey matrix was an Asuran reconstruction trap masked beneath Ancient-style control geometry. A finite defensive cadre has rebuilt around the matrix. The ruin still contains no usable Pattern Archive or functioning reconstruction network.",
                    LetterDefOf.ThreatBig,
                    parent);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Deceptive survey-annex reveal committed but presentation failed: " + ex.Message);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (revealed)
                return "Survey matrix: hidden Asuran layer exhausted";
            if (warningIssued)
                return "Survey matrix: anomalous pattern harmonic detected";
            return "Survey matrix: precursor signature nominal";
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref warningIssued, "wngDeceptiveAncientWarningIssued", false);
            Scribe_Values.Look(ref revealed, "wngSurveyAnnexRevealed", false);
            Scribe_Values.Look(ref warningTick, "wngDeceptiveAncientWarningTick", -1);
            Scribe_Values.Look(ref revealReadyTick, "wngSurveyAnnexRevealReadyTick", -1);
            Scribe_Values.Look(ref configuredThreatPoints, "wngDeceptiveAncientThreatPoints", 700f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                warningTick = Math.Max(-1, warningTick);
                revealReadyTick = Math.Max(-1, revealReadyTick);
                configuredThreatPoints = Math.Max(400f, configuredThreatPoints);
            }
        }
    }

    public sealed class SitePartWorker_DeceptiveSurveyAnnex : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null) return;

            AncientLegacySiteUtility.BuildRoom(map, map.Center, 6, 4);
            Thing core = AncientLegacySiteUtility.PlaceThing(map, "WNG_DeceptiveSurveyAnnexCore", map.Center);
            float threatPoints = 700f;
            if (map.Parent is Site site)
                threatPoints = Math.Max(threatPoints, site.desiredThreatPoints);
            core?.TryGetComp<CompDeceptiveSurveyAnnexCore>()?.ConfigureThreat(threatPoints);

            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorTableSmall", map.Center + new IntVec3(0, 0, 2));
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorFormChair", map.Center + new IntVec3(-2, 0, 2));
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorFormChair", map.Center + new IntVec3(2, 0, 2));
            AncientLegacySiteUtility.PlaceThing(map, "WNG_PrecursorLumen", map.Center + new IntVec3(0, 0, -2));
        }
    }
}
