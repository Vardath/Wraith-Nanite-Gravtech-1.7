using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class WraithSleeperAgentUtility
    {
        public const string SleeperHediffDefName = "WNG_WraithSleeperConditioning";

        public static bool TryApplyResidualConditioning(Pawn pawn, Faction sourceFaction)
        {
            if (pawn?.health?.hediffSet == null ||
                pawn.Dead ||
                pawn.RaceProps?.Humanlike != true ||
                sourceFaction == null ||
                !WraithLineageUtility.IsWraithLineage(sourceFaction))
                return false;

            HediffDef def =
                DefDatabase<HediffDef>.GetNamedSilentFail(SleeperHediffDefName);
            if (def == null)
                return false;

            HediffWithComps hediff =
                pawn.health.hediffSet.GetFirstHediffOfDef(def) as HediffWithComps;

            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(def, pawn) as HediffWithComps;
                if (hediff == null)
                    return false;
                pawn.health.AddHediff(hediff);
            }

            HediffComp_WraithSleeperAgent comp =
                hediff.TryGetComp<HediffComp_WraithSleeperAgent>();
            if (comp == null)
                return false;

            comp.ConfigureSourceFaction(sourceFaction);
            return true;
        }
    }

    public sealed class HediffCompProperties_WraithSleeperAgent : HediffCompProperties
    {
        public IntRange sabotageIntervalTicks =
            new IntRange(180000, 360000);
        public float sabotageChance = 0.55f;

        public IntRange intelligenceIntervalTicks =
            new IntRange(300000, 600000);
        public float intelligenceLeakChance = 0.30f;

        public int noTargetRetryTicks = 60000;

        public HediffCompProperties_WraithSleeperAgent()
        {
            compClass = typeof(HediffComp_WraithSleeperAgent);
        }
    }

    /// <summary>
    /// Residual post-rescue sleeper behavior. This is not an Enthrallment ownership state:
    /// current native RimWorld slavery remains the only committed stage-4 Enthrallment mechanic.
    /// This comp exists only after WNG has restored the exact formerly-enthralled pawn to the
    /// player's faction/guest state.
    /// </summary>
    public sealed class HediffComp_WraithSleeperAgent : HediffComp
    {
        private int nextSabotageTick = -1;
        private int nextIntelligenceTick = -1;
        private Faction sourceFaction;

        private HediffCompProperties_WraithSleeperAgent Props =>
            (HediffCompProperties_WraithSleeperAgent)props;

        public void ConfigureSourceFaction(Faction faction)
        {
            if (faction != null &&
                WraithLineageUtility.IsWraithLineage(faction))
                sourceFaction = faction;

            if (nextSabotageTick < 0)
                ScheduleNextSabotage();
            if (nextIntelligenceTick < 0)
                ScheduleNextIntelligence();
        }

        public override void CompPostMake()
        {
            base.CompPostMake();
            ScheduleNextSabotage();
            ScheduleNextIntelligence();
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            int now = Find.TickManager?.TicksGame ?? 0;
            ProcessIntelligenceLeak(now);

            if (nextSabotageTick < 0)
            {
                ScheduleNextSabotage();
                return;
            }

            if (now < nextSabotageTick)
                return;

            Pawn pawn = Pawn;
            if (!IsActiveSleeper(pawn))
            {
                nextSabotageTick = SafeFutureTick(now, RetryDelay);
                return;
            }

            if (!Rand.Chance(
                    Math.Max(
                        0f,
                        Math.Min(
                            1f,
                            Props?.sabotageChance ?? 0.55f))))
            {
                ScheduleNextSabotage();
                return;
            }

            if (!TrySabotageMachine(pawn))
            {
                nextSabotageTick = SafeFutureTick(now, RetryDelay);
                return;
            }

            ScheduleNextSabotage();
        }

        private void ProcessIntelligenceLeak(int now)
        {
            if (nextIntelligenceTick < 0)
            {
                ScheduleNextIntelligence();
                return;
            }

            if (now < nextIntelligenceTick)
                return;

            Pawn pawn = Pawn;
            if (!IsActiveSleeper(pawn))
            {
                nextIntelligenceTick = SafeFutureTick(now, RetryDelay);
                return;
            }

            if (!Rand.Chance(
                    Math.Max(
                        0f,
                        Math.Min(
                            1f,
                            Props?.intelligenceLeakChance ?? 0.30f))))
            {
                ScheduleNextIntelligence();
                return;
            }

            WraithMatureHiveRetaliationRegistry registry =
                Current.Game?.GetComponent<WraithMatureHiveRetaliationRegistry>();

            if (registry == null ||
                !registry.ScheduleSleeperIntelligenceLeak(
                    pawn,
                    sourceFaction))
            {
                nextIntelligenceTick = SafeFutureTick(now, RetryDelay);
                return;
            }

            ScheduleNextIntelligence();
        }

        private int RetryDelay =>
            Math.Max(
                60000,
                Props?.noTargetRetryTicks ?? 60000);

        private void ScheduleNextSabotage()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            IntRange range =
                Props?.sabotageIntervalTicks ??
                new IntRange(180000, 360000);

            nextSabotageTick =
                SafeFutureTick(
                    now,
                    Math.Max(
                        60000,
                        range.RandomInRange));
        }

        private void ScheduleNextIntelligence()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            IntRange range =
                Props?.intelligenceIntervalTicks ??
                new IntRange(300000, 600000);

            nextIntelligenceTick =
                SafeFutureTick(
                    now,
                    Math.Max(
                        60000,
                        range.RandomInRange));
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long value =
                (long)Math.Max(0, now) +
                Math.Max(1, delay);
            return value >= int.MaxValue
                ? int.MaxValue
                : (int)value;
        }

        private static bool IsActiveSleeper(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Dead ||
                !pawn.Spawned ||
                pawn.Map == null ||
                pawn.Faction != Faction.OfPlayer ||
                !pawn.IsColonist ||
                pawn.IsPrisoner ||
                pawn.Downed ||
                !pawn.Awake())
                return false;

            if (ModsConfig.IdeologyActive &&
                pawn.IsSlaveOfColony)
                return false;

            return true;
        }

        private static bool TrySabotageMachine(Pawn pawn)
        {
            if (pawn?.Map == null)
                return false;

            List<Building> candidates =
                pawn.Map.listerBuildings.allBuildingsColonist
                    .Where(
                        building =>
                        {
                            if (building == null ||
                                !building.Spawned ||
                                building.Destroyed ||
                                building.Faction != Faction.OfPlayer)
                                return false;

                            CompBreakdownable breakdown =
                                building.TryGetComp<CompBreakdownable>();
                            return breakdown != null &&
                                   !breakdown.BrokenDown;
                        })
                    .ToList();

            if (!candidates.TryRandomElement(out Building target))
                return false;

            CompBreakdownable targetBreakdown =
                target.TryGetComp<CompBreakdownable>();
            if (targetBreakdown == null ||
                targetBreakdown.BrokenDown)
                return false;

            targetBreakdown.DoBreakdown();

            try
            {
                Messages.Message(
                    "A suspicious systems failure has struck " +
                    target.LabelShortCap +
                    ". The machine shows signs of deliberate interference.",
                    target,
                    MessageTypeDefOf.NegativeEvent,
                    historical: false);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Sleeper sabotage committed but message presentation failed: " +
                    ex.Message);
            }

            return true;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();

            Scribe_Values.Look(
                ref nextSabotageTick,
                "wngSleeperNextSabotageTick",
                -1);
            Scribe_Values.Look(
                ref nextIntelligenceTick,
                "wngSleeperNextIntelligenceTick",
                -1);
            Scribe_References.Look(
                ref sourceFaction,
                "wngSleeperSourceFaction");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                nextSabotageTick =
                    Math.Max(
                        -1,
                        nextSabotageTick);
                nextIntelligenceTick =
                    Math.Max(
                        -1,
                        nextIntelligenceTick);

                if (sourceFaction != null &&
                    !WraithLineageUtility.IsWraithLineage(sourceFaction))
                    sourceFaction = null;
            }
        }
    }
}
