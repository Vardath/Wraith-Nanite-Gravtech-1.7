using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AsuranArchiveRepair : CompProperties
    {
        public int repairIntervalTicks = 900;
        public int healPerPulse = 3;
        public float archiveRadius = 45f;

        public CompProperties_AsuranArchiveRepair()
        {
            compClass = typeof(CompAsuranArchiveRepair);
        }
    }

    /// <summary>
    /// Retained Asuran structural continuity behavior, re-authored for the clean rebuild.
    /// A damaged Asuran/Precursor structure repairs only while a powered same-faction
    /// Pattern Archive is physically present within the configured radius. The Archive
    /// may satisfy its own support requirement while powered.
    /// </summary>
    public sealed class CompAsuranArchiveRepair : ThingComp
    {
        private const string PatternArchiveDefName = "WNG_AsuranPatternArchive";
        private int nextRepairTick = -1;

        private CompProperties_AsuranArchiveRepair Props =>
            (CompProperties_AsuranArchiveRepair)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (nextRepairTick < 0)
                ScheduleNextPulse();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextRepairTick, "wngAsuranArchiveRepairNextTick", -1);
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
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null ||
                parent.Faction == null || parent.HitPoints >= parent.MaxHitPoints)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextRepairTick < 0)
            {
                ScheduleNextPulse();
                return;
            }

            if (now < nextRepairTick)
                return;

            ScheduleNextPulse();

            if (!HasPoweredSameFactionArchive(parent.Map, parent.Faction, parent.Position, Props.archiveRadius))
                return;

            int amount = Math.Max(1, Props.healPerPulse);
            parent.HitPoints = Math.Min(parent.MaxHitPoints, parent.HitPoints + amount);
        }

        private void ScheduleNextPulse()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            nextRepairTick = now + Math.Max(1, Props.repairIntervalTicks);
        }

        private static bool HasPoweredSameFactionArchive(
            Map map,
            Faction faction,
            IntVec3 origin,
            float radius)
        {
            if (map == null || faction == null)
                return false;

            ThingDef archiveDef = DefDatabase<ThingDef>.GetNamedSilentFail(PatternArchiveDefName);
            if (archiveDef == null)
                return false;

            float radiusSquared = Math.Max(0f, radius) * Math.Max(0f, radius);
            foreach (Thing archive in map.listerThings.ThingsOfDef(archiveDef))
            {
                if (archive == null || archive.Destroyed || !archive.Spawned ||
                    archive.Faction != faction ||
                    archive.Position.DistanceToSquared(origin) > radiusSquared)
                    continue;

                CompPowerTrader power = archive.TryGetComp<CompPowerTrader>();
                if (power?.PowerOn == true)
                    return true;
            }

            return false;
        }
    }
}
