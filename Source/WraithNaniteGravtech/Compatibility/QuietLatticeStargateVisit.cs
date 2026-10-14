using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    internal static class QuietLatticeStargateVisitUtility
    {
        public const string QuietFactionDefName = "WNG_HumanFormEnclave";
        public const string StargateArrivalModeDefName = "StargateMod_StargateEnterMode";
        public const string StargateThingClassName = "StargatesMod.Building_Stargate";
        public const string HumanFormKindDefName = "WNG_HumanFormReplicator";
        public const string EngineerKindDefName = "WNG_PrecursorEngineer";
        public const string CourierDefName = "WNG_PuddleJumper_NPC";
        public const int DelegationSize = 3;
        public const int BoardingOrderIntervalTicks = 180;
        public const int BoardingGraceTicks = 1800;

        public static Faction QuietFaction
        {
            get
            {
                FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(QuietFactionDefName);
                return def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
            }
        }

        public static PawnsArrivalModeDef StargateArrivalMode =>
            DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(StargateArrivalModeDefName);

        public static bool IsResolvedHomeMapStargate(Map map, IntVec3 spawnCenter)
        {
            if (map == null || !spawnCenter.IsValid || !spawnCenter.InBounds(map))
                return false;

            List<Thing> things = map.thingGrid.ThingsListAtFast(spawnCenter);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing?.Map != map)
                    continue;
                if (string.Equals(thing.def?.thingClass?.FullName, StargateThingClassName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public static bool HasUsableStargate(Map map, Faction faction)
        {
            PawnsArrivalModeDef mode = StargateArrivalMode;
            if (map == null || faction == null || mode?.Worker == null)
                return false;

            IncidentParms probe = new IncidentParms
            {
                target = map,
                faction = faction,
                raidArrivalMode = mode
            };

            try
            {
                if (!mode.Worker.TryResolveRaidSpawnCenter(probe) || probe.raidArrivalMode != mode)
                    return false;

                // CatCraft intentionally includes linked pocket maps when it resolves a Stargate.
                // This WNG incident promises arrival on the requested player-home map, so reject a
                // linked-map gate instead of waiting forever for pawns to emerge on the wrong map.
                return IsResolvedHomeMapStargate(map, probe.spawnCenter);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] CatCraft Stargate availability probe failed safely: " + ex.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Optional CatCraft integration with no compile-time CatCraft reference. CatCraft's own
    /// PawnsArrivalModeDef resolves the gate, opens it and buffers the exact Pawn objects. WNG
    /// supplies only the Quiet-Lattice visit and later Odyssey-native physical courier departure.
    /// </summary>
    public sealed class IncidentWorker_QuietLatticeStargateVisit : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms.target is Map map) || !map.IsPlayerHome)
                return false;

            MapComponent_QuietLatticeStargateVisit component = map.GetComponent<MapComponent_QuietLatticeStargateVisit>();
            if (component == null || component.HasActiveVisit)
                return false;

            Faction quiet = QuietLatticeStargateVisitUtility.QuietFaction;
            if (quiet == null || quiet.HostileTo(Faction.OfPlayer))
                return false;

            if (DefDatabase<ThingDef>.GetNamedSilentFail(QuietLatticeStargateVisitUtility.CourierDefName) == null)
                return false;

            foreach (GameCondition condition in map.GameConditionManager.ActiveConditions)
                if (condition?.def?.preventNeutralVisitors == true)
                    return false;

            return QuietLatticeStargateVisitUtility.HasUsableStargate(map, quiet) && base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map) || !map.IsPlayerHome)
                return false;

            MapComponent_QuietLatticeStargateVisit component = map.GetComponent<MapComponent_QuietLatticeStargateVisit>();
            Faction quiet = QuietLatticeStargateVisitUtility.QuietFaction;
            PawnsArrivalModeDef stargateMode = QuietLatticeStargateVisitUtility.StargateArrivalMode;
            if (component == null || component.HasActiveVisit || quiet == null || quiet.HostileTo(Faction.OfPlayer) || stargateMode?.Worker == null)
                return false;

            parms.faction = quiet;
            parms.raidArrivalMode = stargateMode;
            try
            {
                if (!stargateMode.Worker.TryResolveRaidSpawnCenter(parms) || parms.raidArrivalMode != stargateMode)
                    return false; // CatCraft fell back to edge arrival: do not mislabel it a Stargate visit.
                if (!QuietLatticeStargateVisitUtility.IsResolvedHomeMapStargate(map, parms.spawnCenter))
                    return false; // CatCraft resolved a linked pocket-map Stargate, not this home map.
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Quiet Lattice Stargate visit could not resolve a CatCraft gate: " + ex.Message);
                return false;
            }

            List<Pawn> visitors = GenerateDelegation(quiet);
            if (visitors.Count != QuietLatticeStargateVisitUtility.DelegationSize)
            {
                DestroyUncommitted(visitors);
                return false;
            }

            try
            {
                // CatCraft now owns these exact Pawn objects in its receive buffer. Do not create a
                // Lord until they physically emerge; Lord ownership of buffered pawns is unsafe.
                stargateMode.Worker.Arrive(visitors, parms);

                int duration = Rand.RangeInclusive(12000, 18000);
                if (!component.TryBeginVisit(visitors, duration))
                    throw new InvalidOperationException("Map visit component rejected a newly committed delegation.");
            }
            catch (Exception ex)
            {
                // Arrive may already have transferred one or more exact pawns into CatCraft's
                // receive buffer. Never destroy or replace them after this transaction boundary.
                Log.Warning("[WNG] Quiet Lattice Stargate visit failed during CatCraft commit; exact generated visitors were not replaced: " + ex.Message);
                return true;
            }

            try
            {
                SendStandardLetter(def.letterLabel, def.letterText, def.letterDef, parms, new TargetInfo(parms.spawnCenter, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Quiet Lattice Stargate visit committed but arrival presentation failed: " + ex.Message);
            }
            return true;
        }

        private static List<Pawn> GenerateDelegation(Faction faction)
        {
            List<Pawn> pawns = new List<Pawn>();
            PawnKindDef engineer = DefDatabase<PawnKindDef>.GetNamedSilentFail(QuietLatticeStargateVisitUtility.EngineerKindDefName);
            PawnKindDef humanForm = DefDatabase<PawnKindDef>.GetNamedSilentFail(QuietLatticeStargateVisitUtility.HumanFormKindDefName);
            if (engineer == null || humanForm == null)
                return pawns;

            try
            {
                pawns.Add(PawnGenerator.GeneratePawn(engineer, faction));
                pawns.Add(PawnGenerator.GeneratePawn(humanForm, faction));
                pawns.Add(PawnGenerator.GeneratePawn(humanForm, faction));
                pawns.RemoveAll(p => p == null);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Quiet Lattice delegation generation failed: " + ex.Message);
            }
            return pawns;
        }

        private static void DestroyUncommitted(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns ?? Enumerable.Empty<Pawn>())
                if (pawn != null && !pawn.Destroyed)
                    pawn.Destroy(DestroyMode.Vanish);
        }
    }

    /// <summary>
    /// Tracks the exact CatCraft-arrived visitor objects until their separate physical Puddle
    /// Jumper extraction. The component never dials a gate, owns an address or touches CatCraft
    /// receive-buffer internals.
    /// </summary>
    public sealed class MapComponent_QuietLatticeStargateVisit : MapComponent
    {
        private List<Pawn> visitors = new List<Pawn>();
        private Lord visitorLord;
        private Building_PassengerShuttle courier;
        private int visitDurationTicks;
        private int visitStartedTick = -1;
        private int boardingStartedTick = -1;
        private int nextBoardingOrderTick;
        private bool launchIssued;

        public MapComponent_QuietLatticeStargateVisit(Map map) : base(map) { }

        public bool HasActiveVisit =>
            (visitors != null && visitors.Any(p => p != null && !p.Dead && !p.Destroyed)) ||
            (courier != null && !courier.Destroyed);

        public bool TryBeginVisit(List<Pawn> exactVisitors, int durationTicks)
        {
            if (HasActiveVisit || exactVisitors == null || exactVisitors.Count == 0)
                return false;

            visitors = new List<Pawn>(exactVisitors);
            visitorLord = null;
            courier = null;
            visitDurationTicks = Math.Max(6000, durationTicks);
            visitStartedTick = -1;
            boardingStartedTick = -1;
            nextBoardingOrderTick = 0;
            launchIssued = false;
            return true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            if (!map.IsHashIntervalTick(60) || visitors == null || visitors.Count == 0)
                return;

            // Dead delegates must not hold the receive-buffer transaction open forever.
            visitors.RemoveAll(p => p == null || p.Dead || p.Destroyed);
            if (visitors.Count == 0)
            {
                ClearVisit();
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            List<Pawn> spawnedHere = visitors.Where(p => p.Spawned && p.Map == map).ToList();

            // CatCraft buffers the exact Pawn objects during its dial sequence. Assign their
            // ordinary visitor Lord only after every still-living delegate has physically emerged.
            if (visitorLord == null)
            {
                if (spawnedHere.Count != visitors.Count)
                    return;

                Faction visitFaction = QuietLatticeStargateVisitUtility.QuietFaction ?? visitors[0].Faction;
                if (visitFaction == null)
                {
                    ClearVisit();
                    return;
                }

                try
                {
                    IntVec3 chillSpot = CellFinder.RandomClosewalkCellNear(map.Center, map, 18);
                    visitorLord = LordMaker.MakeNewLord(
                        visitFaction,
                        new LordJob_VisitColony(visitFaction, chillSpot, 60000),
                        map,
                        spawnedHere);
                    visitStartedTick = now;
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Quiet Lattice delegates emerged but could not enter native visitor behavior: " + ex.Message);
                    RestoreNativeExitForStrandedVisitors(spawnedHere, visitFaction);
                    ClearVisit();
                    return;
                }
            }
            else if (visitStartedTick < 0)
            {
                // Save-compatibility guard for a Lord reference restored without the start tick.
                visitStartedTick = now;
            }

            Faction quiet = QuietLatticeStargateVisitUtility.QuietFaction;
            bool relationsFailed = quiet == null || quiet.HostileTo(Faction.OfPlayer);
            bool scheduledDeparture = now - visitStartedTick >= visitDurationTicks;

            if (courier == null)
            {
                if (!relationsFailed && !scheduledDeparture)
                    return;
                if (!TryStageCourier(quiet ?? visitors[0].Faction, spawnedHere, now))
                {
                    // If a physical courier cannot be staged, return the survivors to native
                    // visitor exit behavior rather than inventing a second transport system.
                    RestoreNativeExitForStrandedVisitors(spawnedHere, quiet ?? visitors[0].Faction);
                    ClearVisit();
                }
                return;
            }

            if (courier.Destroyed || !courier.Spawned)
            {
                if (!launchIssued)
                {
                    RestoreNativeExitForStrandedVisitors(
                        visitors.Where(p => p.Spawned && p.Map == map).ToList(),
                        quiet ?? visitors.FirstOrDefault()?.Faction);
                }
                ClearVisit();
                return;
            }

            if (now >= nextBoardingOrderTick)
            {
                nextBoardingOrderTick = now + QuietLatticeStargateVisitUtility.BoardingOrderIntervalTicks;
                DirectVisitorsToCourier();
            }

            CompTransporter transporter = courier.TryGetComp<CompTransporter>();
            CompShuttle shuttle = courier.TryGetComp<CompShuttle>();
            if (transporter == null || shuttle == null)
                return;

            ThingOwner held = transporter.GetDirectlyHeldThings();
            bool allRequiredLoaded = shuttle.requiredPawns.All(p => p != null && held.Contains(p));
            bool graceExpired = boardingStartedTick >= 0 && now - boardingStartedTick >= QuietLatticeStargateVisitUtility.BoardingGraceTicks;

            if (allRequiredLoaded || graceExpired)
                TryLaunchCourier(transporter, graceExpired);
        }

        private bool TryStageCourier(Faction faction, List<Pawn> spawnedVisitors, int now)
        {
            ThingDef courierDef = DefDatabase<ThingDef>.GetNamedSilentFail(QuietLatticeStargateVisitUtility.CourierDefName);
            if (courierDef == null || faction == null || spawnedVisitors == null || spawnedVisitors.Count == 0)
                return false;

            Building_PassengerShuttle staged = null;
            try
            {
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, 0f))
                    entry = CellFinder.RandomEdgeCell(map);

                staged = ThingMaker.MakeThing(courierDef) as Building_PassengerShuttle;
                if (staged == null)
                    throw new InvalidOperationException("NPC Puddle Jumper Def did not create Building_PassengerShuttle.");
                staged.SetFaction(faction);
                if (!GenPlace.TryPlaceThing(staged, entry, map, ThingPlaceMode.Near, rot: Rot4.East, squareRadius: 10))
                    throw new InvalidOperationException("No valid placement for Quiet Lattice Puddle Jumper courier.");

                CompRefuelable fuel = staged.TryGetComp<CompRefuelable>();
                CompTransporter transporter = staged.TryGetComp<CompTransporter>();
                CompShuttle shuttle = staged.TryGetComp<CompShuttle>();
                if (fuel == null || transporter == null || shuttle == null || staged.TryGetComp<CompLaunchable>() == null)
                    throw new InvalidOperationException("NPC Puddle Jumper is missing an Odyssey shuttle contract component.");

                fuel.Refuel(fuel.Props.fuelCapacity);
                shuttle.requiredPawns.Clear();
                foreach (Pawn pawn in spawnedVisitors)
                {
                    // Downed/stranded delegates remain under ordinary visitor-exit ownership;
                    // never make a non-boardable pawn a hard shuttle requirement.
                    if (EligibleForCourier(pawn) && !pawn.Downed)
                        shuttle.requiredPawns.AddUnique(pawn);
                }
                TransporterUtility.InitiateLoading(Gen.YieldSingle(transporter));
            }
            catch (Exception ex)
            {
                if (staged != null && !staged.Destroyed)
                    staged.Destroy(DestroyMode.Vanish);
                Log.Warning("[WNG] Quiet Lattice return courier could not stage: " + ex.Message);
                return false;
            }

            courier = staged;
            boardingStartedTick = now;
            nextBoardingOrderTick = now;
            launchIssued = false;

            CompShuttle stagedShuttle = courier.TryGetComp<CompShuttle>();
            foreach (Pawn pawn in spawnedVisitors)
            {
                if (stagedShuttle != null && stagedShuttle.requiredPawns.Contains(pawn))
                    pawn.GetLord()?.RemovePawn(pawn);
            }

            try
            {
                Messages.Message("The Quiet Lattice delegation's Puddle Jumper courier has arrived for departure.", courier, MessageTypeDefOf.NeutralEvent, false);
            }
            catch { }
            return true;
        }

        private bool EligibleForCourier(Pawn pawn)
        {
            // Player custody is a real state transition, not something this visit may silently
            // override. Arrested/enslaved delegates remain on the map and are not re-lorded.
            return pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.Spawned && pawn.Map == map &&
                !pawn.IsPrisoner && !pawn.IsSlave;
        }

        private void DirectVisitorsToCourier()
        {
            if (courier == null || courier.Destroyed || !courier.Spawned)
                return;

            CompShuttle shuttle = courier.TryGetComp<CompShuttle>();
            if (shuttle == null)
                return;

            foreach (Pawn pawn in visitors)
            {
                if (!EligibleForCourier(pawn) || pawn.Downed || pawn.jobs == null || !shuttle.requiredPawns.Contains(pawn))
                    continue;
                if (pawn.CurJobDef == JobDefOf.EnterTransporter && pawn.CurJob?.targetA.Thing == courier)
                    continue;

                Job board = JobMaker.MakeJob(JobDefOf.EnterTransporter, courier);
                board.playerForced = true;
                if (!pawn.jobs.TryTakeOrderedJob(board, JobTag.Misc))
                {
                    // A failed order never leaves the native shuttle permanently waiting for a
                    // pawn it cannot load. Return that exact pawn to ordinary visitor exit.
                    shuttle.requiredPawns.Remove(pawn);
                    RestoreNativeExitForStrandedVisitors(
                        new List<Pawn> { pawn },
                        QuietLatticeStargateVisitUtility.QuietFaction ?? pawn.Faction);
                }
            }
        }

        private void TryLaunchCourier(CompTransporter transporter, bool graceExpired)
        {
            if (courier == null || courier.Destroyed || !courier.Spawned || transporter == null || launchIssued)
                return;

            CompShuttle shuttle = courier.TryGetComp<CompShuttle>();
            CompLaunchable launchable = courier.TryGetComp<CompLaunchable>();
            if (shuttle == null || launchable == null)
                return;

            ThingOwner held = transporter.GetDirectlyHeldThings();

            // At the grace boundary, any required pawn that never reached the transporter becomes
            // an ordinary stranded visitor instead of blocking or being silently deleted.
            if (graceExpired)
            {
                List<Pawn> expiredRequirements = shuttle.requiredPawns
                    .Where(p => p == null || !held.Contains(p))
                    .Where(p => p != null)
                    .ToList();
                foreach (Pawn pawn in expiredRequirements)
                    shuttle.requiredPawns.Remove(pawn);
                RestoreNativeExitForStrandedVisitors(
                    expiredRequirements.Where(EligibleForCourier).ToList(),
                    QuietLatticeStargateVisitUtility.QuietFaction ?? expiredRequirements.FirstOrDefault()?.Faction);
            }

            // A launch is valid only when every remaining exact native requirement is physically
            // inside the transporter. This is independent of CompLaunchable's broader NPC check.
            if (shuttle.requiredPawns.Any(p => p == null || !held.Contains(p)))
                return;

            List<Pawn> stranded = visitors
                .Where(p => EligibleForCourier(p) && !held.Contains(p))
                .ToList();
            if (stranded.Count > 0)
            {
                foreach (Pawn pawn in stranded)
                    shuttle.requiredPawns.Remove(pawn);
                RestoreNativeExitForStrandedVisitors(
                    stranded,
                    QuietLatticeStargateVisitUtility.QuietFaction ?? stranded[0].Faction);
            }

            if (!launchable.CanLaunch().Accepted)
                return;

            List<Pawn> boardedBeforeLaunch = visitors
                .Where(p => p != null && held.Contains(p))
                .ToList();
            PlanetTile destination = FindDepartureDestination();
            IntVec3 departureCell = courier.Position;
            launchIssued = true;
            Exception launchException = null;
            try
            {
                launchable.TryLaunch(destination, new TransportersArrivalAction_QuietLatticeDeparture());
            }
            catch (Exception ex)
            {
                launchException = ex;
            }

            // A launch only commits when the physical shuttle has left and every exact visitor
            // that was aboard has moved out of the old map transporter into native flight/transit.
            bool departedWithBoardedVisitors = !courier.Spawned && boardedBeforeLaunch.All(p =>
                p != null && !p.Spawned && p.ParentHolder != null && !held.Contains(p));
            if (!departedWithBoardedVisitors)
            {
                launchIssued = false;
                if (launchException != null)
                    Log.Warning("[WNG] Quiet Lattice courier launch failed before physical departure: " + launchException.Message);
                return;
            }

            if (launchException != null)
                Log.Warning("[WNG] Quiet Lattice courier reported an exception after physical departure; the exact boarded visitors remain in native transport: " + launchException.Message);

            try
            {
                Messages.Message("The Quiet Lattice Puddle Jumper has departed with the delegation members who reached the courier.", new TargetInfo(departureCell, map), MessageTypeDefOf.NeutralEvent, false);
            }
            catch { }
            ClearVisit();
        }

        private PlanetTile FindDepartureDestination()
        {
            if (TileFinder.TryFindNewSiteTile(out PlanetTile destination, 1, 4, allowCaravans: false))
                return destination;
            return map.Tile;
        }

        private void RestoreNativeExitForStrandedVisitors(List<Pawn> stranded, Faction faction)
        {
            if (stranded == null || stranded.Count == 0 || faction == null)
                return;
            List<Pawn> unassigned = stranded
                .Where(p => EligibleForCourier(p) && p.GetLord() == null)
                .ToList();
            if (unassigned.Count == 0)
                return;
            try
            {
                IntVec3 chillSpot = CellFinder.RandomClosewalkCellNear(map.Center, map, 18);
                LordMaker.MakeNewLord(faction, new LordJob_VisitColony(faction, chillSpot, 600), map, unassigned);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Stranded Quiet Lattice visitors could not be returned to native visitor exit behavior: " + ex.Message);
            }
        }

        private void ClearVisit()
        {
            visitors = visitors ?? new List<Pawn>();
            visitors.Clear();
            visitorLord = null;
            courier = null;
            visitDurationTicks = 0;
            visitStartedTick = -1;
            boardingStartedTick = -1;
            nextBoardingOrderTick = 0;
            launchIssued = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref visitors, "wngQuietLatticeVisitors", LookMode.Reference);
            Scribe_References.Look(ref visitorLord, "wngQuietLatticeVisitorLord");
            Scribe_References.Look(ref courier, "wngQuietLatticeCourier");
            Scribe_Values.Look(ref visitDurationTicks, "wngQuietLatticeDurationTicks", 0);
            Scribe_Values.Look(ref visitStartedTick, "wngQuietLatticeVisitStartedTick", -1);
            Scribe_Values.Look(ref boardingStartedTick, "wngQuietLatticeBoardingStartedTick", -1);
            Scribe_Values.Look(ref nextBoardingOrderTick, "wngQuietLatticeNextBoardingOrderTick", 0);
            Scribe_Values.Look(ref launchIssued, "wngQuietLatticeLaunchIssued", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                visitors = visitors ?? new List<Pawn>();
        }
    }

    /// <summary>
    /// Off-map cleanup for the NPC visitor flight. Exact Pawns leave native flight ownership by
    /// reference; the hidden courier is removed with ActiveTransporterInfo's native shuttle API.
    /// </summary>
    public sealed class TransportersArrivalAction_QuietLatticeDeparture : TransportersArrivalAction
    {
        public override bool GeneratesMap => false;

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            foreach (ActiveTransporterInfo info in transporters ?? new List<ActiveTransporterInfo>())
            {
                ThingOwner contents = info?.innerContainer;
                if (info == null || contents == null)
                    continue;

                // Native launch already calls Pawn.ExitMap, so these pawns are normally already
                // registered as WorldPawns. Remove the exact references from flight ownership first
                // and only use PassToWorld as a defensive fallback.
                foreach (Pawn pawn in contents.OfType<Pawn>().ToList())
                {
                    contents.Remove(pawn);
                    if (!Find.WorldPawns.Contains(pawn))
                        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
                }

                // The scripted NPC courier must not become player cargo or be handed to the world.
                // Use the native shuttle removal contract before disposing of the exact hidden craft.
                Thing hiddenShuttle = info.GetShuttle();
                if (hiddenShuttle != null)
                {
                    hiddenShuttle = info.RemoveShuttle();
                    if (hiddenShuttle != null && !hiddenShuttle.Destroyed)
                        hiddenShuttle.Destroy(DestroyMode.Vanish);
                }

                // The courier is not intended to carry arbitrary cargo. Clean any unexpected
                // non-pawn leftovers only after all exact pawn and shuttle references are detached.
                foreach (Thing thing in contents.ToList())
                {
                    contents.Remove(thing);
                    if (thing != null && !thing.Destroyed)
                        thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}
