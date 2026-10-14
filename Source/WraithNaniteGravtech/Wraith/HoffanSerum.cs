using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class HoffanSerumUtility
    {
        private const string ProtectionDefName = "WNG_HoffanProtection";
        private const string FatalReactionDefName = "WNG_HoffanFatalReaction";
        private const string WraithPoisonDefName = "WNG_HoffanWraithPoisoning";

        public static HediffDef ProtectionDef => DefDatabase<HediffDef>.GetNamedSilentFail(ProtectionDefName);
        public static HediffDef FatalReactionDef => DefDatabase<HediffDef>.GetNamedSilentFail(FatalReactionDefName);
        public static HediffDef WraithPoisonDef => DefDatabase<HediffDef>.GetNamedSilentFail(WraithPoisonDefName);

        public static bool HasProtection(Pawn pawn)
        {
            HediffDef def = ProtectionDef;
            return def != null && pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) != null;
        }

        public static bool EligibleRecipient(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null || pawn.RaceProps == null)
                return false;
            if (!pawn.RaceProps.Humanlike || !pawn.RaceProps.IsFlesh || pawn.RaceProps.IsMechanoid)
                return false;

            string xenotype = pawn.genes?.Xenotype?.defName;
            return xenotype != "WNG_Wraith" &&
                   xenotype != "WNG_NanitePrecursor" &&
                   xenotype != "WNG_HumanFormReplicator";
        }

        public static void ApplyPrototype(Pawn pawn, float fatalChance)
        {
            if (!EligibleRecipient(pawn) || HasProtection(pawn))
                return;

            AddPermanentHediff(pawn, ProtectionDef);

            if (fatalChance > 0f && Rand.Chance(fatalChance))
            {
                AddPermanentHediff(pawn, FatalReactionDef);
                if (pawn.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        pawn.LabelShortCap + " is suffering a catastrophic reaction to the prototype Hoffan serum. The anti-Wraith protection remains active, but the reaction is life-threatening.",
                        pawn,
                        MessageTypeDefOf.ThreatBig,
                        historical: false);
                }
            }
            else if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    pawn.LabelShortCap + " survived prototype Hoffan inoculation and is now resistant to Wraith feeding, though the treatment suppresses immune response.",
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
        }

        public static bool TryPoisonFeeder(Pawn wraith, Pawn protectedVictim)
        {
            if (wraith == null || wraith.Dead || !HasProtection(protectedVictim))
                return false;

            HediffDef poisonDef = WraithPoisonDef;
            if (poisonDef == null || wraith.health?.hediffSet == null)
                return false;

            if (wraith.health.hediffSet.GetFirstHediffOfDef(poisonDef) == null)
                AddPermanentHediff(wraith, poisonDef);

            if (protectedVictim?.Faction == Faction.OfPlayer || wraith.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    "Hoffan serum blocked the feeding attempt. The serum reacted with the Wraith feeding chemistry and poisoned " + wraith.LabelShortCap + ".",
                    wraith,
                    MessageTypeDefOf.ThreatBig,
                    historical: false);
            }
            return true;
        }

        private static void AddPermanentHediff(Pawn pawn, HediffDef def)
        {
            if (pawn?.health?.hediffSet == null || def == null || pawn.health.hediffSet.GetFirstHediffOfDef(def) != null)
                return;

            Hediff hediff = HediffMaker.MakeHediff(def, pawn);
            hediff.Severity = 1f;
            pawn.health.AddHediff(hediff);
        }
    }

    /// <summary>
    /// First-generation Hoffan serum deliberately retains the catastrophic prototype risk shown in
    /// Poisoning the Well. The first successful inoculation gives permanent feeding protection and
    /// rolls exactly once for the fatal systemic reaction; repeat doses do not reroll the victim.
    /// </summary>
    public sealed class IngestionOutcomeDoer_HoffanSerum : IngestionOutcomeDoer
    {
        public float fatalReactionChance = 0.50f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            HoffanSerumUtility.ApplyPrototype(pawn, fatalReactionChance);
        }
    }
}
