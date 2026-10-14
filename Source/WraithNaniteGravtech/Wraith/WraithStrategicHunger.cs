using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public enum WraithFeedingRequestStage
    {
        None,
        Subjects,
        Participants
    }

    public sealed class WraithStrategicHungerState : IExposable
    {
        public string factionDefName;
        public float hunger;
        public float raidPressure;
        public int nextRequestTick = -1;
        public int nextAggressionTick = -1;
        public WraithFeedingRequestStage requestStage;
        public List<Pawn> pendingSubjects = new List<Pawn>();
        public List<Pawn> pendingFeeders = new List<Pawn>();

        public WraithStrategicHungerState()
        {
        }

        public WraithStrategicHungerState(string factionDefName, float initialHunger)
        {
            this.factionDefName = factionDefName;
            hunger = initialHunger;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionDefName, "factionDefName");
            Scribe_Values.Look(ref hunger, "hunger", 0f);
            Scribe_Values.Look(ref raidPressure, "raidPressure", 0f);
            Scribe_Values.Look(ref nextRequestTick, "nextRequestTick", -1);
            Scribe_Values.Look(ref nextAggressionTick, "nextAggressionTick", -1);
            Scribe_Values.Look(ref requestStage, "requestStage", WraithFeedingRequestStage.None);
            Scribe_Collections.Look(ref pendingSubjects, "pendingSubjects", LookMode.Reference);
            Scribe_Collections.Look(ref pendingFeeders, "pendingFeeders", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingSubjects ??= new List<Pawn>();
                pendingFeeders ??= new List<Pawn>();
                pendingSubjects.RemoveAll(p => p == null || p.Destroyed);
                pendingFeeders.RemoveAll(p => p == null || p.Destroyed);
                hunger = Mathf.Clamp01(hunger);
                raidPressure = Mathf.Max(0f, raidPressure);
            }
        }
    }

    /// <summary>
    /// Faction-level feeding pressure. This system is intentionally separate from pawn-level
    /// Drain Life and from future Mature-Hive local feeding stock. Only this registry may open
    /// the strategic feeding request UI.
    /// </summary>
    public sealed class WraithStrategicHungerRegistry : GameComponent
    {
        private const int CheckIntervalTicks = 1200;
        private const int TicksPerDay = 60000;
        private const int RequestRetryTicks = 30000;
        private const int AcceptedRequestCooldownTicks = 240000;
        private const int RefusedRequestCooldownTicks = 120000;
        private const int AggressionCooldownTicks = 180000;
        private const float ControlledFeedVictimAgeYears = 10f;
        private const float ControlledFeedLifeForceGain = 0.34f;

        private List<WraithStrategicHungerState> states = new List<WraithStrategicHungerState>();

        [Unsaved(false)]
        private bool requestWindowOpen;

        public WraithStrategicHungerRegistry(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null || Find.FactionManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now % CheckIntervalTicks != 0)
                return;

            EnsureStates(now);

            // A pending decision always has priority and Dialog_MessageBox itself force-pauses play.
            WraithStrategicHungerState pending = states.FirstOrDefault(s => s.requestStage != WraithFeedingRequestStage.None);
            if (pending != null)
            {
                if (!requestWindowOpen)
                    ReopenPendingRequest(pending);
                return;
            }

            float elapsedDays = CheckIntervalTicks / (float)TicksPerDay;
            foreach (WraithStrategicHungerState state in states)
            {
                Faction faction = WraithLineageUtility.Resolve(state.factionDefName);
                if (faction == null || faction.defeated)
                    continue;

                WraithHungerPolicy policy = WraithHungerPolicy.For(state.factionDefName);
                state.hunger = Mathf.Clamp01(state.hunger + policy.hungerPerDay * elapsedDays);

                if (state.hunger >= policy.requestThreshold)
                {
                    Map home = BestPlayerHome();
                    List<Pawn> candidates = EligibleStrategicFeedingSubjects(home).ToList();
                    if (candidates.Count == 0)
                    {
                        // Genuine unresolved hunger becomes pressure even when the colony has
                        // nothing suitable to offer; this still never opens a request window.
                        state.raidPressure += policy.unresolvedPressurePerDay * elapsedDays;
                    }
                    else if (now >= state.nextRequestTick)
                    {
                        BeginRequest(state, faction, candidates, now);
                        return;
                    }
                }

                TryConvertPressureToRaid(state, faction, now);
            }
        }

        private void EnsureStates(int now)
        {
            states ??= new List<WraithStrategicHungerState>();
            EnsureState(WraithLineageUtility.SableBroodDefName, 0.32f, now);
            EnsureState(WraithLineageUtility.CinderCourtDefName, 0.24f, now);
            EnsureState(WraithLineageUtility.VeiledHiveDefName, 0.18f, now);
            EnsureState(WraithLineageUtility.PaleCovenantDefName, 0.12f, now);
        }

        private void EnsureState(string defName, float initialHunger, int now)
        {
            if (states.Any(s => s.factionDefName == defName))
                return;

            states.Add(new WraithStrategicHungerState(defName, initialHunger)
            {
                nextRequestTick = now + Rand.RangeInclusive(120000, 240000),
                nextAggressionTick = now + AggressionCooldownTicks
            });
        }

        private static Map BestPlayerHome()
        {
            return Find.Maps
                .Where(m => m != null && m.IsPlayerHome)
                .OrderByDescending(m => m.PlayerWealthForStoryteller)
                .FirstOrDefault();
        }

        private static IEnumerable<Pawn> EligibleStrategicFeedingSubjects(Map map)
        {
            if (map == null)
                return Enumerable.Empty<Pawn>();

            HediffDef lifeDrained = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_LifeDrained");
            return map.mapPawns.AllPawnsSpawned.Where(p =>
                p != null
                && !p.Dead
                && p.Spawned
                && p.guest?.IsPrisoner == true
                && p.RaceProps != null
                && p.RaceProps.Humanlike
                && p.RaceProps.IsFlesh
                && !p.RaceProps.IsMechanoid
                && p.genes?.Xenotype?.defName != "WNG_Wraith"
                && !HoffanSerumUtility.HasProtection(p)
                && (lifeDrained == null || p.health?.hediffSet?.HasHediff(lifeDrained) != true));
        }

        private void BeginRequest(WraithStrategicHungerState state, Faction faction, List<Pawn> candidates, int now)
        {
            if (state == null || faction == null || candidates.NullOrEmpty())
                return;

            WraithHungerPolicy policy = WraithHungerPolicy.For(state.factionDefName);
            int requestedCount = Mathf.Clamp(1 + Mathf.FloorToInt((state.hunger - policy.requestThreshold) * 8f), 1, 3);
            requestedCount = Math.Min(requestedCount, candidates.Count);

            state.pendingSubjects.Clear();
            state.pendingSubjects.AddRange(candidates
                .OrderBy(p => p.LabelShort)
                .ThenBy(p => p.thingIDNumber)
                .Take(requestedCount));

            if (!CreateRequestFeeders(state, faction, requestedCount))
            {
                state.pendingSubjects.Clear();
                state.nextRequestTick = now + RequestRetryTicks;
                return;
            }

            state.requestStage = WraithFeedingRequestStage.Subjects;
            OpenSubjectsStage(state, faction);
        }

        private static bool CreateRequestFeeders(WraithStrategicHungerState state, Faction faction, int count)
        {
            state.pendingFeeders.Clear();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    string kindName = i == count - 1 && count >= 3 ? "WNG_WraithKeeper" : (i % 2 == 0 ? "WNG_WraithHunter" : "WNG_WraithWarrior");
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName);
                    if (kind == null)
                        throw new InvalidOperationException("Missing strategic feeding PawnKind " + kindName + ".");

                    Pawn feeder = PawnGenerator.GeneratePawn(kind, faction);
                    state.pendingFeeders.Add(feeder);

                    // These are the exact hungry Wraiths assigned to the request, not decorative names.
                    Gene_Resource_LifeForce lifeForce = feeder.genes?.GetFirstGeneOfType<Gene_Resource_LifeForce>();
                    if (lifeForce != null)
                        lifeForce.Value = Mathf.Clamp(0.10f + (1f - state.hunger) * 0.25f, 0.05f, 0.35f);

                    Find.WorldPawns.PassToWorld(feeder, PawnDiscardDecideMode.KeepForever);
                }
                return state.pendingFeeders.Count == count;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Failed to create strategic Wraith feeding participants: " + ex);
                CleanupRequestFeeders(state);
                return false;
            }
        }

        private void ReopenPendingRequest(WraithStrategicHungerState state)
        {
            Faction faction = WraithLineageUtility.Resolve(state.factionDefName);
            if (faction == null || !PendingRequestStillValid(state))
            {
                ResolveRefusal(state, faction, "The feeding arrangement could no longer be completed.");
                return;
            }

            if (state.requestStage == WraithFeedingRequestStage.Subjects)
                OpenSubjectsStage(state, faction);
            else if (state.requestStage == WraithFeedingRequestStage.Participants)
                OpenParticipantsStage(state, faction);
        }

        private static bool PendingRequestStillValid(WraithStrategicHungerState state)
        {
            if (state?.pendingSubjects.NullOrEmpty() != false || state.pendingFeeders.NullOrEmpty())
                return false;
            if (state.pendingSubjects.Count != state.pendingFeeders.Count)
                return false;
            return state.pendingSubjects.All(p => p != null && !p.Dead && p.Spawned && p.guest?.IsPrisoner == true && !HoffanSerumUtility.HasProtection(p))
                && state.pendingFeeders.All(p => p != null && !p.Dead && p.IsWorldPawn());
        }

        private void OpenSubjectsStage(WraithStrategicHungerState state, Faction faction)
        {
            if (!PendingRequestStillValid(state))
            {
                ResolveRefusal(state, faction, "The requested feeding stock is no longer available.");
                return;
            }

            requestWindowOpen = true;
            string subjects = string.Join("\n", state.pendingSubjects.Select(p => "  • " + p.LabelShortCap));
            string text = faction.Name + " is experiencing genuine strategic feeding pressure. The Hive requests controlled access to the following prisoner feeding stock:\n\n"
                + subjects
                + "\n\nThis request comes from faction-level hunger. Ordinary Wraith Drain Life and local Hive feeding do not create this negotiation.";

            Action review = () =>
            {
                requestWindowOpen = false;
                state.requestStage = WraithFeedingRequestStage.Participants;
                OpenParticipantsStage(state, faction);
            };
            Action refuse = () =>
            {
                requestWindowOpen = false;
                ResolveRefusal(state, faction, "You refused access to the requested feeding stock.");
            };

            Find.WindowStack.Add(new Dialog_MessageBox(
                text,
                "Review involved Wraiths",
                review,
                "Refuse",
                refuse,
                faction.Name + " — feeding request",
                buttonADestructive: false,
                acceptAction: review,
                cancelAction: refuse));
        }

        private void OpenParticipantsStage(WraithStrategicHungerState state, Faction faction)
        {
            if (!PendingRequestStillValid(state))
            {
                ResolveRefusal(state, faction, "The feeding arrangement could no longer be completed.");
                return;
            }

            requestWindowOpen = true;
            string feeders = string.Join("\n", state.pendingFeeders.Select(p => "  • " + p.LabelShortCap + " — " + p.kindDef.LabelCap));
            string subjects = string.Join(", ", state.pendingSubjects.Select(p => p.LabelShortCap));
            string text = "The exact Wraiths assigned to this feeding are:\n\n"
                + feeders
                + "\n\nThey will conduct a controlled partial feeding on: " + subjects + ". Each prisoner will age by "
                + ControlledFeedVictimAgeYears.ToString("0") + " biological years and receive the temporary Life Drained condition.";

            Action accept = () =>
            {
                requestWindowOpen = false;
                ResolveAcceptedFeeding(state, faction);
            };
            Action refuse = () =>
            {
                requestWindowOpen = false;
                ResolveRefusal(state, faction, "You refused the named Wraith feeding party.");
            };

            Find.WindowStack.Add(new Dialog_MessageBox(
                text,
                "Allow controlled feeding",
                accept,
                "Refuse",
                refuse,
                faction.Name + " — assigned feeders",
                buttonADestructive: false,
                acceptAction: accept,
                cancelAction: refuse));
        }

        private void ResolveAcceptedFeeding(WraithStrategicHungerState state, Faction faction)
        {
            if (!PendingRequestStillValid(state))
            {
                ResolveRefusal(state, faction, "The feeding arrangement failed before it could be completed.");
                return;
            }

            HediffDef lifeDrainedDef = DefDatabase<HediffDef>.GetNamedSilentFail("WNG_LifeDrained");
            if (lifeDrainedDef == null
                || state.pendingSubjects.Any(p => p?.ageTracker == null || p.health?.hediffSet == null)
                || state.pendingFeeders.Any(p => p?.genes == null))
            {
                ResolveRefusal(state, faction, "The feeding arrangement failed its biological preflight checks.");
                return;
            }

            List<long> oldAges = state.pendingSubjects.Select(p => p.ageTracker.AgeBiologicalTicks).ToList();
            List<Gene_Resource_LifeForce> resources = state.pendingFeeders
                .Select(p => p.genes.GetFirstGeneOfType<Gene_Resource_LifeForce>())
                .ToList();
            List<float> oldLifeForce = resources.Select(g => g?.Value ?? 0f).ToList();
            Pawn messageTarget = state.pendingSubjects.FirstOrDefault();
            int subjectCount = state.pendingSubjects.Count;

            try
            {
                for (int i = 0; i < subjectCount; i++)
                {
                    Pawn subject = state.pendingSubjects[i];
                    AdjustBiologicalAge(subject, (long)ControlledFeedVictimAgeYears);
                    RefreshHediff(subject, "WNG_LifeDrained");
                    if (!subject.health.hediffSet.HasHediff(lifeDrainedDef))
                        throw new InvalidOperationException("Strategic feeding did not apply Life Drained to " + subject.LabelShortCap + ".");

                    resources[i]?.AddLifeForce(ControlledFeedLifeForceGain);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Strategic Wraith feeding failed before commit: " + ex);
                for (int i = 0; i < state.pendingSubjects.Count && i < oldAges.Count; i++)
                {
                    Pawn subject = state.pendingSubjects[i];
                    if (subject?.ageTracker != null)
                        subject.ageTracker.AgeBiologicalTicks = oldAges[i];
                    Hediff applied = subject?.health?.hediffSet?.GetFirstHediffOfDef(lifeDrainedDef);
                    if (applied != null)
                        subject.health.RemoveHediff(applied);
                }
                for (int i = 0; i < resources.Count && i < oldLifeForce.Count; i++)
                {
                    if (resources[i] != null)
                        resources[i].Value = oldLifeForce[i];
                }
                ResolveRefusal(state, faction, "The attempted feeding arrangement broke down before completion.");
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            state.hunger = Mathf.Clamp01(state.hunger - Math.Min(0.75f, 0.28f * subjectCount));
            state.raidPressure = Mathf.Max(0f, state.raidPressure - 0.25f * subjectCount);
            state.nextRequestTick = now + AcceptedRequestCooldownTicks;
            state.nextAggressionTick = Math.Max(state.nextAggressionTick, now + AggressionCooldownTicks);
            ClearPendingRequest(state);

            try
            {
                Messages.Message(
                    faction.Name + " completed the negotiated feeding. Its strategic hunger has eased.",
                    messageTarget,
                    MessageTypeDefOf.PositiveEvent,
                    historical: true);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Strategic feeding committed, but its success message failed: " + ex.Message);
            }
        }

        private void ResolveRefusal(WraithStrategicHungerState state, Faction faction, string reason)
        {
            if (state == null)
                return;

            WraithHungerPolicy policy = WraithHungerPolicy.For(state.factionDefName);
            state.raidPressure += policy.refusalPressure;
            state.hunger = Mathf.Clamp01(state.hunger + policy.refusalHungerIncrease);
            int now = Find.TickManager?.TicksGame ?? 0;
            state.nextRequestTick = now + RefusedRequestCooldownTicks;

            if (faction != null && Faction.OfPlayer != null && !faction.def.permanentEnemy && policy.refusalGoodwillPenalty > 0)
            {
                int delta = -policy.refusalGoodwillPenalty;
                if (faction.CanChangeGoodwillFor(Faction.OfPlayer, delta))
                    faction.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: true, canSendHostilityLetter: true);
            }

            if (faction != null)
            {
                Messages.Message(
                    reason + " " + faction.Name + "'s unresolved hunger is increasing attack pressure.",
                    MessageTypeDefOf.NegativeEvent,
                    historical: true);
            }

            ClearPendingRequest(state);
        }

        private static void ClearPendingRequest(WraithStrategicHungerState state)
        {
            CleanupRequestFeeders(state);
            state.pendingSubjects?.Clear();
            state.requestStage = WraithFeedingRequestStage.None;
        }

        private static void CleanupRequestFeeders(WraithStrategicHungerState state)
        {
            if (state?.pendingFeeders == null || Find.WorldPawns == null)
                return;

            foreach (Pawn pawn in state.pendingFeeders.ToList())
            {
                if (pawn != null && pawn.IsWorldPawn())
                    Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
            }
            state.pendingFeeders.Clear();
        }

        private static void TryConvertPressureToRaid(WraithStrategicHungerState state, Faction faction, int now)
        {
            if (state.raidPressure < 1f || now < state.nextAggressionTick || faction == null || Faction.OfPlayer == null)
                return;
            if (!faction.HostileTo(Faction.OfPlayer))
                return;

            Map home = BestPlayerHome();
            if (home == null)
                return;

            WraithHungerPolicy policy = WraithHungerPolicy.For(state.factionDefName);
            IncidentParms parms = new IncidentParms
            {
                forced = true,
                target = home,
                faction = faction,
                points = Mathf.Max(35f, StorytellerUtility.DefaultThreatPointsNow(home) * policy.raidPointsFactor),
                raidStrategy = RaidStrategyDefOf.ImmediateAttack
            };

            if (IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
            {
                state.raidPressure = Mathf.Max(0f, state.raidPressure - 1f);
                state.nextAggressionTick = now + AggressionCooldownTicks;
            }
        }

        private static void RefreshHediff(Pawn pawn, string defName)
        {
            if (pawn?.health?.hediffSet == null)
                return;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
            pawn.health.AddHediff(def);
        }

        private static void AdjustBiologicalAge(Pawn pawn, long years)
        {
            if (pawn?.ageTracker == null || years == 0L)
                return;
            const long ticksPerYear = 3600000L;
            long current = pawn.ageTracker.AgeBiologicalTicks;
            long next;
            try
            {
                checked
                {
                    next = current + years * ticksPerYear;
                }
            }
            catch (OverflowException)
            {
                next = years > 0L ? long.MaxValue : 0L;
            }
            pawn.ageTracker.AgeBiologicalTicks = Math.Max(0L, next);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref states, "wngWraithStrategicHungerStates", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                states ??= new List<WraithStrategicHungerState>();
                states.RemoveAll(s => s == null || s.factionDefName.NullOrEmpty());
                requestWindowOpen = false;
            }
        }
    }

    public readonly struct WraithHungerPolicy
    {
        public readonly float hungerPerDay;
        public readonly float requestThreshold;
        public readonly float unresolvedPressurePerDay;
        public readonly float refusalPressure;
        public readonly float refusalHungerIncrease;
        public readonly int refusalGoodwillPenalty;
        public readonly float raidPointsFactor;

        public WraithHungerPolicy(
            float hungerPerDay,
            float requestThreshold,
            float unresolvedPressurePerDay,
            float refusalPressure,
            float refusalHungerIncrease,
            int refusalGoodwillPenalty,
            float raidPointsFactor)
        {
            this.hungerPerDay = hungerPerDay;
            this.requestThreshold = requestThreshold;
            this.unresolvedPressurePerDay = unresolvedPressurePerDay;
            this.refusalPressure = refusalPressure;
            this.refusalHungerIncrease = refusalHungerIncrease;
            this.refusalGoodwillPenalty = refusalGoodwillPenalty;
            this.raidPointsFactor = raidPointsFactor;
        }

        public static WraithHungerPolicy For(string factionDefName)
        {
            switch (factionDefName)
            {
                case WraithLineageUtility.SableBroodDefName:
                    return new WraithHungerPolicy(0.050f, 0.70f, 0.12f, 0.45f, 0.08f, 0, 1.10f);
                case WraithLineageUtility.CinderCourtDefName:
                    return new WraithHungerPolicy(0.045f, 0.74f, 0.10f, 0.35f, 0.07f, 20, 1.00f);
                case WraithLineageUtility.VeiledHiveDefName:
                    return new WraithHungerPolicy(0.035f, 0.80f, 0.06f, 0.20f, 0.05f, 12, 0.90f);
                case WraithLineageUtility.PaleCovenantDefName:
                    return new WraithHungerPolicy(0.030f, 0.86f, 0.04f, 0.12f, 0.04f, 6, 0.80f);
                default:
                    return new WraithHungerPolicy(0.040f, 0.78f, 0.08f, 0.25f, 0.05f, 10, 0.95f);
            }
        }
    }
}
