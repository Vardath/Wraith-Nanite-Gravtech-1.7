using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Native-world-site glue for Mature Wraith Hives. This class deliberately owns only
    /// world/site discovery and placement of the existing metabolic Heart. The Heart's
    /// CompWraithMatureHive remains the single owner of local population, feeding stock,
    /// hibernation, biomass, replacement and dormancy-vault state.
    /// </summary>
    public static class WraithMatureHiveSiteUtility
    {
        public const string SitePartDefName = "WNG_WraithMatureHive";

        public static bool IsMatureHiveSite(Site site, SitePartDef matureHiveDef = null)
        {
            if (site == null || site.parts == null)
                return false;

            SitePartDef expected = matureHiveDef ?? DefDatabase<SitePartDef>.GetNamedSilentFail(SitePartDefName);
            if (expected == null)
                return false;

            return site.parts.Any(p => p != null && p.def == expected);
        }

        public static bool HasLiveMatureHive(Faction faction, SitePartDef matureHiveDef = null)
        {
            if (faction == null || Find.WorldObjects == null)
                return false;

            return Find.WorldObjects.AllWorldObjects
                .OfType<Site>()
                .Any(site => site != null && site.Faction == faction && IsMatureHiveSite(site, matureHiveDef));
        }

        public static List<Faction> EligibleHostileLineages(SitePartDef matureHiveDef)
        {
            if (matureHiveDef == null || Faction.OfPlayer == null)
                return new List<Faction>();

            return WraithLineageUtility.ActiveLineages()
                .Where(f => f != null
                    && f != Faction.OfPlayer
                    && !f.defeated
                    && f.HostileTo(Faction.OfPlayer)
                    && matureHiveDef.FactionCanOwn(f)
                    && !HasLiveMatureHive(f, matureHiveDef))
                .ToList();
        }
    }

    public sealed class SitePartWorker_WraithMatureHive : SitePartWorker
    {
        private const int DefeatCheckIntervalTicks = 250;

        public override bool FactionCanOwn(Faction faction)
        {
            return faction != null
                && faction != Faction.OfPlayer
                && !faction.defeated
                && WraithLineageUtility.IsWraithLineage(faction);
        }

        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            Site site = map?.Parent as Site;
            Faction faction = site?.Faction;
            if (map == null || site == null || !FactionCanOwn(faction))
                return;

            ThingDef heartDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_HiveMetabolicHeart");
            if (heartDef == null)
                return;

            Building existing = WraithHiveEcologyUtility.FindBuilding(map, faction, heartDef, map.Center, 9999f);
            if (existing != null)
                return;

            Building heart = WraithHiveEcologyUtility.SpawnBuildingNear(map, heartDef, map.Center, faction, 18);
            if (heart == null)
                Log.Warning("WNG: failed to place Mature-Hive metabolic Heart for site " + site.ID + ".");
        }

        public override void SitePartWorkerTick(SitePart sitePart)
        {
            base.SitePartWorkerTick(sitePart);
            Site site = sitePart?.site;
            if (site == null || !site.HasMap || site.Map == null || site.Faction == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if ((now + site.ID) % DefeatCheckIntervalTicks != 0)
                return;

            // A lineage that has made peace before the Hive is physically broken does not
            // receive a forced retaliation. Native diplomacy remains authoritative.
            if (Faction.OfPlayer == null || !site.Faction.HostileTo(Faction.OfPlayer))
                return;

            if (GenHostility.AnyHostileActiveThreatToPlayer(site.Map, countDormantPawnsAsHostile: true))
                return;

            WraithMatureHiveRetaliationRegistry registry = Current.Game?.GetComponent<WraithMatureHiveRetaliationRegistry>();
            registry?.ScheduleHiveRetaliation(site.ID, site.Faction);
        }
    }

    public sealed class IncidentWorker_WraithMatureHiveDiscovery : IncidentWorker
    {
        private const int MinSiteDistance = 8;
        private const int MaxSiteDistance = 24;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !map.IsPlayerHome)
                return false;

            SitePartDef hiveDef = DefDatabase<SitePartDef>.GetNamedSilentFail(WraithMatureHiveSiteUtility.SitePartDefName);
            return hiveDef != null && WraithMatureHiveSiteUtility.EligibleHostileLineages(hiveDef).Count > 0;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !map.IsPlayerHome)
                return false;

            SitePartDef hiveDef = DefDatabase<SitePartDef>.GetNamedSilentFail(WraithMatureHiveSiteUtility.SitePartDefName);
            if (hiveDef == null)
                return false;

            List<Faction> candidates = WraithMatureHiveSiteUtility.EligibleHostileLineages(hiveDef);
            if (candidates.Count == 0)
                return false;

            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, MinSiteDistance, MaxSiteDistance, allowCaravans: false))
                return false;

            Faction lineage = candidates.RandomElement();
            float threatPoints = parms.points > 0f
                ? parms.points
                : StorytellerUtility.DefaultSiteThreatPointsNow();

            // false is deliberate: Cinder/Veiled/Pale must remain diplomatically mutable.
            // Sable's permanent hostility remains owned by its FactionDef.
            Site site = SiteMaker.MakeSite(
                hiveDef,
                tile,
                lineage,
                ifHostileThenMustRemainHostile: false,
                threatPoints: Mathf.Max(500f, threatPoints));
            if (site == null)
                return false;

            site.customLabel = lineage.Name + " mature Hive";
            Find.WorldObjects.Add(site);

            Find.LetterStack.ReceiveLetter(
                "Mature Wraith Hive located",
                "Long-range traces have exposed a mature Wraith Hive belonging to " + lineage.Name + ". " +
                "Its local feeding population, hibernating Wraith, captive feeding stock, biomass reserve and bounded replacement cycle are physical systems on the site rather than strategic feeding-request state.",
                LetterDefOf.ThreatBig,
                site);
            return true;
        }
    }
}
