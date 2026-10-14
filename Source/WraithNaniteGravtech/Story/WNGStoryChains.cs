using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace WraithNaniteGravtech
{
    public enum WNGStoryBranch
    {
        Wraith,
        Replicator,
        Ancient
    }

    public sealed class WNGStoryProgress : GameComponent
    {
        private int wraithMask;
        private int replicatorMask;
        private int ancientMask;
        private bool convergenceSeen;
        private bool sawInfiltrationMission;

        public WNGStoryProgress(Game game) { }

        public bool IsVisited(WNGStoryBranch branch, int stage)
        {
            int bit = 1 << Math.Max(0, Math.Min(2, stage));
            return (MaskFor(branch) & bit) != 0;
        }

        public bool IsBranchComplete(WNGStoryBranch branch) => (MaskFor(branch) & 7) == 7;

        public bool AllBranchesComplete =>
            IsBranchComplete(WNGStoryBranch.Wraith)
            && IsBranchComplete(WNGStoryBranch.Replicator)
            && IsBranchComplete(WNGStoryBranch.Ancient);

        public bool ConvergenceSeen => convergenceSeen;

        public void MarkVisited(WNGStoryBranch branch, int stage)
        {
            int bit = 1 << Math.Max(0, Math.Min(2, stage));
            switch (branch)
            {
                case WNGStoryBranch.Wraith:
                    wraithMask |= bit;
                    break;
                case WNGStoryBranch.Replicator:
                    replicatorMask |= bit;
                    break;
                case WNGStoryBranch.Ancient:
                    ancientMask |= bit;
                    break;
            }
        }

        public void MarkConvergence() => convergenceSeen = true;

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now % 250 != 0)
                return;

            foreach (Map map in Find.Maps)
            {
                Site site = map?.Parent as Site;
                if (site?.parts == null)
                    continue;

                foreach (SitePart part in site.parts)
                {
                    switch (part?.def?.defName)
                    {
                        case "WNG_WraithRuinedLaboratory":
                            MarkVisited(WNGStoryBranch.Wraith, 0);
                            break;
                        case "WNG_WraithCloningInstallation":
                            MarkVisited(WNGStoryBranch.Wraith, 1);
                            break;
                        case "WNG_WraithMatureHive":
                            MarkVisited(WNGStoryBranch.Wraith, 2);
                            break;
                        case "WNG_ReplicatorConsumedRuin":
                            MarkVisited(WNGStoryBranch.Replicator, 0);
                            break;
                        case "WNG_ReplicatorQueenVault":
                            MarkVisited(WNGStoryBranch.Replicator, 2);
                            break;
                        case "WNG_PrecursorLaboratorySite":
                            MarkVisited(WNGStoryBranch.Ancient, 0);
                            break;
                        case "WNG_PrecursorVaultSite":
                            MarkVisited(WNGStoryBranch.Ancient, 1);
                            break;
                        case "WNG_AsuranDormantFacility":
                            MarkVisited(WNGStoryBranch.Ancient, 2);
                            break;
                    }
                }
            }

            foreach (Map map in Find.Maps.Where(m => m != null && m.IsPlayerHome))
            {
                MapComponent_HumanFormInfiltration component = map.GetComponent<MapComponent_HumanFormInfiltration>();
                if (component == null)
                    continue;

                if (component.HasActiveMission)
                {
                    sawInfiltrationMission = true;
                    bool stillUnderCover = map.mapPawns.AllPawnsSpawned
                        .Any(p => p != null && HumanFormInfiltrationUtility.IsUnderCover(p));
                    if (!stillUnderCover)
                        MarkVisited(WNGStoryBranch.Replicator, 1);
                }
                else if (sawInfiltrationMission)
                {
                    MarkVisited(WNGStoryBranch.Replicator, 1);
                }
            }
        }

        private int MaskFor(WNGStoryBranch branch)
        {
            switch (branch)
            {
                case WNGStoryBranch.Wraith:
                    return wraithMask;
                case WNGStoryBranch.Replicator:
                    return replicatorMask;
                default:
                    return ancientMask;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref wraithMask, "wngStoryWraithMask", 0);
            Scribe_Values.Look(ref replicatorMask, "wngStoryReplicatorMask", 0);
            Scribe_Values.Look(ref ancientMask, "wngStoryAncientMask", 0);
            Scribe_Values.Look(ref convergenceSeen, "wngStoryConvergenceSeen", false);
            Scribe_Values.Look(ref sawInfiltrationMission, "wngStorySawInfiltration", false);
        }
    }

    internal static class WNGStoryChainUtility
    {
        public static WNGStoryProgress Progress => Current.Game?.GetComponent<WNGStoryProgress>();

        public static Map BestPlayerHome()
        {
            return Find.Maps
                .Where(m => m != null && m.IsPlayerHome)
                .OrderByDescending(m => m.PlayerWealthForStoryteller)
                .FirstOrDefault();
        }

        public static bool HasActiveChain(WNGStoryBranch branch)
        {
            return Find.QuestManager?.QuestsListForReading != null
                && Find.QuestManager.QuestsListForReading.Any(q =>
                    q != null
                    && (q.State == QuestState.Ongoing || q.State == QuestState.NotYetAccepted)
                    && q.PartsListForReading.OfType<QuestPart_WNGStoryChain>().Any(p => p.Branch == branch));
        }

        public static Site FindSite(string sitePartDefName)
        {
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail(sitePartDefName);
            if (part == null || Find.WorldObjects == null)
                return null;

            return Find.WorldObjects.AllWorldObjects
                .OfType<Site>()
                .FirstOrDefault(s => s?.parts != null && s.parts.Any(p => p?.def == part));
        }

        public static bool SiteStillExists(Site site)
        {
            return site != null
                && Find.WorldObjects != null
                && Find.WorldObjects.AllWorldObjects.Contains(site);
        }

        public static bool TryFireIncident(string incidentDefName, Map home)
        {
            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDefName);
            if (incident == null || home == null)
                return false;

            IncidentParms parms = new IncidentParms
            {
                forced = true,
                target = home,
                points = Math.Max(250f, StorytellerUtility.DefaultThreatPointsNow(home))
            };
            return incident.Worker.TryExecute(parms);
        }
    }

    public abstract class QuestNode_Root_WNGStoryBranch : QuestNode
    {
        protected abstract WNGStoryBranch StoryBranch { get; }

        protected override bool TestRunInt(Slate slate)
        {
            // The Replicator branch is driven by Replicator incidents. Do not offer a quest
            // whose first/third stages are explicitly disabled by the player's story-event toggle.
            if (StoryBranch == WNGStoryBranch.Replicator &&
                !WNGSettingsUtility.ReplicatorStoryEventsEnabled)
                return false;

            WNGStoryProgress progress = WNGStoryChainUtility.Progress;
            return WNGStoryChainUtility.BestPlayerHome() != null
                && progress != null
                && !progress.IsBranchComplete(StoryBranch)
                && !WNGStoryChainUtility.HasActiveChain(StoryBranch);
        }

        protected override void RunInt()
        {
            QuestGen.quest.AddPart(new QuestPart_WNGStoryChain
            {
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID("Accepted"),
                inSignalEnable = QuestGenUtility.HardcodedSignalWithQuestID("Accepted"),
                Branch = StoryBranch
            });
        }
    }

    public sealed class QuestNode_Root_WraithLegacy : QuestNode_Root_WNGStoryBranch
    {
        protected override WNGStoryBranch StoryBranch => WNGStoryBranch.Wraith;
    }

    public sealed class QuestNode_Root_ReplicatorPattern : QuestNode_Root_WNGStoryBranch
    {
        protected override WNGStoryBranch StoryBranch => WNGStoryBranch.Replicator;
    }

    public sealed class QuestNode_Root_AncientLegacy : QuestNode_Root_WNGStoryBranch
    {
        protected override WNGStoryBranch StoryBranch => WNGStoryBranch.Ancient;
    }

    public sealed class QuestPart_WNGStoryChain : QuestPartActivable
    {
        public string inSignal;
        public WNGStoryBranch Branch;

        private int stage;
        private Site currentSite;
        private bool started;
        private bool sawInfiltrationMission;
        private int nextLaunchAttemptTick;

        public override string DescriptionPart
        {
            get
            {
                if (!started)
                    return "Legacy trail: awaiting acceptance.";
                if (stage >= 3)
                    return "Legacy trail: complete.";
                return StageDescription();
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (started || signal.tag != inSignal)
                return;
            started = true;
            SkipAlreadyCompletedStages();
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (!started || stage >= 3)
                return;

            WNGStoryProgress progress = WNGStoryChainUtility.Progress;
            if (progress == null)
                return;

            SkipAlreadyCompletedStages();
            if (stage >= 3)
                return;

            if (Branch == WNGStoryBranch.Replicator && stage == 1)
                TickInfiltrationStage(progress);
            else
                TickSiteStage(progress);
        }

        private void TickSiteStage(WNGStoryProgress progress)
        {
            string sitePartDefName = SitePartDefName();
            string incidentDefName = IncidentDefName();
            if (string.IsNullOrEmpty(sitePartDefName) || string.IsNullOrEmpty(incidentDefName))
                return;

            if (!WNGStoryChainUtility.SiteStillExists(currentSite))
                currentSite = WNGStoryChainUtility.FindSite(sitePartDefName);

            if (currentSite != null && currentSite.HasMap)
            {
                progress.MarkVisited(Branch, stage);
                Advance();
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (currentSite != null || now < nextLaunchAttemptTick)
                return;

            nextLaunchAttemptTick = SafeFutureTick(now, 600);
            if (WNGStoryChainUtility.TryFireIncident(incidentDefName, WNGStoryChainUtility.BestPlayerHome()))
                currentSite = WNGStoryChainUtility.FindSite(sitePartDefName);
        }

        private void TickInfiltrationStage(WNGStoryProgress progress)
        {
            Map home = WNGStoryChainUtility.BestPlayerHome();
            if (home == null)
                return;

            MapComponent_HumanFormInfiltration component = home.GetComponent<MapComponent_HumanFormInfiltration>();
            if (component == null)
                return;

            if (component.HasActiveMission)
            {
                sawInfiltrationMission = true;
                bool underCover = home.mapPawns.AllPawnsSpawned
                    .Any(p => p != null && HumanFormInfiltrationUtility.IsUnderCover(p));
                if (!underCover)
                {
                    progress.MarkVisited(Branch, stage);
                    Advance();
                }
                return;
            }

            if (sawInfiltrationMission)
            {
                progress.MarkVisited(Branch, stage);
                Advance();
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextLaunchAttemptTick)
                return;

            nextLaunchAttemptTick = SafeFutureTick(now, 600);
            if (WNGStoryChainUtility.TryFireIncident("WNG_HumanFormInfiltration", home))
                sawInfiltrationMission = component.HasActiveMission;
        }

        private void SkipAlreadyCompletedStages()
        {
            WNGStoryProgress progress = WNGStoryChainUtility.Progress;
            while (progress != null && stage < 3 && progress.IsVisited(Branch, stage))
                stage++;

            if (stage >= 3)
                CompleteBranch();
        }

        private void Advance()
        {
            stage++;
            currentSite = null;
            sawInfiltrationMission = false;
            nextLaunchAttemptTick = 0;
            SkipAlreadyCompletedStages();
        }

        private void CompleteBranch()
        {
            if (quest.State != QuestState.Ongoing && quest.State != QuestState.NotYetAccepted)
                return;

            string label;
            string message;
            switch (Branch)
            {
                case WNGStoryBranch.Wraith:
                    label = "Wraith legacy trail connected";
                    message = "The ruined feeding laboratory, abandoned cloning installation and mature Hive now form one coherent biological history. Wraith living technology, feeding ecology and lineage development remain a distinct biological system.";
                    break;
                case WNGStoryBranch.Replicator:
                    label = "Replicator pattern trail connected";
                    message = "The consumed ruin, exact human-form infiltrator and Queen-pattern vault now resolve into one Replicator continuity trail. Block swarms, human-form nanites and sovereign control remain distinct layers rather than interchangeable mechanics.";
                    break;
                default:
                    label = "Ancient legacy trail connected";
                    message = "The open precursor laboratory, sealed vault and dormant Asuran facility now form a coherent Ancient-to-Asuran trail. The field evidence supports reconstruction without granting free endgame technology.";
                    break;
            }

            TrySendLetter(label, message);
            quest.End(QuestEndOutcome.Success);
        }

        private string SitePartDefName()
        {
            if (Branch == WNGStoryBranch.Wraith)
                return stage == 0 ? "WNG_WraithRuinedLaboratory"
                    : stage == 1 ? "WNG_WraithCloningInstallation"
                    : "WNG_WraithMatureHive";

            if (Branch == WNGStoryBranch.Replicator)
                return stage == 0 ? "WNG_ReplicatorConsumedRuin"
                    : stage == 2 ? "WNG_ReplicatorQueenVault"
                    : null;

            return stage == 0 ? "WNG_PrecursorLaboratorySite"
                : stage == 1 ? "WNG_PrecursorVaultSite"
                : "WNG_AsuranDormantFacility";
        }

        private string IncidentDefName()
        {
            if (Branch == WNGStoryBranch.Wraith)
                return stage == 0 ? "WNG_WraithRuinedLaboratoryDiscovery"
                    : stage == 1 ? "WNG_WraithCloningInstallationDiscovery"
                    : "WNG_WraithMatureHiveDiscovery";

            if (Branch == WNGStoryBranch.Replicator)
                return stage == 0 ? "WNG_ReplicatorConsumedRuinDiscovery"
                    : stage == 2 ? "WNG_ReplicatorQueenVaultDiscovery"
                    : null;

            return stage == 0 ? "WNG_PrecursorLaboratoryDiscovery"
                : stage == 1 ? "WNG_PrecursorVaultDiscovery"
                : "WNG_AsuranDormantFacilityDiscovery";
        }

        private string StageDescription()
        {
            if (Branch == WNGStoryBranch.Wraith)
                return stage == 0
                    ? "Follow the signal to the ruined Wraith feeding laboratory."
                    : stage == 1
                        ? "Trace the biological record to the abandoned Wraith cloning installation."
                        : "Reach the mature Wraith Hive and examine the living ecology directly.";

            if (Branch == WNGStoryBranch.Replicator)
                return stage == 0
                    ? "Investigate the Replicator-consumed ruin."
                    : stage == 1
                        ? "Resolve the exact human-form infiltration."
                        : "Reach the Queen-pattern vault and inspect the sovereign-continuity evidence.";

            return stage == 0
                ? "Investigate the open precursor laboratory."
                : stage == 1
                    ? "Follow the Ancient trail to the sealed precursor vault."
                    : "Reach the dormant Asuran facility and examine its later nanite architecture.";
        }

        private static void TrySendLetter(string label, string message)
        {
            try
            {
                Map home = WNGStoryChainUtility.BestPlayerHome();
                if (home != null)
                    Find.LetterStack.ReceiveLetter(label, message, LetterDefOf.PositiveEvent, new TargetInfo(home.Center, home));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Story-chain completion committed but presentation failed: " + ex.Message);
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(0, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "wngStorySignal");
            Scribe_Values.Look(ref Branch, "wngStoryBranch", WNGStoryBranch.Wraith);
            Scribe_Values.Look(ref stage, "wngStoryStage", 0);
            Scribe_References.Look(ref currentSite, "wngStoryCurrentSite");
            Scribe_Values.Look(ref started, "wngStoryStarted", false);
            Scribe_Values.Look(ref sawInfiltrationMission, "wngStorySawInfiltration", false);
            Scribe_Values.Look(ref nextLaunchAttemptTick, "wngStoryNextLaunchAttempt", 0);
        }
    }

    public sealed class QuestNode_Root_WNGConvergence : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            WNGStoryProgress progress = WNGStoryChainUtility.Progress;
            return progress != null && progress.AllBranchesComplete && !progress.ConvergenceSeen;
        }

        protected override void RunInt()
        {
            QuestGen.quest.AddPart(new QuestPart_WNGConvergence
            {
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID("Accepted")
            });
        }
    }

    public sealed class QuestPart_WNGConvergence : QuestPart
    {
        public string inSignal;
        private bool resolved;

        public override string DescriptionPart =>
            resolved ? "Convergence: resolved." : "Compare the completed Wraith, Replicator and Ancient/Asuran evidence trails.";

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (resolved || signal.tag != inSignal)
                return;

            WNGStoryProgress progress = WNGStoryChainUtility.Progress;
            if (progress == null || !progress.AllBranchesComplete || progress.ConvergenceSeen)
                return;

            progress.MarkConvergence();
            resolved = true;

            try
            {
                Map home = WNGStoryChainUtility.BestPlayerHome();
                if (home != null)
                    Find.LetterStack.ReceiveLetter(
                        "Three histories intersect",
                        "The completed field records show three distinct technologies intersecting across the same history: Wraith biology, Replicator nanites, and Ancient-derived Asuran systems. Their encounters explain shared ruins and conflicts without collapsing them into one technology. No free research or endgame artifact is awarded.",
                        LetterDefOf.PositiveEvent,
                        new TargetInfo(home.Center, home));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Story convergence committed but presentation failed: " + ex.Message);
            }

            quest.End(QuestEndOutcome.Success);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "wngConvergenceSignal");
            Scribe_Values.Look(ref resolved, "wngConvergenceResolved", false);
        }
    }
}
