using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_GoauldDeathGliderMission : CompProperties
    {
        public int attackPasses = 2;
        public int boltsPerPass = 2;
        public float acquisitionRadius = 45f;
        public float sortieFuelCost = 30f;
        public int requiredCrew = 2;
        public ThingDef projectile;

        public CompProperties_GoauldDeathGliderMission()
        {
            compClass = typeof(CompGoauldDeathGliderMission);
        }
    }

    /// <summary>
    /// Combat layer for the exact native Death Glider Thing. CompTransporter remains the authority
    /// for crew/cargo identity. During each pass the exact fighter is nested in a spawned skyfaller,
    /// so RimWorld's SpawnedOrAnyParentSpawned holder chain remains valid for transporter save logic.
    /// </summary>
    public sealed class CompGoauldDeathGliderMission : ThingComp
    {
        private bool sortieActive;
        private int completedPasses;
        private int boltsFired;
        private IntVec3 returnCell = IntVec3.Invalid;
        private Rot4 returnRotation = Rot4.North;
        private bool retreatAfterSortie;
        private Faction priorityTargetFaction;
        private IntVec3 priorityTargetCell = IntVec3.Invalid;

        private CompProperties_GoauldDeathGliderMission Props => (CompProperties_GoauldDeathGliderMission)props;
        private CompTransporter Transporter => parent?.TryGetComp<CompTransporter>();
        private CompRefuelable Fuel => parent?.TryGetComp<CompRefuelable>();

        public bool SortieActive => sortieActive;
        public bool ShouldWithdrawAfterFinalPass => retreatAfterSortie && parent?.Faction != Faction.OfPlayer;

        public void ConfigureHostileRetreat()
        {
            if (!sortieActive && parent?.Faction != Faction.OfPlayer)
                retreatAfterSortie = true;
        }

        public void ConfigurePriorityTarget(Faction targetFaction, IntVec3 focusCell)
        {
            if (sortieActive || parent?.Faction == Faction.OfPlayer)
                return;
            priorityTargetFaction = targetFaction;
            priorityTargetCell = focusCell;
        }

        public IntVec3 FindNextAttackPassCell(Map map, IntVec3 previousPassCell)
        {
            if (map != null && priorityTargetFaction != null &&
                priorityTargetCell.IsValid && priorityTargetCell.InBounds(map))
                return GoauldDeathGliderFlightUtility.FindAttackPassCellNear(map, priorityTargetCell);

            return GoauldDeathGliderFlightUtility.FindAttackPassCell(
                map,
                previousPassCell,
                oppositeSide: true);
        }

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
                defaultLabel = "Death Glider combat sortie",
                defaultDesc = "Fly this exact two-seat Death Glider through two physical staff-cannon attack passes, then return it to its original landing cell when possible. Exactly two conscious humanlike crew must be aboard.",
                icon = ContentFinder<Texture2D>.Get("Things/Building/Goauld/Shuttle/WNG_GoauldDeathGlider", false),
                action = () => TryBeginCombatSortie(showFailureMessage: true)
            };

            CompTransporter transporter = Transporter;
            CompRefuelable fuel = Fuel;
            int requiredCrew = Math.Max(1, Props.requiredCrew);
            float fuelCost = Math.Max(0f, Props.sortieFuelCost);

            if (sortieActive)
                command.Disable("This Death Glider is already committed to a combat sortie.");
            else if (parent?.Spawned != true || parent.Map == null)
                command.Disable("The Death Glider must be landed on a map.");
            else if (transporter == null)
                command.Disable("Native shuttle transporter unavailable.");
            else if (transporter.AnythingLeftToLoad)
                command.Disable("Finish loading the assigned Death Glider crew and cargo first.");
            else if (transporter.OverMassCapacity)
                command.Disable("The Death Glider is over mass capacity.");
            else if (OperationalCrewCount != requiredCrew)
                command.Disable($"A Death Glider combat sortie requires exactly {requiredCrew} conscious humanlike crew.");
            else if (fuel == null || fuel.Fuel < fuelCost)
                command.Disable($"At least {fuelCost:0.#} {fuel.Props.FuelLabel} is required for a combat sortie.");
            else if (Props.projectile == null)
                command.Disable("Death Glider staff-cannon projectile is unavailable.");

            yield return command;
        }

        public bool TryBeginCombatSortie(bool showFailureMessage = true)
        {
            if (sortieActive || parent?.Spawned != true || parent.Map == null)
                return false;

            CompTransporter transporter = Transporter;
            CompRefuelable fuel = Fuel;
            int requiredCrew = Math.Max(1, Props.requiredCrew);
            float fuelCost = Math.Max(0f, Props.sortieFuelCost);

            if (transporter == null || transporter.AnythingLeftToLoad || transporter.OverMassCapacity ||
                OperationalCrewCount != requiredCrew || fuel == null || fuel.Fuel < fuelCost || Props.projectile == null)
            {
                if (showFailureMessage)
                    Messages.Message("Death Glider sortie requirements are not met.", parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            Map map = parent.Map;
            returnCell = parent.Position;
            returnRotation = parent.Rotation;
            completedPasses = 0;
            boltsFired = 0;
            sortieActive = true;

            IntVec3 passCell = priorityTargetFaction != null &&
                               priorityTargetCell.IsValid &&
                               priorityTargetCell.InBounds(map)
                ? GoauldDeathGliderFlightUtility.FindAttackPassCellNear(map, priorityTargetCell)
                : GoauldDeathGliderFlightUtility.FindAttackPassCell(map, returnCell, oppositeSide: false);
            if (!GoauldDeathGliderFlightUtility.TryBeginPhysicalPass(parent, map, passCell))
            {
                sortieActive = false;
                returnCell = IntVec3.Invalid;
                if (showFailureMessage)
                    Messages.Message("The Death Glider could not begin its combat sortie.", parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            // Functional commit: fuel is spent only after the exact fighter is safely owned by the
            // first spawned pass skyfaller. A failed transition leaves fuel and fighter untouched.
            if (fuelCost > 0f)
                fuel.ConsumeFuel(fuelCost);
            return true;
        }

        public bool ExecutePhysicalPass(Map map, IntVec3 passCell)
        {
            if (!sortieActive || map == null || parent == null || parent.Destroyed)
                return false;

            FirePairedStaffCannons(map, passCell);
            completedPasses++;
            return completedPasses < Math.Max(1, Props.attackPasses);
        }

        public IntVec3 FindReturnLandingCell(Map map, IntVec3 fallback)
        {
            if (map != null && returnCell.IsValid && returnCell.InBounds(map) &&
                (parent?.def == null || GenSpawn.CanSpawnAt(parent.def, returnCell, map, returnRotation, canWipeEdifices: false)))
                return returnCell;

            return GoauldDeathGliderFlightUtility.FindLandingCell(parent, map, fallback);
        }

        public Rot4 ReturnRotation => returnRotation;

        public void NotifyPhysicallyLanded()
        {
            sortieActive = false;
            returnCell = IntVec3.Invalid;
            retreatAfterSortie = false;
            priorityTargetFaction = null;
            priorityTargetCell = IntVec3.Invalid;
        }

        public void NotifyPhysicalWithdrawalCommitted()
        {
            sortieActive = false;
            returnCell = IntVec3.Invalid;
            retreatAfterSortie = false;
            priorityTargetFaction = null;
            priorityTargetCell = IntVec3.Invalid;
        }

        private void FirePairedStaffCannons(Map map, IntVec3 passCell)
        {
            if (map?.listerThings?.AllThings == null || parent?.Faction == null || Props.projectile == null)
                return;

            float radius = Math.Max(1f, Props.acquisitionRadius);
            float radiusSq = radius * radius;
            int boltCount = Math.Max(2, Props.boltsPerPass);

            List<Thing> targets = map.listerThings.AllThings
                .Where(t => IsValidCombatTarget(t, map))
                .Where(t => t.Position.DistanceToSquared(passCell) <= radiusSq)
                .OrderBy(t => t is Pawn ? 0 : 1)
                .ThenBy(t => t.Position.DistanceToSquared(passCell))
                .ThenBy(t => t.thingIDNumber)
                .Take(2)
                .ToList();

            if (targets.Count == 0)
                return;

            for (int i = 0; i < boltCount; i++)
            {
                Thing target = targets[i % targets.Count];
                Thing made = ThingMaker.MakeThing(Props.projectile);
                Projectile projectile = made as Projectile;
                if (projectile == null)
                {
                    made?.Destroy();
                    continue;
                }

                Thing spawnedProjectile = GenSpawn.Spawn(projectile, passCell, map);
                if (spawnedProjectile?.Spawned != true)
                {
                    if (!projectile.Destroyed)
                        projectile.Destroy(DestroyMode.Vanish);
                    continue;
                }

                projectile.Launch(parent, passCell.ToVector3Shifted(), target, target, ProjectileHitFlags.IntendedTarget);
                boltsFired++;
            }
        }

        private bool IsValidCombatTarget(Thing thing, Map map)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map || thing.Faction == null ||
                parent?.Faction == null || thing.Faction == parent.Faction)
                return false;
            if (!(thing is Pawn) && !(thing is Building))
                return false;
            if (priorityTargetFaction != null)
                return thing.Faction == priorityTargetFaction;
            return parent.Faction.HostileTo(thing.Faction);
        }

        public override string CompInspectStringExtra()
        {
            int requiredCrew = Math.Max(1, Props.requiredCrew);
            string state = sortieActive
                ? $"in flight ({completedPasses}/{Math.Max(1, Props.attackPasses)} passes, {boltsFired} staff-cannon bolts fired)"
                : "landed";
            return $"Death Glider combat crew: {OperationalCrewCount}/{requiredCrew}" +
                   $"\nCombat status: {state}" +
                   "\nCarrier role: the physical fighter can travel with a Ha'tak when parked on its connected gravship deck.";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref sortieActive, "wngDeathGliderSortieActive", false);
            Scribe_Values.Look(ref completedPasses, "wngDeathGliderCompletedPasses", 0);
            Scribe_Values.Look(ref boltsFired, "wngDeathGliderBoltsFired", 0);
            Scribe_Values.Look(ref returnCell, "wngDeathGliderReturnCell", IntVec3.Invalid);
            Scribe_Values.Look(ref returnRotation, "wngDeathGliderReturnRotation", Rot4.North);
            Scribe_Values.Look(ref retreatAfterSortie, "wngDeathGliderRetreatAfterSortie", false);
            Scribe_References.Look(ref priorityTargetFaction, "wngDeathGliderPriorityTargetFaction");
            Scribe_Values.Look(ref priorityTargetCell, "wngDeathGliderPriorityTargetCell", IntVec3.Invalid);
        }
    }

    /// <summary>Save-persistent skyfaller holder for the exact fighter between physical passes.</summary>
    public sealed class Skyfaller_GoauldDeathGliderAttackPass : Skyfaller
    {
        private bool passResolved;
        private bool continueFlightAfterPass;
        private Thing emergencyCraft;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref passResolved, "wngDeathGliderPassResolved", false);
            Scribe_Values.Look(ref continueFlightAfterPass, "wngDeathGliderContinueAfterPass", false);
            Scribe_Deep.Look(ref emergencyCraft, "wngDeathGliderEmergencyCraft");
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

            CompGoauldDeathGliderMission mission = craft.TryGetComp<CompGoauldDeathGliderMission>();
            bool committed = false;
            try
            {
                if (!passResolved)
                {
                    continueFlightAfterPass = mission != null && mission.ExecutePhysicalPass(map, passCell);
                    passResolved = true;
                }

                if (continueFlightAfterPass)
                {
                    IntVec3 nextCell = mission != null
                        ? mission.FindNextAttackPassCell(map, passCell)
                        : GoauldDeathGliderFlightUtility.FindAttackPassCell(map, passCell, oppositeSide: true);
                    committed = GoauldDeathGliderFlightUtility.SpawnAttackPass(craft, map, nextCell);
                }
                else if (mission?.ShouldWithdrawAfterFinalPass == true)
                {
                    committed = GoauldDeathGliderFlightUtility.TryBeginHostileWithdrawal(craft, map, passCell, mission);
                }
                if (!committed)
                    committed = LandExactCraft(craft, map, passCell, mission);
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Death Glider physical-pass transition threw before exact-craft commit: " + ex);
                committed = LandExactCraft(craft, map, passCell, mission);
            }

            if (!committed)
            {
                if (!innerContainer.TryAdd(craft))
                {
                    if (craft.ParentHolder != null)
                    {
                        Log.Warning("[WNG] Exact Death Glider already has another real holder; preserving that owner.");
                        Destroy(DestroyMode.Vanish);
                        return;
                    }
                    emergencyCraft = craft;
                    Log.Error("[WNG] Exact Death Glider could neither land nor re-enter the attack-pass holder; retaining it by deep save and retrying.");
                }
                ticksToImpact = 60;
                return;
            }

            Destroy(DestroyMode.Vanish);
        }

        private static bool LandExactCraft(Thing craft, Map map, IntVec3 near, CompGoauldDeathGliderMission mission)
        {
            if (craft == null || craft.Destroyed || map == null)
                return false;
            if (craft.Spawned)
                return true;

            IntVec3 cell = mission != null ? mission.FindReturnLandingCell(map, near) : GoauldDeathGliderFlightUtility.FindLandingCell(craft, map, near);
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
                Log.Warning("[WNG] Death Glider exact-craft landing failed before commit: " + ex.Message);
            }
            return false;
        }
    }

    public static class GoauldDeathGliderFlightUtility
    {
        private const string AttackPassDefName = "WNG_GoauldDeathGliderAttackPass";

        public static bool TryBeginPhysicalPass(Thing craft, Map map, IntVec3 passCell)
        {
            if (craft == null || map == null || !passCell.IsValid || !passCell.InBounds(map))
                return false;

            bool wasSpawned = craft.Spawned;
            IntVec3 oldCell = wasSpawned ? craft.Position : IntVec3.Invalid;
            Rot4 oldRotation = craft.Rotation;
            if (wasSpawned)
                craft.DeSpawn();

            if (SpawnAttackPass(craft, map, passCell))
                return true;

            if (!craft.Spawned && oldCell.IsValid && oldCell.InBounds(map))
            {
                IntVec3 rollbackCell = FindLandingCell(craft, map, oldCell);
                if (rollbackCell.IsValid && rollbackCell.InBounds(map))
                {
                    try { GenSpawn.Spawn(craft, rollbackCell, map, oldRotation, WipeMode.VanishOrMoveAside); }
                    catch (Exception ex) { Log.Error("[WNG] Death Glider first-pass rollback could not respawn the exact craft: " + ex); }
                }
            }
            return false;
        }

        public static bool SpawnAttackPass(Thing craft, Map map, IntVec3 passCell)
        {
            ThingDef skyfallerDef = DefDatabase<ThingDef>.GetNamedSilentFail(AttackPassDefName);
            if (skyfallerDef == null || craft == null || craft.Destroyed || craft.Spawned || map == null)
                return false;

            if (!passCell.IsValid || !passCell.InBounds(map))
                passCell = FindPassCell(map, map.Center);
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
                Log.Warning("[WNG] Death Glider attack-pass transition failed before commit: " + ex.Message);
                return false;
            }
        }


        public static bool TryBeginHostileWithdrawal(Thing craft, Map map, IntVec3 departureCell, CompGoauldDeathGliderMission mission)
        {
            if (craft == null || craft.Destroyed || craft.Spawned || map == null || mission == null ||
                !mission.ShouldWithdrawAfterFinalPass)
                return false;

            CompTransporter transporter = craft.TryGetComp<CompTransporter>();
            CompLaunchable launchable = craft.TryGetComp<CompLaunchable>();
            ThingDef leavingDef = launchable?.Props?.skyfallerLeaving;
            if (transporter?.innerContainer == null || leavingDef == null)
                return false;

            ActiveTransporter activeTransporter = null;
            FlyShipLeaving leaving = null;
            bool committed = false;
            try
            {
                ThingDef activeDef = launchable.Props.activeTransporterDef ?? ThingDefOf.ActiveDropPod;
                activeTransporter = ThingMaker.MakeThing(activeDef) as ActiveTransporter;
                if (activeTransporter == null)
                    return false;

                activeTransporter.Contents = new ActiveTransporterInfo();
                activeTransporter.Contents.sentTransporterDef = craft.def;
                activeTransporter.Rotation = craft.Rotation;

                // Keep the exact crew/cargo and exact fighter together inside Odyssey's native
                // active-transporter holder. Nothing is destroyed merely to represent escape.
                activeTransporter.Contents.innerContainer.TryAddRangeOrTransfer(
                    transporter.GetDirectlyHeldThings(), canMergeWithExistingStacks: true, destroyLeftover: false);
                if (transporter.innerContainer.Any)
                    throw new InvalidOperationException("Could not transfer all exact Death Glider contents into the native leaving holder.");

                activeTransporter.Contents.SetShuttle(craft);
                if (craft.ParentHolder != activeTransporter.Contents || craft.Spawned)
                    throw new InvalidOperationException("The exact Death Glider did not enter the native leaving holder.");

                leaving = SkyfallerMaker.MakeSkyfaller(leavingDef, activeTransporter) as FlyShipLeaving;
                if (leaving == null)
                    throw new InvalidOperationException("Could not create the native Death Glider leaving skyfaller.");

                leaving.groupID = -1;
                leaving.createWorldObject = false;
                leaving.Rotation = craft.Rotation;
                Thing spawned = GenSpawn.Spawn(leaving, departureCell, map, WipeMode.Vanish);
                committed = spawned?.Spawned == true;
                if (!committed)
                    throw new InvalidOperationException("The native Death Glider leaving skyfaller did not spawn.");

                mission.NotifyPhysicalWithdrawalCommitted();
                return true;
            }
            catch (Exception ex)
            {
                // A spawned leaving skyfaller is already the physical commit boundary. Never pull
                // the exact craft back out after that point, even if presentation/state cleanup throws.
                if (leaving?.Spawned == true)
                {
                    mission.NotifyPhysicalWithdrawalCommitted();
                    Log.Error("[WNG] Hostile Death Glider withdrawal threw after physical departure commit; preserving the native leaving holder: " + ex);
                    return true;
                }

                Log.Warning("[WNG] Hostile Death Glider withdrawal failed before physical commit; landing the exact craft instead: " + ex.Message);
                try
                {
                    if (leaving?.innerContainer != null && activeTransporter != null && leaving.innerContainer.Contains(activeTransporter))
                        leaving.innerContainer.Remove(activeTransporter);
                    if (leaving != null && !leaving.Destroyed)
                        leaving.Destroy(DestroyMode.Vanish);

                    if (activeTransporter?.Contents != null)
                    {
                        Thing heldShuttle = activeTransporter.Contents.GetShuttle();
                        if (heldShuttle == craft)
                            activeTransporter.Contents.RemoveShuttle();
                        activeTransporter.Contents.innerContainer.TryTransferAllToContainer(
                            transporter.innerContainer, canMergeWithExistingStacks: false);
                    }
                    if (activeTransporter != null && !activeTransporter.Destroyed && activeTransporter.ParentHolder == null)
                        activeTransporter.Destroy(DestroyMode.Vanish);
                }
                catch (Exception rollbackEx)
                {
                    Log.Error("[WNG] Death Glider withdrawal rollback encountered an error while preserving exact objects: " + rollbackEx);
                }
                return false;
            }
        }

        public static IntVec3 FindAttackPassCellNear(Map map, IntVec3 focus)
        {
            if (map == null)
                return focus;
            return FindPassCell(map, focus);
        }

        public static IntVec3 FindAttackPassCell(Map map, IntVec3 origin, bool oppositeSide)
        {
            if (map == null)
                return origin;
            IntVec3 preferred;
            if (oppositeSide)
                preferred = new IntVec3(map.Size.x - 1 - origin.x, 0, map.Size.z - 1 - origin.z);
            else
            {
                int x = origin.x < map.Size.x / 2 ? Math.Min(map.Size.x - 2, Math.Max(1, map.Size.x * 3 / 4)) : Math.Max(1, map.Size.x / 4);
                int z = origin.z < map.Size.z / 2 ? Math.Min(map.Size.z - 2, Math.Max(1, map.Size.z * 3 / 4)) : Math.Max(1, map.Size.z / 4);
                preferred = new IntVec3(x, 0, z);
            }
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
