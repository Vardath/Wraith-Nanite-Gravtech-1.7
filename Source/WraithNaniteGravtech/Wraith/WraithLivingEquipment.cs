using System;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WraithLivingEquipmentMaturation : CompProperties
    {
        public CompProperties_WraithLivingEquipmentMaturation()
        {
            compClass = typeof(CompWraithLivingEquipmentMaturation);
        }
    }

    /// <summary>
    /// Tracks the simple biological maturation of newly grown Wraith equipment.
    /// This is deliberately maintenance-free: no light, biomass, Life Force,
    /// deterioration-state or bonus-feeding checks belong here. Combat Extended ammunition,
    /// when present, is a separate optional compatibility concern.
    /// </summary>
    public sealed class CompWraithLivingEquipmentMaturation : ThingComp
    {
        private int growthStartTick = -1;

        public bool Mature
        {
            get
            {
                EnsureGrowthStartTick();
                return Find.TickManager == null || Find.TickManager.TicksGame - growthStartTick >= WNGSettingsUtility.WraithLivingEquipmentMaturationTicks;
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureGrowthStartTick();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref growthStartTick, "wngLivingEquipmentGrowthStartTick", -1);
        }

        public override string CompInspectStringExtra()
        {
            EnsureGrowthStartTick();
            if (Mature)
                return "Living tissue: mature";

            int now = Find.TickManager?.TicksGame ?? growthStartTick;
            int remaining = Math.Max(0, WNGSettingsUtility.WraithLivingEquipmentMaturationTicks - (now - growthStartTick));
            float hours = remaining / 2500f;
            return "Living tissue: immature (" + hours.ToString("0.0") + " h to maturity)";
        }

        private void EnsureGrowthStartTick()
        {
            if (growthStartTick >= 0)
                return;

            growthStartTick = Find.TickManager?.TicksGame ?? 0;
        }
    }
}
