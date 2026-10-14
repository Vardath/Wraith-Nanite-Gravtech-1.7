using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class SpecialThingFilterWorker_NonWraithCorpse : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            Corpse corpse = t as Corpse;
            return corpse != null && !WraithHiveEcologyUtility.IsWraith(corpse.InnerPawn);
        }

        public override bool AlwaysMatches(ThingDef def)
        {
            return false;
        }
    }

    public sealed class Recipe_ExtractWraithEnzyme : Recipe_Surgery
    {
        private const float LifeForceCost = 0.18f;
        private const string EnzymeDefName = "WNG_RawWraithEnzyme";

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || !WraithHiveEcologyUtility.IsWraith(pawn) || pawn.Dead)
                return false;

            if (!base.AvailableOnNow(thing, part))
                return false;

            Gene_Resource_LifeForce lifeForce = pawn.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            return lifeForce != null && lifeForce.CanSpend(LifeForceCost);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || !WraithHiveEcologyUtility.IsWraith(pawn))
                return "WNG enzyme extraction requires a living Wraith.";

            Gene_Resource_LifeForce lifeForce = pawn.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            if (lifeForce == null || !lifeForce.CanSpend(LifeForceCost))
                return "The Wraith does not have enough Life Force for safe enzyme extraction.";

            return base.AvailableReport(thing, part);
        }

        public override bool CompletableEver(Pawn surgeryTarget)
        {
            Gene_Resource_LifeForce lifeForce = surgeryTarget?.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            return WraithHiveEcologyUtility.IsWraith(surgeryTarget)
                && lifeForce != null
                && lifeForce.CanSpend(LifeForceCost)
                && base.CompletableEver(surgeryTarget);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (!WraithHiveEcologyUtility.IsWraith(pawn))
                return;

            Gene_Resource_LifeForce lifeForce = pawn.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            if (lifeForce == null || !lifeForce.CanSpend(LifeForceCost))
            {
                Messages.Message("Wraith enzyme extraction stopped: the donor no longer has enough Life Force.", pawn, MessageTypeDefOf.NeutralEvent, historical: false);
                return;
            }

            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                    return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (!lifeForce.TrySpend(LifeForceCost))
                return;

            ThingDef enzymeDef = DefDatabase<ThingDef>.GetNamedSilentFail(EnzymeDefName);
            if (enzymeDef == null)
            {
                lifeForce.AddLifeForce(LifeForceCost);
                Log.Error("WNG Wraith enzyme extraction could not find " + EnzymeDefName + "; Life Force cost was rolled back.");
                return;
            }

            Thing enzyme = ThingMaker.MakeThing(enzymeDef);
            if (!GenPlace.TryPlaceThing(enzyme, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near))
            {
                lifeForce.AddLifeForce(LifeForceCost);
                enzyme.Destroy();
                Log.Error("WNG Wraith enzyme extraction could not place its product; Life Force cost was rolled back.");
                return;
            }

            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -35);
        }
    }

    public sealed class IngestionOutcomeDoer_WraithEnzymeWeaning : IngestionOutcomeDoer
    {
        public float severityReduction = 0.12f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn?.health?.hediffSet == null || severityReduction <= 0f)
                return;

            HediffDef addictionDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_WraithEnzymeAddiction");
            Hediff addiction = addictionDef == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(addictionDef);
            if (addiction == null)
                return;

            HealthUtility.AdjustSeverity(pawn, addictionDef, -severityReduction * System.Math.Max(1, ingestedCount));
        }
    }

}
