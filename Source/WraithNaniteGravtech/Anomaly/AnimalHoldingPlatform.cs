using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech.Anomaly
{
    /// <summary>
    /// Extends Anomaly holding platforms into general biological specimen containment without
    /// reclassifying ordinary animals as Anomaly entities. The exact animal pawn remains the thing
    /// carried into, held by, released from, and (when separately studiable) studied on the platform.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AnimalHoldingPlatformBootstrap
    {
        static AnimalHoldingPlatformBootstrap()
        {
            if (!ModsConfig.AnomalyActive)
                return;

            ConfigureAnimalStudyDefs(systematicStudyUnlocked: false);
        }

        public static void ConfigureAnimalStudyDefs(bool systematicStudyUnlocked)
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null || !def.race.Animal)
                    continue;

                if (def.comps == null)
                    def.comps = new List<CompProperties>();

                if (!def.comps.Any(c => c is CompProperties_HoldingPlatformTarget))
                {
                    def.comps.Add(new CompProperties_HoldingPlatformTarget
                    {
                        baseEscapeIntervalMtbDays = 60f,
                        lookForTargetOnEscape = false,
                        canBeExecuted = false,
                        getsColdContainmentBonus = false,
                        hasAnimation = false
                    });
                }

                // AnimalThingBase already carries Anomaly's hidden studiable comp for shambler use.
                // Reconfigure that native grammar for exact-pawn biological specimen study instead
                // of stacking a second CompStudiable on every animal. If a child Def contributed
                // another studiable comp (Iratus historically did), collapse to one resolved comp.
                List<CompProperties_Studiable> studyProps = def.comps.OfType<CompProperties_Studiable>().ToList();
                CompProperties_Studiable study = def.defName == "WNG_IratusBug"
                    ? studyProps.LastOrDefault()
                    : studyProps.FirstOrDefault();

                if (study == null)
                {
                    study = new CompProperties_Studiable();
                    def.comps.Add(study);
                }

                for (int i = def.comps.Count - 1; i >= 0; i--)
                {
                    if (def.comps[i] is CompProperties_Studiable duplicate && !ReferenceEquals(duplicate, study))
                        def.comps.RemoveAt(i);
                }

                bool iratus = def.defName == "WNG_IratusBug";
                study.frequencyTicks = 120000;
                SetStudiableField(study, "knowledgeCategory", KnowledgeCategoryDefOf.Basic);
                SetStudiableField(study, "anomalyKnowledge", iratus ? 1.25f : (systematicStudyUnlocked ? 0.20f : 0.05f));
                study.minMonolithLevelForStudy = 0;
                SetStudiableField(study, "requiresHoldingPlatform", true);
                SetStudiableField(study, "requiresImprisonment", false);

                if (!def.comps.Any(c => c is CompProperties_WNGAnimalStudyTracker))
                    def.comps.Add(new CompProperties_WNGAnimalStudyTracker());

                if (def.inspectorTabs == null)
                    def.inspectorTabs = new List<System.Type>();
                if (!def.inspectorTabs.Contains(typeof(ITab_StudyNotes)))
                    def.inspectorTabs.Add(typeof(ITab_StudyNotes));
            }
        }

        private static void SetStudiableField(CompProperties_Studiable study, string fieldName, object value)
        {
            FieldInfo field = typeof(CompProperties_Studiable).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
                field.SetValue(study, value);
        }
    }

    /// <summary>
    /// RimWorld 1.6's native Anomaly capture provider deliberately rejects ordinary animals through
    /// StudiedAtHoldingPlatform. WNG keeps the native custody job/platform but supplies the missing
    /// biological-specimen capture option. Only downed animals pass CompHoldingPlatformTarget.CanBeCaptured.
    /// </summary>
    public sealed class FloatMenuOptionProvider_WNGAnimalContainment : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return ModsConfig.AnomalyActive;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn animal = clickedThing as Pawn;
            if (animal?.RaceProps == null || !animal.RaceProps.Animal || animal.Dead)
                yield break;

            CompHoldingPlatformTarget holdComp = animal.TryGetComp<CompHoldingPlatformTarget>();
            if (holdComp == null || !holdComp.CanBeCaptured)
                yield break;

            Pawn handler = context.FirstSelectedPawn;
            if (!handler.CanReserveAndReach(animal, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, ignoreOtherReservations: true))
            {
                yield return new FloatMenuOption(
                    "CannotGenericWorkCustom".Translate("CaptureLower".Translate(animal)) + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            List<Building_HoldingPlatform> platforms = handler.Map.listerBuildings
                .AllBuildingsColonistOfClass<Building_HoldingPlatform>()
                .Where(p => !p.Occupied && handler.CanReserveAndReach(p, PathEndMode.Touch, Danger.Deadly))
                .ToList();

            Thing closest = GenClosest.ClosestThing_Global_Reachable(
                handler.Position,
                handler.Map,
                platforms,
                PathEndMode.ClosestTouch,
                TraverseParms.For(handler, Danger.Some),
                9999f,
                null,
                delegate(Thing t)
                {
                    CompEntityHolder holder = t.TryGetComp<CompEntityHolder>();
                    if (holder == null || holder.ContainmentStrength < animal.GetStatValue(StatDefOf.MinimumContainmentStrength))
                        return 0f;
                    return holder.ContainmentStrength / Mathf.Max(animal.PositionHeld.DistanceTo(t.Position), 1f);
                });

            if (closest == null)
            {
                yield return new FloatMenuOption(
                    "CannotGenericWorkCustom".Translate("CaptureLower".Translate(animal)) + ": " + "NoHoldingPlatformsAvailable".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption("Capture".Translate(animal.Label, animal), delegate
                {
                    BeginCapture(handler, animal, holdComp, closest);
                }),
                handler,
                animal);

            if (platforms.Count > 1)
            {
                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption("Capture".Translate(animal.Label, animal) + " (" + "ChooseEntityHolder".Translate() + "...)", delegate
                    {
                        StudyUtility.TargetHoldingPlatformForEntity(handler, animal);
                    }),
                    handler,
                    animal);
            }
        }

        private static void BeginCapture(Pawn handler, Pawn animal, CompHoldingPlatformTarget holdComp, Thing platform)
        {
            if (!ContainmentUtility.SafeContainerExistsFor(animal))
            {
                Messages.Message(
                    "MessageNoRoomWithMinimumContainmentStrength".Translate(animal.Label),
                    MessageTypeDefOf.ThreatSmall);
            }

            holdComp.targetHolder = platform;
            Job job = JobMaker.MakeJob(JobDefOf.CarryToEntityHolder, platform, animal);
            job.count = 1;
            handler.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
