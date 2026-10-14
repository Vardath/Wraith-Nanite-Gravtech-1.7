using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    internal static class ReplicatorQueenRecoveryUtility
    {
        public const int InitialRecoveryDelayTicks = 2500;
        public const int MissionOrderIntervalTicks = 180;
        public const int SurvivorBoardingGraceTicks = 600;
        public const int RecoveryTeamSize = 4;
        public const string HostileFactionDefName = "WNG_PrecursorCollective";
        public const string OperativeKindDefName = "WNG_HumanFormReplicator";
        public const string CarrierDefName = "WNG_AsuranQueenRecoveryCarrier";

        public static Faction HostileFaction
        {
            get
            {
                FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(HostileFactionDefName);
                return def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
            }
        }
    }

    /// <summary>
    /// Physical recovery operation for the exact Queen. The first and recurring attempts share this
    /// one transaction path: four operatives and one real Odyssey transporter stage all-or-nothing;
    /// subdual/carry/load are not capture; only native physical shuttle departure with the exact Queen
    /// aboard commits capture. Recurring attempts can only be claimed by the map physically holding her.
    /// </summary>
    public sealed class MapComponent_ReplicatorQueenRecovery : MapComponent
    {
        private Building_PassengerShuttle carrier;
        private List<Pawn> operatives = new List<Pawn>();
        private int nextOrderTick;
        private int queenLoadedTick = -1;
        private bool launchIssued;
        private bool recurringMission;
        private int abortStartedTick = -1;

        public MapComponent_ReplicatorQueenRecovery(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            if (!map.IsHashIntervalTick(60))
                return;

            GameComponent_ReplicatorQueenState state = ReplicatorQueenUtility.State;
            Pawn queen = state?.ExactQueen;
            if (state == null || queen == null || state.Captured)
            {
                ClearLocalMission();
                return;
            }

            if (queen.Dead)
            {
                if (state.RecoveryOwnedByMap(map.uniqueID, out bool _))
                    state.MarkRecoveryEndedByQueenDeath();
                ClearLocalMission();
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (!state.FirstRecoveryStarted && !state.RecurringRecoveryStarted)
            {
                // A due recovery attempt waits globally until the exact Queen is physically present
                // on a player map. No other player map can claim or receive the mission while she is
                // caravaning, held off-map, or physically elsewhere.
                if (!queen.Spawned || queen.Map != map || queen.Faction != Faction.OfPlayer)
                    return;

                if (state.FirstRecoveryReady(now))
                    TryStartMission(state, queen, now, recurring: false);
                else if (state.RecurringRecoveryReady(now))
                    TryStartMission(state, queen, now, recurring: true);
                return;
            }

            if (!state.RecoveryOwnedByMap(map.uniqueID, out bool ownedRecurring))
                return;
            recurringMission = ownedRecurring;

            if (carrier == null || carrier.Destroyed || !carrier.Spawned)
            {
                if (!launchIssued)
                {
                    RecoverQueenFromFailedCarrier(queen);
                    ResolveMissionDefeat(state);
                    PresentDefeatBestEffort(queen, targetMoved: false);
                }
                ClearLocalMission();
                return;
            }

            operatives = operatives ?? new List<Pawn>();
            operatives.RemoveAll(p => p == null || p.Dead || p.Destroyed);

            CompTransporter transporter = carrier.TryGetComp<CompTransporter>();
            bool queenLoaded = transporter != null && transporter.GetDirectlyHeldThings().Contains(queen);
            if (queenLoaded && queenLoadedTick < 0)
                queenLoadedTick = now;

            bool queenStillPhysicalTarget = queenLoaded || (queen.Spawned && queen.Map == map && queen.Faction == Faction.OfPlayer);
            if (!queenStillPhysicalTarget)
            {
                BeginMissionAbort(now, transporter);
                if (now >= nextOrderTick)
                {
                    nextOrderTick = now + ReplicatorQueenRecoveryUtility.MissionOrderIntervalTicks;
                    DirectSurvivorsToCarrier();
                }
                if (!launchIssued && abortStartedTick >= 0 && now - abortStartedTick >= ReplicatorQueenRecoveryUtility.SurvivorBoardingGraceTicks)
                    TryLaunchRetreat(state, transporter);
                return;
            }

            if (!queenLoaded && operatives.Count == 0)
            {
                ResolveMissionDefeat(state);
                PresentDefeatBestEffort(queen, targetMoved: false);
                ClearLocalMission();
                return;
            }

            if (now >= nextOrderTick)
            {
                nextOrderTick = now + ReplicatorQueenRecoveryUtility.MissionOrderIntervalTicks;
                DirectMission(queen, transporter, queenLoaded);
            }

            if (queenLoaded && !launchIssued && now - queenLoadedTick >= ReplicatorQueenRecoveryUtility.SurvivorBoardingGraceTicks)
                TryLaunchCapturedQueen(state, queen, transporter);
        }

        private void TryStartMission(GameComponent_ReplicatorQueenState state, Pawn queen, int now, bool recurring)
        {
            Faction hostile = ReplicatorQueenRecoveryUtility.HostileFaction;
            PawnKindDef operativeKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(ReplicatorQueenRecoveryUtility.OperativeKindDefName);
            ThingDef carrierDef = DefDatabase<ThingDef>.GetNamedSilentFail(ReplicatorQueenRecoveryUtility.CarrierDefName);
            if (hostile == null || operativeKind == null || carrierDef == null)
                return;

            bool claimed = recurring
                ? state.TryClaimRecurringRecovery(map.uniqueID)
                : state.TryClaimFirstRecovery(map.uniqueID);
            if (!claimed)
                return;

            List<Pawn> stagedOperatives = new List<Pawn>();
            Building_PassengerShuttle stagedCarrier = null;
            try
            {
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, 0f))
                    entry = CellFinder.RandomEdgeCell(map);

                stagedCarrier = ThingMaker.MakeThing(carrierDef) as Building_PassengerShuttle;
                if (stagedCarrier == null)
                    throw new InvalidOperationException("Recovery carrier Def did not create Building_PassengerShuttle.");
                stagedCarrier.SetFaction(hostile);
                if (!GenPlace.TryPlaceThing(stagedCarrier, entry, map, ThingPlaceMode.Near, rot: Rot4.East, squareRadius: 10))
                    throw new InvalidOperationException("No valid edge placement for recovery carrier.");

                CompRefuelable fuel = stagedCarrier.TryGetComp<CompRefuelable>();
                if (fuel == null)
                    throw new InvalidOperationException("Recovery carrier has no native refuelable component.");
                fuel.Refuel(fuel.Props.fuelCapacity);

                for (int i = 0; i < ReplicatorQueenRecoveryUtility.RecoveryTeamSize; i++)
                {
                    Pawn operative = PawnGenerator.GeneratePawn(operativeKind, hostile);
                    if (operative == null)
                        throw new InvalidOperationException("Failed to generate recovery operative " + (i + 1) + ".");
                    IntVec3 cell = CellFinder.RandomClosewalkCellNear(stagedCarrier.Position, map, 8);
                    GenSpawn.Spawn(operative, cell, map);
                    stagedOperatives.Add(operative);
                }
            }
            catch (Exception ex)
            {
                foreach (Pawn pawn in stagedOperatives)
                    if (pawn != null && !pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                if (stagedCarrier != null && !stagedCarrier.Destroyed)
                    stagedCarrier.Destroy(DestroyMode.Vanish);
                if (recurring)
                    state.RollBackRecurringRecoveryClaim(1200);
                else
                    state.RollBackFirstRecoveryClaim(1200);
                Log.Warning("[WNG] " + (recurring ? "Recurring" : "First") + " Replicator Queen recovery could not stage all four operatives plus carrier; mission claim rolled back: " + ex.Message);
                return;
            }

            carrier = stagedCarrier;
            operatives = stagedOperatives;
            nextOrderTick = now;
            queenLoadedTick = -1;
            launchIssued = false;
            recurringMission = recurring;
            abortStartedTick = -1;

            try
            {
                Find.LetterStack.ReceiveLetter(
                    recurring ? "Lattice recovery team returns" : "Lattice recovery team",
                    recurring
                        ? "Another four-operative Lattice recovery team has arrived on the map physically containing the exact Replicator Queen. The capture rules are unchanged: they must subdue, carry and load that same Pawn, and only a real shuttle departure commits capture."
                        : "Four human-form Lattice operatives have arrived with a recovery shuttle. They are focused on the exact Replicator Queen. Their doctrine is capture: disable her, carry the same pawn into the carrier, then leave. Downing or loading her is not capture; stopping the shuttle before departure keeps her recoverable.",
                    LetterDefOf.ThreatBig,
                    stagedCarrier);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Queen recovery mission committed but arrival presentation failed: " + ex.Message);
            }
        }

        private void DirectMission(Pawn queen, CompTransporter transporter, bool queenLoaded)
        {
            if (queenLoaded)
            {
                DirectSurvivorsToCarrier();
                return;
            }

            if (!queen.Spawned || queen.Map != map)
                return;

            bool loaderAssigned = operatives.Any(p => p != null && p.Spawned && p.CurJobDef?.defName == "WNG_LoadReplicatorQueenIntoCarrier");
            bool subduerAssigned = operatives.Any(p => p != null && p.Spawned && p.CurJobDef?.defName == "WNG_SubdueReplicatorQueen");

            foreach (Pawn operative in operatives.OrderBy(p => p.thingIDNumber))
            {
                if (!CanReceiveOrder(operative))
                    continue;

                if (queen.Downed)
                {
                    if (loaderAssigned || carrier == null || carrier.Destroyed || !carrier.Spawned)
                        continue;
                    JobDef loadDef = DefDatabase<JobDef>.GetNamedSilentFail("WNG_LoadReplicatorQueenIntoCarrier");
                    if (loadDef == null)
                        continue;
                    Job job = JobMaker.MakeJob(loadDef, queen, carrier);
                    if (operative.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                        loaderAssigned = true;
                }
                else if (!subduerAssigned)
                {
                    JobDef subdueDef = DefDatabase<JobDef>.GetNamedSilentFail("WNG_SubdueReplicatorQueen");
                    if (subdueDef == null)
                        continue;
                    Job job = JobMaker.MakeJob(subdueDef, queen);
                    if (operative.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                        subduerAssigned = true;
                }
            }
        }

        private void BeginMissionAbort(int now, CompTransporter transporter)
        {
            if (abortStartedTick < 0)
            {
                abortStartedTick = now;
                try
                {
                    if (transporter != null && !transporter.LoadingInProgressOrReadyToLaunch)
                        TransporterUtility.InitiateLoading(Gen.YieldSingle(transporter));
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Queen recovery target left the mission map; carrier loading could not begin yet: " + ex.Message);
                }
                try
                {
                    Messages.Message("The exact Replicator Queen is no longer physically on the recovery map. The Lattice team is aborting rather than redirecting to a different map or proxy target.", carrier, MessageTypeDefOf.NeutralEvent, false);
                }
                catch { }
            }
            DirectSurvivorsToCarrier();
        }

        private void DirectSurvivorsToCarrier()
        {
            if (carrier == null || carrier.Destroyed || !carrier.Spawned)
                return;
            foreach (Pawn operative in operatives)
            {
                if (!CanReceiveOrder(operative))
                    continue;
                if (operative.CurJobDef == JobDefOf.EnterTransporter && operative.CurJob?.targetA.Thing == carrier)
                    continue;
                Job board = JobMaker.MakeJob(JobDefOf.EnterTransporter, carrier);
                board.playerForced = true;
                operative.jobs.TryTakeOrderedJob(board, JobTag.Misc);
            }
        }

        private void TryLaunchCapturedQueen(GameComponent_ReplicatorQueenState state, Pawn queen, CompTransporter transporter)
        {
            if (carrier == null || carrier.Destroyed || !carrier.Spawned || transporter == null ||
                !transporter.GetDirectlyHeldThings().Contains(queen))
                return;

            CompLaunchable launchable = carrier.TryGetComp<CompLaunchable>();
            if (launchable == null || !launchable.CanLaunch().Accepted)
                return;

            PlanetTile destination = FindWithdrawalDestination();
            IntVec3 departureCell = carrier.Position;
            Faction captor = carrier.Faction ?? ReplicatorQueenRecoveryUtility.HostileFaction;
            if (captor == null)
                return;

            launchIssued = true;
            Exception launchException = null;
            try
            {
                launchable.TryLaunch(destination, new TransportersArrivalAction_ReplicatorQueenRecovery(captor));
            }
            catch (Exception ex)
            {
                launchException = ex;
            }

            // Native CompLaunchable moves the exact transporter contents into an ActiveTransporter
            // and despawns the shuttle as the leaving skyfaller is created. That physical transfer
            // is the capture-departure boundary. An exception alone is not rollback authority: if
            // the exact Queen already left the map in native transport, capture must still commit.
            bool departedWithQueen = !carrier.Spawned && !queen.Spawned &&
                queen.ParentHolder != null && !transporter.GetDirectlyHeldThings().Contains(queen);
            if (!departedWithQueen)
            {
                launchIssued = false;
                if (launchException != null)
                    Log.Warning("[WNG] Queen recovery shuttle launch failed before physical departure; exact Queen remains uncommitted: " + launchException.Message);
                return;
            }

            if (launchException != null)
                Log.Warning("[WNG] Queen recovery shuttle reported an exception after physical departure; capture remains committed because the exact Queen already left in native transport: " + launchException.Message);

            state.MarkCaptureDeparture(captor);
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Replicator Queen captured",
                    "The recovery shuttle has physically departed with the exact Replicator Queen aboard. Her identity is preserved in transit; the Lattice Collective now holds her only because the real carrier successfully left the map.",
                    LetterDefOf.ThreatBig,
                    new TargetInfo(departureCell, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Queen capture departure committed but presentation failed: " + ex.Message);
            }
            ClearLocalMission();
        }

        private void TryLaunchRetreat(GameComponent_ReplicatorQueenState state, CompTransporter transporter)
        {
            if (carrier == null || carrier.Destroyed || !carrier.Spawned || transporter == null)
                return;
            if (!transporter.LoadingInProgressOrReadyToLaunch)
            {
                try { TransporterUtility.InitiateLoading(Gen.YieldSingle(transporter)); }
                catch { return; }
            }

            CompLaunchable launchable = carrier.TryGetComp<CompLaunchable>();
            if (launchable == null || !launchable.CanLaunch().Accepted)
                return;

            PlanetTile destination = FindWithdrawalDestination();
            launchIssued = true;
            Exception launchException = null;
            try
            {
                launchable.TryLaunch(destination, new TransportersArrivalAction_ReplicatorQueenRecoveryRetreat());
            }
            catch (Exception ex)
            {
                launchException = ex;
            }

            bool departed = !carrier.Spawned;
            if (!departed)
            {
                launchIssued = false;
                if (launchException != null)
                    Log.Warning("[WNG] Aborting Queen recovery shuttle failed before departure; mission remains active for retry: " + launchException.Message);
                return;
            }

            if (launchException != null)
                Log.Warning("[WNG] Aborting Queen recovery shuttle reported an exception after physical departure; mission defeat still commits because the carrier already left: " + launchException.Message);
            ResolveMissionDefeat(state);
            PresentDefeatBestEffort(ReplicatorQueenUtility.State?.ExactQueen, targetMoved: true);
            ClearLocalMission();
        }

        private PlanetTile FindWithdrawalDestination()
        {
            if (TileFinder.TryFindNewSiteTile(out PlanetTile destination, 1, 4, allowCaravans: false))
                return destination;
            return map.Tile;
        }

        private void ResolveMissionDefeat(GameComponent_ReplicatorQueenState state)
        {
            if (state == null)
                return;
            if (recurringMission)
                state.MarkRecurringRecoveryDefeated();
            else
                state.MarkFirstRecoveryDefeated();
        }

        private void RecoverQueenFromFailedCarrier(Pawn queen)
        {
            if (queen == null || queen.Dead)
                return;
            try
            {
                if (!queen.Spawned && queen.ParentHolder is CompTransporter transporter && transporter.parent?.MapHeld == map)
                    transporter.GetDirectlyHeldThings().TryDrop(queen, transporter.parent.PositionHeld, map, ThingPlaceMode.Near, 1, out Thing _);
                if (queen.Faction != Faction.OfPlayer && (queen.Spawned && queen.Map == map))
                    queen.SetFaction(Faction.OfPlayer);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Failed recovery carrier cleanup could not fully restore exact Queen to player control: " + ex.Message);
            }
        }

        private static bool CanReceiveOrder(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.Spawned && pawn.jobs != null;
        }

        private void PresentDefeatBestEffort(Pawn queen, bool targetMoved)
        {
            try
            {
                string message = targetMoved
                    ? "The Lattice recovery attempt has aborted because the exact Replicator Queen is no longer physically on that map. No capture state was committed."
                    : (recurringMission
                        ? "The recurring Lattice recovery attempt has failed. The exact Replicator Queen remains recoverable and another attempt may occur after the configured cadence."
                        : "The first Lattice recovery attempt has failed. The exact Replicator Queen remains recoverable and later recovery attempts may occur after the configured cadence.");
                Messages.Message(message, queen, MessageTypeDefOf.PositiveEvent, true);
            }
            catch { }
        }

        private void ClearLocalMission()
        {
            carrier = null;
            operatives = operatives ?? new List<Pawn>();
            operatives.Clear();
            nextOrderTick = 0;
            queenLoadedTick = -1;
            launchIssued = false;
            recurringMission = false;
            abortStartedTick = -1;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref carrier, "wngQueenRecoveryCarrier");
            Scribe_Collections.Look(ref operatives, "wngQueenRecoveryOperatives", LookMode.Reference);
            Scribe_Values.Look(ref nextOrderTick, "wngQueenRecoveryNextOrderTick", 0);
            Scribe_Values.Look(ref queenLoadedTick, "wngQueenRecoveryLoadedTick", -1);
            Scribe_Values.Look(ref launchIssued, "wngQueenRecoveryLaunchIssued", false);
            Scribe_Values.Look(ref recurringMission, "wngQueenRecoveryRecurringMission", false);
            Scribe_Values.Look(ref abortStartedTick, "wngQueenRecoveryAbortStartedTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                operatives = operatives ?? new List<Pawn>();
                if (abortStartedTick < -1) abortStartedTick = -1;
            }
        }
    }

    public sealed class JobDriver_SubdueReplicatorQueen : JobDriver
    {
        private Pawn Queen => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Queen != null && pawn.Reserve(Queen, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Queen == null || Queen.Dead || !ReplicatorQueenUtility.IsExactQueen(Queen));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(180);

            Toil apply = ToilMaker.MakeToil("WNG_NonlethalQueenSubdual");
            apply.initAction = delegate
            {
                Pawn queen = Queen;
                if (queen == null || queen.Dead || queen.Downed)
                    return;
                HediffDef anesthetic = DefDatabase<HediffDef>.GetNamedSilentFail("Anesthetic");
                if (anesthetic == null)
                    return;
                Hediff hediff = queen.health?.hediffSet?.GetFirstHediffOfDef(anesthetic);
                if (hediff == null)
                {
                    hediff = HediffMaker.MakeHediff(anesthetic, queen);
                    queen.health.AddHediff(hediff);
                }
                hediff.Severity = Math.Max(1f, hediff.Severity);
                queen.stances?.stunner?.StunFor(120, pawn, addBattleLog: false);
            };
            apply.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return apply;
        }
    }

    public sealed class JobDriver_LoadReplicatorQueenIntoCarrier : JobDriver
    {
        private Pawn Queen => job.GetTarget(TargetIndex.A).Pawn;
        private Thing Carrier => job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Queen != null && Carrier != null && pawn.Reserve(Queen, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(() => Queen == null || Queen.Dead || !Queen.Downed || !ReplicatorQueenUtility.IsExactQueen(Queen));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            Toil load = ToilMaker.MakeToil("WNG_LoadExactQueenIntoRecoveryCarrier");
            load.initAction = delegate
            {
                Pawn queen = pawn.carryTracker?.CarriedThing as Pawn;
                CompTransporter transporter = Carrier?.TryGetComp<CompTransporter>();
                if (queen == null || transporter == null || !ReplicatorQueenUtility.IsExactQueen(queen))
                    return;
                if (!transporter.LoadingInProgressOrReadyToLaunch)
                    TransporterUtility.InitiateLoading(Gen.YieldSingle(transporter));
                int moved = pawn.carryTracker.innerContainer.TryTransferToContainer(
                    queen, transporter.GetDirectlyHeldThings(), 1);
                if (moved != 1)
                    Log.Warning("[WNG] Exact Queen reached recovery carrier but could not be transferred into its native CompTransporter.");
            };
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }

    /// <summary>
    /// Destination cleanup for the NPC-only recovery flight. Capture itself commits on native
    /// launch/departure; this action simply preserves the exact Queen as a world pawn after transit
    /// instead of landing the hostile recovery shuttle back onto a player map.
    /// </summary>
    public sealed class TransportersArrivalAction_ReplicatorQueenRecovery : TransportersArrivalAction
    {
        private Faction captorFaction;

        public TransportersArrivalAction_ReplicatorQueenRecovery() { }

        public TransportersArrivalAction_ReplicatorQueenRecovery(Faction captor)
        {
            captorFaction = captor;
        }

        public override bool GeneratesMap => false;

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            GameComponent_ReplicatorQueenState state = ReplicatorQueenUtility.State;
            Pawn queen = state?.ExactQueen;
            Faction hostile = captorFaction ?? ReplicatorQueenRecoveryUtility.HostileFaction;

            if (queen != null && !queen.Dead)
            {
                foreach (ActiveTransporterInfo info in transporters ?? new List<ActiveTransporterInfo>())
                {
                    if (info?.innerContainer != null && info.innerContainer.Contains(queen))
                    {
                        info.innerContainer.Remove(queen);
                        break;
                    }
                }
                try
                {
                    if (hostile != null && queen.Faction != hostile)
                        queen.SetFaction(hostile);
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Queen recovery arrived but hostile faction reconciliation remains pending: " + ex.Message);
                }
                if (!queen.Spawned && !Find.WorldPawns.Contains(queen))
                    Find.WorldPawns.PassToWorld(queen, PawnDiscardDecideMode.KeepForever);
                state?.MarkCaptureDeparture(hostile);
            }

            foreach (ActiveTransporterInfo info in transporters ?? new List<ActiveTransporterInfo>())
            {
                ThingOwner contents = info?.innerContainer;
                if (contents == null)
                    continue;
                foreach (Pawn pawn in contents.OfType<Pawn>().ToList())
                {
                    contents.Remove(pawn);
                    if (!Find.WorldPawns.Contains(pawn))
                        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
                }
                contents.ClearAndDestroyContents();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref captorFaction, "wngQueenRecoveryCaptorFaction");
        }
    }

    /// <summary>
    /// Cleanup for an aborted recovery whose exact Queen left the mission map before loading.
    /// This action never touches Queen state; it only preserves surviving operatives as world pawns.
    /// </summary>
    public sealed class TransportersArrivalAction_ReplicatorQueenRecoveryRetreat : TransportersArrivalAction
    {
        public override bool GeneratesMap => false;

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            foreach (ActiveTransporterInfo info in transporters ?? new List<ActiveTransporterInfo>())
            {
                ThingOwner contents = info?.innerContainer;
                if (contents == null)
                    continue;
                foreach (Pawn pawn in contents.OfType<Pawn>().ToList())
                {
                    contents.Remove(pawn);
                    if (!Find.WorldPawns.Contains(pawn))
                        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
                }
                contents.ClearAndDestroyContents();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}
