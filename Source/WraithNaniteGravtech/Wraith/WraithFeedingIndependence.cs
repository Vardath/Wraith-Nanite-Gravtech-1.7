using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class WraithFeedingIndependenceUtility
    {
        public const string SuccessDefName = "WNG_FeedingIndependent";
        public const string RejectionDefName = "WNG_FeedingIndependenceRejection";
        public const string LifeForceGeneDefName = "WNG_LifeForceMetabolism";
        public const float FirstGenerationSuccessChance = 0.40f;

        public static bool IsFeedingIndependent(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(SuccessDefName);
            return def != null && pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) != null;
        }

        public static Gene LifeForceGene(Pawn pawn)
        {
            GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(LifeForceGeneDefName);
            return def == null ? null : pawn?.genes?.GetGene(def);
        }

        public static bool Eligible(Pawn pawn)
        {
            return pawn != null
                && !pawn.Dead
                && WraithHiveEcologyUtility.IsWraith(pawn)
                && !WraithRetroviralUtility.IsHumanized(pawn)
                && !IsFeedingIndependent(pawn)
                && LifeForceGene(pawn) != null;
        }

        public static bool TryCommitSuccess(Pawn pawn)
        {
            if (!Eligible(pawn) || pawn.genes == null || pawn.health?.hediffSet == null)
                return false;

            HediffDef successDef = DefDatabase<HediffDef>.GetNamedSilentFail(SuccessDefName);
            Gene lifeForce = LifeForceGene(pawn);
            if (successDef == null || lifeForce == null)
                return false;

            GeneDef originalLifeForceDef = lifeForce.def;
            if (originalLifeForceDef == null)
                return false;

            bool wasXenogene = pawn.genes.IsXenogene(lifeForce);
            float oldLifeForce = lifeForce is Gene_Resource_LifeForce resource ? resource.Value : -1f;

            pawn.genes.RemoveGene(lifeForce);
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            pawn.health.AddHediff(successDef);

            if (!IsFeedingIndependent(pawn) || LifeForceGene(pawn) != null)
            {
                Hediff marker = pawn.health.hediffSet.GetFirstHediffOfDef(successDef);
                if (marker != null)
                    pawn.health.RemoveHediff(marker);

                Gene restored = pawn.genes.AddGene(originalLifeForceDef, wasXenogene);
                if (restored is Gene_Resource_LifeForce restoredResource && oldLifeForce >= 0f)
                    restoredResource.Value = System.Math.Min(restoredResource.Max, System.Math.Max(0f, oldLifeForce));
                pawn.needs?.AddOrRemoveNeedsAsAppropriate();
                return false;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    pawn.LabelShortCap + " survived the metabolic rewrite. Ordinary digestion has returned and Wraith feeding is no longer required.",
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
            return true;
        }

        public static void ApplyRejection(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead)
                return;

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(RejectionDefName);
            if (def == null)
                return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
            pawn.health.AddHediff(def);

            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    pawn.LabelShortCap + " rejected the feeding-independence vector and is suffering catastrophic systemic inflammation.",
                    pawn,
                    MessageTypeDefOf.ThreatBig,
                    historical: false);
            }
        }
    }

    public sealed class Recipe_AdministerFeedingIndependenceVector : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return WraithFeedingIndependenceUtility.Eligible(pawn) && base.AvailableOnNow(thing, part);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || !WraithHiveEcologyUtility.IsWraith(pawn))
                return "Feeding-independence therapy requires an active Wraith phenotype.";
            if (WraithRetroviralUtility.IsHumanized(pawn))
                return "Complete or allow relapse from retroviral humanization before attempting permanent metabolic rewriting.";
            if (WraithFeedingIndependenceUtility.IsFeedingIndependent(pawn))
                return "This Wraith is already feeding-independent.";
            if (WraithFeedingIndependenceUtility.LifeForceGene(pawn) == null)
                return "This Wraith has no active Life Force metabolism to rewrite.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (!WraithFeedingIndependenceUtility.Eligible(pawn))
                return;

            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                    return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            // Resolve every custom Def before the biological outcome roll. Missing content must not
            // consume the Wraith's existing Life Force metabolism or create a half-committed cure.
            if (DefDatabase<HediffDef>.GetNamedSilentFail(WraithFeedingIndependenceUtility.SuccessDefName) == null
                || DefDatabase<HediffDef>.GetNamedSilentFail(WraithFeedingIndependenceUtility.RejectionDefName) == null
                || DefDatabase<GeneDef>.GetNamedSilentFail(WraithFeedingIndependenceUtility.LifeForceGeneDefName) == null)
            {
                Log.Error("WNG feeding-independence therapy could not resolve its required Defs; no metabolic rewrite was attempted.");
                return;
            }

            if (Rand.Chance(WraithFeedingIndependenceUtility.FirstGenerationSuccessChance))
            {
                if (!WraithFeedingIndependenceUtility.TryCommitSuccess(pawn))
                    Log.Error("WNG feeding-independence therapy selected success but could not commit the gene transaction; the original Life Force metabolism was restored.");
                return;
            }

            WraithFeedingIndependenceUtility.ApplyRejection(pawn);
        }
    }
}
