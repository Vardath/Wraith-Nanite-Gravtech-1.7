using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Author-tunable orbital firing envelope for the physical Ha'tak heavy-plasma battery.
    /// RimWorld's native Bombardment remains the strike owner so interception, fire, warmup,
    /// save/load and damage behavior stay native wherever possible.
    /// </summary>
    public sealed class CompProperties_GoauldHatakOrbitalBombardment : CompProperties
    {
        public int maxWorldRangeTiles = 12;
        public int cooldownTicks = 60000;
        public float impactAreaRadius = 12f;
        public FloatRange explosionRadiusRange = new FloatRange(2.8f, 4.0f);
        public int bombIntervalTicks = 18;
        public int warmupTicks = 120;
        public int randomFireRadius = 14;

        public CompProperties_GoauldHatakOrbitalBombardment()
        {
            compClass = typeof(CompGoauldHatakOrbitalBombardment);
        }
    }

    /// <summary>
    /// Cross-map orbital fire from the same exact heavy-plasma battery used for local combat.
    /// Targets must already be generated Surface maps; WNG never creates or proxies a remote map
    /// merely to receive fire. Cooldown commits only after the native Bombardment Thing exists.
    /// </summary>
    public sealed class CompGoauldHatakOrbitalBombardment : ThingComp
    {
        private const int NativeBombardmentExplosionCount = 30;
        private int nextFireTick;

        private CompProperties_GoauldHatakOrbitalBombardment Props =>
            (CompProperties_GoauldHatakOrbitalBombardment)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer)
                yield break;

            Command_Action command = new Command_Action
            {
                defaultLabel = "Ha'tak orbital bombardment",
                defaultDesc = "While this exact Ha'tak battery is operating from an Odyssey Orbit map, designate a different already-generated Surface map within range and order a native RimWorld bombardment from orbit.",
                icon = TexCommand.Attack,
                action = BeginWorldTargeting
            };

            AcceptanceReport report = CanOrderBombardment();
            if (!report.Accepted)
                command.Disable(report.Reason);

            yield return command;
        }

        private AcceptanceReport CanOrderBombardment()
        {
            if (parent?.Spawned != true || parent.Map == null)
                return "The plasma battery is not deployed on a map.";
            if (parent.Faction != Faction.OfPlayer)
                return "Only a player-owned Ha'tak heavy plasma battery can receive orbital fire orders.";
            if (!ModsConfig.OdysseyActive)
                return "Odyssey is required for Ha'tak orbital firing geometry.";
            if (!ModsConfig.RoyaltyActive || ThingDefOf.Bombardment == null)
                return "Royalty is required for RimWorld's native orbital bombardment entity.";
            if (parent.Map.Tile.LayerDef != PlanetLayerDefOf.Orbit)
                return "The Ha'tak must be in Odyssey Orbit before this battery can bombard a surface map.";

            CompGravshipFacility facility = parent.TryGetComp<CompGravshipFacility>();
            if (facility == null || !WNGGravshipFamilyUtility.ExactEngineLinkIsValid(
                    facility, WNGGravshipFamily.Goauld, requiresPower: false))
            {
                return "The battery is not physically linked to the exact Ha'tak grav engine.";
            }

            if (!WNGFamilyPowerUtility.IsPowered(parent, WNGGravshipFamily.Goauld))
                return "The Ha'tak heavy plasma battery has no Goa'uld ship power.";

            CompPowerTrader powerProxy = parent.TryGetComp<CompPowerTrader>();
            if (powerProxy == null || !powerProxy.PowerOn)
                return "The Ha'tak heavy plasma battery is not operational.";

            int remaining = nextFireTick - (Find.TickManager?.TicksGame ?? 0);
            if (remaining > 0)
                return "Orbital bombardment cooling down (" + remaining.ToStringTicksToPeriod() + ").";

            return true;
        }

        private void BeginWorldTargeting()
        {
            AcceptanceReport report = CanOrderBombardment();
            if (!report.Accepted)
            {
                Messages.Message(report.Reason, parent, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            PlanetTile sourceTile = parent.Map.Tile;
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(sourceTile)));
            Find.WorldSelector.ClearSelection();
            Find.WorldTargeter.BeginTargeting(
                ChoseWorldTarget,
                canTargetTiles: false,
                mouseAttachment: TexCommand.Attack,
                closeWorldTabWhenFinished: false,
                onUpdate: null,
                extraLabelGetter: WorldTargetLabel,
                canSelectTarget: CanSelectWorldTarget,
                originForClosest: sourceTile,
                showCancelButton: true);
        }

        private TaggedString WorldTargetLabel(GlobalTargetInfo target)
        {
            MapParent targetParent = ResolveTargetMapParent(target);
            if (targetParent == null || !targetParent.HasMap || targetParent.Map == null)
                return "Requires an already-generated Surface map.";
            if (parent?.Map == targetParent.Map)
                return "Select a different map.";
            if (targetParent.Tile.LayerDef != PlanetLayerDefOf.Surface)
                return "Target must be on the planetary surface.";

            int distance = WorldDistanceTo(targetParent.Tile);
            int maxRange = Math.Max(1, Props.maxWorldRangeTiles);
            if (distance == int.MaxValue || distance > maxRange)
                return "Outside Ha'tak orbital firing range (" + maxRange + " tiles).";
            return "Ha'tak orbital range: " + distance + " / " + maxRange + " tiles";
        }

        private bool CanSelectWorldTarget(GlobalTargetInfo target)
        {
            MapParent targetParent = ResolveTargetMapParent(target);
            if (targetParent == null || !targetParent.HasMap || targetParent.Map == null)
                return false;

            Map sourceMap = parent?.Map;
            if (sourceMap == null || targetParent.Map == sourceMap)
                return false;
            if (targetParent.Tile.LayerDef != PlanetLayerDefOf.Surface)
                return false;

            int distance = WorldDistanceTo(targetParent.Tile);
            return distance != int.MaxValue && distance <= Math.Max(1, Props.maxWorldRangeTiles);
        }

        private static MapParent ResolveTargetMapParent(GlobalTargetInfo target)
        {
            return target.HasWorldObject ? target.WorldObject as MapParent : null;
        }

        private int WorldDistanceTo(PlanetTile targetTile)
        {
            Map sourceMap = parent?.Map;
            if (sourceMap == null || Find.WorldGrid == null || !targetTile.Valid)
                return int.MaxValue;

            return Find.WorldGrid.TraversalDistanceBetween(
                sourceMap.Tile,
                targetTile,
                passImpassable: true,
                maxDist: Math.Max(1, Props.maxWorldRangeTiles) + 1,
                canTraverseLayers: true);
        }

        private bool ChoseWorldTarget(GlobalTargetInfo target)
        {
            if (!CanSelectWorldTarget(target))
            {
                Messages.Message(
                    "Select a different already-generated Surface map within the Ha'tak battery's orbital firing range.",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            Map targetMap = ResolveTargetMapParent(target)?.Map;
            if (targetMap == null)
                return false;

            Current.Game.CurrentMap = targetMap;
            CameraJumper.TryHideWorld();
            BeginLocalTargeting(targetMap);
            return true;
        }

        private void BeginLocalTargeting(Map targetMap)
        {
            TargetingParameters targeting = TargetingParameters.ForCell();
            targeting.validator = delegate(TargetInfo target)
            {
                if (!target.Cell.InBounds(targetMap) || target.Cell.Fogged(targetMap))
                    return false;
                RoofDef roof = target.Cell.GetRoof(targetMap);
                return roof == null || !roof.isThickRoof;
            };

            Find.Targeter.BeginTargeting(
                targeting,
                delegate(LocalTargetInfo target)
                {
                    TryFireAt(targetMap, target.Cell);
                },
                caster: null,
                actionWhenFinished: null,
                mouseAttachment: TexCommand.Attack,
                requiresCastedSelected: false);
        }

        private bool TryFireAt(Map targetMap, IntVec3 targetCell)
        {
            AcceptanceReport report = CanOrderBombardment();
            if (!report.Accepted)
            {
                Messages.Message(report.Reason, parent, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            if (targetMap == null || targetMap == parent.Map || !targetCell.InBounds(targetMap) ||
                targetMap.Tile.LayerDef != PlanetLayerDefOf.Surface)
                return false;

            int maxRange = Math.Max(1, Props.maxWorldRangeTiles);
            if (WorldDistanceTo(targetMap.Tile) > maxRange)
            {
                Messages.Message(
                    "The selected Surface map is outside Ha'tak orbital bombardment range.",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            RoofDef roof = targetCell.GetRoof(targetMap);
            if (targetCell.Fogged(targetMap) || (roof != null && roof.isThickRoof))
            {
                Messages.Message(
                    "Ha'tak orbital fire cannot be designated on a fogged cell or beneath thick overhead mountain.",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            Bombardment bombardment = ThingMaker.MakeThing(ThingDefOf.Bombardment) as Bombardment;
            if (bombardment == null)
            {
                Log.Error("[WNG] Ha'tak orbital bombardment could not create RimWorld's native Bombardment Thing.");
                return false;
            }

            // Configure every field that RimWorld actually serializes before SpawnSetup. The native
            // explosionCount remains 30 because Bombardment 1.6 does not scribe that field; changing
            // it would silently revert after a save made during warmup.
            FloatRange configured = Props.explosionRadiusRange;
            float minExplosion = Mathf.Max(1f, configured.min);
            float maxExplosion = Mathf.Max(minExplosion, configured.max);
            bombardment.impactAreaRadius = Mathf.Max(1f, Props.impactAreaRadius);
            bombardment.explosionRadiusRange = new FloatRange(minExplosion, maxExplosion);
            bombardment.bombIntervalTicks = Math.Max(1, Props.bombIntervalTicks);
            bombardment.warmupTicks = Math.Max(1, Props.warmupTicks);
            bombardment.explosionCount = NativeBombardmentExplosionCount;
            bombardment.randomFireRadius = Math.Max(1, Props.randomFireRadius);
            bombardment.instigator = parent;
            bombardment.weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Gun_GoauldHeavyPlasmaCannon") ?? parent.def;

            Thing spawned = GenSpawn.Spawn(bombardment, targetCell, targetMap);
            if (spawned?.Spawned != true)
            {
                if (!bombardment.Destroyed)
                    bombardment.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Ha'tak orbital bombardment could not spawn RimWorld's native Bombardment Thing.");
                return false;
            }

            // Mechanical commit boundary: a real native Bombardment now exists on the target map.
            // Presentation failures after this point may not refund the cooldown or duplicate fire.
            nextFireTick = (Find.TickManager?.TicksGame ?? 0) + Math.Max(0, Props.cooldownTicks);
            try
            {
                SoundDefOf.OrbitalStrike_Ordered.PlayOneShotOnCamera();
                Messages.Message(
                    "Ha'tak orbital bombardment ordered.",
                    new TargetInfo(targetCell, targetMap),
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Ha'tak orbital bombardment committed, but presentation failed: " + ex.Message);
            }
            return true;
        }

        public override string CompInspectStringExtra()
        {
            if (parent?.Spawned != true)
                return null;

            int remaining = nextFireTick - (Find.TickManager?.TicksGame ?? 0);
            if (remaining > 0)
                return "Orbital bombardment: cooling down (" + remaining.ToStringTicksToPeriod() + ")";
            if (parent.Map?.Tile.LayerDef != PlanetLayerDefOf.Orbit)
                return "Orbital bombardment: requires Odyssey Orbit";
            return "Orbital bombardment: ready";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextFireTick, "wngHatakOrbitalBombardmentNextFireTick", 0);
        }
    }
}
