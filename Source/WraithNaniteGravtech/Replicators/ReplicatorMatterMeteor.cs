using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Early-warning Replicator custody event. It never spawns finished Replicator pawns.
    /// Several separated meteor impacts deliver ordinary WNG_ReplicatorMatter, after which the
    /// existing 30,000-tick dormancy, containment, reassembly and hostile-cap rules are authoritative.
    /// </summary>
    public sealed class IncidentWorker_ReplicatorMatterMeteorShower : IncidentWorker
    {
        private const int MinimumClusters = 3;
        private const int MaximumClusters = 4;
        private const int MinimumMatterPerCluster = 12;
        private const int MaximumMatterPerCluster = 18;
        private const int MaximumExistingMatter = 60;
        private const int EdgeMargin = 7;
        private const int SearchAttempts = 120;
        private const int MinimumSeparationSquared = 64;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                map == null ||
                !map.IsPlayerHome ||
                ReplicatorAssimilationUtility.CountHostileBlocks(map) > 0)
                return false;

            ThingDef matterDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_ReplicatorMatter");
            ThingDef meteorDef = DefDatabase<ThingDef>.GetNamedSilentFail("MeteoriteIncoming");
            if (matterDef == null || meteorDef == null)
                return false;

            if (ExistingMatterCount(map, matterDef) >= MaximumExistingMatter)
                return false;

            return base.CanFireNowSub(parms) &&
                   TryFindImpactCell(map, null, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                map == null ||
                !map.IsPlayerHome ||
                ReplicatorAssimilationUtility.CountHostileBlocks(map) > 0)
                return false;

            ThingDef matterDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_ReplicatorMatter");
            ThingDef meteorDef = DefDatabase<ThingDef>.GetNamedSilentFail("MeteoriteIncoming");
            if (matterDef == null || meteorDef == null ||
                ExistingMatterCount(map, matterDef) >= MaximumExistingMatter)
                return false;

            int clusterCount = Rand.RangeInclusive(MinimumClusters, MaximumClusters);
            List<IntVec3> cells = new List<IntVec3>(clusterCount);
            for (int i = 0; i < clusterCount; i++)
            {
                if (!TryFindImpactCell(map, cells, out IntVec3 cell))
                    return false;
                cells.Add(cell);
            }

            List<Thing> stagedMatter = new List<Thing>(cells.Count);
            int crisisBonus = Math.Max(
                0,
                ReplicatorCrisisPressureUtility.MatterSeedBonus);
            int baseBonusPerCluster =
                cells.Count <= 0 ? 0 : crisisBonus / cells.Count;
            int bonusRemainder =
                cells.Count <= 0 ? 0 : crisisBonus % cells.Count;

            try
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    Thing matter = ThingMaker.MakeThing(matterDef);
                    if (matter == null)
                        throw new InvalidOperationException("Could not create Replicator Matter payload.");

                    int bonus =
                        baseBonusPerCluster +
                        (i < bonusRemainder ? 1 : 0);
                    int requested =
                        Rand.RangeInclusive(
                            MinimumMatterPerCluster,
                            MaximumMatterPerCluster) +
                        bonus;

                    matter.stackCount = Math.Min(
                        matterDef.stackLimit,
                        requested);
                    stagedMatter.Add(matter);
                }
            }
            catch (Exception ex)
            {
                foreach (Thing thing in stagedMatter)
                {
                    if (thing != null && !thing.Destroyed && thing.ParentHolder == null)
                        thing.Destroy(DestroyMode.Vanish);
                }
                Log.Warning("[WNG] Replicator Matter meteor event aborted before any physical impact was committed: " + ex.Message);
                return false;
            }

            int committedClusters = 0;
            int committedMatter = 0;
            for (int i = 0; i < stagedMatter.Count; i++)
            {
                Thing matter = stagedMatter[i];
                try
                {
                    SkyfallerMaker.SpawnSkyfaller(meteorDef, matter, cells[i], map);
                    committedClusters++;
                    committedMatter += matter.stackCount;
                }
                catch (Exception ex)
                {
                    if (matter != null && !matter.Destroyed && matter.ParentHolder == null)
                        matter.Destroy(DestroyMode.Vanish);

                    for (int j = i + 1; j < stagedMatter.Count; j++)
                    {
                        Thing uncommitted = stagedMatter[j];
                        if (uncommitted != null && !uncommitted.Destroyed && uncommitted.ParentHolder == null)
                            uncommitted.Destroy(DestroyMode.Vanish);
                    }

                    if (committedClusters <= 0)
                    {
                        Log.Warning("[WNG] Replicator Matter meteor event failed before any physical impact was committed: " + ex.Message);
                        return false;
                    }

                    Log.Warning("[WNG] Replicator Matter meteor event partially committed; preserving the already-physical impacts and suppressing storyteller retry: " + ex.Message);
                    break;
                }
            }

            TryPlayArrivalSound(cells[0], map);
            TrySendLetter(
                map,
                cells[0],
                committedMatter,
                committedClusters,
                crisisBonus);
            return committedClusters > 0;
        }

        private static int ExistingMatterCount(Map map, ThingDef matterDef)
        {
            if (map == null || matterDef == null)
                return 0;

            return map.listerThings.ThingsOfDef(matterDef)
                .Where(t => t != null && !t.Destroyed && t.Spawned)
                .Sum(t => Math.Max(0, t.stackCount));
        }

        private static bool TryFindImpactCell(
            Map map,
            List<IntVec3> existing,
            out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (map == null ||
                map.Size.x <= EdgeMargin * 2 + 2 ||
                map.Size.z <= EdgeMargin * 2 + 2)
                return false;

            for (int attempt = 0; attempt < SearchAttempts; attempt++)
            {
                IntVec3 cell = new IntVec3(
                    Rand.RangeInclusive(EdgeMargin, map.Size.x - EdgeMargin - 1),
                    0,
                    Rand.RangeInclusive(EdgeMargin, map.Size.z - EdgeMargin - 1));

                if (!cell.InBounds(map) ||
                    !cell.Standable(map) ||
                    cell.Roofed(map) ||
                    cell.GetFirstBuilding(map) != null)
                    continue;

                if (existing != null &&
                    existing.Any(other =>
                        other.DistanceToSquared(cell) < MinimumSeparationSquared))
                    continue;

                result = cell;
                return true;
            }

            return false;
        }

        private static void TryPlayArrivalSound(IntVec3 cell, Map map)
        {
            try
            {
                SoundDef sound =
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_ReplicatorMatterFall");
                sound?.PlayOneShot(new TargetInfo(cell, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Replicator Matter meteor sound failed after event commit: " + ex.Message);
            }
        }

        private void TrySendLetter(
            Map map,
            IntVec3 cell,
            int totalMatter,
            int clusterCount,
            int crisisBonus)
        {
            try
            {
                string label = def?.letterLabel ?? "Replicator Matter fall";
                string baseText = def?.letterText ??
                    "Several anomalous machine-matter objects are falling toward the colony.";
                string text = baseText +
                    "\n\nApproximately " + totalMatter +
                    " Replicator Blocks are arriving in " + clusterCount +
                    " separated impacts. They are ordinary WNG Replicator Matter, initially dormant. " +
                    "If left exposed outside powered nanite containment for roughly 30,000 ticks, " +
                    "qualifying stacks can begin reconstructing hostile Drones under the existing matter-first rules." +
                    (crisisBonus > 0
                        ? "\n\nRegional Replicator crisis pressure added " + crisisBonus +
                          " extra Blocks to this independent seed. No active swarm received free reinforcements."
                        : string.Empty);

                Find.LetterStack.ReceiveLetter(
                    label,
                    text,
                    def?.letterDef ?? LetterDefOf.ThreatSmall,
                    new TargetInfo(cell, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Replicator Matter meteor letter failed after event commit: " + ex.Message);
            }
        }
    }
}
