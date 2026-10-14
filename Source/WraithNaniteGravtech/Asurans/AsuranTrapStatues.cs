using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AsuranSleeperStatue : CompProperties
    {
        public IntRange awakeningDelayDays = new IntRange(5, 60);
        public string hostileFactionDefName = "WNG_PrecursorCollective";
        public string pawnKindDefName = "WNG_HumanFormReplicator";

        public CompProperties_AsuranSleeperStatue()
        {
            compClass = typeof(CompAsuranSleeperStatue);
        }
    }

    /// <summary>
    /// An Asuran-derived artifact whose hidden awakening deadline is rolled on first actual map
    /// placement. Manufacturing/trade/minified storage does not start the clock. After initialization,
    /// minifying preserves the deadline; if it matures in storage it resolves on re-placement.
    /// </summary>
    public sealed class CompAsuranSleeperStatue : ThingComp
    {
        private const int TicksPerDay = 60000;
        private int awakeningTick = -1;
        private bool awakened;

        private CompProperties_AsuranSleeperStatue Props => (CompProperties_AsuranSleeperStatue)props;


        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureDeadline();
            TryAwaken();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            TryAwaken();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref awakeningTick, "awakeningTick", -1);
            Scribe_Values.Look(ref awakened, "awakened", false);
        }

        private void EnsureDeadline()
        {
            if (awakened || awakeningTick >= 0 || Find.TickManager == null)
                return;

            int days = Props.awakeningDelayDays.RandomInRange;
            awakeningTick = Find.TickManager.TicksGame + (days * TicksPerDay);
        }

        private void TryAwaken()
        {
            if (awakened || !parent.Spawned || parent.Map == null || Find.TickManager == null)
                return;
            EnsureDeadline();
            if (awakeningTick < 0 || Find.TickManager.TicksGame < awakeningTick)
                return;

            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(Props.hostileFactionDefName);
            PawnKindDef pawnKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.pawnKindDefName);
            Faction faction = factionDef == null ? null : Find.FactionManager.FirstFactionOfDef(factionDef);
            if (faction == null || pawnKind == null)
                return;

            Pawn pawn = PawnGenerator.GeneratePawn(pawnKind, faction);
            if (pawn == null)
                return;

            Map map = parent.Map;
            IntVec3 position = parent.Position;
            awakened = true;
            parent.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(pawn, position, map);
            Messages.Message(
                "An Asuran sculpture unfolds into a hostile human-form Replicator.",
                pawn,
                MessageTypeDefOf.ThreatBig);
        }
    }

    public sealed class CompProperties_AsuranFeederStatue : CompProperties
    {
        public IntRange feedingDelayDays = new IntRange(5, 20);
        public float maximumFeedingGlow = 0.10f;
        public float feedingRadius = 12f;
        public IntRange slurryYield = new IntRange(35, 70);
        public string slurryDefName = "WNG_AsuranNaniteSlurry";

        public CompProperties_AsuranFeederStatue()
        {
            compClass = typeof(CompAsuranFeederStatue);
        }
    }

    /// <summary>
    /// Concealed matter-harvesting Asuran artifact. Its first cycle is rolled on first placement and
    /// advances only while the installed statue is in darkness. Minification pauses but preserves the
    /// same cycle. Feeding converts one real nearby flesh pawn into existing Asuran nanite slurry.
    /// </summary>
    public sealed class CompAsuranFeederStatue : ThingComp
    {
        private const int TicksPerDay = 60000;
        private const int RareTickInterval = 250;
        private int requiredDarkTicks = -1;
        private int accumulatedDarkTicks;

        private CompProperties_AsuranFeederStatue Props => (CompProperties_AsuranFeederStatue)props;


        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureCycle();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned || parent.Map == null)
                return;

            EnsureCycle();
            if (parent.Map.glowGrid.GroundGlowAt(parent.Position) > Props.maximumFeedingGlow)
                return;

            accumulatedDarkTicks += RareTickInterval;
            if (requiredDarkTicks > 0 && accumulatedDarkTicks >= requiredDarkTicks)
                TryFeed();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref requiredDarkTicks, "requiredDarkTicks", -1);
            Scribe_Values.Look(ref accumulatedDarkTicks, "accumulatedDarkTicks", 0);
        }

        private void EnsureCycle()
        {
            if (requiredDarkTicks > 0)
                return;
            requiredDarkTicks = Props.feedingDelayDays.RandomInRange * TicksPerDay;
            accumulatedDarkTicks = 0;
        }

        private void TryFeed()
        {
            Map map = parent.Map;
            if (map == null)
                return;

            List<Pawn> candidates = map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn != null
                    && !pawn.Dead
                    && pawn.RaceProps != null
                    && pawn.RaceProps.IsFlesh
                    && pawn.Position.InHorDistOf(parent.Position, Props.feedingRadius)
                    && GenSight.LineOfSight(parent.Position, pawn.Position, map))
                .ToList();
            if (candidates.Count == 0)
                return;

            Pawn victim = candidates.RandomElement();
            IntVec3 victimPosition = victim.Position;
            victim.Kill(null);
            Corpse corpse = victim.Corpse;
            if (corpse != null && !corpse.Destroyed)
                corpse.Destroy(DestroyMode.Vanish);

            ThingDef slurryDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.slurryDefName);
            if (slurryDef != null)
            {
                Thing slurry = ThingMaker.MakeThing(slurryDef);
                slurry.stackCount = Props.slurryYield.RandomInRange;
                if (!GenPlace.TryPlaceThing(slurry, victimPosition, map, ThingPlaceMode.Near))
                    slurry.Destroy(DestroyMode.Vanish);
            }

            Messages.Message(
                "A dark Asuran sculpture consumes nearby living matter, leaving only nanite slurry.",
                parent,
                MessageTypeDefOf.NegativeEvent);

            requiredDarkTicks = Props.feedingDelayDays.RandomInRange * TicksPerDay;
            accumulatedDarkTicks = 0;
        }
    }


    public sealed class CompProperties_AsuranReplicatorReliquary : CompProperties
    {
        public IntRange releaseDelayDays = new IntRange(5, 60);
        public int replicatorCount = 5;
        public string hostileFactionDefName = "WNG_ReplicatorSwarm";
        public string pawnKindDefName = "WNG_ReplicatorDrone";

        public CompProperties_AsuranReplicatorReliquary()
        {
            compClass = typeof(CompAsuranReplicatorReliquary);
        }
    }

    /// <summary>
    /// An Asuran reliquary whose hidden release deadline is rolled on first actual map placement.
    /// Manufacturing/trade/minified storage does not start it. Once mature and installed, it breaks
    /// into exactly five hostile autonomous Drones sharing one domain; failed placement rolls back.
    /// </summary>
    public sealed class CompAsuranReplicatorReliquary : ThingComp
    {
        private const int TicksPerDay = 60000;
        private int releaseTick = -1;
        private bool released;

        private CompProperties_AsuranReplicatorReliquary Props => (CompProperties_AsuranReplicatorReliquary)props;


        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureDeadline();
            TryRelease();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            TryRelease();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref releaseTick, "releaseTick", -1);
            Scribe_Values.Look(ref released, "released", false);
        }

        private void EnsureDeadline()
        {
            if (released || releaseTick >= 0 || Find.TickManager == null)
                return;

            int days = Props.releaseDelayDays.RandomInRange;
            releaseTick = Find.TickManager.TicksGame + (days * TicksPerDay);
        }

        private void TryRelease()
        {
            if (released || !parent.Spawned || parent.Map == null || Find.TickManager == null)
                return;
            EnsureDeadline();
            if (releaseTick < 0 || Find.TickManager.TicksGame < releaseTick)
                return;

            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(Props.hostileFactionDefName);
            PawnKindDef pawnKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.pawnKindDefName);
            Faction faction = factionDef == null ? null : Find.FactionManager.FirstFactionOfDef(factionDef);
            int count = Math.Max(1, Props.replicatorCount);
            if (faction == null || pawnKind == null)
                return;

            Map map = parent.Map;
            IntVec3 position = parent.Position;
            string sharedDomain = "statue:" + parent.thingIDNumber + ":" + releaseTick;
            List<Pawn> staged = new List<Pawn>(count);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    Pawn drone = PawnGenerator.GeneratePawn(pawnKind, faction);
                    drone.TryGetComp<CompReplicatorDomain>()?.AssignAutonomousDomain(sharedDomain);
                    if (!GenPlace.TryPlaceThing(drone, position, map, ThingPlaceMode.Near))
                    {
                        if (!drone.Destroyed)
                            drone.Destroy(DestroyMode.Vanish);
                        RollBack(staged);
                        return;
                    }
                    staged.Add(drone);
                }
            }
            catch (Exception ex)
            {
                RollBack(staged);
                Log.Error("[WNG] Asuran Replicator reliquary release failed; staged Drones were rolled back where possible: " + ex);
                return;
            }

            // Commit only after every real Drone has been generated, domain-assigned and placed.
            released = true;
            parent.Destroy(DestroyMode.Vanish);
            Messages.Message(
                "An Asuran reliquary collapses into five hostile Replicator Drones.",
                staged.FirstOrDefault(),
                MessageTypeDefOf.ThreatBig);
        }

        private static void RollBack(List<Pawn> staged)
        {
            if (staged == null)
                return;
            for (int i = 0; i < staged.Count; i++)
            {
                Pawn pawn = staged[i];
                if (pawn != null && !pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
            }
            staged.Clear();
        }
    }

    /// <summary>
    /// Generates exactly the Asuran sculpture Def supplied in XML, initializes its art quality,
    /// then minifies the exact building so its hidden comp state travels through trade intact.
    /// </summary>
    public sealed class StockGenerator_AsuranTrapStatue : StockGenerator
    {
        public ThingDef thingDef;
        public float chance = 0.15f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (thingDef == null || Rand.Value > chance)
                yield break;

            Thing statue = ThingMaker.MakeThing(thingDef);
            CompQuality quality = statue.TryGetComp<CompQuality>();
            quality?.SetQuality(QualityUtility.GenerateQualityTraderItem(), ArtGenerationContext.Outsider);
            Thing tradedThing = statue.TryMakeMinified();
            if (tradedThing != null)
                yield return tradedThing;
        }

        public override bool HandlesThingDef(ThingDef def)
        {
            return def == thingDef;
        }

        public override IEnumerable<string> ConfigErrors(TraderKindDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;
            if (thingDef == null)
                yield return "StockGenerator_AsuranTrapStatue requires thingDef.";
            else if (!thingDef.Minifiable)
                yield return thingDef.defName + " must be minifiable for Asuran artifact trade.";
            else if (!thingDef.tradeability.TraderCanSell())
                yield return thingDef.defName + " must be trader-sellable.";
        }
    }
}
