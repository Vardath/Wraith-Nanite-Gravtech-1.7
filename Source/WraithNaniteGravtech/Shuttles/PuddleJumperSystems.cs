using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public static class PuddleJumperControlUtility
    {
        public static bool HasCompatiblePawnAboard(Thing parent)
        {
            CompTransporter transporter = parent?.TryGetComp<CompTransporter>();
            return transporter?.innerContainer?.OfType<Pawn>().Any(p => p != null && !p.Dead && AncientCompatibilityUtility.IsCompatible(p)) == true;
        }
    }

    /// <summary>
    /// Player Puddle Jumpers use Odyssey's native launch lifecycle, but Ancient flight controls
    /// will not authorize launch unless a real ATA-compatible pawn is physically aboard.
    /// The shared compatibility utility accepts the natural Ancient activation gene, a successfully
    /// retained artificial interface, or the deliberately compatible Asuran synthetic handshake.
    /// </summary>
    public sealed class CompProperties_AncientPuddleJumperLaunchable : CompProperties_Launchable
    {
        public CompProperties_AncientPuddleJumperLaunchable()
        {
            compClass = typeof(CompAncientPuddleJumperLaunchable);
        }
    }

    public sealed class CompAncientPuddleJumperLaunchable : CompLaunchable
    {
        public override AcceptanceReport CanLaunch(float? overrideFuelLevel = null)
        {
            AcceptanceReport vanilla = base.CanLaunch(overrideFuelLevel);
            if (!vanilla.Accepted)
                return vanilla;

            if (parent?.Faction == Faction.OfPlayer &&
                !PuddleJumperControlUtility.HasCompatiblePawnAboard(parent))
            {
                return "Requires an Ancient/ATA-compatible pawn aboard the Puddle Jumper.";
            }

            return true;
        }
    }

    public sealed class CompProperties_PuddleJumperDroneArmament : CompProperties
    {
        public float droneRange = 72f;
        public int droneCooldownTicks = 900;
        public string researchDefName = "WNG_AncientDroneControl";
        public string ammoDefName = "WNG_AncientDrone";
        public string projectileDefName = "WNG_AncientDroneProjectile";
        public CompProperties_PuddleJumperDroneArmament() { compClass = typeof(CompPuddleJumperDroneArmament); }
    }

    public sealed class CompPuddleJumperDroneArmament : ThingComp
    {
        private int nextDroneLaunchTick;
        public CompProperties_PuddleJumperDroneArmament Props => (CompProperties_PuddleJumperDroneArmament)props;

        private bool HasNeuralControl()
        {
            return PuddleJumperControlUtility.HasCompatiblePawnAboard(parent) ||
                   AncientControlNetworkUtility.HasActiveControlFor(parent);
        }

        private bool UpgradeUnlocked()
        {
            ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(Props.researchDefName);
            return research != null && research.IsFinished;
        }

        private Thing FindLoadedDrone()
        {
            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            return transporter?.innerContainer.FirstOrDefault(t => t != null && !t.Destroyed && t.def?.defName == Props.ammoDefName && t.stackCount > 0);
        }

        private int LoadedDroneCount()
        {
            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            return transporter == null ? 0 : transporter.innerContainer.Where(t => t != null && !t.Destroyed && t.def?.defName == Props.ammoDefName).Sum(t => Math.Max(0, t.stackCount));
        }

        private bool ValidDroneTarget(Thing target)
        {
            if (target == null || target.Destroyed || !target.Spawned || target.Map != parent.Map) return false;
            if (target.Faction == null || parent.Faction == null || !target.Faction.HostileTo(parent.Faction)) return false;
            float range = Math.Max(8f, Props.droneRange);
            return target.Position.DistanceToSquared(parent.Position) <= range * range;
        }

        private void RestoreReservedDrone(CompTransporter transporter, Thing reservedDrone)
        {
            if (reservedDrone == null || reservedDrone.Destroyed) return;
            if (transporter != null && transporter.innerContainer.TryAdd(reservedDrone, true)) return;
            if (parent.Spawned && parent.Map != null) GenPlace.TryPlaceThing(reservedDrone, parent.Position, parent.Map, ThingPlaceMode.Near);
        }

        private void BeginSelectDroneTarget()
        {
            if (parent.Faction != Faction.OfPlayer || parent.Map == null || !UpgradeUnlocked() || !HasNeuralControl()) return;
            if ((Find.TickManager?.TicksGame ?? 0) < nextDroneLaunchTick || FindLoadedDrone() == null) return;
            TargetingParameters parms = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = false,
                canTargetLocations = false,
                canTargetSelf = false,
                validator = t => ValidDroneTarget(t.Thing)
            };
            Find.Targeter.BeginTargeting(parms, LaunchDroneAt);
        }

        private void LaunchDroneAt(LocalTargetInfo target)
        {
            if (parent.Faction != Faction.OfPlayer || parent.Map == null || !UpgradeUnlocked() || !HasNeuralControl() || !ValidDroneTarget(target.Thing)) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextDroneLaunchTick) return;
            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            Thing ammo = FindLoadedDrone();
            if (transporter == null || ammo == null) return;
            ThingDef projectileDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.projectileDefName);
            Projectile projectile = projectileDef == null ? null : ThingMaker.MakeThing(projectileDef) as Projectile;
            if (projectile == null) return;
            Thing reservedDrone = ammo.SplitOff(1);
            if (reservedDrone == null) return;
            try
            {
                GenSpawn.Spawn(projectile, parent.Position, parent.Map);
                projectile.Launch(parent, parent.DrawPos, target, target, ProjectileHitFlags.IntendedTarget, false, null);
            }
            catch
            {
                if (projectile.Spawned && !projectile.Destroyed) projectile.Destroy(DestroyMode.Vanish);
                RestoreReservedDrone(transporter, reservedDrone);
                return;
            }
            nextDroneLaunchTick = Math.Min(int.MaxValue, now + Math.Max(60, Props.droneCooldownTicks));
            if (!reservedDrone.Destroyed) reservedDrone.Destroy(DestroyMode.Vanish);
            DefDatabase<SoundDef>.GetNamedSilentFail("WNG_AncientDroneLaunch")?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (parent.Faction != Faction.OfPlayer) yield break;
            Command_Action launch = new Command_Action
            {
                defaultLabel = "Launch Jumper drone",
                defaultDesc = "Launch one reconstructed Ancient drone carried in the Puddle Jumper. Requires Ancient drone-control research plus either an ATA-compatible pawn physically aboard or an active allied Ancient control chair within 72 cells.",
                icon = ContentFinder<Texture2D>.Get("UI/WNG/AncientDrone", true),
                action = BeginSelectDroneTarget
            };
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!UpgradeUnlocked()) launch.Disable("Requires Ancient drone control research.");
            else if (!HasNeuralControl()) launch.Disable("Requires an ATA-compatible pawn aboard or an active allied Ancient control chair within 72 cells.");
            else if (LoadedDroneCount() <= 0) launch.Disable("Load at least one reconstructed Ancient drone into the Jumper cargo.");
            else if (now < nextDroneLaunchTick) launch.Disable("Jumper drone-control emitters are recalibrating.");
            yield return launch;
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
                return null;

            string tier = UpgradeUnlocked()
                ? "Jumper systems tier: armed command suite"
                : "Jumper systems tier: reconnaissance suite";

            if (!UpgradeUnlocked())
                return tier + "\nDirect drone armament: requires Ancient drone control";

            string control = PuddleJumperControlUtility.HasCompatiblePawnAboard(parent)
                ? "onboard ATA-compatible pilot"
                : AncientControlNetworkUtility.HasActiveControlFor(parent)
                    ? "linked Ancient control chair"
                    : "no neural controller";

            string state = tier +
                           "\nDrone control: " + control +
                           "\nAncient drones in cargo: " + LoadedDroneCount();

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextDroneLaunchTick)
                state += "\nDrone-control recalibration: " +
                         ((nextDroneLaunchTick - now) / 60f).ToString("0.0") + " s";

            return state;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextDroneLaunchTick, "wngJumperNextDroneLaunch", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nextDroneLaunchTick < 0) nextDroneLaunchTick = 0;
        }
    }

    public sealed class CompProperties_PuddleJumperSystems : CompProperties
    {
        public float cloakFuelPerRareTick = 0.0025f;
        public int cloakCooldownTicks = 1200;
        public int sensorCooldownTicks = 2500;
        public CompProperties_PuddleJumperSystems() { compClass = typeof(CompPuddleJumperSystems); }
    }

    public sealed class CompPuddleJumperSystems : ThingComp
    {
        private bool cloaked;
        private int cloakCooldownUntilTick;
        private int nextSensorTick;
        private int hostileIngressCloakUntilTick = -1;
        public CompProperties_PuddleJumperSystems Props => (CompProperties_PuddleJumperSystems)props;
        public bool Cloaked => cloaked;

        /// <summary>
        /// Bounded non-player ingress use of the same finite cloak as the player Jumper.
        /// It requires a real compatible operator already aboard and real slurry fuel.
        /// </summary>
        public bool BeginHostileIngressCloak(int durationTicks)
        {
            if (parent == null || parent.Destroyed || parent.Faction == null || parent.Faction == Faction.OfPlayer ||
                Faction.OfPlayer == null || !parent.Faction.HostileTo(Faction.OfPlayer) ||
                !PuddleJumperControlUtility.HasCompatiblePawnAboard(parent))
                return false;

            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            float minimumFuel = Math.Max(0f, Props.cloakFuelPerRareTick);
            if (fuel == null || fuel.Fuel + 0.0001f < minimumFuel)
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            cloaked = true;
            hostileIngressCloakUntilTick = Math.Min(int.MaxValue, now + Math.Max(250, durationTicks));
            return true;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent == null || !parent.IsHashIntervalTick(250) || !cloaked || !parent.Spawned || parent.Map == null || parent.Destroyed)
                return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (hostileIngressCloakUntilTick >= 0 && now >= hostileIngressCloakUntilTick)
            {
                CollapseCloak(true);
                return;
            }
            if (!PuddleJumperControlUtility.HasCompatiblePawnAboard(parent))
            {
                CollapseCloak(true);
                return;
            }
            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            float cost = Math.Max(0f, Props.cloakFuelPerRareTick);
            if (fuel == null || fuel.Fuel + 0.0001f < cost)
            {
                CollapseCloak(true);
                return;
            }
            if (cost > 0f) fuel.ConsumeFuel(cost);
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            if (absorbed || !cloaked || parent.Map == null) return;
            Thing instigator = dinfo.Instigator;
            if (instigator == null || !instigator.Spawned || instigator.Map != parent.Map) return;
            if (instigator.Position.DistanceToSquared(parent.Position) <= 25f) return;
            absorbed = true;
            CollapseCloak(true);
            Messages.Message(parent.LabelCap + " shimmered into view as its cloak dispersed an incoming attack.", parent, parent.Faction == Faction.OfPlayer ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.ThreatSmall, false);
        }

        private void CollapseCloak(bool startCooldown)
        {
            cloaked = false;
            hostileIngressCloakUntilTick = -1;
            if (startCooldown) cloakCooldownUntilTick = (Find.TickManager?.TicksGame ?? 0) + Math.Max(60, Props.cloakCooldownTicks);
        }

        private void TogglePlayerCloak()
        {
            if (parent.Faction != Faction.OfPlayer) return;
            if (cloaked) { CollapseCloak(false); return; }
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < cloakCooldownUntilTick || !PuddleJumperControlUtility.HasCompatiblePawnAboard(parent)) return;
            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            if (fuel == null || fuel.Fuel + 0.0001f < Math.Max(0f, Props.cloakFuelPerRareTick)) return;
            cloaked = true;
        }

        private void PlayerSensorSweep()
        {
            if (parent.Faction != Faction.OfPlayer || parent.Map == null || !PuddleJumperControlUtility.HasCompatiblePawnAboard(parent)) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextSensorTick) return;
            nextSensorTick = now + Math.Max(60, Props.sensorCooldownTicks);
            Thing target = parent.Map.listerThings.AllThings
                .Where(t => t is Building && t.Spawned && t.Faction != null && parent.Faction.HostileTo(t.Faction))
                .OrderByDescending(t => t.MarketValue)
                .ThenBy(t => t.Position.DistanceToSquared(parent.Position))
                .FirstOrDefault();
            if (target == null) Messages.Message("Puddle Jumper sensors found no hostile technological target on this map.", parent, MessageTypeDefOf.NeutralEvent, false);
            else Messages.Message("Puddle Jumper sensor priority: " + target.LabelCap + ".", target, MessageTypeDefOf.NeutralEvent, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (parent.Faction != Faction.OfPlayer) yield break;
            Command_Toggle cloak = new Command_Toggle
            {
                defaultLabel = "Puddle Jumper cloak",
                defaultDesc = "Engage the Jumper's finite concealment field. It consumes the Jumper's nanite-slurry reserve while active and can disperse one long-range incoming attack before collapsing.",
                isActive = () => cloaked,
                toggleAction = TogglePlayerCloak
            };
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!cloaked && now < cloakCooldownUntilTick) cloak.Disable("Cloak field is recalibrating.");
            else if (!cloaked && !PuddleJumperControlUtility.HasCompatiblePawnAboard(parent)) cloak.Disable("Requires an ATA-compatible pawn aboard.");
            yield return cloak;

            Command_Action sensor = new Command_Action
            {
                defaultLabel = "Sensor sweep",
                defaultDesc = "Use the Jumper's neural sensor suite to identify the highest-value hostile technological signature on this map.",
                action = PlayerSensorSweep
            };
            if (!PuddleJumperControlUtility.HasCompatiblePawnAboard(parent)) sensor.Disable("Requires an ATA-compatible pawn aboard.");
            else if (now < nextSensorTick) sensor.Disable("Sensors are recalibrating.");
            yield return sensor;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cloaked, "wngJumperCloaked", false);
            Scribe_Values.Look(ref cloakCooldownUntilTick, "wngJumperCloakCooldownUntil", 0);
            Scribe_Values.Look(ref nextSensorTick, "wngJumperNextSensorTick", 0);
            Scribe_Values.Look(ref hostileIngressCloakUntilTick, "wngJumperHostileIngressCloakUntil", -1);
        }
    }
}
