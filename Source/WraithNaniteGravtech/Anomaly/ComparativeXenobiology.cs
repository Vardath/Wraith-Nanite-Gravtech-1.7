using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech.Anomaly
{
    /// <summary>
    /// Save-persistent comparative zoology state. Ordinary animal study gives trace Basic Anomaly
    /// knowledge and records biological breadth; Iratus study and three distinct ordinary species
    /// satisfy the two discovery gates for Comparative Xenobiology.
    /// </summary>
    public sealed class WNGComparativeXenobiologyState : GameComponent
    {
        public const int IratusStudyAnalysisId = 860107;
        public const int AnimalDiversityAnalysisId = 860108;
        public const int RequiredDistinctOrdinarySpecies = 3;
        public const float IratusKnowledgeGate = 0.50f;
        public const float FirstSpeciesBonusKnowledge = 0.04f;

        private Dictionary<string, int> speciesStudyCounts = new Dictionary<string, int>();
        private bool lastSystematicStudyUnlocked;
        private int nextConfigCheckTick;

        public WNGComparativeXenobiologyState(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureAnalysisTasks();
            SyncGateStateFromSavedStudies();
            RefreshStudyConfiguration(force: true);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame < nextConfigCheckTick)
                return;

            nextConfigCheckTick = Find.TickManager.TicksGame + 2500;
            RefreshStudyConfiguration(force: false);
        }

        public void RecordAnimalStudy(Pawn animal, Pawn studier)
        {
            if (animal?.def == null || animal.RaceProps == null || !animal.RaceProps.Animal)
                return;

            EnsureAnalysisTasks();

            string key = animal.def.defName;
            bool firstSpeciesStudy = !speciesStudyCounts.TryGetValue(key, out int count) || count <= 0;
            speciesStudyCounts[key] = count + 1;

            if (key == "WNG_IratusBug")
            {
                CompStudiable studiable = animal.TryGetComp<CompStudiable>();
                if (studiable != null && studiable.anomalyKnowledgeGained >= IratusKnowledgeGate)
                    Find.AnalysisManager.ForceCompleteAnalysisProgress(IratusStudyAnalysisId);
                return;
            }

            if (firstSpeciesStudy)
            {
                // A one-time breadth bonus rewards comparing new organisms without making ordinary
                // animals competitive with genuine Anomaly/Iratus research.
                Find.ResearchManager.ApplyKnowledge(KnowledgeCategoryDefOf.Basic, FirstSpeciesBonusKnowledge);
            }

            if (DistinctOrdinarySpeciesStudied >= RequiredDistinctOrdinarySpecies)
                Find.AnalysisManager.ForceCompleteAnalysisProgress(AnimalDiversityAnalysisId);
        }

        public int DistinctOrdinarySpeciesStudied
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<string, int> entry in speciesStudyCounts)
                {
                    if (entry.Value > 0 && entry.Key != "WNG_IratusBug")
                        total++;
                }
                return total;
            }
        }

        private void EnsureAnalysisTasks()
        {
            if (Find.AnalysisManager == null)
                return;

            if (!Find.AnalysisManager.HasAnalysisWithID(IratusStudyAnalysisId))
                Find.AnalysisManager.AddAnalysisTask(IratusStudyAnalysisId, 1);
            if (!Find.AnalysisManager.HasAnalysisWithID(AnimalDiversityAnalysisId))
                Find.AnalysisManager.AddAnalysisTask(AnimalDiversityAnalysisId, 1);
        }

        private void SyncGateStateFromSavedStudies()
        {
            if (DistinctOrdinarySpeciesStudied >= RequiredDistinctOrdinarySpecies)
                Find.AnalysisManager?.ForceCompleteAnalysisProgress(AnimalDiversityAnalysisId);
        }

        private void RefreshStudyConfiguration(bool force)
        {
            ResearchProjectDef project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("WNG_ComparativeXenobiology");
            bool systematicUnlocked = project?.IsFinished ?? false;
            if (!force && systematicUnlocked == lastSystematicStudyUnlocked)
                return;

            lastSystematicStudyUnlocked = systematicUnlocked;
            AnimalHoldingPlatformBootstrap.ConfigureAnimalStudyDefs(systematicUnlocked);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref speciesStudyCounts, "wngAnimalSpeciesStudyCounts", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && speciesStudyCounts == null)
                speciesStudyCounts = new Dictionary<string, int>();
        }
    }

    public sealed class CompProperties_WNGAnimalStudyTracker : CompProperties
    {
        public CompProperties_WNGAnimalStudyTracker()
        {
            compClass = typeof(CompWNGAnimalStudyTracker);
        }
    }

    /// <summary>
    /// Receives native StudyManager notifications. We only count the knowledge-bearing callback;
    /// native Study() also emits a non-category progress callback for the same interaction.
    /// </summary>
    public sealed class CompWNGAnimalStudyTracker : ThingComp, IThingStudied
    {
        public void OnStudied(Pawn studier, float amount, KnowledgeCategoryDef category = null)
        {
            if (category == null || !(parent is Pawn animal) || animal.RaceProps == null || !animal.RaceProps.Animal)
                return;

            Current.Game?.GetComponent<WNGComparativeXenobiologyState>()?.RecordAnimalStudy(animal, studier);
        }
    }

    /// <summary>
    /// Analysis-gate comp used only so native ResearchProjectDef.requiredAnalyzed can express
    /// study-discovered prerequisites. It cannot be manually analyzed and exposes no gizmo.
    /// </summary>
    public sealed class CompProperties_WNGStudyGateAnalyzable : CompProperties_CompAnalyzableUnlockResearch
    {
        public CompProperties_WNGStudyGateAnalyzable()
        {
            compClass = typeof(CompWNGStudyGateAnalyzable);
        }
    }

    public sealed class CompWNGStudyGateAnalyzable : CompAnalyzableUnlockResearch
    {
        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
        {
            return false;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield break;
        }
    }
}
