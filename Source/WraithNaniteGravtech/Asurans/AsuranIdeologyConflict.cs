using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public enum AsuranIdeologicalStance
    {
        Unassigned = 0,
        CollectiveLoyalist = 1,
        Reconstructionist = 2,
        Individualist = 3
    }

    public static class AsuranIdeologyUtility
    {
        private const string LoyalistDefName = "WNG_AsuranCollectiveLoyalist";
        private const string ReconstructionistDefName = "WNG_AsuranReconstructionist";
        private const string IndividualistDefName = "WNG_AsuranIndividualist";
        private const string AutonomyBreakDefName = "WNG_AsuranAutonomyBreak";
        private const string ConsensusDistressDefName = "WNG_AsuranConsensusDistress";

        public static AsuranIdeologicalStance StanceOf(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return AsuranIdeologicalStance.Unassigned;
            if (Has(pawn, LoyalistDefName)) return AsuranIdeologicalStance.CollectiveLoyalist;
            if (Has(pawn, ReconstructionistDefName)) return AsuranIdeologicalStance.Reconstructionist;
            if (Has(pawn, IndividualistDefName)) return AsuranIdeologicalStance.Individualist;
            return AsuranIdeologicalStance.Unassigned;
        }

        public static void EnsureStance(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Dead ||
                pawn.health?.hediffSet == null ||
                !AsuranCollectiveUtility.IsNaniteSynthetic(pawn) ||
                !AsuranCollectiveUtility.IsLinked(pawn) ||
                StanceOf(pawn) != AsuranIdeologicalStance.Unassigned)
            {
                return;
            }

            RestoreStance(pawn, ChooseInitialStance(pawn));
        }

        public static void RestoreStance(Pawn pawn, AsuranIdeologicalStance stance)
        {
            if (pawn?.health?.hediffSet == null || stance == AsuranIdeologicalStance.Unassigned)
                return;

            string wanted = DefNameFor(stance);
            foreach (string defName in new[] { LoyalistDefName, ReconstructionistDefName, IndividualistDefName })
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                Hediff existing = def == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing != null && defName != wanted)
                    pawn.health.RemoveHediff(existing);
            }

            HediffDef wantedDef = DefDatabase<HediffDef>.GetNamedSilentFail(wanted);
            if (wantedDef != null && pawn.health.hediffSet.GetFirstHediffOfDef(wantedDef) == null)
                pawn.health.AddHediff(wantedDef);
        }

        public static int EffectiveReconstructionPeers(Pawn pawn, int physicalPeers)
        {
            return Has(pawn, AutonomyBreakDefName) ? 0 : Math.Max(0, physicalPeers);
        }

        public static float CollectiveStateSeverity(Pawn pawn, int physicalPeers)
        {
            int peers = Math.Max(0, Math.Min(4, EffectiveReconstructionPeers(pawn, physicalPeers)));
            switch (StanceOf(pawn))
            {
                case AsuranIdeologicalStance.CollectiveLoyalist:
                    return Math.Min(1f, peers * 0.30f);
                case AsuranIdeologicalStance.Reconstructionist:
                    return Math.Min(1f, peers * 0.22f);
                case AsuranIdeologicalStance.Individualist:
                    return Math.Min(1f, peers * 0.12f);
                default:
                    return Math.Min(1f, peers * 0.25f);
            }
        }

        public static float ReconstructionMultiplier(Pawn pawn, int physicalPeers, float fallbackPerPeer)
        {
            int peers = Math.Max(0, Math.Min(4, EffectiveReconstructionPeers(pawn, physicalPeers)));
            float result;
            switch (StanceOf(pawn))
            {
                case AsuranIdeologicalStance.CollectiveLoyalist:
                    result = 0.90f + peers * 0.18f;
                    break;
                case AsuranIdeologicalStance.Reconstructionist:
                    result = 1.15f + peers * 0.10f;
                    break;
                case AsuranIdeologicalStance.Individualist:
                    result = 1.08f + peers * 0.05f;
                    break;
                default:
                    result = 1f + peers * Math.Max(0f, fallbackPerPeer);
                    break;
            }

            if (Has(pawn, ConsensusDistressDefName))
                result *= 0.80f;

            return Math.Max(0.10f, result);
        }

        private static AsuranIdeologicalStance ChooseInitialStance(Pawn pawn)
        {
            uint hash = unchecked((uint)(pawn.thingIDNumber * 1103515245 + 12345));
            int roll = (int)(hash % 100u);
            string faction = pawn.Faction?.def?.defName;

            if (faction == "WNG_PrecursorCollective")
            {
                if (roll < 65) return AsuranIdeologicalStance.CollectiveLoyalist;
                if (roll < 90) return AsuranIdeologicalStance.Reconstructionist;
                return AsuranIdeologicalStance.Individualist;
            }

            if (faction == "WNG_HumanFormEnclave")
            {
                if (roll < 15) return AsuranIdeologicalStance.CollectiveLoyalist;
                if (roll < 50) return AsuranIdeologicalStance.Reconstructionist;
                return AsuranIdeologicalStance.Individualist;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                if (roll < 20) return AsuranIdeologicalStance.CollectiveLoyalist;
                if (roll < 60) return AsuranIdeologicalStance.Reconstructionist;
                return AsuranIdeologicalStance.Individualist;
            }

            if (roll < 34) return AsuranIdeologicalStance.CollectiveLoyalist;
            if (roll < 67) return AsuranIdeologicalStance.Reconstructionist;
            return AsuranIdeologicalStance.Individualist;
        }

        private static bool Has(Pawn pawn, string defName)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            return def != null && pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) != null;
        }

        private static string DefNameFor(AsuranIdeologicalStance stance)
        {
            switch (stance)
            {
                case AsuranIdeologicalStance.CollectiveLoyalist: return LoyalistDefName;
                case AsuranIdeologicalStance.Reconstructionist: return ReconstructionistDefName;
                case AsuranIdeologicalStance.Individualist: return IndividualistDefName;
                default: return ReconstructionistDefName;
            }
        }
    }

    /// <summary>
    /// Restores persistent Asuran ideological identity and the later conflict consequences without
    /// changing faction allegiance. Individualists resist dense consensus; loyalists suffer prolonged
    /// isolation; reconstructionists mediate nearby individualist pressure.
    /// </summary>
    public sealed class GameComponent_AsuranIdeologyConflict : GameComponent
    {
        private const int RefreshIntervalTicks = 600;
        private const int IndividualistDensePeerThreshold = 3;
        private const int IndividualistPressureThresholdTicks = 60000;
        private const int LoyalistIsolationThresholdTicks = 90000;
        private const int PressureDecayPerRefresh = 1200;
        private const float MediatorRadius = 35f;

        private int nextTick;
        private Dictionary<int, int> pressureByPawn = new Dictionary<int, int>();
        private List<int> autonomyAnnouncedPawnIds = new List<int>();
        private List<int> distressAnnouncedPawnIds = new List<int>();

        public GameComponent_AsuranIdeologyConflict(Game game) { }

        public override void GameComponentTick()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextTick)
                return;
            nextTick = SafeFutureTick(now, RefreshIntervalTicks);

            HashSet<int> active = new HashSet<int>();
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns == null)
                    continue;

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (!Eligible(pawn))
                        continue;

                    active.Add(pawn.thingIDNumber);
                    AsuranIdeologyUtility.EnsureStance(pawn);
                    TickConflict(pawn);
                }
            }

            if (pressureByPawn.Count > 4096)
            {
                foreach (int id in pressureByPawn.Keys.Where(id => !active.Contains(id)).Take(2048).ToList())
                    pressureByPawn.Remove(id);
            }
        }

        private static bool Eligible(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   pawn.Spawned &&
                   pawn.health != null &&
                   AsuranCollectiveUtility.IsNaniteSynthetic(pawn) &&
                   AsuranCollectiveUtility.IsLinked(pawn);
        }

        private void TickConflict(Pawn pawn)
        {
            if (HasHediff(pawn, "WNG_AsuranAutonomyBreak") || HasHediff(pawn, "WNG_AsuranConsensusDistress"))
            {
                SetPressure(pawn, 0);
                return;
            }

            int pressure = Pressure(pawn);
            if (AsuranCollectiveUtility.IsDisrupted(pawn))
            {
                SetPressure(pawn, Math.Max(0, pressure - PressureDecayPerRefresh));
                return;
            }

            int peers = AsuranCollectiveUtility.CountLocalNetworkPeers(pawn);
            switch (AsuranIdeologyUtility.StanceOf(pawn))
            {
                case AsuranIdeologicalStance.Individualist:
                    if (peers >= IndividualistDensePeerThreshold)
                    {
                        int gain = RefreshIntervalTicks;
                        if (CountNearbyReconstructionists(pawn) > 0)
                            gain /= 2;
                        pressure += gain;
                        SetPressure(pawn, pressure);
                        if (pressure >= IndividualistPressureThresholdTicks)
                            Trigger(pawn, "WNG_AsuranAutonomyBreak", autonomyAnnouncedPawnIds,
                                pawn.LabelShort + " has rejected dense collective consensus for one day. Their individualist identity remains unchanged, but active reconstruction assistance from the lattice is refused until the episode passes.");
                    }
                    else
                    {
                        SetPressure(pawn, Math.Max(0, pressure - PressureDecayPerRefresh));
                    }
                    break;

                case AsuranIdeologicalStance.CollectiveLoyalist:
                    if (peers == 0)
                    {
                        pressure += RefreshIntervalTicks;
                        SetPressure(pawn, pressure);
                        if (pressure >= LoyalistIsolationThresholdTicks)
                            Trigger(pawn, "WNG_AsuranConsensusDistress", distressAnnouncedPawnIds,
                                pawn.LabelShort + " has suffered consensus deprivation after prolonged isolation. Reconstruction efficiency and learning are reduced until the episode passes.");
                    }
                    else
                    {
                        SetPressure(pawn, Math.Max(0, pressure - PressureDecayPerRefresh));
                    }
                    break;

                default:
                    SetPressure(pawn, Math.Max(0, pressure - PressureDecayPerRefresh));
                    break;
            }
        }

        private static int CountNearbyReconstructionists(Pawn pawn)
        {
            if (pawn?.Map == null || pawn.Faction == null)
                return 0;

            bool relayed = AsuranCollectiveUtility.HasPoweredArchive(pawn.Map, pawn.Faction);
            float radiusSq = MediatorRadius * MediatorRadius;
            int count = 0;

            foreach (Pawn other in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == null ||
                    other == pawn ||
                    other.Dead ||
                    other.Faction != pawn.Faction ||
                    !AsuranCollectiveUtility.IsLinked(other) ||
                    AsuranCollectiveUtility.IsDisrupted(other) ||
                    AsuranIdeologyUtility.StanceOf(other) != AsuranIdeologicalStance.Reconstructionist)
                {
                    continue;
                }

                if (!relayed && other.Position.DistanceToSquared(pawn.Position) > radiusSq)
                    continue;

                count++;
            }

            return count;
        }

        private void Trigger(Pawn pawn, string hediffDefName, List<int> announced, string message)
        {
            SetPressure(pawn, 0);

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
            if (def != null && pawn.health?.hediffSet?.GetFirstHediffOfDef(def) == null)
                pawn.health.AddHediff(def);

            try
            {
                if (pawn.Map != null)
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_NaniteNeuralInterface")
                        ?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Asuran ideology conflict committed but audio failed: " + ex.Message);
            }

            if (pawn.Faction != Faction.OfPlayer || announced.Contains(pawn.thingIDNumber))
                return;

            try
            {
                Messages.Message(message, pawn, MessageTypeDefOf.NegativeEvent, historical: false);
                announced.Add(pawn.thingIDNumber);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Asuran ideology conflict committed but player message failed: " + ex.Message);
            }
        }

        private static bool HasHediff(Pawn pawn, string defName)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            return def != null && pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) != null;
        }

        private int Pressure(Pawn pawn)
        {
            return pressureByPawn.TryGetValue(pawn.thingIDNumber, out int value) ? Math.Max(0, value) : 0;
        }

        private void SetPressure(Pawn pawn, int value)
        {
            if (value <= 0)
                pressureByPawn.Remove(pawn.thingIDNumber);
            else
                pressureByPawn[pawn.thingIDNumber] = value;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextTick, "wngAsuranIdeologyConflictNextTick", 0);
            Scribe_Collections.Look(ref pressureByPawn, "wngAsuranIdeologyConflictPressure", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref autonomyAnnouncedPawnIds, "wngAsuranAutonomyAnnouncements", LookMode.Value);
            Scribe_Collections.Look(ref distressAnnouncedPawnIds, "wngAsuranDistressAnnouncements", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pressureByPawn = pressureByPawn ?? new Dictionary<int, int>();
                autonomyAnnouncedPawnIds = (autonomyAnnouncedPawnIds ?? new List<int>()).Where(x => x >= 0).Distinct().ToList();
                distressAnnouncedPawnIds = (distressAnnouncedPawnIds ?? new List<int>()).Where(x => x >= 0).Distinct().ToList();
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(1, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }
    }
}
