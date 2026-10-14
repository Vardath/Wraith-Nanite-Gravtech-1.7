using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public enum WNGGravshipFamily
    {
        None,
        Wraith,
        Asuran,
        Goauld
    }

    /// <summary>
    /// Real Odyssey grav engine behavior with WNG-owned presentation.
    /// Building_GravEngine hardcodes the vanilla floating orb and cooldown graphic,
    /// which visually overwrites every custom grav-engine Def that uses the vanilla class.
    /// This subclass keeps all native launch/substructure/facility logic while drawing the
    /// Def's own graphic (and its comps) without the hardcoded vanilla art layers.
    /// </summary>
    public sealed class Building_WNGGravEngine : Building_GravEngine
    {
        public override Graphic Graphic => DefaultGraphic;

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // Equivalent to ThingWithComps/Thing dynamic drawing, deliberately bypassing
            // Building_GravEngine.DrawAt's static vanilla GravEngine_Orb overlay.
            if (def.drawerType == DrawerType.RealtimeOnly || !Spawned)
                Graphic.Draw(drawLoc, flip ? Rotation.Opposite : Rotation, this);

            SilhouetteUtility.DrawGraphicSilhouette(this, drawLoc);
            Comps_DrawAt(drawLoc, flip);
            Comps_PostDraw();
        }
    }

    public enum WNGFamilyPowerRole
    {
        Conduit,
        Coupler,
        Generator,
        Consumer
    }

    public enum WNGFuelEndpointRole
    {
        Conduit,
        Tank,
        Thruster
    }

    public sealed class CompProperties_WNGFamilyPowerNode : CompProperties
    {
        public WNGGravshipFamily family = WNGGravshipFamily.None;
        public WNGFamilyPowerRole role = WNGFamilyPowerRole.Conduit;
        public float capacityWatts;
        public float demandWatts;

        public CompProperties_WNGFamilyPowerNode()
        {
            compClass = typeof(CompWNGFamilyPowerNode);
        }
    }

    public sealed class CompWNGFamilyPowerNode : ThingComp
    {
        public CompProperties_WNGFamilyPowerNode Props => (CompProperties_WNGFamilyPowerNode)props;

        public bool Powered => WNGFamilyPowerUtility.IsPowered(parent, Props.family);

        public override void CompTick()
        {
            base.CompTick();
            if (parent == null || !parent.Spawned || !parent.IsHashIntervalTick(60))
                return;

            if (Props.role == WNGFamilyPowerRole.Coupler)
            {
                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                if (power != null)
                {
                    float demand = WNGFamilyPowerUtility.NetworkDemand(parent, Props.family);
                    float draw = Math.Min(Math.Max(0f, Props.capacityWatts), Math.Max(0f, demand));
                    power.PowerOutput = -draw;
                }
            }
            else if (Props.role == WNGFamilyPowerRole.Generator)
            {
                if (WNGFamilyPowerUtility.NetworkDemand(parent, Props.family) > 0.01f)
                {
                    CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
                    if (fuel != null && fuel.HasFuel && fuel.Props.fuelConsumptionRate > 0f)
                    {
                        float amount = fuel.Props.fuelConsumptionRate * 60f / GenDate.TicksPerDay;
                        fuel.ConsumeFuel(Math.Min(amount, fuel.Fuel));
                    }
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent == null || !parent.Spawned || Props.family == WNGGravshipFamily.None)
                return null;

            List<CompWNGFamilyPowerNode> network = WNGFamilyPowerUtility.NetworkNodes(parent, Props.family);
            float supply = WNGFamilyPowerUtility.NetworkSupply(network);
            float demand = WNGFamilyPowerUtility.NetworkDemand(network);
            string label = Props.family == WNGGravshipFamily.Wraith ? "Wraith neural grid"
                : Props.family == WNGGravshipFamily.Asuran ? "Asuran power lattice"
                : Props.family == WNGGravshipFamily.Goauld ? "Goa'uld ship power grid"
                : "gravship power grid";
            return label + ": " + supply.ToString("0") + " / " + demand.ToString("0") + " W"
                + (WNGFamilyPowerUtility.NetworkPowered(network) ? "" : " (insufficient)");
        }
    }

    internal static class WNGFamilyPowerUtility
    {
        internal static List<CompWNGFamilyPowerNode> NetworkNodes(Thing origin, WNGGravshipFamily family)
        {
            List<CompWNGFamilyPowerNode> result = new List<CompWNGFamilyPowerNode>();
            if (origin == null || !origin.Spawned || origin.Map == null || family == WNGGravshipFamily.None)
                return result;

            Queue<Thing> open = new Queue<Thing>();
            HashSet<Thing> visited = new HashSet<Thing>();
            open.Enqueue(origin);
            visited.Add(origin);

            while (open.Count > 0)
            {
                Thing thing = open.Dequeue();
                CompWNGFamilyPowerNode node = thing.TryGetComp<CompWNGFamilyPowerNode>();
                if (node == null || node.Props.family != family)
                    continue;

                result.Add(node);
                foreach (IntVec3 occupied in thing.OccupiedRect())
                {
                    foreach (IntVec3 cell in GenAdj.CardinalDirections.Select(d => occupied + d).Prepend(occupied))
                    {
                        if (!cell.InBounds(thing.Map))
                            continue;
                        foreach (Thing next in thing.Map.thingGrid.ThingsListAt(cell))
                        {
                            if (next == null || next.Destroyed || !visited.Add(next))
                                continue;
                            CompWNGFamilyPowerNode nextNode = next.TryGetComp<CompWNGFamilyPowerNode>();
                            if (nextNode != null && nextNode.Props.family == family)
                                open.Enqueue(next);
                        }
                    }
                }
            }

            return result;
        }

        internal static float NetworkDemand(Thing origin, WNGGravshipFamily family)
        {
            return NetworkDemand(NetworkNodes(origin, family));
        }

        internal static float NetworkDemand(IEnumerable<CompWNGFamilyPowerNode> network)
        {
            return network.Where(n => n?.Props.role == WNGFamilyPowerRole.Consumer)
                .Sum(n => Math.Max(0f, n.Props.demandWatts));
        }

        internal static float NetworkSupply(IEnumerable<CompWNGFamilyPowerNode> network)
        {
            float supply = 0f;
            foreach (CompWNGFamilyPowerNode node in network)
            {
                if (node == null || node.parent == null || !node.parent.Spawned)
                    continue;

                if (node.Props.role == WNGFamilyPowerRole.Generator)
                {
                    CompRefuelable fuel = node.parent.TryGetComp<CompRefuelable>();
                    if ((fuel == null || fuel.HasFuel) && FlickUtility.WantsToBeOn(node.parent))
                        supply += Math.Max(0f, node.Props.capacityWatts);
                }
                else if (node.Props.role == WNGFamilyPowerRole.Coupler)
                {
                    CompPowerTrader power = node.parent.TryGetComp<CompPowerTrader>();
                    if (power != null && power.PowerOn && FlickUtility.WantsToBeOn(node.parent))
                        supply += Math.Max(0f, node.Props.capacityWatts);
                }
            }
            return supply;
        }

        internal static bool NetworkPowered(IEnumerable<CompWNGFamilyPowerNode> network)
        {
            List<CompWNGFamilyPowerNode> list = network?.ToList() ?? new List<CompWNGFamilyPowerNode>();
            float demand = NetworkDemand(list);
            return demand <= 0.01f || NetworkSupply(list) + 0.01f >= demand;
        }

        internal static bool IsPowered(Thing origin, WNGGravshipFamily family)
        {
            List<CompWNGFamilyPowerNode> network = NetworkNodes(origin, family);
            return network.Count > 0 && NetworkPowered(network);
        }

        internal static bool ShouldPowerLinkTo(IntVec3 cell, Thing parent, WNGGravshipFamily family)
        {
            if (parent?.Map == null || !cell.InBounds(parent.Map))
                return false;

            foreach (Thing thing in parent.Map.thingGrid.ThingsListAt(cell))
            {
                CompWNGFamilyPowerNode node = thing?.TryGetComp<CompWNGFamilyPowerNode>();
                if (node != null && node.Props.family == family)
                    return true;

                // Vanilla linked walls/conduits join through blueprints and frames as well as
                // completed buildings. Resolve the Thing being built and inspect its Def-owned
                // family node so WNG conduit runs visibly join while they are being laid out.
                ThingDef builtDef = BuiltThingDefForLinking(thing);
                CompProperties_WNGFamilyPowerNode props =
                    builtDef?.GetCompProperties<CompProperties_WNGFamilyPowerNode>();
                if (props != null && props.family == family)
                    return true;
            }

            return false;
        }

        private static ThingDef BuiltThingDefForLinking(Thing thing)
        {
            if (thing?.def == null)
                return null;

            return thing.def.entityDefToBuild as ThingDef ?? thing.def;
        }
    }

    public abstract class Graphic_LinkedWNGFamilyPower : Graphic_Linked
    {
        protected abstract WNGGravshipFamily Family { get; }

        public override void Init(GraphicRequest req)
        {
            data = req.graphicData;
            path = req.path;
            maskPath = req.maskPath;
            color = req.color;
            colorTwo = req.colorTwo;
            drawSize = req.drawSize;
            subGraphic = GraphicDatabase.Get(
                typeof(Graphic_Single),
                req.path,
                req.shader,
                req.drawSize,
                req.color,
                req.colorTwo,
                req.graphicData,
                req.shaderParameters,
                req.maskPath);
        }

        public override bool ShouldLinkWith(IntVec3 c, Thing parent)
        {
            return WNGFamilyPowerUtility.ShouldPowerLinkTo(c, parent, Family);
        }
    }

    public sealed class Graphic_LinkedWraithFamilyPower : Graphic_LinkedWNGFamilyPower
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Wraith;

        public override Graphic GetColoredVersion(Shader newShader, UnityEngine.Color newColor, UnityEngine.Color newColorTwo)
        {
            return new Graphic_LinkedWraithFamilyPower
            {
                subGraphic = subGraphic.GetColoredVersion(newShader, newColor, newColorTwo),
                data = data
            };
        }
    }

    public sealed class Graphic_LinkedAsuranFamilyPower : Graphic_LinkedWNGFamilyPower
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Asuran;

        public override Graphic GetColoredVersion(Shader newShader, UnityEngine.Color newColor, UnityEngine.Color newColorTwo)
        {
            return new Graphic_LinkedAsuranFamilyPower
            {
                subGraphic = subGraphic.GetColoredVersion(newShader, newColor, newColorTwo),
                data = data
            };
        }
    }

    public sealed class Graphic_LinkedGoauldFamilyPower : Graphic_LinkedWNGFamilyPower
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Goauld;

        public override Graphic GetColoredVersion(Shader newShader, UnityEngine.Color newColor, UnityEngine.Color newColorTwo)
        {
            return new Graphic_LinkedGoauldFamilyPower
            {
                subGraphic = subGraphic.GetColoredVersion(newShader, newColor, newColorTwo),
                data = data
            };
        }
    }

    public sealed class CompProperties_WNGFuelEndpoint : CompProperties
    {
        public WNGGravshipFamily family = WNGGravshipFamily.None;
        public WNGFuelEndpointRole role = WNGFuelEndpointRole.Conduit;

        public CompProperties_WNGFuelEndpoint()
        {
            compClass = typeof(CompWNGFuelEndpoint);
        }
    }

    public sealed class CompWNGFuelEndpoint : ThingComp
    {
        public CompProperties_WNGFuelEndpoint Props => (CompProperties_WNGFuelEndpoint)props;
    }

    internal static class WNGFamilyFuelUtility
    {
        internal static List<CompWNGFuelEndpoint> NetworkNodes(Thing origin, WNGGravshipFamily family)
        {
            List<CompWNGFuelEndpoint> result = new List<CompWNGFuelEndpoint>();
            if (origin == null || !origin.Spawned || origin.Map == null || family == WNGGravshipFamily.None)
                return result;

            Queue<Thing> open = new Queue<Thing>();
            HashSet<Thing> visited = new HashSet<Thing>();
            open.Enqueue(origin);
            visited.Add(origin);

            while (open.Count > 0)
            {
                Thing thing = open.Dequeue();
                CompWNGFuelEndpoint node = thing.TryGetComp<CompWNGFuelEndpoint>();
                if (node == null || node.Props.family != family)
                    continue;

                result.Add(node);
                foreach (IntVec3 occupied in thing.OccupiedRect())
                {
                    foreach (IntVec3 cell in GenAdj.CardinalDirections.Select(d => occupied + d).Prepend(occupied))
                    {
                        if (!cell.InBounds(thing.Map))
                            continue;
                        foreach (Thing next in thing.Map.thingGrid.ThingsListAt(cell))
                        {
                            if (next == null || next.Destroyed || !visited.Add(next))
                                continue;
                            CompWNGFuelEndpoint nextNode = next.TryGetComp<CompWNGFuelEndpoint>();
                            if (nextNode != null && nextNode.Props.family == family)
                                open.Enqueue(next);
                        }
                    }
                }
            }

            return result;
        }

        internal static bool HasRoleConnection(Thing origin, WNGGravshipFamily family, WNGFuelEndpointRole role)
        {
            return NetworkNodes(origin, family).Any(n => n != null && n.parent != origin && n.Props.role == role);
        }

        internal static bool ShouldFuelLinkTo(IntVec3 cell, Thing parent, WNGGravshipFamily family)
        {
            if (parent?.Map == null || !cell.InBounds(parent.Map))
                return false;

            foreach (Thing thing in parent.Map.thingGrid.ThingsListAt(cell))
            {
                CompWNGFuelEndpoint node = thing?.TryGetComp<CompWNGFuelEndpoint>();
                if (node != null && node.Props.family == family)
                    return true;

                ThingDef builtDef = thing?.def?.entityDefToBuild as ThingDef ?? thing?.def;
                CompProperties_WNGFuelEndpoint props =
                    builtDef?.GetCompProperties<CompProperties_WNGFuelEndpoint>();
                if (props != null && props.family == family)
                    return true;
            }

            return false;
        }
    }

    public abstract class Graphic_LinkedWNGFuelConduit : Graphic_Linked
    {
        protected abstract WNGGravshipFamily Family { get; }

        public override void Init(GraphicRequest req)
        {
            data = req.graphicData;
            path = req.path;
            maskPath = req.maskPath;
            color = req.color;
            colorTwo = req.colorTwo;
            drawSize = req.drawSize;
            subGraphic = GraphicDatabase.Get(
                typeof(Graphic_Single),
                req.path,
                req.shader,
                req.drawSize,
                req.color,
                req.colorTwo,
                req.graphicData,
                req.shaderParameters,
                req.maskPath);
        }

        public override bool ShouldLinkWith(IntVec3 c, Thing parent)
        {
            return WNGFamilyFuelUtility.ShouldFuelLinkTo(c, parent, Family);
        }
    }

    public sealed class Graphic_LinkedWraithFuelConduit : Graphic_LinkedWNGFuelConduit
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Wraith;

        public override Graphic GetColoredVersion(Shader newShader, UnityEngine.Color newColor, UnityEngine.Color newColorTwo)
        {
            return new Graphic_LinkedWraithFuelConduit
            {
                subGraphic = subGraphic.GetColoredVersion(newShader, newColor, newColorTwo),
                data = data
            };
        }
    }

    public sealed class Graphic_LinkedAsuranFuelConduit : Graphic_LinkedWNGFuelConduit
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Asuran;

        public override Graphic GetColoredVersion(Shader newShader, UnityEngine.Color newColor, UnityEngine.Color newColorTwo)
        {
            return new Graphic_LinkedAsuranFuelConduit
            {
                subGraphic = subGraphic.GetColoredVersion(newShader, newColor, newColorTwo),
                data = data
            };
        }
    }

    public sealed class Graphic_LinkedGoauldFuelConduit : Graphic_LinkedWNGFuelConduit
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Goauld;

        public override Graphic GetColoredVersion(Shader newShader, UnityEngine.Color newColor, UnityEngine.Color newColorTwo)
        {
            return new Graphic_LinkedGoauldFuelConduit
            {
                subGraphic = subGraphic.GetColoredVersion(newShader, newColor, newColorTwo),
                data = data
            };
        }
    }

    internal static class WNGGravshipFamilyUtility
    {
        internal static string EngineDefName(WNGGravshipFamily family)
        {
            if (family == WNGGravshipFamily.Wraith) return "WNG_WraithGravEngine";
            if (family == WNGGravshipFamily.Asuran) return "WNG_AsuranGravEngine";
            if (family == WNGGravshipFamily.Goauld) return "WNG_GoauldGravEngine";
            return null;
        }

        internal static void EnsureExactEngineTarget(CompGravshipFacility facility, WNGGravshipFamily family)
        {
            if (!ModsConfig.OdysseyActive || facility?.props is not CompProperties_GravshipFacility props)
                return;

            string exactName = EngineDefName(family);
            ThingDef exactEngine = exactName.NullOrEmpty()
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(exactName);
            if (exactEngine == null)
                return;

            // Make every WNG gravship facility proactively link to its own family engine.
            // This mirrors the native facility path but removes spawn-order dependence on the
            // engine's reverse CompAffectedByFacilities scan.
            if (props.linkableBuildings == null ||
                props.linkableBuildings.Count != 1 ||
                props.linkableBuildings[0] != exactEngine)
            {
                props.linkableBuildings = new List<ThingDef> { exactEngine };
            }
        }

        internal static bool PhysicalEngineLinkIsValid(CompGravshipFacility facility, WNGGravshipFamily family)
        {
            if (!ModsConfig.OdysseyActive || facility?.parent?.Spawned != true || facility.parent.Map == null)
                return false;

            Building_GravEngine engine = facility.engine;
            string exactName = EngineDefName(family);
            if (engine == null || engine.Destroyed || !engine.Spawned || engine.Map != facility.parent.Map ||
                engine.def?.defName != exactName || !facility.LinkedBuildings.Contains(engine))
                return false;

            if (facility.parent.Faction != null && engine.Faction != null && facility.parent.Faction != engine.Faction)
                return false;

            // Do not call Building_GravEngine.OnValidSubstructure/ValidSubstructure here.
            // Those lazy properties can regenerate gravship draw layers and allocate Unity Meshes.
            // Scenario map generation runs on RimWorld's long-event worker thread, where touching
            // Unity graphics objects can hard-crash the process. The native facility link itself is
            // the authoritative relationship; final launch/substructure validation remains RimWorld's.
            return true;
        }

        internal static bool ExactEngineLinkIsValid(CompGravshipFacility facility, WNGGravshipFamily family, bool requiresPower)
        {
            if (!PhysicalEngineLinkIsValid(facility, family))
                return false;

            if (requiresPower)
            {
                CompWNGFamilyPowerNode power = facility.parent.TryGetComp<CompWNGFamilyPowerNode>();
                if (power != null && !power.Powered)
                    return false;
            }

            return true;
        }

        internal static AcceptanceReport RequireFamilySubstructure(
            IntVec3 loc, Rot4 rot, BuildableDef checkingDef, Map map,
            WNGGravshipFamily family, Thing thingToIgnore = null)
        {
            foreach (IntVec3 cell in GenAdj.OccupiedRect(loc, rot, checkingDef.Size))
            {
                TerrainDef foundation = map.terrainGrid.FoundationAt(cell);
                string required = family == WNGGravshipFamily.Wraith
                    ? "WNG_WraithGravshipSubstructure"
                    : family == WNGGravshipFamily.Asuran
                        ? "WNG_AsuranGravshipSubstructure"
                        : "WNG_GoauldGravshipSubstructure";
                string familyLabel = family == WNGGravshipFamily.Wraith ? "Wraith"
                    : family == WNGGravshipFamily.Asuran ? "Asuran"
                    : "Goa'uld";
                if (foundation?.defName != required)
                    return familyLabel + " gravship systems require same-family gravship substructure.";
            }
            return AcceptanceReport.WasAccepted;
        }
    }

    /// <summary>
    /// Exact-family definition layer for WNG pilot consoles.
    ///
    /// WNG still inherits RimWorld's CompPilotConsole. The thin runtime subclass below exists only
    /// to bridge Odyssey's two hard-coded PilotConsole ThingDef assumptions:
    /// 1) the launch ritual gizmo can be omitted for a custom pilot-console ThingDef even though the
    ///    real CompPilotConsole is linked and right-click piloting works;
    /// 2) Gravship.AddThing records its world-object pilot console only when the ThingDef is exactly
    ///    ThingDefOf.PilotConsole.
    ///
    /// All piloting, ritual setup, destination selection, launch validation and takeoff remain the
    /// native Odyssey implementations.
    /// </summary>
    public sealed class CompProperties_WNGPilotConsole : CompProperties_GravshipFacility
    {
        public WNGGravshipFamily family = WNGGravshipFamily.None;

        public CompProperties_WNGPilotConsole()
        {
            compClass = typeof(CompPilotConsole_WNGFamily);
        }

        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);

            string exactName = WNGGravshipFamilyUtility.EngineDefName(family);
            ThingDef exactEngine = exactName.NullOrEmpty()
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(exactName);

            if (exactEngine == null)
            {
                Log.Error("[WNG] Pilot console " + parentDef?.defName +
                          " could not resolve its exact grav engine for family " + family + ".");
                return;
            }

            linkableBuildings = new List<ThingDef> { exactEngine };
        }
    }

    /// <summary>
    /// Native Odyssey pilot console with one UI compatibility bridge for custom WNG ThingDefs.
    /// The command yielded here is RimWorld's own Command_Ritual from the real GravshipLaunch
    /// precept. If Thing.GetGizmos already emitted that ritual, this comp emits nothing extra.
    /// </summary>
    public sealed class CompPilotConsole_WNGFamily : CompPilotConsole
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (!parent.Spawned || engine == null || !ModsConfig.IdeologyActive)
                yield break;

            Faction player = Faction.OfPlayerSilentFail;
            if (player?.ideos == null)
                yield break;

            Precept_Ritual launchRitual = null;
            foreach (Ideo ideo in player.ideos.AllIdeos)
            {
                launchRitual = ideo?.GetPrecept(PreceptDefOf.GravshipLaunch) as Precept_Ritual;
                if (launchRitual != null)
                    break;
            }

            if (launchRitual == null)
                yield break;

            // Thing.GetGizmos populates this set when it successfully emitted the ritual already.
            // That keeps the bridge invisible on vanilla-compatible paths and prevents duplicates.
            if (launchRitual.sourcePattern != null &&
                Thing.showingGizmosForRitualsTmp.Contains(launchRitual.sourcePattern))
            {
                yield break;
            }

            foreach (Gizmo gizmo in launchRitual.GetGizmoFor(parent))
                yield return gizmo;
        }
    }

    /// <summary>
    /// Native bridges include WNG pilot consoles and cinematic family hull masks while preserving
    /// the vanilla launch mechanics and linked-wall renderer.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class WNGGravshipVanillaBridgeBootstrap
    {
        static WNGGravshipVanillaBridgeBootstrap()
        {
            Harmony harmony = new Harmony("vardath.wraithnanitegravtech.gravship-native-bridge");
            harmony.PatchAll(typeof(WNGGravshipVanillaBridgeBootstrap).Assembly);

            // WNG gravships are deliberately Odyssey-native. If Vanilla Gravship Expanded is
            // present, its global launch patches must not replace the launch sequence for WNG's
            // Wraith, Asuran or Goa'uld engines. Install a late, optional guard around the VGE
            // patch methods themselves; no VGE assembly reference or VGE behavior is required.
            WNGVGEVanillaLaunchIsolation.TryInstall(harmony);
            LongEventHandler.ExecuteWhenFinished(
                () => WNGVGEVanillaLaunchIsolation.TryInstall(harmony));
        }
    }

    /// <summary>
    /// Keeps every WNG gravship family on Odyssey's vanilla launch sequence even when another
    /// active mod globally patches that sequence.
    ///
    /// Player.log proved Vanilla Gravship Expanded was replacing PreLaunchConfirmation and then
    /// throwing from its Building_GravEngine.ConsumeFuel postfix after Odyssey had already applied
    /// fuel/cooldown. These guards skip only those VGE patch methods when the launch belongs to a
    /// Building_WNGGravEngine or CompPilotConsole_WNGFamily. Vanilla engines and VGE ships are
    /// untouched.
    ///
    /// This is intentionally reflection-only and optional: WNG has no compile-time dependency on
    /// VGE and does not adopt any VGE launch behavior.
    /// </summary>
    internal static class WNGVGEVanillaLaunchIsolation
    {
        private static bool installed;

        internal static void TryInstall(Harmony harmony)
        {
            if (installed || harmony == null)
                return;

            Type showRitual = AccessTools.TypeByName(
                "VanillaGravshipExpanded.Dialog_BeginRitual_ShowRitualBeginWindow_Patch");
            Type preLaunch = AccessTools.TypeByName(
                "VanillaGravshipExpanded.GravshipUtility_PreLaunchConfirmation_Patch");
            Type settle = AccessTools.TypeByName(
                "VanillaGravshipExpanded.SettlementProximityGoodwillUtility_CheckConfirmSettle_Patch");
            Type consumeFuel = AccessTools.TypeByName(
                "VanillaGravshipExpanded.Building_GravEngine_ConsumeFuel_Patch");
            Type drawOutcome = AccessTools.TypeByName(
                "VanillaGravshipExpanded.Dialog_BeginRitual_DrawExtraRitualOutcomeDescriptions_Patch");
            Type ritualBehavior = AccessTools.TypeByName(
                "VanillaGravshipExpanded.RitualBehaviorWorker_GravshipLaunch_TryExecuteOn_Patch");
            Type ritualOutcome = AccessTools.TypeByName(
                "VanillaGravshipExpanded.RitualOutcomeEffectWorker_GravshipLaunch_Apply_Patch");

            // VGE is optional. If its assembly is not active, there is nothing to isolate from.
            if (showRitual == null || preLaunch == null || settle == null || consumeFuel == null ||
                drawOutcome == null || ritualBehavior == null || ritualOutcome == null)
            {
                return;
            }

            var targets = new[]
            {
                AccessTools.Method(showRitual, "Prefix"),
                AccessTools.Method(preLaunch, "Prefix"),
                AccessTools.Method(settle, "Prefix"),
                AccessTools.Method(consumeFuel, "Prefix"),
                AccessTools.Method(consumeFuel, "Postfix"),
                AccessTools.Method(drawOutcome, "Postfix"),
                AccessTools.Method(ritualBehavior, "Postfix"),
                AccessTools.Method(ritualOutcome, "Postfix"),
            };

            if (targets.Any(method => method == null))
                return;

            PatchGuard(harmony, targets[0], nameof(GuardVgeShowRitualBeginWindow));
            PatchGuard(harmony, targets[1], nameof(GuardVgePreLaunchConfirmation));
            PatchGuard(harmony, targets[2], nameof(GuardVgeCheckConfirmSettle));
            PatchGuard(harmony, targets[3], nameof(GuardVgeConsumeFuel));
            PatchGuard(harmony, targets[4], nameof(GuardVgeConsumeFuel));
            PatchGuard(harmony, targets[5], nameof(GuardVgeDrawLaunchOutcome));
            PatchGuard(harmony, targets[6], nameof(GuardVgeRitualBehavior));
            PatchGuard(harmony, targets[7], nameof(GuardVgeRitualOutcome));

            installed = true;
        }

        private static void PatchGuard(Harmony harmony, System.Reflection.MethodBase target, string guardName)
        {
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(WNGVGEVanillaLaunchIsolation), guardName))
                {
                    priority = Priority.First
                });
        }

        private static bool IsWNGEngine(Building_GravEngine engine)
        {
            return engine is Building_WNGGravEngine;
        }

        private static bool IsWNGPilot(TargetInfo target)
        {
            return target.HasThing &&
                   target.Thing?.TryGetComp<CompPilotConsole_WNGFamily>() != null;
        }

        // We are patching VGE's bool Prefix method itself. Returning false skips the VGE method;
        // __result=true makes Harmony treat that skipped VGE prefix as "allow vanilla to continue".
        private static bool GuardVgeShowRitualBeginWindow(object[] __args, ref bool __result)
        {
            if (__args != null &&
                __args.Length > 1 &&
                __args[1] is TargetInfo target &&
                IsWNGPilot(target))
            {
                __result = true;
                return false;
            }
            return true;
        }

        // VGE replaces Odyssey's confirmation launchAction with its own ExecuteGravshipLaunch.
        // Skipping this method leaves the original Odyssey action unchanged.
        private static bool GuardVgePreLaunchConfirmation(object[] __args)
        {
            return !(__args != null &&
                     __args.Length > 0 &&
                     __args[0] is Building_GravEngine engine &&
                     IsWNGEngine(engine));
        }

        // If stale VGE launch state exists, do not let it replace Odyssey's tile confirmation
        // callback for a WNG engine.
        private static bool GuardVgeCheckConfirmSettle(object[] __args)
        {
            return !(__args != null &&
                     __args.Length > 3 &&
                     __args[3] is Building_GravEngine engine &&
                     IsWNGEngine(engine));
        }

        // This blocks both VGE's ConsumeFuel Prefix and the crashing Postfix for WNG engines.
        // Odyssey's Building_GravEngine.ConsumeFuel body still runs normally.
        private static bool GuardVgeConsumeFuel(object[] __args)
        {
            return !(__args != null &&
                     __args.Length > 0 &&
                     __args[0] is Building_GravEngine engine &&
                     IsWNGEngine(engine));
        }

        // VGE's launch-dialog outcome renderer assumes VGE-only engine comps such as CompHeatManager.
        private static bool GuardVgeDrawLaunchOutcome(object[] __args)
        {
            if (__args == null || __args.Length == 0 || !(__args[0] is Dialog_BeginRitual dialog))
                return true;

            var targetField = AccessTools.Field(typeof(Dialog_BeginRitual), "target");
            if (targetField?.GetValue(dialog) is not TargetInfo target)
                return true;

            Building_GravEngine engine =
                target.Thing?.TryGetComp<CompPilotConsole>()?.engine;
            return !IsWNGEngine(engine);
        }

        private static bool GuardVgeRitualBehavior(object[] __args)
        {
            return !(__args != null &&
                     __args.Length > 0 &&
                     __args[0] is TargetInfo target &&
                     IsWNGPilot(target));
        }

        private static bool GuardVgeRitualOutcome(object[] __args)
        {
            if (__args == null || __args.Length <= 2 || !(__args[2] is LordJob_Ritual ritual))
                return true;

            Building_GravEngine engine =
                ritual.selectedTarget.Thing?.TryGetComp<CompPilotConsole>()?.engine;
            return !IsWNGEngine(engine);
        }
    }

    [HarmonyPatch(typeof(RitualObligationTargetWorker_GravshipLaunch),
        nameof(RitualObligationTargetWorker_GravshipLaunch.GetTargets))]
    internal static class WNGRitualObligationTargetWorkerGravshipLaunchPatch
    {
        private static IEnumerable<TargetInfo> Postfix(
            IEnumerable<TargetInfo> __result,
            RitualObligation obligation,
            Map map)
        {
            HashSet<Thing> seen = new HashSet<Thing>();

            foreach (TargetInfo target in __result)
            {
                if (target.HasThing)
                    seen.Add(target.Thing);
                yield return target;
            }

            foreach (string defName in new[]
            {
                "WNG_OrganicPilotNode",
                "WNG_PrecursorPilotConsole",
                "WNG_GoauldPeltac"
            })
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                    continue;

                foreach (Thing thing in map.listerThings.ThingsOfDef(def))
                {
                    if (!seen.Add(thing))
                        continue;

                    CompPilotConsole console = thing.TryGetComp<CompPilotConsole>();
                    if (console != null && (bool)console.CanUseNow())
                        yield return thing;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Gravship), "AddThing")]
    internal static class WNGGravshipAddThingPilotConsolePatch
    {
        private static void Postfix(Thing thing, ref Building ___pilotConsole)
        {
            if (thing is Building building &&
                thing.TryGetComp<CompPilotConsole_WNGFamily>() != null)
            {
                ___pilotConsole = building;
            }
        }
    }

    [HarmonyPatch(typeof(SectionLayer_GravshipHull),
        nameof(SectionLayer_GravshipHull.BakeGravshipIndoorMesh))]
    internal static class WNGGravshipBakeIndoorHullMaskPatch
    {
        private static void Postfix(
            List<LayerSubMesh> __result,
            Map map,
            CellRect bounds,
            Vector3 center)
        {
            if (__result != null && ModsConfig.OdysseyActive)
                __result.AddRange(SectionLayer_WNGFamilyGravshipHull.BakeGravshipIndoorMesh(map, bounds, center));
        }
    }

    public sealed class CompProperties_WNGGravshipFacility : CompProperties_GravshipFacility
    {
        public WNGGravshipFamily family = WNGGravshipFamily.None;
        public bool requiresFamilyPower;

        public CompProperties_WNGGravshipFacility()
        {
            compClass = typeof(CompGravshipFacility_WNGFamily);
        }
    }

    public class CompGravshipFacility_WNGFamily : CompGravshipFacility
    {
        protected CompProperties_WNGGravshipFacility WNGProps => (CompProperties_WNGGravshipFacility)props;

        public override void PostSpawnSetup(bool respawningAfterReload)
        {
            WNGGravshipFamilyUtility.EnsureExactEngineTarget(this, WNGProps.family);
            base.PostSpawnSetup(respawningAfterReload);
        }

        public override bool CanBeActive =>
            WNGGravshipFamilyUtility.ExactEngineLinkIsValid(this, WNGProps.family, WNGProps.requiresFamilyPower);
    }

    public sealed class CompProperties_WNGFuelTankFacility : CompProperties_GravshipFacility
    {
        public WNGGravshipFamily family = WNGGravshipFamily.None;
        public bool requiresFamilyPower;

        public CompProperties_WNGFuelTankFacility()
        {
            compClass = typeof(CompGravshipFuelTank_WNGFamily);
        }
    }

    public sealed class CompGravshipFuelTank_WNGFamily : CompGravshipFacility
    {
        private CompProperties_WNGFuelTankFacility WNGProps => (CompProperties_WNGFuelTankFacility)props;

        public override void PostSpawnSetup(bool respawningAfterReload)
        {
            WNGGravshipFamilyUtility.EnsureExactEngineTarget(this, WNGProps.family);
            base.PostSpawnSetup(respawningAfterReload);
        }

        public override bool CanBeActive =>
            WNGGravshipFamilyUtility.ExactEngineLinkIsValid(this, WNGProps.family, WNGProps.requiresFamilyPower) &&
            WNGFamilyFuelUtility.HasRoleConnection(parent, WNGProps.family, WNGFuelEndpointRole.Thruster);

        public override string CompInspectStringExtra()
        {
            string native = base.CompInspectStringExtra();
            if (parent.Spawned && !WNGFamilyFuelUtility.HasRoleConnection(parent, WNGProps.family, WNGFuelEndpointRole.Thruster))
                return native.NullOrEmpty() ? "Same-family drive feed disconnected." : native + "\nSame-family drive feed disconnected.";
            return native;
        }
    }

    public sealed class CompProperties_WNGThruster : CompProperties_GravshipThruster
    {
        public WNGGravshipFamily family = WNGGravshipFamily.None;
        public bool requiresFamilyPower = true;
        public SoundDef launchSound;

        public CompProperties_WNGThruster()
        {
            compClass = typeof(CompGravshipThruster_WNGFamily);
        }
    }

    public sealed class CompGravshipThruster_WNGFamily : CompGravshipThruster
    {
        private static int lastFamilyLaunchSoundTick = -1;

        private CompProperties_WNGThruster WNGProps => (CompProperties_WNGThruster)props;

        public override void PostSpawnSetup(bool respawningAfterReload)
        {
            WNGGravshipFamilyUtility.EnsureExactEngineTarget(this, WNGProps.family);
            base.PostSpawnSetup(respawningAfterReload);
        }

        public override bool CanBeActive
        {
            get
            {
                if (!CanLink() || Breakdownable.BrokenDown || Blocked || LinkedBuildings.NullOrEmpty())
                    return false;
                if (!WNGGravshipFamilyUtility.ExactEngineLinkIsValid(this, WNGProps.family, WNGProps.requiresFamilyPower))
                    return false;
                return WNGFamilyFuelUtility.HasRoleConnection(parent, WNGProps.family, WNGFuelEndpointRole.Tank);
            }
        }

        public override string CompInspectStringExtra()
        {
            string native = base.CompInspectStringExtra();
            if (parent.Spawned && !WNGFamilyFuelUtility.HasRoleConnection(parent, WNGProps.family, WNGFuelEndpointRole.Tank))
                return native.NullOrEmpty() ? "Same-family fuel feed disconnected." : native + "\nSame-family fuel feed disconnected.";
            return native;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            Building_GravEngine linkedEngine = engine;
            IntVec3 soundPosition = parent != null ? parent.Position : IntVec3.Invalid;
            SoundDef launchSound = WNGProps.launchSound;
            int now = Find.TickManager?.TicksGame ?? -1;

            // Native launch commits fuel/cooldown before the gravship capture despawns its facilities.
            // Landing does not take this path, and ordinary deconstruction/reinstall is excluded by
            // the active gravship-cutscene requirement.
            bool committedTakeoffCapture =
                launchSound != null &&
                linkedEngine != null &&
                map != null &&
                soundPosition.IsValid &&
                soundPosition.InBounds(map) &&
                WorldComponent_GravshipController.CutsceneInProgress &&
                linkedEngine.cooldownCompleteTick > now;

            base.PostDeSpawn(map, mode);

            // GenerateGravship despawns every attached thruster synchronously in the same game tick.
            // Emit one family drive cue for the whole craft, not one voice per physical thruster.
            if (!committedTakeoffCapture || now < 0 || lastFamilyLaunchSoundTick == now)
                return;

            lastFamilyLaunchSoundTick = now;
            try
            {
                launchSound.PlayOneShot(new TargetInfo(soundPosition, map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Gravship family launch sound failed: " + ex.Message);
            }
        }
    }

    public sealed class PlaceWorker_RequireWraithGravshipSubstructure : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            return WNGGravshipFamilyUtility.RequireFamilySubstructure(loc, rot, checkingDef, map, WNGGravshipFamily.Wraith, thingToIgnore);
        }
    }

    public sealed class PlaceWorker_RequireAsuranGravshipSubstructure : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            return WNGGravshipFamilyUtility.RequireFamilySubstructure(loc, rot, checkingDef, map, WNGGravshipFamily.Asuran, thingToIgnore);
        }
    }

    public sealed class PlaceWorker_RequireGoauldGravshipSubstructure : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            return WNGGravshipFamilyUtility.RequireFamilySubstructure(loc, rot, checkingDef, map, WNGGravshipFamily.Goauld, thingToIgnore);
        }
    }

    public abstract class PlaceWorker_InRangeOfFamilyGravEngine : PlaceWorker
    {
        protected abstract WNGGravshipFamily Family { get; }

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            string defName = WNGGravshipFamilyUtility.EngineDefName(Family);
            ThingDef engineDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (engineDef == null)
                return "Missing family grav engine definition.";
            foreach (Thing engine in map.listerThings.ThingsOfDef(engineDef))
            {
                if (engine != null && engine.Spawned && engine.Position.InHorDistOf(loc, 18.9f))
                    return AcceptanceReport.WasAccepted;
            }
            return "Must be placed within the field of the matching grav engine.";
        }
    }

    public sealed class PlaceWorker_InRangeOfWraithGravEngine : PlaceWorker_InRangeOfFamilyGravEngine
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Wraith;
    }

    public sealed class PlaceWorker_InRangeOfAsuranGravEngine : PlaceWorker_InRangeOfFamilyGravEngine
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Asuran;
    }

    public sealed class PlaceWorker_InRangeOfGoauldGravEngine : PlaceWorker_InRangeOfFamilyGravEngine
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Goauld;
    }



    public sealed class CompProperties_WraithGravshipMetabolicRepair : CompProperties
    {
        public int pulseIntervalTicks = 480;
        public float radius = 30f;
        public int maxTargetsPerPulse = 5;
        public int healPerTarget = 8;
        public float biomassPerPulse = 1f;

        public CompProperties_WraithGravshipMetabolicRepair()
        {
            compClass = typeof(CompWraithGravshipMetabolicRepair);
        }
    }

    public sealed class CompWraithGravshipMetabolicRepair : ThingComp
    {
        private CompProperties_WraithGravshipMetabolicRepair Props => (CompProperties_WraithGravshipMetabolicRepair)props;

        public override void CompTick()
        {
            base.CompTick();
            if (parent == null || !parent.Spawned || parent.Map == null ||
                !parent.IsHashIntervalTick(Math.Max(60, Props.pulseIntervalTicks)))
                return;

            CompRefuelable fuel = parent.TryGetComp<CompRefuelable>();
            if (fuel == null || fuel.Fuel + 0.0001f < Math.Max(0f, Props.biomassPerPulse))
                return;

            List<Thing> damaged = GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.radius, true)
                .Where(t => t != null && t.Spawned && t != parent && t.Faction == parent.Faction &&
                            t.def != null && (t.def.defName.StartsWith("WNG_Wraith", StringComparison.Ordinal) ||
                                              t.def.defName.StartsWith("WNG_Organic", StringComparison.Ordinal)) &&
                            t.HitPoints > 0 && t.HitPoints < t.MaxHitPoints)
                .OrderBy(t => (float)t.HitPoints / Math.Max(1, t.MaxHitPoints))
                .Take(Math.Max(1, Props.maxTargetsPerPulse))
                .ToList();

            if (damaged.Count == 0)
                return;

            fuel.ConsumeFuel(Math.Min(fuel.Fuel, Math.Max(0f, Props.biomassPerPulse)));
            foreach (Thing target in damaged)
                target.HitPoints = Math.Min(target.MaxHitPoints, target.HitPoints + Math.Max(1, Props.healPerTarget));
        }
    }

    public abstract class PlaceWorker_WNGFamilySubstructure : PlaceWorker
    {
        protected abstract WNGGravshipFamily Family { get; }
        protected abstract string ExtenderDefName { get; }

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            string engineName = WNGGravshipFamilyUtility.EngineDefName(Family);
            ThingDef engineDef = DefDatabase<ThingDef>.GetNamedSilentFail(engineName);
            if (engineDef != null)
            {
                foreach (Thing engine in map.listerThings.ThingsOfDef(engineDef))
                    if (engine != null && engine.Spawned && engine.Position.InHorDistOf(loc, 18.9f))
                        return AcceptanceReport.WasAccepted;
            }

            ThingDef extenderDef = DefDatabase<ThingDef>.GetNamedSilentFail(ExtenderDefName);
            if (extenderDef != null)
            {
                foreach (Thing extender in map.listerThings.ThingsOfDef(extenderDef))
                    if (extender != null && extender.Spawned && extender.Position.InHorDistOf(loc, 16.9f))
                        return AcceptanceReport.WasAccepted;
            }

            return "This deck must remain inside the matching grav engine or field-extender footprint.";
        }
    }

    public sealed class PlaceWorker_WraithGravshipSubstructure : PlaceWorker_WNGFamilySubstructure
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Wraith;
        protected override string ExtenderDefName => "WNG_WraithGravFieldExtender";
    }

    public sealed class PlaceWorker_AsuranGravshipSubstructure : PlaceWorker_WNGFamilySubstructure
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Asuran;
        protected override string ExtenderDefName => "WNG_AsuranGravFieldExtender";
    }

    public sealed class PlaceWorker_GoauldGravshipSubstructure : PlaceWorker_WNGFamilySubstructure
    {
        protected override WNGGravshipFamily Family => WNGGravshipFamily.Goauld;
        protected override string ExtenderDefName => "WNG_GoauldGravFieldProjector";
    }

    public sealed class MinifiedAsuranGravEngine : MinifiedThing
    {
        public override void PostMake()
        {
            base.PostMake();
            if (InnerThing != null)
                return;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_AsuranGravEngine");
            if (def == null)
            {
                Log.ErrorOnce("[WNG] Asuran grav engine Def is missing; minified engine could not initialize.", 205901337);
                return;
            }
            InnerThing = ThingMaker.MakeThing(def);
        }
    }

}
