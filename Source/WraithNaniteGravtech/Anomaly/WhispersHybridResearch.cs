using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech.Anomaly
{
    public static class WhispersHybridResearchUtility
    {
        public const int AnalysisId = 860109;
        public const string HybridXenotypeDefName = "WNG_WhispersHybrid";
        public const string TissueDefName = "WNG_WhispersHybridTissue";
        public const string RecoveryDefName = "WNG_WhispersBiopsyRecovery";
        public const float FirstSampleKnowledge = 0.75f;
        public const float RepeatSampleKnowledge = 0.20f;

        public static bool IsLivingHybrid(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.genes?.Xenotype?.defName == HybridXenotypeDefName;
        }

        public static bool RecentlySampled(Pawn pawn)
        {
            HediffDef recovery = DefDatabase<HediffDef>.GetNamedSilentFail(RecoveryDefName);
            return recovery != null && pawn?.health?.hediffSet?.HasHediff(recovery) == true;
        }
    }

    public sealed class Recipe_BiopsyWhispersHybrid : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return WhispersHybridResearchUtility.IsLivingHybrid(pawn) &&
                   !WhispersHybridResearchUtility.RecentlySampled(pawn) &&
                   base.AvailableOnNow(thing, part);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (!WhispersHybridResearchUtility.IsLivingHybrid(pawn))
                return "This procedure requires a living Whispers hybrid.";
            if (WhispersHybridResearchUtility.RecentlySampled(pawn))
                return "This hybrid's altered sensory tissues are still recovering from recent sampling.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (!WhispersHybridResearchUtility.IsLivingHybrid(pawn) || WhispersHybridResearchUtility.RecentlySampled(pawn))
                return;

            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                    return;
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            ThingDef tissueDef = DefDatabase<ThingDef>.GetNamedSilentFail(WhispersHybridResearchUtility.TissueDefName);
            HediffDef recoveryDef = DefDatabase<HediffDef>.GetNamedSilentFail(WhispersHybridResearchUtility.RecoveryDefName);
            AnalysisManager analysis = Find.AnalysisManager;
            if (tissueDef == null || recoveryDef == null || pawn.MapHeld == null || analysis == null || Find.ResearchManager == null)
            {
                Log.Error("WNG Whispers hybrid biopsy could not resolve tissue/recovery Defs, specimen map, or Anomaly research state; no study was committed.");
                return;
            }

            Thing tissue = ThingMaker.MakeThing(tissueDef);
            if (!GenPlace.TryPlaceThing(tissue, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near))
            {
                tissue.Destroy();
                Log.Error("WNG Whispers hybrid biopsy could not place its tissue sample; study and recovery were not committed.");
                return;
            }

            bool firstSample = !analysis.IsAnalysisComplete(WhispersHybridResearchUtility.AnalysisId);
            if (!analysis.HasAnalysisWithID(WhispersHybridResearchUtility.AnalysisId))
                analysis.AddAnalysisTask(WhispersHybridResearchUtility.AnalysisId, 1);
            analysis.ForceCompleteAnalysisProgress(WhispersHybridResearchUtility.AnalysisId);

            Find.ResearchManager.ApplyKnowledge(
                KnowledgeCategoryDefOf.Basic,
                firstSample ? WhispersHybridResearchUtility.FirstSampleKnowledge : WhispersHybridResearchUtility.RepeatSampleKnowledge);

            pawn.health.AddHediff(recoveryDef);
        }
    }
}
