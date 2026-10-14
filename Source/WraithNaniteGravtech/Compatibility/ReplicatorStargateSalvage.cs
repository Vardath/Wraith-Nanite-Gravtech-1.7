using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    internal static class ReplicatorStargateSalvageUtility
    {
        public const int MinimumMatter = 20;
        public const int MaximumMatter = 40;
        public const int MaximumExistingMatter = 60;

        public static bool TryResolveExactHomeGate(
            Map map,
            out Thing gate,
            out IntVec3 gateCell)
        {
            gate = null;
            gateCell = IntVec3.Invalid;

            PawnsArrivalModeDef mode =
                QuietLatticeStargateVisitUtility.StargateArrivalMode;
            if (map == null || mode?.Worker == null || Find.FactionManager == null)
                return false;

            List<Faction> candidates = new List<Faction>();
            Faction quiet = QuietLatticeStargateVisitUtility.QuietFaction;
            if (quiet != null && !quiet.defeated)
                candidates.Add(quiet);

            candidates.AddRange(
                Find.FactionManager.AllFactions
                    .Where(f =>
                        f != null &&
                        !f.defeated &&
                        !candidates.Contains(f))
                    .OrderBy(f => f.loadID));

            foreach (Faction faction in candidates)
            {
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
                        continue;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Replicator Stargate-salvage probe failed safely for " +
                        (faction.Name ?? faction.def?.defName ?? "<unknown faction>") +
                        ": " + ex.Message);
                    continue;
                }

                gateCell = probe.spawnCenter;
                gate = map.thingGrid.ThingsListAtFast(gateCell)
                    .FirstOrDefault(t =>
                        t?.Map == map &&
                        string.Equals(
                            t.def?.thingClass?.FullName,
                            QuietLatticeStargateVisitUtility.StargateThingClassName,
                            StringComparison.Ordinal));

                if (gate != null)
                    return true;
            }

            gate = null;
            gateCell = IntVec3.Invalid;
            return false;
        }

        public static int ExistingMatterCount(
            Map map,
            ThingDef matterDef)
        {
            if (map == null || matterDef == null)
                return 0;

            return map.listerThings.ThingsOfDef(matterDef)
                .Where(t => t != null && !t.Destroyed && t.Spawned)
                .Sum(t => Math.Max(0, t.stackCount));
        }
    }

    /// <summary>
    /// Low-intensity optional CatCraft contamination event. CatCraft's own arrival resolver chooses
    /// the exact usable home-map gate; WNG never dials it, owns an address, or touches receive
    /// buffers. One ordinary Matter stack is then physically placed beside that exact gate.
    /// </summary>
    public sealed class IncidentWorker_ReplicatorStargateSalvage :
        IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                map == null ||
                !map.IsPlayerHome ||
                ReplicatorAssimilationUtility.CountHostileBlocks(map) > 0)
                return false;

            ThingDef matterDef =
                DefDatabase<ThingDef>.GetNamedSilentFail(
                    "WNG_ReplicatorMatter");
            if (matterDef == null ||
                ReplicatorStargateSalvageUtility.ExistingMatterCount(
                    map,
                    matterDef) >=
                ReplicatorStargateSalvageUtility.MaximumExistingMatter)
                return false;

            return ReplicatorStargateSalvageUtility.TryResolveExactHomeGate(
                       map,
                       out _,
                       out _) &&
                   base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                map == null ||
                !map.IsPlayerHome ||
                ReplicatorAssimilationUtility.CountHostileBlocks(map) > 0)
                return false;

            ThingDef matterDef =
                DefDatabase<ThingDef>.GetNamedSilentFail(
                    "WNG_ReplicatorMatter");
            if (matterDef == null ||
                ReplicatorStargateSalvageUtility.ExistingMatterCount(
                    map,
                    matterDef) >=
                ReplicatorStargateSalvageUtility.MaximumExistingMatter)
                return false;

            if (!ReplicatorStargateSalvageUtility.TryResolveExactHomeGate(
                    map,
                    out Thing gate,
                    out IntVec3 gateCell))
                return false;

            Thing matter = ThingMaker.MakeThing(matterDef);
            if (matter == null)
                return false;

            matter.stackCount = Math.Min(
                matterDef.stackLimit,
                Rand.RangeInclusive(
                    ReplicatorStargateSalvageUtility.MinimumMatter,
                    ReplicatorStargateSalvageUtility.MaximumMatter));

            bool committed;
            try
            {
                committed = GenPlace.TryPlaceThing(
                    matter,
                    gate.Position,
                    map,
                    ThingPlaceMode.Near);
            }
            catch (Exception ex)
            {
                committed =
                    matter.Spawned &&
                    matter.Map == map;
                Log.Warning(
                    committed
                        ? "[WNG] Replicator Stargate salvage reported an exception after Matter placement; preserving the physical commit: " + ex.Message
                        : "[WNG] Replicator Stargate salvage failed before physical Matter placement: " + ex.Message);
            }

            if (!committed)
            {
                if (!matter.Destroyed &&
                    !matter.Spawned &&
                    matter.ParentHolder == null)
                    matter.Destroy(DestroyMode.Vanish);
                return false;
            }

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Contaminated Stargate salvage",
                    "Apparently inert modular machine substrate has appeared beside the exact Stargate CatCraft currently resolves for this map. " +
                    "It is ordinary Replicator Matter rather than an active unit. The stack is still governed by the current uncontained-exposure, powered-containment and matter-first reassembly rules.",
                    LetterDefOf.ThreatSmall,
                    matter);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator Stargate salvage committed but presentation failed: " +
                    ex.Message);
            }

            return true;
        }
    }
}
