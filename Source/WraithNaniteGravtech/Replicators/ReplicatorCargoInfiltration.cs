using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class ReplicatorCargoInfiltrationUtility
    {
        public const int MinimumSteel = 60;
        public const int MaximumSteel = 90;
        public const int MinimumComponents = 4;
        public const int MaximumComponents = 7;

        public static bool EligibleMap(Map map)
        {
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                map == null ||
                !map.IsPlayerHome ||
                ReplicatorAssimilationUtility.CountHostileBlocks(map) > 0)
                return false;

            ThingDef sealedDef =
                DefDatabase<ThingDef>.GetNamedSilentFail("WNG_SealedMachineSalvage");
            if (sealedDef == null)
                return false;

            return !map.listerThings.ThingsOfDef(sealedDef)
                .Any(t => t != null && !t.Destroyed);
        }

        public static bool TryDropConsignment(
            Map map,
            bool includeSupplies,
            out IntVec3 dropCell)
        {
            dropCell = IntVec3.Invalid;
            if (!EligibleMap(map))
                return false;

            ThingDef sealedDef =
                DefDatabase<ThingDef>.GetNamedSilentFail("WNG_SealedMachineSalvage");
            if (sealedDef == null)
                return false;

            dropCell = DropCellFinder.RandomDropSpot(map);
            if (!dropCell.IsValid)
                return false;

            Thing sealedSalvage = ThingMaker.MakeThing(sealedDef);
            if (sealedSalvage == null)
                return false;

            List<Thing> cargo = new List<Thing> { sealedSalvage };

            if (includeSupplies)
            {
                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = Rand.RangeInclusive(MinimumSteel, MaximumSteel);
                cargo.Add(steel);

                Thing components = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
                components.stackCount = Rand.RangeInclusive(
                    MinimumComponents,
                    MaximumComponents);
                cargo.Add(components);
            }

            try
            {
                DropPodUtility.DropThingsNear(
                    dropCell,
                    map,
                    cargo);
                return true;
            }
            catch (Exception ex)
            {
                // If RimWorld committed the exact sealed carrier into a drop-pod holder or spawned
                // it before another payload/presentation step failed, the gameplay transaction has
                // already happened. Treat that as success so the storyteller cannot duplicate it.
                bool committed =
                    sealedSalvage.Destroyed ||
                    sealedSalvage.Spawned ||
                    sealedSalvage.ParentHolder != null;

                if (!committed)
                {
                    foreach (Thing thing in cargo)
                    {
                        if (thing != null &&
                            !thing.Destroyed &&
                            !thing.Spawned &&
                            thing.ParentHolder == null)
                            thing.Destroy(DestroyMode.Vanish);
                    }
                }

                Log.Warning(
                    committed
                        ? "[WNG] Replicator contaminated cargo partially committed; suppressing storyteller retry: " + ex.Message
                        : "[WNG] Replicator contaminated cargo failed before physical commit: " + ex.Message);
                return committed;
            }
        }

        public static void BestEffortLetter(
            string label,
            string text,
            LetterDef letterDef,
            LookTargets targets)
        {
            try
            {
                Find.LetterStack.ReceiveLetter(
                    label,
                    text,
                    letterDef,
                    targets);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator cargo gameplay committed but letter presentation failed: " +
                    ex.Message);
            }
        }
    }

    /// <summary>
    /// A deliberately misleading but bounded cargo event. The player receives useful industrial
    /// supplies plus one exact sealed salvage carrier. The carrier, not the incident worker, owns
    /// the later contamination lifecycle.
    /// </summary>
    public sealed class IncidentWorker_ReplicatorContaminatedCargo : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            return map != null &&
                   ReplicatorCargoInfiltrationUtility.EligibleMap(map) &&
                   base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null ||
                !ReplicatorCargoInfiltrationUtility.TryDropConsignment(
                    map,
                    includeSupplies: true,
                    out IntVec3 cell))
                return false;

            ReplicatorCargoInfiltrationUtility.BestEffortLetter(
                "Recovered salvage cargo",
                "An automated recovery beacon has routed a small cargo pod to the colony: " +
                "ordinary industrial supplies and one sealed piece of machine salvage. " +
                "The casing reports no conventional power source, although its construction is unusually precise.",
                LetterDefOf.PositiveEvent,
                new TargetInfo(cell, map));

            return true;
        }
    }
}
