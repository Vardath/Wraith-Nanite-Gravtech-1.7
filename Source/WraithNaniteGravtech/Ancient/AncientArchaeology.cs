using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class QuestNode_Root_AncientArchaeology : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            if (Find.Maps == null || !Find.Maps.Any(m => m != null && m.IsPlayerHome))
                return false;
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_AncientArchaeologyCache");
            if (part == null)
                return false;
            if (Find.QuestManager?.QuestsListForReading != null &&
                Find.QuestManager.QuestsListForReading.Any(q => q != null && (q.State == QuestState.Ongoing || q.State == QuestState.NotYetAccepted) &&
                    q.PartsListForReading.OfType<QuestPart_AncientArchaeology>().Any()))
                return false;
            return !Find.WorldObjects.AllWorldObjects.OfType<Site>()
                .Any(site => site?.parts != null && site.parts.Any(p => p?.def == part));
        }

        protected override void RunInt()
        {
            string accepted = QuestGenUtility.HardcodedSignalWithQuestID("Accepted");
            QuestGen.quest.AddPart(new QuestPart_AncientArchaeology { inSignal = accepted, inSignalEnable = accepted });
        }
    }

    public sealed class QuestPart_AncientArchaeology : QuestPartActivable
    {
        public string inSignal;
        private Site site;
        private bool started;
        private bool resolved;

        public override string DescriptionPart
        {
            get
            {
                if (resolved) return "Ancient archaeology lead: reached.";
                if (!started) return "Ancient archaeology lead: accept the survey data.";
                return site == null ? "Ancient archaeology lead: resolving coordinates." : "Ancient archaeology lead: travel to the marked cache.";
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (started || resolved || signal.tag != inSignal)
                return;
            started = true;
            if (!TryCreateSite())
            {
                resolved = true;
                quest.End(QuestEndOutcome.Fail);
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (!started || resolved)
                return;
            if (site == null)
            {
                resolved = true;
                quest.End(QuestEndOutcome.Fail);
                return;
            }
            if (site.HasMap)
            {
                resolved = true;
                quest.End(QuestEndOutcome.Success);
            }
        }

        private bool TryCreateSite()
        {
            SitePartDef part = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_AncientArchaeologyCache");
            if (part == null)
                return false;
            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, 8, 30, allowCaravans: false))
                return false;
            float points = Math.Max(250f, StorytellerUtility.DefaultSiteThreatPointsNow() * 0.65f);
            Site created = SiteMaker.MakeSite(part, tile, faction: null, ifHostileThenMustRemainHostile: false, threatPoints: points);
            if (created == null)
                return false;
            created.customLabel = "Ancient Survey Cache";
            try
            {
                Find.WorldObjects.Add(created);
                site = created;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "wngAncientArchaeologySignal");
            Scribe_References.Look(ref site, "wngAncientArchaeologySite");
            Scribe_Values.Look(ref started, "wngAncientArchaeologyStarted", false);
            Scribe_Values.Look(ref resolved, "wngAncientArchaeologyResolved", false);
        }
    }

    public sealed class SitePartWorker_AncientArchaeologyCache : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (map == null)
                return;
            PlaceArtifact(map, "WNG_RecoveredAncientDrone", map.Center);
            PlaceArtifact(map, "WNG_RecoveredVacuumEnergyModule", map.Center + new IntVec3(2, 0, 0));
        }

        private static void PlaceArtifact(Map map, string defName, IntVec3 preferred)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;
            Thing thing = ThingMaker.MakeThing(def);
            if (!GenPlace.TryPlaceThing(thing, preferred, map, ThingPlaceMode.Near) && !thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }
    }
}
