using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech.Anomaly
{
    public static class IratusPharmacologyUtility
    {
        public const string IratusDefName = "WNG_IratusBug";
        public const string ExtractDefName = "WNG_IratusGlandExtract";
        public const string SamplingRecoveryDefName = "WNG_IratusSamplingRecovery";
        public const string ParalysisDefName = "WNG_IratusParalysis";

        public static bool IsLivingIratus(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.def?.defName == IratusDefName;
        }

        public static HediffDef SamplingRecoveryDef => DefDatabase<HediffDef>.GetNamedSilentFail(SamplingRecoveryDefName);
        public static HediffDef ParalysisDef => DefDatabase<HediffDef>.GetNamedSilentFail(ParalysisDefName);

        public static bool RecentlySampled(Pawn pawn)
        {
            HediffDef def = SamplingRecoveryDef;
            return pawn?.health?.hediffSet != null && def != null && pawn.health.hediffSet.HasHediff(def);
        }
    }

    public sealed class Recipe_ExtractIratusBiologicals : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || !IratusPharmacologyUtility.IsLivingIratus(pawn))
                return false;

            if (!base.AvailableOnNow(thing, part))
                return false;

            return pawn.Downed
                && !IratusPharmacologyUtility.RecentlySampled(pawn);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (!IratusPharmacologyUtility.IsLivingIratus(pawn))
                return "Iratus biological extraction requires a living Iratus bug.";
            if (!pawn.Downed)
                return "The Iratus bug must be downed or otherwise immobilized before biological extraction.";
            if (IratusPharmacologyUtility.RecentlySampled(pawn))
                return "This Iratus bug is still regenerating from a recent biological extraction.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (!IratusPharmacologyUtility.IsLivingIratus(pawn) || !pawn.Downed || IratusPharmacologyUtility.RecentlySampled(pawn))
                return;

            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                    return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            ThingDef extractDef = DefDatabase<ThingDef>.GetNamedSilentFail(IratusPharmacologyUtility.ExtractDefName);
            HediffDef recoveryDef = IratusPharmacologyUtility.SamplingRecoveryDef;
            if (extractDef == null || recoveryDef == null || pawn.MapHeld == null)
            {
                Log.Error("WNG Iratus biological extraction could not resolve its extract/recovery Def or patient map; no sample was committed.");
                return;
            }

            Thing extract = ThingMaker.MakeThing(extractDef);
            extract.stackCount = 2;
            if (!GenPlace.TryPlaceThing(extract, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near))
            {
                extract.Destroy();
                Log.Error("WNG Iratus biological extraction could not place its product; the specimen recovery state was not committed.");
                return;
            }

            pawn.health.AddHediff(recoveryDef);
        }
    }

    public sealed class Recipe_AdministerIratusParalytic : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid)
                return false;

            if (!base.AvailableOnNow(thing, part))
                return false;

            HediffDef paralysis = IratusPharmacologyUtility.ParalysisDef;
            return paralysis != null && !pawn.health.hediffSet.HasHediff(paralysis);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            HediffDef paralysis = IratusPharmacologyUtility.ParalysisDef;
            if (paralysis == null || pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid)
                return;

            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                    return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (!pawn.health.hediffSet.HasHediff(paralysis))
            {
                Hediff hediff = HediffMaker.MakeHediff(paralysis, pawn);
                hediff.Severity = 1f;
                pawn.health.AddHediff(hediff);
            }
        }
    }
}
