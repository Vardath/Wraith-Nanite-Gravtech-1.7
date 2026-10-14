using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech.Anomaly
{
    public static class IratusRestorativeUtility
    {
        public const string QueenKindDefName = "WNG_IratusQueen";
        public const string QueenExtractDefName = "WNG_IratusQueenExtract";
        public const string RegenerationDefName = "WNG_IratusQueenRegeneration";
        public const string HoffanWraithPoisoningDefName = "WNG_HoffanWraithPoisoning";

        public static bool IsLivingQueen(Pawn pawn)
        {
            return IratusPharmacologyUtility.IsLivingIratus(pawn)
                && pawn.kindDef?.defName == QueenKindDefName;
        }
    }

    public sealed class Recipe_ExtractIratusQueenRestorative : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || !IratusRestorativeUtility.IsLivingQueen(pawn))
                return false;

            if (!base.AvailableOnNow(thing, part))
                return false;

            return pawn.Downed
                && !IratusPharmacologyUtility.RecentlySampled(pawn);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (!IratusRestorativeUtility.IsLivingQueen(pawn))
                return "Restorative-factor extraction requires a living Iratus queen.";
            if (!pawn.Downed)
                return "The Iratus queen must be downed or otherwise immobilized before extraction.";
            if (IratusPharmacologyUtility.RecentlySampled(pawn))
                return "This Iratus queen is still regenerating from recent biological sampling.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (!IratusRestorativeUtility.IsLivingQueen(pawn) || !pawn.Downed || IratusPharmacologyUtility.RecentlySampled(pawn))
                return;

            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                    return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            ThingDef extractDef = DefDatabase<ThingDef>.GetNamedSilentFail(IratusRestorativeUtility.QueenExtractDefName);
            HediffDef recoveryDef = IratusPharmacologyUtility.SamplingRecoveryDef;
            if (extractDef == null || recoveryDef == null || pawn.MapHeld == null)
            {
                Log.Error("WNG Iratus queen restorative extraction could not resolve its Defs or specimen map; no sample was committed.");
                return;
            }

            Thing extract = ThingMaker.MakeThing(extractDef);
            if (!GenPlace.TryPlaceThing(extract, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near))
            {
                extract.Destroy();
                Log.Error("WNG Iratus queen restorative extraction could not place its sample; recovery state was not committed.");
                return;
            }

            pawn.health.AddHediff(recoveryDef);
        }
    }

    public sealed class Recipe_AdministerIratusQueenRestorative : Recipe_Surgery
    {
        private const float FatalReactionChance = 0.85f;

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return !pawn.Dead
                && global::WraithNaniteGravtech.WraithHiveEcologyUtility.IsWraith(pawn)
                && base.AvailableOnNow(thing, part);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || !global::WraithNaniteGravtech.WraithHiveEcologyUtility.IsWraith(pawn))
                return "Iratus queen restorative treatment is only compatible with Wraith physiology.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || pawn.Dead || !global::WraithNaniteGravtech.WraithHiveEcologyUtility.IsWraith(pawn))
                return;

            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                    return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (Rand.Chance(FatalReactionChance))
            {
                pawn.Kill(null);
                return;
            }

            foreach (Hediff_Injury injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
            {
                if (injury != null && injury.Severity > 0f)
                    injury.Heal(injury.Severity);
            }

            HediffDef hoffanPoison = DefDatabase<HediffDef>.GetNamedSilentFail(IratusRestorativeUtility.HoffanWraithPoisoningDefName);
            if (hoffanPoison != null)
            {
                Hediff poison = pawn.health.hediffSet.GetFirstHediffOfDef(hoffanPoison);
                if (poison != null)
                    pawn.health.RemoveHediff(poison);
            }

            HediffDef regeneration = DefDatabase<HediffDef>.GetNamedSilentFail(IratusRestorativeUtility.RegenerationDefName);
            if (regeneration != null && !pawn.health.hediffSet.HasHediff(regeneration))
                pawn.health.AddHediff(regeneration);
        }
    }
}
