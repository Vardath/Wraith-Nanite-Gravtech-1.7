using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AbilityNaniteInterface : CompProperties_AbilityEffect
    {
        public float copyReserveCost = 0.60f;
        public PawnKindDef fallbackCopyPawnKind;

        public CompProperties_AbilityNaniteInterface()
        {
            compClass = typeof(CompAbilityEffect_NaniteInterface);
        }
    }

    /// <summary>
    /// Touch-range human-form nanite interface. This owns bounded biological-mind operations only;
    /// it does not grant Queen, Sovereign Neural Lattice or Temporary-Asuran block authority.
    /// </summary>
    public sealed class CompAbilityEffect_NaniteInterface : CompAbilityEffect
    {
        internal static readonly string[] HumanFormNaniteGeneDefNames =
        {
            "WNG_NaniteBody",
            "WNG_NaniteReserve",
            "WNG_NaniteReconstruction",
            "WNG_EMPSensitiveNanites",
            "WNG_AsuranCollectiveLink",
            "WNG_NeuralInterface"
        };

        public new CompProperties_AbilityNaniteInterface Props =>
            (CompProperties_AbilityNaniteInterface)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Pawn subject = target.Pawn;
            bool valid = NeuralInterfaceUtility.IsValidTarget(caster, subject, requireAdjacent: false);
            if (!valid && throwMessages && caster != null)
            {
                Messages.Message(
                    "Neural Interface requires another living biological humanlike target that is not already a WNG nanite synthetic.",
                    caster,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
            return valid && base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            Pawn subject = target.Pawn;
            if (!NeuralInterfaceUtility.IsValidTarget(caster, subject, requireAdjacent: true))
            {
                if (caster != null)
                {
                    Messages.Message(
                        "The Neural Interface target is no longer available at touch range.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                return;
            }

            // The dialog is a player decision surface. Hostile/NPC Neural Interface behavior, if
            // later required, belongs to a separate AI/story layer rather than silently choosing here.
            if (caster.Faction != Faction.OfPlayer)
                return;

            Find.WindowStack.Add(new Dialog_NeuralInterface(
                caster,
                subject,
                Math.Max(0f, Props.copyReserveCost),
                Props.fallbackCopyPawnKind));
        }
    }

    internal static class NeuralInterfaceUtility
    {
        public static bool IsValidTarget(Pawn caster, Pawn subject, bool requireAdjacent)
        {
            if (caster == null || subject == null || caster == subject || caster.Dead || subject.Dead)
                return false;
            if (!caster.Spawned || !subject.Spawned || caster.Map == null || caster.Map != subject.Map)
                return false;
            if (subject.RaceProps == null || !subject.RaceProps.Humanlike || !subject.RaceProps.IsFlesh || subject.RaceProps.IsMechanoid)
                return false;
            if (AsuranCollectiveUtility.IsNaniteSynthetic(subject))
                return false;
            if (requireAdjacent && !caster.Position.AdjacentTo8WayOrInside(subject.Position))
                return false;
            return true;
        }

        public static void TryRecruit(Pawn caster, Pawn subject)
        {
            if (!Revalidate(caster, subject))
                return;
            if (caster.Faction == null)
            {
                Reject(caster, "The Neural Interface operator has no faction to recruit into.");
                return;
            }

            subject.SetFaction(caster.Faction);
            Messages.Message(
                $"{subject.LabelShortCap}'s allegiance was rewritten through the Neural Interface.",
                subject,
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }

        public static void TryImprison(Pawn caster, Pawn subject)
        {
            if (!Revalidate(caster, subject))
                return;
            if (caster.Faction == null || subject.guest == null)
            {
                Reject(caster, "The target cannot enter a valid prisoner state.");
                return;
            }
            if (!subject.Downed && !subject.IsPrisoner && !subject.IsSlave)
            {
                Reject(caster, "Imprisonment requires a downed or already captive target so native guest state remains valid.");
                return;
            }

            subject.guest.SetGuestStatus(caster.Faction, GuestStatus.Prisoner);
            Messages.Message(
                $"{subject.LabelShortCap} is now held as a prisoner.",
                subject,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        public static void TryEnslave(Pawn caster, Pawn subject)
        {
            if (!ModsConfig.IdeologyActive)
            {
                Reject(caster, "Enslavement requires Ideology.");
                return;
            }
            if (!Revalidate(caster, subject))
                return;
            if (caster.Faction == null || subject.guest == null)
            {
                Reject(caster, "The target cannot enter a valid slave state.");
                return;
            }
            if (!subject.Downed && !subject.IsPrisoner && !subject.IsSlave)
            {
                Reject(caster, "Enslavement requires a downed or already captive target so native guest state remains valid.");
                return;
            }

            subject.guest.SetGuestStatus(caster.Faction, GuestStatus.Slave);
            Messages.Message(
                $"{subject.LabelShortCap} is now enslaved.",
                subject,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        public static void TryCopySkills(Pawn caster, Pawn subject)
        {
            if (!Revalidate(caster, subject))
                return;
            if (caster.skills == null || subject.skills == null)
            {
                Reject(caster, "The target does not expose a compatible learned-skill pattern.");
                return;
            }

            foreach (SkillRecord source in subject.skills.skills)
            {
                if (source?.def == null)
                    continue;
                SkillRecord destination = caster.skills.GetSkill(source.def);
                if (destination == null)
                    continue;

                destination.Level = Math.Max(destination.Level, Math.Min(20, source.Level));
                if ((int)source.passion > (int)destination.passion)
                    destination.passion = source.passion;
                destination.xpSinceLastLevel = Math.Max(destination.xpSinceLastLevel, source.xpSinceLastLevel);
                destination.xpSinceMidnight = Math.Max(destination.xpSinceMidnight, source.xpSinceMidnight);
            }

            Messages.Message(
                $"{caster.LabelShortCap} copied the strongest learned skill pattern available from {subject.LabelShortCap}.",
                caster,
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }

        public static void TryBuildCopy(Pawn caster, Pawn subject, float copyReserveCost, PawnKindDef fallbackCopyPawnKind)
        {
            if (!Revalidate(caster, subject))
                return;
            if (caster.Faction == null || caster.Map == null)
            {
                Reject(caster, "The Neural Interface operator has no valid faction/map context for reconstruction.");
                return;
            }

            Gene_Resource_NaniteReserve reserve = caster.genes?.GetFirstGeneOfType<Gene_Resource_NaniteReserve>();
            if (reserve == null || !reserve.Active)
            {
                Reject(caster, "This operator has no active Nanite Reserve.");
                return;
            }

            float cost = Math.Max(0f, copyReserveCost);
            if (!reserve.CanSpend(cost))
            {
                Reject(caster, $"Human-form reconstruction requires {cost:P0} Nanite Reserve; only {reserve.Value:P0} is available.");
                return;
            }

            Pawn copy = null;
            bool spawned = false;
            bool committed = false;
            try
            {
                copy = GenerateRacePreservingBody(caster, subject, fallbackCopyPawnKind);
                if (copy == null)
                {
                    Reject(caster, "Human-form reconstruction failed before assembly. No Nanite Reserve was spent.");
                    return;
                }

                CopyExactPattern(subject, copy);
                LayerHumanFormNanites(copy);
                StripGeneratedGear(copy);
                AsuranDefaultApparelUtility.EnsureRoleApparel(copy);

                // Physical placement is the transaction commit boundary. Resource debit follows
                // successful placement; a debit race rolls the just-placed copy back immediately.
                if (!GenPlace.TryPlaceThing(copy, caster.Position, caster.Map, ThingPlaceMode.Near))
                {
                    DestroyUncommitted(copy);
                    Reject(caster, "No valid placement cell was available. No Nanite Reserve was spent.");
                    return;
                }
                spawned = true;

                if (!reserve.TrySpend(cost))
                {
                    DestroyUncommitted(copy);
                    Reject(caster, "Nanite Reserve changed before commit; the reconstruction was rolled back.");
                    return;
                }

                committed = true;

                // Belief processing happens only after physical placement and reserve debit both
                // commit. The fail-soft Ideology bridge is not allowed to roll this transaction back.
                WNGIdeologyEvents.RecordHumanFormNaniteReconstruction(caster);

                // Audio is post-commit presentation only. Failure must never roll back or suppress
                // the already-created exact copy or its Nanite Reserve debit.
                try
                {
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_NaniteCopyComplete")
                        ?.PlayOneShot(new TargetInfo(copy.Position, copy.Map));
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Nanite-copy completion sound failed after commit: " + ex.Message);
                }

                copy.Drawer?.renderer?.SetAllGraphicsDirty();
                Messages.Message(
                    $"A human-form copy of {subject.LabelShortCap} was reconstructed for {cost:P0} Nanite Reserve.",
                    copy,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
            catch (Exception ex)
            {
                // Before a successful resource commit the staged/spawned body is disposable. This
                // is intentionally fail-closed and never charges the reserve from a failed path.
                if (!committed && copy != null && !copy.Destroyed)
                    DestroyUncommitted(copy);
                Log.Error($"[WNG] Neural Interface reconstruction failure: {ex}");
                if (!committed)
                {
                    Reject(caster, spawned
                        ? "Human-form reconstruction failed during final commit and was rolled back."
                        : "Human-form reconstruction failed before a usable copy was committed.");
                }
                else
                {
                    Log.Warning("[WNG] Human-form copy was already committed; post-commit presentation failure was not allowed to delete or duplicate it.");
                }
            }
        }

        private static Pawn GenerateRacePreservingBody(Pawn caster, Pawn source, PawnKindDef fallbackCopyPawnKind)
        {
            if (source?.def != null && source.def != ThingDefOf.Human && source.kindDef?.race == source.def)
            {
                try
                {
                    Pawn racialCopy = PawnGenerator.GeneratePawn(source.kindDef, caster.Faction);
                    if (racialCopy != null && racialCopy.def == source.def)
                        return racialCopy;
                    DestroyUncommitted(racialCopy);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WNG] Could not generate a compatible source-race copy for {source.def.defName}; using the WNG human-form fallback. {ex.GetType().Name}: {ex.Message}");
                }
            }

            PawnKindDef fallback = fallbackCopyPawnKind
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_HumanFormCopy");
            if (fallback == null)
                return null;

            try
            {
                return PawnGenerator.GeneratePawn(fallback, caster.Faction);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WNG] Human-form fallback body generation failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void CopyExactPattern(Pawn source, Pawn copy)
        {
            if (source == null || copy == null)
                return;

            copy.Name = source.Name;
            copy.gender = source.gender;

            if (source.ageTracker != null && copy.ageTracker != null)
            {
                copy.ageTracker.AgeBiologicalTicks = Math.Max(0L, source.ageTracker.AgeBiologicalTicks);
                copy.ageTracker.AgeChronologicalTicks = Math.Max(
                    copy.ageTracker.AgeBiologicalTicks,
                    source.ageTracker.AgeChronologicalTicks);
            }

            if (source.story != null && copy.story != null)
            {
                copy.story.Childhood = source.story.Childhood;
                copy.story.Adulthood = source.story.Adulthood;
                copy.story.Title = source.story.Title;
                copy.story.birthLastName = source.story.birthLastName;
                copy.story.bodyType = source.story.bodyType;
                copy.story.headType = source.story.headType;
                copy.story.hairDef = source.story.hairDef;
                copy.story.HairColor = source.story.HairColor;
                copy.story.skinColorOverride = source.story.skinColorOverride;

                if (source.story.traits?.allTraits != null && copy.story.traits?.allTraits != null)
                {
                    copy.story.traits.allTraits.Clear();
                    foreach (Trait trait in source.story.traits.allTraits)
                    {
                        if (trait?.def != null)
                            copy.story.traits.GainTrait(new Trait(trait.def, trait.Degree, trait.ScenForced));
                    }
                }
            }

            if (source.style != null && copy.style != null)
            {
                copy.style.beardDef = source.style.beardDef;
                copy.style.FaceTattoo = source.style.FaceTattoo;
                copy.style.BodyTattoo = source.style.BodyTattoo;
            }

            if (source.skills != null && copy.skills != null)
            {
                foreach (SkillRecord sourceSkill in source.skills.skills)
                {
                    if (sourceSkill?.def == null)
                        continue;
                    SkillRecord destination = copy.skills.GetSkill(sourceSkill.def);
                    if (destination == null)
                        continue;
                    destination.Level = Math.Min(20, Math.Max(0, sourceSkill.Level));
                    destination.passion = sourceSkill.passion;
                    destination.xpSinceLastLevel = Math.Max(0f, sourceSkill.xpSinceLastLevel);
                    destination.xpSinceMidnight = Math.Max(0f, sourceSkill.xpSinceMidnight);
                }
            }

            CopyGenome(source, copy);
        }

        private static void CopyGenome(Pawn source, Pawn copy)
        {
            if (!ModsConfig.BiotechActive || source?.genes == null || copy?.genes == null)
                return;

            foreach (Gene existing in copy.genes.GenesListForReading.ToList())
                copy.genes.RemoveGene(existing);

            XenotypeDef sourceXenotype = source.genes.Xenotype;
            if (sourceXenotype != null)
                copy.genes.SetXenotype(sourceXenotype);

            foreach (Gene sourceGene in source.genes.Endogenes)
            {
                if (sourceGene?.def != null && !HasGene(copy, sourceGene.def))
                    copy.genes.AddGene(sourceGene.def, xenogene: false);
            }
            foreach (Gene sourceGene in source.genes.Xenogenes)
            {
                if (sourceGene?.def != null && !HasGene(copy, sourceGene.def))
                    copy.genes.AddGene(sourceGene.def, xenogene: true);
            }

            copy.genes.xenotypeName = source.genes.xenotypeName;
            copy.genes.iconDef = source.genes.iconDef;
        }

        private static void LayerHumanFormNanites(Pawn copy)
        {
            if (copy?.genes == null)
                return;

            foreach (string defName in CompAbilityEffect_NaniteInterface.HumanFormNaniteGeneDefNames)
            {
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
                if (def != null && !HasGene(copy, def))
                    copy.genes.AddGene(def, xenogene: true);
            }
        }

        private static bool HasGene(Pawn pawn, GeneDef def)
        {
            return pawn?.genes?.GenesListForReading.Any(g => g?.def == def) == true;
        }


        private static bool Revalidate(Pawn caster, Pawn subject)
        {
            if (IsValidTarget(caster, subject, requireAdjacent: true))
                return true;
            Reject(caster, "The Neural Interface operation was cancelled because the target state changed.");
            return false;
        }

        private static void StripGeneratedGear(Pawn pawn)
        {
            pawn?.apparel?.DestroyAll();
            pawn?.equipment?.DestroyAllEquipment();
        }

        private static void DestroyUncommitted(Pawn pawn)
        {
            if (pawn != null && !pawn.Destroyed)
                pawn.Destroy(DestroyMode.Vanish);
        }

        private static void Reject(Pawn caster, string text)
        {
            Messages.Message(text, caster, MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
