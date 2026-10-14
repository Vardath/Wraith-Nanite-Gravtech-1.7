using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_VacuumEnergyPower : CompProperties_Power
    {
        public float generatedPower = 15000f;
        public CompProperties_VacuumEnergyPower() { compClass = typeof(CompVacuumEnergyPower); }
    }

    // The physical CompRefuelable owns module lifetime. Output exists only while a real
    // vacuum-energy module is loaded and the tap is deliberately switched on.
    public sealed class CompVacuumEnergyPower : CompPowerTrader
    {
        public new CompProperties_VacuumEnergyPower Props => (CompProperties_VacuumEnergyPower)props;

        public override void CompTick()
        {
            base.CompTick();
            UpdateOutput();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            UpdateOutput();
        }

        private void UpdateOutput()
        {
            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            CompFlickable flick = parent.TryGetComp<CompFlickable>();
            bool enabled = flick == null || flick.SwitchIsOn;
            PowerOutput = enabled && fuel != null && fuel.HasFuel ? Math.Max(0f, Props.generatedPower) : 0f;
        }

        public override string CompInspectStringExtra()
        {
            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            CompFlickable flick = parent.TryGetComp<CompFlickable>();
            if (flick != null && !flick.SwitchIsOn)
                return "Vacuum-energy tap: isolated.";
            if (fuel == null || !fuel.HasFuel)
                return "Vacuum-energy tap: dormant — no containment module loaded.";
            return $"Vacuum-energy tap: stable\nNet output: {Math.Max(0f, Props.generatedPower):N0} W";
        }
    }
}
