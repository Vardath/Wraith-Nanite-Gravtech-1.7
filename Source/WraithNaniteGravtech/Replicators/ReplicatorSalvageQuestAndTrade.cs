using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace WraithNaniteGravtech
{
    internal static class ReplicatorSalvageConsignmentUtility
    {
        public static Map BestEligibleMap()
        {
            return Find.Maps?
                .Where(m => m != null && ReplicatorCargoInfiltrationUtility.EligibleMap(m))
                .OrderByDescending(m => m.PlayerWealthForStoryteller)
                .FirstOrDefault();
        }
    }

    public sealed class QuestNode_Root_ReplicatorSalvageConsignment : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            return WNGSettingsUtility.ReplicatorStoryEventsEnabled &&
                   ReplicatorSalvageConsignmentUtility.BestEligibleMap() != null;
        }

        protected override void RunInt()
        {
            QuestGen.quest.AddPart(
                new QuestPart_ReplicatorSalvageConsignment
                {
                    inSignal =
                        QuestGenUtility.HardcodedSignalWithQuestID("Accepted"),
                    signalListenMode = QuestPart.SignalListenMode.OngoingOnly
                });
        }
    }

    /// <summary>
    /// Free salvage offer. Acceptance rechecks that there is still an eligible player-home map,
    /// then uses the same physical cargo transaction as the storyteller incident. No payment or
    /// hidden quest-side contamination state exists; the exact sealed carrier owns the later hazard.
    /// </summary>
    public sealed class QuestPart_ReplicatorSalvageConsignment :
        QuestPart_RequirementsToAccept
    {
        public string inSignal;
        private bool resolved;

        public override string DescriptionPart =>
            "Accepting authorizes an immediate cargo-pod delivery to one eligible player settlement. " +
            "The consignment contains ordinary industrial supplies and one exact sealed machine-salvage item.";

        public override AcceptanceReport CanAccept()
        {
            if (resolved)
                return "This salvage consignment is already resolved.";
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled)
                return "Replicator story events are disabled.";
            if (ReplicatorSalvageConsignmentUtility.BestEligibleMap() == null)
                return "No player home settlement is currently eligible to receive the salvage consignment.";
            return true;
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (resolved || signal.tag != inSignal)
                return;

            Map map =
                ReplicatorSalvageConsignmentUtility.BestEligibleMap();
            if (map == null)
            {
                resolved = true;
                quest.End(QuestEndOutcome.Fail);
                TryMessage(
                    "The salvage broker could not identify an eligible settlement for delivery.",
                    MessageTypeDefOf.RejectInput);
                return;
            }

            bool committed =
                ReplicatorCargoInfiltrationUtility.TryDropConsignment(
                    map,
                    includeSupplies: true,
                    out IntVec3 dropCell);

            resolved = true;
            if (!committed)
            {
                quest.End(QuestEndOutcome.Fail);
                TryMessage(
                    "The salvage consignment failed before any physical delivery was committed.",
                    MessageTypeDefOf.NegativeEvent);
                return;
            }

            quest.End(QuestEndOutcome.Success);
            ReplicatorCargoInfiltrationUtility.BestEffortLetter(
                "Salvage consignment delivered",
                "The accepted off-world salvage lot has arrived: useful industrial supplies and one sealed precision machine component. " +
                "The exact physical carrier is now part of the colony's inventory and any later behavior belongs to that item itself.",
                LetterDefOf.PositiveEvent,
                new TargetInfo(dropCell, map));
        }

        private static void TryMessage(
            string text,
            MessageTypeDef type)
        {
            try
            {
                Messages.Message(text, type);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Replicator salvage quest resolved but message presentation failed: " +
                    ex.Message);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref inSignal,
                "wngReplicatorSalvageAcceptedSignal");
            Scribe_Values.Look(
                ref resolved,
                "wngReplicatorSalvageResolved",
                false);
        }
    }

    /// <summary>
    /// Rare trader insertion for the exact sealed carrier. Transit time remains inert because the
    /// carrier's countdown only advances while physically spawned on a map.
    /// </summary>
    public sealed class StockGenerator_ReplicatorSealedSalvage :
        StockGenerator
    {
        public ThingDef thingDef;
        public float chance = 0.035f;

        public override IEnumerable<Thing> GenerateThings(
            PlanetTile forTile,
            Faction faction = null)
        {
            if (!WNGSettingsUtility.ReplicatorStoryEventsEnabled ||
                thingDef == null ||
                Rand.Value > Math.Max(0f, Math.Min(1f, chance)))
                yield break;

            Thing item = ThingMaker.MakeThing(thingDef);
            if (item != null)
                yield return item;
        }

        public override bool HandlesThingDef(ThingDef def)
        {
            return def == thingDef;
        }

        public override IEnumerable<string> ConfigErrors(
            TraderKindDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;

            if (thingDef == null)
                yield return
                    "StockGenerator_ReplicatorSealedSalvage requires thingDef.";
            else if (!thingDef.tradeability.TraderCanSell())
                yield return
                    thingDef.defName +
                    " must be trader-sellable for sealed salvage infiltration.";

            if (chance < 0f || chance > 1f)
                yield return
                    "StockGenerator_ReplicatorSealedSalvage chance must be between 0 and 1.";
        }
    }
}
