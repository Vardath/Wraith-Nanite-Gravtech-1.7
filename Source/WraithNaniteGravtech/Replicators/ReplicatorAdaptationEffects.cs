using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorAdaptationEffects : CompProperties
    {
        public float materialAssimilationTimeFactor = 0.85f;
        public float powerRegenerationIntervalFactor = 0.65f;
        public float powerRegenerationHealFactor = 1.35f;
        public float armorDeflectionChance = 0.12f;
        public int shieldRechargeTicks = 1800;
        public string rangedWeaponDef = "WNG_ReplicatorPulseCaster";
        public string antiShieldWeaponDef = "WNG_ReplicatorShieldDisruptor";
        public string gravFlightHediffDef = "WNG_ReplicatorAdaptGravFlight";

        public CompProperties_ReplicatorAdaptationEffects()
        {
            compClass = typeof(CompReplicatorAdaptationEffects);
        }
    }

    /// <summary>
    /// Concrete cumulative adaptation effects. Ranged grows the integrated pulse caster; Grav
    /// uses RimWorld's native low-hover flight; Shield/Armor/Material/Power retain their bounded
    /// effects; Shield + learned AntiShield upgrades the integrated weapon to the phase disruptor.
    /// </summary>
    public sealed class CompReplicatorAdaptationEffects : ThingComp
    {
        private int shieldReadyTick;

        private CompProperties_ReplicatorAdaptationEffects Props =>
            (CompProperties_ReplicatorAdaptationEffects)props;

        private CompReplicatorAdaptation Adaptation => parent.TryGetComp<CompReplicatorAdaptation>();

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            NotifyAdaptationsChanged();
        }

        public override void CompTick()
        {
            base.CompTick();

            Pawn pawn = parent as Pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned)
                return;

            // Native flight is the Grav adaptation's physical mobility effect. EMP does not get a
            // second suppression clock here: the shared interference endpoint simply forces any
            // active low-hover state to land while disruption remains in force.
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn) && pawn.Flying)
                pawn.flight?.ForceLand();
        }

        public void NotifyAdaptationsChanged()
        {
            Pawn pawn = parent as Pawn;
            if (pawn == null)
                return;

            EnsureIntegratedWeapon(pawn);
            EnsureGravFlightHediff(pawn);
        }

        public float AssimilationTimeFactor
        {
            get
            {
                if (!WNGSettingsUtility.ReplicatorMaterialAdaptationEnabled)
                    return 1f;

                return Adaptation?.Has(ReplicatorAdaptationFlags.Material) == true
                    ? ClampFactor(Props.materialAssimilationTimeFactor)
                    : 1f;
            }
        }

        public float RegenerationIntervalFactor
        {
            get
            {
                return Adaptation?.Has(ReplicatorAdaptationFlags.Power) == true
                    ? ClampFactor(Props.powerRegenerationIntervalFactor)
                    : 1f;
            }
        }

        public float RegenerationHealFactor
        {
            get
            {
                return Adaptation?.Has(ReplicatorAdaptationFlags.Power) == true
                    ? Math.Max(0.01f, Props.powerRegenerationHealFactor)
                    : 1f;
            }
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;

            Pawn pawn = parent as Pawn;
            CompReplicatorAdaptation adaptation = Adaptation;
            if (pawn == null || adaptation == null || dinfo.Def == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;

            // EMP always reaches native RimWorld handling. It also collapses/delays the learned
            // shield. Never absorb EMP here or create an independent EMP stun mechanic.
            if (dinfo.Def == DamageDefOf.EMP)
            {
                if (adaptation.Has(ReplicatorAdaptationFlags.Shield))
                    shieldReadyTick = SafeFutureTick(now, Math.Max(60, Props.shieldRechargeTicks));
                return;
            }

            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn))
                return;

            if (adaptation.Has(ReplicatorAdaptationFlags.Shield) && now >= shieldReadyTick && dinfo.Amount > 0f)
            {
                absorbed = true;
                shieldReadyTick = SafeFutureTick(now, Math.Max(60, Props.shieldRechargeTicks));
                return;
            }

            if (adaptation.Has(ReplicatorAdaptationFlags.Armor) && IsPhysicalDamage(dinfo.Def))
            {
                float chance = Math.Max(0f, Math.Min(0.95f, Props.armorDeflectionChance));
                if (chance > 0f && Rand.Chance(chance))
                    absorbed = true;
            }
        }


        private void EnsureIntegratedWeapon(Pawn pawn)
        {
            if (pawn.equipment == null)
                return;

            ThingDef rangedDef = string.IsNullOrWhiteSpace(Props.rangedWeaponDef)
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(Props.rangedWeaponDef);
            ThingDef disruptorDef = string.IsNullOrWhiteSpace(Props.antiShieldWeaponDef)
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(Props.antiShieldWeaponDef);

            bool antiShieldBody = Adaptation?.Has(ReplicatorAdaptationFlags.Shield) == true &&
                                  Adaptation.Has(ReplicatorAdaptationFlags.AntiShield);
            ThingDef wanted = antiShieldBody
                ? disruptorDef
                : (Adaptation?.Has(ReplicatorAdaptationFlags.Ranged) == true ? rangedDef : null);
            if (wanted == null)
                return;

            ThingWithComps primary = pawn.equipment.Primary;
            if (primary?.def == wanted)
                return;

            // The two WNG adaptation weapons are integrated machine organs. When a cumulative
            // lineage upgrades from the pulse caster to the shield disruptor, destroy only the old
            // integrated WNG organ. Never discard unrelated player/third-party equipment here.
            if (primary != null && (primary.def == rangedDef || primary.def == disruptorDef))
                pawn.equipment.DestroyEquipment(primary);

            if (pawn.equipment.Primary != null)
                return;

            ThingWithComps weapon = ThingMaker.MakeThing(wanted) as ThingWithComps;
            if (weapon != null)
                pawn.equipment.AddEquipment(weapon);
        }

        private void EnsureGravFlightHediff(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null || Adaptation?.Has(ReplicatorAdaptationFlags.Grav) != true)
                return;

            HediffDef hediffDef = string.IsNullOrWhiteSpace(Props.gravFlightHediffDef)
                ? null
                : DefDatabase<HediffDef>.GetNamedSilentFail(Props.gravFlightHediffDef);
            if (hediffDef != null && !pawn.health.hediffSet.HasHediff(hediffDef))
                pawn.health.AddHediff(hediffDef);
        }

        private static bool IsPhysicalDamage(DamageDef def)
        {
            return def == DamageDefOf.Bullet || def == DamageDefOf.Cut || def == DamageDefOf.Blunt;
        }

        private static float ClampFactor(float factor)
        {
            return Math.Max(0.05f, Math.Min(4f, factor));
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref shieldReadyTick, "wngReplicatorAdaptiveShieldReadyTick", 0);
        }
    }
}
