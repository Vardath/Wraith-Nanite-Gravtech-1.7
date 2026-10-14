using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech.Trade
{
    public sealed class CompProperties_KassaMarketDemand : CompProperties
    {
        public float demandPerUnit = 0.05f;

        public CompProperties_KassaMarketDemand()
        {
            compClass = typeof(CompKassaMarketDemand);
        }
    }

    /// <summary>
    /// Uses RimWorld's native per-item PreTraded callback. The trader has already split the exact
    /// sold quantity into this Thing before the callback fires, so parent.stackCount is authoritative.
    /// </summary>
    public sealed class CompKassaMarketDemand : ThingComp
    {
        private CompProperties_KassaMarketDemand Props => (CompProperties_KassaMarketDemand)props;

        public override void PrePreTraded(TradeAction action, Pawn playerNegotiator, ITrader trader)
        {
            base.PrePreTraded(action, playerNegotiator, trader);
            if (action != TradeAction.PlayerSells || trader?.Faction == null || trader.Faction == Faction.OfPlayer)
                return;

            int soldCount = Math.Max(1, parent?.stackCount ?? 1);
            Current.Game?.GetComponent<KassaMarketDemandRegistry>()?.RecordSale(
                trader.Faction,
                soldCount,
                Math.Max(0f, Props.demandPerUnit));
        }
    }

    public sealed class KassaFactionDemandState : IExposable
    {
        public int factionLoadId = -1;
        public float demand;
        public float lifetimeDemandCreated;
        public int lifetimeUnitsSold;
        public int lastSaleTick = -1;
        public int lastUpdateTick = -1;
        public int nextReturnTick = -1;
        public int returnVisits;
        public bool dependencyNoticeSent;

        public KassaFactionDemandState()
        {
        }

        public KassaFactionDemandState(int factionLoadId, int now)
        {
            this.factionLoadId = factionLoadId;
            lastUpdateTick = now;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
            Scribe_Values.Look(ref demand, "demand", 0f);
            Scribe_Values.Look(ref lifetimeDemandCreated, "lifetimeDemandCreated", 0f);
            Scribe_Values.Look(ref lifetimeUnitsSold, "lifetimeUnitsSold", 0);
            Scribe_Values.Look(ref lastSaleTick, "lastSaleTick", -1);
            Scribe_Values.Look(ref lastUpdateTick, "lastUpdateTick", -1);
            Scribe_Values.Look(ref nextReturnTick, "nextReturnTick", -1);
            Scribe_Values.Look(ref returnVisits, "returnVisits", 0);
            Scribe_Values.Look(ref dependencyNoticeSent, "dependencyNoticeSent", false);
        }
    }

    /// <summary>
    /// Persistent faction-level Kassa market demand. Actual sales create demand. Demand fades slowly
    /// without resupply and, once established, can cause the same non-hostile faction to send one of
    /// its own Kassa-compatible trader caravans back to a player home map.
    /// </summary>
    public sealed class KassaMarketDemandRegistry : GameComponent
    {
        // Centralized provisional balance. Keep these here rather than scattering economic magic numbers.
        public const float ReturnDemandThreshold = 4f;
        public const float MaxDemand = 50f;
        public const float DemandDecayPerDay = 0.05f;
        public const int CheckIntervalTicks = 2500;
        public const int RetryTicks = 60000;
        public const float MinReturnDays = 5f;
        public const float MaxReturnDays = 12f;

        private List<KassaFactionDemandState> states = new List<KassaFactionDemandState>();
        private int nextCheckTick;

        public KassaMarketDemandRegistry(Game game)
        {
        }

        public void RecordSale(Faction faction, int soldCount, float demandPerUnit)
        {
            if (faction == null || faction == Faction.OfPlayer || faction.defeated || soldCount <= 0 || demandPerUnit <= 0f)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            KassaFactionDemandState state = GetOrCreateState(faction.loadID, now);
            ApplyDecay(state, now);

            float addedDemand = Math.Min(MaxDemand, soldCount * demandPerUnit);
            state.demand = Mathf.Clamp(state.demand + addedDemand, 0f, MaxDemand);
            state.lifetimeDemandCreated += addedDemand;
            state.lifetimeUnitsSold += soldCount;
            state.lastSaleTick = now;

            int desiredReturn = now + ReturnIntervalTicks(state.demand);
            if (state.nextReturnTick < now || state.nextReturnTick < 0 || desiredReturn < state.nextReturnTick)
                state.nextReturnTick = desiredReturn;

            if (!state.dependencyNoticeSent && state.demand >= ReturnDemandThreshold)
            {
                state.dependencyNoticeSent = true;
                Messages.Message(
                    $"Kassa demand has taken hold among {faction.Name}. Their traders may return seeking continued supply.",
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            states ??= new List<KassaFactionDemandState>();
            int now = Find.TickManager?.TicksGame ?? 0;
            foreach (KassaFactionDemandState state in states)
            {
                if (state != null && state.lastUpdateTick < 0)
                    state.lastUpdateTick = now;
            }
            nextCheckTick = now + CheckIntervalTicks;
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextCheckTick)
                return;

            nextCheckTick = now + CheckIntervalTicks;
            if (states == null || states.Count == 0)
                return;

            foreach (KassaFactionDemandState state in states.ToList())
            {
                if (state == null || state.factionLoadId < 0)
                    continue;

                ApplyDecay(state, now);
                if (state.demand < ReturnDemandThreshold || state.nextReturnTick < 0 || now < state.nextReturnTick)
                    continue;

                Faction faction = ResolveFaction(state.factionLoadId);
                if (faction == null || faction.defeated || faction.HostileTo(Faction.OfPlayer))
                {
                    state.nextReturnTick = now + RetryTicks;
                    continue;
                }

                if (TrySendReturnCustomer(faction))
                {
                    state.returnVisits++;
                    state.nextReturnTick = now + ReturnIntervalTicks(state.demand);
                }
                else
                {
                    state.nextReturnTick = now + RetryTicks;
                }
            }
        }

        private KassaFactionDemandState GetOrCreateState(int factionLoadId, int now)
        {
            states ??= new List<KassaFactionDemandState>();
            KassaFactionDemandState state = states.FirstOrDefault(s => s != null && s.factionLoadId == factionLoadId);
            if (state != null)
                return state;

            state = new KassaFactionDemandState(factionLoadId, now);
            states.Add(state);
            return state;
        }

        private static void ApplyDecay(KassaFactionDemandState state, int now)
        {
            if (state == null)
                return;
            if (state.lastUpdateTick < 0)
            {
                state.lastUpdateTick = now;
                return;
            }

            int elapsed = Math.Max(0, now - state.lastUpdateTick);
            if (elapsed <= 0)
                return;

            float days = elapsed / 60000f;
            state.demand = Math.Max(0f, state.demand - DemandDecayPerDay * days);
            state.lastUpdateTick = now;
        }

        private static int ReturnIntervalTicks(float demand)
        {
            float intensity = Mathf.InverseLerp(ReturnDemandThreshold, MaxDemand, demand);
            float days = Mathf.Lerp(MaxReturnDays, MinReturnDays, intensity);
            days += Rand.Range(-1f, 1f);
            return Math.Max(60000, Mathf.RoundToInt(Math.Max(1f, days) * 60000f));
        }

        private static Faction ResolveFaction(int loadId)
        {
            return Find.FactionManager?.AllFactions?.FirstOrDefault(f => f != null && f.loadID == loadId);
        }

        private static Map BestPlayerHome()
        {
            return Find.Maps?
                .Where(m => m != null && m.IsPlayerHome)
                .OrderByDescending(m => m.PlayerWealthForStoryteller)
                .FirstOrDefault();
        }

        private static TraderKindDef FindKassaTraderKind(Faction faction)
        {
            if (faction?.def?.caravanTraderKinds == null || faction.def.caravanTraderKinds.Count == 0)
                return null;

            ThingDef raw = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_Kassa");
            ThingDef concentrate = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_KassaConcentrate");
            ThingDef distillate = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_KassaDistillate");

            return faction.def.caravanTraderKinds
                .Where(t => t != null && t.CalculatedCommonality > 0f)
                .Where(t => (raw != null && t.WillTrade(raw))
                         || (concentrate != null && t.WillTrade(concentrate))
                         || (distillate != null && t.WillTrade(distillate)))
                .OrderByDescending(t =>
                    (raw != null && t.WillTrade(raw) ? 1 : 0)
                    + (concentrate != null && t.WillTrade(concentrate) ? 1 : 0)
                    + (distillate != null && t.WillTrade(distillate) ? 1 : 0))
                .ThenByDescending(t => t.CalculatedCommonality)
                .FirstOrDefault();
        }

        private static bool TrySendReturnCustomer(Faction faction)
        {
            Map map = BestPlayerHome();
            TraderKindDef traderKind = FindKassaTraderKind(faction);
            IncidentDef incident = IncidentDefOf.TraderCaravanArrival;
            if (map == null || traderKind == null || incident?.Worker == null)
                return false;

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
            parms.faction = faction;
            parms.traderKind = traderKind;
            if (!incident.Worker.TryExecute(parms))
                return false;

            Messages.Message(
                $"{faction.Name} traders have returned as Kassa demand persists.",
                MessageTypeDefOf.PositiveEvent,
                historical: false);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref states, "wngKassaFactionDemandStates", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && states == null)
                states = new List<KassaFactionDemandState>();
        }
    }
}
