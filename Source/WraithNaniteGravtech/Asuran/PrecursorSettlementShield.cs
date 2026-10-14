using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_PrecursorSettlementShield : CompProperties
    {
        public float radius = 14.9f;
        public float energyMax = 500f;
        public float rechargePerTick = 0.025f;
        public int resetDelayTicks = 3000;
        public int empDisableTicks = 3000;

        public CompProperties_PrecursorSettlementShield()
        {
            compClass = typeof(CompPrecursorSettlementShield);
        }
    }

    /// <summary>
    /// Fixed ground defensive tier between the personal shield and the Odyssey gravship emitter.
    /// The field intercepts hostile projectiles entering from outside its perimeter, permits outgoing
    /// fire, uses ordinary electrical power, has a finite saved charge and collapses under EMP.
    /// </summary>
    public sealed class CompPrecursorSettlementShield : ThingComp
    {
        private float energy = -1f;
        private int disabledUntilTick;
        private int nextInterceptTick;

        public CompProperties_PrecursorSettlementShield Props =>
            (CompProperties_PrecursorSettlementShield)props;

        private int Now => Find.TickManager?.TicksGame ?? 0;
        private bool Disabled => Now < disabledUntilTick;

        private bool Powered
        {
            get
            {
                CompPowerTrader power = parent?.TryGetComp<CompPowerTrader>();
                CompFlickable flick = parent?.TryGetComp<CompFlickable>();
                return (power == null || power.PowerOn) &&
                       (flick == null || flick.SwitchIsOn);
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            energy = Math.Max(1f, Props.energyMax);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (parent == null ||
                !parent.Spawned ||
                parent.Map == null ||
                parent.Faction == null)
            {
                return;
            }

            if (energy < 0f)
                energy = Math.Max(1f, Props.energyMax);

            if (Disabled || !Powered)
                return;

            if (energy < Props.energyMax)
            {
                energy = Math.Min(
                    Math.Max(1f, Props.energyMax),
                    energy + Math.Max(0f, Props.rechargePerTick));
            }

            if (energy <= 0f || Now < nextInterceptTick)
                return;

            nextInterceptTick = SafeFutureTick(Now, 1);

            List<Thing> projectiles = parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                Projectile projectile = projectiles[i] as Projectile;
                if (!ShouldIntercept(projectile))
                    continue;

                Intercept(projectile);
                if (energy <= 0f)
                    break;
            }
        }

        private bool ShouldIntercept(Projectile projectile)
        {
            if (projectile == null ||
                projectile.Destroyed ||
                !projectile.Spawned ||
                projectile.Map != parent.Map)
            {
                return false;
            }

            float radius = Math.Max(0.1f, Props.radius);
            float radiusSq = radius * radius;
            if ((projectile.DrawPos - parent.DrawPos).sqrMagnitude > radiusSq)
                return false;

            Thing launcher = projectile.Launcher;

            // Fire originating inside the field is allowed to leave.
            if (launcher != null &&
                launcher.Spawned &&
                launcher.Map == parent.Map &&
                (launcher.DrawPos - parent.DrawPos).sqrMagnitude <= radiusSq)
            {
                return false;
            }

            Faction launcherFaction = launcher?.Faction;
            return launcherFaction == null ||
                   (parent.Faction != null && launcherFaction.HostileTo(parent.Faction));
        }

        private void Intercept(Projectile projectile)
        {
            Pawn attacker = projectile.Launcher as Pawn;
            bool emp = projectile.def?.projectile?.damageDef == DamageDefOf.EMP;
            float cost = Math.Max(1f, projectile.DamageAmount);

            // A genuine hostile Replicator interaction with a functioning field is AntiShield
            // evidence in the current cumulative adaptation/domain system.
            if (attacker != null &&
                !attacker.Dead &&
                ReplicatorAssimilationUtility.IsBlockReplicator(attacker) &&
                attacker.Faction != null &&
                parent.Faction != null &&
                attacker.Faction.HostileTo(parent.Faction))
            {
                ReplicatorAdaptationUtility.ShareShieldEngagement(attacker);
            }

            // Projectile destruction is the interception commit. The energy/reset consequence is
            // established immediately afterwards; audio is presentation only.
            projectile.Destroy(DestroyMode.Vanish);

            bool broken = false;
            if (emp)
            {
                energy = 0f;
                disabledUntilTick = Math.Max(
                    disabledUntilTick,
                    SafeFutureTick(Now, Math.Max(1, Props.empDisableTicks)));
                broken = true;
            }
            else
            {
                energy = Math.Max(0f, energy - cost);
                if (energy <= 0f)
                {
                    disabledUntilTick = Math.Max(
                        disabledUntilTick,
                        SafeFutureTick(Now, Math.Max(1, Props.resetDelayTicks)));
                    broken = true;
                }
            }

            PlaySound("EnergyShield_AbsorbDamage");
            if (broken)
                PlaySound("EnergyShield_Broken");
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            absorbed = false;

            // A direct EMP hit against the projector also collapses the field.
            if (dinfo.Def != DamageDefOf.EMP)
                return;

            energy = 0f;
            disabledUntilTick = Math.Max(
                disabledUntilTick,
                SafeFutureTick(Now, Math.Max(1, Props.empDisableTicks)));
        }

        public override string CompInspectStringExtra()
        {
            float max = Math.Max(1f, Props.energyMax);
            float pct = Math.Max(0f, Math.Min(1f, energy / max));
            string state = !Powered
                ? "offline"
                : Disabled
                    ? "resetting"
                    : energy <= 0f
                        ? "depleted"
                        : "active";

            return "Settlement field: " + Math.Round(pct * 100f) + "% (" + state + ")" +
                   "\nField radius: " + Props.radius.ToString("0.0") + " cells";
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "Shield field status",
                defaultDesc =
                    "A fixed Asuran settlement field. It intercepts hostile projectiles entering " +
                    "from outside while allowing fire from inside to leave. EMP collapses the " +
                    "stored field and ordinary electrical power is required for recharge.",
                action = () => Messages.Message(
                    CompInspectStringExtra(),
                    parent,
                    MessageTypeDefOf.NeutralEvent,
                    historical: false)
            };
        }

        private void PlaySound(string defName)
        {
            if (parent == null || !parent.Spawned || parent.Map == null)
                return;

            try
            {
                DefDatabase<SoundDef>.GetNamedSilentFail(defName)
                    ?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Settlement shield state committed but sound presentation failed: " + ex.Message);
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private void NormalizeLoadedState()
        {
            float max = Math.Max(1f, Props?.energyMax ?? 1f);
            if (float.IsNaN(energy) || float.IsInfinity(energy) || energy < 0f)
                energy = max;
            else
                energy = Math.Max(0f, Math.Min(max, energy));

            int now = Now;
            int maxDisable = Math.Max(
                1,
                Math.Max(
                    Props?.resetDelayTicks ?? 1,
                    Props?.empDisableTicks ?? 1));

            if (disabledUntilTick <= now)
                disabledUntilTick = 0;
            else
                disabledUntilTick = Math.Min(
                    disabledUntilTick,
                    SafeFutureTick(now, maxDisable));

            if (nextInterceptTick <= now)
                nextInterceptTick = 0;
            else
                nextInterceptTick = Math.Min(nextInterceptTick, SafeFutureTick(now, 1));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref energy, "wngSettlementShieldEnergy", -1f);
            Scribe_Values.Look(ref disabledUntilTick, "wngSettlementShieldDisabledUntil", 0);
            Scribe_Values.Look(ref nextInterceptTick, "wngSettlementShieldNextInterceptTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                NormalizeLoadedState();
        }
    }
}
