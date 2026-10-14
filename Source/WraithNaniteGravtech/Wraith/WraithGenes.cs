using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Centralized first-build tuning for the Wraith Life Force system. Values live on the
    /// Life Force GeneDef so later balance passes can change behavior without scattering constants
    /// across feeding/regeneration/hibernation code.
    /// </summary>
    public sealed class WraithLifeForceSettingsExtension : DefModExtension
    {
        public float hibernationDrainFactor = 0.02f;
        public float starvedThreshold = 0.15f;
        public float torporThreshold = 0.001f;
        public float torporRecoveryThreshold = 0.08f;

        public float lowReserveThreshold = 0.40f;
        public float highReserveThreshold = 0.70f;
        public float depletedRegenerationFactor = 0.05f;
        public float starvingRegenerationFactor = 0.25f;
        public float lowRegenerationFactor = 0.60f;
        public float healthyRegenerationFactor = 1.00f;
        public float highRegenerationFactor = 1.60f;
        public float hibernatingRegenerationFactor = 0.12f;

        public int injuryHealIntervalTicks = 2500;
        public float baseInjuryHealAmount = 1.5f;
        public float injuryLifeForceCostPerSeverity = 0.0025f;

        public int limbRegrowthIntervalTicks = 60000;
        public float limbRegrowthMinimumReserve = 0.70f;
        public float limbRegrowthLifeForceCost = 0.25f;
    }

    /// <summary>
    /// Wraith Life Force deliberately uses RimWorld's Gene_Resource persistence/gizmo model but
    /// does NOT implement IGeneResourceDrain and does NOT call GeneResourceDrainUtility. The native
    /// hemogen helper applies Hemogen Craving at zero, which is sanguophage behavior and not Wraith
    /// biology. Drain/starvation/torpor are owned here instead.
    /// </summary>
    public class Gene_Resource_LifeForce : Gene_Resource
    {
        private const string StarvedDefName = "WNG_LifeForceStarved";
        private const string TorporDefName = "WNG_LifeForceTorpor";
        private const string HibernationDefName = "WNG_WraithHibernating";

        public WraithLifeForceSettingsExtension Settings =>
            def.GetModExtension<WraithLifeForceSettingsExtension>() ?? new WraithLifeForceSettingsExtension();

        public override float InitialResourceMax => 1f;
        public override float MinLevelForAlert => Settings.starvedThreshold;

        protected override Color BarColor => new Color(0.44f, 0.12f, 0.18f);
        protected override Color BarHighlightColor => new Color(0.70f, 0.24f, 0.30f);

        public bool IsHibernating => HasHediff(HibernationDefName);

        protected virtual bool SupportsDeliberateHibernation => true;

        public float RegenerationFactor
        {
            get
            {
                WraithLifeForceSettingsExtension tuning = Settings;
                if (IsHibernating)
                    return Math.Max(0f, tuning.hibernatingRegenerationFactor);
                if (Value <= tuning.torporThreshold)
                    return Math.Max(0f, tuning.depletedRegenerationFactor);
                if (Value < tuning.starvedThreshold)
                    return Math.Max(0f, tuning.starvingRegenerationFactor);
                if (Value < tuning.lowReserveThreshold)
                    return Math.Max(0f, tuning.lowRegenerationFactor);
                if (Value < tuning.highReserveThreshold)
                    return Math.Max(0f, tuning.healthyRegenerationFactor);
                return Math.Max(0f, tuning.highRegenerationFactor);
            }
        }

        public override void PostRemove()
        {
            CleanupOwnedHediffs();
            base.PostRemove();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || !Active || delta <= 0)
                return;

            WraithLifeForceSettingsExtension tuning = Settings;
            float drainPerDay = Math.Max(0f, def.resourceLossPerDay);
            float drainFactor = IsHibernating ? Math.Max(0f, tuning.hibernationDrainFactor) : 1f;
            if (drainPerDay > 0f && Value > 0f)
                Value -= drainPerDay * drainFactor * delta / 60000f;

            if (pawn.IsHashIntervalTick(60, delta))
                ReconcileDepletionState();
        }

        public void AddLifeForce(float amount)
        {
            if (amount > 0f)
                Value = Math.Min(Max, Value + amount);
            ReconcileDepletionState();
        }

        public bool CanSpend(float amount)
        {
            return amount <= 0f || Value + 0.000001f >= amount;
        }

        public bool TrySpend(float amount)
        {
            if (amount <= 0f)
                return true;
            if (!CanSpend(amount))
                return false;
            Value -= amount;
            ReconcileDepletionState();
            return true;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (!SupportsDeliberateHibernation || !Active || pawn == null || pawn.Faction != Faction.OfPlayer || pawn.Dead)
                yield break;

            Command_Action command = new Command_Action
            {
                defaultLabel = IsHibernating ? "Wake from hibernation" : "Enter hibernation",
                defaultDesc = IsHibernating
                    ? "Wake this Wraith from deep biological hibernation. If its Life Force is still critically depleted, starvation or torpor will immediately return."
                    : "Enter deep Wraith hibernation. The pawn becomes almost completely inert, but passive Life Force consumption falls to a tiny fraction of normal.",
                action = ToggleHibernation
            };
            yield return command;
        }

        private void ToggleHibernation()
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead)
                return;

            HediffDef hibernationDef = DefDatabase<HediffDef>.GetNamedSilentFail(HibernationDefName);
            if (hibernationDef == null)
                return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hibernationDef);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }
            else
            {
                pawn.health.AddHediff(hibernationDef);
                // Player-triggered hibernation presentation is fail-soft and owns no state.
                try
                {
                    if (pawn.Spawned && pawn.Map != null)
                        DefDatabase<SoundDef>.GetNamedSilentFail("WNG_WraithHibernate")
                            ?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Wraith hibernation sound failed: " + ex.Message);
                }
            }

            ReconcileDepletionState();
        }

        private void ReconcileDepletionState()
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead)
                return;

            HediffDef starvedDef = DefDatabase<HediffDef>.GetNamedSilentFail(StarvedDefName);
            HediffDef torporDef = DefDatabase<HediffDef>.GetNamedSilentFail(TorporDefName);
            if (starvedDef == null || torporDef == null)
                return;

            Hediff starved = pawn.health.hediffSet.GetFirstHediffOfDef(starvedDef);
            Hediff torpor = pawn.health.hediffSet.GetFirstHediffOfDef(torporDef);

            // Deliberate hibernation is its own dormant state. It should not stack the involuntary
            // starvation/torpor presentation on top of itself.
            if (IsHibernating)
            {
                if (starved != null)
                    pawn.health.RemoveHediff(starved);
                if (torpor != null)
                    pawn.health.RemoveHediff(torpor);
                return;
            }

            WraithLifeForceSettingsExtension tuning = Settings;
            if (Value <= tuning.torporThreshold)
            {
                if (starved != null)
                    pawn.health.RemoveHediff(starved);
                if (torpor == null)
                    pawn.health.AddHediff(torporDef);
                return;
            }

            if (torpor != null && Value >= tuning.torporRecoveryThreshold)
            {
                pawn.health.RemoveHediff(torpor);
                torpor = null;
            }

            if (torpor == null && Value < tuning.starvedThreshold)
            {
                if (starved == null)
                    pawn.health.AddHediff(starvedDef);
            }
            else if (Value >= tuning.starvedThreshold && starved != null)
            {
                pawn.health.RemoveHediff(starved);
            }
        }

        private void CleanupOwnedHediffs()
        {
            if (pawn?.health?.hediffSet == null)
                return;

            foreach (string defName in new[] { StarvedDefName, TorporDefName, HibernationDefName })
            {
                HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                Hediff existing = hediffDef == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (existing != null)
                    pawn.health.RemoveHediff(existing);
            }
        }

        private bool HasHediff(string defName)
        {
            if (pawn?.health?.hediffSet == null)
                return false;
            HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            return hediffDef != null && pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef) != null;
        }
    }

    public sealed class GeneGizmo_Resource_LifeForce : GeneGizmo_Resource
    {
        private static bool draggingBar;
        private readonly Gene_Resource_LifeForce lifeForceGene;

        public GeneGizmo_Resource_LifeForce(
            Gene_Resource_LifeForce gene,
            List<IGeneResourceDrain> drainGenes,
            Color barColor,
            Color barHighlightColor)
            : base(gene, drainGenes, barColor, barHighlightColor)
        {
            lifeForceGene = gene;
        }

        protected override bool DraggingBar
        {
            get => draggingBar;
            set => draggingBar = value;
        }

        protected override string GetTooltip()
        {
            if (lifeForceGene == null)
                return string.Empty;

            return $"{lifeForceGene.def.resourceLabel}: {lifeForceGene.ValuePercent:P0}\n{lifeForceGene.def.resourceDescription}";
        }
    }

    public sealed class Gene_WraithRegeneration : Gene
    {
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null || !Active)
                return;

            Gene_Resource_LifeForce lifeForce = pawn.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            if (lifeForce == null || !lifeForce.Active)
                return;

            WraithLifeForceSettingsExtension tuning = lifeForce.Settings;
            int injuryInterval = Math.Max(1, tuning.injuryHealIntervalTicks);
            int limbInterval = Math.Max(1, tuning.limbRegrowthIntervalTicks);

            if (pawn.IsHashIntervalTick(injuryInterval, delta))
                HealOneInjury(lifeForce, tuning);

            if (lifeForce.Value >= tuning.limbRegrowthMinimumReserve &&
                pawn.IsHashIntervalTick(limbInterval, delta))
            {
                TryRegrowOneMissingPart(lifeForce, tuning);
            }
        }

        private void HealOneInjury(Gene_Resource_LifeForce lifeForce, WraithLifeForceSettingsExtension tuning)
        {
            Hediff_Injury injury = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(x => !x.IsPermanent() && x.Severity > 0f)
                .OrderByDescending(x => x.Severity)
                .FirstOrDefault();
            if (injury == null)
                return;

            float desiredHeal = Math.Max(0f, tuning.baseInjuryHealAmount) * lifeForce.RegenerationFactor;
            if (desiredHeal <= 0f)
                return;

            float costPerSeverity = Math.Max(0f, tuning.injuryLifeForceCostPerSeverity);
            if (costPerSeverity > 0f)
                desiredHeal = Math.Min(desiredHeal, lifeForce.Value / costPerSeverity);

            float actualHeal = Math.Min(injury.Severity, desiredHeal);
            if (actualHeal <= 0f)
                return;

            float lifeForceCost = actualHeal * costPerSeverity;
            if (!lifeForce.CanSpend(lifeForceCost))
                return;

            injury.Heal(actualHeal);
            lifeForce.TrySpend(lifeForceCost);
        }

        private void TryRegrowOneMissingPart(Gene_Resource_LifeForce lifeForce, WraithLifeForceSettingsExtension tuning)
        {
            float cost = Math.Max(0f, tuning.limbRegrowthLifeForceCost);
            if (!lifeForce.CanSpend(cost))
                return;

            List<BodyPartRecord> presentParts = pawn.health.hediffSet.GetNotMissingParts().ToList();
            BodyPartRecord partToRestore = pawn.def.race.body.AllParts
                .Where(part => pawn.health.hediffSet.PartIsMissing(part)
                    && part.parent != null
                    && presentParts.Contains(part.parent)
                    && !pawn.health.hediffSet.AncestorHasDirectlyAddedParts(part))
                .OrderByDescending(part => part.def.GetMaxHealth(pawn))
                .FirstOrDefault();

            if (partToRestore == null)
                return;

            pawn.health.RestorePart(partToRestore);
            if (!lifeForce.TrySpend(cost))
                return;

            // Presentation is fail-soft and commits only after the exact missing part and Life Force transaction.
            try
            {
                if (pawn.Spawned && pawn.Map != null)
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_WraithReturnLife")
                        ?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Wraith return-life sound failed: " + ex.Message);
            }
        }
    }
}
