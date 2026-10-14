using System;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorMatterReassembly : CompProperties
    {
        public int minimumStack = 10;
        public int consumePerDrone = 10;
        public int exposedTicksRequired = 30000;

        public CompProperties_ReplicatorMatterReassembly()
        {
            compClass = typeof(CompReplicatorMatterReassembly);
        }
    }

    /// <summary>
    /// Physical loose-block hazard. Exposure time is stack state and is save-persistent. Powered
    /// containment pauses exposure rather than erasing it. Once sufficiently exposed, a qualifying
    /// stack stages one real hostile Drone and consumes exactly the configured number of Blocks only
    /// after that Drone has been placed successfully.
    /// </summary>
    public sealed class CompReplicatorMatterReassembly : ThingComp
    {
        private const int RareTickInterval = 250;
        private const string DroneKindDefName = "WNG_ReplicatorDrone";
        private const string SwarmFactionDefName = "WNG_ReplicatorSwarm";

        private int exposedTicks;
        private int nextCapRetryTick;

        private CompProperties_ReplicatorMatterReassembly Props =>
            (CompProperties_ReplicatorMatterReassembly)props;

        public override void PostSplitOff(Thing piece)
        {
            base.PostSplitOff(piece);
            CompReplicatorMatterReassembly other = piece?.TryGetComp<CompReplicatorMatterReassembly>();
            if (other != null)
                other.exposedTicks = exposedTicks;
        }

        public override void PreAbsorbStack(Thing otherStack, int count)
        {
            base.PreAbsorbStack(otherStack, count);
            CompReplicatorMatterReassembly other = otherStack?.TryGetComp<CompReplicatorMatterReassembly>();
            if (other != null)
            {
                // A mature/active Block mixed into a fresher stack must not be made dormant again by
                // stockpile merging. Stack-level state therefore keeps the oldest exposure age.
                exposedTicks = Math.Max(exposedTicks, other.exposedTicks);
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (parent?.Spawned != true || parent.Map == null)
                return;

            if (ReplicatorContainmentUtility.IsContained(parent.Map, parent.Position))
                return;

            if (exposedTicks > int.MaxValue - RareTickInterval)
                exposedTicks = int.MaxValue;
            else
                exposedTicks += RareTickInterval;

            if (exposedTicks < Math.Max(0, Props.exposedTicksRequired) ||
                parent.stackCount < Math.Max(1, Props.minimumStack))
                return;

            TryReassembleOneDrone();
        }

        private void TryReassembleOneDrone()
        {
            Map map = parent?.Map;
            if (map == null || parent.Destroyed || !parent.Spawned)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now >= 0 && now < nextCapRetryTick)
                return;
            int cost = Math.Max(1, Props.consumePerDrone);
            if (parent.stackCount < cost || ReplicatorContainmentUtility.IsContained(map, parent.Position))
                return;

            PawnKindDef droneKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(DroneKindDefName);
            FactionDef swarmDef = DefDatabase<FactionDef>.GetNamedSilentFail(SwarmFactionDefName);
            Faction swarmFaction = swarmDef == null ? null : Find.FactionManager?.FirstFactionOfDef(swarmDef);
            if (droneKind == null || swarmFaction == null)
                return;

            ReplicatorBlockExtension ext = droneKind.race?.GetModExtension<ReplicatorBlockExtension>();
            if (ext != null && ReplicatorAssimilationUtility.CountHostileBlocks(map) + 1 > ReplicatorAssimilationUtility.HostilePopulationCap(ext))
            {
                // The map-wide pawn scan is unnecessary every 250 ticks for
                // each mature stack while population is already at capacity.
                nextCapRetryTick = now + 2500;
                return;
            }

            Pawn drone = null;
            try
            {
                drone = PawnGenerator.GeneratePawn(droneKind, swarmFaction);
                if (!GenPlace.TryPlaceThing(
                        drone,
                        parent.Position,
                        map,
                        ThingPlaceMode.Near,
                        null,
                        cell => !ReplicatorContainmentUtility.IsContained(map, cell)))
                {
                    if (!drone.Destroyed)
                        drone.Destroy(DestroyMode.Vanish);
                    return;
                }

                // Placement is the first half of the transaction. Consume physical Blocks only after
                // the exact Drone exists on-map. If exact-stack destruction fails before commit, roll
                // the staged Drone back; if destruction already completed, conservation keeps it.
                if (parent.stackCount == cost)
                {
                    try
                    {
                        parent.Destroy(DestroyMode.Vanish);
                    }
                    catch (Exception ex)
                    {
                        if (!parent.Destroyed)
                        {
                            if (!drone.Destroyed)
                                drone.Destroy(DestroyMode.Vanish);
                            Log.Error("[WNG] Replicator Block reassembly failed before stack consumption: " + ex);
                            return;
                        }

                        Log.Error("[WNG] Replicator Block stack reported an exception after consumption; keeping committed Drone: " + ex);
                    }
                }
                else
                {
                    parent.stackCount -= cost;
                }

                // Reassembly sound is strictly post-commit presentation.
                PlaySoundFailSoft("WNG_ReplicatorAssembly", drone);
            }
            catch (Exception ex)
            {
                if (drone != null && !drone.Destroyed)
                    drone.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Replicator Block reassembly failed; staged Drone rolled back when possible: " + ex);
            }
        }


        private static void PlaySoundFailSoft(string defName, Thing target)
        {
            try
            {
                if (target?.Spawned == true && target.Map != null)
                    DefDatabase<SoundDef>.GetNamedSilentFail(defName)?.PlayOneShot(new TargetInfo(target.Position, target.Map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Replicator presentation sound failed: " + ex.Message);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref exposedTicks, "wngReplicatorMatterExposedTicks", 0);
            Scribe_Values.Look(ref nextCapRetryTick, "wngReplicatorMatterNextCapRetryTick", 0);
        }

        public override string CompInspectStringExtra()
        {
            if (parent?.Spawned == true && parent.Map != null &&
                ReplicatorContainmentUtility.IsContained(parent.Map, parent.Position))
                return "Replicator Blocks: contained (reassembly exposure paused)";

            int required = Math.Max(0, Props.exposedTicksRequired);
            int shown = Math.Min(exposedTicks, required);
            return "Replicator Block exposure: " + shown + " / " + required + " ticks";
        }
    }
}
