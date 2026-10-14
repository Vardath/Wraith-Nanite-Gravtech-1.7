using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AbilityHumanFormConsume : CompProperties_AbilityEffect
    {
        public float minimumFoodFraction = 0.08f;
        public float foodFractionPerSqrtMass = 0.02f;
        public float maximumFoodFraction = 0.50f;
        public float minimumReserveGain = 0.06f;
        public float reserveGainPerSqrtMass = 0.015f;
        public float maximumReserveGain = 0.40f;
        public float substrateFoodFraction = 0.15f;
        public float substrateReserveGain = 0.10f;

        public CompProperties_AbilityHumanFormConsume()
        {
            compClass = typeof(CompAbilityEffect_HumanFormConsume);
        }
    }

    /// <summary>
    /// Explicit player-directed material assimilation for WNG human-form nanite synthetics.
    /// It deliberately never creates offspring. The exact target is consumed only after the
    /// caster is in touch range, then its physical mass becomes ordinary Food need plus the
    /// caster's bounded Nanite Reserve.
    /// </summary>
    public sealed class CompAbilityEffect_HumanFormConsume : CompAbilityEffect
    {
        private const string EmpDisruptionDefName = "WNG_NaniteEMPDisruption";

        public new CompProperties_AbilityHumanFormConsume Props =>
            (CompProperties_AbilityHumanFormConsume)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            string reason;
            bool valid = HumanFormAssimilationUtility.CanConsume(caster, target, out reason);
            if (!valid && throwMessages && caster != null && !reason.NullOrEmpty())
            {
                Messages.Message(reason, caster, MessageTypeDefOf.RejectInput, historical: false);
            }

            return valid && base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            string reason;
            if (!HumanFormAssimilationUtility.CanConsume(caster, target, out reason, requireTouch: true))
            {
                if (caster != null && !reason.NullOrEmpty())
                    Messages.Message(reason, caster, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Need_Food food = caster.needs?.food;
            Gene_Resource_NaniteReserve reserve =
                caster.genes?.GetFirstGeneOfType<Gene_Resource_NaniteReserve>();
            if (reserve == null || !reserve.Active)
                return;

            float foodFraction;
            float reserveGain;

            Thing thing = target.Thing;
            if (thing != null)
            {
                float mass = HumanFormAssimilationUtility.EffectiveMass(thing);
                float sqrtMass = (float)Math.Sqrt(Math.Max(0f, mass));
                foodFraction = Clamp(
                    Math.Max(0f, Props.minimumFoodFraction) +
                    sqrtMass * Math.Max(0f, Props.foodFractionPerSqrtMass),
                    0f,
                    Math.Max(0f, Props.maximumFoodFraction));
                reserveGain = Clamp(
                    Math.Max(0f, Props.minimumReserveGain) +
                    sqrtMass * Math.Max(0f, Props.reserveGainPerSqrtMass),
                    0f,
                    Math.Max(0f, Props.maximumReserveGain));

                // Revalidate immediately before the destructive commit.
                if (!HumanFormAssimilationUtility.IsConsumableThing(caster, thing))
                    return;

                HashSet<IntVec3> pendingRoofCollapsesBefore =
                    ReplicatorEnvironmentalAssimilationUtility.CapturePendingRoofCollapses(caster.Map);

                try
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    if (!thing.Destroyed)
                    {
                        Log.Error("[WNG] Human-form assimilation target destruction failed before commit: " + ex);
                        return;
                    }

                    Log.Warning("[WNG] Human-form assimilation target reported an exception after destruction; keeping the committed feedstock gain: " + ex.Message);
                }

                if (!thing.Destroyed)
                    return;

                // Human-form Replicators use the same clean roof-consumption rule as block swarms:
                // if removing this wall/rock made vanilla schedule roof collapse, consume those
                // newly unsupported roofs instead of allowing thick roof to create collapsed rock.
                ReplicatorEnvironmentalAssimilationUtility.AssimilateNewlyUnsupportedRoofs(
                    caster.Map,
                    pendingRoofCollapsesBefore);
            }
            else
            {
                IntVec3 cell = target.Cell;
                if (!HumanFormAssimilationUtility.TryConsumeSubstrate(caster, cell))
                    return;

                foodFraction = Math.Max(0f, Props.substrateFoodFraction);
                reserveGain = Math.Max(0f, Props.substrateReserveGain);
            }

            float foodGain = food == null
                ? 0f
                : Math.Max(0f, food.MaxLevel * foodFraction);
            reserve.AddAssimilatedFeedstock(foodGain, reserveGain);

            try
            {
                DefDatabase<SoundDef>.GetNamedSilentFail("WNG_ReplicatorAssimilate")
                    ?.PlayOneShot(new TargetInfo(caster.Position, caster.Map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Human-form assimilation committed but its sound failed: " + ex.Message);
            }
        }

        private static float Clamp(float value, float min, float max)
        {
            if (max < min)
                max = min;
            return Math.Min(max, Math.Max(min, value));
        }
    }

    public static class HumanFormAssimilationUtility
    {
        private const string ReplicatorMatterDefName = "WNG_ReplicatorMatter";
        private const string ReplicatorCoreFragmentDefName = "WNG_ReplicatorCoreFragment";
        private const string EmpDisruptionDefName = "WNG_NaniteEMPDisruption";
        private const string QueenChildKindDefName = "WNG_ReplicatorQueenChild";

        public static bool CanConsume(Pawn caster, LocalTargetInfo target, out string reason, bool requireTouch = false)
        {
            reason = null;

            if (caster == null || caster.Destroyed || caster.Dead || !caster.Spawned || caster.Map == null)
            {
                reason = "The nanite body is unavailable.";
                return false;
            }

            if (!AsuranCollectiveUtility.IsNaniteSynthetic(caster))
            {
                reason = "Only a WNG human-form nanite synthetic can assimilate world matter.";
                return false;
            }

            Gene_Resource_NaniteReserve reserve =
                caster.genes?.GetFirstGeneOfType<Gene_Resource_NaniteReserve>();
            Need_Food food = caster.needs?.food;
            if (reserve == null || !reserve.Active)
            {
                reason = "This synthetic body lacks a functioning Nanite Reserve feedstock system.";
                return false;
            }

            HediffDef disruption = DefDatabase<HediffDef>.GetNamedSilentFail(EmpDisruptionDefName);
            if (disruption != null &&
                caster.health?.hediffSet?.GetFirstHediffOfDef(disruption) != null)
            {
                reason = "EMP disruption prevents controlled material assimilation.";
                return false;
            }

            bool reserveFull = reserve.Value >= reserve.Max - 0.0001f;
            bool foodFullOrUnavailable = food == null ||
                                         food.CurLevel >= food.MaxLevel - 0.0001f;
            if (reserveFull && foodFullOrUnavailable)
            {
                reason = food == null
                    ? "Nanite Reserve is already full."
                    : "Food and Nanite Reserve are already full.";
                return false;
            }

            Thing thing = target.Thing;
            if (thing != null)
            {
                if (!IsConsumableThing(caster, thing))
                {
                    reason = "Select a destroyable physical item, building, natural rock, or plant outside active Replicator containment.";
                    return false;
                }

                // The Queen's recovered child-form body keeps the non-violent feedstock function,
                // but does not gain adult-scale structural assimilation. It may consume loose
                // items and plants, never buildings/natural rock or map substrate.
                if (IsRestrictedChildForm(caster) && thing.def.category == ThingCategory.Building)
                {
                    reason = "This child-form nanite body can assimilate loose matter and plants, but not structural mass.";
                    return false;
                }

                if (requireTouch && !caster.Position.AdjacentTo8WayOrInside(thing.Position))
                {
                    reason = "The material is no longer within assimilation range.";
                    return false;
                }

                return true;
            }

            if (IsRestrictedChildForm(caster))
            {
                reason = "This child-form nanite body cannot assimilate roof, floor, or ground layers.";
                return false;
            }

            IntVec3 cell = target.Cell;
            if (!IsConsumableSubstrate(caster, cell))
            {
                reason = "Select consumable roof, floor, or ground outside active Replicator containment.";
                return false;
            }

            if (requireTouch && !caster.Position.AdjacentTo8WayOrInside(cell))
            {
                reason = "The substrate is no longer within assimilation range.";
                return false;
            }

            return true;
        }

        public static bool IsConsumableThing(Pawn caster, Thing target)
        {
            if (caster?.Map == null || target == null || target == caster || target.Destroyed ||
                !target.Spawned || target.Map != caster.Map || target is Pawn || target is Corpse)
            {
                return false;
            }

            ThingDef def = target.def;
            if (def == null || !def.destroyable)
                return false;

            if (def.defName == ReplicatorMatterDefName || def.defName == ReplicatorCoreFragmentDefName)
                return false;

            if (ReplicatorContainmentUtility.BlocksAssimilation(caster, target))
                return false;

            if (def.category == ThingCategory.Item)
                return target.stackCount > 0;

            return def.category == ThingCategory.Building ||
                   def.category == ThingCategory.Plant;
        }

        public static bool IsConsumableSubstrate(Pawn caster, IntVec3 cell)
        {
            if (caster?.Map == null || !cell.InBounds(caster.Map))
                return false;

            Map map = caster.Map;
            if (ReplicatorContainmentUtility.IsContained(map, caster.Position) ||
                ReplicatorContainmentUtility.IsContained(map, cell))
                return false;

            // A click on a cell containing a physical target must consume the Thing itself, not
            // silently strip the substrate from under it.
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (IsConsumableThing(caster, things[i]))
                    return false;
            }

            if (map.roofGrid.Roofed(cell))
                return true;

            if (map.terrainGrid.CanRemoveTopLayerAt(cell))
                return true;

            if (map.terrainGrid.CanRemoveFoundationAt(cell))
                return true;

            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            bool isVoid = terrain == null ||
                          string.Equals(terrain.defName, "Space", StringComparison.OrdinalIgnoreCase);

            // Gravel is the terminal stripped-ground state. Treating it as consumable would let
            // repeated casts generate free Food/Nanite Reserve without removing any matter.
            return !isVoid && terrain != TerrainDefOf.Gravel;
        }

        public static bool TryConsumeSubstrate(Pawn caster, IntVec3 cell)
        {
            if (!IsConsumableSubstrate(caster, cell))
                return false;

            Map map = caster.Map;
            try
            {
                // One cast consumes exactly one physical substrate layer. A roof over a floor is
                // therefore two separate feedstock actions rather than one cast deleting both.
                if (map.roofGrid.Roofed(cell))
                {
                    map.roofGrid.SetRoof(cell, null);
                    FilthMaker.RemoveAllFilth(cell, map);
                    return true;
                }

                if (map.terrainGrid.CanRemoveTopLayerAt(cell))
                {
                    map.terrainGrid.RemoveTopLayer(cell, doLeavings: false);
                    FilthMaker.RemoveAllFilth(cell, map);
                    return true;
                }

                // Odyssey gravship substructure lives in the terrain foundation grid.
                if (map.terrainGrid.CanRemoveFoundationAt(cell))
                {
                    map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                    FilthMaker.RemoveAllFilth(cell, map);
                    return true;
                }

                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                bool isVoid = terrain == null ||
                              string.Equals(terrain.defName, "Space", StringComparison.OrdinalIgnoreCase);
                if (!isVoid && terrain != TerrainDefOf.Gravel)
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
                    FilthMaker.RemoveAllFilth(cell, map);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Human-form substrate assimilation failed: " + ex);
                return false;
            }
        }

        private static bool IsRestrictedChildForm(Pawn caster)
        {
            return caster?.kindDef?.defName == QueenChildKindDefName;
        }

        public static float EffectiveMass(Thing thing)
        {
            if (thing?.def == null)
                return 0f;

            float mass = 0f;
            try
            {
                mass = Math.Max(0f, thing.GetStatValue(StatDefOf.Mass));
            }
            catch
            {
                mass = Math.Max(0f, thing.def.BaseMass);
            }

            if (thing.def.category == ThingCategory.Item)
                mass *= Math.Max(1, thing.stackCount);

            return mass;
        }
    }
}
