using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AbilityHiveSense : CompProperties_AbilityEffect
    {
        public float radius = 40f;
        public HediffDef focusHediff;

        public CompProperties_AbilityHiveSense()
        {
            compClass = typeof(CompAbilityEffect_HiveSense);
        }
    }

    /// <summary>
    /// Local Wraith mind-sense. This deliberately reports aggregate information rather than
    /// revealing exact cells: it is telepathic awareness, not a wallhack or map-fog bypass.
    /// Wraith-linked identity is the biological telepathy gene, so future hybrids can join the
    /// same grammar without special-casing a xenotype name.
    /// </summary>
    public sealed class CompAbilityEffect_HiveSense : CompAbilityEffect
    {
        public new CompProperties_AbilityHiveSense Props => (CompProperties_AbilityHiveSense)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            if (!WraithTelepathyUtility.IsTelepathicWraith(caster))
            {
                if (throwMessages && caster != null)
                    Messages.Message("Hive Sense requires active Wraith telepathy.", caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            Map map = caster?.Map;
            if (caster == null || map == null)
                return;

            // Restore the historical focus state without removing the newer local-mind scan.
            // Refresh rather than stack so repeated casts extend one bounded 15,000-tick effect.
            HediffDef focus = Props.focusHediff;
            if (focus != null && caster.health?.hediffSet != null)
            {
                Hediff existingFocus = caster.health.hediffSet.GetFirstHediffOfDef(focus);
                if (existingFocus != null)
                    caster.health.RemoveHediff(existingFocus);
                caster.health.AddHediff(focus);
            }

            float radiusSquared = Props.radius * Props.radius;
            var sensed = map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && p != caster && !p.Dead &&
                            WraithTelepathyUtility.IsTelepathicWraith(p) &&
                            (p.Position - caster.Position).LengthHorizontalSquared <= radiusSquared)
                .ToList();

            if (sensed.Count == 0)
            {
                Messages.Message(
                    "Hive Sense finds no other Wraith-linked mind within " + Mathf.RoundToInt(Props.radius) + " cells.",
                    caster,
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }

            int hostile = sensed.Count(p => p.HostileTo(caster));
            int allied = sensed.Count(p => p.Faction != null && p.Faction == caster.Faction);
            int other = sensed.Count - hostile - allied;
            float nearest = sensed.Min(p => (p.Position - caster.Position).LengthHorizontal);

            Messages.Message(
                "Hive Sense detects " + sensed.Count + " Wraith-linked " + (sensed.Count == 1 ? "mind" : "minds") +
                " within " + Mathf.RoundToInt(Props.radius) + " cells: " + allied + " allied, " + hostile +
                " hostile, " + other + " unaligned. Nearest presence is about " + Mathf.RoundToInt(nearest) + " cells away.",
                caster,
                hostile > 0 ? MessageTypeDefOf.CautionInput : MessageTypeDefOf.NeutralEvent,
                false);
        }
    }

    public sealed class CompProperties_AbilityWraithHallucination : CompProperties_AbilityEffect
    {
        public HediffDef hallucinationHediff;

        public CompProperties_AbilityWraithHallucination()
        {
            compClass = typeof(CompAbilityEffect_WraithHallucination);
        }
    }

    /// <summary>
    /// Full-Wraith sensory intrusion. This refreshes one temporary Hediff on the same living
    /// biological target. It deliberately changes no faction, guest/slave state, age, Life Force,
    /// genes, xenotype or pawn identity.
    /// </summary>
    public sealed class CompAbilityEffect_WraithHallucination : CompAbilityEffect
    {
        public new CompProperties_AbilityWraithHallucination Props =>
            (CompProperties_AbilityWraithHallucination)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Pawn victim = target.Pawn;

            if (!WraithTelepathyUtility.IsTelepathicWraith(caster))
            {
                if (throwMessages && caster != null)
                    Messages.Message(
                        "Hallucination requires active Wraith telepathy.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        false);
                return false;
            }

            if (victim == null ||
                victim == caster ||
                victim.Dead ||
                victim.health?.hediffSet == null ||
                victim.RaceProps == null ||
                !victim.RaceProps.IsFlesh ||
                victim.RaceProps.IsMechanoid)
            {
                if (throwMessages && caster != null)
                    Messages.Message(
                        "Hallucination requires another living biological target.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        false);
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            Pawn victim = target.Pawn;
            HediffDef hediffDef = Props.hallucinationHediff;
            if (!WraithTelepathyUtility.IsTelepathicWraith(caster) ||
                victim == null ||
                victim == caster ||
                victim.Dead ||
                victim.health?.hediffSet == null ||
                victim.RaceProps == null ||
                !victim.RaceProps.IsFlesh ||
                victim.RaceProps.IsMechanoid ||
                hediffDef == null)
            {
                return;
            }

            // Refresh rather than stack. Removing and re-adding recreates the disappearing comp's
            // bounded 12k–24k duration while preserving the exact target Pawn.
            Hediff existing = victim.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
                victim.health.RemoveHediff(existing);

            victim.health.AddHediff(hediffDef);
        }
    }

    public sealed class CompProperties_AbilityQueensCommand : CompProperties_AbilityEffect
    {
        public float radius = 10f;
        public HediffDef commandHediff;

        public CompProperties_AbilityQueensCommand()
        {
            compClass = typeof(CompAbilityEffect_QueensCommand);
        }
    }

    /// <summary>
    /// Queen-only allied Hive synchronization. It never changes faction/controller state and only
    /// affects living allied pawns carrying active Wraith telepathy, preserving exact pawn identity.
    /// </summary>
    public sealed class CompAbilityEffect_QueensCommand : CompAbilityEffect
    {
        public new CompProperties_AbilityQueensCommand Props => (CompProperties_AbilityQueensCommand)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            if (caster == null || caster.Dead || caster.kindDef?.defName != "WNG_WraithQueen" ||
                !WraithTelepathyUtility.IsTelepathicWraith(caster))
            {
                if (throwMessages && caster != null)
                    Messages.Message("Queen's Command requires a living Wraith Queen with active telepathy.", caster, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            Map map = caster?.Map;
            HediffDef hediffDef = Props.commandHediff;
            if (caster == null || map == null || hediffDef == null)
                return;

            float radiusSquared = Props.radius * Props.radius;
            int affected = 0;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.Faction != caster.Faction ||
                    !WraithTelepathyUtility.IsTelepathicWraith(pawn) ||
                    (pawn.Position - caster.Position).LengthHorizontalSquared > radiusSquared)
                    continue;

                Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
                if (existing != null)
                    pawn.health.RemoveHediff(existing);
                pawn.health?.AddHediff(hediffDef);
                affected++;
            }

            Messages.Message(
                "The Queen's command synchronizes " + affected + " nearby Wraith-linked " + (affected == 1 ? "mind" : "minds") + ".",
                caster,
                MessageTypeDefOf.PositiveEvent,
                false);
        }
    }

    internal static class WraithTelepathyUtility
    {
        private const string TelepathyGeneDefName = "WNG_WraithTelepathy";

        public static bool IsTelepathicWraith(Pawn pawn)
        {
            if (pawn?.genes == null || pawn.Dead)
                return false;

            GeneDef telepathy = DefDatabase<GeneDef>.GetNamedSilentFail(TelepathyGeneDefName);
            if (telepathy == null)
                return false;

            Gene gene = pawn.genes.GetGene(telepathy);
            return gene != null && gene.Active;
        }
    }
}
