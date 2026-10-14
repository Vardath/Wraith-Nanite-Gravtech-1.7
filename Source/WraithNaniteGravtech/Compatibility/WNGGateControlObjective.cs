using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public enum WNGGateControlObjectiveKind
    {
        None = 0,
        WraithHunt = 1
    }

    /// <summary>
    /// WNG-owned objective state layered over CatCraft's optional Stargate. It never dials,
    /// owns an address, changes an iris, or touches CatCraft receive buffers. It remembers the
    /// exact gate assigned to one hostile WNG corridor and exposes whether CatCraft can still use
    /// that same gate for the current Wraith faction.
    /// </summary>
    public sealed class MapComponent_WNGGateControlObjective : MapComponent
    {
        private const int ReconcileIntervalTicks = 120;
        private const int StartupGraceTicks = 600;

        private bool active;
        private WNGGateControlObjectiveKind kind;
        private string factionDefName;
        private Thing sourceGate;
        private IntVec3 sourceGateCell = IntVec3.Invalid;
        private int startedTick = -1;
        private int nextReconcileTick;
        private bool gateWasDenied;

        public MapComponent_WNGGateControlObjective(Map map) : base(map) { }

        public bool Active => active;
        public WNGGateControlObjectiveKind Kind => kind;
        public Thing SourceGate => sourceGate;
        public IntVec3 SourceGateCell => sourceGateCell;
        public string FactionDefName => factionDefName;

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public bool Begin(Thing gate, string factionDefName, WNGGateControlObjectiveKind objectiveKind)
        {
            if (active || gate == null || gate.Destroyed || !gate.Spawned || gate.Map != map ||
                objectiveKind == WNGGateControlObjectiveKind.None || factionDefName.NullOrEmpty())
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            active = true;
            kind = objectiveKind;
            this.factionDefName = factionDefName;
            sourceGate = gate;
            sourceGateCell = gate.Position;
            startedTick = now;
            nextReconcileTick = SafeFutureTick(now, ReconcileIntervalTicks);
            gateWasDenied = false;
            return true;
        }

        public bool ExactGateUsable
        {
            get
            {
                Faction faction = WraithStargateHuntUtility.ResolveFaction(factionDefName);
                return faction != null && WraithStargateHuntUtility.IsExactGateUsable(map, sourceGate, faction);
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!active || Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < nextReconcileTick)
                return;
            nextReconcileTick = SafeFutureTick(now, ReconcileIntervalTicks);

            if ((long)now - startedTick >= StartupGraceTicks && !ExactGateUsable)
                gateWasDenied = true;

            if (!RelatedMissionActive())
                Resolve();
        }

        private bool RelatedMissionActive()
        {
            if (kind == WNGGateControlObjectiveKind.WraithHunt)
                return map.GetComponent<MapComponent_WraithGateHunt>()?.Active == true;
            return false;
        }

        public string AlertLabel =>
            kind == WNGGateControlObjectiveKind.WraithHunt
                ? "Gate objective: Wraith hunt"
                : "Gate-control objective";

        public string AlertExplanation
        {
            get
            {
                MapComponent_WraithGateHunt hunt = map.GetComponent<MapComponent_WraithGateHunt>();
                string gateState;
                if (sourceGate == null || sourceGate.Destroyed || !sourceGate.Spawned || sourceGate.Map != map)
                    gateState = "destroyed or absent";
                else
                    gateState = ExactGateUsable ? "available to the Wraith corridor" : "currently denied";

                string huntState = hunt == null || !hunt.Active
                    ? "ending"
                    : hunt.Extracting ? "withdrawing" : "hunting";
                int captures = hunt?.Captures ?? 0;
                int quota = hunt?.CaptureQuota ?? 0;

                string reinforcement;
                if (hunt?.ReinforcementLaunched == true)
                    reinforcement = "follow-on redial already delivered";
                else if (hunt?.ReinforcementDenied == true)
                    reinforcement = "follow-on redial denied or abandoned";
                else if (hunt?.ReinforcementPending == true)
                    reinforcement = "one bounded follow-on redial remains possible";
                else
                    reinforcement = "no follow-on redial pending";

                return "A Wraith hunting party is using one exact Stargate as its strategic corridor.\n\n"
                    + "Gate state: " + gateState + ".\n"
                    + "Hunt state: " + huntState + ".\n"
                    + "Dart captures currently aboard the physical hostile Dart: " + captures
                    + (quota > 0 ? "/" + quota : "") + ".\n"
                    + "Reinforcement: " + reinforcement + ".\n\n"
                    + (ExactGateUsable
                        ? "Objective: deny this exact gate before the Wraith reuse it for reinforcement or fast extraction."
                        : "Objective: keep this exact gate denied until the Wraith mission resolves. Re-enabling it can restore the fast route.")
                    + "\n\nWNG observes CatCraft's own current arrival resolution only. It never dials the Stargate or manipulates CatCraft receive buffers.";
            }
        }

        private void Resolve()
        {
            if (!active)
                return;

            bool denied = gateWasDenied;
            TargetInfo target = new TargetInfo(sourceGateCell, map);
            ResetState();
            try
            {
                Messages.Message(
                    denied
                        ? "The hostile Stargate corridor has ended after the colony denied its assigned gate."
                        : "The hostile Stargate corridor has ended.",
                    target,
                    denied ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }
            catch { }
        }

        private void ResetState()
        {
            active = false;
            kind = WNGGateControlObjectiveKind.None;
            factionDefName = null;
            sourceGate = null;
            sourceGateCell = IntVec3.Invalid;
            startedTick = -1;
            nextReconcileTick = 0;
            gateWasDenied = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref active, "wngGateObjectiveActive", false);
            Scribe_Values.Look(ref kind, "wngGateObjectiveKind", WNGGateControlObjectiveKind.None);
            Scribe_Values.Look(ref factionDefName, "wngGateObjectiveFaction");
            Scribe_References.Look(ref sourceGate, "wngGateObjectiveSourceGate");
            Scribe_Values.Look(ref sourceGateCell, "wngGateObjectiveSourceGateCell");
            Scribe_Values.Look(ref startedTick, "wngGateObjectiveStartedTick", -1);
            Scribe_Values.Look(ref nextReconcileTick, "wngGateObjectiveNextReconcile", 0);
            Scribe_Values.Look(ref gateWasDenied, "wngGateObjectiveWasDenied", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!active || kind == WNGGateControlObjectiveKind.None || factionDefName.NullOrEmpty())
                {
                    ResetState();
                    return;
                }

                if (!sourceGateCell.IsValid)
                {
                    if (sourceGate != null && !sourceGate.Destroyed && sourceGate.Spawned && sourceGate.Map == map)
                        sourceGateCell = sourceGate.Position;
                    else
                    {
                        ResetState();
                        return;
                    }
                }

                int now = Find.TickManager?.TicksGame ?? 0;
                if (startedTick < 0 || startedTick > now)
                    startedTick = now;
                if (nextReconcileTick <= 0 || nextReconcileTick > SafeFutureTick(now, ReconcileIntervalTicks))
                    nextReconcileTick = SafeFutureTick(now, ReconcileIntervalTicks);
            }
        }
    }

    public sealed class Alert_WNGGateControlObjective : Alert
    {
        public Alert_WNGGateControlObjective()
        {
            defaultPriority = AlertPriority.High;
            defaultLabel = "Gate-control objective";
            defaultExplanation = "A hostile WNG Stargate corridor requires attention.";
        }

        public override AlertReport GetReport()
        {
            MapComponent_WNGGateControlObjective objective = FindActiveObjective();
            if (objective == null)
                return AlertReport.Inactive;
            if (objective.SourceGate != null && objective.SourceGate.Spawned && !objective.SourceGate.Destroyed)
                return AlertReport.CulpritIs(objective.SourceGate);
            return AlertReport.Active;
        }

        public override string GetLabel()
        {
            return FindActiveObjective()?.AlertLabel ?? defaultLabel;
        }

        public override TaggedString GetExplanation()
        {
            return FindActiveObjective()?.AlertExplanation ?? defaultExplanation;
        }

        private static MapComponent_WNGGateControlObjective FindActiveObjective()
        {
            foreach (Map candidate in Find.Maps)
            {
                MapComponent_WNGGateControlObjective objective =
                    candidate?.GetComponent<MapComponent_WNGGateControlObjective>();
                if (objective != null && objective.Active)
                    return objective;
            }
            return null;
        }
    }
}
