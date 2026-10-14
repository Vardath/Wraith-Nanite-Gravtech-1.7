using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Native Odyssey projectile-interceptor behavior with its power assumption redirected into
    /// the isolated Asuran gravship power lattice. No vanilla power connector is added to the
    /// shield itself; ordinary colony power can reach it only through WNG_AsuranPowerCoupler.
    /// </summary>
    public sealed class CompProperties_WNGAsuranGravshipShield : CompProperties_ProjectileInterceptor
    {
        public CompProperties_WNGAsuranGravshipShield()
        {
            compClass = typeof(CompWNGAsuranGravshipShield);
        }
    }

    public sealed class CompWNGAsuranGravshipShield : CompProjectileInterceptor
    {
        private bool FamilyOperational
        {
            get
            {
                if (parent == null || !parent.Spawned || parent.Map == null)
                    return false;
                CompGravshipFacility facility = parent.TryGetComp<CompGravshipFacility>();
                return facility != null && facility.CanBeActive &&
                       WNGFamilyPowerUtility.IsPowered(parent, WNGGravshipFamily.Asuran);
            }
        }

        // Mirrors Odyssey's native shield recharge rate while making loss of family power costly.
        protected override int HitPointsPerInterval => FamilyOperational ? 2 : -1;

        public override void CompTick()
        {
            // A powered field cannot remain up after its isolated Asuran lattice collapses.
            // Deactivate uses the native charge/cooldown lifecycle rather than inventing a second
            // shield state machine.
            if (parent != null && parent.Spawned && Active && !FamilyOperational)
                Deactivate();

            base.CompTick();
        }

        public override string CompInspectStringExtra()
        {
            string native = base.CompInspectStringExtra();
            if (parent != null && parent.Spawned && !FamilyOperational)
            {
                const string power = "Asuran power lattice unavailable.";
                return native.NullOrEmpty() ? power : native + "\n" + power;
            }
            return native;
        }
    }

    /// <summary>
    /// Keeps Odyssey's normal gravship-shield interaction but refuses activation unless the
    /// emitter is actually linked to the Asuran engine and its isolated family network is powered.
    /// </summary>
    public sealed class CompInteractable_WNGAsuranGravshipShield : CompInteractable_GravshipShieldGenerator
    {
        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
        {
            if (parent == null || !parent.Spawned)
                return false;

            CompGravshipFacility facility = parent.TryGetComp<CompGravshipFacility>();
            if (facility == null || !facility.CanBeActive)
                return "Shield emitter must be linked to the powered Asuran gravship family.";

            if (!WNGFamilyPowerUtility.IsPowered(parent, WNGGravshipFamily.Asuran))
                return "Insufficient Asuran family power.";

            return base.CanInteract(activateBy, checkOptionalItems);
        }
    }
}
