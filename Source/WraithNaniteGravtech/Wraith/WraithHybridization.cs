using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Small food-compatible Life Force bank used only by the deliberately incomplete
    /// Wraith-human graft. It reuses the current Wraith feeding/resource transaction model
    /// but deliberately does not expose voluntary Wraith hibernation.
    /// </summary>
    public sealed class Gene_Resource_HybridLifeForce : Gene_Resource_LifeForce
    {
        public override float InitialResourceMax => 0.40f;
        protected override bool SupportsDeliberateHibernation => false;
    }

    /// <summary>
    /// Injury-only hybrid regeneration. Missing anatomy is never restored by this gene.
    /// </summary>
    public sealed class Gene_WraithHybridRegeneration : Gene
    {
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null || !Active)
                return;

            Gene_Resource_HybridLifeForce resource = pawn.genes?.GetFirstGeneOfType<Gene_Resource_HybridLifeForce>();
            if (resource == null || !resource.Active)
                return;

            WraithLifeForceSettingsExtension tuning = resource.Settings;
            int interval = Math.Max(1, tuning.injuryHealIntervalTicks);
            if (!pawn.IsHashIntervalTick(interval, delta))
                return;

            Hediff_Injury injury = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(x => !x.IsPermanent() && x.Severity > 0f)
                .OrderByDescending(x => x.Severity)
                .FirstOrDefault();
            if (injury == null)
                return;

            float desiredHeal = Math.Max(0f, tuning.baseInjuryHealAmount) * resource.RegenerationFactor;
            float costPerSeverity = Math.Max(0f, tuning.injuryLifeForceCostPerSeverity);
            if (costPerSeverity > 0f)
                desiredHeal = Math.Min(desiredHeal, resource.Value / costPerSeverity);

            float actualHeal = Math.Min(injury.Severity, desiredHeal);
            if (actualHeal <= 0f)
                return;

            float cost = actualHeal * costPerSeverity;
            if (!resource.CanSpend(cost))
                return;

            injury.Heal(actualHeal);
            resource.TrySpend(cost);
        }
    }

    /// <summary>
    /// Save-persistent deterministic instability cadence for the incomplete hybrid graft.
    /// </summary>
    public sealed class Gene_WraithHybridInstability : Gene
    {
        private const int FlareCadenceTicks = 120000;
        private int nextFlareTick = -1;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null || !Active)
                return;

            int now = GenTicks.TicksGame;
            if (nextFlareTick < 0)
            {
                nextFlareTick = SafeFutureTick(now, FlareCadenceTicks);
                return;
            }

            if (now < nextFlareTick)
                return;

            HediffDef flareDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_HybridInstabilityFlare");
            if (flareDef != null)
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(flareDef);
                if (existing != null)
                    pawn.health.RemoveHediff(existing);
                pawn.health.AddHediff(flareDef);
            }

            do
            {
                nextFlareTick = SafeFutureTick(nextFlareTick, FlareCadenceTicks);
            }
            while (nextFlareTick <= now);
        }

        public override void PostRemove()
        {
            if (pawn?.health?.hediffSet != null)
            {
                HediffDef flareDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_HybridInstabilityFlare");
                Hediff flare = flareDef == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(flareDef);
                if (flare != null)
                    pawn.health.RemoveHediff(flare);
            }
            base.PostRemove();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextFlareTick, "wngHybridNextInstabilityFlareTick", -1);
        }

        private static int SafeFutureTick(int start, int delta)
        {
            long value = (long)start + Math.Max(1, delta);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }
    }

    public sealed class CompProperties_AbilityHybridTelepathicProbe : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityHybridTelepathicProbe()
        {
            compClass = typeof(CompAbilityEffect_HybridTelepathicProbe);
        }
    }

    public sealed class CompAbilityEffect_HybridTelepathicProbe : CompAbilityEffect
    {
        private const string ProbeHediffDefName = "WNG_TelepathicallyProbed";

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn victim = target.Thing as Pawn;
            if (!ValidBiologicalTarget(victim))
                return;

            HediffDef probeDef = DefDatabase<HediffDef>.GetNamedSilentFail(ProbeHediffDefName);
            if (probeDef == null || victim.health?.hediffSet == null)
                return;

            Hediff existing = victim.health.hediffSet.GetFirstHediffOfDef(probeDef);
            if (existing != null)
                victim.health.RemoveHediff(existing);
            victim.health.AddHediff(probeDef);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn victim = target.Pawn;
            if (!ValidBiologicalTarget(victim))
            {
                if (throwMessages && parent?.pawn != null)
                {
                    Messages.Message(
                        "Telepathic Probe requires another living biological humanlike target.",
                        parent.pawn,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        private static bool ValidBiologicalTarget(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   pawn.RaceProps != null &&
                   pawn.RaceProps.Humanlike &&
                   pawn.RaceProps.IsFlesh &&
                   !pawn.RaceProps.IsMechanoid &&
                   !AsuranCollectiveUtility.IsNaniteSynthetic(pawn);
        }
    }

    public sealed class CompProperties_WraithHybridization : CompProperties
    {
        public int baseBiomassCost = 360;
        public int baseDurationTicks = 180000;
        public float donorMinimumLifeForce = 0.70f;
        public float donorLifeForceCost = 0.40f;
        public float initialHybridLifeForce = 0.25f;
        public float gravSubstructureBiomassFactor = 0.70f;
        public float gravSubstructureDurationFactor = 0.65f;
        public int completionRetryTicks = 250;

        public CompProperties_WraithHybridization()
        {
            compClass = typeof(CompWraithHybridization);
        }
    }

    /// <summary>
    /// Player Growth Chamber path for the retained intelligent Wraith-human hybrid branch.
    /// The exact selected pawn is mutated in place after a paid, save-persistent integration cycle.
    /// No recruitment, faction rewrite, proxy pawn or replacement body is involved.
    /// </summary>
    public sealed class CompWraithHybridization : ThingComp
    {
        private static readonly string[] HybridGeneDefNames =
        {
            "WNG_HybridRegeneration",
            "WNG_HybridLifeForce",
            "WNG_HybridTelepathy",
            "WNG_HybridInstability"
        };

        private Pawn activeSubject;
        private int activeStartedTick = -1;
        private int activeFinishTick = -1;
        private int activeChargedBiomass;
        private bool activeGravSubstructureBonus;
        private int nextCompletionAttemptTick = -1;

        public CompProperties_WraithHybridization Props => (CompProperties_WraithHybridization)props;
        public bool CycleActive => activeSubject != null && activeFinishTick >= 0;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer || !parent.Spawned || parent.Map == null)
                yield break;

            ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("WNG_ForbiddenHybridization");
            if (research?.IsFinished != true)
                yield break;

            bool boosted = HasGravSubstructureBonus();
            int biomass = EffectiveBiomassCost(boosted);
            int ticks = EffectiveDuration(boosted);

            Command_Action command = new Command_Action
            {
                defaultLabel = "Hybridise living subject",
                defaultDesc =
                    "Select a living player pawn or colony prisoner for an incomplete Wraith-human graft. The same exact pawn is altered in place. " +
                    "Requires a living conscious Keeper or Queen, " + biomass + " Wraith biomass, and a true Wraith donor with at least " +
                    Props.donorMinimumLifeForce.ToString("0.00") + " Life Force; the donor contributes " +
                    Props.donorLifeForceCost.ToString("0.00") + ". The subject keeps ordinary food needs and gains only limited Wraith traits. " +
                    (boosted
                        ? "Grav substructure is active, reducing this cycle's biomass and time but not its donor Life Force cost."
                        : "Installing the whole chamber on grav substructure reduces biomass and integration time without reducing the donor cost.") +
                    "\nIntegration time: " + (ticks / 60000f).ToString("0.##") + " days.",
                action = BeginSubjectTargeting
            };

            if (!CanStartBase(out string reason))
                command.Disable(reason);

            yield return command;
        }

        private void BeginSubjectTargeting()
        {
            if (!CanStartBase(out string reason))
            {
                Messages.Message(reason, parent, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetItems = false,
                canTargetBuildings = false,
                canTargetLocations = false,
                canTargetSelf = false,
                validator = info => IsEligibleSubject(info.Thing as Pawn)
            };
            Find.Targeter.BeginTargeting(parameters, ConfirmSubject);
        }

        private void ConfirmSubject(LocalTargetInfo target)
        {
            Pawn subject = target.Pawn;
            if (!CanStartBase(out string reason))
            {
                Messages.Message(reason, parent, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (!IsEligibleSubject(subject))
            {
                Messages.Message(
                    "Hybridisation requires a living biological player pawn or colony prisoner who is not already Wraith, nanite-synthetic or hybridised.",
                    parent,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Begin invasive Wraith-human hybridisation on " + subject.LabelShort +
                "? The exact pawn will remain the same individual, but the graft is permanent and causes recurring instability.",
                () => TryStartHybridization(subject),
                destructive: true));
        }

        private bool CanStartBase(out string reason)
        {
            reason = null;
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null || parent.Faction != Faction.OfPlayer)
            {
                reason = "The player Growth Chamber is unavailable.";
                return false;
            }
            if (CycleActive)
            {
                reason = "This Growth Chamber is already integrating a Wraith-human graft.";
                return false;
            }

            CompPlayerWraithGrowthChamber gestation = parent.TryGetComp<CompPlayerWraithGrowthChamber>();
            if (gestation?.CycleActive == true)
            {
                reason = "This Growth Chamber is already gestating a Wraith.";
                return false;
            }

            if (FindInitiator() == null)
            {
                reason = "A living, conscious player Wraith Keeper or Queen must be present to initiate hybridisation.";
                return false;
            }

            bool boosted = HasGravSubstructureBonus();
            int biomassCost = EffectiveBiomassCost(boosted);
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            if (biomass == null || WraithHiveEcologyUtility.CountResource(parent.Map, biomass) < biomassCost)
            {
                reason = "Requires " + biomassCost + " Wraith biomass.";
                return false;
            }

            if (WraithHiveEcologyUtility.FindChargedDonor(parent.Map, Faction.OfPlayer, Props.donorMinimumLifeForce) == null)
            {
                reason = "Requires a true player Wraith donor with at least " +
                    Props.donorMinimumLifeForce.ToString("0.00") + " Life Force.";
                return false;
            }

            return true;
        }

        private bool IsEligibleSubject(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Dead ||
                !pawn.Spawned ||
                pawn.Map != parent?.Map ||
                pawn.RaceProps == null ||
                !pawn.RaceProps.Humanlike ||
                !pawn.RaceProps.IsFlesh ||
                pawn.RaceProps.IsMechanoid ||
                pawn.genes == null ||
                pawn.IsQuestLodger())
            {
                return false;
            }

            bool playerPawn = pawn.Faction == Faction.OfPlayer;
            bool colonyPrisoner = pawn.IsPrisonerOfColony;
            if (!playerPawn && !colonyPrisoner)
                return false;

            if (WraithHiveEcologyUtility.IsWraith(pawn) || AsuranCollectiveUtility.IsNaniteSynthetic(pawn))
                return false;

            if (pawn.genes.Xenotype?.defName == "WNG_WhispersHybrid")
                return false;

            HediffDef markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_UnstableWraithHybridState");
            if (markerDef != null && pawn.health?.hediffSet?.GetFirstHediffOfDef(markerDef) != null)
                return false;

            for (int i = 0; i < HybridGeneDefNames.Length; i++)
            {
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(HybridGeneDefNames[i]);
                if (def != null && pawn.genes.GetGene(def) != null)
                    return false;
            }

            return true;
        }

        private void TryStartHybridization(Pawn subject)
        {
            if (!CanStartBase(out string reason) || !IsEligibleSubject(subject))
            {
                Messages.Message(
                    reason ?? "The selected subject is no longer eligible for Wraith-human hybridisation.",
                    parent,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Pawn initiator = FindInitiator();
            Gene_Resource_LifeForce donor = WraithHiveEcologyUtility.FindChargedDonor(
                parent.Map,
                Faction.OfPlayer,
                Props.donorMinimumLifeForce);
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            HediffDef incubationDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_HybridizationIncubation");
            if (initiator == null || donor == null || biomass == null || incubationDef == null)
            {
                Messages.Message(
                    "Hybridisation could not resolve its supervisor, donor, biomass or incubation definition.",
                    parent,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            bool boosted = HasGravSubstructureBonus();
            int biomassCost = EffectiveBiomassCost(boosted);
            int durationTicks = EffectiveDuration(boosted);
            float oldDonorValue = donor.Value;

            if (!WraithHiveEcologyUtility.TryConsumeResource(parent.Map, biomass, biomassCost))
            {
                Messages.Message("The required Wraith biomass could not be committed safely.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!donor.TrySpend(Math.Max(0f, Props.donorLifeForceCost)))
            {
                WraithHiveEcologyUtility.SpawnResource(parent.Map, parent.Position, biomass, biomassCost);
                Messages.Message("The Wraith donor could not commit the required Life Force; biomass was refunded.", parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool addedIncubation = false;
            try
            {
                Hediff existing = subject.health?.hediffSet?.GetFirstHediffOfDef(incubationDef);
                if (existing == null)
                {
                    subject.health.AddHediff(incubationDef);
                    addedIncubation = true;
                }

                int now = GenTicks.TicksGame;
                activeSubject = subject;
                activeStartedTick = now;
                activeFinishTick = SafeFutureTick(now, durationTicks);
                activeChargedBiomass = biomassCost;
                activeGravSubstructureBonus = boosted;
                nextCompletionAttemptTick = activeFinishTick;
            }
            catch (Exception ex)
            {
                donor.Value = oldDonorValue;
                WraithHiveEcologyUtility.SpawnResource(parent.Map, parent.Position, biomass, biomassCost);
                if (addedIncubation && subject.health?.hediffSet != null)
                {
                    Hediff incubation = subject.health.hediffSet.GetFirstHediffOfDef(incubationDef);
                    if (incubation != null)
                        subject.health.RemoveHediff(incubation);
                }
                ClearCycle();
                Log.Error("[WNG] Hybrid graft initiation rolled back: " + ex);
                return;
            }

            WraithLivingTechnologyUtility.BestEffortMessage(
                initiator.LabelShort + " initiated Wraith-human hybridisation on " + subject.LabelShort +
                (boosted ? " using the grav-substructure efficiency bonus." : "."),
                subject,
                MessageTypeDefOf.NeutralEvent);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!CycleActive || parent?.Faction != Faction.OfPlayer || !parent.Spawned || parent.Map == null)
                return;

            if (activeSubject == null || activeSubject.Destroyed || activeSubject.Dead)
            {
                RemoveIncubation(activeSubject);
                ClearCycle();
                return;
            }

            int now = GenTicks.TicksGame;
            if (now < activeFinishTick || (nextCompletionAttemptTick >= 0 && now < nextCompletionAttemptTick))
                return;

            if (!activeSubject.Spawned || activeSubject.Map != parent.Map)
            {
                nextCompletionAttemptTick = SafeFutureTick(now, Math.Max(1, Props.completionRetryTicks));
                return;
            }

            TryCompleteHybridization(now);
        }

        private void TryCompleteHybridization(int now)
        {
            if (activeSubject?.genes == null || activeSubject.health?.hediffSet == null)
            {
                nextCompletionAttemptTick = SafeFutureTick(now, Math.Max(1, Props.completionRetryTicks));
                return;
            }

            List<GeneDef> defs = new List<GeneDef>();
            for (int i = 0; i < HybridGeneDefNames.Length; i++)
            {
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(HybridGeneDefNames[i]);
                if (def == null)
                {
                    nextCompletionAttemptTick = SafeFutureTick(now, Math.Max(1, Props.completionRetryTicks));
                    Log.ErrorOnce(
                        "[WNG] Paid Wraith-human hybridisation cannot resolve gene " + HybridGeneDefNames[i] + "; the exact subject remains pending.",
                        parent.thingIDNumber ^ 0x48594252);
                    return;
                }
                defs.Add(def);
            }

            HediffDef markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_UnstableWraithHybridState");
            if (markerDef == null)
            {
                nextCompletionAttemptTick = SafeFutureTick(now, Math.Max(1, Props.completionRetryTicks));
                return;
            }

            List<Gene> addedGenes = new List<Gene>();
            bool addedMarker = false;
            try
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    if (activeSubject.genes.GetGene(defs[i]) != null)
                        continue;

                    Gene added = activeSubject.genes.AddGene(defs[i], xenogene: true);
                    if (added == null)
                        throw new InvalidOperationException("Gene tracker rejected " + defs[i].defName + ".");
                    addedGenes.Add(added);
                }

                GeneDef hybridLifeDef = DefDatabase<GeneDef>.GetNamedSilentFail("WNG_HybridLifeForce");
                Gene_Resource_HybridLifeForce hybridLife =
                    hybridLifeDef == null ? null : activeSubject.genes.GetGene(hybridLifeDef) as Gene_Resource_HybridLifeForce;
                if (hybridLife != null)
                    hybridLife.Value = Math.Min(hybridLife.Max, Math.Max(hybridLife.Value, Props.initialHybridLifeForce));

                if (activeSubject.health.hediffSet.GetFirstHediffOfDef(markerDef) == null)
                {
                    activeSubject.health.AddHediff(markerDef);
                    addedMarker = true;
                }

                activeSubject.needs?.AddOrRemoveNeedsAsAppropriate();
                RemoveIncubation(activeSubject);

                Pawn committedSubject = activeSubject;
                bool usedGravBonus = activeGravSubstructureBonus;
                ClearCycle();

                WraithLivingTechnologyUtility.BestEffortMessage(
                    committedSubject.LabelShort + " survived Wraith-human hybridisation. The exact pawn now carries a limited, unstable Wraith-derived xenogene graft" +
                    (usedGravBonus ? " grown with grav-substructure assistance." : "."),
                    committedSubject,
                    MessageTypeDefOf.PositiveEvent);
            }
            catch (Exception ex)
            {
                for (int i = addedGenes.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (addedGenes[i] != null)
                            activeSubject.genes.RemoveGene(addedGenes[i]);
                    }
                    catch { }
                }

                if (addedMarker)
                {
                    Hediff marker = activeSubject.health.hediffSet.GetFirstHediffOfDef(markerDef);
                    if (marker != null)
                        activeSubject.health.RemoveHediff(marker);
                }

                activeSubject.needs?.AddOrRemoveNeedsAsAppropriate();
                nextCompletionAttemptTick = SafeFutureTick(now, Math.Max(1, Props.completionRetryTicks));
                Log.ErrorOnce(
                    "[WNG] Paid Wraith-human hybridisation could not commit cleanly; exact subject remains pending and no replacement pawn was created: " + ex,
                    parent.thingIDNumber ^ 0x48594253);
            }
        }

        private Pawn FindInitiator()
        {
            if (parent?.Map == null)
                return null;

            return parent.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != null &&
                            !p.Dead &&
                            !p.Downed &&
                            p.Awake() &&
                            p.Faction == Faction.OfPlayer &&
                            WraithHiveEcologyUtility.IsKeeperOrQueen(p, Faction.OfPlayer))
                .OrderBy(p => p.Position.DistanceToSquared(parent.Position))
                .FirstOrDefault();
        }

        private bool HasGravSubstructureBonus()
        {
            if (parent == null || !parent.Spawned || parent.Map == null)
                return false;

            CellRect occupied = GenAdj.OccupiedRect(parent.Position, parent.Rotation, parent.def.Size);
            bool any = false;
            foreach (IntVec3 cell in occupied.Cells)
            {
                if (!cell.InBounds(parent.Map))
                    return false;

                TerrainDef foundation = parent.Map.terrainGrid.FoundationAt(cell);
                if (foundation == null || !foundation.IsSubstructure)
                    return false;
                any = true;
            }
            return any;
        }

        private int EffectiveBiomassCost(bool boosted)
        {
            float factor = boosted ? Mathf.Clamp(Props.gravSubstructureBiomassFactor, 0.01f, 1f) : 1f;
            return Math.Max(1, Mathf.CeilToInt(Math.Max(1, Props.baseBiomassCost) * factor));
        }

        private int EffectiveDuration(bool boosted)
        {
            float factor = boosted ? Mathf.Clamp(Props.gravSubstructureDurationFactor, 0.01f, 1f) : 1f;
            return Math.Max(1, Mathf.CeilToInt(Math.Max(1, Props.baseDurationTicks) * factor));
        }

        private static int SafeFutureTick(int start, int delta)
        {
            long value = (long)start + Math.Max(1, delta);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static void RemoveIncubation(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            HediffDef incubationDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_HybridizationIncubation");
            Hediff incubation = incubationDef == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(incubationDef);
            if (incubation != null)
                pawn.health.RemoveHediff(incubation);
        }

        private void ClearCycle()
        {
            activeSubject = null;
            activeStartedTick = -1;
            activeFinishTick = -1;
            activeChargedBiomass = 0;
            activeGravSubstructureBonus = false;
            nextCompletionAttemptTick = -1;
        }

        public override string CompInspectStringExtra()
        {
            if (!CycleActive)
                return null;

            int now = GenTicks.TicksGame;
            float days = Math.Max(0, activeFinishTick - now) / 60000f;
            return "Hybrid graft: " + (activeSubject?.LabelShort ?? "exact subject") +
                   "\nTime remaining: " + days.ToString("0.##") + " days" +
                   "\nCommitted biomass: " + activeChargedBiomass +
                   (activeGravSubstructureBonus ? " (grav bonus snapshotted at initiation)" : string.Empty);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref activeSubject, "wngHybridizationSubject");
            Scribe_Values.Look(ref activeStartedTick, "wngHybridizationStartedTick", -1);
            Scribe_Values.Look(ref activeFinishTick, "wngHybridizationFinishTick", -1);
            Scribe_Values.Look(ref activeChargedBiomass, "wngHybridizationChargedBiomass", 0);
            Scribe_Values.Look(ref activeGravSubstructureBonus, "wngHybridizationGravBonus", false);
            Scribe_Values.Look(ref nextCompletionAttemptTick, "wngHybridizationNextCompletionAttemptTick", -1);
        }
    }
}
