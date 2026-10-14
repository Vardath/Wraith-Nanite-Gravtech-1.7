using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Gives a newly generated player start a small chance to begin beside an intact CatCraft
    /// Stargate and matching DHD. WNG only places CatCraft-owned Defs and does not own addresses,
    /// network registration, dialing, receive buffers or gate state.
    ///
    /// Existing saves are explicitly excluded by the early-game tick window, and the resolved flag
    /// is saved so the roll can never repeat for the same game.
    /// </summary>
    public sealed class GameComponent_WNGStartingStargate : GameComponent
    {
        private const float StartingStargateChance = 0.15f;
        private const int LatestStartResolutionTick = 2500;
        private const int SearchRadius = 40;

        private const string StargateDefName = "StargateMod_Stargate";
        private const string DhdDefName = "StargateMod_DialHomeDevice";
        private const string StargateThingClassName = "StargatesMod.Building_Stargate";

        private bool resolved;

        public GameComponent_WNGStartingStargate(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref resolved, "wngStartingStargateResolved", false);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (resolved || Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now > LatestStartResolutionTick)
            {
                // Do not retrofit old saves with a free gate.
                resolved = true;
                return;
            }

            Map map = Find.Maps?
                .FirstOrDefault(m =>
                    m != null &&
                    m.IsPlayerHome &&
                    m.mapPawns != null &&
                    m.mapPawns.FreeColonistsSpawnedCount > 0);

            if (map == null)
                return;

            // The first valid home map is the one and only roll for this game.
            resolved = true;

            ThingDef gateDef =
                DefDatabase<ThingDef>.GetNamedSilentFail(StargateDefName);
            ThingDef dhdDef =
                DefDatabase<ThingDef>.GetNamedSilentFail(DhdDefName);

            // CatCraft Stargates! is optional. If it is absent, do nothing.
            if (gateDef == null || dhdDef == null)
                return;

            if (MapAlreadyHasStargate(map) || !Rand.Chance(StartingStargateChance))
                return;

            if (!TryFindSite(map, gateDef, dhdDef, out IntVec3 gateCell, out IntVec3 dhdCell))
                return;

            Thing gate = null;
            Thing dhd = null;
            try
            {
                gate = GenSpawn.Spawn(
                    gateDef,
                    gateCell,
                    map,
                    Rot4.South,
                    WipeMode.VanishOrMoveAside);

                dhd = GenSpawn.Spawn(
                    dhdDef,
                    dhdCell,
                    map,
                    Rot4.South,
                    WipeMode.VanishOrMoveAside);

                Find.LetterStack.ReceiveLetter(
                    "Ancient Stargate",
                    "An intact Stargate and its Dial Home Device were already present on this site when the colony arrived. " +
                    "The gate remains part of the native Stargates! network; WNG has not altered its address or dialing rules.",
                    LetterDefOf.NeutralEvent,
                    gate);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Starting Stargate placement failed safely: " + ex.Message);

                if (dhd != null && !dhd.Destroyed)
                    dhd.Destroy(DestroyMode.Vanish);
                if (gate != null && !gate.Destroyed)
                    gate.Destroy(DestroyMode.Vanish);
            }
        }

        private static bool MapAlreadyHasStargate(Map map)
        {
            if (map?.listerThings?.AllThings == null)
                return false;

            return map.listerThings.AllThings.Any(t =>
                t != null &&
                !t.Destroyed &&
                string.Equals(
                    t.def?.thingClass?.FullName,
                    StargateThingClassName,
                    StringComparison.Ordinal));
        }

        private static bool TryFindSite(
            Map map,
            ThingDef gateDef,
            ThingDef dhdDef,
            out IntVec3 gateCell,
            out IntVec3 dhdCell)
        {
            gateCell = IntVec3.Invalid;
            dhdCell = IntVec3.Invalid;

            bool Validator(IntVec3 cell)
            {
                if (!cell.InBounds(map) ||
                    cell.Fogged(map) ||
                    cell.Roofed(map) ||
                    !GenSpawn.CanSpawnAt(
                        gateDef,
                        cell,
                        map,
                        Rot4.South,
                        canWipeEdifices: false))
                {
                    return false;
                }

                IntVec3 proposedDhd = cell + IntVec3.South * 3;
                if (!proposedDhd.InBounds(map) ||
                    proposedDhd.Fogged(map) ||
                    proposedDhd.Roofed(map) ||
                    !GenSpawn.CanSpawnAt(
                        dhdDef,
                        proposedDhd,
                        map,
                        Rot4.South,
                        canWipeEdifices: false))
                {
                    return false;
                }

                // Keep the discovered gate close enough to feel like part of the starting site,
                // but not directly under the initial colonist/drop-pod cluster.
                return cell.DistanceToSquared(map.Center) >= 64;
            }

            if (!CellFinder.TryFindRandomCellNear(
                    map.Center,
                    map,
                    SearchRadius,
                    Validator,
                    out gateCell,
                    400))
            {
                return false;
            }

            dhdCell = gateCell + IntVec3.South * 3;
            return true;
        }
    }
}
