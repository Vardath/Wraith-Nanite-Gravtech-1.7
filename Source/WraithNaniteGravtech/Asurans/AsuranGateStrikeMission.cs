using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Save-persistent mission state for one exact hostile Asuran Puddle-Jumper boarding party.
    /// The current gate-ingress comp owns physical deployment; this state owns only the recovered
    /// bounded strike lifecycle after those exact pawns are on the map.
    /// </summary>
    public sealed class AsuranGateStrikeTeamState : IExposable
    {
        public Thing sourceJumper;
        public Thing sourceGate;
        public int missionStartTick = -1;
        public int nextMissionOrderTick = -1;
        public bool moduleEscaped;
        public List<Pawn> members = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_References.Look(ref sourceJumper, "sourceJumper");
            Scribe_References.Look(ref sourceGate, "sourceGate");
            Scribe_Values.Look(ref missionStartTick, "missionStartTick", -1);
            Scribe_Values.Look(ref nextMissionOrderTick, "nextMissionOrderTick", -1);
            Scribe_Values.Look(ref moduleEscaped, "moduleEscaped", false);
            Scribe_Collections.Look(ref members, "members", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                members = (members ?? new List<Pawn>())
                    .Where(p => p != null && !p.Dead && !p.Destroyed)
                    .Distinct()
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Restores the distinct historical gate-strike lifecycle without restoring the obsolete
    /// CompAsuranStrikeTeam wrapper. Exact crew are learned from CompHostileGateJumperIngress.
    /// For a bounded mission window they pressure high-value colony technology while the existing
    /// hostile-technology-theft component retains exclusive ownership of pattern/module transactions.
    /// After the recovered extraction deadline, or after a physical vacuum-module carrier commits,
    /// surviving exact gate-arrived crew withdraw only through their exact usable source Stargate.
    /// A blocked/destroyed gate never turns into a free map-edge escape.
    /// </summary>
    public sealed class MapComponent_AsuranGateStrikeMission : MapComponent
    {
        private const int TickInterval = 250;
        private const int MissionOrderIntervalTicks = 500;
        private const int ExtractionStartTicks = 3600;
        private const float ExtractionRadiusSquared = 6.25f;
        private const float MissionTargetRangeSquared = 3025f;

        private List<AsuranGateStrikeTeamState> teams =
            new List<AsuranGateStrikeTeamState>();

        public MapComponent_AsuranGateStrikeMission(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null || !map.IsPlayerHome)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now % TickInterval != 0)
                return;

            DiscoverExactGateTeams(now);

            teams ??= new List<AsuranGateStrikeTeamState>();
            for (int i = teams.Count - 1; i >= 0; i--)
            {
                AsuranGateStrikeTeamState state = teams[i];
                if (state == null || !TickTeam(state, now))
                    teams.RemoveAt(i);
            }
        }

        private void DiscoverExactGateTeams(int now)
        {
            ThingDef jumperDef =
                DefDatabase<ThingDef>.GetNamedSilentFail("WNG_PuddleJumper_NPC");
            if (jumperDef == null)
                return;

            teams ??= new List<AsuranGateStrikeTeamState>();

            foreach (Thing jumper in map.listerThings.ThingsOfDef(jumperDef))
            {
                if (jumper == null ||
                    jumper.Destroyed ||
                    !jumper.Spawned ||
                    jumper.Map != map ||
                    jumper.Faction == null ||
                    Faction.OfPlayer == null ||
                    !jumper.Faction.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }

                CompHostileGateJumperIngress ingress =
                    jumper.TryGetComp<CompHostileGateJumperIngress>();
                if (ingress == null || ingress.SourceGate == null)
                    continue;

                List<Pawn> deployed = ingress.DeployedCrew
                    .Where(p => p != null && !p.Dead && !p.Destroyed)
                    .Distinct()
                    .ToList();
                if (deployed.Count == 0)
                    continue;

                AsuranGateStrikeTeamState state =
                    teams.FirstOrDefault(x => x?.sourceJumper == jumper);
                if (state == null)
                {
                    state = new AsuranGateStrikeTeamState
                    {
                        sourceJumper = jumper,
                        sourceGate = ingress.SourceGate,
                        missionStartTick = now,
                        nextMissionOrderTick = now
                    };
                    teams.Add(state);
                }
                else if (ingress.SourceGate != null)
                {
                    state.sourceGate = ingress.SourceGate;
                }

                state.members ??= new List<Pawn>();
                foreach (Pawn pawn in deployed)
                {
                    if (!state.members.Contains(pawn))
                        state.members.Add(pawn);
                }
            }
        }

        private bool TickTeam(AsuranGateStrikeTeamState state, int now)
        {
            state.members ??= new List<Pawn>();

            // A module carrier that has already crossed into WorldPawns committed the old
            // early-withdrawal condition. Ordinary non-module world pawns are already gone
            // and no longer need to remain in this local mission state.
            for (int i = state.members.Count - 1; i >= 0; i--)
            {
                Pawn pawn = state.members[i];
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    state.members.RemoveAt(i);
                    continue;
                }

                if (!pawn.Spawned && Find.WorldPawns.Contains(pawn))
                {
                    if (HasVacuumModule(pawn))
                        state.moduleEscaped = true;
                    state.members.RemoveAt(i);
                }
                else if (pawn.Spawned && pawn.Map != map)
                {
                    state.members.RemoveAt(i);
                }
            }

            List<Pawn> spawned = state.members
                .Where(p =>
                    p != null &&
                    !p.Dead &&
                    !p.Destroyed &&
                    p.Spawned &&
                    p.Map == map &&
                    p.Faction != null &&
                    Faction.OfPlayer != null &&
                    p.Faction.HostileTo(Faction.OfPlayer))
                .ToList();

            if (spawned.Count == 0)
                return state.members.Count > 0;

            Pawn moduleCarrier = spawned.FirstOrDefault(HasVacuumModule);
            bool earlyExtraction = moduleCarrier != null || state.moduleEscaped;
            bool timedExtraction =
                state.missionStartTick >= 0 &&
                now >= state.missionStartTick + ExtractionStartTicks;

            if (earlyExtraction || timedExtraction)
            {
                DirectExactGateExtraction(state, spawned);
                return true;
            }

            // Historical module recovery suspended ordinary sabotage orders for the whole strike.
            if (spawned.Any(p =>
                    p?.CurJobDef?.defName ==
                    AsuranHostileTechnologyTheftUtility.ModuleJobDefName))
            {
                return true;
            }

            DirectHighValueTechnologyAttack(state, spawned, now);
            return true;
        }

        private void DirectHighValueTechnologyAttack(
            AsuranGateStrikeTeamState state,
            List<Pawn> spawned,
            int now)
        {
            if (now < state.nextMissionOrderTick)
                return;

            state.nextMissionOrderTick =
                SafeFutureTick(now, MissionOrderIntervalTicks);

            IntVec3 center =
                state.sourceJumper != null &&
                !state.sourceJumper.Destroyed &&
                state.sourceJumper.Spawned &&
                state.sourceJumper.Map == map
                    ? state.sourceJumper.Position
                    : state.sourceGate != null &&
                      !state.sourceGate.Destroyed &&
                      state.sourceGate.Spawned &&
                      state.sourceGate.Map == map
                        ? state.sourceGate.Position
                        : map.Center;

            Building target = map.listerThings.AllThings
                .OfType<Building>()
                .Where(b =>
                    b != null &&
                    !b.Destroyed &&
                    b.Spawned &&
                    b.Faction == Faction.OfPlayer &&
                    b.Position.DistanceToSquared(center) <=
                        MissionTargetRangeSquared)
                .OrderByDescending(MissionScore)
                .ThenBy(b => b.Position.DistanceToSquared(center))
                .FirstOrDefault();

            if (target == null)
                return;

            foreach (Pawn pawn in spawned)
            {
                if (!CanReceiveMissionOrder(pawn))
                    continue;
                if (pawn.CurJobDef == JobDefOf.AttackStatic &&
                    pawn.CurJob?.targetA.Thing == target)
                {
                    continue;
                }

                Job job = new Job(JobDefOf.AttackStatic, target)
                {
                    expiryInterval = 1400,
                    maxNumStaticAttacks = 3,
                    endIfCantShootTargetFromCurPos = false
                };
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }

        private void DirectExactGateExtraction(
            AsuranGateStrikeTeamState state,
            List<Pawn> spawned)
        {
            Thing gate = state.sourceGate;
            Faction faction = spawned
                .Select(p => p?.Faction)
                .FirstOrDefault(f => f != null);

            if (gate == null ||
                gate.Destroyed ||
                !gate.Spawned ||
                gate.Map != map ||
                faction == null ||
                !WraithStargateHuntUtility.IsExactGateUsable(
                    map,
                    gate,
                    faction))
            {
                return;
            }

            IntVec3 gateCell = gate.Position;

            foreach (Pawn pawn in spawned.ToList())
            {
                if (pawn == null ||
                    pawn.Dead ||
                    pawn.Downed ||
                    pawn.Destroyed ||
                    !pawn.Spawned ||
                    pawn.Map != map)
                {
                    continue;
                }

                // The current theft component owns the exact physical module carrier transaction.
                if (HasVacuumModule(pawn))
                    continue;

                // Preserve the old rule that an active pattern scan is allowed to finish rather
                // than being silently overwritten by the team mission controller.
                if (pawn.CurJobDef?.defName ==
                    AsuranHostileTechnologyTheftUtility.PatternJobDefName)
                {
                    continue;
                }

                if (pawn.Position.DistanceToSquared(gateCell) <=
                    ExtractionRadiusSquared)
                {
                    if (TryCommitExactGateRetreat(pawn, gateCell))
                        state.members.Remove(pawn);
                    continue;
                }

                GiveExtractionGoto(pawn, gateCell);
            }
        }

        private bool TryCommitExactGateRetreat(
            Pawn pawn,
            IntVec3 gateCell)
        {
            if (pawn == null ||
                pawn.Dead ||
                pawn.Destroyed ||
                !pawn.Spawned ||
                pawn.Map != map)
            {
                return false;
            }

            pawn.jobs?.StopAll();

            try
            {
                pawn.DeSpawn(DestroyMode.Vanish);
                if (!Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.PassToWorld(
                        pawn,
                        PawnDiscardDecideMode.Decide);
                }
                return Find.WorldPawns.Contains(pawn);
            }
            catch (Exception ex)
            {
                if (!Find.WorldPawns.Contains(pawn) &&
                    !pawn.Spawned &&
                    !pawn.Destroyed)
                {
                    try
                    {
                        GenSpawn.Spawn(
                            pawn,
                            gateCell.IsValid && gateCell.InBounds(map)
                                ? gateCell
                                : map.Center,
                            map);
                    }
                    catch (Exception rollback)
                    {
                        Log.Error(
                            "[WNG] Asuran gate-strike retreat rollback failed for exact pawn " +
                            pawn + ": " + rollback);
                    }
                }

                Log.Warning(
                    "[WNG] Asuran gate-strike retreat remained uncommitted: " +
                    ex.Message);
                return false;
            }
        }

        private static void GiveExtractionGoto(
            Pawn pawn,
            IntVec3 gateCell)
        {
            if (pawn?.jobs == null || !gateCell.IsValid)
                return;

            if (pawn.CurJobDef == JobDefOf.Goto &&
                pawn.CurJob?.targetA.Cell == gateCell)
            {
                return;
            }

            Job job = new Job(JobDefOf.Goto, gateCell)
            {
                expiryInterval = 1200,
                locomotionUrgency = LocomotionUrgency.Sprint
            };
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static bool CanReceiveMissionOrder(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   !pawn.Downed &&
                   pawn.Spawned &&
                   pawn.jobs != null &&
                   !AsuranHostileTechnologyTheftUtility
                       .IsTechnologyTheftJob(pawn);
        }

        private static bool HasVacuumModule(Pawn pawn)
        {
            return pawn?.carryTracker?.CarriedThing?.def?.defName ==
                   AsuranHostileTechnologyTheftUtility.VacuumModuleDefName;
        }

        private static float MissionScore(Building building)
        {
            if (building?.def == null)
                return float.MinValue;

            string name =
                (building.def.defName ?? string.Empty).ToLowerInvariant();
            float score = Math.Max(0f, building.MarketValue) * 0.05f;

            if (building.TryGetComp<CompPowerTrader>() != null)
                score += 25f;
            if (building is Building_WorkTable)
                score += 25f;

            foreach (string token in new[]
            {
                "stargate", "archive", "research", "fabricator", "grav",
                "shield", "reactor", "battery", "turret", "comms", "console"
            })
            {
                if (name.Contains(token))
                    score += 24f;
            }

            return score;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref teams,
                "wngAsuranGateStrikeMissionTeams",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                teams = (teams ?? new List<AsuranGateStrikeTeamState>())
                    .Where(state => state != null)
                    .ToList();
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result =
                (long)Math.Max(0, now) + Math.Max(1, delay);
            return result >= int.MaxValue
                ? int.MaxValue
                : (int)result;
        }
    }
}
