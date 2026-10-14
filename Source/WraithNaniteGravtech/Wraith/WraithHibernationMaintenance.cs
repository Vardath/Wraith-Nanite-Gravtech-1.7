using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WraithHibernationMaintenance : CompProperties
    {
        public int checkIntervalTicks = 250;
        public int keeperTendIntervalTicks = 60000;
        public int biomassCost = 1;
        public float lifeForceRestore = 0.02f;

        public CompProperties_WraithHibernationMaintenance()
        {
            compClass = typeof(CompWraithHibernationMaintenance);
        }
    }

    /// <summary>
    /// Restored caretaker function for the physical Wraith hibernation pod.
    ///
    /// Current D108 manual hibernation remains owned by Gene_Resource_LifeForce; this component
    /// never applies or removes WNG_WraithHibernating. It only tends one exact occupant who is
    /// already genuinely hibernating. Once per configured interval, a same-faction Keeper or Queen
    /// may spend real cultured biomass to replace a small fraction of unavoidable dormant Life Force
    /// loss. It does not touch strategic Hive hunger, feeding requests or captive feeding stock.
    /// </summary>
    public sealed class CompWraithHibernationMaintenance : ThingComp
    {
        private int nextKeeperTendTick = -1;

        private CompProperties_WraithHibernationMaintenance Props =>
            (CompProperties_WraithHibernationMaintenance)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && nextKeeperTendTick < 0)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                nextKeeperTendTick = SafeFutureTick(
                    now,
                    Math.Max(1, Props.keeperTendIntervalTicks));
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            Building_Bed bed = parent as Building_Bed;
            if (bed == null ||
                parent?.Spawned != true ||
                parent.Map == null ||
                !parent.IsHashIntervalTick(Math.Max(60, Props.checkIntervalTicks)))
                return;

            Pawn sleeper = HibernatingOccupant(bed);
            if (sleeper == null)
                return;

            Gene_Resource_LifeForce lifeForce =
                sleeper.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            if (lifeForce == null || !lifeForce.Active)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextKeeperTendTick < 0)
                nextKeeperTendTick = SafeFutureTick(
                    now,
                    Math.Max(1, Props.keeperTendIntervalTicks));
            if (now < nextKeeperTendTick)
                return;

            // A full reserve needs no biomass. Still advance the maintenance cadence so a full pod
            // cannot become immediately due the instant a tiny amount of Life Force is later lost.
            if (lifeForce.Value >= lifeForce.Max - 0.0001f)
            {
                nextKeeperTendTick = SafeFutureTick(
                    now,
                    Math.Max(1, Props.keeperTendIntervalTicks));
                return;
            }

            Faction faction = sleeper.Faction ?? parent.Faction;
            if (faction == null ||
                !WraithHiveEcologyUtility.HasKeeperOrQueen(parent.Map, faction))
                return;

            ThingDef biomass =
                DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            int cost = Math.Max(0, Props.biomassCost);
            if (cost > 0 &&
                (biomass == null ||
                 WraithHiveEcologyUtility.CountResource(parent.Map, biomass) < cost))
                return;

            float oldValue = lifeForce.Value;
            bool paid = false;
            try
            {
                if (cost > 0)
                {
                    if (!WraithHiveEcologyUtility.TryConsumeResource(
                            parent.Map,
                            biomass,
                            cost))
                        return;
                    paid = true;
                }

                lifeForce.Value = Math.Min(
                    lifeForce.Max,
                    oldValue + Math.Max(0f, Props.lifeForceRestore));

                nextKeeperTendTick = SafeFutureTick(
                    now,
                    Math.Max(1, Props.keeperTendIntervalTicks));
            }
            catch (Exception ex)
            {
                try
                {
                    lifeForce.Value = oldValue;
                }
                catch { }

                if (paid && biomass != null)
                {
                    try
                    {
                        WraithHiveEcologyUtility.SpawnResource(
                            parent.Map,
                            parent.Position,
                            biomass,
                            cost);
                    }
                    catch (Exception refundEx)
                    {
                        Log.Error(
                            "[WNG] Hibernation-pod tending failed and biomass refund also failed: " +
                            refundEx);
                    }
                }

                Log.Error(
                    "[WNG] Hibernation-pod tending transaction failed and was rolled back where possible: " +
                    ex);
            }
        }

        private static Pawn HibernatingOccupant(Building_Bed bed)
        {
            if (bed == null)
                return null;

            HediffDef hibernating =
                DefDatabase<HediffDef>.GetNamedSilentFail("WNG_WraithHibernating");
            if (hibernating == null)
                return null;

            return bed.CurOccupants
                .Where(p =>
                    p != null &&
                    !p.Dead &&
                    WraithHiveEcologyUtility.IsWraith(p) &&
                    p.health?.hediffSet != null &&
                    p.health.hediffSet.HasHediff(hibernating))
                .OrderBy(p => p.thingIDNumber)
                .FirstOrDefault();
        }

        public override string CompInspectStringExtra()
        {
            Building_Bed bed = parent as Building_Bed;
            Pawn sleeper = HibernatingOccupant(bed);
            if (sleeper == null)
                return "Keeper tending: awaiting a genuinely hibernating Wraith occupant";

            Gene_Resource_LifeForce lifeForce =
                sleeper.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            if (lifeForce == null || !lifeForce.Active)
                return "Keeper tending: occupant has no active Life Force reserve";

            if (lifeForce.Value >= lifeForce.Max - 0.0001f)
                return "Keeper tending: Life Force reserve full";

            Faction faction = sleeper.Faction ?? parent?.Faction;
            if (parent?.Map == null ||
                faction == null ||
                !WraithHiveEcologyUtility.HasKeeperOrQueen(parent.Map, faction))
                return "Keeper tending: same-faction Keeper or Queen required";

            int cost = Math.Max(0, Props.biomassCost);
            ThingDef biomass =
                DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            int stock = biomass == null || parent?.Map == null
                ? 0
                : WraithHiveEcologyUtility.CountResource(parent.Map, biomass);
            if (cost > 0 && stock < cost)
                return "Keeper tending: cultured biomass required (" + stock + "/" + cost + ")";

            int now = Find.TickManager?.TicksGame ?? 0;
            int remaining = Math.Max(0, nextKeeperTendTick - now);
            if (remaining > 0)
            {
                return "Keeper tending: next maintenance in " +
                       Math.Ceiling(remaining / 2500f).ToString("0.0") +
                       " hours\nCultured biomass reserve: " + stock;
            }

            return "Keeper tending: maintenance ready\nCultured biomass reserve: " + stock;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(
                ref nextKeeperTendTick,
                "wngHibernationPodNextKeeperTendTick",
                -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                nextKeeperTendTick = Math.Max(-1, nextKeeperTendTick);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
