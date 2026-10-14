using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Tuning for the clean human-form nanite physiology foundation. The values are kept
    /// on the Nanite Reserve GeneDef so later balance work can alter the physiology without
    /// spreading magic numbers across resource and reconstruction code.
    /// </summary>
    public sealed class NaniteReserveSettingsExtension : DefModExtension
    {
        public float initialReserve = 0.50f;
        public float reserveGainPerFoodLevel = 0.35f;

        public int reservePoweredInjuryIntervalTicks = 450;
        public float reservePoweredInjuryCost = 0.015f;
        public float reservePoweredInjuryHeal = 1.75f;

        public int emergencyInjuryIntervalTicks = 1800;
        public float emergencyInjuryHeal = 1.25f;

        public int reservePoweredPartIntervalTicks = 30000;
        public float reservePoweredPartCost = 0.25f;
        public int emergencyPartIntervalTicks = 90000;
    }

    /// <summary>
    /// Marker for synthetic human-form physiology. Need suppression and food-poisoning immunity
    /// are Def-owned so this class intentionally carries no parallel state machine.
    /// </summary>
    public sealed class Gene_NaniteBody : Gene
    {
    }

    /// <summary>
    /// Native Biotech resource-gene state is the sole Nanite Reserve owner. There is no passive
    /// reserve drain and no separately deep-saved Gene. Ordinary Food-need increases are observed
    /// as feedstock input, matching the accepted food-fuelled reserve contract.
    /// </summary>
    public sealed class Gene_Resource_NaniteReserve : Gene_Resource
    {
        private float lastObservedFoodLevel = -1f;

        public NaniteReserveSettingsExtension Settings =>
            def.GetModExtension<NaniteReserveSettingsExtension>() ?? new NaniteReserveSettingsExtension();

        public override float InitialResourceMax => 1f;
        public override float MinLevelForAlert => 0.10f;

        protected override Color BarColor => new Color(0.22f, 0.70f, 0.78f);
        protected override Color BarHighlightColor => new Color(0.45f, 0.92f, 0.98f);

        public override void PostAdd()
        {
            base.PostAdd();
            Value = Math.Min(Max, Math.Max(0f, Settings.initialReserve));
            lastObservedFoodLevel = pawn?.needs?.food?.CurLevel ?? -1f;
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || !Active || delta <= 0 || !pawn.IsHashIntervalTick(60, delta))
                return;

            Need_Food food = pawn.needs?.food;
            if (food == null)
            {
                lastObservedFoodLevel = -1f;
                return;
            }

            float current = food.CurLevel;
            if (lastObservedFoodLevel >= 0f)
            {
                float foodGain = current - lastObservedFoodLevel;
                if (foodGain > 0.001f && Value < Max)
                {
                    float reserveGain = foodGain * Math.Max(0f, Settings.reserveGainPerFoodLevel);
                    Value = Math.Min(Max, Value + reserveGain);
                }
            }

            lastObservedFoodLevel = current;
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
            Value = Math.Max(0f, Value - amount);
            return true;
        }

        /// <summary>
        /// Commits player-directed physical feedstock assimilation without letting the ordinary
        /// Food-observer count the same synthetic meal a second time on its next sample.
        /// </summary>
        public void AddAssimilatedFeedstock(float foodLevelGain, float reserveGain)
        {
            Need_Food food = pawn?.needs?.food;
            if (food != null && foodLevelGain > 0f)
                food.CurLevel = Math.Min(food.MaxLevel, food.CurLevel + foodLevelGain);

            if (reserveGain > 0f)
                Value = Math.Min(Max, Value + reserveGain);

            lastObservedFoodLevel = food?.CurLevel ?? -1f;
        }
    }

    public sealed class GeneGizmo_Resource_NaniteReserve : GeneGizmo_Resource
    {
        private static bool draggingBar;
        private readonly Gene_Resource_NaniteReserve reserveGene;

        public GeneGizmo_Resource_NaniteReserve(
            Gene_Resource_NaniteReserve gene,
            List<IGeneResourceDrain> drainGenes,
            Color barColor,
            Color barHighlightColor)
            : base(gene, drainGenes, barColor, barHighlightColor)
        {
            reserveGene = gene;
        }

        protected override bool DraggingBar
        {
            get => draggingBar;
            set => draggingBar = value;
        }

        protected override string GetTooltip()
        {
            if (reserveGene == null)
                return string.Empty;

            return $"{reserveGene.def.resourceLabel}: {reserveGene.ValuePercent:P0}\n{reserveGene.def.resourceDescription}";
        }
    }

    /// <summary>
    /// Reconstruction is intentionally separate from resource storage. Sufficient reserve buys
    /// the accepted fast repair cadence; inability to pay falls back to slower emergency repair.
    /// EMP disruption suspends both paths rather than creating a second reserve/EMP state system.
    /// </summary>
    public sealed class Gene_NaniteReconstruction : Gene
    {
        private const string DisruptionDefName = "WNG_NaniteEMPDisruption";

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || !Active || delta <= 0 || pawn.health?.hediffSet == null)
                return;
            if (HasEmpDisruption())
                return;

            Gene_Resource_NaniteReserve reserve = pawn.genes?.GetFirstGeneOfType<Gene_Resource_NaniteReserve>();
            if (reserve == null || !reserve.Active)
                return;

            NaniteReserveSettingsExtension tuning = reserve.Settings;
            float collectiveMultiplier = AsuranCollectiveUtility.ReconstructionMultiplier(pawn);
            bool canPowerInjuryRepair = reserve.CanSpend(Math.Max(0f, tuning.reservePoweredInjuryCost));
            int injuryInterval = Math.Max(1, canPowerInjuryRepair
                ? tuning.reservePoweredInjuryIntervalTicks
                : tuning.emergencyInjuryIntervalTicks);
            if (pawn.IsHashIntervalTick(injuryInterval, delta))
                RepairOneInjury(reserve, tuning, canPowerInjuryRepair, collectiveMultiplier);

            bool canPowerPartRepair = reserve.CanSpend(Math.Max(0f, tuning.reservePoweredPartCost));
            int basePartInterval = Math.Max(1, canPowerPartRepair
                ? tuning.reservePoweredPartIntervalTicks
                : tuning.emergencyPartIntervalTicks);
            int partInterval = Math.Max(1, (int)(basePartInterval / Math.Max(1f, collectiveMultiplier)));
            if (pawn.IsHashIntervalTick(partInterval, delta))
                ReconstructOnePart(reserve, tuning, canPowerPartRepair);
        }

        private void RepairOneInjury(
            Gene_Resource_NaniteReserve reserve,
            NaniteReserveSettingsExtension tuning,
            bool reservePowered,
            float collectiveMultiplier)
        {
            Hediff_Injury injury = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(x => x != null && !x.IsPermanent() && x.Severity > 0f)
                .OrderByDescending(x => x.Severity)
                .FirstOrDefault();
            if (injury == null)
                return;

            float requestedHeal = Math.Max(0f, reservePowered
                ? tuning.reservePoweredInjuryHeal
                : tuning.emergencyInjuryHeal) * Math.Max(1f, collectiveMultiplier);
            float actualHeal = Math.Min(injury.Severity, requestedHeal);
            if (actualHeal <= 0f)
                return;

            float cost = reservePowered ? Math.Max(0f, tuning.reservePoweredInjuryCost) : 0f;
            if (reservePowered && !reserve.CanSpend(cost))
                return;

            float before = injury.Severity;
            injury.Heal(actualHeal);
            if (reservePowered && injury.Severity < before)
                reserve.TrySpend(cost);
        }

        private void ReconstructOnePart(
            Gene_Resource_NaniteReserve reserve,
            NaniteReserveSettingsExtension tuning,
            bool reservePowered)
        {
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

            float cost = reservePowered ? Math.Max(0f, tuning.reservePoweredPartCost) : 0f;
            if (reservePowered && !reserve.CanSpend(cost))
                return;

            pawn.health.RestorePart(partToRestore);
            if (reservePowered)
                reserve.TrySpend(cost);
        }

        private bool HasEmpDisruption()
        {
            HediffDef disruption = DefDatabase<HediffDef>.GetNamedSilentFail(DisruptionDefName);
            return disruption != null && pawn.health.hediffSet.GetFirstHediffOfDef(disruption) != null;
        }
    }

    /// <summary>
    /// Human-form nanites are synthetic enough that EMP is a deliberate custom weakness even
    /// though the pawn is humanlike. A real EMP DamageDef hit refreshes one disruption Hediff.
    /// EMP Shunt remains mitigation only: it shortens that real disruption instead of preventing it.
    /// </summary>
    public sealed class Gene_EMPSensitiveNanites : Gene
    {
        private const string ReceiverDefName = "WNG_NaniteEMPReceiver";

        public override void PostAdd()
        {
            base.PostAdd();
            AddReceiverIfMissing();
        }

        public override void PostRemove()
        {
            RemoveOwnedHediff(ReceiverDefName);
            RemoveOwnedHediff("WNG_NaniteEMPDisruption");
            base.PostRemove();
        }

        private void AddReceiverIfMissing()
        {
            if (pawn?.health?.hediffSet == null)
                return;
            HediffDef receiver = DefDatabase<HediffDef>.GetNamedSilentFail(ReceiverDefName);
            if (receiver != null && pawn.health.hediffSet.GetFirstHediffOfDef(receiver) == null)
                pawn.health.AddHediff(receiver);
        }

        private void RemoveOwnedHediff(string defName)
        {
            if (pawn?.health?.hediffSet == null)
                return;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            Hediff existing = def == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
        }
    }

    public sealed class HediffCompProperties_NaniteEMPReceiver : HediffCompProperties
    {
        public HediffCompProperties_NaniteEMPReceiver()
        {
            compClass = typeof(HediffComp_NaniteEMPReceiver);
        }
    }

    public sealed class HediffComp_NaniteEMPReceiver : HediffComp
    {
        private const string DisruptionDefName = "WNG_NaniteEMPDisruption";
        private const string EmpShuntDefName = "WNG_EMPShuntLattice";
        private const float EmpShuntDurationFactor = 0.50f;
        private const int EmpShuntMinimumTicks = 600;

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);
            Pawn pawn = parent?.pawn;
            if (pawn?.health?.hediffSet == null || pawn.Dead || dinfo.Def != DamageDefOf.EMP)
                return;

            HediffDef disruptionDef = DefDatabase<HediffDef>.GetNamedSilentFail(DisruptionDefName);
            if (disruptionDef == null)
                return;

            // Remove/re-add so every real EMP hit refreshes the normal Def-owned random disruption
            // window. Only after that state exists does EMP Shunt shorten it; the hit is never ignored.
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(disruptionDef);
            if (existing != null)
                pawn.health.RemoveHediff(existing);

            Hediff disruption = pawn.health.AddHediff(disruptionDef);
            if (!HasEmpShunt(pawn) || disruption == null)
                return;

            HediffComp_Disappears disappears = disruption.TryGetComp<HediffComp_Disappears>();
            if (disappears == null)
                return;

            int shortened = Math.Max(
                EmpShuntMinimumTicks,
                Mathf.RoundToInt(disappears.EffectiveTicksToDisappear * EmpShuntDurationFactor));
            disappears.SetDuration(shortened);
        }

        private static bool HasEmpShunt(Pawn pawn)
        {
            HediffDef shunt = DefDatabase<HediffDef>.GetNamedSilentFail(EmpShuntDefName);
            return shunt != null && pawn.health.hediffSet.GetFirstHediffOfDef(shunt) != null;
        }
    }
}
