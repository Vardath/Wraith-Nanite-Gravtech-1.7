using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_PlayerWraithFeedingNiche : CompProperties
    {
        public int checkIntervalTicks = 250;
        public int cycleTicks = 120000;
        public int victimAgeYears = 2;
        public float lifeForceGainPerWraith = 0.12f;
        public float feedOnlyBelowLifeForce = 0.85f;
        public int maxWraithsFed = 3;

        public CompProperties_PlayerWraithFeedingNiche()
        {
            compClass = typeof(CompPlayerWraithFeedingNiche);
        }
    }

    /// <summary>
    /// Restored player-side Keeper ration feeding for the physical Wraith Feeding Niche.
    ///
    /// Hostile mature-Hive feeding remains owned by CompWraithMatureHive and strategic faction
    /// requests remain owned by WraithStrategicHungerRegistry. This component only acts on a
    /// player-owned niche containing one exact colony prisoner, and only when a same-faction
    /// Keeper/Queen is present and player Wraith actually need Life Force.
    ///
    /// Current D108 local-feeding semantics are authoritative: +2 victim biological years,
    /// Life Drained, +0.12 Life Force to at most three hungry Wraith, no lethal repeat-feed
    /// shortcut and no biomass generation.
    /// </summary>
    public sealed class CompPlayerWraithFeedingNiche : ThingComp
    {
        private int nextFeedTick = -1;
        private bool autoFeed = true;

        private CompProperties_PlayerWraithFeedingNiche Props =>
            (CompProperties_PlayerWraithFeedingNiche)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad && nextFeedTick < 0)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                nextFeedTick = SafeFutureTick(
                    now,
                    Math.Max(1, Props.cycleTicks));
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!autoFeed ||
                parent?.Spawned != true ||
                parent.Map == null ||
                parent.Faction != Faction.OfPlayer ||
                !parent.IsHashIntervalTick(Math.Max(60, Props.checkIntervalTicks)))
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextFeedTick < 0)
                nextFeedTick = SafeFutureTick(
                    now,
                    Math.Max(1, Props.cycleTicks));
            if (now < nextFeedTick)
                return;

            Building_Bed bed = parent as Building_Bed;
            if (bed == null ||
                !WraithHiveEcologyUtility.HasKeeperOrQueen(
                    parent.Map,
                    Faction.OfPlayer))
                return;

            Pawn captive = bed.CurOccupants
                .Where(p =>
                    WraithHiveEcologyUtility.IsValidFeedingStock(
                        p,
                        Faction.OfPlayer))
                .OrderBy(p => p.thingIDNumber)
                .FirstOrDefault();
            if (captive == null ||
                captive.ageTracker == null ||
                captive.health?.hediffSet == null)
                return;

            List<Gene_Resource_LifeForce> hungry =
                WraithHiveEcologyUtility.HungryWraiths(
                    parent.Map,
                    Faction.OfPlayer,
                    Math.Max(0f, Props.feedOnlyBelowLifeForce));
            if (hungry.Count == 0)
                return;

            int count = Math.Min(
                Math.Max(1, Props.maxWraithsFed),
                hungry.Count);

            long oldAge = captive.ageTracker.AgeBiologicalTicks;
            HediffDef lifeDrainedDef =
                DefDatabase<HediffDef>.GetNamedSilentFail("WNG_LifeDrained");
            Hediff priorLifeDrained =
                lifeDrainedDef == null
                    ? null
                    : captive.health.hediffSet.GetFirstHediffOfDef(
                        lifeDrainedDef);
            float priorLifeDrainedSeverity =
                priorLifeDrained?.Severity ?? 0f;

            float[] oldLifeForce = new float[count];
            for (int i = 0; i < count; i++)
                oldLifeForce[i] = hungry[i].Value;

            try
            {
                WraithHiveEcologyUtility.AdjustBiologicalAge(
                    captive,
                    Math.Max(0, Props.victimAgeYears));
                WraithHiveEcologyUtility.RefreshHediff(
                    captive,
                    "WNG_LifeDrained");

                Hediff currentLifeDrained =
                    lifeDrainedDef == null
                        ? null
                        : captive.health.hediffSet.GetFirstHediffOfDef(
                            lifeDrainedDef);
                if (lifeDrainedDef == null || currentLifeDrained == null)
                    throw new InvalidOperationException(
                        "Player Feeding Niche did not establish Life Drained.");

                float gain = Math.Max(
                    0f,
                    Props.lifeForceGainPerWraith);
                for (int i = 0; i < count; i++)
                    hungry[i].AddLifeForce(gain);

                nextFeedTick = SafeFutureTick(
                    now,
                    Math.Max(1, Props.cycleTicks));
            }
            catch (Exception ex)
            {
                captive.ageTracker.AgeBiologicalTicks = oldAge;

                if (lifeDrainedDef != null)
                {
                    Hediff current =
                        captive.health.hediffSet.GetFirstHediffOfDef(
                            lifeDrainedDef);

                    if (priorLifeDrained == null)
                    {
                        if (current != null)
                            captive.health.RemoveHediff(current);
                    }
                    else
                    {
                        if (current == null)
                        {
                            Hediff restored =
                                captive.health.AddHediff(lifeDrainedDef);
                            if (restored != null)
                                restored.Severity =
                                    priorLifeDrainedSeverity;
                        }
                        else
                        {
                            current.Severity =
                                priorLifeDrainedSeverity;
                        }
                    }
                }

                for (int i = 0; i < count; i++)
                    hungry[i].Value = oldLifeForce[i];

                Log.Error(
                    "[WNG] Player Feeding Niche transaction failed and was rolled back: " +
                    ex);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer)
                yield break;

            yield return new Command_Toggle
            {
                defaultLabel = "Keeper ration feeding",
                defaultDesc =
                    "When enabled, a player Keeper or Queen may supervise a bounded nonlethal local feeding cycle on the exact prisoner occupying this niche, but only when player Wraith actually need Life Force. This is separate from strategic faction hunger and never uses the lethal repeat-feed shortcut.",
                icon =
                    ContentFinder<Texture2D>.Get(
                        "UI/WNG/Genes/WraithLifeForce",
                        false) ??
                    BaseContent.BadTex,
                isActive = () => autoFeed,
                toggleAction = () => autoFeed = !autoFeed
            };
        }

        public override string CompInspectStringExtra()
        {
            if (parent?.Faction != Faction.OfPlayer)
                return null;

            if (!autoFeed)
                return "Keeper ration feeding: disabled";

            Building_Bed bed = parent as Building_Bed;
            Pawn captive = bed?.CurOccupants
                .FirstOrDefault(p =>
                    WraithHiveEcologyUtility.IsValidFeedingStock(
                        p,
                        Faction.OfPlayer));
            if (captive == null)
                return "Keeper ration feeding: awaiting an exact colony prisoner";

            if (parent.Map == null ||
                !WraithHiveEcologyUtility.HasKeeperOrQueen(
                    parent.Map,
                    Faction.OfPlayer))
                return "Keeper ration feeding: same-faction Keeper or Queen required";

            List<Gene_Resource_LifeForce> hungry =
                WraithHiveEcologyUtility.HungryWraiths(
                    parent.Map,
                    Faction.OfPlayer,
                    Math.Max(0f, Props.feedOnlyBelowLifeForce));
            if (hungry.Count == 0)
                return "Keeper ration feeding: no player Wraith below the Life Force threshold";

            int now = Find.TickManager?.TicksGame ?? 0;
            int remaining = Math.Max(0, nextFeedTick - now);
            if (remaining > 0)
            {
                return "Keeper ration feeding: next cycle in " +
                       Math.Ceiling(remaining / 2500f).ToString("0.0") +
                       " hours\nEligible hungry Wraith: " +
                       hungry.Count;
            }

            return "Keeper ration feeding: cycle ready\nEligible hungry Wraith: " +
                   hungry.Count;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(
                ref nextFeedTick,
                "wngPlayerNicheNextRationFeedTick",
                -1);
            Scribe_Values.Look(
                ref autoFeed,
                "wngPlayerNicheAutoRationFeed",
                true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                nextFeedTick = Math.Max(-1, nextFeedTick);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
