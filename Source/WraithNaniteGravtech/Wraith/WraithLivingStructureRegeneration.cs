using System;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WraithLivingStructureRegeneration : CompProperties
    {
        public int repairIntervalTicks = 500;
        public int hitPointsPerPulse = 1;

        public CompProperties_WraithLivingStructureRegeneration()
        {
            compClass = typeof(CompWraithLivingStructureRegeneration);
        }
    }

    /// <summary>
    /// Slow intrinsic regeneration for terrestrial Wraith living structure tissue.
    /// This is deliberately separate from gravship repair: ship-scale Wraith repair
    /// remains owned by the biomass-fed gravship metabolic-heart system.
    /// </summary>
    public sealed class CompWraithLivingStructureRegeneration : ThingComp
    {
        private int nextRepairTick = -1;

        public CompProperties_WraithLivingStructureRegeneration Props =>
            (CompProperties_WraithLivingStructureRegeneration)props;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextRepairTick, "nextRepairTick", -1);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (nextRepairTick < 0)
            {
                ScheduleNextPulse();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            TryRepair();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            TryRepair();
        }

        private void TryRepair()
        {
            if (parent == null || parent.Destroyed || !parent.Spawned)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextRepairTick < 0)
            {
                ScheduleNextPulse();
                return;
            }

            if (now < nextRepairTick)
            {
                return;
            }

            ScheduleNextPulse();

            if (parent.HitPoints >= parent.MaxHitPoints)
            {
                return;
            }

            int amount = Math.Max(1, Props.hitPointsPerPulse);
            parent.HitPoints = Math.Min(parent.MaxHitPoints, parent.HitPoints + amount);
        }

        private void ScheduleNextPulse()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            nextRepairTick = now + Math.Max(1, Props.repairIntervalTicks);
        }
    }
}
