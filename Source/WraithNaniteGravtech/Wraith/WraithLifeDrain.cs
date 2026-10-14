using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AbilityLifeDrain : CompProperties_AbilityEffect
    {
        public long victimAgeYears = 50L;
        public long casterRejuvenationYears = 5L;
        public long minimumCasterAgeYears = 18L;
        public float lifeForceGain = 1f;
        public bool killIfAlreadyDrained = true;
        public bool addLifeDrainedHediff = true;
        public bool addFedRecentlyHediff = true;
        public float torsoDamage = 0f;

        public CompProperties_AbilityLifeDrain()
        {
            compClass = typeof(CompAbilityEffect_LifeDrain);
        }
    }

    /// <summary>
    /// One transaction owns both full Drain Life and Partial Feed. Strategic Wraith faction hunger
    /// is intentionally absent: ordinary pawn feeding only changes the two pawns and the caster's
    /// Life Force resource.
    /// </summary>
    public sealed class CompAbilityEffect_LifeDrain : CompAbilityEffect
    {
        private const long TicksPerYear = 3600000L;
        private const string LifeDrainedDefName = "WNG_LifeDrained";
        private const string FedRecentlyDefName = "WNG_FedRecently";
        private const string VitalFeedbackDefName = "WNG_VitalFeedbackOrgan";
        private const string FeedingResistanceDefName = "WNG_FeedingResistance";
        private const string FeedingResistanceCrisisDefName = "WNG_FeedingResistanceCrisis";
        private const string FeedingRejectionBacklashDefName = "WNG_FeedingRejectionBacklash";
        private const int VitalFeedbackStunTicks = 240;
        private const float FeedingResistanceExtractionFactor = 0.20f;
        private const float FeedingResistanceRejectionChance = 0.35f;

        public new CompProperties_AbilityLifeDrain Props => (CompProperties_AbilityLifeDrain)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn victim = target.Pawn;
            Pawn caster = parent?.pawn;
            if (!IsValidBiologicalTarget(caster, victim))
                return;

            Gene_Resource_LifeForce resource = caster.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            if (resource == null || !resource.Active)
                return;

            // Vital Feedback intercepts the exact feeding transaction before any victim aging,
            // feeder rejuvenation, Life Force gain, marker Hediffs or Ideology event can commit.
            // The same victim Pawn remains untouched while a bounded native stun represents the
            // bioelectric backlash into the attacking Wraith.
            if (HasHediff(victim, VitalFeedbackDefName))
            {
                ApplyVitalFeedbackBacklash(caster, victim);
                return;
            }

            // Hoffan protection intercepts before any feeding-side transaction commits: no victim
            // aging, no Life Force gain, no Wraith rejuvenation, no Fed Recently marker and no
            // completed-feeding Ideology event. The failed feed instead poisons the exact Wraith.
            if (HoffanSerumUtility.HasProtection(victim))
            {
                HoffanSerumUtility.TryPoisonFeeder(caster, victim);
                return;
            }

            // Vital Resistance is the retained temporary middle ground between having no countermeasure
            // and the later permanent Hoffan treatment. It never grants immunity. A feeding attempt can
            // reject completely, producing bounded temporary consequences on both exact pawns; otherwise
            // the same current feeding transaction proceeds at one fifth of its ordinary transfer strength.
            bool feedingResistance = HasHediff(victim, FeedingResistanceDefName);
            if (feedingResistance && Rand.Chance(FeedingResistanceRejectionChance))
            {
                AddOrRefreshHediff(victim, FeedingResistanceCrisisDefName);
                AddOrRefreshHediff(caster, FeedingRejectionBacklashDefName);
                Messages.Message(
                    victim.LabelShortCap + "'s vital resistance rejected " + caster.LabelShort + "'s feeding attempt.",
                    victim,
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
                return;
            }

            HediffDef lifeDrainedDef = DefDatabase<HediffDef>.GetNamedSilentFail(LifeDrainedDefName);
            bool alreadyLifeDrained = lifeDrainedDef != null &&
                victim.health?.hediffSet?.GetFirstHediffOfDef(lifeDrainedDef) != null;

            // Full Drain Life is deliberately lethal when repeated before the victim's Life Drained
            // state has recovered. Partial Feed shares the transaction but opts out of this rule.
            if (alreadyLifeDrained && Props.killIfAlreadyDrained)
            {
                victim.Kill(null);
                return;
            }

            long victimAgeYears = feedingResistance
                ? ScaleWholeYears(Props.victimAgeYears, FeedingResistanceExtractionFactor)
                : Props.victimAgeYears;
            long casterRejuvenationYears = feedingResistance
                ? ScaleWholeYears(Props.casterRejuvenationYears, FeedingResistanceExtractionFactor)
                : Props.casterRejuvenationYears;
            float lifeForceGain = feedingResistance
                ? Math.Max(0f, Props.lifeForceGain) * FeedingResistanceExtractionFactor
                : Math.Max(0f, Props.lifeForceGain);

            if (victimAgeYears != 0L)
                AdjustBiologicalAge(victim, victimAgeYears);
            if (Props.addLifeDrainedHediff)
                AddOrRefreshHediff(victim, LifeDrainedDefName);

            ApplyFeedingTorsoDamage(caster, victim, Props.torsoDamage);

            if (casterRejuvenationYears != 0L)
                AdjustBiologicalAge(caster, -casterRejuvenationYears, Props.minimumCasterAgeYears);
            if (Props.addFedRecentlyHediff)
                AddOrRefreshHediff(caster, FedRecentlyDefName);

            resource.AddLifeForce(lifeForceGain);

            // Ideology is observational here: only a completed feeding transaction emits the
            // history event, and belief handling can never cancel or alter the Life Force commit.
            WNGIdeologyEvents.RecordWraithFeeding(caster, victim);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn victim = target.Pawn;
            Pawn caster = parent?.pawn;
            if (!IsValidBiologicalTarget(caster, victim))
            {
                if (throwMessages && caster != null)
                {
                    Messages.Message(
                        "Drain Life requires another living biological pawn; mechanical and non-flesh targets contain no usable Life Force.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }

            Gene_Resource_LifeForce resource = caster.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            if (resource == null || !resource.Active)
            {
                if (throwMessages)
                {
                    Messages.Message(
                        "This pawn has no active Wraith Life Force reserve.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        private static bool IsValidBiologicalTarget(Pawn caster, Pawn victim)
        {
            return caster != null &&
                   victim != null &&
                   victim != caster &&
                   !victim.Dead &&
                   victim.RaceProps != null &&
                   victim.RaceProps.IsFlesh &&
                   !victim.RaceProps.IsMechanoid;
        }

        private static void ApplyFeedingTorsoDamage(Pawn caster, Pawn victim, float amount)
        {
            if (caster == null || victim == null || victim.Dead || amount <= 0f || victim.health?.hediffSet == null)
                return;

            BodyPartRecord torso = victim.health.hediffSet
                .GetNotMissingParts()
                .FirstOrDefault(part => part?.def == BodyPartDefOf.Torso);

            if (torso == null)
                torso = victim.RaceProps?.body?.corePart;
            if (torso == null)
                return;

            victim.TakeDamage(new DamageInfo(
                DamageDefOf.Cut,
                amount,
                armorPenetration: 0f,
                instigator: caster,
                hitPart: torso));
        }

        private static bool HasHediff(Pawn pawn, string defName)
        {
            if (pawn?.health?.hediffSet == null)
                return false;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            return def != null && pawn.health.hediffSet.GetFirstHediffOfDef(def) != null;
        }

        private static void ApplyVitalFeedbackBacklash(Pawn caster, Pawn victim)
        {
            if (caster?.stances?.stunner == null)
                return;
            caster.stances.stunner.StunFor(
                VitalFeedbackStunTicks,
                victim,
                addBattleLog: false,
                showMote: true);
        }

        private static void AddOrRefreshHediff(Pawn pawn, string defName)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (hediffDef == null)
                return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
            pawn.health.AddHediff(hediffDef);
        }

        private static long ScaleWholeYears(long years, float factor)
        {
            if (years <= 0L || factor <= 0f)
                return 0L;

            double scaled = years * (double)factor;
            return scaled < 1d ? 0L : (long)Math.Floor(scaled);
        }

        private static void AdjustBiologicalAge(Pawn pawn, long yearsDelta, long minimumYears = 0L)
        {
            if (pawn?.ageTracker == null || yearsDelta == 0L)
                return;

            long current = pawn.ageTracker.AgeBiologicalTicks;
            long next;
            try
            {
                checked
                {
                    next = current + yearsDelta * TicksPerYear;
                }
            }
            catch (OverflowException)
            {
                next = yearsDelta > 0L ? long.MaxValue : 0L;
            }

            long minimum;
            try
            {
                checked
                {
                    minimum = Math.Max(0L, minimumYears) * TicksPerYear;
                }
            }
            catch (OverflowException)
            {
                minimum = long.MaxValue;
            }

            if (next < minimum)
                next = minimum;
            if (next < 0L)
                next = 0L;
            pawn.ageTracker.AgeBiologicalTicks = next;
        }
    }

    public sealed class CompProperties_AbilityEnthrall : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityEnthrall()
        {
            compClass = typeof(CompAbilityEffect_Enthrall);
        }
    }

    /// <summary>
    /// Restored Wraith Enthrall contract: a Wraith may immediately enslave the exact targeted
    /// humanlike pawn when that pawn is downed or already a prisoner. Native guest/slave state owns
    /// the transition; WNG creates no proxy pawn and no parallel slavery system.
    /// </summary>
    public sealed class CompAbilityEffect_Enthrall : CompAbilityEffect
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn victim = target.Pawn;
            Pawn caster = parent?.pawn;
            if (!CanEnthrall(caster, victim))
                return;

            GenGuest.TryEnslavePrisoner(caster, victim);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn victim = target.Pawn;
            Pawn caster = parent?.pawn;
            if (!CanEnthrall(caster, victim))
            {
                if (throwMessages && caster != null)
                {
                    Messages.Message(
                        "Enthrall requires another living humanlike pawn who is downed or already a prisoner.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        private static bool CanEnthrall(Pawn caster, Pawn victim)
        {
            return caster != null &&
                   caster.Faction != null &&
                   victim != null &&
                   victim != caster &&
                   !victim.Dead &&
                   victim.RaceProps != null &&
                   victim.RaceProps.Humanlike &&
                   victim.guest != null &&
                   !victim.IsSlave &&
                   victim.Faction != caster.Faction &&
                   (victim.Downed || victim.IsPrisoner);
        }
    }

    public sealed class CompProperties_AbilityReturnLife : CompProperties_AbilityEffect
    {
        public float lifeForceCost = 0.66f;
        public long restoredAgeYears = 10L;
        public long minimumTargetAgeYears = 18L;

        public CompProperties_AbilityReturnLife()
        {
            compClass = typeof(CompAbilityEffect_ReturnLife);
        }
    }

    /// <summary>
    /// Queen-only reversal of Wraith feeding. The target remains the exact same living pawn;
    /// this transfers stored vitality only and never resurrects or performs generic injury repair.
    /// </summary>
    public sealed class CompAbilityEffect_ReturnLife : CompAbilityEffect
    {
        private const long TicksPerYear = 3600000L;
        private const string LifeDrainedDefName = "WNG_LifeDrained";
        private const string QueenKindDefName = "WNG_WraithQueen";

        public new CompProperties_AbilityReturnLife Props => (CompProperties_AbilityReturnLife)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            Pawn recipient = target.Pawn;
            if (!CanReturnLife(caster, recipient, out Gene_Resource_LifeForce resource, out Hediff drained))
                return;

            float cost = Math.Max(0f, Props.lifeForceCost);
            long originalAge = recipient.ageTracker.AgeBiologicalTicks;
            long restoredAge = CalculateRestoredAge(
                originalAge,
                Math.Max(0L, Props.restoredAgeYears),
                Math.Max(0L, Props.minimumTargetAgeYears));

            if (!resource.TrySpend(cost))
                return;

            bool removedDrained = false;
            try
            {
                recipient.ageTracker.AgeBiologicalTicks = restoredAge;

                if (drained != null)
                {
                    recipient.health.RemoveHediff(drained);
                    removedDrained = true;
                }
            }
            catch (Exception ex)
            {
                resource.AddLifeForce(cost);
                recipient.ageTracker.AgeBiologicalTicks = originalAge;

                if (removedDrained)
                {
                    HediffDef drainedDef = DefDatabase<HediffDef>.GetNamedSilentFail(LifeDrainedDefName);
                    if (drainedDef != null && recipient.health?.hediffSet?.GetFirstHediffOfDef(drainedDef) == null)
                        recipient.health.AddHediff(drainedDef);
                }

                Log.Error("[WNG] Return Life rolled back after an incomplete target transaction: " + ex.Message);
                return;
            }

            try
            {
                if (caster.Spawned && caster.Map != null)
                {
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_WraithReturnLife")
                        ?.PlayOneShot(new TargetInfo(caster.Position, caster.Map));
                }

                Messages.Message(
                    caster.LabelShortCap + " returned stored life force to " + recipient.LabelShortCap + ".",
                    recipient,
                    MessageTypeDefOf.PositiveEvent,
                    historical: true);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Return Life committed but presentation failed: " + ex.Message);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Pawn recipient = target.Pawn;

            if (!CanReturnLife(caster, recipient, out _, out Hediff drained))
            {
                if (throwMessages && caster != null)
                {
                    Messages.Message(
                        "Return Life requires a Wraith Queen with sufficient Life Force and another living biological humanlike.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }

            long currentAge = recipient.ageTracker.AgeBiologicalTicks;
            long restoredAge = CalculateRestoredAge(
                currentAge,
                Math.Max(0L, Props.restoredAgeYears),
                Math.Max(0L, Props.minimumTargetAgeYears));

            if (drained == null && restoredAge >= currentAge)
            {
                if (throwMessages)
                {
                    Messages.Message(
                        "This target has no feeding trauma or recoverable biological ageing for Return Life to reverse.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        private bool CanReturnLife(
            Pawn caster,
            Pawn recipient,
            out Gene_Resource_LifeForce resource,
            out Hediff drained)
        {
            resource = null;
            drained = null;

            if (caster == null ||
                caster.kindDef?.defName != QueenKindDefName ||
                recipient == null ||
                recipient == caster ||
                recipient.Dead ||
                recipient.RaceProps == null ||
                !recipient.RaceProps.Humanlike ||
                !recipient.RaceProps.IsFlesh ||
                recipient.RaceProps.IsMechanoid ||
                AsuranCollectiveUtility.IsNaniteSynthetic(recipient) ||
                recipient.ageTracker == null)
            {
                return false;
            }

            resource = caster.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            float cost = Math.Max(0f, Props.lifeForceCost);
            if (resource == null || !resource.Active || !resource.CanSpend(cost))
                return false;

            HediffDef drainedDef = DefDatabase<HediffDef>.GetNamedSilentFail(LifeDrainedDefName);
            drained = drainedDef == null ? null : recipient.health?.hediffSet?.GetFirstHediffOfDef(drainedDef);
            return true;
        }

        private static long CalculateRestoredAge(long currentTicks, long years, long adultFloorYears)
        {
            long reduction;
            long adultFloor;
            try
            {
                checked
                {
                    reduction = years * TicksPerYear;
                    adultFloor = adultFloorYears * TicksPerYear;
                }
            }
            catch (OverflowException)
            {
                reduction = long.MaxValue;
                adultFloor = long.MaxValue;
            }

            long safeCurrent = Math.Max(0L, currentTicks);
            long floor = Math.Min(safeCurrent, Math.Max(0L, adultFloor));
            long reduced = safeCurrent > reduction ? safeCurrent - reduction : 0L;
            return Math.Max(floor, reduced);
        }
    }

    internal static class WraithCaptiveProcedureUtility
    {
        public static bool IsEligiblePrisoner(Pawn caster, Pawn prisoner)
        {
            return caster != null &&
                   caster.Faction != null &&
                   prisoner != null &&
                   prisoner != caster &&
                   !prisoner.Dead &&
                   prisoner.RaceProps != null &&
                   prisoner.RaceProps.Humanlike &&
                   prisoner.RaceProps.IsFlesh &&
                   !prisoner.RaceProps.IsMechanoid &&
                   !AsuranCollectiveUtility.IsNaniteSynthetic(prisoner) &&
                   prisoner.guest != null &&
                   prisoner.guest.IsPrisoner &&
                   prisoner.guest.HostFaction == caster.Faction;
        }

        public static void AddOrRefresh(Pawn pawn, HediffDef hediffDef)
        {
            if (pawn?.health?.hediffSet == null || hediffDef == null)
                return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
                pawn.health.RemoveHediff(existing);

            pawn.health.AddHediff(hediffDef);
        }
    }

    public sealed class CompProperties_AbilityWraithCaptiveExperiment : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityWraithCaptiveExperiment()
        {
            compClass = typeof(CompAbilityEffect_WraithCaptiveExperiment);
        }
    }

    /// <summary>
    /// Player-directed invasive Wraith prisoner experiment. The exact prisoner remains the same pawn
    /// under the same guest/faction custody; only temporary neurological and observer-insight effects change.
    /// </summary>
    public sealed class CompAbilityEffect_WraithCaptiveExperiment : CompAbilityEffect
    {
        private const string SubjectDefName = "WNG_WraithExperimentSubject";
        private const string InsightDefName = "WNG_WraithExperimentalInsight";

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            Pawn prisoner = target.Pawn;
            if (!WraithCaptiveProcedureUtility.IsEligiblePrisoner(caster, prisoner))
                return;

            HediffDef subjectDef = DefDatabase<HediffDef>.GetNamedSilentFail(SubjectDefName);
            HediffDef insightDef = DefDatabase<HediffDef>.GetNamedSilentFail(InsightDefName);
            if (subjectDef == null || insightDef == null)
                return;

            WraithCaptiveProcedureUtility.AddOrRefresh(prisoner, subjectDef);
            WraithCaptiveProcedureUtility.AddOrRefresh(caster, insightDef);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Pawn prisoner = target.Pawn;
            if (!WraithCaptiveProcedureUtility.IsEligiblePrisoner(caster, prisoner))
            {
                if (throwMessages && caster != null)
                {
                    Messages.Message(
                        "Captive Experiment requires a living biological humanlike prisoner held by the Wraith's faction.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }

            return base.Valid(target, throwMessages);
        }
    }

    public sealed class CompProperties_AbilityWraithCaptiveInterrogation : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityWraithCaptiveInterrogation()
        {
            compClass = typeof(CompAbilityEffect_WraithCaptiveInterrogation);
        }
    }

    /// <summary>
    /// Alternate captive-management path: sustained telepathic interrogation rather than invasive
    /// neurological mapping. It reuses the established Telepathically Probed state, grants the same
    /// bounded experimental insight to the Wraith observer, and never feeds on, recruits, enthralls,
    /// replaces, ages or changes custody of the exact prisoner.
    /// </summary>
    public sealed class CompAbilityEffect_WraithCaptiveInterrogation : CompAbilityEffect
    {
        private const string ProbeDefName = "WNG_TelepathicallyProbed";
        private const string InsightDefName = "WNG_WraithExperimentalInsight";

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            Pawn prisoner = target.Pawn;
            if (!WraithCaptiveProcedureUtility.IsEligiblePrisoner(caster, prisoner))
                return;

            HediffDef probeDef = DefDatabase<HediffDef>.GetNamedSilentFail(ProbeDefName);
            HediffDef insightDef = DefDatabase<HediffDef>.GetNamedSilentFail(InsightDefName);
            if (probeDef == null || insightDef == null)
                return;

            WraithCaptiveProcedureUtility.AddOrRefresh(prisoner, probeDef);
            WraithCaptiveProcedureUtility.AddOrRefresh(caster, insightDef);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Pawn prisoner = target.Pawn;
            if (!WraithCaptiveProcedureUtility.IsEligiblePrisoner(caster, prisoner))
            {
                if (throwMessages && caster != null)
                {
                    Messages.Message(
                        "Captive Interrogation requires a living biological humanlike prisoner held by the Wraith's faction.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }

            return base.Valid(target, throwMessages);
        }
    }

}
