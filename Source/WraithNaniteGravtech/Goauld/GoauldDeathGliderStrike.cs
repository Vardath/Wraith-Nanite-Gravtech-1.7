using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Small hostile interoperability layer: one real WNG Death Glider, crewed by two exact Jaffa
    /// generated from the selected external System-Lord faction's own Combat roster, performs the
    /// same save-persistent two-pass physical sortie used by the player craft.
    /// </summary>
    public sealed class IncidentWorker_GoauldDeathGliderStrike : IncidentWorker
    {
        private const string GliderDefName = "WNG_GoauldDeathGlider_NPC";
        private const string LegacyPlayerGliderDefName = "WNG_GoauldDeathGlider";
        private const string AttackPassDefName = "WNG_GoauldDeathGliderAttackPass";

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || Faction.OfPlayer == null || !GoauldOptionalInterop.FullEcosystemActive())
                return false;

            ThingDef gliderDef = DefDatabase<ThingDef>.GetNamedSilentFail(GliderDefName);
            if (gliderDef == null || !map.mapPawns.FreeColonistsSpawned.Any(p => p != null && !p.Dead))
                return false;
            if (HostileGliderOperationAlreadyActive(map, gliderDef))
                return false;

            return CandidateFactions(parms.faction).Any(f => GoauldOptionalInterop.CombatJaffaKinds(f).Count > 0) &&
                   base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || !GoauldOptionalInterop.FullEcosystemActive())
                return false;

            ThingDef gliderDef = DefDatabase<ThingDef>.GetNamedSilentFail(GliderDefName);
            if (gliderDef == null || HostileGliderOperationAlreadyActive(map, gliderDef))
                return false;

            List<Faction> factions = CandidateFactions(parms.faction)
                .Where(f => GoauldOptionalInterop.CombatJaffaKinds(f).Count > 0)
                .ToList();
            if (factions.Count == 0)
                return false;

            Faction faction = parms.faction != null && factions.Contains(parms.faction)
                ? parms.faction
                : factions.RandomElement();
            List<PawnKindDef> crewKinds = GoauldOptionalInterop.CombatJaffaKinds(faction);
            if (crewKinds.Count == 0)
                return false;

            IntVec3 entryCell;
            if (!TryFindEntryCell(map, gliderDef, out entryCell))
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
                    throw new InvalidOperationException("The hostile Death Glider could not be physically placed.");

                CompTransporter transporter = glider.TryGetComp<CompTransporter>();
                CompRefuelable fuel = glider.TryGetComp<CompRefuelable>();
                CompGoauldDeathGliderMission mission = glider.TryGetComp<CompGoauldDeathGliderMission>();
                if (transporter == null || fuel == null || mission == null)
                    throw new InvalidOperationException("The Death Glider native transporter/fuel/mission stack is incomplete.");

                fuel.Refuel(fuel.Props.fuelCapacity);
                for (int i = 0; i < 2; i++)
                {
                    Pawn pawn = GoauldOptionalInterop.GenerateExactJaffa(faction, crewKinds);
                    if (pawn == null)
                        throw new InvalidOperationException("Could not generate an exact Jaffa crew member from the external faction roster.");
                    if (!transporter.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false))
                    {
                        if (!pawn.Destroyed)
                            pawn.Destroy(DestroyMode.Vanish);
                        throw new InvalidOperationException("Could not load an exact Jaffa crew member into the native transporter.");
                    }
                    transporter.Notify_ThingAdded(pawn);
                }

                // Hostile incident craft withdraw physically after their final pass. Player-owned
                // Death Gliders keep the normal sortie-and-return behavior.
                mission.ConfigureHostileRetreat();

                // Mechanical commit is the first physical attack-pass holder successfully taking the
                // exact craft. Before this point all generated state can still be destroyed cleanly.
                physicallyCommitted = mission.TryBeginCombatSortie(showFailureMessage: false);
                if (!physicallyCommitted)
                    throw new InvalidOperationException("The exact hostile Death Glider could not enter its first physical pass.");
            }
            catch (Exception ex)
            {
                // If the exact craft already has a real parent holder, the first physical pass owns it
                // even if an exception prevented TryBeginCombatSortie from returning normally. Never
                // mistake that post-commit state for a rollback-safe pre-commit failure.
                bool holderCommitted = glider.ParentHolder != null && !glider.Spawned;
                if (!physicallyCommitted && !holderCommitted)
                {
                    Log.Warning("[WNG] System-Lord Death Glider strike aborted before physical commit: " + ex.Message);
                    RollBackUncommittedGlider(glider);
                    return false;
                }

                physicallyCommitted = true;
                Log.Error("[WNG] Death Glider strike threw after physical commit; preserving the exact in-flight craft: " + ex);
            }

            parms.faction = faction;
            try
            {
                SendStandardLetter(def.letterLabel, def.letterText, def.letterDef, parms, new TargetInfo(entryCell, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Death Glider strike committed, but its presentation letter failed: " + ex.Message);
            }
            return true;
        }

        private static IEnumerable<Faction> CandidateFactions(Faction requested)
        {
            IEnumerable<Faction> all = GoauldOptionalInterop.ActiveSystemLordFactions()
                .Where(f => f != null && !f.defeated && Faction.OfPlayer != null && f.HostileTo(Faction.OfPlayer));
            if (requested == null)
                return all;
            return all.Where(f => f == requested);
        }

        private static bool HostileGliderOperationAlreadyActive(Map map, ThingDef gliderDef)
        {
            if (map == null || gliderDef == null)
                return true;

            if (map.listerThings.ThingsOfDef(gliderDef).Any(t =>
                t != null && !t.Destroyed && GoauldOptionalInterop.IsSystemLordFaction(t.Faction)))
                return true;

            // Preserve old saves that already contain a hostile player-Def Glider from before
            // the player/NPC shuttle split was restored. Do not spawn a duplicate operation.
            ThingDef legacyPlayerDef = DefDatabase<ThingDef>.GetNamedSilentFail(LegacyPlayerGliderDefName);
            if (legacyPlayerDef != null && legacyPlayerDef != gliderDef &&
                map.listerThings.ThingsOfDef(legacyPlayerDef).Any(t =>
                    t != null && !t.Destroyed && GoauldOptionalInterop.IsSystemLordFaction(t.Faction)))
                return true;

            ThingDef passDef = DefDatabase<ThingDef>.GetNamedSilentFail(AttackPassDefName);
            return passDef != null && map.listerThings.ThingsOfDef(passDef).Any(t => t != null && !t.Destroyed);
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
            {
                if (!c.InBounds(map) || c.Fogged(map) || c.Roofed(map))
                    return false;
            }

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
