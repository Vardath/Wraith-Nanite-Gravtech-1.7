using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Bounded first-contact route for the autonomous block-Replicator ecosystem.
    /// This deliberately reuses the current Drone and physical loose-Block systems rather than
    /// reviving historical cargo proxies, hidden matter accounts or a second reproduction model.
    /// Retained consumed-ruin husks are inert physical evidence only and own no Replicator state.
    /// </summary>
    public static class ReplicatorFirstContactUtility
    {
        public const string SitePartDefName = "WNG_ReplicatorConsumedRuin";
        private const string SwarmFactionDefName = "WNG_ReplicatorSwarm";

        public static SitePartDef SitePartDef => DefDatabase<SitePartDef>.GetNamedSilentFail(SitePartDefName);

        public static Faction SwarmFaction
        {
            get
            {
                FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(SwarmFactionDefName);
                return def == null ? null : Find.FactionManager?.FirstFactionOfDef(def);
            }
        }

        public static bool HasLiveConsumedRuin()
        {
            SitePartDef part = SitePartDef;
            if (part == null || Find.WorldObjects?.AllWorldObjects == null)
                return false;

            return Find.WorldObjects.AllWorldObjects
                .OfType<Site>()
                .Any(site => site?.parts != null && site.parts.Any(p => p?.def == part));
        }
    }

    public sealed class IncidentWorker_ReplicatorConsumedRuinDiscovery : IncidentWorker
    {
        private const int MinSiteDistance = 8;
        private const int MaxSiteDistance = 24;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            return WNGSettingsUtility.ReplicatorStoryEventsEnabled
                && map != null
                && map.IsPlayerHome
                && ReplicatorFirstContactUtility.SitePartDef != null
                && ReplicatorFirstContactUtility.SwarmFaction != null
                && !ReplicatorFirstContactUtility.HasLiveConsumedRuin();
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            SitePartDef part = ReplicatorFirstContactUtility.SitePartDef;
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled || map == null || !map.IsPlayerHome || part == null || ReplicatorFirstContactUtility.SwarmFaction == null)
                return false;
            if (ReplicatorFirstContactUtility.HasLiveConsumedRuin())
                return false;

            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, MinSiteDistance, MaxSiteDistance, allowCaravans: false))
                return false;

            Site site = SiteMaker.MakeSite(
                part,
                tile,
                faction: null,
                ifHostileThenMustRemainHostile: false,
                threatPoints: 0f);
            if (site == null)
                return false;

            site.customLabel = "Replicator-consumed ruin";
            try
            {
                Find.WorldObjects.Add(site);
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Failed to add Replicator first-contact site: " + ex);
                return false;
            }

            Find.LetterStack.ReceiveLetter(
                "Replicator trace located",
                "A stripped ruin has been located nearby. Reconnaissance shows unmistakable Replicator consumption patterns and loose self-organizing blocks among the remains. No active swarm is visible. The blocks are physical salvage, but leaving enough of them exposed can reconstruct new Replicator Drones.",
                LetterDefOf.ThreatSmall,
                site);
            return true;
        }
    }

    public sealed class SitePartWorker_ReplicatorConsumedRuin : SitePartWorker
    {
        private const string MatterDefName = "WNG_ReplicatorMatter";
        private const int StackCount = 3;
        private const int MinBlocksPerStack = 12;
        private const int MaxBlocksPerStack = 18;

        private static readonly IntVec3[] StackOffsets =
        {
            new IntVec3(-8, 0, -3),
            new IntVec3(7, 0, -2),
            new IntVec3(0, 0, 8)
        };

        private static readonly string[] SceneryDefNames =
        {
            "WNG_ReplicatorStrippedWallHusk",
            "WNG_ReplicatorStrippedWallHusk",
            "WNG_ReplicatorScouredMachineHusk",
            "WNG_ReplicatorScouredMachineHusk",
            "WNG_ReplicatorConsumptionScar",
            "WNG_ReplicatorConsumptionScar"
        };

        private static readonly IntVec3[] SceneryOffsets =
        {
            new IntVec3(-5, 0, -6),
            new IntVec3(6, 0, -5),
            new IntVec3(-4, 0, 4),
            new IntVec3(5, 0, 4),
            new IntVec3(-1, 0, -4),
            new IntVec3(2, 0, 2)
        };

        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            Site site = map?.Parent as Site;
            if (map == null || site == null)
                return;

            ThingDef matterDef = DefDatabase<ThingDef>.GetNamedSilentFail(MatterDefName);
            if (matterDef == null)
            {
                Log.Warning("[WNG] Replicator consumed-ruin map generated without the Replicator Blocks Def.");
                return;
            }

            // This site is aftermath, not an active infestation. Exactly three separated stacks
            // of ordinary Blocks are the threat. Their existing exposure/reassembly component,
            // containment pause and whole-block cap remain authoritative.
            for (int i = 0; i < StackCount; i++)
            {
                Thing matter = ThingMaker.MakeThing(matterDef);
                matter.stackCount = Rand.RangeInclusive(MinBlocksPerStack, MaxBlocksPerStack);
                IntVec3 preferred = map.Center + StackOffsets[i];

                if (!GenPlace.TryPlaceThing(matter, preferred, map, ThingPlaceMode.Near) && !matter.Destroyed)
                    matter.Destroy(DestroyMode.Vanish);
            }

            // Inert retained scenery makes the first-contact site read as a consumed technological
            // ruin instead of three unexplained resource piles. These Things contain no Matter,
            // comps, research, loot or incident state; failed scenery placement never affects the
            // authoritative loose-Block transaction above.
            for (int i = 0; i < SceneryDefNames.Length && i < SceneryOffsets.Length; i++)
            {
                ThingDef sceneryDef = DefDatabase<ThingDef>.GetNamedSilentFail(SceneryDefNames[i]);
                if (sceneryDef == null)
                    continue;

                Thing scenery = ThingMaker.MakeThing(sceneryDef);
                IntVec3 preferred = map.Center + SceneryOffsets[i];
                if (!GenPlace.TryPlaceThing(scenery, preferred, map, ThingPlaceMode.Near) && !scenery.Destroyed)
                    scenery.Destroy(DestroyMode.Vanish);
            }
        }
    }
}
