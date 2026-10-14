using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Short player counterattack window following an actual same-gate Wraith withdrawal.
    /// The exact drafted pawns go off-map by reference, the operation is resolved abstractly,
    /// then the same pawns return through the same gate if CatCraft still resolves it as the
    /// corridor, or later by a conventional edge route. No CatCraft address/dial state is invented.
    /// </summary>
    public sealed class MapComponent_WraithGatePursuit : MapComponent
    {
        public const string PursuitLabel = "Pursue Wraith through Stargate";
        private const int PursuitWindowTicks = 5000;
        private const int PursuitDurationTicks = 7500;
        private const int ReturnFallbackDelayTicks = 6000;
        private const int LoadRecoveryDelayTicks = 250;
        private const float AssemblyRadius = 8f;
        private const int MinimumTeamSize = 2;
        private const int MaximumTeamSize = 4;

        private bool opportunityActive;
        private bool pursuitInProgress;
        private Thing sourceGate;
        private IntVec3 sourceGateCell = IntVec3.Invalid;
        private string sourceFactionDefName;
        private int opportunityExpiryTick = -1;
        private int returnTick = -1;
        private int fallbackReturnTick = -1;
        private bool pursuitSuccess;
        private List<int> captiveIds = new List<int>();
        private List<Pawn> pursuitTeam = new List<Pawn>();

        public MapComponent_WraithGatePursuit(Map map) : base(map) { }

        public bool Active => opportunityActive || pursuitInProgress;
        public bool PursuitInProgress => pursuitInProgress;
        public Thing SourceGate => sourceGate;

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public bool Open(Thing exactGate, string factionDefName, IEnumerable<int> exactCaptiveIds)
        {
            if (pursuitInProgress || opportunityActive ||
                exactGate == null || exactGate.Destroyed || !exactGate.Spawned ||
                exactGate.Map != map || !map.IsPlayerHome || factionDefName.NullOrEmpty())
                return false;

            Faction faction = WraithStargateHuntUtility.ResolveFaction(factionDefName);
            if (faction == null || !WraithStargateHuntUtility.IsExactGateUsable(map, exactGate, faction))
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            opportunityActive = true;
            sourceGate = exactGate;
            sourceGateCell = exactGate.Position;
            sourceFactionDefName = factionDefName;
            opportunityExpiryTick = SafeFutureTick(now, PursuitWindowTicks);
            captiveIds = exactCaptiveIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();

            try
            {
                string captiveText = captiveIds.Count > 0
                    ? " The trace includes " + captiveIds.Count + " exact culling captive" +
                      (captiveIds.Count == 1 ? "" : "s") +
                      "; if they have reached persistent Wraith custody by the time the strike resolves, a successful pursuit can recover them."
                    : " Even without a tracked captive, a successful pursuit can hit the withdrawing corridor before it disperses.";
                Find.LetterStack.ReceiveLetter(
                    "Wraith Stargate pursuit window",
                    "Wraith survivors have just escaped through the exact Stargate used by their hunting party. " +
                    "For a short time their route is traceable without WNG reading or owning a CatCraft address. " +
                    "Draft two to four conscious humanlike colonists, assemble them within eight cells of this gate, then right-click the gate with one of them to launch a bounded counterattack." +
                    captiveText,
                    LetterDefOf.PositiveEvent,
                    new TargetInfo(sourceGateCell, map));
            }
            catch { }
            return true;
        }

        public bool IsPursuitGate(Thing gate)
        {
            if (!opportunityActive || pursuitInProgress || gate == null || gate != sourceGate)
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            return now <= opportunityExpiryTick && gate.Spawned && !gate.Destroyed && gate.Map == map;
        }

        public string DisabledReason(Pawn leader)
        {
            if (leader == null || leader.Faction != Faction.OfPlayer || leader.RaceProps?.Humanlike != true)
                return "A drafted player-controlled humanlike pawn must lead the pursuit.";
            if (!leader.Drafted || leader.Downed || leader.InMentalState || !leader.Spawned || leader.Map != map)
                return "The pursuit leader must be drafted, conscious and present on this map.";
            if (!IsPursuitGate(sourceGate))
                return "The pursuit window has closed.";

            Faction sourceFaction = WraithStargateHuntUtility.ResolveFaction(sourceFactionDefName);
            if (sourceFaction == null || !WraithStargateHuntUtility.IsExactGateUsable(map, sourceGate, sourceFaction))
                return "The exact Stargate corridor is no longer usable.";
            if (leader.Position.DistanceToSquared(sourceGateCell) > AssemblyRadius * AssemblyRadius)
                return "Move the drafted pursuit leader within eight cells of the exact Stargate.";
            if (EligibleTeam(leader).Count < MinimumTeamSize)
                return "Assemble at least two drafted, conscious humanlike player pawns within eight cells of the gate.";
            return null;
        }

        public bool TryLaunch(Pawn leader)
        {
            string disabled = DisabledReason(leader);
            if (!disabled.NullOrEmpty())
                return false;

            List<Pawn> team = EligibleTeam(leader);
            if (team.Count < MinimumTeamSize)
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            opportunityActive = false;
            pursuitInProgress = true;
            pursuitTeam = team;
            returnTick = SafeFutureTick(now, PursuitDurationTicks);
            fallbackReturnTick = SafeFutureTick(returnTick, ReturnFallbackDelayTicks);

            float chance = 0.55f +
                           0.15f * Math.Max(0, team.Count - MinimumTeamSize) -
                           0.05f * captiveIds.Count;
            chance = Math.Max(0.35f, Math.Min(0.90f, chance));
            pursuitSuccess = Rand.Chance(chance);

            foreach (Pawn pawn in pursuitTeam.ToList())
            {
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map)
                    continue;
                pawn.jobs?.StopAll();
                if (pawn.drafter != null)
                    pawn.drafter.Drafted = false;
                pawn.DeSpawn(DestroyMode.Vanish);
                if (!Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Stargate pursuit launched",
                    pursuitTeam.Count + " colonists have followed the Wraith route off-map. " +
                    "The exact pawn identities are retained. The team will attempt to return through the same gate; if that corridor is lost they will take a slower conventional route home.",
                    LetterDefOf.NeutralEvent,
                    new TargetInfo(sourceGateCell, map));
            }
            catch { }
            return true;
        }

        private List<Pawn> EligibleTeam(Pawn leader)
        {
            if (map == null || sourceGate == null || !sourceGateCell.IsValid)
                return new List<Pawn>();

            float radiusSq = AssemblyRadius * AssemblyRadius;
            return map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && !p.Dead && !p.Downed && !p.InMentalState && p.Spawned && p.Map == map)
                .Where(p => p.Faction == Faction.OfPlayer && p.RaceProps?.Humanlike == true && p.Drafted)
                .Where(p => p.Position.DistanceToSquared(sourceGateCell) <= radiusSq)
                .OrderBy(p => p == leader ? 0 : 1)
                .ThenBy(p => p.Position.DistanceToSquared(sourceGateCell))
                .Take(MaximumTeamSize)
                .ToList();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (opportunityActive && !pursuitInProgress && now > opportunityExpiryTick)
            {
                ClearOpportunity();
                return;
            }
            if (!pursuitInProgress || now < returnTick)
                return;

            Faction sourceFaction = WraithStargateHuntUtility.ResolveFaction(sourceFactionDefName);
            bool gateUsable = sourceFaction != null &&
                              WraithStargateHuntUtility.IsExactGateUsable(map, sourceGate, sourceFaction);
            if (gateUsable)
            {
                CompleteReturn(throughGate: true);
                return;
            }

            if (now >= fallbackReturnTick)
                CompleteReturn(throughGate: false);
        }

        private void CompleteReturn(bool throughGate)
        {
            if (!pursuitInProgress)
                return;

            IntVec3 returnAnchor = throughGate &&
                                   sourceGate != null &&
                                   sourceGate.Spawned &&
                                   sourceGate.Map == map
                ? sourceGate.Position
                : EdgeReturnAnchor();

            List<Pawn> returning = pursuitTeam?
                .Where(p => p != null && !p.Dead)
                .ToList() ?? new List<Pawn>();
            foreach (Pawn pawn in returning)
                ReturnPawn(pawn, returnAnchor, throughGate ? 5 : 6);

            int recovered = 0;
            if (pursuitSuccess && captiveIds != null && captiveIds.Count > 0)
            {
                Faction captor = WraithStargateHuntUtility.ResolveFaction(sourceFactionDefName);
                WraithCullingCustodyRegistry registry =
                    Current.Game?.GetComponent<WraithCullingCustodyRegistry>();
                if (captor != null && registry != null)
                {
                    foreach (int id in captiveIds.Distinct().ToList())
                    {
                        if (registry.TryReleasePursuitCaptive(id, captor, map, returnAnchor, out Pawn released) &&
                            released != null)
                        {
                            recovered++;
                        }
                    }
                }
            }

            bool success = pursuitSuccess;
            string routeText = throughGate
                ? "The pursuit team has returned through the same Stargate corridor."
                : "The exact Stargate corridor was unavailable at return time, so the pursuit team completed a slower conventional withdrawal and re-entered from the map edge.";
            string resultText = success
                ? recovered > 0
                    ? " The counterattack succeeded and recovered " + recovered + " exact abducted colonist" + (recovered == 1 ? "" : "s") + " from Wraith custody."
                    : " The counterattack succeeded in striking the withdrawing route, but no tracked captive was available in persistent custody at the recovery boundary."
                : " The Wraith defence forced the strike team to break off before recovering any captive.";

            Pawn target = returning.FirstOrDefault();
            ClearMissionState();
            try
            {
                Find.LetterStack.ReceiveLetter(
                    success ? "Stargate pursuit resolved" : "Stargate pursuit repelled",
                    routeText + resultText,
                    success ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent,
                    target);
            }
            catch { }
        }

        private void ReturnPawn(Pawn pawn, IntVec3 anchor, int radius)
        {
            if (pawn == null || pawn.Dead || pawn.Spawned || map == null)
                return;
            if (Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(anchor, map, radius);
            GenSpawn.Spawn(pawn, cell, map);
        }

        private IntVec3 EdgeReturnAnchor()
        {
            IntVec3 anchor = new IntVec3(3, 0, Math.Max(3, map.Size.z / 2));
            return anchor.InBounds(map) ? anchor : map.Center;
        }

        private void ClearOpportunity()
        {
            opportunityActive = false;
            if (!pursuitInProgress)
            {
                sourceGate = null;
                sourceGateCell = IntVec3.Invalid;
                sourceFactionDefName = null;
                opportunityExpiryTick = -1;
                captiveIds?.Clear();
            }
        }

        private void ClearMissionState()
        {
            pursuitInProgress = false;
            opportunityActive = false;
            sourceGate = null;
            sourceGateCell = IntVec3.Invalid;
            sourceFactionDefName = null;
            opportunityExpiryTick = -1;
            returnTick = -1;
            fallbackReturnTick = -1;
            pursuitSuccess = false;
            captiveIds?.Clear();
            pursuitTeam?.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref opportunityActive, "wngGatePursuitOpportunityActive", false);
            Scribe_Values.Look(ref pursuitInProgress, "wngGatePursuitInProgress", false);
            Scribe_References.Look(ref sourceGate, "wngGatePursuitExactGate");
            Scribe_Values.Look(ref sourceGateCell, "wngGatePursuitGateCell");
            Scribe_Values.Look(ref sourceFactionDefName, "wngGatePursuitFaction");
            Scribe_Values.Look(ref opportunityExpiryTick, "wngGatePursuitExpiry", -1);
            Scribe_Values.Look(ref returnTick, "wngGatePursuitReturnTick", -1);
            Scribe_Values.Look(ref fallbackReturnTick, "wngGatePursuitFallbackTick", -1);
            Scribe_Values.Look(ref pursuitSuccess, "wngGatePursuitSuccess", false);
            Scribe_Collections.Look(ref captiveIds, "wngGatePursuitCaptiveIds", LookMode.Value);
            Scribe_Collections.Look(ref pursuitTeam, "wngGatePursuitTeam", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                captiveIds = captiveIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
                pursuitTeam = pursuitTeam?
                    .Where(p => p != null && !p.Dead)
                    .Distinct()
                    .Take(MaximumTeamSize)
                    .ToList() ?? new List<Pawn>();

                int now = Find.TickManager?.TicksGame ?? 0;
                if (pursuitInProgress)
                {
                    opportunityActive = false;
                    opportunityExpiryTick = -1;
                    if (pursuitTeam.Count == 0)
                    {
                        ClearMissionState();
                        return;
                    }
                    if (returnTick <= 0)
                        returnTick = SafeFutureTick(now, LoadRecoveryDelayTicks);
                    if (fallbackReturnTick <= 0 || fallbackReturnTick < returnTick)
                        fallbackReturnTick = SafeFutureTick(returnTick, ReturnFallbackDelayTicks);
                }
                else if (opportunityActive)
                {
                    pursuitTeam.Clear();
                    returnTick = -1;
                    fallbackReturnTick = -1;
                    pursuitSuccess = false;
                    if (sourceGate == null || sourceGate.Destroyed || !sourceGate.Spawned ||
                        sourceGate.Map != map || !sourceGateCell.IsValid || opportunityExpiryTick < now)
                    {
                        ClearMissionState();
                    }
                }
                else
                {
                    ClearMissionState();
                }
            }
        }
    }

    /// <summary>
    /// Adds the pursuit order to CatCraft's exact gate without patching the external ThingDef.
    /// </summary>
    public sealed class FloatMenuOptionProvider_WraithGatePursuit : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => false;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Map map = clickedThing?.Map;
            MapComponent_WraithGatePursuit pursuit =
                map?.GetComponent<MapComponent_WraithGatePursuit>();
            if (pursuit == null || !pursuit.IsPursuitGate(clickedThing))
                yield break;

            Pawn leader = context.FirstSelectedPawn;
            string disabled = pursuit.DisabledReason(leader);
            if (!disabled.NullOrEmpty())
            {
                yield return new FloatMenuOption(MapComponent_WraithGatePursuit.PursuitLabel + " (" + disabled + ")", null);
                yield break;
            }

            yield return new FloatMenuOption(
                MapComponent_WraithGatePursuit.PursuitLabel,
                () => pursuit.TryLaunch(leader),
                MenuOptionPriority.High,
                null,
                clickedThing);
        }
    }
}
