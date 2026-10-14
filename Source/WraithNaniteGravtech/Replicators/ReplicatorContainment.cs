using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorContainmentProjector : CompProperties
    {
        public float radius = 8.5f;

        public CompProperties_ReplicatorContainmentProjector()
        {
            compClass = typeof(CompReplicatorContainmentProjector);
        }
    }

    /// <summary>
    /// The projector deliberately owns only the physical powered-field state. Systems that care about
    /// containment query ReplicatorContainmentUtility instead of duplicating power/radius logic.
    /// </summary>
    public sealed class CompReplicatorContainmentProjector : ThingComp
    {
        private CompProperties_ReplicatorContainmentProjector Props =>
            (CompProperties_ReplicatorContainmentProjector)props;

        public float Radius => Props.radius;

        public bool Active
        {
            get
            {
                if (parent == null || !parent.Spawned)
                    return false;

                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                return power != null && power.PowerOn;
            }
        }

        public override string CompInspectStringExtra()
        {
            return "Replicator containment: " + (Active ? "active" : "inactive") +
                   "\nField radius: " + Radius.ToString("0.#") + " cells";
        }
    }

    public static class ReplicatorContainmentUtility
    {
        private const string ProjectorDefName = "WNG_ReplicatorContainmentProjector";

        public static bool IsContained(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid)
                return false;

            ThingDef projectorDef = DefDatabase<ThingDef>.GetNamedSilentFail(ProjectorDefName);
            if (projectorDef == null)
                return false;

            List<Thing> projectors = map.listerThings.ThingsOfDef(projectorDef);
            for (int i = 0; i < projectors.Count; i++)
            {
                Thing projector = projectors[i];
                CompReplicatorContainmentProjector comp = projector?.TryGetComp<CompReplicatorContainmentProjector>();
                if (comp == null || !comp.Active)
                    continue;

                float radius = comp.Radius;
                if (cell.DistanceToSquared(projector.Position) <= radius * radius)
                    return true;
            }

            return false;
        }

        public static bool BlocksAssimilation(Pawn replicator, Thing target)
        {
            if (replicator?.Map == null)
                return false;

            if (IsContained(replicator.Map, replicator.Position))
                return true;

            return target?.Spawned == true && target.Map == replicator.Map &&
                   IsContained(replicator.Map, target.Position);
        }
    }
}
