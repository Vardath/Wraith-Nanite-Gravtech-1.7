using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    internal static class ReplicatorStargateAssaultUtility
    {
        public const string SwarmFactionDefName = "WNG_ReplicatorSwarm";
        public const string DroneKindDefName = "WNG_ReplicatorDrone";
        public const int MaximumInitialWave = 12;
        public const int MinimumInitialWave = 3;
        public const int FollowupWaves = 2;
        public const int FollowupDelayTicks = 9000;
        public const int FollowupRetryTicks = 1500;
        public const int FollowupRetryWindowTicks = 30000;
        public const float FollowupPointsFactor = 0.35f;
        public const float DroneCombatPower = 35f;

        public static Faction SwarmFaction
        {
            get
            {
                if (Find.FactionManager == null)
                    return null;
                return Find.FactionManager.AllFactions
                    .FirstOrDefault(f =>
                        f != null &&
                        !f.defeated &&
                        f.def?.defName == SwarmFactionDefName);
            }
        }

        public static PawnKindDef DroneKind =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(DroneKindDefName);

        public static bool TryResolveExactGate(
            Map map,
            Faction faction,
            out Thing gate,
            out IntVec3 gateCell)
        {
            gate = null;
            gateCell = IntVec3.Invalid;

            PawnsArrivalModeDef mode =
                QuietLatticeStargateVisitUtility.StargateArrivalMode;
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
                if (!mode.Worker.TryResolveRaidSpawnCenter(probe) ||
                    probe.raidArrivalMode != mode ||
                    !QuietLatticeStargateVisitUtility.IsResolvedHomeMapStargate(
                        map,
                        probe.spawnCenter))
                    return false;
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator Stargate-assault probe failed safely: " +
                    ex.Message);
                return false;
            }

            gateCell = probe.spawnCenter;
            gate = map.thingGrid.ThingsListAtFast(gateCell)
                .FirstOrDefault(t =>
                    t?.Map == map &&
                    string.Equals(
                        t.def?.thingClass?.FullName,
                        QuietLatticeStargateVisitUtility.StargateThingClassName,
                        StringComparison.Ordinal));
            return gate != null;
        }

        public static bool IsExactGateStillUsable(
            Map map,
            Thing exactGate,
            Faction faction)
        {
            return exactGate != null &&
                   !exactGate.Destroyed &&
                   exactGate.Spawned &&
                   exactGate.Map == map &&
                   TryResolveExactGate(
                       map,
                       faction,
                       out Thing resolved,
                       out _) &&
                   resolved == exactGate;
        }

        public static int CountDomainBlocks(
            Map map,
            string domainId)
        {
            if (map == null || domainId.NullOrEmpty())
                return 0;

            return map.mapPawns.AllPawnsSpawned.Count(p =>
                p != null &&
                !p.Dead &&
                ReplicatorAssimilationUtility.IsBlockReplicator(p) &&
                string.Equals(
                    ReplicatorDomainUtility.DomainId(p),
                    domainId,
                    StringComparison.Ordinal));
        }

        public static int RemainingHostileCapacity(
            Map map,
            PawnKindDef droneKind,
            string domainId,
            int corridorCommittedCount)
        {
            if (map == null || droneKind == null)
                return 0;

            ReplicatorBlockExtension ext =
                droneKind.race?.GetModExtension<ReplicatorBlockExtension>();
            int cap =
                ReplicatorAssimilationUtility.HostilePopulationCap(ext);
            int live =
                ReplicatorAssimilationUtility.CountHostileBlocks(map);
            int emergedFromCorridor =
                CountDomainBlocks(map, domainId);
            int stillReserved =
                Math.Max(
                    0,
                    Math.Max(0, corridorCommittedCount) -
                    emergedFromCorridor);

            return Math.Max(
                0,
                cap - live - stillReserved);
        }

        public static int DesiredWaveCount(
            float storytellerPoints,
            int remainingCapacity,
            bool initialWave)
        {
            if (remainingCapacity <= 0)
                return 0;

            int calculated =
                Math.Max(
                    1,
                    (int)Math.Round(
                        Math.Max(DroneCombatPower, storytellerPoints) /
                        DroneCombatPower));

            int max =
                initialWave
                    ? MaximumInitialWave
                    : Math.Max(
                        MinimumInitialWave,
                        (int)Math.Ceiling(
                            MaximumInitialWave * FollowupPointsFactor));

            if (initialWave)
                calculated = Math.Max(MinimumInitialWave, calculated);

            return Math.Max(
                0,
                Math.Min(
                    remainingCapacity,
                    Math.Min(max, calculated)));
        }

        public static bool TryCommitWave(
            Map map,
            Thing exactGate,
            Faction faction,
            float storytellerPoints,
            string domainId,
            int corridorCommittedCount,
            bool initialWave,
            out int committedCount)
        {
            committedCount = 0;

            PawnsArrivalModeDef mode =
                QuietLatticeStargateVisitUtility.StargateArrivalMode;
            PawnKindDef droneKind = DroneKind;
            if (map == null ||
                exactGate == null ||
                faction == null ||
                mode?.Worker == null ||
                droneKind == null ||
                domainId.NullOrEmpty() ||
                !IsExactGateStillUsable(map, exactGate, faction))
                return false;

            int remaining =
                RemainingHostileCapacity(
                    map,
                    droneKind,
                    domainId,
                    corridorCommittedCount);
            int count =
                DesiredWaveCount(
                    storytellerPoints,
                    remaining,
                    initialWave);
            if (count <= 0)
                return false;

            IncidentParms arrivalParms = new IncidentParms
            {
                target = map,
                faction = faction,
                raidArrivalMode = mode,
                points = storytellerPoints
            };

            try
            {
                if (!mode.Worker.TryResolveRaidSpawnCenter(arrivalParms) ||
                    arrivalParms.raidArrivalMode != mode ||
                    !QuietLatticeStargateVisitUtility.IsResolvedHomeMapStargate(
                        map,
                        arrivalParms.spawnCenter))
                    return false;

                Thing resolved = map.thingGrid
                    .ThingsListAtFast(arrivalParms.spawnCenter)
                    .FirstOrDefault(t =>
                        t?.Map == map &&
                        string.Equals(
                            t.def?.thingClass?.FullName,
                            QuietLatticeStargateVisitUtility.StargateThingClassName,
                            StringComparison.Ordinal));
                if (resolved != exactGate)
                    return false;
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator Stargate wave could not revalidate the exact CatCraft gate: " +
                    ex.Message);
                return false;
            }

            List<Pawn> pawns =
                new List<Pawn>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Pawn pawn =
                        PawnGenerator.GeneratePawn(
                            droneKind,
                            faction);
                    if (pawn == null)
                        throw new InvalidOperationException(
                            "Drone generation returned null.");

                    CompReplicatorDomain domain =
                        pawn.TryGetComp<CompReplicatorDomain>();
                    if (domain == null)
                    {
                        pawn.Destroy(DestroyMode.Vanish);
                        throw new InvalidOperationException(
                            "Generated Replicator Drone lacks its domain component.");
                    }

                    domain.AssignAutonomousDomain(domainId);
                    pawns.Add(pawn);
                }
            }
            catch (Exception ex)
            {
                DestroyUncommitted(pawns);
                Log.Warning(
                    "[WNG] Replicator Stargate wave aborted before CatCraft ownership: " +
                    ex.Message);
                return false;
            }

            try
            {
                mode.Worker.Arrive(pawns, arrivalParms);
                committedCount = pawns.Count;
                return committedCount > 0;
            }
            catch (Exception ex)
            {
                committedCount = pawns.Count(p =>
                    p != null &&
                    !p.Destroyed &&
                    (p.Spawned || p.ParentHolder != null));

                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null ||
                        pawn.Destroyed ||
                        pawn.Spawned ||
                        pawn.ParentHolder != null)
                        continue;
                    pawn.Destroy(DestroyMode.Vanish);
                }

                if (committedCount > 0)
                {
                    Log.Warning(
                        "[WNG] Replicator Stargate wave partially committed into CatCraft ownership; preserving exact committed Drones and suppressing retry duplication: " +
                        ex.Message);
                    return true;
                }

                Log.Warning(
                    "[WNG] Replicator Stargate wave failed before any exact Drone entered CatCraft ownership: " +
                    ex.Message);
                return false;
            }
        }

        private static void DestroyUncommitted(
            IEnumerable<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns ?? Enumerable.Empty<Pawn>())
            {
                if (pawn != null &&
                    !pawn.Destroyed &&
                    !pawn.Spawned &&
                    pawn.ParentHolder == null)
                    pawn.Destroy(DestroyMode.Vanish);
            }
        }
    }

    /// <summary>
    /// Save-safe same-gate redial chain for one primitive autonomous Replicator swarm domain.
    /// The exact gate is never substituted. Disabling/destroying it prevents follow-up waves; WNG
    /// retries only within the bounded historical window and then abandons the corridor.
    /// </summary>
    public sealed class MapComponent_ReplicatorStargateAssault :
        MapComponent
    {
        private Thing sourceGate;
        private IntVec3 sourceGateCell = IntVec3.Invalid;
        private string domainId;
        private int wavesRemaining;
        private int nextWaveTick;
        private int waveExpiryTick;
        private int nextCheckTick;
        private float basePoints;
        private int corridorCommittedCount;

        public MapComponent_ReplicatorStargateAssault(
            Map map) : base(map) { }

        public bool Active =>
            wavesRemaining > 0 &&
            !domainId.NullOrEmpty();

        public Thing SourceGate => sourceGate;
        public int WavesRemaining => Math.Max(0, wavesRemaining);
        public string DomainId => domainId;

        private static int SafeFutureTick(
            int now,
            int delay)
        {
            long result =
                (long)Math.Max(0, now) +
                Math.Max(1, delay);
            return result >= int.MaxValue
                ? int.MaxValue
                : (int)result;
        }

        public bool Begin(
            Thing exactGate,
            string exactDomainId,
            float initialPoints,
            int initialCommittedCount)
        {
            if (Active ||
                exactGate == null ||
                exactGate.Destroyed ||
                !exactGate.Spawned ||
                exactGate.Map != map ||
                exactDomainId.NullOrEmpty() ||
                initialCommittedCount <= 0)
                return false;

            int now =
                Find.TickManager?.TicksGame ?? 0;
            sourceGate = exactGate;
            sourceGateCell = exactGate.Position;
            domainId = exactDomainId;
            wavesRemaining =
                ReplicatorStargateAssaultUtility.FollowupWaves;
            basePoints =
                Math.Max(
                    ReplicatorStargateAssaultUtility.DroneCombatPower,
                    initialPoints);
            corridorCommittedCount =
                Math.Max(1, initialCommittedCount);
            nextWaveTick =
                SafeFutureTick(
                    now,
                    ReplicatorStargateAssaultUtility.FollowupDelayTicks);
            waveExpiryTick =
                SafeFutureTick(
                    nextWaveTick,
                    ReplicatorStargateAssaultUtility.FollowupRetryWindowTicks);
            nextCheckTick =
                SafeFutureTick(now, 250);
            return true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!Active ||
                map == null ||
                Find.TickManager == null)
                return;

            int now =
                Find.TickManager.TicksGame;
            if (now < nextCheckTick)
                return;

            nextCheckTick =
                SafeFutureTick(now, 250);

            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                now > waveExpiryTick ||
                sourceGate == null ||
                sourceGate.Destroyed ||
                !sourceGate.Spawned ||
                sourceGate.Map != map)
            {
                Clear();
                return;
            }

            if (now < nextWaveTick)
                return;

            Faction swarm =
                ReplicatorStargateAssaultUtility.SwarmFaction;
            PawnKindDef droneKind =
                ReplicatorStargateAssaultUtility.DroneKind;
            if (swarm == null ||
                swarm.defeated ||
                droneKind == null)
            {
                Clear();
                return;
            }

            int remaining =
                ReplicatorStargateAssaultUtility.RemainingHostileCapacity(
                    map,
                    droneKind,
                    domainId,
                    corridorCommittedCount);
            if (remaining <= 0)
            {
                Clear();
                return;
            }

            if (!ReplicatorStargateAssaultUtility.IsExactGateStillUsable(
                    map,
                    sourceGate,
                    swarm))
            {
                nextWaveTick =
                    SafeFutureTick(
                        now,
                        ReplicatorStargateAssaultUtility.FollowupRetryTicks);
                return;
            }

            float points =
                Math.Max(
                    ReplicatorStargateAssaultUtility.DroneCombatPower,
                    basePoints *
                    ReplicatorStargateAssaultUtility.FollowupPointsFactor);

            if (!ReplicatorStargateAssaultUtility.TryCommitWave(
                    map,
                    sourceGate,
                    swarm,
                    points,
                    domainId,
                    corridorCommittedCount,
                    initialWave: false,
                    out int committed))
            {
                nextWaveTick =
                    SafeFutureTick(
                        now,
                        ReplicatorStargateAssaultUtility.FollowupRetryTicks);
                return;
            }

            corridorCommittedCount +=
                Math.Max(0, committed);
            wavesRemaining--;

            try
            {
                Messages.Message(
                    "The exact Stargate corridor has redialed and delivered another primitive Replicator wave from the same autonomous swarm domain.",
                    new TargetInfo(sourceGateCell, map),
                    MessageTypeDefOf.ThreatSmall,
                    historical: false);
            }
            catch { }

            if (wavesRemaining <= 0)
            {
                Clear();
                return;
            }

            nextWaveTick =
                SafeFutureTick(
                    now,
                    ReplicatorStargateAssaultUtility.FollowupDelayTicks);
            waveExpiryTick =
                SafeFutureTick(
                    nextWaveTick,
                    ReplicatorStargateAssaultUtility.FollowupRetryWindowTicks);
        }

        private void Clear()
        {
            sourceGate = null;
            sourceGateCell = IntVec3.Invalid;
            domainId = null;
            wavesRemaining = 0;
            nextWaveTick = 0;
            waveExpiryTick = 0;
            nextCheckTick = 0;
            basePoints = 0f;
            corridorCommittedCount = 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(
                ref sourceGate,
                "wngReplicatorGateAssaultSourceGate");
            Scribe_Values.Look(
                ref sourceGateCell,
                "wngReplicatorGateAssaultSourceGateCell");
            Scribe_Values.Look(
                ref domainId,
                "wngReplicatorGateAssaultDomain");
            Scribe_Values.Look(
                ref wavesRemaining,
                "wngReplicatorGateAssaultWaves",
                0);
            Scribe_Values.Look(
                ref nextWaveTick,
                "wngReplicatorGateAssaultNextWave",
                0);
            Scribe_Values.Look(
                ref waveExpiryTick,
                "wngReplicatorGateAssaultExpiry",
                0);
            Scribe_Values.Look(
                ref nextCheckTick,
                "wngReplicatorGateAssaultNextCheck",
                0);
            Scribe_Values.Look(
                ref basePoints,
                "wngReplicatorGateAssaultBasePoints",
                0f);
            Scribe_Values.Look(
                ref corridorCommittedCount,
                "wngReplicatorGateAssaultCommittedCount",
                0);

            if (Scribe.mode ==
                LoadSaveMode.PostLoadInit)
            {
                wavesRemaining =
                    Math.Max(
                        0,
                        Math.Min(
                            ReplicatorStargateAssaultUtility.FollowupWaves,
                            wavesRemaining));
                corridorCommittedCount =
                    Math.Max(0, corridorCommittedCount);

                if (wavesRemaining <= 0 ||
                    domainId.NullOrEmpty() ||
                    sourceGate == null ||
                    sourceGate.Destroyed ||
                    !sourceGate.Spawned ||
                    sourceGate.Map != map)
                    Clear();
            }
        }
    }

    public sealed class IncidentWorker_ReplicatorStargateAssault :
        IncidentWorker
    {
        protected override bool CanFireNowSub(
            IncidentParms parms)
        {
            Map map =
                parms?.target as Map;
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                map == null ||
                !map.IsPlayerHome ||
                map.GetComponent<MapComponent_ReplicatorStargateAssault>()?.Active == true)
                return false;

            Faction swarm =
                ReplicatorStargateAssaultUtility.SwarmFaction;
            PawnKindDef drone =
                ReplicatorStargateAssaultUtility.DroneKind;
            if (swarm == null ||
                drone == null ||
                swarm.defeated ||
                Faction.OfPlayer == null ||
                !swarm.HostileTo(Faction.OfPlayer))
                return false;

            int remaining =
                ReplicatorStargateAssaultUtility.RemainingHostileCapacity(
                    map,
                    drone,
                    null,
                    0);
            if (remaining <
                ReplicatorStargateAssaultUtility.MinimumInitialWave)
                return false;

            return ReplicatorStargateAssaultUtility.TryResolveExactGate(
                       map,
                       swarm,
                       out _,
                       out _) &&
                   base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(
            IncidentParms parms)
        {
            Map map =
                parms?.target as Map;
            if (map == null ||
                !map.IsPlayerHome ||
                !WNGSettingsUtility.ReplicatorStoryEventsEnabled)
                return false;

            MapComponent_ReplicatorStargateAssault component =
                map.GetComponent<MapComponent_ReplicatorStargateAssault>();
            Faction swarm =
                ReplicatorStargateAssaultUtility.SwarmFaction;
            if (component == null ||
                component.Active ||
                swarm == null ||
                swarm.defeated)
                return false;

            if (!ReplicatorStargateAssaultUtility.TryResolveExactGate(
                    map,
                    swarm,
                    out Thing gate,
                    out IntVec3 gateCell))
                return false;

            float points =
                parms.points > 0f
                    ? parms.points
                    : Math.Max(
                        250f,
                        StorytellerUtility.DefaultThreatPointsNow(map));

            int now =
                Find.TickManager?.TicksGame ?? 0;
            string domainId =
                "gate:" +
                map.uniqueID + ":" +
                gate.thingIDNumber + ":" +
                now;

            if (!ReplicatorStargateAssaultUtility.TryCommitWave(
                    map,
                    gate,
                    swarm,
                    points,
                    domainId,
                    corridorCommittedCount: 0,
                    initialWave: true,
                    out int committed))
                return false;

            bool scheduled =
                component.Begin(
                    gate,
                    domainId,
                    points,
                    committed);
            if (!scheduled)
            {
                // The exact first wave is already physically owned by CatCraft. Never replace or
                // destroy it merely because follow-up scheduling failed.
                Log.Warning(
                    "[WNG] Replicator Stargate assault first wave committed but follow-up corridor state could not initialize.");
            }

            parms.faction = swarm;

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Replicators through the gate",
                    "The colony's exact Stargate has opened onto a hostile Replicator-controlled corridor. " +
                    committed + " primitive Drones from one autonomous swarm domain are entering through CatCraft's own arrival path. " +
                    "The swarm begins primitive; surviving units can assimilate, adapt and recombine under the current Replicator ecology. " +
                    "Up to two follow-on redials are possible while this same gate remains CatCraft's usable arrival owner. " +
                    "Disable, block or destroy that exact gate to sever the corridor.",
                    LetterDefOf.ThreatBig,
                    new TargetInfo(gateCell, map));
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator Stargate assault committed but letter presentation failed: " +
                    ex.Message);
            }

            return true;
        }
    }
}
