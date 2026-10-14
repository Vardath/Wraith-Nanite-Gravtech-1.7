using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    internal static class WraithStargateHuntUtility
    {
        public static PawnsArrivalModeDef ArrivalMode => QuietLatticeStargateVisitUtility.StargateArrivalMode;

        public static IEnumerable<Faction> HostileLineages()
        {
            if (Faction.OfPlayer == null)
                return Enumerable.Empty<Faction>();
            return WraithLineageUtility.ActiveLineages()
                .Where(f => f != null && !f.defeated && f.HostileTo(Faction.OfPlayer));
        }

        public static Faction ResolveFaction(string defName)
        {
            return defName.NullOrEmpty() ? null : WraithLineageUtility.Resolve(defName);
        }

        public static bool TryResolveExactGate(Map map, Faction faction, out Thing gate, out IntVec3 gateCell)
        {
            gate = null;
            gateCell = IntVec3.Invalid;
            PawnsArrivalModeDef mode = ArrivalMode;
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
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Wraith Stargate hunt probe failed safely: " + ex.Message);
                return false;
            }

            gateCell = probe.spawnCenter;
            if (!gateCell.IsValid || !gateCell.InBounds(map))
                return false;

            gate = map.thingGrid.ThingsListAtFast(gateCell)
                .FirstOrDefault(t =>
                    t?.Map == map &&
                    string.Equals(
                        t.def?.thingClass?.FullName,
                        QuietLatticeStargateVisitUtility.StargateThingClassName,
                        StringComparison.Ordinal));
            return gate != null;
        }

        public static bool IsExactGateUsable(Map map, Thing exactGate, Faction faction)
        {
            if (map == null || exactGate == null || exactGate.Destroyed || !exactGate.Spawned || exactGate.Map != map || faction == null)
                return false;
            return TryResolveExactGate(map, faction, out Thing resolved, out _) && resolved == exactGate;
        }
    }

    /// <summary>
    /// Strategic Wraith doctrine for a CatCraft-delivered raid. The mission records one exact source
    /// gate, tracks only newly-arrived members of the same Wraith lineage, uses the existing physical
    /// Dart culling mission for prey capture, permits one bounded reinforcement redial through the same
    /// gate, then orders survivors back to that same corridor. If the exact gate remains unavailable,
    /// survivors eventually abandon the fast route and flee conventionally to the map edge.
    /// </summary>
    public sealed class MapComponent_WraithGateHunt : MapComponent
    {
        private const int RecruitmentWindowTicks = 15000;
        private const int OrderIntervalTicks = 300;
        private const int PressureCheckDelayTicks = 4200;
        private const int HardHuntLimitTicks = 18000;
        private const int BlockedGateFallbackTicks = 1800;
        private const float GateExitRadius = 2.7f;
        private const float RecruitmentRadius = 42f;
        private const int ReinforcementDelayTicks = 9000;
        private const int ReinforcementRetryTicks = 1500;
        private const int ReinforcementWindowTicks = 6000;

        private bool active;
        private string factionDefName;
        private Thing sourceGate;
        private IntVec3 sourceGateCell = IntVec3.Invalid;
        private int missionStartTick = -1;
        private int recruitmentEndTick = -1;
        private int nextOrderTick = -1;
        private int captureQuota;
        private int peakHunterCount;
        private bool extracting;
        private string extractionReason;
        private int gateBlockedSinceTick = -1;
        private bool edgeFallbackCommitted;
        private int reinforcementDueTick = -1;
        private int reinforcementExpiryTick = -1;
        private bool reinforcementLaunched;
        private bool reinforcementDenied;
        private bool pursuitWindowOpened;
        private float baseRaidPoints;
        private List<int> preExistingPawnIds = new List<int>();
        private List<Pawn> hunters = new List<Pawn>();

        public MapComponent_WraithGateHunt(Map map) : base(map) { }

        public bool Active => active;
        public bool Extracting => active && extracting;
        public int CaptureQuota => Math.Max(0, captureQuota);
        public int Captures => CountCurrentDartCaptives();
        public bool ReinforcementPending => active && !extracting && !reinforcementLaunched && !reinforcementDenied;
        public bool ReinforcementLaunched => reinforcementLaunched;
        public bool ReinforcementDenied => reinforcementDenied;
        public Thing SourceGate => sourceGate;
        public string FactionDefName => factionDefName;

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private static bool Elapsed(int now, int start, int delay)
        {
            return start >= 0 && (long)now - start >= Math.Max(0, delay);
        }

        public bool BeginMission(
            Thing gate,
            Faction faction,
            float raidPoints,
            IEnumerable<int> existingFactionPawnIds,
            bool cullingScheduled)
        {
            if (active || gate == null || gate.Destroyed || !gate.Spawned || gate.Map != map || faction?.def == null)
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            active = true;
            factionDefName = faction.def.defName;
            sourceGate = gate;
            sourceGateCell = gate.Position;
            missionStartTick = now;
            recruitmentEndTick = SafeFutureTick(now, RecruitmentWindowTicks);
            nextOrderTick = SafeFutureTick(now, 120);
            baseRaidPoints = Math.Max(100f, raidPoints);
            captureQuota = cullingScheduled ? (raidPoints >= 1200f ? 3 : raidPoints >= 600f ? 2 : 1) : 0;
            peakHunterCount = 0;
            extracting = false;
            extractionReason = null;
            gateBlockedSinceTick = -1;
            edgeFallbackCommitted = false;
            reinforcementDueTick = SafeFutureTick(now, ReinforcementDelayTicks);
            reinforcementExpiryTick = SafeFutureTick(reinforcementDueTick, ReinforcementWindowTicks);
            reinforcementLaunched = false;
            reinforcementDenied = false;
            pursuitWindowOpened = false;
            preExistingPawnIds = existingFactionPawnIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            hunters = new List<Pawn>();
            return true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!active || map == null || Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            RecruitNewArrivals(now);

            if (!extracting)
            {
                TryLaunchReinforcement(now);
                int combatCapable = hunters.Count(IsCombatCapableHunter);

                if (captureQuota > 0 && Captures >= captureQuota)
                    BeginExtraction("culling quota reached", now);
                else if (Elapsed(now, missionStartTick, PressureCheckDelayTicks) &&
                         peakHunterCount >= 2 &&
                         combatCapable * 2 <= peakHunterCount)
                    BeginExtraction("hunting party badly degraded", now);
                else if (Elapsed(now, missionStartTick, HardHuntLimitTicks))
                    BeginExtraction("hunt window expired", now);
            }

            if (extracting && now >= nextOrderTick)
            {
                nextOrderTick = SafeFutureTick(now, OrderIntervalTicks);
                DirectExtraction(now);
            }

            if (now > recruitmentEndTick && hunters.All(p =>
                    p == null || p.Dead || p.Destroyed || p.Downed || !p.Spawned || p.Map != map))
            {
                EndMission();
            }
        }

        private void RecruitNewArrivals(int now)
        {
            if (now > recruitmentEndTick || factionDefName.NullOrEmpty())
                return;

            Faction faction = WraithStargateHuntUtility.ResolveFaction(factionDefName);
            if (faction == null)
                return;

            IntVec3 center = sourceGate != null && sourceGate.Spawned && sourceGate.Map == map
                ? sourceGate.Position
                : sourceGateCell;
            float radiusSq = RecruitmentRadius * RecruitmentRadius;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned
                         .Where(p => p != null && !p.Dead && p.Faction == faction)
                         .Where(p => !preExistingPawnIds.Contains(p.thingIDNumber))
                         .Where(p => center.IsValid && p.Position.DistanceToSquared(center) <= radiusSq)
                         .OrderBy(p => p.Position.DistanceToSquared(center)))
            {
                if (!hunters.Contains(pawn))
                    hunters.Add(pawn);
            }

            peakHunterCount = Math.Max(
                peakHunterCount,
                hunters.Count(p => p != null && !p.Dead && !p.Destroyed));
        }

        private void TryLaunchReinforcement(int now)
        {
            if (reinforcementLaunched || reinforcementDenied || extracting || now < reinforcementDueTick)
                return;

            if (now > reinforcementExpiryTick)
            {
                reinforcementDenied = true;
                return;
            }

            Faction faction = WraithStargateHuntUtility.ResolveFaction(factionDefName);
            PawnsArrivalModeDef mode = WraithStargateHuntUtility.ArrivalMode;
            if (faction == null || faction.defeated || mode?.Worker == null)
            {
                reinforcementDenied = true;
                return;
            }

            if (!WraithStargateHuntUtility.IsExactGateUsable(map, sourceGate, faction))
            {
                reinforcementDueTick = SafeFutureTick(now, ReinforcementRetryTicks);
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = Math.Max(80f, baseRaidPoints * 0.45f),
                raidArrivalMode = mode,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                forced = true
            };

            bool launched = false;
            try
            {
                launched = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Wraith Stargate reinforcement redial failed safely: " + ex.Message);
            }

            if (launched)
            {
                reinforcementLaunched = true;
                try
                {
                    Messages.Message(
                        "The same Stargate corridor has redialed and delivered a bounded Wraith reinforcement wave.",
                        new TargetInfo(sourceGateCell, map),
                        MessageTypeDefOf.ThreatSmall,
                        historical: false);
                }
                catch { }
            }
            else
            {
                reinforcementDueTick = SafeFutureTick(now, ReinforcementRetryTicks);
            }
        }

        private void BeginExtraction(string reason, int now)
        {
            if (extracting)
                return;

            extracting = true;
            extractionReason = reason;
            nextOrderTick = now;
            if (!reinforcementLaunched)
                reinforcementDenied = true;

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Wraith Stargate hunt withdrawing",
                    "The Wraith hunting party has broken off its assault (" + reason +
                    ") and is trying to withdraw through the same Stargate it used for ingress. " +
                    "Keeping that exact gate unavailable will deny the fast route and force any survivors to flee conventionally.",
                    LetterDefOf.ThreatSmall,
                    new TargetInfo(sourceGateCell, map));
            }
            catch { }
        }

        private void DirectExtraction(int now)
        {
            Faction faction = WraithStargateHuntUtility.ResolveFaction(factionDefName);
            bool gateAvailable = faction != null &&
                                 WraithStargateHuntUtility.IsExactGateUsable(map, sourceGate, faction);

            if (gateAvailable && !edgeFallbackCommitted)
            {
                gateBlockedSinceTick = -1;
                DirectToGate();
                return;
            }

            if (!edgeFallbackCommitted)
            {
                if (gateBlockedSinceTick < 0)
                    gateBlockedSinceTick = now;

                if (!Elapsed(now, gateBlockedSinceTick, BlockedGateFallbackTicks))
                {
                    DirectToGateRally();
                    return;
                }

                edgeFallbackCommitted = true;
                try
                {
                    Messages.Message(
                        "The exact Stargate corridor has remained unavailable long enough to break the Wraith extraction plan. Surviving hunters are falling back to a conventional map-edge retreat.",
                        new TargetInfo(sourceGateCell, map),
                        MessageTypeDefOf.PositiveEvent,
                        historical: false);
                }
                catch { }
            }

            DirectToMapEdge();
        }

        private void DirectToGate()
        {
            if (!sourceGateCell.IsValid)
                return;

            float exitRadiusSq = GateExitRadius * GateExitRadius;
            foreach (Pawn pawn in hunters.ToList())
            {
                if (!IsCombatCapableHunter(pawn))
                    continue;

                if (pawn.Position.DistanceToSquared(sourceGateCell) <= exitRadiusSq)
                {
                    TryOpenPursuitWindow();
                    ExtractPawn(pawn);
                    continue;
                }

                GiveGoto(pawn, sourceGateCell, 900);
            }
        }

        private void DirectToGateRally()
        {
            if (!sourceGateCell.IsValid)
                return;
            foreach (Pawn pawn in hunters)
                if (IsCombatCapableHunter(pawn))
                    GiveGoto(pawn, sourceGateCell, 750);
        }

        private void DirectToMapEdge()
        {
            foreach (Pawn pawn in hunters.ToList())
            {
                if (!IsCombatCapableHunter(pawn))
                    continue;

                if (pawn.Position.OnEdge(map))
                {
                    ExtractPawn(pawn);
                    continue;
                }

                if (CellFinder.TryFindRandomPawnExitCell(pawn, out IntVec3 edgeCell))
                    GiveGoto(pawn, edgeCell, 1200);
            }
        }

        private static void GiveGoto(Pawn pawn, IntVec3 cell, int expiry)
        {
            if (pawn?.jobs == null || !cell.IsValid)
                return;
            if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob?.targetA.Cell == cell)
                return;

            Job job = new Job(JobDefOf.Goto, cell)
            {
                expiryInterval = expiry,
                locomotionUrgency = LocomotionUrgency.Sprint
            };
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void TryOpenPursuitWindow()
        {
            if (pursuitWindowOpened ||
                sourceGate == null || sourceGate.Destroyed || !sourceGate.Spawned || sourceGate.Map != map)
                return;

            MapComponent_WraithGatePursuit pursuit =
                map.GetComponent<MapComponent_WraithGatePursuit>();
            if (pursuit != null && pursuit.Open(sourceGate, factionDefName, CurrentPursuitCaptiveIds()))
                pursuitWindowOpened = true;
        }

        private List<int> CurrentPursuitCaptiveIds()
        {
            List<int> ids = new List<int>();
            Faction faction = WraithStargateHuntUtility.ResolveFaction(factionDefName);
            if (faction == null)
                return ids;

            ThingDef dartDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithDart_NPC");
            if (dartDef != null)
            {
                foreach (Thing dart in map.listerThings.ThingsOfDef(dartDef))
                {
                    if (dart == null || dart.Destroyed || dart.Faction != faction)
                        continue;
                    CompTransporter transporter = dart.TryGetComp<CompTransporter>();
                    if (transporter == null)
                        continue;
                    ids.AddRange(transporter.innerContainer.OfType<Pawn>()
                        .Where(p => p != null && !p.Dead && WraithCullingUtility.IsEligibleBiologicalHuman(p))
                        .Select(p => p.thingIDNumber));
                }
            }

            WraithCullingCustodyRegistry registry =
                Current.Game?.GetComponent<WraithCullingCustodyRegistry>();
            if (registry != null)
            {
                ids.AddRange(registry.Records
                    .Where(record =>
                        record != null &&
                        record.pawn != null &&
                        !record.pawn.Dead &&
                        record.captorFaction == faction &&
                        record.rescueSiteId < 0 &&
                        record.capturedTick >= missionStartTick)
                    .Select(record => record.pawn.thingIDNumber));
            }

            return ids.Where(id => id > 0).Distinct().ToList();
        }

        private void ExtractPawn(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map != map)
                return;
            pawn.jobs?.StopAll();
            pawn.DeSpawn(DestroyMode.Vanish);
            if (!Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
        }

        private int CountCurrentDartCaptives()
        {
            Faction faction = WraithStargateHuntUtility.ResolveFaction(factionDefName);
            ThingDef dartDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_WraithDart_NPC");
            if (faction == null || dartDef == null || map == null)
                return 0;

            int total = 0;
            foreach (Thing dart in map.listerThings.ThingsOfDef(dartDef))
            {
                if (dart == null || dart.Destroyed || dart.Faction != faction)
                    continue;
                CompTransporter transporter = dart.TryGetComp<CompTransporter>();
                if (transporter == null)
                    continue;
                total += transporter.innerContainer.OfType<Pawn>()
                    .Count(p => p != null && !p.Dead && WraithCullingUtility.IsEligibleBiologicalHuman(p));
            }
            return total;
        }

        private bool IsCombatCapableHunter(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed && !pawn.Downed &&
                   pawn.Spawned && pawn.Map == map && pawn.jobs != null;
        }

        private void EndMission()
        {
            active = false;
            factionDefName = null;
            sourceGate = null;
            sourceGateCell = IntVec3.Invalid;
            missionStartTick = -1;
            recruitmentEndTick = -1;
            nextOrderTick = -1;
            captureQuota = 0;
            peakHunterCount = 0;
            extracting = false;
            extractionReason = null;
            gateBlockedSinceTick = -1;
            edgeFallbackCommitted = false;
            reinforcementDueTick = -1;
            reinforcementExpiryTick = -1;
            reinforcementLaunched = false;
            reinforcementDenied = false;
            pursuitWindowOpened = false;
            baseRaidPoints = 0f;
            preExistingPawnIds?.Clear();
            hunters?.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref active, "wngWraithGateHuntActive", false);
            Scribe_Values.Look(ref factionDefName, "wngWraithGateHuntFaction");
            Scribe_References.Look(ref sourceGate, "wngWraithGateHuntSourceGate");
            Scribe_Values.Look(ref sourceGateCell, "wngWraithGateHuntSourceGateCell");
            Scribe_Values.Look(ref missionStartTick, "wngWraithGateHuntStart", -1);
            Scribe_Values.Look(ref recruitmentEndTick, "wngWraithGateHuntRecruitmentEnd", -1);
            Scribe_Values.Look(ref nextOrderTick, "wngWraithGateHuntNextOrder", -1);
            Scribe_Values.Look(ref captureQuota, "wngWraithGateHuntCaptureQuota", 0);
            Scribe_Values.Look(ref peakHunterCount, "wngWraithGateHuntPeakHunters", 0);
            Scribe_Values.Look(ref extracting, "wngWraithGateHuntExtracting", false);
            Scribe_Values.Look(ref extractionReason, "wngWraithGateHuntExtractionReason");
            Scribe_Values.Look(ref gateBlockedSinceTick, "wngWraithGateHuntGateBlockedSince", -1);
            Scribe_Values.Look(ref edgeFallbackCommitted, "wngWraithGateHuntEdgeFallback", false);
            Scribe_Values.Look(ref reinforcementDueTick, "wngWraithGateHuntReinforcementDue", -1);
            Scribe_Values.Look(ref reinforcementExpiryTick, "wngWraithGateHuntReinforcementExpiry", -1);
            Scribe_Values.Look(ref reinforcementLaunched, "wngWraithGateHuntReinforcementLaunched", false);
            Scribe_Values.Look(ref reinforcementDenied, "wngWraithGateHuntReinforcementDenied", false);
            Scribe_Values.Look(ref pursuitWindowOpened, "wngWraithGateHuntPursuitWindowOpened", false);
            Scribe_Values.Look(ref baseRaidPoints, "wngWraithGateHuntBasePoints", 0f);
            Scribe_Collections.Look(ref preExistingPawnIds, "wngWraithGateHuntPreExisting", LookMode.Value);
            Scribe_Collections.Look(ref hunters, "wngWraithGateHuntHunters", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                preExistingPawnIds = preExistingPawnIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
                hunters = hunters?.Where(p => p != null && !p.Dead && !p.Destroyed).Distinct().ToList() ?? new List<Pawn>();
                captureQuota = Math.Max(0, Math.Min(3, captureQuota));
                peakHunterCount = Math.Max(peakHunterCount, hunters.Count);
                baseRaidPoints = Math.Max(0f, baseRaidPoints);
                if (active && !sourceGateCell.IsValid)
                {
                    if (sourceGate != null && !sourceGate.Destroyed && sourceGate.Spawned && sourceGate.Map == map)
                        sourceGateCell = sourceGate.Position;
                    else
                        EndMission();
                }
            }
        }
    }

    public sealed class IncidentWorker_WraithStargateHunt : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms?.target is Map map) || !map.IsPlayerHome)
                return false;
            if (map.GetComponent<MapComponent_WraithGateHunt>()?.Active == true)
                return false;
            if (map.GetComponent<MapComponent_WraithDartCulling>()?.HasActiveMission == true)
                return false;

            bool any = WraithStargateHuntUtility.HostileLineages()
                .Any(f => WraithStargateHuntUtility.TryResolveExactGate(map, f, out _, out _));
            return any && base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms?.target is Map map) || !map.IsPlayerHome)
                return false;

            List<Faction> candidates = WraithStargateHuntUtility.HostileLineages()
                .Where(f => WraithStargateHuntUtility.TryResolveExactGate(map, f, out _, out _))
                .ToList();
            if (candidates.Count == 0)
                return false;

            Faction faction = candidates.RandomElement();
            if (!WraithStargateHuntUtility.TryResolveExactGate(map, faction, out Thing gate, out IntVec3 gateCell))
                return false;

            List<int> preExisting = map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && p.Faction == faction)
                .Select(p => p.thingIDNumber)
                .Where(id => id > 0)
                .ToList();

            PawnsArrivalModeDef mode = WraithStargateHuntUtility.ArrivalMode;
            if (mode?.Worker == null)
                return false;

            parms.faction = faction;
            parms.raidArrivalMode = mode;
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            if (parms.points <= 0f)
                parms.points = Math.Max(250f, StorytellerUtility.DefaultThreatPointsNow(map));

            try
            {
                if (!mode.Worker.TryResolveRaidSpawnCenter(parms) ||
                    parms.raidArrivalMode != mode ||
                    !parms.spawnCenter.IsValid ||
                    !parms.spawnCenter.InBounds(map))
                    return false;

                Thing resolved = map.thingGrid.ThingsListAtFast(parms.spawnCenter)
                    .FirstOrDefault(t =>
                        t?.Map == map &&
                        string.Equals(
                            t.def?.thingClass?.FullName,
                            QuietLatticeStargateVisitUtility.StargateThingClassName,
                            StringComparison.Ordinal));
                if (resolved != gate)
                    return false;
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Wraith Stargate hunt could not commit its exact CatCraft corridor: " + ex.Message);
                return false;
            }

            bool raidLaunched;
            try
            {
                raidLaunched = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Wraith Stargate hunt raid launch failed safely: " + ex.Message);
                return false;
            }
            if (!raidLaunched)
                return false;

            bool cullingScheduled = map.GetComponent<MapComponent_WraithDartCulling>()?.Schedule(faction) == true;
            MapComponent_WraithGateHunt hunt = map.GetComponent<MapComponent_WraithGateHunt>();
            if (hunt == null || !hunt.BeginMission(gate, faction, parms.points, preExisting, cullingScheduled))
            {
                Log.Warning("[WNG] Wraith Stargate raid committed but strategic hunt state could not initialize; preserving the already-launched native raid.");
                return true;
            }

            map.GetComponent<MapComponent_WNGGateControlObjective>()?.Begin(
                gate,
                faction.def.defName,
                WNGGateControlObjectiveKind.WraithHunt);

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Wraith Stargate hunting party",
                    faction.Name + " has entered through the colony's Stargate as a coordinated hunting party. " +
                    (cullingScheduled ? "A real Wraith Dart is conducting the existing bounded culling run in support. " : "") +
                    "The exact gate is now their reinforcement and extraction corridor. Denying that gate can block the follow-on redial and force survivors to retreat conventionally.",
                    LetterDefOf.ThreatBig,
                    new TargetInfo(gateCell, map));
            }
            catch { }
            return true;
        }
    }
}
