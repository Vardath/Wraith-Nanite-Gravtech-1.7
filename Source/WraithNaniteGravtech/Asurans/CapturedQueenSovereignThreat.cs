using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Real captured-Queen consequence. The exact retained Queen remains the controller reference,
    /// while every mixed threat receives its own controller domain so separate raids do not merge,
    /// coordinate, repair or retaliate together merely because they share the Lattice faction.
    /// </summary>
    public static class CapturedQueenSovereignUtility
    {
        public const string LatticeFactionDefName = "WNG_PrecursorCollective";

        public static bool IsValidRetainedQueen(Pawn queen, Faction retainingFaction)
        {
            GameComponent_ReplicatorQueenState state = ReplicatorQueenUtility.State;
            return state != null && state.Captured && queen != null && !queen.Dead &&
                   state.ExactQueen == queen && retainingFaction != null && queen.Faction == retainingFaction &&
                   retainingFaction.def?.defName == LatticeFactionDefName;
        }

        public static bool TryGetRetainingFaction(out Pawn queen, out Faction faction)
        {
            queen = ReplicatorQueenUtility.State?.ExactQueen;
            faction = queen?.Faction;
            return IsValidRetainedQueen(queen, faction);
        }

        public static string BuildThreatDomainId(Pawn queen, Map map, int now)
        {
            if (queen == null || map == null)
                return null;
            return "captured-queen:" + queen.thingIDNumber + ":map:" + map.uniqueID + ":tick:" + Math.Max(0, now);
        }

        public static bool HasActiveSovereignThreat(Map map, Faction faction)
        {
            if (map == null || faction == null)
                return false;
            return map.mapPawns.AllPawnsSpawned.Any(p => p != null && !p.Dead && p.Faction == faction &&
                ReplicatorDomainUtility.Domain(p)?.Authority == ReplicatorControlAuthority.CapturedQueenSovereign);
        }
    }

    public sealed class IncidentWorker_CapturedQueenSovereignStrike : IncidentWorker
    {
        private const float MinimumThreatPoints = 760f;
        private const int MaximumPawns = 16;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !map.IsPlayerHome || Faction.OfPlayer == null)
                return false;
            if (!CapturedQueenSovereignUtility.TryGetRetainingFaction(out Pawn queen, out Faction faction))
                return false;
            if (queen == null || faction == null || !faction.HostileTo(Faction.OfPlayer))
                return false;
            return !CapturedQueenSovereignUtility.HasActiveSovereignThreat(map, faction);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !CanFireNowSub(parms) ||
                !CapturedQueenSovereignUtility.TryGetRetainingFaction(out Pawn queen, out Faction faction))
                return false;

            PawnKindDef commander = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_PrecursorCommander");
            PawnKindDef soldier = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_PrecursorSoldier");
            PawnKindDef humanForm = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_HumanFormReplicator");
            PawnKindDef drone = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_ReplicatorDrone");
            PawnKindDef hunter = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_ReplicatorHunter");
            PawnKindDef bulwark = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_ReplicatorBulwark");
            PawnKindDef artillery = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_ReplicatorArtillery");
            if (commander == null || soldier == null || humanForm == null || drone == null || hunter == null || bulwark == null || artillery == null)
                return false;

            float requestedPoints = parms.points > 0f ? parms.points : StorytellerUtility.DefaultThreatPointsNow(map);
            float budget = Math.Max(MinimumThreatPoints, requestedPoints);
            List<PawnKindDef> kinds = BuildComposition(budget, commander, soldier, humanForm, drone, hunter, bulwark, artillery);
            if (kinds.Count < 5 || !kinds.Any(IsBlockKind) || !kinds.Any(k => !IsBlockKind(k)))
                return false;

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entryCell, map, 0f))
                entryCell = CellFinder.RandomEdgeCell(map);

            List<Pawn> staged = new List<Pawn>();
            List<Pawn> spawned = new List<Pawn>();
            string domainId = CapturedQueenSovereignUtility.BuildThreatDomainId(queen, map, Find.TickManager?.TicksGame ?? 0);
            if (string.IsNullOrEmpty(domainId))
                return false;

            try
            {
                foreach (PawnKindDef kind in kinds)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                    if (pawn == null)
                        throw new InvalidOperationException("PawnGenerator returned null for " + kind.defName);
                    staged.Add(pawn);
                }

                foreach (Pawn pawn in staged)
                {
                    IntVec3 cell = CellFinder.RandomClosewalkCellNear(entryCell, map, 9);
                    GenSpawn.Spawn(pawn, cell, map);
                    spawned.Add(pawn);

                    if (ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                    {
                        CompReplicatorSovereignState sovereign = pawn.TryGetComp<CompReplicatorSovereignState>();
                        if (sovereign == null || !sovereign.TryAssignCapturedQueen(queen, faction, domainId))
                            throw new InvalidOperationException("Could not assign captured-Queen sovereignty to " + pawn.LabelShortCap);
                    }
                }

                // Revalidate the physical controller before the assault becomes committed. If the
                // exact Queen was lost during staging, the entire staged threat is rolled back.
                if (!CapturedQueenSovereignUtility.IsValidRetainedQueen(queen, faction))
                    throw new InvalidOperationException("The exact captured Queen is no longer retained by the Lattice faction.");

                LordJob_AssaultColony lordJob = new LordJob_AssaultColony(
                    faction,
                    canTimeoutOrFlee: true,
                    canKidnap: false,
                    sappers: false,
                    useAvoidGridSmart: true,
                    canSteal: false);
                Lord lord = LordMaker.MakeNewLord(faction, lordJob, map);
                foreach (Pawn pawn in spawned)
                    lord.AddPawn(pawn);
            }
            catch (Exception ex)
            {
                RollBackStagedPawns(staged, spawned);
                Log.Warning("[WNG] Captured-Queen sovereign strike failed before commit; staged pawns removed: " + ex.Message);
                return false;
            }

            try
            {
                List<Pawn> blockPawns = spawned.Where(ReplicatorAssimilationUtility.IsBlockReplicator).ToList();
                Find.LetterStack.ReceiveLetter(
                    "Queen-linked Replicator strike",
                    "The Lattice Collective is exploiting its physical custody of the exact Replicator Queen. A mixed Asuran force has arrived with real block Replicators bound to a remote sovereign domain rooted in her retained lattice. Destroying or recovering the Queen would end that remote sovereign authority; this is not a generic faction-wide Replicator bonus.",
                    LetterDefOf.ThreatBig,
                    new LookTargets(blockPawns.Count > 0 ? blockPawns : spawned));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Captured-Queen sovereign strike committed but presentation failed: " + ex.Message);
            }
            return true;
        }

        private static List<PawnKindDef> BuildComposition(
            float budget,
            PawnKindDef commander,
            PawnKindDef soldier,
            PawnKindDef humanForm,
            PawnKindDef drone,
            PawnKindDef hunter,
            PawnKindDef bulwark,
            PawnKindDef artillery)
        {
            List<PawnKindDef> result = new List<PawnKindDef>();
            float remaining = budget;

            // Every valid strike is visibly mixed before any scaling occurs.
            Add(result, commander, ref remaining);
            Add(result, soldier, ref remaining);
            Add(result, hunter, ref remaining);
            Add(result, drone, ref remaining);
            Add(result, drone, ref remaining);

            PawnKindDef[] humanCycle = { soldier, humanForm, soldier };
            PawnKindDef[] blockCycle = { hunter, drone, bulwark, drone, artillery, hunter };
            int humanIndex = 0;
            int blockIndex = 0;
            bool addBlock = true;

            while (result.Count < MaximumPawns)
            {
                PawnKindDef next = addBlock ? blockCycle[blockIndex++ % blockCycle.Length] : humanCycle[humanIndex++ % humanCycle.Length];
                addBlock = !addBlock;
                float cost = Math.Max(1f, next.combatPower);
                if (remaining < cost)
                {
                    bool anythingAffordable = humanCycle.Concat(blockCycle).Any(k => remaining >= Math.Max(1f, k.combatPower));
                    if (!anythingAffordable)
                        break;
                    continue;
                }
                Add(result, next, ref remaining);
            }
            return result;
        }

        private static void Add(List<PawnKindDef> result, PawnKindDef kind, ref float remaining)
        {
            if (kind == null)
                return;
            result.Add(kind);
            remaining -= Math.Max(1f, kind.combatPower);
        }

        private static bool IsBlockKind(PawnKindDef kind)
        {
            string defName = kind?.defName ?? string.Empty;
            return defName.StartsWith("WNG_Replicator", StringComparison.Ordinal) && defName != "WNG_ReplicatorQueenChild";
        }

        private static void RollBackStagedPawns(IEnumerable<Pawn> staged, ICollection<Pawn> spawned)
        {
            if (staged == null)
                return;
            foreach (Pawn pawn in staged)
            {
                if (pawn == null || pawn.Destroyed)
                    continue;
                try
                {
                    if (pawn.Spawned)
                        pawn.Destroy(DestroyMode.Vanish);
                    else
                        pawn.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Failed to remove an uncommitted captured-Queen strike pawn: " + ex.Message);
                }
            }
            spawned?.Clear();
        }
    }
}
