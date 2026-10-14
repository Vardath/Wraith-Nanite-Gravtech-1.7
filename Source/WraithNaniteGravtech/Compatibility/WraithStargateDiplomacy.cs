using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    internal static class WraithStargateEnvoyUtility
    {
        public const string VeiledHiveDefName = "WNG_WraithVeiledHive";
        public const string PaleCovenantDefName = "WNG_WraithPaleCovenant";
        public const int MaximumVisitors = 4;
        public const int VisitDurationTicks = 15000;

        private static readonly string[] EligibleFactionDefNames =
        {
            VeiledHiveDefName,
            PaleCovenantDefName
        };

        public static List<Faction> EligibleFactions()
        {
            if (Faction.OfPlayer == null)
                return new List<Faction>();

            return EligibleFactionDefNames
                .Select(WraithLineageUtility.Resolve)
                .Where(f => f != null && !f.defeated && !f.HostileTo(Faction.OfPlayer))
                .OrderBy(f => f.loadID)
                .ToList();
        }

        public static Thing ExactResolvedGate(Map map, IntVec3 spawnCenter)
        {
            if (map == null || !spawnCenter.IsValid || !spawnCenter.InBounds(map))
                return null;

            return map.thingGrid.ThingsListAtFast(spawnCenter)
                .FirstOrDefault(t =>
                    t?.Map == map &&
                    string.Equals(
                        t.def?.thingClass?.FullName,
                        QuietLatticeStargateVisitUtility.StargateThingClassName,
                        StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Tracks exact neutral Wraith envoy Pawns across CatCraft's buffered arrival. WNG creates no
    /// Lord until every surviving exact envoy has physically emerged on the requested home map.
    /// If diplomacy turns hostile during transit, the same emerged Pawns become an ordinary native
    /// assault group rather than being replaced or silently deleted.
    /// </summary>
    public sealed class MapComponent_WraithStargateEnvoy : MapComponent
    {
        private const int UncommittedCleanupGraceTicks = 600;
        private const int MaximumLordAttempts = 5;

        private List<Pawn> visitors = new List<Pawn>();
        private Faction envoyFaction;
        private Thing sourceGate;
        private int visitDurationTicks;
        private int preparedTick = -1;
        private int lordAttempts;

        public MapComponent_WraithStargateEnvoy(Map map) : base(map) { }

        public bool HasActiveEnvoy =>
            visitors != null && visitors.Any(p => p != null && !p.Dead && !p.Destroyed);

        public bool TryPrepare(
            List<Pawn> exactVisitors,
            Faction faction,
            Thing exactGate,
            int durationTicks)
        {
            if (HasActiveEnvoy || exactVisitors.NullOrEmpty() || faction == null ||
                exactGate == null || exactGate.Destroyed || !exactGate.Spawned || exactGate.Map != map)
                return false;

            visitors = exactVisitors.Where(p => p != null && !p.Dead && !p.Destroyed).Distinct().ToList();
            if (visitors.Count == 0)
                return false;

            envoyFaction = faction;
            sourceGate = exactGate;
            visitDurationTicks = Math.Max(6000, durationTicks);
            preparedTick = Find.TickManager?.TicksGame ?? 0;
            lordAttempts = 0;
            return true;
        }

        public void CancelPrepared()
        {
            ClearTracking();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            if (!map.IsHashIntervalTick(60) || visitors == null || visitors.Count == 0)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            visitors.RemoveAll(p => p == null || p.Dead || p.Destroyed);
            if (visitors.Count == 0)
            {
                ClearTracking();
                return;
            }

            // If CatCraft rejected one generated pawn before taking ownership, clean only that
            // uncommitted exact object after a short grace period. Anything with a holder remains
            // CatCraft-owned and is never touched by WNG.
            if (preparedTick >= 0 && (long)now - preparedTick >= UncommittedCleanupGraceTicks)
            {
                foreach (Pawn pawn in visitors
                             .Where(p => p != null && !p.Spawned && p.ParentHolder == null && !Find.WorldPawns.Contains(p))
                             .ToList())
                {
                    visitors.Remove(pawn);
                    if (!pawn.Destroyed)
                        pawn.Destroy(DestroyMode.Vanish);
                }

                if (visitors.Count == 0)
                {
                    ClearTracking();
                    return;
                }
            }

            List<Pawn> emerged = visitors
                .Where(p => p.Spawned && p.Map == map)
                .ToList();
            if (emerged.Count != visitors.Count)
                return;

            Faction faction = envoyFaction ?? emerged.Select(p => p.Faction).FirstOrDefault(f => f != null);
            if (faction == null)
            {
                ClearTracking();
                return;
            }

            List<Pawn> freeEnvoys = emerged
                .Where(p => !p.IsPrisoner && !p.IsSlave && p.Faction == faction)
                .ToList();
            if (freeEnvoys.Count == 0)
            {
                ClearTracking();
                return;
            }

            try
            {
                if (Faction.OfPlayer != null && faction.HostileTo(Faction.OfPlayer))
                {
                    LordMaker.MakeNewLord(
                        faction,
                        new LordJob_AssaultColony(
                            faction,
                            canKidnap: false,
                            canTimeoutOrFlee: true,
                            sappers: false,
                            useAvoidGridSmart: false,
                            canSteal: false),
                        map,
                        freeEnvoys);

                    try
                    {
                        Messages.Message(
                            "Relations changed while the Wraith envoy was in transit. The same emerged delegates have abandoned visitor protocol and become hostile.",
                            freeEnvoys[0],
                            MessageTypeDefOf.ThreatSmall,
                            historical: false);
                    }
                    catch { }
                }
                else
                {
                    IntVec3 chillSpot = CellFinder.RandomClosewalkCellNear(map.Center, map, 18);
                    LordMaker.MakeNewLord(
                        faction,
                        new LordJob_VisitColony(faction, chillSpot, visitDurationTicks),
                        map,
                        freeEnvoys);
                }

                ClearTracking();
            }
            catch (Exception ex)
            {
                lordAttempts++;
                Log.Warning("[WNG] Wraith Stargate envoy emerged but native Lord assignment failed: " + ex.Message);
                if (lordAttempts >= MaximumLordAttempts)
                    ClearTracking();
            }
        }

        private void ClearTracking()
        {
            visitors = visitors ?? new List<Pawn>();
            visitors.Clear();
            envoyFaction = null;
            sourceGate = null;
            visitDurationTicks = 0;
            preparedTick = -1;
            lordAttempts = 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref visitors, "wngWraithGateEnvoyVisitors", LookMode.Reference);
            Scribe_References.Look(ref envoyFaction, "wngWraithGateEnvoyFaction");
            Scribe_References.Look(ref sourceGate, "wngWraithGateEnvoySourceGate");
            Scribe_Values.Look(ref visitDurationTicks, "wngWraithGateEnvoyDuration", 0);
            Scribe_Values.Look(ref preparedTick, "wngWraithGateEnvoyPreparedTick", -1);
            Scribe_Values.Look(ref lordAttempts, "wngWraithGateEnvoyLordAttempts", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                visitors = visitors?
                    .Where(p => p != null && !p.Dead && !p.Destroyed)
                    .Distinct()
                    .ToList() ?? new List<Pawn>();
                lordAttempts = Math.Max(0, Math.Min(MaximumLordAttempts, lordAttempts));
                if (visitors.Count == 0)
                    ClearTracking();
            }
        }
    }

    /// <summary>
    /// Optional non-hostile CatCraft Stargate traffic for Wraith politics. Arrival itself grants no
    /// goodwill, treaty, research, feeding access or other reward; ordinary RimWorld relations and
    /// the existing WNG Wraith diplomacy systems remain authoritative.
    /// </summary>
    public sealed class IncidentWorker_WraithStargateDiplomaticEnvoy : IncidentWorker
    {
        private const float DefaultVisitorPoints = 130f;
        private const float MinimumVisitorPoints = 90f;
        private const float MaximumVisitorPoints = 230f;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms?.target is Map map) || !map.IsPlayerHome || Faction.OfPlayer == null)
                return false;

            if (map.GetComponent<MapComponent_WraithStargateEnvoy>()?.HasActiveEnvoy == true ||
                map.GetComponent<MapComponent_QuietLatticeStargateVisit>()?.HasActiveVisit == true ||
                map.GetComponent<MapComponent_WNGGateControlObjective>()?.Active == true ||
                map.GetComponent<MapComponent_WraithGateHunt>()?.Active == true ||
                map.GetComponent<MapComponent_WraithGatePursuit>()?.Active == true)
                return false;

            foreach (GameCondition condition in map.GameConditionManager.ActiveConditions)
                if (condition?.def?.preventNeutralVisitors == true)
                    return false;

            return WraithStargateEnvoyUtility.EligibleFactions()
                .Any(f => QuietLatticeStargateVisitUtility.HasUsableStargate(map, f)) &&
                base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms?.target is Map map) || !map.IsPlayerHome || Faction.OfPlayer == null)
                return false;

            MapComponent_WraithStargateEnvoy component =
                map.GetComponent<MapComponent_WraithStargateEnvoy>();
            if (component == null || component.HasActiveEnvoy)
                return false;

            List<Faction> candidates = WraithStargateEnvoyUtility.EligibleFactions()
                .Where(f => QuietLatticeStargateVisitUtility.HasUsableStargate(map, f))
                .ToList();
            if (candidates.Count == 0)
                return false;

            Faction faction = candidates.RandomElement();
            PawnsArrivalModeDef mode = QuietLatticeStargateVisitUtility.StargateArrivalMode;
            if (mode?.Worker == null)
                return false;

            parms.faction = faction;
            parms.points = Math.Max(
                MinimumVisitorPoints,
                Math.Min(
                    MaximumVisitorPoints,
                    parms.points > 0f ? parms.points : DefaultVisitorPoints));
            parms.raidArrivalMode = mode;

            try
            {
                if (!mode.Worker.TryResolveRaidSpawnCenter(parms) ||
                    parms.raidArrivalMode != mode ||
                    !QuietLatticeStargateVisitUtility.IsResolvedHomeMapStargate(map, parms.spawnCenter))
                    return false;
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Wraith diplomatic Stargate probe failed safely: " + ex.Message);
                return false;
            }

            Thing gate = WraithStargateEnvoyUtility.ExactResolvedGate(map, parms.spawnCenter);
            if (gate == null)
                return false;

            PawnGroupMakerParms groupParms =
                IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                    PawnGroupKindDefOf.Peaceful,
                    parms,
                    ensureCanGenerateAtLeastOnePawn: true);

            List<Pawn> allGenerated;
            try
            {
                allGenerated = PawnGroupMakerUtility
                    .GeneratePawns(groupParms, warnOnZeroResults: false)
                    .Where(p => p != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Wraith diplomatic envoy generation failed: " + ex.Message);
                return false;
            }

            if (allGenerated.Count == 0)
                return false;

            List<Pawn> visitors = allGenerated
                .Take(WraithStargateEnvoyUtility.MaximumVisitors)
                .ToList();
            foreach (Pawn extra in allGenerated.Skip(WraithStargateEnvoyUtility.MaximumVisitors))
                if (extra != null && !extra.Destroyed)
                    extra.Destroy(DestroyMode.Vanish);

            if (!component.TryPrepare(
                    visitors,
                    faction,
                    gate,
                    WraithStargateEnvoyUtility.VisitDurationTicks))
            {
                DestroyUncommitted(visitors);
                return false;
            }

            try
            {
                mode.Worker.Arrive(visitors, parms);
            }
            catch (Exception ex)
            {
                bool maybeCommitted = visitors.Any(p =>
                    p != null && !p.Destroyed && (p.Spawned || p.ParentHolder != null));
                if (!maybeCommitted)
                {
                    component.CancelPrepared();
                    DestroyUncommitted(visitors);
                    Log.Warning("[WNG] Wraith Stargate envoy failed before CatCraft took ownership: " + ex.Message);
                    return false;
                }

                Log.Warning("[WNG] Wraith Stargate envoy reported an exception after CatCraft may have taken ownership; preserving the exact tracked Pawns: " + ex.Message);
                return true;
            }

            try
            {
                int goodwill = faction.BaseGoodwillWith(Faction.OfPlayer);
                Find.LetterStack.ReceiveLetter(
                    "Wraith envoy through the Stargate",
                    "A neutral envoy from " + faction.Name +
                    " is entering through the colony's Stargate. Their current goodwill with the colony is " +
                    goodwill + ". This is political traffic, not a culling run: arrival grants no goodwill, treaty, feeding right or technology by itself. Any concrete bargain remains governed by ordinary relations and the existing Wraith diplomacy systems.",
                    LetterDefOf.NeutralEvent,
                    gate);
            }
            catch { }
            return true;
        }

        private static void DestroyUncommitted(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns ?? Enumerable.Empty<Pawn>())
            {
                if (pawn == null || pawn.Destroyed || pawn.Spawned || pawn.ParentHolder != null)
                    continue;
                if (!Find.WorldPawns.Contains(pawn))
                    pawn.Destroy(DestroyMode.Vanish);
            }
        }
    }
}
