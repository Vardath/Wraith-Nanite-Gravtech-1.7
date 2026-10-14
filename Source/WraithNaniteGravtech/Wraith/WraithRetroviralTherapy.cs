using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class WraithRetroviralUtility
    {
        public const string HumanizationDefName = "WNG_WraithRetroviralHumanization";
        public const string WraithXenotypeDefName = "WNG_Wraith";
        public const string HumanizedXenotypeDefName = "WNG_HumanizedWraith";
        public const string HybridStabilisedDefName = "WNG_HybridStabilised";
        public const int SuppressionWindowTicks = 75000; // 1.25 RimWorld days: daily dosing with a small clinical buffer.

        public static HediffDef HumanizationDef => DefDatabase<HediffDef>.GetNamedSilentFail(HumanizationDefName);

        public static Hediff_WraithRetroviralHumanization HumanizationFor(Pawn pawn)
        {
            HediffDef def = HumanizationDef;
            return def == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) as Hediff_WraithRetroviralHumanization;
        }

        public static bool IsHumanized(Pawn pawn)
        {
            return HumanizationFor(pawn) != null;
        }

        public static bool HasHybridStabiliser(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return false;

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(HybridStabilisedDefName);
            return def != null && pawn.health.hediffSet.HasHediff(def);
        }
    }

    /// <summary>
    /// Reversible, exact-pawn Wraith humanization. This deliberately does not call SetXenotype:
    /// vanilla SetXenotype clears all xenogenes, which would destroy unrelated implanted genetics.
    /// Instead, only gene instances belonging to WNG_Wraith are removed and recorded, their original
    /// endogenous/xenogene class is saved, and SetXenotypeDirect changes only the displayed identity.
    /// The exact Life Force value and Wraith-derived ability cooldowns are also carried through relapse.
    /// </summary>
    public sealed class Hediff_WraithRetroviralHumanization : HediffWithComps
    {
        private List<GeneDef> removedEndogenes = new List<GeneDef>();
        private List<GeneDef> removedXenogenes = new List<GeneDef>();
        private List<AbilityDef> removedWraithAbilities = new List<AbilityDef>();
        private List<int> removedAbilityCooldowns = new List<int>();
        private float savedLifeForce = -1f;
        private int suppressionExpiryTick = -1;
        private bool stateApplied;
        private bool restoring;

        public int SuppressionTicksRemaining => suppressionExpiryTick < 0 ? 0 : Math.Max(0, suppressionExpiryTick - GenTicks.TicksGame);

        public override string LabelInBrackets
        {
            get
            {
                int ticks = SuppressionTicksRemaining;
                if (ticks > 0)
                    return ticks.ToStringTicksToPeriod() + " suppression remaining";
                if (WraithRetroviralUtility.HasHybridStabiliser(pawn))
                    return "primary suppression lapsed — stabiliser holding";
                return "suppression lapsed";
            }
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            if (!stateApplied)
                ApplyHumanizedState();
        }

        public override bool ShouldRemove =>
            base.ShouldRemove ||
            (stateApplied
                && !restoring
                && suppressionExpiryTick >= 0
                && GenTicks.TicksGame >= suppressionExpiryTick
                && !WraithRetroviralUtility.HasHybridStabiliser(pawn));

        public void RefreshSuppression()
        {
            if (!stateApplied || pawn == null || pawn.Dead)
                return;

            // Maintenance cannot be stockpiled. Every injection resets the clock to a little over
            // one day, matching the daily-treatment contract while allowing ordinary scheduling slack.
            suppressionExpiryTick = GenTicks.TicksGame + WraithRetroviralUtility.SuppressionWindowTicks;
        }

        public override void PostRemoved()
        {
            RestoreWraithState();
            base.PostRemoved();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref removedEndogenes, "removedEndogenes", LookMode.Def);
            Scribe_Collections.Look(ref removedXenogenes, "removedXenogenes", LookMode.Def);
            Scribe_Collections.Look(ref removedWraithAbilities, "removedWraithAbilities", LookMode.Def);
            Scribe_Collections.Look(ref removedAbilityCooldowns, "removedAbilityCooldowns", LookMode.Value);
            Scribe_Values.Look(ref savedLifeForce, "savedLifeForce", -1f);
            Scribe_Values.Look(ref suppressionExpiryTick, "suppressionExpiryTick", -1);
            Scribe_Values.Look(ref stateApplied, "stateApplied", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (removedEndogenes == null) removedEndogenes = new List<GeneDef>();
                if (removedXenogenes == null) removedXenogenes = new List<GeneDef>();
                if (removedWraithAbilities == null) removedWraithAbilities = new List<AbilityDef>();
                if (removedAbilityCooldowns == null) removedAbilityCooldowns = new List<int>();
            }
        }

        private void ApplyHumanizedState()
        {
            if (pawn?.genes == null || pawn.Dead || pawn.genes.Xenotype?.defName != WraithRetroviralUtility.WraithXenotypeDefName)
                return;

            XenotypeDef wraith = DefDatabase<XenotypeDef>.GetNamedSilentFail(WraithRetroviralUtility.WraithXenotypeDefName);
            if (wraith == null || wraith.genes.NullOrEmpty())
                return;

            removedEndogenes.Clear();
            removedXenogenes.Clear();
            removedWraithAbilities.Clear();
            removedAbilityCooldowns.Clear();

            Gene_Resource_LifeForce lifeForce = pawn.genes.GetFirstGeneOfType<Gene_Resource_LifeForce>();
            savedLifeForce = lifeForce == null ? -1f : lifeForce.Value;

            CaptureWraithAbilityState(wraith);

            HashSet<GeneDef> wraithGeneDefs = new HashSet<GeneDef>(wraith.genes);
            List<Gene> toRemove = pawn.genes.GenesListForReading
                .Where(g => g != null && wraithGeneDefs.Contains(g.def))
                .ToList();

            for (int i = 0; i < toRemove.Count; i++)
            {
                Gene gene = toRemove[i];
                if (pawn.genes.IsXenogene(gene))
                    removedXenogenes.Add(gene.def);
                else
                    removedEndogenes.Add(gene.def);
            }

            // Remove all Wraith-owned abilities before their source genes are removed. Gene removal
            // will harmlessly attempt the same cleanup for gene-granted abilities, while caste powers
            // are explicitly absent throughout the humanized state.
            RemoveRecordedWraithAbilities();

            for (int i = 0; i < toRemove.Count; i++)
                pawn.genes.RemoveGene(toRemove[i]);

            XenotypeDef humanized = DefDatabase<XenotypeDef>.GetNamedSilentFail(WraithRetroviralUtility.HumanizedXenotypeDefName);
            if (humanized == null)
            {
                // Nothing biological has been committed irreversibly yet: restore the recorded state
                // instead of leaving an unlabeled half-transformation.
                RestoreGenes(removedEndogenes, false);
                RestoreGenes(removedXenogenes, true);
                RestoreWraithAbilities();
                Gene_Resource_LifeForce rollbackLifeForce = pawn.genes.GetFirstGeneOfType<Gene_Resource_LifeForce>();
                if (rollbackLifeForce != null && savedLifeForce >= 0f)
                    rollbackLifeForce.Value = Math.Min(rollbackLifeForce.Max, Math.Max(0f, savedLifeForce));
                Log.Error("WNG Wraith retroviral treatment could not resolve the humanized xenotype; the transformation was rolled back.");
                return;
            }

            pawn.genes.SetXenotypeDirect(humanized);
            suppressionExpiryTick = GenTicks.TicksGame + WraithRetroviralUtility.SuppressionWindowTicks;
            stateApplied = true;

            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    pawn.LabelShortCap + " has entered a retrovirally humanized state. Wraith biology is suppressed, but daily maintenance injections are required.",
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
        }

        private void RestoreWraithState()
        {
            if (!stateApplied || restoring || pawn?.genes == null)
                return;

            restoring = true;
            try
            {
                XenotypeDef wraith = DefDatabase<XenotypeDef>.GetNamedSilentFail(WraithRetroviralUtility.WraithXenotypeDefName);
                if (wraith == null)
                {
                    Log.Error("WNG Wraith retroviral relapse could not resolve the Wraith xenotype; recorded biology was left untouched.");
                    return;
                }

                pawn.genes.SetXenotypeDirect(wraith);

                RestoreGenes(removedEndogenes, false);
                RestoreGenes(removedXenogenes, true);

                Gene_Resource_LifeForce lifeForce = pawn.genes.GetFirstGeneOfType<Gene_Resource_LifeForce>();
                if (lifeForce != null && savedLifeForce >= 0f)
                    lifeForce.Value = Math.Min(lifeForce.Max, Math.Max(0f, savedLifeForce));

                RestoreWraithAbilities();
                stateApplied = false;

                if (!pawn.Dead && pawn.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        "Suppression treatment lapsed for " + pawn.LabelShortCap + ". The recorded Wraith phenotype has reasserted itself.",
                        pawn,
                        MessageTypeDefOf.CautionInput,
                        historical: false);
                }
            }
            finally
            {
                restoring = false;
            }
        }

        private void RestoreGenes(List<GeneDef> defs, bool xenogene)
        {
            if (defs == null)
                return;

            for (int i = 0; i < defs.Count; i++)
            {
                GeneDef def = defs[i];
                if (def != null && pawn.genes.GetGene(def) == null)
                    pawn.genes.AddGene(def, xenogene);
            }
        }

        private void CaptureWraithAbilityState(XenotypeDef wraith)
        {
            if (pawn.abilities == null)
                return;

            HashSet<AbilityDef> defs = new HashSet<AbilityDef>();
            for (int i = 0; i < wraith.genes.Count; i++)
            {
                GeneDef geneDef = wraith.genes[i];
                if (geneDef?.abilities == null)
                    continue;
                for (int j = 0; j < geneDef.abilities.Count; j++)
                {
                    AbilityDef abilityDef = geneDef.abilities[j];
                    if (abilityDef != null)
                        defs.Add(abilityDef);
                }
            }

            foreach (string defName in new[] { "WNG_PredatorsPresence", "WNG_Compulsion", "WNG_QueensCommand" })
            {
                AbilityDef abilityDef = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
                if (abilityDef != null)
                    defs.Add(abilityDef);
            }

            foreach (AbilityDef def in defs)
            {
                Ability ability = pawn.abilities.GetAbility(def);
                if (ability == null)
                    continue;
                removedWraithAbilities.Add(def);
                removedAbilityCooldowns.Add(Math.Max(0, ability.CooldownTicksRemaining));
            }
        }

        private void RemoveRecordedWraithAbilities()
        {
            if (pawn.abilities == null)
                return;
            for (int i = 0; i < removedWraithAbilities.Count; i++)
            {
                AbilityDef def = removedWraithAbilities[i];
                if (def != null && pawn.abilities.GetAbility(def) != null)
                    pawn.abilities.RemoveAbility(def);
            }
        }

        private void RestoreWraithAbilities()
        {
            if (pawn.abilities == null)
                return;

            for (int i = 0; i < removedWraithAbilities.Count; i++)
            {
                AbilityDef def = removedWraithAbilities[i];
                if (def == null)
                    continue;

                Ability ability = pawn.abilities.GetAbility(def);
                if (ability == null)
                {
                    pawn.abilities.GainAbility(def);
                    ability = pawn.abilities.GetAbility(def);
                }

                int cooldown = i < removedAbilityCooldowns.Count ? Math.Max(0, removedAbilityCooldowns[i]) : 0;
                if (ability != null && cooldown > 0)
                    ability.StartCooldown(cooldown);
            }
        }
    }


    /// <summary>
    /// Temporary adjunct to retroviral humanization. The stabiliser does not humanize a Wraith and
    /// does not replace the primary suppression cocktail. It only holds an already suppressed
    /// phenotype through a short maintenance lapse. Re-dosing resets, rather than stacks, the window.
    /// </summary>
    public sealed class Hediff_HybridStabilised : HediffWithComps
    {
        public const int StabilisationWindowTicks = 180000; // 3 RimWorld days.
        private int expiryTick = -1;

        public int TicksRemaining => expiryTick < 0 ? 0 : Math.Max(0, expiryTick - GenTicks.TicksGame);

        public override string LabelInBrackets => TicksRemaining > 0
            ? TicksRemaining.ToStringTicksToPeriod() + " remaining"
            : "lapsed";

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Refresh();
        }

        public void Refresh()
        {
            if (pawn == null || pawn.Dead)
                return;
            expiryTick = GenTicks.TicksGame + StabilisationWindowTicks;
        }

        public override bool ShouldRemove => base.ShouldRemove || (expiryTick >= 0 && GenTicks.TicksGame >= expiryTick);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref expiryTick, "expiryTick", -1);
        }
    }

    public sealed class Recipe_AdministerHybridStabiliser : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return WraithRetroviralUtility.HumanizationFor(pawn) != null && base.AvailableOnNow(thing, part);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (WraithRetroviralUtility.HumanizationFor(pawn) == null)
                return "Hybrid stabilisation is only useful on an already retrovirally humanized Wraith.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || pawn.Dead || WraithRetroviralUtility.HumanizationFor(pawn) == null)
                return;

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(WraithRetroviralUtility.HybridStabilisedDefName);
            if (def == null)
            {
                Log.Error("WNG hybrid stabiliser could not resolve its HediffDef.");
                return;
            }

            if (billDoer != null)
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);

            Hediff_HybridStabilised stabilised = pawn.health.hediffSet.GetFirstHediffOfDef(def) as Hediff_HybridStabilised;
            if (stabilised == null)
            {
                pawn.health.AddHediff(def);
            }
            else
            {
                stabilised.Refresh();
            }
        }
    }

    public sealed class Recipe_AdministerWraithRetrovirus : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return !pawn.Dead
                && WraithHiveEcologyUtility.IsWraith(pawn)
                && !WraithRetroviralUtility.IsHumanized(pawn)
                && base.AvailableOnNow(thing, part);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || !WraithHiveEcologyUtility.IsWraith(pawn))
                return "Wraith retroviral treatment requires an active Wraith phenotype.";
            if (WraithRetroviralUtility.IsHumanized(pawn))
                return "This Wraith is already under retroviral humanization.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || pawn.Dead || !WraithHiveEcologyUtility.IsWraith(pawn) || WraithRetroviralUtility.IsHumanized(pawn))
                return;

            if (billDoer != null)
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);

            HediffDef def = WraithRetroviralUtility.HumanizationDef;
            if (def == null)
            {
                Log.Error("WNG Wraith retrovirus could not resolve its humanization HediffDef.");
                return;
            }

            pawn.health.AddHediff(def);
        }
    }

    public sealed class Recipe_AdministerWraithSuppression : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return WraithRetroviralUtility.HumanizationFor(pawn) != null && base.AvailableOnNow(thing, part);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (WraithRetroviralUtility.HumanizationFor(pawn) == null)
                return "Suppression injections are only useful while Wraith retroviral humanization is active.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            Hediff_WraithRetroviralHumanization state = WraithRetroviralUtility.HumanizationFor(pawn);
            if (state == null || pawn.Dead)
                return;

            if (billDoer != null)
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);

            state.RefreshSuppression();
        }
    }
}
