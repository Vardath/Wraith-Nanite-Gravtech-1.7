using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Save-wide experiential memory for hostile autonomous block-Replicator infestations.
    ///
    /// Current D108 adaptations are binary once locally learned, so historical technology exposure
    /// is deliberately NOT copied into a later swarm as already-learned flags. Instead it becomes a
    /// bounded target-priority hint: a fresh autonomous lineage remembers which categories were worth
    /// reacquiring, but must still physically assimilate matching technology on its current map.
    ///
    /// AntiShield remains different because current D108 still has a real eighteen-sample engagement
    /// ladder. Historical barrier encounters can therefore seed only a sub-threshold evidence count.
    /// Player, Queen, SNL, temporary-Asuran and captured-Queen domains never contribute to or receive
    /// this autonomous-swarm history.
    /// </summary>
    public sealed class ReplicatorAdaptationHistory : GameComponent
    {
        private const int CategoryExposureCap = 6;
        private const int ShieldEngagementHistoryCap = 54;
        private const int ShieldEngagementSeedDivisor = 3;

        private int rangedExposure;
        private int armorExposure;
        private int powerExposure;
        private int gravExposure;
        private int shieldExposure;
        private int shieldEngagementExposure;

        public ReplicatorAdaptationHistory(Game game) { }

        public ReplicatorAdaptationFlags InterestFlags
        {
            get
            {
                ReplicatorAdaptationFlags flags = ReplicatorAdaptationFlags.None;
                if (rangedExposure > 0) flags |= ReplicatorAdaptationFlags.Ranged;
                if (armorExposure > 0) flags |= ReplicatorAdaptationFlags.Armor;
                if (powerExposure > 0) flags |= ReplicatorAdaptationFlags.Power;
                if (gravExposure > 0) flags |= ReplicatorAdaptationFlags.Grav;
                if (shieldExposure > 0) flags |= ReplicatorAdaptationFlags.Shield;
                return flags;
            }
        }

        public int ShieldEngagementSeed =>
            Math.Max(
                0,
                shieldEngagementExposure / ShieldEngagementSeedDivisor);

        public void RecordEvidence(
            Pawn source,
            ReplicatorAdaptationFlags evidence)
        {
            if (!EligibleAutonomousHostileSource(source))
                return;

            // Material is baseline ecology, not learned technology. AntiShield is recorded from
            // actual barrier encounters below, never from assimilated shield hardware.
            if ((evidence & ReplicatorAdaptationFlags.Ranged) != 0)
                rangedExposure = BoundedIncrement(rangedExposure);
            if ((evidence & ReplicatorAdaptationFlags.Armor) != 0)
                armorExposure = BoundedIncrement(armorExposure);
            if ((evidence & ReplicatorAdaptationFlags.Power) != 0)
                powerExposure = BoundedIncrement(powerExposure);
            if ((evidence & ReplicatorAdaptationFlags.Grav) != 0)
                gravExposure = BoundedIncrement(gravExposure);
            if ((evidence & ReplicatorAdaptationFlags.Shield) != 0)
                shieldExposure = BoundedIncrement(shieldExposure);
        }

        public void RecordShieldEngagement(Pawn source)
        {
            if (!EligibleAutonomousHostileSource(source))
                return;

            shieldEngagementExposure =
                Math.Min(
                    ShieldEngagementHistoryCap,
                    Math.Max(0, shieldEngagementExposure) + 1);
        }

        public void ApplySeed(
            Pawn pawn,
            CompReplicatorAdaptation adaptation)
        {
            if (adaptation == null ||
                !EligibleAutonomousHostileSource(pawn))
                return;

            adaptation.ApplyHistoricalSeed(
                InterestFlags,
                ShieldEngagementSeed);
        }

        private static bool EligibleAutonomousHostileSource(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Dead ||
                pawn.Destroyed ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(pawn) ||
                pawn.Faction == null ||
                Faction.OfPlayer == null ||
                !pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            CompReplicatorDomain domain =
                ReplicatorDomainUtility.Domain(pawn);

            return domain != null &&
                   domain.Authority ==
                       ReplicatorControlAuthority.AutonomousSwarm;
        }

        private static int BoundedIncrement(int current)
        {
            return Math.Min(
                CategoryExposureCap,
                Math.Max(0, current) + 1);
        }

        private void Clamp()
        {
            rangedExposure = ClampCategory(rangedExposure);
            armorExposure = ClampCategory(armorExposure);
            powerExposure = ClampCategory(powerExposure);
            gravExposure = ClampCategory(gravExposure);
            shieldExposure = ClampCategory(shieldExposure);
            shieldEngagementExposure =
                Math.Max(
                    0,
                    Math.Min(
                        ShieldEngagementHistoryCap,
                        shieldEngagementExposure));
        }

        private static int ClampCategory(int value)
        {
            return Math.Max(
                0,
                Math.Min(
                    CategoryExposureCap,
                    value));
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // Preserve the historical save keys so a save from the prior implementation can retain
            // useful bounded experience even though current D108 interprets technology memory as
            // reacquisition priority rather than free threshold points.
            Scribe_Values.Look(
                ref rangedExposure,
                "wngReplicatorHistoryRanged",
                0);
            Scribe_Values.Look(
                ref armorExposure,
                "wngReplicatorHistoryArmor",
                0);
            Scribe_Values.Look(
                ref powerExposure,
                "wngReplicatorHistoryPower",
                0);
            Scribe_Values.Look(
                ref gravExposure,
                "wngReplicatorHistoryGrav",
                0);
            Scribe_Values.Look(
                ref shieldExposure,
                "wngReplicatorHistoryShield",
                0);
            Scribe_Values.Look(
                ref shieldEngagementExposure,
                "wngReplicatorHistoryShieldEngagements",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Clamp();
        }
    }

    public static class ReplicatorAdaptationHistoryUtility
    {
        public static ReplicatorAdaptationHistory State =>
            Current.Game?.GetComponent<ReplicatorAdaptationHistory>();

        public static void RecordEvidence(
            Pawn source,
            ReplicatorAdaptationFlags flags)
        {
            State?.RecordEvidence(source, flags);
        }

        public static void RecordShieldEngagement(Pawn source)
        {
            State?.RecordShieldEngagement(source);
        }

        public static void SeedFreshAutonomousPawn(
            Pawn pawn,
            CompReplicatorAdaptation adaptation)
        {
            State?.ApplySeed(pawn, adaptation);
        }
    }
}
