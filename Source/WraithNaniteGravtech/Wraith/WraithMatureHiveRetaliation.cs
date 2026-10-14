using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class WraithMatureHiveRetaliationState : IExposable
    {
        public int siteId = -1;
        public Faction faction;
        public int dueTick = -1;
        public int retryCount;
        public int responseKind;
        public int sourcePawnId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref siteId, "siteId", -1);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref dueTick, "dueTick", -1);
            Scribe_Values.Look(ref retryCount, "retryCount", 0);
            Scribe_Values.Look(ref responseKind, "responseKind", 0);
            Scribe_Values.Look(ref sourcePawnId, "sourcePawnId", 0);
        }
    }

    /// <summary>
    /// Exact-lineage delayed response to the physical destruction of a Mature Hive.
    /// This is intentionally independent of WraithStrategicHungerRegistry: destroying a Hive
    /// is a military/political consequence, not an ordinary feeding request or hunger-pressure event.
    /// </summary>
    public sealed class WraithMatureHiveRetaliationRegistry : GameComponent
    {
        private const int TickInterval = 250;
        private const int InitialDelayMinTicks = 120000;
        private const int InitialDelayMaxTicks = 240000;
        private const int RetryDelayTicks = 60000;
        private const int MaxRetries = 3;
        private const int ResponseKindHiveDestroyed = 0;
        private const int ResponseKindSleeperLeak = 1;
        private const float SleeperThreatFactor = 0.65f;

        private List<WraithMatureHiveRetaliationState> pending = new List<WraithMatureHiveRetaliationState>();
        private List<int> processedSiteIds = new List<int>();

        public WraithMatureHiveRetaliationRegistry(Game game)
        {
        }

        public void ScheduleHiveRetaliation(int siteId, Faction faction)
        {
            if (siteId < 0 || faction == null || faction.defeated || Faction.OfPlayer == null)
                return;
            if (!WraithLineageUtility.IsWraithLineage(faction) || !faction.HostileTo(Faction.OfPlayer))
                return;
            if (processedSiteIds.Contains(siteId) || pending.Any(x => x != null && x.siteId == siteId))
                return;

            // The site is consumed into the response pipeline once. Repeated SitePart ticks,
            // map reloads or save/reload therefore cannot schedule duplicate retaliation.
            processedSiteIds.Add(siteId);
            int now = Find.TickManager?.TicksGame ?? 0;
            pending.Add(new WraithMatureHiveRetaliationState
            {
                siteId = siteId,
                faction = faction,
                dueTick = now + Rand.RangeInclusive(InitialDelayMinTicks, InitialDelayMaxTicks),
                retryCount = 0,
                responseKind = ResponseKindHiveDestroyed,
                sourcePawnId = 0
            });
        }

        public bool ScheduleSleeperIntelligenceLeak(Pawn pawn, Faction sourceFaction)
        {
            if (pawn == null || pawn.thingIDNumber <= 0 || sourceFaction == null ||
                sourceFaction.defeated || Faction.OfPlayer == null)
                return false;
            if (!WraithLineageUtility.IsWraithLineage(sourceFaction) ||
                !sourceFaction.HostileTo(Faction.OfPlayer))
                return false;

            if (pending.Any(x =>
                    x != null &&
                    x.responseKind == ResponseKindSleeperLeak &&
                    x.sourcePawnId == pawn.thingIDNumber))
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            pending.Add(new WraithMatureHiveRetaliationState
            {
                siteId = -1,
                faction = sourceFaction,
                dueTick = now + Rand.RangeInclusive(InitialDelayMinTicks, InitialDelayMaxTicks),
                retryCount = 0,
                responseKind = ResponseKindSleeperLeak,
                sourcePawnId = pawn.thingIDNumber
            });
            return true;
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null || pending == null || pending.Count == 0)
                return;

            int now = Find.TickManager.TicksGame;
            if (now % TickInterval != 0)
                return;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                WraithMatureHiveRetaliationState state = pending[i];
                if (state == null)
                {
                    pending.RemoveAt(i);
                    continue;
                }
                if (now < state.dueTick)
                    continue;

                Faction faction = state.faction;
                if (faction == null || faction.defeated || Faction.OfPlayer == null || !faction.HostileTo(Faction.OfPlayer))
                {
                    // Peace or faction defeat cancels the response. This component never forces hostility.
                    pending.RemoveAt(i);
                    continue;
                }

                Map home = BestPlayerHome();
                if (home == null)
                {
                    if (!ScheduleRetryOrExpire(state, now))
                        pending.RemoveAt(i);
                    continue;
                }

                float points = Mathf.Max(35f, StorytellerUtility.DefaultThreatPointsNow(home));
                if (state.responseKind == ResponseKindSleeperLeak)
                    points = Mathf.Max(35f, points * SleeperThreatFactor);

                IncidentParms parms = new IncidentParms
                {
                    forced = true,
                    target = home,
                    faction = faction,
                    points = points,
                    raidStrategy = RaidStrategyDefOf.ImmediateAttack
                };

                if (IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                {
                    // The native raid is already committed. Carrier staging is bounded physical
                    // support only and must never turn a successful retaliation into a retry.
                    try
                    {
                        WraithStrategicCarrierUtility.TryStageSupportCarrier(
                            home,
                            faction,
                            points,
                            cruiserThreshold: 1800f);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(
                            "[WNG] Wraith retaliation raid committed but strategic carrier staging failed: " +
                            ex.Message);
                    }

                    if (state.responseKind == ResponseKindSleeperLeak)
                        BestEffortSleeperBreachLetter(home, faction);
                    pending.RemoveAt(i);
                    continue;
                }

                if (!ScheduleRetryOrExpire(state, now))
                    pending.RemoveAt(i);
            }
        }

        private static void BestEffortSleeperBreachLetter(Map home, Faction faction)
        {
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Wraith intelligence breach",
                    "A Wraith strike force is approaching with unsettling precision. Colony routines and infrastructure appear to have been compromised by intelligence leaked from inside the settlement. The exact sleeper source is not identified by the response itself.",
                    LetterDefOf.ThreatBig,
                    home == null ? null : new TargetInfo(home.Center, home));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Sleeper intelligence response committed but breach presentation failed: " + ex.Message);
            }
        }

        private static bool ScheduleRetryOrExpire(WraithMatureHiveRetaliationState state, int now)
        {
            state.retryCount++;
            if (state.retryCount > MaxRetries)
                return false;
            state.dueTick = now + RetryDelayTicks;
            return true;
        }

        private static Map BestPlayerHome()
        {
            return Find.Maps
                .Where(m => m != null && m.IsPlayerHome)
                .OrderByDescending(m => m.PlayerWealthForStoryteller)
                .FirstOrDefault();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "wngMatureHiveRetaliationPending", LookMode.Deep);
            Scribe_Collections.Look(ref processedSiteIds, "wngMatureHiveRetaliationProcessedSites", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pending == null)
                    pending = new List<WraithMatureHiveRetaliationState>();
                if (processedSiteIds == null)
                    processedSiteIds = new List<int>();

                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    WraithMatureHiveRetaliationState state = pending[i];
                    if (state == null)
                    {
                        pending.RemoveAt(i);
                        continue;
                    }

                    state.responseKind =
                        state.responseKind == ResponseKindSleeperLeak
                            ? ResponseKindSleeperLeak
                            : ResponseKindHiveDestroyed;
                    state.retryCount = Math.Max(0, Math.Min(MaxRetries, state.retryCount));

                    bool valid =
                        state.responseKind == ResponseKindSleeperLeak
                            ? state.sourcePawnId > 0
                            : state.siteId >= 0;
                    if (!valid)
                        pending.RemoveAt(i);
                }

                processedSiteIds = processedSiteIds
                    .Where(id => id >= 0)
                    .Distinct()
                    .ToList();
            }
        }
    }
}
