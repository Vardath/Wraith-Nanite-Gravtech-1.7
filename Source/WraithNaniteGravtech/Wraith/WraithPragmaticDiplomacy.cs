using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class WraithPragmaticDiplomacyTuningDef : Def
    {
        public int checkIntervalTicks = 1200;
        public int initialOfferMinTicks = 240000;
        public int initialOfferMaxTicks = 420000;
        public int repeatOfferMinTicks = 420000;
        public int repeatOfferMaxTicks = 720000;
        public int retryOfferTicks = 60000;

        public int cinderBiomassCost = 100;
        public int cinderBiomassGoodwill = 18;
        public int cinderDeclineGoodwill = -4;

        public int veiledBiomassCost = 65;
        public int veiledBiomassGoodwill = 15;
        public int veiledMedicineCost = 5;
        public int veiledNeutroamineCost = 8;
        public int veiledMedicalGoodwill = 10;

        public int paleBiomassCost = 40;
        public int paleBiomassGoodwill = 14;
        public int paleMedicineCost = 4;
        public int paleNeutroamineCost = 5;
        public int paleMedicalGoodwill = 12;
    }

    internal sealed class WraithPragmaticDiplomacyTerms
    {
        public int biomassCost;
        public int biomassGoodwill;
        public int medicineCost;
        public int neutroamineCost;
        public int medicalGoodwill;
        public int declineGoodwill;

        public static WraithPragmaticDiplomacyTerms For(
            Faction faction,
            WraithPragmaticDiplomacyTuningDef tuning)
        {
            if (tuning == null)
                return null;

            switch (faction?.def?.defName)
            {
                case WraithLineageUtility.CinderCourtDefName:
                    return new WraithPragmaticDiplomacyTerms
                    {
                        biomassCost = Math.Max(0, tuning.cinderBiomassCost),
                        biomassGoodwill = tuning.cinderBiomassGoodwill,
                        declineGoodwill = tuning.cinderDeclineGoodwill
                    };
                case WraithLineageUtility.VeiledHiveDefName:
                    return new WraithPragmaticDiplomacyTerms
                    {
                        biomassCost = Math.Max(0, tuning.veiledBiomassCost),
                        biomassGoodwill = tuning.veiledBiomassGoodwill,
                        medicineCost = Math.Max(0, tuning.veiledMedicineCost),
                        neutroamineCost = Math.Max(0, tuning.veiledNeutroamineCost),
                        medicalGoodwill = tuning.veiledMedicalGoodwill
                    };
                case WraithLineageUtility.PaleCovenantDefName:
                    return new WraithPragmaticDiplomacyTerms
                    {
                        biomassCost = Math.Max(0, tuning.paleBiomassCost),
                        biomassGoodwill = tuning.paleBiomassGoodwill,
                        medicineCost = Math.Max(0, tuning.paleMedicineCost),
                        neutroamineCost = Math.Max(0, tuning.paleNeutroamineCost),
                        medicalGoodwill = tuning.paleMedicalGoodwill
                    };
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Dependency-independent pragmatic diplomacy for the three mutable Wraith lineages.
    ///
    /// This registry deliberately owns only NON-FEEDING exchange offers. The current master-plan
    /// boundary makes WraithStrategicHungerRegistry the sole authority allowed to open faction
    /// feeding-request UI. Ordinary Drain Life, Mature-Hive feeding stock and this diplomacy layer
    /// remain separate systems.
    ///
    /// Successful exchanges spend real colony resources, then mutate ordinary RimWorld goodwill.
    /// If the goodwill commit fails after payment, the consumed resources are refunded.
    /// </summary>
    public sealed class WraithPragmaticDiplomacyRegistry : GameComponent
    {
        private int nextCinderOfferTick = -1;
        private int nextVeiledOfferTick = -1;
        private int nextPaleOfferTick = -1;
        private bool offerWindowOpen;

        public WraithPragmaticDiplomacyRegistry(Game game) { }

        private WraithPragmaticDiplomacyTuningDef Tuning =>
            DefDatabase<WraithPragmaticDiplomacyTuningDef>.GetNamedSilentFail(
                "WNG_WraithPragmaticDiplomacyTuning")
            ?? DefDatabase<WraithPragmaticDiplomacyTuningDef>.AllDefsListForReading.FirstOrDefault();

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(1, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private static int RandomDelay(int min, int max)
        {
            min = Math.Max(1, min);
            max = Math.Max(min, max);
            return Rand.RangeInclusive(min, max);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (offerWindowOpen || Find.TickManager == null || Find.WindowStack == null || Faction.OfPlayer == null)
                return;

            WraithPragmaticDiplomacyTuningDef tuning = Tuning;
            if (tuning == null)
                return;

            int now = Find.TickManager.TicksGame;
            int interval = Math.Max(60, tuning.checkIntervalTicks);
            if (now % interval != 0)
                return;

            Map home = Find.Maps
                .Where(m => m != null && m.IsPlayerHome)
                .OrderByDescending(m => m.PlayerWealthForStoryteller)
                .FirstOrDefault();
            if (home == null)
                return;

            InitializeOfferTicks(now, tuning);
            foreach (string defName in DueLineages(now))
            {
                Faction faction = WraithLineageUtility.Resolve(defName);
                if (!CanNegotiate(faction))
                {
                    SetNextOfferTick(
                        defName,
                        SafeFutureTick(now, Math.Max(1, tuning.retryOfferTicks)));
                    continue;
                }

                OpenDiplomaticApproach(faction, home, tuning);
                return;
            }
        }

        private void InitializeOfferTicks(int now, WraithPragmaticDiplomacyTuningDef tuning)
        {
            if (nextCinderOfferTick < 0)
                nextCinderOfferTick = SafeFutureTick(
                    now,
                    RandomDelay(tuning.initialOfferMinTicks, tuning.initialOfferMaxTicks));
            if (nextVeiledOfferTick < 0)
                nextVeiledOfferTick = SafeFutureTick(
                    now,
                    RandomDelay(tuning.initialOfferMinTicks, tuning.initialOfferMaxTicks));
            if (nextPaleOfferTick < 0)
                nextPaleOfferTick = SafeFutureTick(
                    now,
                    RandomDelay(tuning.initialOfferMinTicks, tuning.initialOfferMaxTicks));
        }

        private IEnumerable<string> DueLineages(int now)
        {
            return new[]
            {
                new KeyValuePair<string, int>(
                    WraithLineageUtility.CinderCourtDefName,
                    nextCinderOfferTick),
                new KeyValuePair<string, int>(
                    WraithLineageUtility.VeiledHiveDefName,
                    nextVeiledOfferTick),
                new KeyValuePair<string, int>(
                    WraithLineageUtility.PaleCovenantDefName,
                    nextPaleOfferTick)
            }
            .Where(pair => pair.Value >= 0 && now >= pair.Value)
            .OrderBy(pair => pair.Value)
            .Select(pair => pair.Key);
        }

        private static bool CanNegotiate(Faction faction)
        {
            if (faction == null ||
                faction.defeated ||
                faction == Faction.OfPlayer ||
                faction.def?.permanentEnemy == true ||
                !WraithLineageUtility.IsWraithLineage(faction) ||
                faction.def?.defName == WraithLineageUtility.SableBroodDefName)
                return false;

            return faction.CanChangeGoodwillFor(Faction.OfPlayer, 1);
        }

        private void OpenDiplomaticApproach(
            Faction faction,
            Map home,
            WraithPragmaticDiplomacyTuningDef tuning)
        {
            WraithPragmaticDiplomacyTerms terms =
                WraithPragmaticDiplomacyTerms.For(faction, tuning);
            if (terms == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;

            // Reserve the cadence before UI creation. Closing the later terms menu without choosing
            // an exchange cannot create a rapid re-open loop.
            SetNextOfferTick(
                faction.def.defName,
                SafeFutureTick(now, Math.Max(1, tuning.repeatOfferMaxTicks)));

            string description =
                DiplomaticOpening(faction) +
                "\n\nCurrent goodwill: " +
                faction.BaseGoodwillWith(Faction.OfPlayer) +
                ".\n\nThe Hive is offering a material exchange, not feeding access. " +
                "Strategic Hunger remains the sole authority for any faction feeding request.";

            Action negotiate = () =>
            {
                offerWindowOpen = false;
                OpenTermsDialog(faction, home, tuning);
            };
            Action decline = () =>
            {
                offerWindowOpen = false;
                ResolveDecline(faction, home, tuning);
            };

            offerWindowOpen = true;
            try
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    description,
                    "Hear terms",
                    negotiate,
                    "Decline",
                    decline,
                    faction.Name + " — Wraith negotiation",
                    buttonADestructive: false,
                    acceptAction: negotiate,
                    cancelAction: decline));
            }
            catch (Exception ex)
            {
                offerWindowOpen = false;
                SetNextOfferTick(
                    faction.def.defName,
                    SafeFutureTick(now, Math.Max(1, tuning.retryOfferTicks)));
                Log.Error("[WNG] Failed to open pragmatic Wraith diplomacy window: " + ex);
            }
        }

        private static string DiplomaticOpening(Faction faction)
        {
            switch (faction?.def?.defName)
            {
                case WraithLineageUtility.CinderCourtDefName:
                    return faction.Name +
                           " has opened a hard-edged channel. The Court is willing to value a useful supplier over immediate predation, but expects substantial tribute.";
                case WraithLineageUtility.VeiledHiveDefName:
                    return faction.Name +
                           " has made discreet contact. The Veiled Hive prefers controlled material exchange to wasteful open conflict.";
                case WraithLineageUtility.PaleCovenantDefName:
                    return faction.Name +
                           " has requested negotiation. The Pale Covenant is prepared to exchange biological support and medical culture supplies as part of coexistence.";
                default:
                    return faction?.Name + " has opened a Wraith diplomatic channel.";
            }
        }

        private void OpenTermsDialog(
            Faction faction,
            Map home,
            WraithPragmaticDiplomacyTuningDef tuning)
        {
            if (!CanNegotiate(faction) || home == null || !home.IsPlayerHome)
            {
                ScheduleAfterOffer(faction, tuning);
                return;
            }

            WraithPragmaticDiplomacyTerms terms =
                WraithPragmaticDiplomacyTerms.For(faction, tuning);
            if (terms == null)
            {
                ScheduleAfterOffer(faction, tuning);
                return;
            }

            List<Dialog_WNGChoiceList.Choice> choices = new List<Dialog_WNGChoiceList.Choice>();

            int biomassAvailable = ResourceCount(home, "WNG_Biomass");
            bool biomassEnabled =
                biomassAvailable >= terms.biomassCost &&
                CanGainGoodwill(faction, terms.biomassGoodwill);
            string biomassLabel =
                "Offer " + terms.biomassCost +
                " cultured biomass (" + biomassAvailable +
                " available) — goodwill +" + terms.biomassGoodwill;
            choices.Add(new Dialog_WNGChoiceList.Choice(
                biomassLabel,
                biomassEnabled
                    ? (Action)(() => ResolveBiomassDeal(faction, home, terms, tuning))
                    : null,
                biomassEnabled ? null : "Insufficient biomass or goodwill cannot increase further."));

            if (terms.medicineCost > 0 || terms.neutroamineCost > 0)
            {
                int medicineAvailable = ResourceCount(home, "MedicineIndustrial");
                int neutroamineAvailable = ResourceCount(home, "Neutroamine");
                bool medicalEnabled =
                    medicineAvailable >= terms.medicineCost &&
                    neutroamineAvailable >= terms.neutroamineCost &&
                    CanGainGoodwill(faction, terms.medicalGoodwill);
                string medicalLabel =
                    "Offer culture supplies: " +
                    terms.medicineCost + " industrial medicine + " +
                    terms.neutroamineCost + " neutroamine — goodwill +" +
                    terms.medicalGoodwill +
                    " (have " + medicineAvailable + "/" + neutroamineAvailable + ")";
                choices.Add(new Dialog_WNGChoiceList.Choice(
                    medicalLabel,
                    medicalEnabled
                        ? (Action)(() => ResolveMedicalDeal(faction, home, terms, tuning))
                        : null,
                    medicalEnabled ? null : "Insufficient supplies or goodwill cannot increase further."));
            }

            // Deliberately no prisoner/feeding option here. Strategic Hunger exclusively owns it.
            choices.Add(new Dialog_WNGChoiceList.Choice(
                "End negotiations",
                () => ScheduleAfterOffer(faction, tuning)));

            try
            {
                Find.WindowStack.Add(new Dialog_WNGChoiceList(
                    faction.Name + " — Wraith terms",
                    "Choose a material reply to the Hive. Feeding access is handled separately by Strategic Hunger.",
                    choices));
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Failed to open pragmatic Wraith terms dialog: " + ex);
                ScheduleAfterOffer(faction, tuning);
            }
        }

        private void ResolveBiomassDeal(
            Faction faction,
            Map home,
            WraithPragmaticDiplomacyTerms terms,
            WraithPragmaticDiplomacyTuningDef tuning)
        {
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            if (biomass == null ||
                !CanGainGoodwill(faction, terms.biomassGoodwill) ||
                WraithHiveEcologyUtility.CountResource(home, biomass) < terms.biomassCost)
            {
                RejectChangedTerms(faction, tuning);
                return;
            }

            if (!WraithHiveEcologyUtility.TryConsumeResource(home, biomass, terms.biomassCost))
            {
                RejectChangedTerms(faction, tuning);
                return;
            }

            if (!TryApplyGoodwill(faction, terms.biomassGoodwill))
            {
                Refund(home, biomass, terms.biomassCost);
                RejectChangedTerms(
                    faction,
                    tuning,
                    "The Hive could not finalize the goodwill change. The cultured biomass tribute was returned.");
                return;
            }

            ScheduleAfterOffer(faction, tuning);
            TryNotify(
                faction.Name +
                " accepted the cultured biomass. Ordinary RimWorld goodwill now records the negotiated relationship change.",
                MessageTypeDefOf.PositiveEvent);
        }

        private void ResolveMedicalDeal(
            Faction faction,
            Map home,
            WraithPragmaticDiplomacyTerms terms,
            WraithPragmaticDiplomacyTuningDef tuning)
        {
            ThingDef medicine = DefDatabase<ThingDef>.GetNamedSilentFail("MedicineIndustrial");
            ThingDef neutroamine = DefDatabase<ThingDef>.GetNamedSilentFail("Neutroamine");
            if (medicine == null ||
                neutroamine == null ||
                !CanGainGoodwill(faction, terms.medicalGoodwill) ||
                WraithHiveEcologyUtility.CountResource(home, medicine) < terms.medicineCost ||
                WraithHiveEcologyUtility.CountResource(home, neutroamine) < terms.neutroamineCost)
            {
                RejectChangedTerms(faction, tuning);
                return;
            }

            bool medicineSpent =
                WraithHiveEcologyUtility.TryConsumeResource(
                    home,
                    medicine,
                    terms.medicineCost);
            if (!medicineSpent)
            {
                RejectChangedTerms(faction, tuning);
                return;
            }

            bool neutroamineSpent =
                WraithHiveEcologyUtility.TryConsumeResource(
                    home,
                    neutroamine,
                    terms.neutroamineCost);
            if (!neutroamineSpent)
            {
                Refund(home, medicine, terms.medicineCost);
                RejectChangedTerms(faction, tuning);
                return;
            }

            if (!TryApplyGoodwill(faction, terms.medicalGoodwill))
            {
                Refund(home, medicine, terms.medicineCost);
                Refund(home, neutroamine, terms.neutroamineCost);
                RejectChangedTerms(
                    faction,
                    tuning,
                    "The Hive could not finalize the goodwill change. The medical culture supplies were returned.");
                return;
            }

            ScheduleAfterOffer(faction, tuning);
            TryNotify(
                faction.Name +
                " accepted the medical culture supplies for living technology and captive care. Ordinary RimWorld goodwill records the result.",
                MessageTypeDefOf.PositiveEvent);
        }

        private void ResolveDecline(
            Faction faction,
            Map home,
            WraithPragmaticDiplomacyTuningDef tuning)
        {
            WraithPragmaticDiplomacyTerms terms =
                WraithPragmaticDiplomacyTerms.For(faction, tuning);
            ScheduleAfterOffer(faction, tuning);

            if (terms == null || terms.declineGoodwill >= 0)
                return;

            bool applied = TryApplyGoodwill(faction, terms.declineGoodwill);
            TryNotify(
                applied
                    ? faction.Name + " regarded the refusal as weakness and withdrew its material offer."
                    : faction.Name + " withdrew its material offer.",
                MessageTypeDefOf.NegativeEvent);
        }

        private void RejectChangedTerms(
            Faction faction,
            WraithPragmaticDiplomacyTuningDef tuning,
            string message = null)
        {
            ScheduleAfterOffer(faction, tuning);
            TryNotify(
                message ??
                "The proposed Wraith exchange could no longer be completed on the offered terms.",
                MessageTypeDefOf.RejectInput);
        }

        private void ScheduleAfterOffer(
            Faction faction,
            WraithPragmaticDiplomacyTuningDef tuning)
        {
            if (faction?.def == null || tuning == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            SetNextOfferTick(
                faction.def.defName,
                SafeFutureTick(
                    now,
                    RandomDelay(
                        tuning.repeatOfferMinTicks,
                        tuning.repeatOfferMaxTicks)));
        }

        private static bool CanGainGoodwill(Faction faction, int delta)
        {
            return faction != null &&
                   Faction.OfPlayer != null &&
                   delta != 0 &&
                   faction.CanChangeGoodwillFor(Faction.OfPlayer, delta);
        }

        private static bool TryApplyGoodwill(Faction faction, int delta)
        {
            if (!CanGainGoodwill(faction, delta))
                return false;
            try
            {
                return faction.TryAffectGoodwillWith(
                    Faction.OfPlayer,
                    delta,
                    canSendMessage: false,
                    canSendHostilityLetter: true);
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Pragmatic Wraith diplomacy goodwill commit failed: " + ex);
                return false;
            }
        }

        private static int ResourceCount(Map map, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            return def == null ? 0 : WraithHiveEcologyUtility.CountResource(map, def);
        }

        private static void Refund(Map map, ThingDef def, int count)
        {
            if (map == null || def == null || count <= 0)
                return;
            try
            {
                WraithHiveEcologyUtility.SpawnResource(map, map.Center, def, count);
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Pragmatic Wraith diplomacy refund failed: " + ex);
            }
        }

        private static void TryNotify(string text, MessageTypeDef type)
        {
            try
            {
                Messages.Message(text, type);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Pragmatic Wraith diplomacy committed but notification failed: " + ex.Message);
            }
        }

        private void SetNextOfferTick(string defName, int tick)
        {
            switch (defName)
            {
                case WraithLineageUtility.CinderCourtDefName:
                    nextCinderOfferTick = tick;
                    break;
                case WraithLineageUtility.VeiledHiveDefName:
                    nextVeiledOfferTick = tick;
                    break;
                case WraithLineageUtility.PaleCovenantDefName:
                    nextPaleOfferTick = tick;
                    break;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref nextCinderOfferTick,
                "wngCinderPragmaticDiplomacyNextTick",
                -1);
            Scribe_Values.Look(
                ref nextVeiledOfferTick,
                "wngVeiledPragmaticDiplomacyNextTick",
                -1);
            Scribe_Values.Look(
                ref nextPaleOfferTick,
                "wngPalePragmaticDiplomacyNextTick",
                -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                offerWindowOpen = false;
                nextCinderOfferTick = Math.Max(-1, nextCinderOfferTick);
                nextVeiledOfferTick = Math.Max(-1, nextVeiledOfferTick);
                nextPaleOfferTick = Math.Max(-1, nextPaleOfferTick);
            }
        }
    }
}
