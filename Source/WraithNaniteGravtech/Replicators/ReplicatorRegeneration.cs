using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorRegeneration : CompProperties
    {
        public int intervalTicks = 600;
        public float healAmount = 0.20f;

        public CompProperties_ReplicatorRegeneration()
        {
            compClass = typeof(CompReplicatorRegeneration);
        }
    }

    /// <summary>
    /// Block Replicator self-repair only. This is deliberately distinct from the future Repairer
    /// specialist's ally-healing role. EMP interference uses the shared native-EMP-backed WNG
    /// disruption utility; there is no second regeneration-specific EMP timer.
    ///
    /// Learned Power adaptation modifies this same component through the separate adaptation-effects
    /// seam; it does not create a parallel repair system.
    /// </summary>
    public sealed class CompReplicatorRegeneration : ThingComp
    {
        private int nextHealTick;

        private CompProperties_ReplicatorRegeneration Props =>
            (CompProperties_ReplicatorRegeneration)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad && nextHealTick <= 0)
                ScheduleNextHeal();
        }

        public override void CompTick()
        {
            base.CompTick();

            Pawn pawn = parent as Pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextHealTick <= 0)
                ScheduleNextHeal();

            if (now < nextHealTick)
                return;

            // Schedule before attempting a heal so EMP or lack of injury never banks/catches up
            // multiple repair pulses later.
            ScheduleNextHeal();

            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn))
                return;

            Hediff_Injury injury = pawn.health?.hediffSet?.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h != null && h.Severity > 0f && !h.IsPermanent())
                .OrderByDescending(h => h.Severity)
                .FirstOrDefault();

            if (injury == null)
                return;

            float healFactor = pawn.TryGetComp<CompReplicatorAdaptationEffects>()?.RegenerationHealFactor ?? 1f;
            float amount = Math.Max(0.01f, Props.healAmount * healFactor);
            injury.Heal(amount);
        }

        private void ScheduleNextHeal()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            float intervalFactor = parent.TryGetComp<CompReplicatorAdaptationEffects>()?.RegenerationIntervalFactor ?? 1f;
            int interval = Math.Max(60, (int)Math.Round(Props.intervalTicks * intervalFactor));
            nextHealTick = now + interval;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextHealTick, "wngReplicatorNextHealTick", 0);
        }
    }
}
