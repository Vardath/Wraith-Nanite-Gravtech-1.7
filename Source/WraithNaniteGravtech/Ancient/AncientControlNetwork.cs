using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AncientControlChair : CompProperties
    {
        public float networkRadius = 72f;
        public float droneRange = 82f;
        public float droneFuelCost = 1f;
        public int operatorCheckInterval = 60;
        public string droneProjectileDefName = "WNG_AncientDroneProjectile";
        public string operatorJobDefName = "WNG_OperateAncientControlChair";
        public CompProperties_AncientControlChair() { compClass = typeof(CompAncientControlChair); }
    }

    public sealed class CompAncientControlChair : ThingComp
    {
        private Pawn operatorPawn;
        private int nextOperatorValidationTick;
        public CompProperties_AncientControlChair Props => (CompProperties_AncientControlChair)props;
        public Pawn OperatorPawn => operatorPawn;

        public bool NetworkActive
        {
            get
            {
                if (!parent.Spawned || parent.Map == null || parent.Faction == null)
                    return false;
                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                return (power == null || power.PowerOn) && IsOperatorActivelyControlling(operatorPawn);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref operatorPawn, "wngAncientChairOperator");
            Scribe_Values.Look(ref nextOperatorValidationTick, "wngAncientChairNextValidation", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || Find.TickManager == null || Find.TickManager.TicksGame < nextOperatorValidationTick)
                return;
            nextOperatorValidationTick = Find.TickManager.TicksGame + Math.Max(30, Props.operatorCheckInterval);
            if (operatorPawn != null && (!operatorPawn.Spawned || operatorPawn.Map != parent.Map || operatorPawn.Dead))
                operatorPawn = null;
        }

        public override string CompInspectStringExtra()
        {
            string status = operatorPawn == null
                ? "Neural operator: none"
                : "Neural operator: " + operatorPawn.LabelShortCap + (NetworkActive ? " (linked)" : " (inactive)");

            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            if (fuel != null)
                status += "\nAncient drones loaded: " + Math.Floor(fuel.Fuel) + " / " + Math.Floor(fuel.Props.fuelCapacity);

            if (parent.Spawned && parent.Map != null)
            {
                int jumpers = AncientControlNetworkUtility
                    .NetworkedThings(parent.Map, parent.Faction, parent.Position, Props.networkRadius, "WNG_PuddleJumper")
                    .Count();
                int shields = AncientControlNetworkUtility
                    .NetworkedShieldThings(parent.Map, parent.Faction, parent.Position, Props.networkRadius)
                    .Count();
                status += "\nCommand network: " + jumpers + " Jumper(s), " + shields + " shield emitter(s)";
            }

            return status;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;
            if (parent.Faction != Faction.OfPlayer || !parent.Spawned)
                yield break;

            Command_Action link = new Command_Action
            {
                defaultLabel = operatorPawn == null ? "Link Ancient operator" : "Change Ancient operator",
                defaultDesc = "Assign an ATA-compatible pawn to physically operate this Ancient control chair.",
                icon = ContentFinder<Texture2D>.Get("UI/WNG/AncientAffinity", true),
                action = BeginSelectOperator
            };
            yield return link;

            Command_Action launch = new Command_Action
            {
                defaultLabel = "Launch Ancient drone",
                defaultDesc = "Launch one physical reconstructed Ancient drone at a hostile target. One loaded drone is consumed per successful launch.",
                icon = ContentFinder<Texture2D>.Get("UI/WNG/AncientDrone", true),
                action = BeginSelectDroneTarget
            };
            if (!NetworkActive)
                launch.Disable("A compatible neural operator must remain at the powered control chair.");
            else if (!HasDroneAmmo())
                launch.Disable("No Ancient drones are loaded.");
            yield return launch;

            Command_Action shieldsOn = new Command_Action
            {
                defaultLabel = "Engage network shields",
                defaultDesc = "Switch on allied WNG Asuran/Ancient gravship shield emitters inside this 72-cell neural control network. Native shield power, charging, EMP and overload behavior remains authoritative.",
                icon = ContentFinder<Texture2D>.Get("UI/WNG/Build/WNG_AncientControlChair", true),
                action = () => SetNetworkShields(true)
            };
            if (!NetworkActive)
                shieldsOn.Disable("The control chair has no active neural operator.");
            yield return shieldsOn;

            Command_Action shieldsOff = new Command_Action
            {
                defaultLabel = "Stand down network shields",
                defaultDesc = "Switch off allied WNG Asuran/Ancient gravship shield emitters inside this 72-cell neural control network.",
                icon = ContentFinder<Texture2D>.Get("UI/WNG/Build/WNG_AncientControlChair", true),
                action = () => SetNetworkShields(false)
            };
            if (!NetworkActive)
                shieldsOff.Disable("The control chair has no active neural operator.");
            yield return shieldsOff;
        }

        private void BeginSelectOperator()
        {
            TargetingParameters parms = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetLocations = false,
                canTargetSelf = false,
                validator = t => t.Thing is Pawn p && IsCompatibleOperator(p)
            };
            Find.Targeter.BeginTargeting(parms, t => AssignOperator(t.Thing as Pawn));
        }

        private void AssignOperator(Pawn pawn)
        {
            if (!IsCompatibleOperator(pawn) || pawn.Map != parent.Map || pawn.jobs == null)
                return;
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(Props.operatorJobDefName);
            if (jobDef == null)
                return;
            Job job = JobMaker.MakeJob(jobDef, parent);
            job.playerForced = true;
            if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                operatorPawn = pawn;
        }

        private bool IsOperatorActivelyControlling(Pawn pawn)
        {
            if (!IsCompatibleOperator(pawn) || !pawn.Spawned || pawn.Map != parent.Map || pawn.Downed)
                return false;
            if (pawn.CurJobDef?.defName != Props.operatorJobDefName || pawn.CurJob?.targetA.Thing != parent)
                return false;
            return pawn.Position.DistanceToSquared(parent.InteractionCell) <= 2f;
        }

        private static bool IsCompatibleOperator(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.Faction == Faction.OfPlayer && AncientCompatibilityUtility.IsCompatible(pawn);
        }

        private bool HasDroneAmmo()
        {
            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            return fuel != null && fuel.Fuel + 0.0001f >= Math.Max(0.01f, Props.droneFuelCost);
        }

        private void BeginSelectDroneTarget()
        {
            if (!NetworkActive || !HasDroneAmmo())
                return;
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

        private bool ValidDroneTarget(Thing target)
        {
            return target != null && !target.Destroyed && target.Spawned && target.Map == parent.Map && target.Faction != null &&
                   parent.Faction != null && target.Faction.HostileTo(parent.Faction) &&
                   target.Position.DistanceToSquared(parent.Position) <= Props.droneRange * Props.droneRange;
        }

        private void LaunchDroneAt(LocalTargetInfo target)
        {
            if (!NetworkActive || !HasDroneAmmo() || !ValidDroneTarget(target.Thing))
                return;
            ThingDef projectileDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.droneProjectileDefName);
            Projectile projectile = projectileDef == null ? null : ThingMaker.MakeThing(projectileDef) as Projectile;
            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            float cost = Math.Max(0.01f, Props.droneFuelCost);
            if (projectile == null || fuel == null || fuel.Fuel + 0.0001f < cost)
            {
                if (projectile != null && !projectile.Destroyed)
                    projectile.Destroy(DestroyMode.Vanish);
                return;
            }

            float before = fuel.Fuel;
            try
            {
                fuel.ConsumeFuel(cost);
                GenSpawn.Spawn(projectile, parent.Position, parent.Map);
                projectile.Launch(parent, parent.DrawPos, target, target, ProjectileHitFlags.IntendedTarget, false, null);
            }
            catch
            {
                if (!projectile.Destroyed)
                    projectile.Destroy(DestroyMode.Vanish);
                float consumed = Math.Max(0f, before - fuel.Fuel);
                if (consumed > 0.0001f)
                    fuel.Refuel(consumed / Math.Max(0.0001f, fuel.Props.FuelMultiplierCurrentDifficulty));
                return;
            }
            DefDatabase<SoundDef>.GetNamedSilentFail("WNG_AncientDroneLaunch")?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
        }
        private void SetNetworkShields(bool enabled)
        {
            if (!NetworkActive || parent.Map == null || parent.Faction == null)
                return;

            int changed = 0;
            foreach (Thing thing in AncientControlNetworkUtility.NetworkedShieldThings(
                         parent.Map,
                         parent.Faction,
                         parent.Position,
                         Props.networkRadius))
            {
                CompFlickable flick = thing.TryGetComp<CompFlickable>();
                if (flick == null || flick.SwitchIsOn == enabled)
                    continue;

                flick.SwitchIsOn = enabled;
                changed++;
            }

            try
            {
                Messages.Message(
                    "Ancient control network " + (enabled ? "engaged " : "stood down ") +
                    changed + " shield emitter(s).",
                    parent,
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Ancient control-network shield state committed but presentation failed: " + ex.Message);
            }
        }
    }

    public static class AncientControlNetworkUtility
    {
        private const string ControlChairDefName = "WNG_AncientControlChair";

        public static readonly string[] ShieldDefNames =
        {
            "WNG_PrecursorShieldEmitter",
            "WNG_AncientVacuumShieldEmitter"
        };

        public static bool HasActiveControlFor(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.Map == null || thing.Faction == null)
                return false;

            ThingDef chairDef = DefDatabase<ThingDef>.GetNamedSilentFail(ControlChairDefName);
            if (chairDef == null)
                return false;

            foreach (Thing candidate in thing.Map.listerThings.ThingsOfDef(chairDef))
            {
                if (candidate == null ||
                    candidate.Destroyed ||
                    !candidate.Spawned ||
                    candidate.Faction != thing.Faction)
                    continue;

                CompAncientControlChair chair = candidate.TryGetComp<CompAncientControlChair>();
                if (chair == null || !chair.NetworkActive)
                    continue;

                float radius = Math.Max(1f, chair.Props.networkRadius);
                if (candidate.Position.DistanceToSquared(thing.Position) <= radius * radius)
                    return true;
            }

            return false;
        }

        public static IEnumerable<Thing> NetworkedThings(
            Map map,
            Faction faction,
            IntVec3 center,
            float radius,
            string defName)
        {
            if (map == null || faction == null || defName.NullOrEmpty())
                yield break;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
                yield break;

            float radiusSquared = Math.Max(1f, radius) * Math.Max(1f, radius);
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing == null || !thing.Spawned || thing.Faction != faction)
                    continue;
                if (thing.Position.DistanceToSquared(center) <= radiusSquared)
                    yield return thing;
            }
        }

        public static IEnumerable<Thing> NetworkedShieldThings(
            Map map,
            Faction faction,
            IntVec3 center,
            float radius)
        {
            if (map == null || faction == null)
                yield break;

            float radiusSquared = Math.Max(1f, radius) * Math.Max(1f, radius);
            foreach (string defName in ShieldDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                    continue;

                foreach (Thing thing in map.listerThings.ThingsOfDef(def))
                {
                    if (thing == null || !thing.Spawned || thing.Faction != faction)
                        continue;
                    if (thing.Position.DistanceToSquared(center) <= radiusSquared)
                        yield return thing;
                }
            }
        }
    }


    // Ancient drone source art faces left in its inventory/UI presentation.
    // RimWorld projectile meshes treat local +Z (texture-up) as flight-forward,
    // so add a fixed +90 degree yaw: the drone's pointed nose then leads the
    // ExactRotation vector from the chair/Jumper to its target.
    public sealed class Projectile_AncientDrone : Bullet
    {
        public override Quaternion ExactRotation =>
            base.ExactRotation * Quaternion.AngleAxis(90f, Vector3.up);
    }

    public sealed class JobDriver_OperateAncientControlChair : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil operate = ToilMaker.MakeToil("OperateAncientControlChair");
            operate.defaultCompleteMode = ToilCompleteMode.Never;
            operate.tickAction = () => pawn.rotationTracker.FaceTarget(TargetA);
            yield return operate;
        }
    }
}
