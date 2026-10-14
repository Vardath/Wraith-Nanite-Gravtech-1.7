using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public enum ReplicatorMaterialGrade
    {
        Fragile = 0,
        Standard = 1,
        Hardened = 2,
        Ultra = 3
    }

    public sealed class CompProperties_ReplicatorMaterialProfile : CompProperties
    {
        public CompProperties_ReplicatorMaterialProfile()
        {
            compClass = typeof(CompReplicatorMaterialProfile);
        }
    }

    /// <summary>
    /// Save-persistent physical material quality for block Replicator bodies.
    ///
    /// This is not stored matter, adaptation knowledge, armor-learning state, or a replication
    /// currency. It records only the durability of the real material from which this exact body
    /// was assembled and keeps that physical property across reconfiguration/splitting.
    /// </summary>
    public sealed class CompReplicatorMaterialProfile : ThingComp
    {
        private int materialGrade = (int)ReplicatorMaterialGrade.Standard;

        private static readonly string[] GradeHediffDefNames =
        {
            "WNG_ReplicatorMaterialFragile",
            "WNG_ReplicatorMaterialStandard",
            "WNG_ReplicatorMaterialHardened",
            "WNG_ReplicatorMaterialUltra"
        };

        public ReplicatorMaterialGrade Grade =>
            (ReplicatorMaterialGrade)Math.Max(
                (int)ReplicatorMaterialGrade.Fragile,
                Math.Min((int)ReplicatorMaterialGrade.Ultra, materialGrade));

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            SynchronizeHediff();
        }

        public void SetGrade(ReplicatorMaterialGrade grade)
        {
            int clamped = Math.Max(
                (int)ReplicatorMaterialGrade.Fragile,
                Math.Min((int)ReplicatorMaterialGrade.Ultra, (int)grade));
            materialGrade = clamped;
            SynchronizeHediff();
        }

        public void InheritFrom(CompReplicatorMaterialProfile source)
        {
            SetGrade(source?.Grade ?? ReplicatorMaterialGrade.Standard);
        }

        public void MergeFrom(IEnumerable<Pawn> donors)
        {
            if (donors == null)
            {
                SetGrade(ReplicatorMaterialGrade.Standard);
                return;
            }

            int total = 0;
            int count = 0;
            foreach (Pawn donor in donors)
            {
                CompReplicatorMaterialProfile profile = donor?.TryGetComp<CompReplicatorMaterialProfile>();
                total += (int)(profile?.Grade ?? ReplicatorMaterialGrade.Standard);
                count++;
            }

            if (count <= 0)
            {
                SetGrade(ReplicatorMaterialGrade.Standard);
                return;
            }

            // Conservative floor-average: recombination can preserve donor material quality but can
            // never improve it for free. Two Hardened donors stay Hardened; Hardened+Ultra stays
            // Hardened; Fragile+Ultra becomes Standard rather than manufacturing superior matter.
            int merged = total / count;
            SetGrade((ReplicatorMaterialGrade)Math.Max(
                (int)ReplicatorMaterialGrade.Fragile,
                Math.Min((int)ReplicatorMaterialGrade.Ultra, merged)));
        }

        public override string CompInspectStringExtra()
        {
            return "Replication material: " + GradeLabel(Grade);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(
                ref materialGrade,
                "wngReplicatorMaterialGrade",
                (int)ReplicatorMaterialGrade.Standard);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                materialGrade = Math.Max(
                    (int)ReplicatorMaterialGrade.Fragile,
                    Math.Min((int)ReplicatorMaterialGrade.Ultra, materialGrade));
        }

        private void SynchronizeHediff()
        {
            Pawn pawn = parent as Pawn;
            if (pawn?.health?.hediffSet == null)
                return;

            HediffDef wanted = DefDatabase<HediffDef>.GetNamedSilentFail(
                GradeHediffDefNames[(int)Grade]);

            for (int i = 0; i < GradeHediffDefNames.Length; i++)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(GradeHediffDefNames[i]);
                if (def == null)
                    continue;

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing != null && def != wanted)
                    pawn.health.RemoveHediff(existing);
            }

            if (wanted != null && pawn.health.hediffSet.GetFirstHediffOfDef(wanted) == null)
                pawn.health.AddHediff(wanted);
        }

        private static string GradeLabel(ReplicatorMaterialGrade grade)
        {
            switch (grade)
            {
                case ReplicatorMaterialGrade.Fragile: return "fragile";
                case ReplicatorMaterialGrade.Hardened: return "hardened";
                case ReplicatorMaterialGrade.Ultra: return "ultra-dense";
                default: return "standard";
            }
        }
    }

    public static class ReplicatorMaterialProfileUtility
    {
        private const float FragileMaxHitPointsFactor = 0.80f;
        private const float HardenedMaxHitPointsFactor = 1.25f;
        private const float UltraMaxHitPointsFactor = 2.00f;

        public static ReplicatorMaterialGrade GradeFromSource(Thing source)
        {
            ThingDef material = source?.Stuff;
            if (material == null && source?.def?.IsStuff == true)
                material = source.def;

            // Fixed-material objects have no trustworthy Stuff identity. Keep them Standard rather
            // than inferring exceptional matter from tech level, price, label or mod-specific names.
            if (material?.stuffProps?.statFactors == null)
                return ReplicatorMaterialGrade.Standard;

            float maxHitPointsFactor = 1f;
            List<StatModifier> factors = material.stuffProps.statFactors;
            for (int i = 0; i < factors.Count; i++)
            {
                StatModifier modifier = factors[i];
                if (modifier != null && modifier.stat == StatDefOf.MaxHitPoints)
                {
                    maxHitPointsFactor = modifier.value;
                    break;
                }
            }

            if (maxHitPointsFactor < FragileMaxHitPointsFactor)
                return ReplicatorMaterialGrade.Fragile;
            if (maxHitPointsFactor >= UltraMaxHitPointsFactor)
                return ReplicatorMaterialGrade.Ultra;
            if (maxHitPointsFactor >= HardenedMaxHitPointsFactor)
                return ReplicatorMaterialGrade.Hardened;
            return ReplicatorMaterialGrade.Standard;
        }
    }
}
