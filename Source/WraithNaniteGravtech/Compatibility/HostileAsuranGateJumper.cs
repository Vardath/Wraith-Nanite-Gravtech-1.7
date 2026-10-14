using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_HostileGateJumperIngress : CompProperties
    {
        public int cloakTicks = 900;
        public int deployRetryTicks = 90;
        public int maximumDeployRetries = 20;

        public CompProperties_HostileGateJumperIngress()
        {
            compClass = typeof(CompHostileGateJumperIngress);
        }
    }

    /// <summary>
    /// Holds the exact hostile Asuran crew inside the same native NPC Jumper while the finite cloak
    /// covers gate ingress. Crew deployment is transactional: if any later exact pawn cannot be
    /// rematerialized, all already-dropped crew are returned to this same transporter before retry.
    /// </summary>
    public sealed class CompHostileGateJumperIngress : ThingComp
    {
        private bool active;
        private Thing sourceGate;
        private int deployAtTick = -1;
        private int nextRetryTick = -1;
        private int deployRetries;
        private List<Pawn> deployedCrew = new List<Pawn>();

        public CompProperties_HostileGateJumperIngress Props =>
            (CompProperties_HostileGateJumperIngress)props;

        public bool Active => active;
        public Thing SourceGate => sourceGate;
        public IEnumerable<Pawn> DeployedCrew =>
            (deployedCrew ?? new List<Pawn>())
                .Where(p => p != null && !p.Dead && !p.Destroyed);

        public bool Configure(Thing exactGate)
        {
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null ||
                exactGate == null || exactGate.Destroyed || !exactGate.Spawned || exactGate.Map != parent.Map ||
                parent.Faction == null || Faction.OfPlayer == null || !parent.Faction.HostileTo(Faction.OfPlayer))
                return false;

            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            CompPuddleJumperSystems systems = parent.TryGetComp<CompPuddleJumperSystems>();
            if (transporter == null || systems == null)
                return false;

            List<Pawn> crew = transporter.innerContainer.OfType<Pawn>().Where(p => p != null && !p.Dead).ToList();
            if (crew.Count == 0 || !crew.Any(AncientCompatibilityUtility.IsCompatible))
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            int cloakTicks = Math.Max(250, Props.cloakTicks);
            if (!systems.BeginHostileIngressCloak(cloakTicks))
                return false;

            sourceGate = exactGate;
            deployedCrew ??= new List<Pawn>();
            deployedCrew.Clear();
            deployAtTick = Math.Min(int.MaxValue, now + cloakTicks);
            nextRetryTick = deployAtTick;
            deployRetries = 0;
            active = true;
            return true;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!active || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                return;
            if (!parent.IsHashIntervalTick(30))
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < deployAtTick || now < nextRetryTick)
                return;

            if (TryDeployCrew())
            {
                active = false;
                deployAtTick = -1;
                nextRetryTick = -1;
                deployRetries = 0;
                return;
            }

            deployRetries = Math.Min(Math.Max(1, Props.maximumDeployRetries), deployRetries + 1);
            if (deployRetries >= Math.Max(1, Props.maximumDeployRetries))
            {
                active = false;
                deployAtTick = -1;
                nextRetryTick = -1;
                try
                {
                    Messages.Message(
                        "The hostile Puddle Jumper cannot safely deploy its boarding party and remains sealed with its exact crew aboard.",
                        parent,
                        MessageTypeDefOf.ThreatSmall,
                        historical: false);
                }
                catch { }
                return;
            }

            nextRetryTick = Math.Min(int.MaxValue, now + Math.Max(30, Props.deployRetryTicks));
        }

        private bool TryDeployCrew()
        {
            Map map = parent.Map;
            Faction faction = parent.Faction;
            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            if (map == null || faction == null || transporter == null)
                return false;

            List<Pawn> crew = transporter.innerContainer.OfType<Pawn>()
                .Where(p => p != null && !p.Dead && !p.Destroyed)
                .OrderBy(p => p.thingIDNumber)
                .ToList();
            if (crew.Count == 0)
                return true;

            List<Pawn> dropped = new List<Pawn>();
            try
            {
                foreach (Pawn pawn in crew)
                {
                    Thing rematerialized;
                    if (!transporter.innerContainer.TryDrop(
                            pawn,
                            parent.Position,
                            map,
                            ThingPlaceMode.Near,
                            1,
                            out rematerialized) ||
                        rematerialized != pawn ||
                        !pawn.Spawned ||
                        pawn.Map != map)
                    {
                        throw new InvalidOperationException("an exact hostile Jumper crew member could not be rematerialized");
                    }
                    dropped.Add(pawn);
                }
            }
            catch (Exception ex)
            {
                foreach (Pawn pawn in dropped.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (pawn == null || pawn.Destroyed)
                            continue;
                        if (pawn.Spawned)
                            pawn.DeSpawn(DestroyMode.Vanish);
                        if (!transporter.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false) &&
                            !pawn.Spawned && !pawn.Destroyed)
                        {
                            GenSpawn.Spawn(pawn, parent.Position, map);
                        }
                    }
                    catch (Exception rollback)
                    {
                        Log.Error("[WNG] Hostile gate-Jumper crew rollback failed for " + pawn + ": " + rollback);
                    }
                }
                Log.Warning("[WNG] Hostile gate-Jumper deployment remained uncommitted: " + ex.Message);
                return false;
            }

            // Every exact crew pawn is now physically committed to the map. Preserve these
            // references so later mission systems can distinguish this gate-arrived team from
            // unrelated Asuran raids on the same map.
            deployedCrew = crew
                .Where(p => p != null && !p.Dead && !p.Destroyed)
                .Distinct()
                .ToList();

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
                // The exact crew is already physically committed to the map. Do not delete or replace
                // them if Lord creation alone fails; ordinary hostile pawn AI remains safer.
                Log.Warning("[WNG] Hostile gate-Jumper crew deployed but native assault Lord creation failed: " + ex.Message);
            }

            try
            {
                Messages.Message(
                    "The concealed Puddle Jumper resolves into view and its Asuran boarding party deploys from the Stargate perimeter.",
                    parent,
                    MessageTypeDefOf.ThreatBig,
                    historical: false);
            }
            catch { }
            return true;
        }

        public override string CompInspectStringExtra()
        {
            if (!active)
                return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            int ticks = Math.Max(0, deployAtTick - now);
            return "Hostile gate ingress: concealed\nBoarding party deployment: " +
                   Math.Ceiling(ticks / 60f).ToString("0") + " s";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref active, "wngHostileGateJumperActive", false);
            Scribe_References.Look(ref sourceGate, "wngHostileGateJumperSourceGate");
            Scribe_Values.Look(ref deployAtTick, "wngHostileGateJumperDeployAt", -1);
            Scribe_Values.Look(ref nextRetryTick, "wngHostileGateJumperNextRetry", -1);
            Scribe_Values.Look(ref deployRetries, "wngHostileGateJumperDeployRetries", 0);
            Scribe_Collections.Look(
                ref deployedCrew,
                "wngHostileGateJumperDeployedCrew",
                LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                deployRetries = Math.Max(0, Math.Min(Math.Max(1, Props.maximumDeployRetries), deployRetries));
                deployedCrew = (deployedCrew ?? new List<Pawn>())
                    .Where(p => p != null && !p.Dead && !p.Destroyed)
                    .Distinct()
                    .ToList();
                if (active && (sourceGate == null || sourceGate.Destroyed || !sourceGate.Spawned || sourceGate.Map != parent.Map))
                    sourceGate = null; // deployment can still complete from the already-present exact craft.
            }
        }
    }

    public sealed class IncidentWorker_HostileAsuranGateJumper : IncidentWorker
    {
        private const string HostileFactionDefName = "WNG_PrecursorCollective";
        private const string JumperDefName = "WNG_PuddleJumper_NPC";
        private const string CommanderKindDefName = "WNG_PrecursorCommander";
        private const string SoldierKindDefName = "WNG_PrecursorSoldier";

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms?.target is Map map) || !map.IsPlayerHome || Faction.OfPlayer == null)
                return false;

            Faction faction = ResolveFaction();
            if (faction == null || faction.defeated || !faction.HostileTo(Faction.OfPlayer))
                return false;

            ThingDef jumperDef = DefDatabase<ThingDef>.GetNamedSilentFail(JumperDefName);
            if (jumperDef == null || map.listerThings.ThingsOfDef(jumperDef).Any(t => t != null && !t.Destroyed))
                return false;

            return TryResolveExactGate(map, faction, out _, out _) && base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms?.target is Map map) || !map.IsPlayerHome)
                return false;

            Faction faction = ResolveFaction();
            if (faction == null || faction.defeated || Faction.OfPlayer == null || !faction.HostileTo(Faction.OfPlayer))
                return false;

            if (!TryResolveExactGate(map, faction, out Thing gate, out IntVec3 gateCell))
                return false;

            ThingDef jumperDef = DefDatabase<ThingDef>.GetNamedSilentFail(JumperDefName);
            PawnKindDef commanderKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(CommanderKindDefName);
            PawnKindDef soldierKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(SoldierKindDefName);
            if (jumperDef == null || commanderKind == null || soldierKind == null)
                return false;

            Building_PassengerShuttle jumper = ThingMaker.MakeThing(jumperDef) as Building_PassengerShuttle;
            if (jumper == null)
                return false;

            List<Pawn> generatedCrew = new List<Pawn>();
            bool placed = false;
            try
            {
                jumper.SetFaction(faction);
                CompTransporter transporter = jumper.TryGetComp<CompTransporter>();
                CompRefuelable fuel = jumper.TryGetComp<CompRefuelable>();
                CompPuddleJumperSystems systems = jumper.TryGetComp<CompPuddleJumperSystems>();
                CompHostileGateJumperIngress mission = jumper.TryGetComp<CompHostileGateJumperIngress>();
                if (transporter == null || fuel == null || systems == null || mission == null)
                    throw new InvalidOperationException("hostile NPC Puddle Jumper is missing its native transporter/fuel/cloak/mission stack");

                generatedCrew.Add(PawnGenerator.GeneratePawn(commanderKind, faction));
                generatedCrew.Add(PawnGenerator.GeneratePawn(soldierKind, faction));
                generatedCrew.Add(PawnGenerator.GeneratePawn(soldierKind, faction));
                if (generatedCrew.Any(p => p == null))
                    throw new InvalidOperationException("one or more exact Asuran boarding-party pawns could not be generated");

                foreach (Pawn pawn in generatedCrew)
                {
                    if (!transporter.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false))
                        throw new InvalidOperationException("an exact Asuran boarding-party pawn could not enter the Jumper transporter");
                    transporter.Notify_ThingAdded(pawn);
                }

                fuel.Refuel(fuel.Props.fuelCapacity);

                if (!GenPlace.TryPlaceThing(jumper, gateCell, map, ThingPlaceMode.Near, rot: Rot4.East, squareRadius: 10))
                    throw new InvalidOperationException("no valid landing footprint exists beside the resolved CatCraft Stargate");
                placed = jumper.Spawned && jumper.Map == map;
                if (!placed)
                    throw new InvalidOperationException("hostile Puddle Jumper did not physically reach the map");

                if (!mission.Configure(gate))
                    throw new InvalidOperationException("hostile Puddle Jumper could not establish its bounded ingress cloak");
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Hostile gate-Jumper strike aborted before a safe mission commit: " + ex.Message);
                if (!placed)
                {
                    CompTransporter transporter = jumper.TryGetComp<CompTransporter>();
                    if (transporter != null)
                    {
                        foreach (Pawn pawn in generatedCrew.ToList())
                        {
                            if (pawn != null && pawn.holdingOwner == transporter.innerContainer)
                                transporter.innerContainer.Remove(pawn);
                            if (pawn != null && !pawn.Destroyed)
                                pawn.Destroy(DestroyMode.Vanish);
                        }
                    }
                    if (!jumper.Destroyed)
                        jumper.Destroy(DestroyMode.Vanish);
                    return false;
                }

                // Once the exact craft and crew are physically committed to the map/holder, preserve
                // them rather than manufacturing replacements. The player can still engage the craft.
                try
                {
                    Messages.Message(
                        "A hostile Asuran Puddle Jumper materialized beside the Stargate, but its ingress sequence faulted after physical arrival.",
                        jumper,
                        MessageTypeDefOf.ThreatSmall,
                        historical: false);
                }
                catch { }
                return true;
            }

            parms.faction = faction;
            try
            {
                SendStandardLetter(def.letterLabel, def.letterText, def.letterDef, parms, jumper);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Hostile gate-Jumper strike committed but presentation failed: " + ex.Message);
            }
            return true;
        }

        private static bool TryResolveExactGate(Map map, Faction faction, out Thing gate, out IntVec3 gateCell)
        {
            gate = null;
            gateCell = IntVec3.Invalid;
            PawnsArrivalModeDef mode = QuietLatticeStargateVisitUtility.StargateArrivalMode;
            if (map == null || faction == null || mode?.Worker == null)
                return false;

            IncidentParms probe = new IncidentParms
            {
                target = map,
                faction = faction,
                raidArrivalMode = mode
            };

            try
            {
                if (!mode.Worker.TryResolveRaidSpawnCenter(probe) || probe.raidArrivalMode != mode)
                    return false;
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Hostile Asuran Stargate probe failed safely: " + ex.Message);
                return false;
            }

            gateCell = probe.spawnCenter;
            if (!gateCell.IsValid || !gateCell.InBounds(map))
                return false;

            List<Thing> things = map.thingGrid.ThingsListAtFast(gateCell);
            gate = things.FirstOrDefault(t =>
                t?.Map == map &&
                string.Equals(
                    t.def?.thingClass?.FullName,
                    QuietLatticeStargateVisitUtility.StargateThingClassName,
                    StringComparison.Ordinal));
            return gate != null;
        }

        private static Faction ResolveFaction()
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(HostileFactionDefName);
            return def == null ? null : Find.FactionManager?.FirstFactionOfDef(def);
        }
    }
}
