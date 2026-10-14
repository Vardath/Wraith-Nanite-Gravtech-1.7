using RimWorld;
using Verse;
using Verse.AI;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Native bill work routing for Asuran-only manufacture. The recipe's
    /// requiredGiverWorkType keeps ordinary Art workgivers out; this final gate
    /// validates the actual worker pawn immediately before vanilla bill selection.
    /// </summary>
    public sealed class WorkGiver_DoAsuranFabrication : WorkGiver_DoBill
    {
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!AsuranCollectiveUtility.IsNaniteSynthetic(pawn))
                return null;

            return base.JobOnThing(pawn, thing, forced);
        }
    }

    /// <summary>
    /// Backward-compatibility bridge for saves created before WNG_AsuranFabrication
    /// existed. New pawns receive alwaysStartActive normally; old DefMaps append a
    /// zero for a new WorkTypeDef, so eligible existing Asurans are repaired here.
    /// No separate gameplay state is stored.
    /// </summary>
    public sealed class MapComponent_AsuranFabricationWork : MapComponent
    {
        private const int CheckIntervalTicks = 600;
        private const string WorkTypeDefName = "WNG_AsuranFabrication";

        public MapComponent_AsuranFabricationWork(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null || Find.TickManager == null)
                return;
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
                return;

            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(WorkTypeDefName);
            if (workType == null)
                return;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!AsuranCollectiveUtility.IsNaniteSynthetic(pawn)
                    || pawn.workSettings == null
                    || !pawn.workSettings.EverWork
                    || pawn.story == null
                    || pawn.WorkTypeIsDisabled(workType))
                    continue;

                if (pawn.workSettings.GetPriority(workType) == 0)
                    pawn.workSettings.SetPriority(workType, Pawn_WorkSettings.DefaultPriority);
            }
        }
    }
}
