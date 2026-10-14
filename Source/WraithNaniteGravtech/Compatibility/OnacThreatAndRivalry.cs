using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    internal static class OnacWraithRivalryUtility
    {
        public const int CinderRivalryGoodwillCeiling = -80;
        public const int VeiledRivalryGoodwillCeiling = -35;
        public const int PaleRivalryGoodwillCeiling = -10;

        public static int? RivalryGoodwillCeiling(Faction wraithFaction)
        {
            switch (wraithFaction?.def?.defName)
            {
                case WraithLineageUtility.CinderCourtDefName:
                    return CinderRivalryGoodwillCeiling;
                case WraithLineageUtility.VeiledHiveDefName:
                    return VeiledRivalryGoodwillCeiling;
                case WraithLineageUtility.PaleCovenantDefName:
                    return PaleRivalryGoodwillCeiling;
                // Sable Brood already uses RimWorld's native permanent-enemy authority.
                default:
                    return null;
            }
        }

        public static bool TryApplyInitialRivalry(Faction externalFaction, Faction wraithFaction)
        {
            if (externalFaction == null || wraithFaction == null || externalFaction == wraithFaction ||
                externalFaction.defeated || wraithFaction.defeated ||
                externalFaction == Faction.OfPlayer || wraithFaction == Faction.OfPlayer ||
                !GoauldOptionalInterop.IsOnacEcosystemFaction(externalFaction) ||
                !WraithLineageUtility.IsWraithLineage(wraithFaction))
                return true;

            int? ceiling = RivalryGoodwillCeiling(wraithFaction);
            if (!ceiling.HasValue)
                return true;

            // Never silently destroy a relationship the player or another system already made.
            if (externalFaction.RelationKindWith(wraithFaction) == FactionRelationKind.Ally)
                return true;

            int current = externalFaction.BaseGoodwillWith(wraithFaction);
            if (current <= ceiling.Value)
                return true;

            int delta = ceiling.Value - current;
            if (!externalFaction.CanChangeGoodwillFor(wraithFaction, delta))
                return true;

            return externalFaction.TryAffectGoodwillWith(
                wraithFaction,
                delta,
                canSendMessage: false,
                canSendHostilityLetter: false);
        }
    }

    /// <summary>
    /// Processes each verified ONAC/RimGate faction and mutable Wraith lineage pair once. This is
    /// initial rival-power recognition only; after a pair is processed, ordinary RimWorld goodwill,
    /// treaties, quests and later player actions remain authoritative.
    /// </summary>
    public sealed class OnacWraithRivalryRegistry : GameComponent
    {
        private const int RetryTicks = 600;
        private List<string> processedPairKeys = new List<string>();

        public OnacWraithRivalryRegistry(Game game) { }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null ||
                Find.TickManager.TicksGame % RetryTicks != 0 ||
                !GoauldOptionalInterop.FullEcosystemActive())
                return;

            processedPairKeys ??= new List<string>();

            foreach (Faction externalFaction in GoauldOptionalInterop.ActiveOnacEcosystemFactions())
            {
                if (externalFaction == null || externalFaction == Faction.OfPlayer)
                    continue;

                foreach (Faction wraithFaction in WraithLineageUtility.ActiveLineages())
                {
                    if (wraithFaction == null || wraithFaction == Faction.OfPlayer)
                        continue;

                    string key = externalFaction.loadID + ":" + wraithFaction.loadID;
                    if (processedPairKeys.Contains(key))
                        continue;

                    if (OnacWraithRivalryUtility.TryApplyInitialRivalry(externalFaction, wraithFaction))
                        processedPairKeys.Add(key);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref processedPairKeys, "wngOnacWraithRivalryPairs", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                processedPairKeys ??= new List<string>();
        }
    }

    /// <summary>
    /// #132: a verified System-Lord combat force may react to a Replicator infestation by sending
    /// one real WNG Death Glider. The external faction supplies the exact Jaffa crew; the existing
    /// WNG craft/flight stack owns the sortie. The Glider's guns are priority-locked to the hidden
    /// WNG Replicator swarm for this mission and faction goodwill with the colony is not changed.
    /// </summary>
    public sealed class IncidentWorker_OnacReplicatorThreatResponse : IncidentWorker
    {
        private const string ReplicatorFactionDefName = "WNG_ReplicatorSwarm";
        private const string GliderDefName = "WNG_GoauldDeathGlider_NPC";
        private const string LegacyGliderDefName = "WNG_GoauldDeathGlider";
        private const string AttackPassDefName = "WNG_GoauldDeathGliderAttackPass";

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !map.IsPlayerHome || !GoauldOptionalInterop.FullEcosystemActive())
                return false;

            Faction replicators = ResolveReplicatorFaction();
            ThingDef gliderDef = DefDatabase<ThingDef>.GetNamedSilentFail(GliderDefName);
            if (replicators == null || gliderDef == null ||
                !ReplicatorTargets(map, replicators).Any() ||
                GliderOperationAlreadyActive(map, gliderDef))
                return false;

            return CandidateResponders(parms.faction).Any() && base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !map.IsPlayerHome || !GoauldOptionalInterop.FullEcosystemActive())
                return false;

            Faction replicators = ResolveReplicatorFaction();
            ThingDef gliderDef = DefDatabase<ThingDef>.GetNamedSilentFail(GliderDefName);
            if (replicators == null || gliderDef == null || GliderOperationAlreadyActive(map, gliderDef))
                return false;

            List<Thing> targets = ReplicatorTargets(map, replicators).ToList();
            if (targets.Count == 0)
                return false;

            List<Faction> responders = CandidateResponders(parms.faction).ToList();
            if (responders.Count == 0)
                return false;

            Faction faction = parms.faction != null && responders.Contains(parms.faction)
                ? parms.faction
                : responders.RandomElement();
            List<PawnKindDef> crewKinds = GoauldOptionalInterop.CombatJaffaKinds(faction);
            if (crewKinds.Count == 0)
                return false;

            Thing focus = targets
                .OrderBy(t => t is Pawn ? 0 : 1)
                .ThenBy(t => t.Position.DistanceToSquared(map.Center))
                .ThenBy(t => t.thingIDNumber)
                .First();

            if (!TryFindEntryCell(map, gliderDef, out IntVec3 entryCell))
                return false;

            Thing glider = ThingMaker.MakeThing(gliderDef);
            if (glider == null)
                return false;

            bool physicallyCommitted = false;
            try
            {
                glider.SetFactionDirect(faction);
                Thing spawned = GenSpawn.Spawn(glider, entryCell, map, Rot4.North, WipeMode.Vanish);
                if (spawned?.Spawned != true)
                    throw new InvalidOperationException("The Replicator-response Death Glider could not be physically placed.");

                CompTransporter transporter = glider.TryGetComp<CompTransporter>();
                CompRefuelable fuel = glider.TryGetComp<CompRefuelable>();
                CompGoauldDeathGliderMission mission = glider.TryGetComp<CompGoauldDeathGliderMission>();
                if (transporter == null || fuel == null || mission == null)
                    throw new InvalidOperationException("The Death Glider transporter/fuel/mission stack is incomplete.");

                fuel.Refuel(fuel.Props.fuelCapacity);
                for (int i = 0; i < 2; i++)
                {
                    Pawn pawn = GoauldOptionalInterop.GenerateExactJaffa(faction, crewKinds);
                    if (pawn == null)
                        throw new InvalidOperationException("Could not generate an exact Jaffa responder from the external faction roster.");
                    if (!transporter.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false))
                    {
                        if (!pawn.Destroyed)
                            pawn.Destroy(DestroyMode.Vanish);
                        throw new InvalidOperationException("Could not load an exact Jaffa responder into the native transporter.");
                    }
                    transporter.Notify_ThingAdded(pawn);
                }

                mission.ConfigurePriorityTarget(replicators, focus.Position);
                mission.ConfigureHostileRetreat();

                physicallyCommitted = mission.TryBeginCombatSortie(showFailureMessage: false);
                if (!physicallyCommitted)
                    throw new InvalidOperationException("The exact Replicator-response Death Glider could not enter its first physical pass.");
            }
            catch (Exception ex)
            {
                bool holderCommitted = glider.ParentHolder != null && !glider.Spawned;
                if (!physicallyCommitted && !holderCommitted)
                {
                    Log.Warning("[WNG] ONAC Replicator-threat response aborted before physical commit: " + ex.Message);
                    RollBackUncommittedGlider(glider);
                    return false;
                }

                physicallyCommitted = true;
                Log.Error("[WNG] ONAC Replicator-threat response threw after physical commit; preserving the exact in-flight craft: " + ex);
            }

            parms.faction = faction;
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "System-Lord anti-Replicator sortie",
                    faction.Name +
                    " has identified the active Replicator swarm as an extreme technological threat and diverted a two-seat Death Glider against it. " +
                    "The exact Jaffa-crewed fighter is priority-locked to Replicator targets for this sortie. " +
                    "This intervention does not change the faction's goodwill or treaty status with the colony.",
                    LetterDefOf.NeutralEvent,
                    new TargetInfo(focus.Position, map));
            }
            catch { }
            return true;
        }

        private static IEnumerable<Faction> CandidateResponders(Faction requested)
        {
            IEnumerable<Faction> all = GoauldOptionalInterop.ActiveSystemLordFactions()
                .Where(f => f != null && !f.defeated)
                .Where(f => GoauldOptionalInterop.CombatJaffaKinds(f).Count > 0);
            return requested == null ? all : all.Where(f => f == requested);
        }

        private static Faction ResolveReplicatorFaction()
        {
            if (Find.FactionManager == null)
                return null;
            return Find.FactionManager.AllFactions
                .FirstOrDefault(f =>
                    f != null &&
                    !f.defeated &&
                    f.def?.defName == ReplicatorFactionDefName);
        }

        private static IEnumerable<Thing> ReplicatorTargets(Map map, Faction replicators)
        {
            if (map?.listerThings?.AllThings == null || replicators == null)
                return Enumerable.Empty<Thing>();

            return map.listerThings.AllThings
                .Where(t =>
                    t != null &&
                    !t.Destroyed &&
                    t.Spawned &&
                    t.Map == map &&
                    t.Faction == replicators &&
                    (t is Pawn || t is Building));
        }

        private static bool GliderOperationAlreadyActive(Map map, ThingDef gliderDef)
        {
            if (map == null || gliderDef == null)
                return true;

            if (map.listerThings.ThingsOfDef(gliderDef).Any(t => t != null && !t.Destroyed))
                return true;

            ThingDef legacy = DefDatabase<ThingDef>.GetNamedSilentFail(LegacyGliderDefName);
            if (legacy != null && legacy != gliderDef &&
                map.listerThings.ThingsOfDef(legacy).Any(t =>
                    t != null && !t.Destroyed && GoauldOptionalInterop.IsSystemLordFaction(t.Faction)))
                return true;

            ThingDef passDef = DefDatabase<ThingDef>.GetNamedSilentFail(AttackPassDefName);
            return passDef != null &&
                   map.listerThings.ThingsOfDef(passDef).Any(t => t != null && !t.Destroyed);
        }

        private static bool TryFindEntryCell(Map map, ThingDef gliderDef, out IntVec3 cell)
        {
            return CellFinder.TryFindRandomEdgeCellWith(
                c => CanPlaceUnroofedGliderAt(c, map, gliderDef),
                map,
                CellFinder.EdgeRoadChance_Hostile,
                out cell);
        }

        private static bool CanPlaceUnroofedGliderAt(IntVec3 root, Map map, ThingDef gliderDef)
        {
            if (map == null || gliderDef == null || !root.InBounds(map) || root.Fogged(map))
                return false;

            foreach (IntVec3 c in GenAdj.OccupiedRect(root, Rot4.North, gliderDef.size))
                if (!c.InBounds(map) || c.Fogged(map) || c.Roofed(map))
                    return false;

            return GenSpawn.CanSpawnAt(gliderDef, root, map, Rot4.North, canWipeEdifices: false);
        }

        private static void RollBackUncommittedGlider(Thing glider)
        {
            if (glider == null)
                return;

            CompTransporter transporter = glider.TryGetComp<CompTransporter>();
            transporter?.innerContainer?.ClearAndDestroyContents();

            if (glider.Spawned)
                glider.DeSpawn();
            if (!glider.Destroyed && glider.ParentHolder == null)
                glider.Destroy(DestroyMode.Vanish);
        }
    }
}
