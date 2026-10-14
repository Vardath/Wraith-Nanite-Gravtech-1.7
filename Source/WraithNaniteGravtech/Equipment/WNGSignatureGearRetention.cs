using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Protects signature WNG spawn gear during the short period in which outfit/equipment AI
    /// (including other mods) can immediately discard freshly generated equipment.
    ///
    /// This is intentionally not a permanent forced-gear loop. After the spawn-retention window,
    /// direct player apparel/weapon changes are respected. Required apparel that a player pawn is
    /// currently wearing is marked forced through RimWorld's native outfit handler so ordinary
    /// outfit optimisation does not strip it; the player can still deliberately remove/replace it.
    /// </summary>
    public sealed class MapComponent_WNGSignatureGearRetention : MapComponent
    {
        private const int ScanIntervalTicks = 15;
        private const int SpawnRetentionTicks = 1800;
        private const int CleanupIntervalTicks = 6000;
        private const float RecoveryRadius = 4.25f;

        private Dictionary<int, int> firstSeenTickByPawnId = new Dictionary<int, int>();

        public MapComponent_WNGSignatureGearRetention(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref firstSeenTickByPawnId, "wngSignatureGearFirstSeen", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && firstSeenTickByPawnId == null)
                firstSeenTickByPawnId = new Dictionary<int, int>();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now % ScanIntervalTicks != 0)
                return;

            IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null || pawns.Count == 0)
                return;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!IsRelevantWngPawn(pawn))
                    continue;

                int id = pawn.thingIDNumber;
                if (!firstSeenTickByPawnId.TryGetValue(id, out int firstSeenTick))
                {
                    firstSeenTick = now;
                    firstSeenTickByPawnId[id] = firstSeenTick;
                }

                bool withinSpawnWindow = now - firstSeenTick <= SpawnRetentionTicks;
                ReconcileRequiredApparel(pawn, withinSpawnWindow);
                ReconcileSignatureWeapon(pawn, withinSpawnWindow);
            }

            if (now % CleanupIntervalTicks == 0)
                PruneMissingPawns(pawns);
        }

        private static bool IsRelevantWngPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map == null)
                return false;

            string kindName = pawn.kindDef?.defName;
            if (string.IsNullOrEmpty(kindName) || !kindName.StartsWith("WNG_", StringComparison.Ordinal))
                return false;

            bool hasRequiredApparel = pawn.kindDef.apparelRequired != null && pawn.kindDef.apparelRequired.Count > 0;
            bool hasWeaponTags = pawn.kindDef.weaponTags != null && pawn.kindDef.weaponTags.Count > 0;
            return hasRequiredApparel || hasWeaponTags;
        }

        private static void ReconcileRequiredApparel(Pawn pawn, bool withinSpawnWindow)
        {
            if (pawn.apparel == null)
                return;

            List<ThingDef> required = pawn.kindDef.apparelRequired;
            if (required == null)
                return;

            for (int i = 0; i < required.Count; i++)
            {
                ThingDef requiredDef = required[i];
                if (requiredDef == null)
                    continue;

                Apparel worn = FindWorn(pawn, requiredDef);
                if (worn == null && withinSpawnWindow)
                {
                    worn = FindDroppedApparel(pawn, requiredDef);
                    if (worn != null)
                    {
                        try
                        {
                            pawn.apparel.Wear(worn, true);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[WNG] Could not restore spawn apparel " + requiredDef.defName + " to " + pawn.LabelShortCap + ": " + ex.Message);
                            worn = null;
                        }
                    }
                }

                if (worn != null && pawn.Faction == Faction.OfPlayer)
                    MarkPlayerApparelForced(pawn, worn);
            }
        }

        private static Apparel FindWorn(Pawn pawn, ThingDef requiredDef)
        {
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel apparel = worn[i];
                if (apparel?.def == requiredDef)
                    return apparel;
            }
            return null;
        }

        private static Apparel FindDroppedApparel(Pawn pawn, ThingDef requiredDef)
        {
            Map map = pawn.Map;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, RecoveryRadius, true))
            {
                if (!cell.InBounds(map))
                    continue;

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Apparel apparel &&
                        apparel.def == requiredDef &&
                        apparel.Wearer == null &&
                        !apparel.Destroyed)
                        return apparel;
                }
            }
            return null;
        }

        private static void MarkPlayerApparelForced(Pawn pawn, Apparel apparel)
        {
            try
            {
                pawn.outfits?.forcedHandler?.SetForced(apparel, true);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Could not mark required apparel as forced for " + pawn.LabelShortCap + ": " + ex.Message);
            }
        }

        private static void ReconcileSignatureWeapon(Pawn pawn, bool withinSpawnWindow)
        {
            if (!withinSpawnWindow || pawn.equipment == null)
                return;

            if (pawn.equipment.Primary != null)
                return;

            List<string> requiredTags = pawn.kindDef.weaponTags;
            if (requiredTags == null || requiredTags.Count == 0)
                return;

            ThingWithComps weapon = FindDroppedSignatureWeapon(pawn, requiredTags);
            if (weapon == null)
                return;

            Map sourceMap = weapon.Map;
            IntVec3 sourceCell = weapon.Position;

            try
            {
                // A spawned Thing is still owned by the map.  Pawn_EquipmentTracker.AddEquipment
                // expects an unowned Thing, so detach the exact dropped weapon before handing it
                // to the equipment tracker.  This prevents the map/equipment double-ownership loop
                // that otherwise repeats every retention scan.
                if (weapon.Spawned)
                    weapon.DeSpawn();

                pawn.equipment.AddEquipment(weapon);
            }
            catch (Exception ex)
            {
                // Preserve the exact weapon if the handoff fails.  Never leave it orphaned after
                // a successful DeSpawn, and do not manufacture a replacement.
                string rollbackFailure = null;
                if (!weapon.Destroyed && !weapon.Spawned && weapon.holdingOwner == null && sourceMap != null)
                {
                    try
                    {
                        IntVec3 recoveryCell = sourceCell.InBounds(sourceMap) ? sourceCell : pawn.Position;
                        GenPlace.TryPlaceThing(weapon, recoveryCell, sourceMap, ThingPlaceMode.Near);
                    }
                    catch (Exception restoreEx)
                    {
                        rollbackFailure = "; map rollback also failed: " + restoreEx.Message;
                    }
                }

                Log.Warning("[WNG] Could not restore spawn weapon " + weapon.def.defName + " to " + pawn.LabelShortCap + ": " + ex.Message + (rollbackFailure ?? string.Empty));
            }
        }

        private static ThingWithComps FindDroppedSignatureWeapon(Pawn pawn, List<string> requiredTags)
        {
            Map map = pawn.Map;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, RecoveryRadius, true))
            {
                if (!cell.InBounds(map))
                    continue;

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    ThingWithComps thing = things[i] as ThingWithComps;
                    if (thing == null || thing.Destroyed || !thing.Spawned || thing.TryGetComp<CompEquippable>() == null)
                        continue;

                    List<string> candidateTags = thing.def.weaponTags;
                    if (candidateTags == null || candidateTags.Count == 0)
                        continue;

                    for (int a = 0; a < requiredTags.Count; a++)
                    {
                        for (int b = 0; b < candidateTags.Count; b++)
                        {
                            if (requiredTags[a] == candidateTags[b])
                                return thing;
                        }
                    }
                }
            }

            return null;
        }

        private void PruneMissingPawns(IReadOnlyList<Pawn> pawns)
        {
            HashSet<int> present = new HashSet<int>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null)
                    present.Add(pawn.thingIDNumber);
            }

            List<int> stale = null;
            foreach (KeyValuePair<int, int> pair in firstSeenTickByPawnId)
            {
                if (!present.Contains(pair.Key))
                {
                    if (stale == null)
                        stale = new List<int>();
                    stale.Add(pair.Key);
                }
            }

            if (stale == null)
                return;

            for (int i = 0; i < stale.Count; i++)
                firstSeenTickByPawnId.Remove(stale[i]);
        }
    }
}
