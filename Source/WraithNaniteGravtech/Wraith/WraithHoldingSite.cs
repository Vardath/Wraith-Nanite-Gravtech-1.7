using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Minimal native rescue site for exact Wraith culling captives. This worker never generates a
    /// replacement captive. It asks WraithCullingCustodyRegistry to transfer the exact stored Pawn
    /// objects onto the map, and returns unrecovered Pawns to that same custody owner before map loss.
    /// </summary>
    public sealed class SitePartWorker_WraithHoldingSite : SitePartWorker
    {
        public override bool FactionCanOwn(Faction faction)
        {
            return WraithLineageUtility.IsWraithLineage(faction);
        }

        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            Site site = map?.Parent as Site;
            WraithCullingCustodyRegistry registry = Current.Game?.GetComponent<WraithCullingCustodyRegistry>();
            if (site == null || registry == null || site.Faction == null)
                return;

            List<WraithAbducteeRecord> records = registry.RecordsForSite(site.ID);
            if (records.Count == 0)
                return;

            int index = 0;
            foreach (WraithAbducteeRecord record in records)
            {
                IntVec3 near = map.Center + new IntVec3((index % 2 == 0 ? -1 : 1) * (3 + index), 0, 2 + index / 2);
                registry.TryMaterializeForSite(record, map, near);
                index++;
            }

            Pawn keeper = SpawnDefenders(map, site.Faction, records.Count, records.Max(r => registry.CaptivityStage(r)));
            if (keeper != null)
            {
                foreach (WraithAbducteeRecord record in records)
                    registry.TryCommitNativeEnthrallmentAtSite(record, keeper);
            }
        }

        private static Pawn SpawnDefenders(Map map, Faction faction, int captiveCount, int maxCaptivityStage)
        {
            if (map == null || faction == null)
                return null;

            PawnKindDef keeper = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithKeeper");
            PawnKindDef hunter = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithHunter");
            PawnKindDef warrior = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithWarrior");
            if (keeper == null || hunter == null || warrior == null)
                return null;

            int stageGuardBonus = Math.Max(0, Math.Min(2, maxCaptivityStage - 2));
            int count = Math.Max(3, Math.Min(10, captiveCount + 2 + stageGuardBonus));
            Lord lord = LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, map.Center, 60000), map);
            Pawn keeperPawn = null;
            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = i == 0 ? keeper : (i % 2 == 0 ? warrior : hunter);
                Pawn defender = PawnGenerator.GeneratePawn(kind, faction);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 16);
                GenSpawn.Spawn(defender, cell, map);
                lord.AddPawn(defender);
                if (i == 0)
                    keeperPawn = defender;
            }
            return keeperPawn;
        }

        public override void SitePartWorkerTick(SitePart sitePart)
        {
            base.SitePartWorkerTick(sitePart);
            if (sitePart?.site == null || !sitePart.site.HasMap || !sitePart.site.IsHashIntervalTick(250))
                return;

            Map map = sitePart.site.Map;
            if (GenHostility.AnyHostileActiveThreatToPlayer(map, countDormantPawnsAsHostile: true))
                return;

            WraithCullingCustodyRegistry registry = Current.Game?.GetComponent<WraithCullingCustodyRegistry>();
            if (registry == null)
                return;

            List<Pawn> released = registry.ReleaseSiteCaptives(sitePart.site.ID);
            if (released.Count > 0)
            {
                Find.LetterStack.ReceiveLetter(
                    "Wraith captives freed",
                    released.Select(p => p.LabelShort).ToCommaList(useAnd: true) + " are free from Wraith custody. Bring them into a player caravan or home map to complete the rescue.",
                    LetterDefOf.PositiveEvent,
                    new LookTargets(released));
            }
        }

        public override void Notify_SiteMapAboutToBeRemoved(SitePart sitePart)
        {
            if (sitePart?.site != null && sitePart.site.HasMap && Current.Game != null)
            {
                WraithCullingCustodyRegistry registry = Current.Game.GetComponent<WraithCullingCustodyRegistry>();
                registry?.ReclaimUnrecoveredSiteCaptives(sitePart.site.ID, sitePart.site.Map);
            }
            base.Notify_SiteMapAboutToBeRemoved(sitePart);
        }

        public override void PostDestroy(SitePart sitePart)
        {
            if (sitePart?.site != null && Current.Game != null)
            {
                WraithCullingCustodyRegistry registry = Current.Game.GetComponent<WraithCullingCustodyRegistry>();
                registry?.NotifyRescueSiteDestroyed(sitePart.site.ID);
            }
            base.PostDestroy(sitePart);
        }
    }
}
