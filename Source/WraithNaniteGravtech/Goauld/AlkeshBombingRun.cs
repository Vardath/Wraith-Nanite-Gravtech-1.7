using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_GoauldAlkeshBombingRun : CompProperties
    {
        public int bombsPerRun = 6;
        public float impactAreaRadius = 8f;
        public float explosionRadiusMin = 3f;
        public float explosionRadiusMax = 4.5f;
        public int bombIntervalTicks = 18;
        public int warmupTicks = 30;
        public int randomFireRadius = 10;
        public float sortieFuelCost = 45f;
        public int requiredCrew = 1;

        public CompProperties_GoauldAlkeshBombingRun()
        {
            compClass = typeof(CompGoauldAlkeshBombingRun);
        }
    }

    /// <summary>
    /// Physical same-map bombing run for the exact landed Al'kesh. Native CompTransporter remains
    /// authoritative for occupants/cargo. The exact shuttle is nested in a spawned pass skyfaller,
    /// then a tightly tuned native Bombardment is created at the selected target. This preserves
    /// native projectile-interceptor/shield behavior instead of bypassing defenses with raw damage.
    /// </summary>
    public sealed class CompGoauldAlkeshBombingRun : ThingComp
    {
        private bool sortieActive;
        private bool strikeCommitted;
        private IntVec3 targetCell = IntVec3.Invalid;
        private IntVec3 returnCell = IntVec3.Invalid;
        private Rot4 returnRotation = Rot4.North;
        private int runsCompleted;

        private CompProperties_GoauldAlkeshBombingRun Props => (CompProperties_GoauldAlkeshBombingRun)props;
        private CompTransporter Transporter => parent?.TryGetComp<CompTransporter>();
        private CompRefuelable Fuel => parent?.TryGetComp<CompRefuelable>();

        public bool SortieActive => sortieActive;
        public Rot4 ReturnRotation => returnRotation;

        public int OperationalCrewCount
        {
            get
            {
                CompTransporter transporter = Transporter;
                if (transporter?.innerContainer == null)
                    return 0;
                return transporter.innerContainer.OfType<Pawn>()
                    .Count(p => p != null && !p.Dead && !p.Downed && p.RaceProps?.Humanlike == true);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer)
                yield break;

            Command_Action command = new Command_Action
            {
                defaultLabel = "Al'kesh bombing run",
                defaultDesc = "Select a ground target. The exact Al'kesh makes a physical attack pass and releases a concentrated plasma bombardment before returning to its landing area. Native shield interceptors can stop individual impacts.",
                icon = ContentFinder<Texture2D>.Get("Things/Building/Goauld/Shuttle/WNG_AlkeshTransport", false),
                action = BeginTargeting
            };

            CompTransporter transporter = Transporter;
            CompRefuelable fuel = Fuel;
            int requiredCrew = Math.Max(1, Props.requiredCrew);
            float fuelCost = Math.Max(0f, Props.sortieFuelCost);

            if (sortieActive)
                command.Disable("This Al'kesh is already committed to a bombing run.");
            else if (parent?.Spawned != true || parent.Map == null)
                command.Disable("The Al'kesh must be landed on a map.");
            else if (transporter == null)
                command.Disable("Native shuttle transporter unavailable.");
            else if (transporter.AnythingLeftToLoad)
                command.Disable("Finish loading assigned crew and cargo first.");
            else if (transporter.OverMassCapacity)
                command.Disable("The Al'kesh is over mass capacity.");
            else if (OperationalCrewCount < requiredCrew)
                command.Disable($"A bombing run requires at least {requiredCrew} conscious humanlike operator aboard.");
            else if (fuel == null || fuel.Fuel < fuelCost)
                command.Disable($"At least {fuelCost:0.#} {fuel.Props.FuelLabel} is required for a bombing run.");

            yield return command;
        }

        private void BeginTargeting()
        {
            if (parent?.Spawned != true || parent.Map == null)
                return;

            Map map = parent.Map;
            Find.Targeter.BeginTargeting(
                TargetingParameters.ForCell(),
                target => TryBeginBombingRun(target.Cell, showFailureMessage: true),
                highlightAction: null,
                targetValidator: target => target.IsValid && target.Cell.IsValid && target.Cell.InBounds(map));
        }

        public bool TryBeginBombingRun(IntVec3 selectedTarget, bool showFailureMessage = true)
        {
            if (sortieActive || parent?.Spawned != true || parent.Map == null ||
                !selectedTarget.IsValid || !selectedTarget.InBounds(parent.Map))
                return false;

            CompTransporter transporter = Transporter;
            CompRefuelable fuel = Fuel;
            int requiredCrew = Math.Max(1, Props.requiredCrew);
            float fuelCost = Math.Max(0f, Props.sortieFuelCost);

            if (transporter == null || transporter.AnythingLeftToLoad || transporter.OverMassCapacity ||
                OperationalCrewCount < requiredCrew || fuel == null || fuel.Fuel < fuelCost)
            {
                if (showFailureMessage)
                    Messages.Message("Al'kesh bombing-run requirements are not met.", parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            Map map = parent.Map;
            targetCell = selectedTarget;
            returnCell = parent.Position;
            returnRotation = parent.Rotation;
            strikeCommitted = false;
            sortieActive = true;

            IntVec3 passCell = GoauldAlkeshFlightUtility.FindBombingPassCell(map, selectedTarget);
            if (!GoauldAlkeshFlightUtility.TryBeginPhysicalPass(parent, map, passCell))
            {
                ClearTransientState();
                if (showFailureMessage)
                    Messages.Message("The Al'kesh could not begin its bombing run.", parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            // Commit the fuel only after the exact craft has a real spawned pass holder. If pass
            // creation fails, both fuel and craft remain in their prior state.
            if (fuelCost > 0f)
                fuel.ConsumeFuel(fuelCost);
            return true;
        }

        public bool ExecuteBombingStrike(Map map)
        {
            if (!sortieActive || strikeCommitted || map == null || parent == null || parent.Destroyed ||
                !targetCell.IsValid || !targetCell.InBounds(map))
                return strikeCommitted;

            Bombardment bombardment = ThingMaker.MakeThing(ThingDefOf.Bombardment) as Bombardment;
            if (bombardment == null)
                return false;

            bombardment.impactAreaRadius = Math.Max(1f, Props.impactAreaRadius);
            float minRadius = Math.Max(0.5f, Math.Min(Props.explosionRadiusMin, Props.explosionRadiusMax));
            float maxRadius = Math.Max(minRadius, Math.Max(Props.explosionRadiusMin, Props.explosionRadiusMax));
            bombardment.explosionRadiusRange = new FloatRange(minRadius, maxRadius);
            bombardment.randomFireRadius = Math.Max(0, Props.randomFireRadius);
            bombardment.bombIntervalTicks = Math.Max(1, Props.bombIntervalTicks);
            bombardment.warmupTicks = Math.Max(1, Props.warmupTicks);
            bombardment.explosionCount = Math.Max(1, Props.bombsPerRun);
            bombardment.instigator = parent;
            bombardment.weaponDef = parent.def;

            try
            {
                Thing spawned = GenSpawn.Spawn(bombardment, targetCell, map, WipeMode.Vanish);
                if (spawned?.Spawned != true)
                {
                    if (!bombardment.Destroyed)
                        bombardment.Destroy(DestroyMode.Vanish);
                    return false;
                }
                strikeCommitted = true;
                runsCompleted++;
                return true;
            }
            catch (Exception ex)
            {
                if (!bombardment.Destroyed)
                {
                    try { bombardment.Destroy(DestroyMode.Vanish); } catch { }
                }
                Log.Warning("[WNG] Al'kesh bombardment failed before strike commit: " + ex.Message);
                return false;
            }
        }

        public IntVec3 FindReturnLandingCell(Map map, IntVec3 fallback)
        {
            if (map != null && returnCell.IsValid && returnCell.InBounds(map) &&
                (parent?.def == null || GenSpawn.CanSpawnAt(parent.def, returnCell, map, returnRotation, canWipeEdifices: false)))
                return returnCell;
            return GoauldAlkeshFlightUtility.FindLandingCell(parent, map, fallback);
        }

        public void NotifyPhysicallyLanded()
        {
            ClearTransientState();
        }

        private void ClearTransientState()
        {
            sortieActive = false;
            strikeCommitted = false;
            targetCell = IntVec3.Invalid;
            returnCell = IntVec3.Invalid;
        }

        public override string CompInspectStringExtra()
        {
            string state = sortieActive
                ? (strikeCommitted ? "bombing pass committed; returning" : "bombing pass in flight")
                : "landed";
            return $"Al'kesh bombing crew: {OperationalCrewCount}/{Math.Max(1, Props.requiredCrew)} minimum" +
                   $"\nBombing status: {state}" +
                   $"\nCompleted bombing runs: {runsCompleted}";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref sortieActive, "wngAlkeshBombingSortieActive", false);
            Scribe_Values.Look(ref strikeCommitted, "wngAlkeshBombingStrikeCommitted", false);
            Scribe_Values.Look(ref targetCell, "wngAlkeshBombingTargetCell", IntVec3.Invalid);
            Scribe_Values.Look(ref returnCell, "wngAlkeshBombingReturnCell", IntVec3.Invalid);
            Scribe_Values.Look(ref returnRotation, "wngAlkeshBombingReturnRotation", Rot4.North);
            Scribe_Values.Look(ref runsCompleted, "wngAlkeshBombingRunsCompleted", 0);
        }
    }

    /// <summary>Save-persistent holder for the exact Al'kesh while it performs one physical bombing pass.</summary>
    public sealed class Skyfaller_GoauldAlkeshBombingPass : Skyfaller
    {
        private bool passResolved;
        private Thing emergencyCraft;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref passResolved, "wngAlkeshBombingPassResolved", false);
            Scribe_Deep.Look(ref emergencyCraft, "wngAlkeshBombingEmergencyCraft");
        }

        protected override void Impact()
        {
            Map map = Map;
            IntVec3 passCell = Position;
            Thing craft = innerContainer?.FirstOrDefault() ?? emergencyCraft;
            if (craft == null || map == null)
            {
                base.Impact();
                return;
            }

            if (emergencyCraft == craft)
                emergencyCraft = null;
            else
                innerContainer.Remove(craft);

            CompGoauldAlkeshBombingRun mission = craft.TryGetComp<CompGoauldAlkeshBombingRun>();
            bool landed = false;
            bool strikeRetryRequired = false;
            try
            {
                if (!passResolved)
                {
                    if (mission != null && !mission.ExecuteBombingStrike(map))
                        strikeRetryRequired = true;
                    else
                        passResolved = true;
                }
                if (!strikeRetryRequired)
                    landed = LandExactCraft(craft, map, passCell, mission);
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Al'kesh bombing-pass transition threw before landing commit: " + ex);
                if (passResolved)
                    landed = LandExactCraft(craft, map, passCell, mission);
                else
                    strikeRetryRequired = true;
            }

            if (!landed)
            {
                if (!innerContainer.TryAdd(craft))
                {
                    if (craft.ParentHolder != null)
                    {
                        Log.Warning("[WNG] Exact Al'kesh already has another real holder; preserving that owner.");
                        Destroy(DestroyMode.Vanish);
                        return;
                    }
                    emergencyCraft = craft;
                    Log.Error("[WNG] Exact Al'kesh could neither land nor re-enter the bombing-pass holder; retaining it by deep save and retrying.");
                }
                ticksToImpact = 60;
                return;
            }

            Destroy(DestroyMode.Vanish);
        }

        private static bool LandExactCraft(Thing craft, Map map, IntVec3 near, CompGoauldAlkeshBombingRun mission)
        {
            if (craft == null || craft.Destroyed || map == null)
                return false;
            if (craft.Spawned)
                return true;

            IntVec3 cell = mission != null ? mission.FindReturnLandingCell(map, near) : GoauldAlkeshFlightUtility.FindLandingCell(craft, map, near);
            if (!cell.IsValid || !cell.InBounds(map))
                return false;

            Rot4 rotation = mission != null ? mission.ReturnRotation : craft.Rotation;
            try
            {
                Thing spawned = GenSpawn.Spawn(craft, cell, map, rotation, WipeMode.VanishOrMoveAside);
                if (spawned?.Spawned == true)
                {
                    mission?.NotifyPhysicallyLanded();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Al'kesh exact-craft landing failed before commit: " + ex.Message);
            }
            return false;
        }
    }

    public static class GoauldAlkeshFlightUtility
    {
        private const string BombingPassDefName = "WNG_AlkeshBombingPass";

        public static bool TryBeginPhysicalPass(Thing craft, Map map, IntVec3 passCell)
        {
            if (craft == null || map == null || !passCell.IsValid || !passCell.InBounds(map))
                return false;

            bool wasSpawned = craft.Spawned;
            IntVec3 oldCell = wasSpawned ? craft.Position : IntVec3.Invalid;
            Rot4 oldRotation = craft.Rotation;
            if (wasSpawned)
                craft.DeSpawn();

            if (SpawnBombingPass(craft, map, passCell))
                return true;

            if (!craft.Spawned && oldCell.IsValid && oldCell.InBounds(map))
            {
                IntVec3 rollbackCell = FindLandingCell(craft, map, oldCell);
                if (rollbackCell.IsValid && rollbackCell.InBounds(map))
                {
                    try { GenSpawn.Spawn(craft, rollbackCell, map, oldRotation, WipeMode.VanishOrMoveAside); }
                    catch (Exception ex) { Log.Error("[WNG] Al'kesh first-pass rollback could not respawn the exact craft: " + ex); }
                }
            }
            return false;
        }

        public static bool SpawnBombingPass(Thing craft, Map map, IntVec3 passCell)
        {
            ThingDef skyfallerDef = DefDatabase<ThingDef>.GetNamedSilentFail(BombingPassDefName);
            if (skyfallerDef == null || craft == null || craft.Destroyed || craft.Spawned || map == null)
                return false;
            if (!passCell.IsValid || !passCell.InBounds(map))
                passCell = FindBombingPassCell(map, map.Center);
            if (!passCell.IsValid || !passCell.InBounds(map))
                return false;

            Skyfaller skyfaller = null;
            try
            {
                skyfaller = SkyfallerMaker.MakeSkyfaller(skyfallerDef);
                if (skyfaller?.innerContainer == null)
                    return false;
                if (!skyfaller.innerContainer.TryAdd(craft))
                {
                    skyfaller.Destroy(DestroyMode.Vanish);
                    return false;
                }
                Thing spawned = GenSpawn.Spawn(skyfaller, passCell, map, WipeMode.Vanish);
                if (spawned?.Spawned == true)
                    return true;

                if (skyfaller.innerContainer.Contains(craft))
                    skyfaller.innerContainer.Remove(craft);
                if (!skyfaller.Destroyed)
                    skyfaller.Destroy(DestroyMode.Vanish);
                return false;
            }
            catch (Exception ex)
            {
                if (skyfaller?.innerContainer != null && skyfaller.innerContainer.Contains(craft))
                    skyfaller.innerContainer.Remove(craft);
                if (skyfaller != null && !skyfaller.Destroyed)
                {
                    try { skyfaller.Destroy(DestroyMode.Vanish); } catch { }
                }
                Log.Warning("[WNG] Al'kesh bombing-pass transition failed before commit: " + ex.Message);
                return false;
            }
        }

        public static IntVec3 FindBombingPassCell(Map map, IntVec3 target)
        {
            if (map == null)
                return target;
            IntVec3 preferred = new IntVec3(
                Math.Max(1, Math.Min(map.Size.x - 2, target.x + (target.x < map.Size.x / 2 ? 10 : -10))),
                0,
                Math.Max(1, Math.Min(map.Size.z - 2, target.z + (target.z < map.Size.z / 2 ? 10 : -10))));
            return FindPassCell(map, preferred);
        }

        private static IntVec3 FindPassCell(Map map, IntVec3 near)
        {
            Predicate<IntVec3> validator = c => c.InBounds(map) && c.Standable(map) && !c.Roofed(map) && c.GetFirstBuilding(map) == null;
            if (near.IsValid && near.InBounds(map) && validator(near))
                return near;
            IntVec3 center = near.IsValid && near.InBounds(map) ? near : map.Center;
            if (CellFinder.TryFindRandomCellNear(center, map, 18, validator, out IntVec3 result))
                return result;
            foreach (IntVec3 cell in map.AllCells)
                if (validator(cell)) return cell;
            return IntVec3.Invalid;
        }

        public static IntVec3 FindLandingCell(Thing craft, Map map, IntVec3 near)
        {
            if (map == null)
                return near;
            Predicate<IntVec3> validator = cell => cell.InBounds(map) && !cell.Fogged(map) &&
                (craft?.def == null || GenSpawn.CanSpawnAt(craft.def, cell, map, craft.Rotation, canWipeEdifices: false));
            if (near.IsValid && near.InBounds(map) && validator(near))
                return near;
            IntVec3 center = near.IsValid && near.InBounds(map) ? near : map.Center;
            if (CellFinder.TryFindRandomCellNear(center, map, 18, validator, out IntVec3 result))
                return result;
            foreach (IntVec3 cell in map.AllCells)
                if (validator(cell)) return cell;
            return IntVec3.Invalid;
        }
    }
}
