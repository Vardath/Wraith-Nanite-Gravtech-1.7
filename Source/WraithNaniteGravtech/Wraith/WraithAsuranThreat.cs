using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class WraithAsuranThreatUtility
    {
        private static readonly HashSet<string> PredatoryLineageDefNames =
            new HashSet<string>
            {
                WraithLineageUtility.SableBroodDefName,
                WraithLineageUtility.CinderCourtDefName
            };

        public static bool IsNaniteSynthetic(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   AsuranCollectiveUtility.IsNaniteSynthetic(pawn);
        }

        public static int CountPlayerNaniteHumanoids(Map map)
        {
            if (map == null || Faction.OfPlayer == null)
                return 0;

            return map.mapPawns.AllPawnsSpawned.Count(pawn =>
                pawn != null &&
                !pawn.Dead &&
                pawn.Spawned &&
                pawn.Faction == Faction.OfPlayer &&
                pawn.RaceProps?.Humanlike == true &&
                IsNaniteSynthetic(pawn));
        }

        public static bool IsPredatoryAntiAsuranLineage(Faction faction)
        {
            return faction?.def != null &&
                   !faction.defeated &&
                   PredatoryLineageDefNames.Contains(faction.def.defName);
        }

        public static List<Faction> HostilePredatoryLineages()
        {
            if (Faction.OfPlayer == null)
                return new List<Faction>();

            return WraithLineageUtility.ActiveLineages()
                .Where(IsPredatoryAntiAsuranLineage)
                .Where(faction => faction.HostileTo(Faction.OfPlayer))
                .OrderBy(faction => faction.loadID)
                .ToList();
        }

        public static Faction ChooseRespondingLineage()
        {
            List<Faction> candidates = HostilePredatoryLineages();
            return candidates.Count == 0
                ? null
                : candidates.RandomElement();
        }
    }

    /// <summary>
    /// Strategic Wraith recognition of an established Asuran/human-form nanite population.
    ///
    /// Sable Brood and Cinder Court preserve the historical predatory doctrine. Veiled Hive and
    /// Pale Covenant are deliberately outside this automatic extermination response. The chosen
    /// responding lineage remains ordinary RimWorld faction state: peace, defeat or loss of the
    /// synthetic population cancels/reset the response rather than WNG forcing hostility.
    /// </summary>
    public sealed class WraithAsuranThreatRegistry : GameComponent
    {
        private const int CheckIntervalTicks = 1200;
        private const int RetryDelayTicks = 60000;
        private const int InitialStrikeMinTicks = 90000;
        private const int InitialStrikeMaxTicks = 180000;
        private const int RepeatStrikeMinTicks = 360000;
        private const int RepeatStrikeMaxTicks = 600000;
        private const float MaximumPressureBonus = 0.65f;

        private int nextStrikeTick = -1;
        private bool warningIssued;
        private Faction respondingLineage;

        public WraithAsuranThreatRegistry(Game game) { }

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue
                ? int.MaxValue
                : (int)value;
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null ||
                Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            Map target = Find.Maps
                .Where(map => map != null && map.IsPlayerHome)
                .Select(map => new
                {
                    Map = map,
                    Count = WraithAsuranThreatUtility.CountPlayerNaniteHumanoids(map)
                })
                .Where(pair => pair.Count > 0)
                .OrderByDescending(pair => pair.Count)
                .ThenBy(pair => pair.Map.uniqueID)
                .Select(pair => pair.Map)
                .FirstOrDefault();

            if (target == null)
            {
                ResetResponse();
                return;
            }

            if (!ResponderStillValid())
            {
                respondingLineage =
                    WraithAsuranThreatUtility.ChooseRespondingLineage();
                nextStrikeTick = -1;
                warningIssued = false;
            }

            if (respondingLineage == null)
                return;

            if (!warningIssued)
            {
                try
                {
                    Find.LetterStack.ReceiveLetter(
                        "Wraith pattern recognition",
                        "Long-range activity from " + respondingLineage.Name +
                        " has changed since synthetic nanite humanoids became established in your colony. " +
                        "This predatory Wraith lineage is no longer treating the settlement as ordinary feeding territory. " +
                        "It has classified the Asuran/human-form population as a self-replicating existential threat and is preparing direct combat action rather than a culling raid.",
                        LetterDefOf.ThreatSmall,
                        new TargetInfo(target.Center, target));
                    warningIssued = true;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Wraith anti-Asuran recognition committed but warning presentation failed; retrying later: " +
                        ex.Message);
                    return;
                }
            }

            if (nextStrikeTick < 0)
            {
                nextStrikeTick = SafeFutureTick(
                    now,
                    Rand.RangeInclusive(
                        InitialStrikeMinTicks,
                        InitialStrikeMaxTicks));
                return;
            }

            if (now < nextStrikeTick)
                return;

            IncidentDef incident =
                DefDatabase<IncidentDef>.GetNamedSilentFail(
                    "WNG_WraithAsuranExtermination");
            if (incident == null)
            {
                nextStrikeTick =
                    SafeFutureTick(now, RetryDelayTicks);
                return;
            }

            IncidentParms parms =
                StorytellerUtility.DefaultParmsNow(
                    IncidentCategoryDefOf.ThreatBig,
                    target);
            int syntheticCount =
                WraithAsuranThreatUtility.CountPlayerNaniteHumanoids(target);
            float pressureFactor =
                1f + Math.Min(
                    MaximumPressureBonus,
                    Math.Max(1, syntheticCount) * 0.10f);

            parms.points =
                Math.Max(
                    parms.points,
                    StorytellerUtility.DefaultThreatPointsNow(target)) *
                pressureFactor;
            parms.faction = respondingLineage;
            parms.forced = true;

            bool launched = false;
            try
            {
                launched = incident.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Wraith anti-Asuran extermination execution failed safely: " +
                    ex.Message);
            }

            nextStrikeTick = SafeFutureTick(
                now,
                launched
                    ? Rand.RangeInclusive(
                        RepeatStrikeMinTicks,
                        RepeatStrikeMaxTicks)
                    : RetryDelayTicks);
        }

        private bool ResponderStillValid()
        {
            return respondingLineage != null &&
                   !respondingLineage.defeated &&
                   WraithAsuranThreatUtility
                       .IsPredatoryAntiAsuranLineage(respondingLineage) &&
                   Faction.OfPlayer != null &&
                   respondingLineage.HostileTo(Faction.OfPlayer);
        }

        private void ResetResponse()
        {
            nextStrikeTick = -1;
            warningIssued = false;
            respondingLineage = null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref nextStrikeTick,
                "wngAsuranThreatNextStrikeTick",
                -1);
            Scribe_Values.Look(
                ref warningIssued,
                "wngAsuranThreatWarningIssued",
                false);
            Scribe_References.Look(
                ref respondingLineage,
                "wngAsuranThreatRespondingLineage");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (nextStrikeTick < -1)
                    nextStrikeTick = -1;

                if (respondingLineage != null &&
                    !WraithAsuranThreatUtility
                        .IsPredatoryAntiAsuranLineage(respondingLineage))
                {
                    ResetResponse();
                }
            }
        }
    }

    public sealed class IncidentWorker_WraithAsuranExtermination :
        IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            Faction lineage = ResolveRespondingLineage(parms);

            return map != null &&
                   map.IsPlayerHome &&
                   lineage != null &&
                   !lineage.defeated &&
                   Faction.OfPlayer != null &&
                   lineage.HostileTo(Faction.OfPlayer) &&
                   WraithAsuranThreatUtility
                       .CountPlayerNaniteHumanoids(map) > 0 &&
                   base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            Faction lineage = ResolveRespondingLineage(parms);
            if (map == null ||
                lineage == null ||
                lineage.defeated ||
                Faction.OfPlayer == null ||
                !lineage.HostileTo(Faction.OfPlayer) ||
                WraithAsuranThreatUtility
                    .CountPlayerNaniteHumanoids(map) <= 0)
            {
                return false;
            }

            parms.faction = lineage;
            parms.raidStrategy =
                RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = null;

            bool committed;
            try
            {
                committed =
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Wraith anti-Asuran combat raid failed before commit: " +
                    ex.Message);
                return false;
            }

            if (!committed)
                return false;

            // The native raid above remains the authoritative combat commit. Physical
            // carrier support is post-commit only: failure cannot make the registry retry the raid.
            try
            {
                WraithStrategicCarrierUtility.TryStageSupportCarrier(
                    map,
                    lineage,
                    parms.points > 0f
                        ? parms.points
                        : StorytellerUtility.DefaultThreatPointsNow(map),
                    cruiserThreshold: 2200f);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Wraith anti-Asuran raid committed but strategic carrier staging failed: " +
                    ex.Message);
            }

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Wraith extermination strike",
                    lineage.Name +
                    " has committed direct combat forces against the synthetic nanite population in your colony. " +
                    "This is not a feeding or culling raid: Sable/Cinder doctrine treats established Asuran and human-form Replicator bodies as a replication threat to be destroyed rather than livestock to be taken alive.",
                    LetterDefOf.ThreatBig,
                    new TargetInfo(map.Center, map));
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Wraith anti-Asuran raid committed but presentation failed: " +
                    ex.Message);
            }

            return true;
        }

        private static Faction ResolveRespondingLineage(IncidentParms parms)
        {
            Faction requested = parms?.faction;
            if (requested != null &&
                !requested.defeated &&
                WraithAsuranThreatUtility
                    .IsPredatoryAntiAsuranLineage(requested))
            {
                return requested;
            }

            return WraithAsuranThreatUtility
                .ChooseRespondingLineage();
        }
    }
}
