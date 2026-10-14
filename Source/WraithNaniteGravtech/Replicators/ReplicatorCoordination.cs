using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class ReplicatorCoordinationUtility
    {
        public const string ControllerDefName = "WNG_ReplicatorController";

        public static bool IsController(Pawn pawn)
        {
            return pawn?.def?.defName == ControllerDefName;
        }
    }

    public sealed class ReplicatorControllerCooldown : IExposable
    {
        public string domainId;
        public int untilTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref domainId, "domainId");
            Scribe_Values.Look(ref untilTick, "untilTick", 0);
        }
    }

    /// <summary>
    /// Map-local mature-swarm coordination. A Controller is a support side-form, not a sovereign
    /// owner and not a physical hierarchy rung. Its loss removes only efficiency bonuses; every
    /// surviving Replicator remains autonomous and keeps the ordinary consume-first contract.
    /// </summary>
    public sealed class MapComponent_ReplicatorCoordination : MapComponent
    {
        public const int ControllerFormationThreshold = 18;
        public const int FormationCheckIntervalTicks = 1200;
        public const int ReplacementCooldownTicks = 30000;
        public const float AssimilationFactor = 0.82f;
        public const float AssemblyFactor = 0.82f;

        private const int ControllerCacheTicks = 300;

        private readonly List<Pawn> controllers = new List<Pawn>();
        private int controllerCacheValidUntil;
        private int nextFormationCheckTick;
        private List<ReplicatorControllerCooldown> controllerReplacementCooldowns = new List<ReplicatorControllerCooldown>();

        public MapComponent_ReplicatorCoordination(Map map) : base(map) { }

        public float AssimilationFactorFor(Pawn pawn)
        {
            return EligibleForHostileCoordination(pawn) && HasFunctioningControllerFor(pawn)
                ? AssimilationFactor
                : 1f;
        }

        public float AssemblyFactorFor(Pawn pawn)
        {
            return EligibleForHostileCoordination(pawn) && HasFunctioningControllerFor(pawn)
                ? AssemblyFactor
                : 1f;
        }

        public bool HasFunctioningControllerFor(Pawn pawn)
        {
            if (pawn == null || string.IsNullOrEmpty(ReplicatorDomainUtility.DomainId(pawn)))
                return false;

            RefreshControllersIfNeeded();
            for (int i = 0; i < controllers.Count; i++)
            {
                Pawn controller = controllers[i];
                if (ControllerIsFunctioning(controller, pawn))
                    return true;
            }
            return false;
        }

        public void RegisterController(Pawn controller)
        {
            if (controller == null || controller.Map != map || controller.Dead || !ReplicatorCoordinationUtility.IsController(controller))
                return;

            if (!controllers.Contains(controller))
                controllers.Add(controller);
            controllerCacheValidUntil = SafeFutureTick(Find.TickManager?.TicksGame ?? 0, ControllerCacheTicks);
        }

        public void NotifyControllerLost(Pawn controller)
        {
            if (controller != null)
                controllers.Remove(controller);

            int now = Find.TickManager?.TicksGame ?? 0;
            string domainId = ReplicatorDomainUtility.DomainId(controller);
            if (!string.IsNullOrEmpty(domainId))
                SetReplacementCooldown(domainId, SafeFutureTick(now, ReplacementCooldownTicks));
            controllerCacheValidUntil = 0;

            if (map?.IsPlayerHome == true && controller != null)
            {
                Messages.Message(
                    "Replicator coordination disrupted. The swarm remains active, but assimilation and recombination are less efficient until a new controller can form.",
                    controller,
                    MessageTypeDefOf.PositiveEvent,
                    historical: true);
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextFormationCheckTick)
                return;

            nextFormationCheckTick = SafeFutureTick(now, FormationCheckIntervalTicks);
            RefreshControllers(force: true);

            TryFormController(now);
        }

        private void TryFormController(int now)
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            Dictionary<string, List<Pawn>> blocksByDomain = new Dictionary<string, List<Pawn>>();

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Faction == null || pawn.Faction == Faction.OfPlayer ||
                    ReplicatorCoordinationUtility.IsController(pawn) || !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                {
                    continue;
                }

                if (Faction.OfPlayer != null && !pawn.Faction.HostileTo(Faction.OfPlayer))
                    continue;

                string domainId = ReplicatorDomainUtility.DomainId(pawn);
                if (string.IsNullOrEmpty(domainId))
                    continue;
                if (!blocksByDomain.TryGetValue(domainId, out List<Pawn> list))
                {
                    list = new List<Pawn>();
                    blocksByDomain.Add(domainId, list);
                }
                list.Add(pawn);
            }

            foreach (KeyValuePair<string, List<Pawn>> pair in blocksByDomain)
            {
                string domainId = pair.Key;
                List<Pawn> blocks = pair.Value;
                if (blocks.Count < ControllerFormationThreshold || IsReplacementBlocked(domainId, now) || HasFunctioningControllerFor(blocks[0]))
                    continue;

                Pawn source = null;
                for (int pass = 0; pass < 2 && source == null; pass++)
                {
                    string wanted = pass == 0 ? "WNG_ReplicatorHunter" : "WNG_ReplicatorBulwark";
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        Pawn candidate = blocks[i];
                        if (candidate.def?.defName != wanted || candidate.Downed ||
                            ReplicatorInterferenceUtility.IsEmpDisrupted(candidate) ||
                            TemporaryAsuranIntrusionUtility.IsCommandSuppressed(candidate) ||
                            ReplicatorContainmentUtility.IsContained(map, candidate.Position))
                        {
                            continue;
                        }

                        if (source == null || candidate.thingIDNumber < source.thingIDNumber)
                            source = candidate;
                    }
                }

                if (source != null && TryConvertToController(source))
                    return; // one controller formation per bounded map check
            }
        }

        private bool TryConvertToController(Pawn source)
        {
            PawnKindDef controllerKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(ReplicatorCoordinationUtility.ControllerDefName);
            if (source == null || source.Dead || !source.Spawned || source.Map != map || source.Faction == null || controllerKind == null)
                return false;

            Pawn controller = null;
            try
            {
                controller = PawnGenerator.GeneratePawn(controllerKind, source.Faction);
                ReplicatorDomainUtility.CopyDomain(source, controller);
                TemporaryAsuranIntrusionUtility.CopyState(source, controller);
                ReplicatorSovereignControlUtility.CopyState(source, controller);
                controller.TryGetComp<CompReplicatorAdaptation>()?.InheritFrom(source.TryGetComp<CompReplicatorAdaptation>());
                controller.TryGetComp<CompReplicatorMaterialProfile>()?.InheritFrom(source.TryGetComp<CompReplicatorMaterialProfile>());

                // Place the replacement first so failure cannot consume the source. The source then
                // vanishes rather than dying, avoiding hierarchy split/leavings during conversion.
                if (!GenPlace.TryPlaceThing(
                        controller,
                        source.Position,
                        map,
                        ThingPlaceMode.Near,
                        null,
                        cell => !ReplicatorContainmentUtility.IsContained(map, cell)))
                {
                    if (!controller.Destroyed)
                        controller.Destroy(DestroyMode.Vanish);
                    return false;
                }

                source.Destroy(DestroyMode.Vanish);
                if (!source.Destroyed)
                {
                    if (!controller.Destroyed)
                        controller.Destroy(DestroyMode.Vanish);
                    return false;
                }

                RegisterController(controller);
                if (map.IsPlayerHome)
                {
                    Messages.Message(
                        "A mature Replicator swarm has reorganized one larger unit into a coordination controller. It accelerates replication but is not required for the swarm to survive.",
                        controller,
                        MessageTypeDefOf.ThreatSmall,
                        historical: true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Replicator controller formation failed: " + ex);
                if (controller != null && !controller.Destroyed)
                    controller.Destroy(DestroyMode.Vanish);
                return false;
            }
        }

        private bool ControllerIsFunctioning(Pawn controller, Pawn target)
        {
            return controller != null && target != null && !controller.Dead && !controller.Downed && controller.Spawned &&
                   controller.Map == map && target.Map == map && ReplicatorDomainUtility.SameDomain(controller, target) &&
                   !ReplicatorInterferenceUtility.IsEmpDisrupted(controller) &&
                   !TemporaryAsuranIntrusionUtility.IsCommandSuppressed(controller) &&
                   !ReplicatorContainmentUtility.IsContained(map, controller.Position);
        }

        private bool IsReplacementBlocked(string domainId, int now)
        {
            if (controllerReplacementCooldowns == null)
                controllerReplacementCooldowns = new List<ReplicatorControllerCooldown>();
            for (int i = controllerReplacementCooldowns.Count - 1; i >= 0; i--)
            {
                ReplicatorControllerCooldown c = controllerReplacementCooldowns[i];
                if (c == null || string.IsNullOrEmpty(c.domainId) || now >= c.untilTick)
                {
                    controllerReplacementCooldowns.RemoveAt(i);
                    continue;
                }
                if (c.domainId == domainId)
                    return true;
            }
            return false;
        }

        private void SetReplacementCooldown(string domainId, int untilTick)
        {
            if (string.IsNullOrEmpty(domainId))
                return;
            if (controllerReplacementCooldowns == null)
                controllerReplacementCooldowns = new List<ReplicatorControllerCooldown>();
            for (int i = 0; i < controllerReplacementCooldowns.Count; i++)
            {
                ReplicatorControllerCooldown c = controllerReplacementCooldowns[i];
                if (c?.domainId == domainId)
                {
                    c.untilTick = Math.Max(c.untilTick, untilTick);
                    return;
                }
            }
            controllerReplacementCooldowns.Add(new ReplicatorControllerCooldown { domainId = domainId, untilTick = untilTick });
        }

        private static bool EligibleForHostileCoordination(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Faction == null || pawn.Faction == Faction.OfPlayer ||
                ReplicatorCoordinationUtility.IsController(pawn) || !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
            {
                return false;
            }

            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn) ||
                TemporaryAsuranIntrusionUtility.IsCommandSuppressed(pawn))
                return false;
            if (pawn.Spawned && ReplicatorContainmentUtility.IsContained(pawn.Map, pawn.Position))
                return false;
            return true;
        }

        private void RefreshControllersIfNeeded()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now >= controllerCacheValidUntil)
                RefreshControllers(force: true);
        }

        private void RefreshControllers(bool force = false)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!force && now < controllerCacheValidUntil)
                return;

            controllers.Clear();
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (pawn != null && !pawn.Dead && pawn.Spawned && ReplicatorCoordinationUtility.IsController(pawn))
                    controllers.Add(pawn);
            }
            controllerCacheValidUntil = SafeFutureTick(now, ControllerCacheTicks);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextFormationCheckTick, "wngReplicatorControllerNextFormationCheck", 0);
            Scribe_Collections.Look(ref controllerReplacementCooldowns, "wngReplicatorControllerReplacementCooldowns", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && controllerReplacementCooldowns == null)
                controllerReplacementCooldowns = new List<ReplicatorControllerCooldown>();
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }

    public sealed class CompProperties_ReplicatorController : CompProperties
    {
        public CompProperties_ReplicatorController()
        {
            compClass = typeof(CompReplicatorController);
        }
    }

    public sealed class CompReplicatorController : ThingComp
    {
        private Pawn Pawn => parent as Pawn;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Pawn?.Map?.GetComponent<MapComponent_ReplicatorCoordination>()?.RegisterController(Pawn);
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            base.Notify_Killed(prevMap, dinfo);
            prevMap?.GetComponent<MapComponent_ReplicatorCoordination>()?.NotifyControllerLost(Pawn);
        }

        public override string CompInspectStringExtra()
        {
            return "Swarm controller: accelerates same-domain Replicator assimilation and recombination while functional. Destroying it disrupts coordination but does not disable the surviving swarm.";
        }
    }
}
