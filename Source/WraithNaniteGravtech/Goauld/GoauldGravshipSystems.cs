using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Native turret power facade backed by the isolated Goa'uld ship grid. Because this is a
    /// CompPowerTrader subclass rather than the exact vanilla CompPowerTrader class, ThingDef's
    /// ConnectToPower rule does not register it as an ordinary colony-grid connector. The turret
    /// still sees a normal CompPowerTrader and therefore retains native targeting/firing behavior.
    /// </summary>
    public sealed class CompProperties_WNGGoauldFamilyPowerProxy : CompProperties_Power
    {
        public CompProperties_WNGGoauldFamilyPowerProxy()
        {
            compClass = typeof(CompPowerTrader_WNGGoauldFamilyProxy);
        }
    }

    public sealed class CompPowerTrader_WNGGoauldFamilyProxy : CompPowerTrader
    {
        private bool FamilyOperational
        {
            get
            {
                if (parent == null || !parent.Spawned || parent.Map == null)
                    return false;

                CompGravshipFacility facility = parent.TryGetComp<CompGravshipFacility>();
                if (facility == null || !WNGGravshipFamilyUtility.ExactEngineLinkIsValid(
                        facility, WNGGravshipFamily.Goauld, requiresPower: false))
                    return false;

                return WNGFamilyPowerUtility.IsPowered(parent, WNGGravshipFamily.Goauld);
            }
        }

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

        public override void PostSwapMap()
        {
            base.PostSwapMap();
            RefreshFromFamilyGrid();
        }

        private void RefreshFromFamilyGrid()
        {
            if (parent == null || !parent.Spawned)
                return;

            PowerOutput = 0f;
            bool shouldBeOn = FamilyOperational && FlickUtility.WantsToBeOn(parent) && !parent.IsBrokenDown();
            if (PowerOn != shouldBeOn)
                PowerOn = shouldBeOn;
        }

        public override string CompInspectStringExtra()
        {
            string native = base.CompInspectStringExtra();
            if (parent != null && parent.Spawned && !FamilyOperational)
            {
                const string text = "Goa'uld ship power unavailable.";
                return native.NullOrEmpty() ? text : native + "\n" + text;
            }
            return native;
        }
    }

    /// <summary>
    /// Native Odyssey projectile interception with recharge/activation tied to the isolated
    /// Goa'uld ship grid rather than a hidden vanilla power connection.
    /// </summary>
    public sealed class CompProperties_WNGGoauldGravshipShield : CompProperties_ProjectileInterceptor
    {
        public CompProperties_WNGGoauldGravshipShield()
        {
            compClass = typeof(CompWNGGoauldGravshipShield);
        }
    }

    public sealed class CompWNGGoauldGravshipShield : CompProjectileInterceptor
    {
        private bool FamilyOperational
        {
            get
            {
                if (parent == null || !parent.Spawned || parent.Map == null)
                    return false;

                CompGravshipFacility facility = parent.TryGetComp<CompGravshipFacility>();
                return facility != null &&
                       WNGGravshipFamilyUtility.ExactEngineLinkIsValid(
                           facility, WNGGravshipFamily.Goauld, requiresPower: false) &&
                       WNGFamilyPowerUtility.IsPowered(parent, WNGGravshipFamily.Goauld);
            }
        }

        protected override int HitPointsPerInterval => FamilyOperational ? 2 : -1;

        public override void CompTick()
        {
            if (parent != null && parent.Spawned && Active && !FamilyOperational)
                Deactivate();
            base.CompTick();
        }

        public override string CompInspectStringExtra()
        {
            string native = base.CompInspectStringExtra();
            if (parent != null && parent.Spawned && !FamilyOperational)
            {
                const string text = "Goa'uld ship power unavailable.";
                return native.NullOrEmpty() ? text : native + "\n" + text;
            }
            return native;
        }
    }

    public sealed class CompInteractable_WNGGoauldGravshipShield : CompInteractable_GravshipShieldGenerator
    {
        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
        {
            if (parent == null || !parent.Spawned)
                return false;

            CompGravshipFacility facility = parent.TryGetComp<CompGravshipFacility>();
            if (facility == null || !WNGGravshipFamilyUtility.ExactEngineLinkIsValid(
                    facility, WNGGravshipFamily.Goauld, requiresPower: false))
                return "Shield emitter must be linked to the Ha'tak grav engine.";

            if (!WNGFamilyPowerUtility.IsPowered(parent, WNGGravshipFamily.Goauld))
                return "Insufficient Goa'uld ship power.";

            return base.CanInteract(activateBy, checkOptionalItems);
        }
    }
}
