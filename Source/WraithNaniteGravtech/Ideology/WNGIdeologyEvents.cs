using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Thin bridge from already-committed WNG actions into Ideology's native history/precept
    /// machinery. This layer is intentionally post-commit and fail-soft: belief processing must
    /// never create, cancel, duplicate or roll back the physical gameplay transaction.
    /// </summary>
    internal static class WNGIdeologyEvents
    {
        private const string WraithFedOnEnemy = "WNG_WraithFedOnEnemy";
        private const string WraithFedOnNonEnemy = "WNG_WraithFedOnNonEnemy";
        private const string HumanFormNaniteReconstruction = "WNG_HumanFormNaniteReconstruction";

        public static void RecordWraithFeeding(Pawn feeder, Pawn victim)
        {
            if (!ShouldRecordFor(feeder) || victim == null)
                return;

            bool enemyOrPrisoner = victim.HostileTo(feeder)
                || (feeder.Faction == Faction.OfPlayer && victim.IsPrisonerOfColony);

            Record(feeder, enemyOrPrisoner ? WraithFedOnEnemy : WraithFedOnNonEnemy);
        }

        public static void RecordHumanFormNaniteReconstruction(Pawn operatorPawn)
        {
            if (!ShouldRecordFor(operatorPawn))
                return;

            Record(operatorPawn, HumanFormNaniteReconstruction);
        }

        private static bool ShouldRecordFor(Pawn pawn)
        {
            // Player-facing foundation only. This prevents randomly generated NPC ideologies from
            // imposing a moral penalty on canonical Wraith/Asuran AI before faction ideology
            // templates are designed and verified as their own later slice.
            return ModsConfig.IdeologyActive
                && pawn != null
                && !pawn.Dead
                && pawn.Faction == Faction.OfPlayer;
        }

        private static void Record(Pawn doer, string eventDefName)
        {
            try
            {
                HistoryEventDef eventDef = DefDatabase<HistoryEventDef>.GetNamedSilentFail(eventDefName);
                if (eventDef == null || Find.HistoryEventsManager == null)
                    return;

                Find.HistoryEventsManager.RecordEvent(
                    new HistoryEvent(eventDef, doer.Named(HistoryEventArgsNames.Doer)));
            }
            catch (Exception ex)
            {
                Log.Warning($"[WNG] Ideology history event '{eventDefName}' failed after gameplay commit; physical state was left unchanged. {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
