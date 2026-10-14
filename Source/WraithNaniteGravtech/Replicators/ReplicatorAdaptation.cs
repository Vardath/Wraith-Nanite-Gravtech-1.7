using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    [Flags]
    public enum ReplicatorAdaptationFlags
    {
        None = 0,
        Material = 1 << 0,
        Armor = 1 << 1,
        Ranged = 1 << 2,
        Power = 1 << 3,
        Shield = 1 << 4,
        Grav = 1 << 5,
        AntiShield = 1 << 6
    }

    public sealed class CompProperties_ReplicatorAdaptation : CompProperties
    {
        // Newer pre-rebuild evidence used eighteen real barrier-engagement samples, never two
        // shield assimilations. Keep both threshold and cadence data-driven for later balancing.
        public int antiShieldEvidenceThreshold = 18;
        public int antiShieldSampleIntervalTicks = 300;

        public CompProperties_ReplicatorAdaptation()
        {
            compClass = typeof(CompReplicatorAdaptation);
        }
    }

    /// <summary>
    /// Learned block-Replicator knowledge only. This component deliberately does not own matter,
    /// controller identity, EMP timing, or adaptation gameplay effects. Those concerns remain
    /// separate so hierarchy transactions can transfer exactly the state that belongs here.
    /// </summary>
    public sealed class CompReplicatorAdaptation : ThingComp
    {
        private int learnedFlags;
        private int shieldEngagementCount;
        private int nextShieldEngagementSampleTick;
        private int historicalInterestFlags;
        private bool historicalSeedApplied;

        private CompProperties_ReplicatorAdaptation Props =>
            (CompProperties_ReplicatorAdaptation)props;

        public ReplicatorAdaptationFlags Learned => (ReplicatorAdaptationFlags)learnedFlags;
        public ReplicatorAdaptationFlags HistoricalInterests =>
            (ReplicatorAdaptationFlags)historicalInterestFlags;
        public int ShieldEngagementCount => shieldEngagementCount;

        public bool Has(ReplicatorAdaptationFlags flag)
        {
            return (Learned & flag) != ReplicatorAdaptationFlags.None;
        }

        public bool HistoricalInterestMatches(
            ReplicatorAdaptationEvidence evidence)
        {
            ReplicatorAdaptationFlags locallyMissing =
                HistoricalInterests & ~Learned;

            return (locallyMissing & evidence.flags) !=
                   ReplicatorAdaptationFlags.None;
        }

        public void ApplyHistoricalSeed(
            ReplicatorAdaptationFlags interests,
            int shieldEvidenceSeed)
        {
            if (historicalSeedApplied)
                return;

            // Material is baseline ecology and AntiShield itself can never be inherited. Technology
            // history is only a reacquisition hint until this local domain physically assimilates it.
            ReplicatorAdaptationFlags allowed =
                ReplicatorAdaptationFlags.Ranged |
                ReplicatorAdaptationFlags.Armor |
                ReplicatorAdaptationFlags.Power |
                ReplicatorAdaptationFlags.Grav |
                ReplicatorAdaptationFlags.Shield;

            historicalInterestFlags |= (int)(interests & allowed);

            if (!Has(ReplicatorAdaptationFlags.AntiShield))
            {
                int threshold =
                    Math.Max(
                        2,
                        Props.antiShieldEvidenceThreshold);

                shieldEngagementCount =
                    Math.Max(
                        shieldEngagementCount,
                        Math.Min(
                            threshold - 1,
                            Math.Max(0, shieldEvidenceSeed)));
            }

            historicalSeedApplied = true;
            parent.TryGetComp<CompReplicatorAdaptationEffects>()
                ?.NotifyAdaptationsChanged();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad &&
                parent is Pawn pawn &&
                !historicalSeedApplied)
            {
                ReplicatorAdaptationHistoryUtility
                    .SeedFreshAutonomousPawn(
                        pawn,
                        this);
            }
        }

        public void Learn(ReplicatorAdaptationFlags flags)
        {
            learnedFlags |= (int)flags;
            parent.TryGetComp<CompReplicatorAdaptationEffects>()?.NotifyAdaptationsChanged();
        }

        /// <summary>
        /// Records one concrete encounter with a functioning barrier. Merely assimilating shield
        /// hardware teaches Shield; it does not advance AntiShield. The cadence lives with the
        /// lineage state so multiple shield sources in the same tick cannot fabricate evidence.
        /// </summary>
        public bool TryRecordShieldEngagement(int now)
        {
            if (!Has(ReplicatorAdaptationFlags.Shield) || Has(ReplicatorAdaptationFlags.AntiShield))
                return false;
            if (now < nextShieldEngagementSampleTick)
                return false;

            int interval = Math.Max(60, Props.antiShieldSampleIntervalTicks);
            nextShieldEngagementSampleTick = SafeFutureTick(now, interval);
            int threshold = Math.Max(2, Props.antiShieldEvidenceThreshold);
            shieldEngagementCount = Math.Min(threshold, shieldEngagementCount + 1);
            if (shieldEngagementCount >= threshold)
                learnedFlags |= (int)ReplicatorAdaptationFlags.AntiShield;

            parent.TryGetComp<CompReplicatorAdaptationEffects>()?.NotifyAdaptationsChanged();
            return true;
        }

        public void InheritFrom(CompReplicatorAdaptation source)
        {
            if (source == null)
                return;

            learnedFlags = source.learnedFlags;
            shieldEngagementCount = source.shieldEngagementCount;
            nextShieldEngagementSampleTick = source.nextShieldEngagementSampleTick;
            historicalInterestFlags = source.historicalInterestFlags;
            historicalSeedApplied = source.historicalSeedApplied;
            parent.TryGetComp<CompReplicatorAdaptationEffects>()?.NotifyAdaptationsChanged();
        }

        public void MergeFrom(IEnumerable<Pawn> sourcePawns)
        {
            if (sourcePawns == null)
                return;

            int mergedFlags = 0;
            int mergedShieldEngagements = 0;
            int mergedNextSample = 0;
            int mergedHistoricalInterests = 0;
            bool anyHistoricalSeed = false;
            foreach (Pawn pawn in sourcePawns)
            {
                CompReplicatorAdaptation source = pawn?.TryGetComp<CompReplicatorAdaptation>();
                if (source == null)
                    continue;

                mergedFlags |= source.learnedFlags;
                // Merge history by maximum, not sum: recombining two bodies that witnessed the
                // same barrier sample must not double-count one shared swarm observation.
                mergedShieldEngagements = Math.Max(mergedShieldEngagements, source.shieldEngagementCount);
                mergedNextSample = Math.Max(mergedNextSample, source.nextShieldEngagementSampleTick);
                mergedHistoricalInterests |= source.historicalInterestFlags;
                anyHistoricalSeed |= source.historicalSeedApplied;
            }

            learnedFlags |= mergedFlags;
            shieldEngagementCount = Math.Max(shieldEngagementCount, mergedShieldEngagements);
            nextShieldEngagementSampleTick = Math.Max(nextShieldEngagementSampleTick, mergedNextSample);
            historicalInterestFlags |= mergedHistoricalInterests;
            historicalSeedApplied |= anyHistoricalSeed;
            if (shieldEngagementCount >= Math.Max(2, Props.antiShieldEvidenceThreshold))
                learnedFlags |= (int)ReplicatorAdaptationFlags.AntiShield;

            parent.TryGetComp<CompReplicatorAdaptationEffects>()?.NotifyAdaptationsChanged();
        }

        public override string CompInspectStringExtra()
        {
            if (Learned == ReplicatorAdaptationFlags.None)
                return null;

            string result = "Replicator adaptations: " + Learned;
            ReplicatorAdaptationFlags remainingHistory =
                HistoricalInterests & ~Learned;
            if (remainingHistory != ReplicatorAdaptationFlags.None)
                result += "\nHistorical reacquisition interest: " + remainingHistory;
            if (Has(ReplicatorAdaptationFlags.Shield) && !Has(ReplicatorAdaptationFlags.AntiShield))
                result += "\nShield countermeasure evidence: " + shieldEngagementCount + " / " + Math.Max(2, Props.antiShieldEvidenceThreshold);
            return result;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref learnedFlags, "wngReplicatorAdaptationFlags", 0);
            // Retain the already-issued WNGv1 save key even though its semantics are now correctly
            // narrowed to real barrier engagement rather than shield assimilation count.
            Scribe_Values.Look(ref shieldEngagementCount, "wngReplicatorShieldEvidenceCount", 0);
            Scribe_Values.Look(ref nextShieldEngagementSampleTick, "wngReplicatorNextShieldEngagementSampleTick", 0);
            Scribe_Values.Look(ref historicalInterestFlags, "wngReplicatorHistoricalInterestFlags", 0);
            Scribe_Values.Look(ref historicalSeedApplied, "wngReplicatorHistoricalSeedApplied", false);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }

    public struct ReplicatorAdaptationEvidence
    {
        public ReplicatorAdaptationFlags flags;

        public ReplicatorAdaptationEvidence(ReplicatorAdaptationFlags flags)
        {
            this.flags = flags;
        }
    }

    public static class ReplicatorAdaptationUtility
    {
        public static ReplicatorAdaptationEvidence EvidenceFrom(Thing target)
        {
            if (target?.def == null)
                return new ReplicatorAdaptationEvidence(ReplicatorAdaptationFlags.None);

            ThingDef def = target.def;
            ReplicatorAdaptationFlags learned =
                WNGSettingsUtility.ReplicatorMaterialAdaptationEnabled
                    ? ReplicatorAdaptationFlags.Material
                    : ReplicatorAdaptationFlags.None;

            // Armor is learned from actual protective equipment with concrete armor values, not
            // merely from the fact that an arbitrary object happens to be a building.
            if (def.IsApparel)
            {
                try
                {
                    float armor = Math.Max(
                        def.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp),
                        Math.Max(
                            def.GetStatValueAbstract(StatDefOf.ArmorRating_Blunt),
                            def.GetStatValueAbstract(StatDefOf.ArmorRating_Heat)));
                    if (armor > 0.10f)
                        learned |= ReplicatorAdaptationFlags.Armor;
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Could not inspect armor evidence on " + def.defName + ": " + ex.Message);
                }
            }

            if (def.IsRangedWeapon || def.building?.turretGunDef != null)
                learned |= ReplicatorAdaptationFlags.Ranged;

            if (target.TryGetComp<CompPowerTrader>() != null || target.TryGetComp<CompPowerBattery>() != null)
                learned |= ReplicatorAdaptationFlags.Power;

            // Assimilating genuine shield hardware teaches shield construction only. AntiShield is
            // deliberately engagement-driven and is recorded through ShareShieldEngagement below.
            if (target.TryGetComp<CompShield>() != null || target.TryGetComp<CompProjectileInterceptor>() != null)
                learned |= ReplicatorAdaptationFlags.Shield;

            // Odyssey is a required WNGv1 dependency. Use its real gravship/shuttle components as
            // evidence instead of guessing from DefName text.
            if (target is Building_GravEngine ||
                target.TryGetComp<CompGravshipFacility>() != null ||
                target.TryGetComp<CompShuttle>() != null)
            {
                learned |= ReplicatorAdaptationFlags.Grav;
            }

            return new ReplicatorAdaptationEvidence(learned);
        }

        public static int ShareEvidence(Pawn source, ReplicatorAdaptationEvidence evidence)
        {
            if (source?.Map == null || source.Faction == null || evidence.flags == ReplicatorAdaptationFlags.None)
                return 0;

            // EMP blocks WNG swarm learning/signalling. Knowledge is now controller-domain scoped;
            // faction alone is deliberately insufficient because multiple sovereign domains may share
            // a faction without sharing learned swarm state.
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(source))
                return 0;

            int recipients = 0;
            IReadOnlyList<Pawn> pawns = source.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn recipient = pawns[i];
                if (!EligibleKnowledgeRecipient(source, recipient))
                    continue;

                CompReplicatorAdaptation adaptation = recipient.TryGetComp<CompReplicatorAdaptation>();
                if (adaptation == null)
                    continue;

                adaptation.Learn(evidence.flags);
                recipients++;
            }

            try
            {
                ReplicatorAdaptationHistoryUtility.RecordEvidence(
                    source,
                    evidence.flags);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Local Replicator adaptation committed but historical evidence recording failed: " +
                    ex.Message);
            }

            return recipients;
        }

        public static int ShareShieldEngagement(Pawn source)
        {
            if (source?.Map == null || source.Faction == null || ReplicatorInterferenceUtility.IsEmpDisrupted(source))
                return 0;

            int now = Find.TickManager?.TicksGame ?? 0;
            int recorded = 0;
            IReadOnlyList<Pawn> pawns = source.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn recipient = pawns[i];
                if (!EligibleKnowledgeRecipient(source, recipient))
                    continue;

                CompReplicatorAdaptation adaptation = recipient.TryGetComp<CompReplicatorAdaptation>();
                if (adaptation?.TryRecordShieldEngagement(now) == true)
                    recorded++;
            }
            if (recorded > 0)
            {
                try
                {
                    ReplicatorAdaptationHistoryUtility
                        .RecordShieldEngagement(source);
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Local shield-engagement evidence committed but historical recording failed: " +
                        ex.Message);
                }
            }

            return recorded;
        }

        public static bool HasActiveNativePersonalShield(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;

            CompShield builtIn = pawn.TryGetComp<CompShield>();
            if (builtIn != null && builtIn.ShieldState == ShieldState.Active && builtIn.Energy > 0.001f)
                return true;

            if (pawn.apparel?.WornApparel == null)
                return false;

            for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
            {
                CompShield shield = pawn.apparel.WornApparel[i]?.TryGetComp<CompShield>();
                if (shield != null && shield.ShieldState == ShieldState.Active && shield.Energy > 0.001f)
                    return true;
            }
            return false;
        }

        private static bool EligibleKnowledgeRecipient(Pawn source, Pawn recipient)
        {
            return recipient != null && !recipient.Destroyed && !recipient.Dead && recipient.Spawned &&
                   ReplicatorAssimilationUtility.IsBlockReplicator(recipient) &&
                   ReplicatorDomainUtility.SameDomain(source, recipient) &&
                   !ReplicatorInterferenceUtility.IsEmpDisrupted(recipient);
        }
    }

    /// <summary>
    /// Concrete native-interceptor integration for AntiShield learning. Every 300 ticks, active
    /// hostile projectile-interceptor fields can provide one barrier-engagement sample to a nearby
    /// shield-adapted Replicator lineage. This is native CompProjectileInterceptor only; no guessed
    /// third-party shield APIs and no Harmony interception patch are used.
    /// </summary>
    public sealed class MapComponent_ReplicatorShieldEngagement : MapComponent
    {
        private const int ScanIntervalTicks = 300;

        public MapComponent_ReplicatorShieldEngagement(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            if (!map.IsHashIntervalTick(ScanIntervalTicks))
                return;

            List<Thing> shields = map.listerThings.ThingsInGroup(ThingRequestGroup.ProjectileInterceptor);
            if (shields == null || shields.Count == 0)
                return;

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < shields.Count; i++)
            {
                Thing shieldThing = shields[i];
                CompProjectileInterceptor interceptor = shieldThing?.TryGetComp<CompProjectileInterceptor>();
                if (interceptor == null || !interceptor.Active || shieldThing.Faction == null)
                    continue;

                float radius = Math.Max(0f, interceptor.Props.radius);
                float radiusSq = radius * radius;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn pawn = pawns[j];
                    if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Faction == null ||
                        !ReplicatorAssimilationUtility.IsBlockReplicator(pawn) ||
                        !shieldThing.Faction.HostileTo(pawn.Faction) ||
                        pawn.Position.DistanceToSquared(shieldThing.Position) > radiusSq)
                    {
                        continue;
                    }

                    CompReplicatorAdaptation adaptation = pawn.TryGetComp<CompReplicatorAdaptation>();
                    if (adaptation?.Has(ReplicatorAdaptationFlags.Shield) != true ||
                        adaptation.Has(ReplicatorAdaptationFlags.AntiShield) ||
                        ReplicatorInterferenceUtility.IsEmpDisrupted(pawn))
                    {
                        continue;
                    }

                    ReplicatorAdaptationUtility.ShareShieldEngagement(pawn);
                    break; // shared lineage sample; do not multiply one shield observation by body count.
                }
            }
        }
    }
}
