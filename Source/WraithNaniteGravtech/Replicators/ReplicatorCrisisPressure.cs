using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class ReplicatorCrisisPressureTuningDef : Def
    {
        public int evaluationIntervalTicks = 2500;
        public int matureInfestationCount = 18;
        public int exposureTicksPerTier = 60000;
        public int clearTicksPerDecay = 120000;
        public int maxPressureTier = 3;
        public int matterSeedBonusPerTier = 10;
    }

    public static class ReplicatorCrisisPressureUtility
    {
        public static GameComponent_ReplicatorCrisisPressure State =>
            Current.Game?.GetComponent<GameComponent_ReplicatorCrisisPressure>();

        public static int PressureTier =>
            State?.PressureTier ?? 0;

        public static int MatterSeedBonus =>
            State?.MatterSeedBonus ?? 0;
    }

    /// <summary>
    /// World-level memory of unresolved mature Replicator infestations.
    ///
    /// The old implementation strengthened later direct Drone outbreaks. Current D108 is matter-first,
    /// so this registry never gives an active swarm free bodies. Each pressure tier instead represents
    /// one Drone-equivalent (10 Blocks by default) added to a later independent Matter meteor seed.
    /// Keeping every player home clear of hostile block Replicators for the configured decay interval
    /// removes one tier.
    /// </summary>
    public sealed class GameComponent_ReplicatorCrisisPressure : GameComponent
    {
        private int pressureTier;
        private int matureExposureTicks;
        private int clearTicks;
        private int nextEvaluationTick;

        public GameComponent_ReplicatorCrisisPressure(Game game) { }

        private ReplicatorCrisisPressureTuningDef Tuning =>
            DefDatabase<ReplicatorCrisisPressureTuningDef>.GetNamedSilentFail(
                "WNG_ReplicatorCrisisPressureTuning")
            ?? DefDatabase<ReplicatorCrisisPressureTuningDef>.AllDefsListForReading.FirstOrDefault();

        public int PressureTier
        {
            get
            {
                int max = Math.Max(0, Tuning?.maxPressureTier ?? 3);
                return Math.Max(0, Math.Min(max, pressureTier));
            }
        }

        public int MatterSeedBonus =>
            PressureTier * Math.Max(0, Tuning?.matterSeedBonusPerTier ?? 10);

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (Find.TickManager == null)
                return;

            ReplicatorCrisisPressureTuningDef tuning = Tuning;
            if (tuning == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < nextEvaluationTick)
                return;

            int interval = Math.Max(60, tuning.evaluationIntervalTicks);
            nextEvaluationTick = SafeFutureTick(now, interval);

            bool anyHostile = false;
            bool anyMature = false;
            Map mostAffected = null;
            int highestCount = 0;

            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome)
                    continue;

                int hostileCount =
                    ReplicatorAssimilationUtility.CountHostileBlocks(map);

                if (hostileCount > highestCount)
                {
                    highestCount = hostileCount;
                    mostAffected = map;
                }

                if (hostileCount > 0)
                    anyHostile = true;

                if (hostileCount >= Math.Max(1, tuning.matureInfestationCount) ||
                    HasHostileSiegeMass(map))
                    anyMature = true;
            }

            if (anyMature)
            {
                clearTicks = 0;
                matureExposureTicks = SafeAdd(
                    matureExposureTicks,
                    interval);

                int threshold = Math.Max(interval, tuning.exposureTicksPerTier);
                int maxTier = Math.Max(0, tuning.maxPressureTier);

                if (matureExposureTicks >= threshold && pressureTier < maxTier)
                {
                    matureExposureTicks -= threshold;
                    pressureTier++;
                    AnnounceEscalation(
                        mostAffected,
                        highestCount);
                }
                else if (pressureTier >= maxTier)
                {
                    matureExposureTicks = Math.Min(
                        matureExposureTicks,
                        threshold);
                }

                return;
            }

            matureExposureTicks = 0;

            if (anyHostile)
            {
                clearTicks = 0;
                return;
            }

            int decayThreshold = Math.Max(
                interval,
                tuning.clearTicksPerDecay);

            if (pressureTier <= 0)
            {
                pressureTier = 0;
                clearTicks = Math.Min(
                    decayThreshold,
                    SafeAdd(clearTicks, interval));
                return;
            }

            clearTicks = SafeAdd(clearTicks, interval);
            if (clearTicks >= decayThreshold)
            {
                clearTicks -= decayThreshold;
                pressureTier--;
                AnnounceDecay();
            }
        }

        private static int SafeAdd(int value, int delta)
        {
            if (delta <= 0)
                return Math.Max(0, value);

            return value > int.MaxValue - delta
                ? int.MaxValue
                : Math.Max(0, value) + delta;
        }

        private static bool HasHostileSiegeMass(Map map)
        {
            if (map == null || Faction.OfPlayer == null)
                return false;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null ||
                    pawn.Dead ||
                    !pawn.Spawned ||
                    pawn.Faction == null ||
                    pawn.def?.defName != "WNG_ReplicatorSiegeMass")
                    continue;

                if (pawn.Faction.HostileTo(Faction.OfPlayer) ||
                    Faction.OfPlayer.HostileTo(pawn.Faction))
                    return true;
            }

            return false;
        }

        private void AnnounceEscalation(Map map, int hostileCount)
        {
            try
            {
                string text =
                    "A mature Replicator infestation has remained established long enough for the wider machine-lattice crisis to deepen. " +
                    "Regional pressure is now tier " + PressureTier + "/" +
                    Math.Max(0, Tuning?.maxPressureTier ?? 3) +
                    ". Active swarms receive no free reinforcements. Instead, the next independent Replicator Matter meteor may carry " +
                    MatterSeedBonus +
                    " additional Blocks, equivalent to " + PressureTier +
                    " extra Drone-scale masses if the material later survives dormancy and self-assembly. " +
                    "Current observed hostile block population: " + Math.Max(0, hostileCount) + ".";

                if (map != null)
                {
                    Find.LetterStack.ReceiveLetter(
                        "Replicator crisis intensifies",
                        text,
                        LetterDefOf.ThreatSmall,
                        new TargetInfo(map.Center, map));
                }
                else
                {
                    Find.LetterStack.ReceiveLetter(
                        "Replicator crisis intensifies",
                        text,
                        LetterDefOf.ThreatSmall);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator crisis escalation committed but notification failed: " +
                    ex.Message);
            }
        }

        private void AnnounceDecay()
        {
            try
            {
                string text = pressureTier > 0
                    ? "Sustained eradication has reduced regional Replicator pressure to tier " +
                      PressureTier + ". Future independent Matter seeds carry less reserve mass."
                    : "Sustained eradication has reduced regional Replicator pressure to baseline. Future Matter meteor events again use their normal seed mass.";

                Find.LetterStack.ReceiveLetter(
                    "Replicator crisis recedes",
                    text,
                    LetterDefOf.PositiveEvent);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator crisis decay committed but notification failed: " +
                    ex.Message);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(
                ref pressureTier,
                "wngReplicatorCrisisPressureTier",
                0);
            Scribe_Values.Look(
                ref matureExposureTicks,
                "wngReplicatorCrisisMatureExposureTicks",
                0);
            Scribe_Values.Look(
                ref clearTicks,
                "wngReplicatorCrisisClearTicks",
                0);
            Scribe_Values.Look(
                ref nextEvaluationTick,
                "wngReplicatorCrisisNextEvaluationTick",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int maxTier = Math.Max(
                    0,
                    Tuning?.maxPressureTier ?? 3);
                pressureTier = Math.Max(
                    0,
                    Math.Min(maxTier, pressureTier));
                matureExposureTicks = Math.Max(
                    0,
                    matureExposureTicks);
                clearTicks = Math.Max(
                    0,
                    clearTicks);
                nextEvaluationTick = Math.Max(
                    0,
                    nextEvaluationTick);
            }
        }
    }
}
