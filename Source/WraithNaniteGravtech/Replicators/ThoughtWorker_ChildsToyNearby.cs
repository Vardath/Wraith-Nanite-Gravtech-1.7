using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Restores the old "delightful little replicator" colony mood effect, but binds it to the
    /// current real WNG_ChildsToy rather than the historical worker's ordinary-Drone mismatch.
    /// Only an actively controlled player Toy counts; uncontrolled/feral conversion removes the
    /// effect naturally because the exact Toy no longer satisfies the current colony-mech contract.
    /// </summary>
    public sealed class ThoughtWorker_ChildsToyNearby : ThoughtWorker
    {
        private const float RadiusSquared = 100f;
        private const string ToyDefName = "WNG_ChildsToy";

        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (pawn == null ||
                !pawn.Spawned ||
                pawn.Map == null ||
                pawn.Faction != Faction.OfPlayer ||
                pawn.RaceProps?.Humanlike != true ||
                pawn.needs?.mood == null)
            {
                return ThoughtState.Inactive;
            }

            foreach (Pawn toy in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (toy == null ||
                    toy.Dead ||
                    !toy.Spawned ||
                    toy.Faction != Faction.OfPlayer ||
                    toy.def?.defName != ToyDefName)
                {
                    continue;
                }

                if (toy.OverseerSubject?.State != OverseerSubjectState.Overseen ||
                    toy.GetOverseer() == null)
                {
                    continue;
                }

                if (pawn.Position.DistanceToSquared(toy.Position) <= RadiusSquared)
                    return ThoughtState.ActiveAtStage(0);
            }

            return ThoughtState.Inactive;
        }
    }
}
