using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Presents WNG's isolated family power grid to native Odyssey building comps that require a
    /// CompPowerTrader (oxygen pusher, vac barrier and orbital scanner) without joining the normal
    /// colony electrical grid. The actual supply/demand accounting remains in WNGFamilyPowerUtility.
    /// </summary>
    public sealed class CompProperties_WNGFamilyPowerProxy : CompProperties_Power
    {
        public WNGGravshipFamily family = WNGGravshipFamily.None;

        public CompProperties_WNGFamilyPowerProxy()
        {
            compClass = typeof(CompPowerTrader_WNGFamilyProxy);
        }
    }

    public sealed class CompPowerTrader_WNGFamilyProxy : CompPowerTrader
    {
        private CompProperties_WNGFamilyPowerProxy WNGProps =>
            (CompProperties_WNGFamilyPowerProxy)props;

        private bool FamilyOperational =>
            parent != null &&
            parent.Spawned &&
            parent.Map != null &&
            WNGProps.family != WNGGravshipFamily.None &&
            WNGFamilyPowerUtility.IsPowered(parent, WNGProps.family);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RefreshFromFamilyGrid();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent != null && parent.Spawned && parent.IsHashIntervalTick(30))
                RefreshFromFamilyGrid();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            RefreshFromFamilyGrid();
        }

        public override void PostSwapMap()
        {
            base.PostSwapMap();
            RefreshFromFamilyGrid();
        }

        public override void SetUpPowerVars()
        {
            // This proxy is deliberately not a vanilla-net producer or consumer. WNG's family
            // node owns the actual watt demand; native Odyssey only needs a truthful PowerOn flag.
            PowerOutput = 0f;
        }

        private void RefreshFromFamilyGrid()
        {
            if (parent == null || !parent.Spawned)
                return;

            PowerOutput = 0f;
            bool shouldBeOn =
                FamilyOperational &&
                FlickUtility.WantsToBeOn(parent) &&
                !parent.IsBrokenDown();

            if (PowerOn != shouldBeOn)
                PowerOn = shouldBeOn;
        }

        public override string CompInspectStringExtra()
        {
            if (parent == null || !parent.Spawned)
                return null;

            string label = WNGProps.family == WNGGravshipFamily.Wraith ? "Wraith neural grid"
                : WNGProps.family == WNGGravshipFamily.Asuran ? "Asuran power lattice"
                : WNGProps.family == WNGGravshipFamily.Goauld ? "Goa'uld ship power grid"
                : "gravship family power";

            return label + (FamilyOperational ? ": online" : ": unavailable");
        }
    }
}
