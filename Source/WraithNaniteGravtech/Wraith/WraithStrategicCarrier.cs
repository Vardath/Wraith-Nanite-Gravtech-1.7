using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WraithStrategicCarrierMission : CompProperties
    {
        public string leavingDefName;
        public int deployDelayTicks = 240;
        public int deployRetryTicks = 120;
        public int maximumDeployRetries = 20;
        public int retreatDelayTicks = 9000;

        public CompProperties_WraithStrategicCarrierMission()
        {
            compClass = typeof(CompWraithStrategicCarrierMission);
        }
    }

    /// <summary>
    /// NPC-only Wraith strategic carrier lifecycle. Exact generated crew remain inside the native
    /// Odyssey transporter until the physical craft has landed. Deployment is all-or-none for each
    /// attempt; partial drops are rolled back into the same carrier. Once the crew is committed to
    /// the map, the surviving empty carrier withdraws through its real leaving skyfaller.
    /// </summary>
    public sealed class CompWraithStrategicCarrierMission : ThingComp
    {
        private bool missionActive;
        private bool crewDeployed;
        private int deployAtTick = -1;
        private int nextDeployAttemptTick = -1;
        private int deployRetries;
        private int retreatAtTick = -1;

        private CompProperties_WraithStrategicCarrierMission Props =>
            (CompProperties_WraithStrategicCarrierMission)props;

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        public bool Configure()
        {
            if (missionActive || parent == null || parent.Destroyed)
                return false;

            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            if (transporter == null ||
                transporter.innerContainer.OfType<Pawn>().All(p => p == null || p.Dead))
                return false;

            missionActive = true;
            crewDeployed = false;
            deployAtTick = -1;
            nextDeployAttemptTick = -1;
            deployRetries = 0;
            retreatAtTick = -1;
            return true;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!missionActive || crewDeployed || parent?.Map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (deployAtTick < 0)
                deployAtTick = SafeFutureTick(now, Math.Max(60, Props.deployDelayTicks));
            if (nextDeployAttemptTick < 0)
                nextDeployAttemptTick = deployAtTick;
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!missionActive ||
                parent == null ||
                parent.Destroyed ||
                !parent.Spawned ||
                parent.Map == null ||
                !parent.IsHashIntervalTick(30))
                return;

            int now = Find.TickManager?.TicksGame ?? 0;

            if (!crewDeployed)
            {
                if (deployAtTick < 0)
                    deployAtTick = SafeFutureTick(now, Math.Max(60, Props.deployDelayTicks));
                if (nextDeployAttemptTick < 0)
                    nextDeployAttemptTick = deployAtTick;
                if (now < deployAtTick || now < nextDeployAttemptTick)
                    return;

                if (TryDeployExactCrew())
                {
                    crewDeployed = true;
                    retreatAtTick = SafeFutureTick(now, Math.Max(600, Props.retreatDelayTicks));
                    return;
                }

                deployRetries++;
                if (deployRetries >= Math.Max(1, Props.maximumDeployRetries))
                {
                    // Preserve the exact crew in the exact carrier. Do not auto-withdraw a holder
                    // whose Pawns never safely materialised; the hostile craft remains a real map
                    // objective rather than deleting or proxying those Pawns.
                    missionActive = false;
                    Log.Warning("[WNG] Strategic Wraith carrier could not safely deploy its exact crew; carrier remains on-map with crew intact.");
                    return;
                }

                nextDeployAttemptTick =
                    SafeFutureTick(now, Math.Max(30, Props.deployRetryTicks));
                return;
            }

            if (retreatAtTick >= 0 && now >= retreatAtTick)
                TryWithdraw();
        }

        private bool TryDeployExactCrew()
        {
            Map map = parent.Map;
            Faction faction = parent.Faction;
            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            if (map == null || faction == null || transporter == null)
                return false;

            List<Pawn> crew = transporter.innerContainer
                .OfType<Pawn>()
                .Where(p => p != null && !p.Dead && !p.Destroyed)
                .OrderBy(p => p.thingIDNumber)
                .ToList();
            if (crew.Count == 0)
                return false;

            List<Pawn> dropped = new List<Pawn>();
            try
            {
                foreach (Pawn pawn in crew)
                {
                    Thing exact;
                    if (!transporter.innerContainer.TryDrop(
                            pawn,
                            parent.Position,
                            map,
                            ThingPlaceMode.Near,
                            1,
                            out exact) ||
                        exact != pawn ||
                        !pawn.Spawned ||
                        pawn.Map != map)
                    {
                        throw new InvalidOperationException(
                            "an exact Wraith strategic-carrier crew member could not be materialised");
                    }
                    dropped.Add(pawn);
                }
            }
            catch (Exception ex)
            {
                foreach (Pawn pawn in dropped.AsEnumerable().Reverse())
                {
                    if (pawn == null || pawn.Destroyed)
                        continue;

                    try
                    {
                        if (pawn.Spawned)
                            pawn.DeSpawn(DestroyMode.Vanish);
                        if (!transporter.innerContainer.TryAdd(
                                pawn,
                                canMergeWithExistingStacks: false) &&
                            !pawn.Spawned &&
                            !pawn.Destroyed)
                        {
                            GenSpawn.Spawn(pawn, parent.Position, map);
                        }
                    }
                    catch (Exception rollback)
                    {
                        Log.Error(
                            "[WNG] Strategic Wraith carrier crew rollback failed for " +
                            pawn + ": " + rollback);
                    }
                }

                Log.Warning(
                    "[WNG] Strategic Wraith carrier deployment remained uncommitted: " +
                    ex.Message);
                return false;
            }

            try
            {
                LordMaker.MakeNewLord(
                    faction,
                    new LordJob_AssaultColony(
                        faction,
                        canKidnap: false,
                        canTimeoutOrFlee: true,
                        sappers: false,
                        useAvoidGridSmart: false,
                        canSteal: false),
                    map,
                    crew);
            }
            catch (Exception ex)
            {
                // Exact crew is already committed to the map. Preserve them and let ordinary hostile
                // Pawn AI continue rather than trying to stuff them back into a now-committed carrier.
                Log.Warning(
                    "[WNG] Strategic Wraith carrier deployed exact crew but native assault-Lord assignment failed: " +
                    ex.Message);
            }

            try
            {
                Messages.Message(
                    parent.LabelCap + " has landed and deployed its exact Wraith assault crew.",
                    parent,
                    MessageTypeDefOf.ThreatSmall,
                    historical: false);
            }
            catch { }

            return true;
        }

        private void TryWithdraw()
        {
            Building_PassengerShuttle shuttle =
                parent as Building_PassengerShuttle;
            ThingDef leavingDef =
                DefDatabase<ThingDef>.GetNamedSilentFail(Props.leavingDefName);
            if (shuttle == null || leavingDef == null)
            {
                missionActive = false;
                return;
            }

            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            if (transporter != null &&
                transporter.innerContainer.OfType<Pawn>().Any(p => p != null && !p.Dead))
            {
                // Never make an untracked exact crew disappear via the generic leaving skyfaller.
                missionActive = false;
                return;
            }

            if (!WNGWraithDartDeparture.TryDepartWithoutWorldObject(
                    shuttle,
                    leavingDef,
                    out _,
                    out string failure))
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                retreatAtTick = SafeFutureTick(now, 600);
                Log.Warning(
                    "[WNG] Strategic Wraith carrier withdrawal failed before physical departure; retry scheduled: " +
                    failure);
                return;
            }

            missionActive = false;
            retreatAtTick = -1;
        }

        public override string CompInspectStringExtra()
        {
            if (!missionActive)
                return null;

            if (!crewDeployed)
                return "Strategic assault carrier: crew preparing to deploy.";

            int now = Find.TickManager?.TicksGame ?? 0;
            int remaining = Math.Max(0, retreatAtTick - now);
            return "Strategic assault carrier: crew deployed\nWithdrawal: " +
                   Math.Ceiling(remaining / 60f).ToString("0") + " s";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(ref missionActive, "wngStrategicCarrierMissionActive", false);
            Scribe_Values.Look(ref crewDeployed, "wngStrategicCarrierCrewDeployed", false);
            Scribe_Values.Look(ref deployAtTick, "wngStrategicCarrierDeployAt", -1);
            Scribe_Values.Look(ref nextDeployAttemptTick, "wngStrategicCarrierNextDeployAttempt", -1);
            Scribe_Values.Look(ref deployRetries, "wngStrategicCarrierDeployRetries", 0);
            Scribe_Values.Look(ref retreatAtTick, "wngStrategicCarrierRetreatAt", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                deployRetries = Math.Max(
                    0,
                    Math.Min(
                        Math.Max(1, Props.maximumDeployRetries),
                        deployRetries));
            }
        }
    }

    public static class WraithStrategicCarrierUtility
    {
        public const string StrikeCraftNpcDefName = "WNG_WraithStrikeCraft_NPC";
        public const string CruiserNpcDefName = "WNG_WraithCruiser_NPC";

        public static bool TryStageSupportCarrier(
            Map map,
            Faction faction,
            float raidPoints,
            float cruiserThreshold)
        {
            if (map == null ||
                faction == null ||
                faction.defeated ||
                !WraithLineageUtility.IsWraithLineage(faction))
                return false;

            bool cruiser =
                raidPoints >= Math.Max(100f, cruiserThreshold);
            string craftDefName =
                cruiser ? CruiserNpcDefName : StrikeCraftNpcDefName;
            string incomingDefName =
                cruiser ? "WNG_WraithCruiserIncoming" : "WNG_WraithStrikeCraftIncoming";

            ThingDef craftDef =
                DefDatabase<ThingDef>.GetNamedSilentFail(craftDefName);
            ThingDef incomingDef =
                DefDatabase<ThingDef>.GetNamedSilentFail(incomingDefName);
            if (craftDef == null || incomingDef == null)
                return false;

            if (map.listerThings.ThingsOfDef(craftDef)
                .Any(t => t != null && !t.Destroyed))
                return false;

            List<PawnKindDef> crewKinds =
                ResolveCrewKinds(cruiser);
            if (crewKinds.Count == 0)
                return false;

            IntVec3 cell;
            if (!TryFindLandingCell(map, craftDef, out cell))
                return false;

            Building_PassengerShuttle craft =
                ThingMaker.MakeThing(craftDef) as Building_PassengerShuttle;
            if (craft == null)
                return false;

            List<Pawn> generatedCrew = new List<Pawn>();
            bool physicallyCommitted = false;
            try
            {
                craft.SetFaction(faction);

                CompTransporter transporter =
                    craft.TryGetComp<CompTransporter>();
                CompRefuelable fuel =
                    craft.TryGetComp<CompRefuelable>();
                CompWraithStrategicCarrierMission mission =
                    craft.TryGetComp<CompWraithStrategicCarrierMission>();
                if (transporter == null ||
                    fuel == null ||
                    mission == null)
                {
                    throw new InvalidOperationException(
                        "NPC Wraith strategic carrier is missing transporter, fuel or mission state");
                }

                foreach (PawnKindDef kind in crewKinds)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                    if (pawn == null)
                        throw new InvalidOperationException(
                            "exact Wraith strategic-carrier crew generation returned null");

                    generatedCrew.Add(pawn);
                    if (!transporter.innerContainer.TryAdd(
                            pawn,
                            canMergeWithExistingStacks: false))
                    {
                        throw new InvalidOperationException(
                            "exact Wraith strategic-carrier crew could not enter native transporter");
                    }
                    transporter.Notify_ThingAdded(pawn);
                }

                fuel.Refuel(fuel.Props.fuelCapacity);

                if (!mission.Configure())
                    throw new InvalidOperationException(
                        "strategic-carrier mission state could not be armed");

                SkyfallerMaker.SpawnSkyfaller(
                    incomingDef,
                    craft,
                    cell,
                    map);
                physicallyCommitted =
                    craft.Spawned ||
                    craft.ParentHolder != null;

                if (!physicallyCommitted)
                {
                    throw new InvalidOperationException(
                        "strategic carrier did not enter a physical map/skyfaller holder");
                }

                return true;
            }
            catch (Exception ex)
            {
                physicallyCommitted =
                    physicallyCommitted ||
                    craft.Spawned ||
                    craft.ParentHolder != null;

                if (physicallyCommitted)
                {
                    Log.Warning(
                        "[WNG] Strategic Wraith carrier reported an exception after physical commit; preserving exact craft and crew: " +
                        ex.Message);
                    return true;
                }

                CompTransporter transporter =
                    craft.TryGetComp<CompTransporter>();
                if (transporter != null)
                {
                    foreach (Pawn pawn in generatedCrew.ToList())
                    {
                        if (pawn != null &&
                            pawn.holdingOwner == transporter.innerContainer)
                        {
                            transporter.innerContainer.Remove(pawn);
                        }

                        if (pawn != null &&
                            !pawn.Destroyed &&
                            !pawn.Spawned &&
                            pawn.ParentHolder == null)
                        {
                            pawn.Destroy(DestroyMode.Vanish);
                        }
                    }
                }

                if (!craft.Destroyed && craft.ParentHolder == null)
                    craft.Destroy(DestroyMode.Vanish);

                Log.Warning(
                    "[WNG] Strategic Wraith carrier failed before physical commit: " +
                    ex.Message);
                return false;
            }
        }

        private static List<PawnKindDef> ResolveCrewKinds(bool cruiser)
        {
            PawnKindDef hunter =
                DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithHunter");
            PawnKindDef warrior =
                DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithWarrior");
            PawnKindDef keeper =
                DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithKeeper");
            PawnKindDef commander =
                DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_WraithCommander");

            if (hunter == null || warrior == null)
                return new List<PawnKindDef>();

            if (!cruiser)
                return new List<PawnKindDef>
                {
                    warrior,
                    warrior,
                    hunter
                };

            if (keeper == null || commander == null)
                return new List<PawnKindDef>();

            return new List<PawnKindDef>
            {
                commander,
                keeper,
                warrior,
                warrior,
                hunter
            };
        }

        private static bool TryFindLandingCell(
            Map map,
            ThingDef craftDef,
            out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (map == null || craftDef == null)
                return false;

            int radius =
                Math.Max(
                    12,
                    Math.Min(map.Size.x, map.Size.z) / 2 - 12);

            foreach (IntVec3 cell in GenRadial
                         .RadialCellsAround(map.Center, radius, true)
                         .Where(c => c.InBounds(map))
                         .OrderByDescending(c => c.DistanceToSquared(map.Center)))
            {
                if (!ClearFootprint(
                        map,
                        cell,
                        craftDef,
                        Rot4.East))
                    continue;

                result = cell;
                return true;
            }

            return false;
        }

        private static bool ClearFootprint(
            Map map,
            IntVec3 center,
            ThingDef craftDef,
            Rot4 rot)
        {
            CellRect rect =
                GenAdj.OccupiedRect(
                    center,
                    rot,
                    craftDef.Size);

            foreach (IntVec3 cell in rect.Cells)
            {
                if (!cell.InBounds(map) ||
                    !cell.Walkable(map) ||
                    cell.GetEdifice(map) != null ||
                    cell.Roofed(map))
                    return false;

                if (map.thingGrid
                    .ThingsListAtFast(cell)
                    .Any(t => t.def.category == ThingCategory.Building))
                    return false;
            }

            return true;
        }
    }
}
