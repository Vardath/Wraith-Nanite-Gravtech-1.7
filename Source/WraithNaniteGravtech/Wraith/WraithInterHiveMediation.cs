using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace WraithNaniteGravtech
{
    internal static class WraithInterHiveMediationUtility
    {
        public const int BiomassCost = 60;
        public const int GoodwillGain = 20;
        public const int GoodwillCeiling = 80;

        private static readonly string[] MutableLineageDefNames =
        {
            WraithLineageUtility.CinderCourtDefName,
            WraithLineageUtility.VeiledHiveDefName,
            WraithLineageUtility.PaleCovenantDefName
        };

        public static IEnumerable<Faction> MutableLineages()
        {
            return MutableLineageDefNames
                .Select(WraithLineageUtility.Resolve)
                .Where(f => f != null && !f.defeated && f.def?.permanentEnemy != true)
                .OrderBy(f => f.loadID);
        }

        public static bool TrySelectPair(out Faction first, out Faction second)
        {
            first = null;
            second = null;
            int lowestGoodwill = int.MaxValue;
            List<Faction> candidates = MutableLineages().ToList();

            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    Faction a = candidates[i];
                    Faction b = candidates[j];
                    int current = a.BaseGoodwillWith(b);
                    int delta = AllowedGoodwillGain(current);
                    if (delta <= 0 || !a.CanChangeGoodwillFor(b, delta))
                        continue;

                    if (current < lowestGoodwill)
                    {
                        lowestGoodwill = current;
                        first = a;
                        second = b;
                    }
                }
            }

            return first != null && second != null;
        }

        public static int AllowedGoodwillGain(int current)
        {
            int ceiling = Math.Max(-100, Math.Min(100, GoodwillCeiling));
            return Math.Max(0, Math.Min(GoodwillGain, ceiling - current));
        }

        public static bool PairStillValid(Faction first, Faction second, out int delta)
        {
            delta = 0;
            if (first == null ||
                second == null ||
                first == second ||
                first.defeated ||
                second.defeated ||
                first.def?.permanentEnemy == true ||
                second.def?.permanentEnemy == true ||
                !WraithLineageUtility.IsWraithLineage(first) ||
                !WraithLineageUtility.IsWraithLineage(second) ||
                first.def?.defName == WraithLineageUtility.SableBroodDefName ||
                second.def?.defName == WraithLineageUtility.SableBroodDefName)
            {
                return false;
            }

            delta = AllowedGoodwillGain(first.BaseGoodwillWith(second));
            return delta > 0 && first.CanChangeGoodwillFor(second, delta);
        }

        public static bool TryFindPaymentMap(out Map map)
        {
            map = null;
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            if (biomass == null || Find.Maps == null)
                return false;

            map = Find.Maps
                .Where(m => m != null && m.IsPlayerHome)
                .OrderByDescending(m => WraithHiveEcologyUtility.CountResource(m, biomass))
                .FirstOrDefault(m => WraithHiveEcologyUtility.CountResource(m, biomass) >= BiomassCost);
            return map != null;
        }

        public static bool TryConsumePayment(out Map map, out IntVec3 refundCell)
        {
            refundCell = IntVec3.Invalid;
            if (!TryFindPaymentMap(out map))
                return false;

            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            Thing source = map.listerThings.ThingsOfDef(biomass)
                .Where(t => t != null && !t.Destroyed && t.Spawned)
                .OrderBy(t => t.thingIDNumber)
                .FirstOrDefault();
            if (source == null)
                return false;

            refundCell = source.Position;
            return WraithHiveEcologyUtility.TryConsumeResource(map, biomass, BiomassCost);
        }

        public static void RefundPayment(Map map, IntVec3 cell)
        {
            ThingDef biomass = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Biomass");
            if (map == null || biomass == null || !cell.IsValid)
                return;

            WraithHiveEcologyUtility.SpawnResource(map, cell, biomass, BiomassCost);
        }

        public static string DisplayName(Faction faction)
        {
            if (faction == null)
                return "Wraith lineage";
            if (!faction.Name.NullOrEmpty())
                return faction.Name;
            return faction.def?.label?.CapitalizeFirst() ?? "Wraith lineage";
        }
    }

    public sealed class QuestNode_Root_WraithInterHiveMediation : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            return WraithInterHiveMediationUtility.TrySelectPair(out _, out _);
        }

        protected override void RunInt()
        {
            if (!WraithInterHiveMediationUtility.TrySelectPair(out Faction first, out Faction second))
                return;

            string acceptedSignal = QuestGenUtility.HardcodedSignalWithQuestID("Accepted");
            QuestPart_WraithInterHiveMediation part = new QuestPart_WraithInterHiveMediation
            {
                first = first,
                second = second,
                inSignal = acceptedSignal,
                signalListenMode = QuestPart.SignalListenMode.OngoingOnly
            };
            QuestGen.quest.AddPart(part);
        }
    }

    /// <summary>
    /// A real faction-politics quest. The colony commits finite cultured biomass, then the actual
    /// native NPC-to-NPC goodwill changes. Sable Brood can never be mediated because its permanent
    /// enemy identity remains authoritative. The biomass is refunded if goodwill mutation fails
    /// after payment, avoiding a half-committed transaction.
    /// </summary>
    public sealed class QuestPart_WraithInterHiveMediation : QuestPart_RequirementsToAccept
    {
        public Faction first;
        public Faction second;
        public string inSignal;
        private bool resolved;

        public override IEnumerable<Faction> InvolvedFactions
        {
            get
            {
                if (first != null)
                    yield return first;
                if (second != null && second != first)
                    yield return second;
            }
        }

        public override string DescriptionPart
        {
            get
            {
                if (!WraithInterHiveMediationUtility.PairStillValid(first, second, out int delta))
                    return "The proposed inter-Hive mediation is no longer politically viable.";

                int current = first.BaseGoodwillWith(second);
                return WraithInterHiveMediationUtility.DisplayName(first) + " and " +
                       WraithInterHiveMediationUtility.DisplayName(second) +
                       " currently stand at " + current + " goodwill. Committing " +
                       WraithInterHiveMediationUtility.BiomassCost +
                       " cultured Wraith biomass can improve their relationship by " +
                       delta + ", up to the mediation ceiling of " +
                       WraithInterHiveMediationUtility.GoodwillCeiling + ".";
            }
        }

        public override AcceptanceReport CanAccept()
        {
            if (resolved)
                return "This mediation is already resolved.";
            if (!WraithInterHiveMediationUtility.PairStillValid(first, second, out _))
                return "The selected Wraith lineages can no longer be mediated on these terms.";
            if (!WraithInterHiveMediationUtility.TryFindPaymentMap(out _))
                return "Requires 60 cultured Wraith biomass in one player home settlement.";
            return true;
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (resolved || signal.tag != inSignal)
                return;

            if (!WraithInterHiveMediationUtility.PairStillValid(first, second, out int delta))
            {
                resolved = true;
                quest.End(QuestEndOutcome.Fail);
                TryMessage(
                    "The inter-Hive channel closed before the exchange could be committed.",
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

            if (!WraithInterHiveMediationUtility.TryConsumePayment(out Map paymentMap, out IntVec3 refundCell))
            {
                resolved = true;
                quest.End(QuestEndOutcome.Fail);
                TryMessage(
                    "The mediation failed because the promised cultured biomass was no longer available.",
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

            bool goodwillCommitted = false;
            try
            {
                goodwillCommitted = first.TryAffectGoodwillWith(
                    second,
                    delta,
                    canSendMessage: false,
                    canSendHostilityLetter: false);
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Inter-Hive mediation goodwill mutation failed after payment: " + ex);
            }

            if (!goodwillCommitted)
            {
                try
                {
                    WraithInterHiveMediationUtility.RefundPayment(paymentMap, refundCell);
                }
                catch (Exception ex)
                {
                    Log.Error("[WNG] Inter-Hive mediation payment refund failed: " + ex);
                }

                resolved = true;
                quest.End(QuestEndOutcome.Fail);
                TryMessage(
                    "The mediation failed after the exchange was prepared. The colony's biomass commitment was returned where possible.",
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

            resolved = true;
            quest.End(QuestEndOutcome.Success);

            int newGoodwill = first.BaseGoodwillWith(second);
            TryMessage(
                "Inter-Hive mediation succeeded. " +
                WraithInterHiveMediationUtility.DisplayName(first) + " and " +
                WraithInterHiveMediationUtility.DisplayName(second) +
                " now stand at " + newGoodwill + " goodwill.",
                MessageTypeDefOf.PositiveEvent);
        }

        public override void Notify_FactionRemoved(Faction faction)
        {
            base.Notify_FactionRemoved(faction);
            if (first == faction)
                first = null;
            if (second == faction)
                second = null;
        }

        private static void TryMessage(string message, MessageTypeDef type)
        {
            try
            {
                Messages.Message(message, type);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Inter-Hive mediation state committed but presentation failed: " + ex.Message);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref first, "wngMediationFirst");
            Scribe_References.Look(ref second, "wngMediationSecond");
            Scribe_Values.Look(ref inSignal, "wngMediationAcceptedSignal");
            Scribe_Values.Look(ref resolved, "wngMediationResolved", false);
        }
    }
}
