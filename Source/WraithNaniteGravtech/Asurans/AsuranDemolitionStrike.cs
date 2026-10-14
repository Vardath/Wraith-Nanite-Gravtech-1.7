using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    internal static class AsuranDemolitionStrikeUtility
    {
        public const string HostileFactionDefName = "WNG_PrecursorCollective";
        public const string PawnKindDefName = "WNG_PrecursorSoldier";
        public const string ChargeHediffDefName = "WNG_AsuranKamikazeCharge";
        public const int MinimumApproachTicks = 900;
        public const int NormalFuseMinTicks = 3600;
        public const int NormalFuseMaxTicks = 5400;
        public const int DownedFuseTicks = 180;
        public const int DeadFuseTicks = 90;
        public const int FinalWarningTicks = 600;
        public const float ObjectiveTriggerRadius = 2.6f;

        public static Faction HostileCollective()
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(HostileFactionDefName);
            return def == null ? null : Find.FactionManager?.FirstFactionOfDef(def);
        }
    }

    /// <summary>
    /// Visible telemetry for the exact synthetic operative whose own nanite lattice has been armed.
    /// The authoritative fuse/target state remains in the save-persistent map component.
    /// </summary>
    public sealed class Hediff_AsuranDemolitionLattice : Hediff
    {
        public override string LabelInBrackets
        {
            get
            {
                MapComponent_AsuranDemolitionStrike mission =
                    pawn?.MapHeld?.GetComponent<MapComponent_AsuranDemolitionStrike>();
                return mission != null && mission.IsOperative(pawn)
                    ? mission.CountdownText + " / " + mission.BlastClass
                    : base.LabelInBrackets;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                MapComponent_AsuranDemolitionStrike mission =
                    pawn?.MapHeld?.GetComponent<MapComponent_AsuranDemolitionStrike>();
                if (mission == null || !mission.IsOperative(pawn))
                    return base.TipStringExtra;

                return "Detonation: " + mission.CountdownText +
                       "\nBlast: " + mission.BlastClass +
                       " (" + mission.BlastRadius.ToString("0.0") + " cells)" +
                       "\nObjective: " + mission.ObjectiveSummary +
                       "\n\nThe operative's own nanite body is the armed demolition lattice. " +
                       "Reaching the objective can trigger detonation after the approach interlock. " +
                       "Downing or killing the operative engages a much shorter dead-man fuse rather than disarming it.";
            }
        }
    }

    /// <summary>
    /// One exact hostile Asuran body is the weapon. Mission state persists across saves. The
    /// operative repeatedly receives a direct movement job toward high-value player technology or,
    /// when none is reachable, a perimeter fallback. Incapacitation accelerates the already-armed
    /// fuse rather than deleting/replacing the pawn or resolving damage abstractly.
    /// </summary>
    public sealed class MapComponent_AsuranDemolitionStrike : MapComponent
    {
        private Pawn operative;
        private Thing objective;
        private IntVec3 objectiveCell = IntVec3.Invalid;
        private IntVec3 lastKnownCell = IntVec3.Invalid;
        private int detonationTick = -1;
        private int approachInterlockUntilTick = -1;
        private int nextOrderTick;
        private float blastRadius;
        private int damageAmount;
        private string blastClass;
        private string objectiveReason;
        private bool deadmanFuse;
        private bool finalWarningPlayed;
        private bool detonated;

        public MapComponent_AsuranDemolitionStrike(Map map) : base(map) { }

        public bool HasActiveMission =>
            operative != null && !detonated && detonationTick >= 0;

        public float BlastRadius => blastRadius;
        public string BlastClass => blastClass ?? "unknown";
        public string ObjectiveSummary =>
            objective != null && !objective.Destroyed
                ? objective.LabelCap + (objectiveReason.NullOrEmpty() ? "" : " — " + objectiveReason)
                : objectiveCell.IsValid
                    ? (objectiveReason.NullOrEmpty() ? "colony interior" : objectiveReason)
                    : "timed demolition";

        public int TicksRemaining =>
            detonationTick < 0
                ? 0
                : Math.Max(0, detonationTick - (Find.TickManager?.TicksGame ?? 0));

        public string CountdownText =>
            Math.Ceiling(TicksRemaining / 60f).ToString("0") + "s";

        public bool IsOperative(Pawn pawn) =>
            pawn != null && pawn == operative && !detonated;

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        public bool TryBegin(
            Pawn exactOperative,
            int fuseTicks,
            float radius,
            int damage,
            string classLabel)
        {
            if (HasActiveMission ||
                exactOperative == null ||
                exactOperative.Dead ||
                !exactOperative.Spawned ||
                exactOperative.Map != map)
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            operative = exactOperative;
            blastRadius = Math.Max(4.5f, Math.Min(9f, radius));
            damageAmount = Math.Max(55, Math.Min(110, damage));
            blastClass = classLabel ?? "medium";
            detonationTick = SafeFutureTick(
                now,
                Math.Max(AsuranDemolitionStrikeUtility.NormalFuseMinTicks, fuseTicks));
            approachInterlockUntilTick = SafeFutureTick(
                now,
                AsuranDemolitionStrikeUtility.MinimumApproachTicks);
            nextOrderTick = now;
            lastKnownCell = exactOperative.Position;
            deadmanFuse = false;
            finalWarningPlayed = false;
            detonated = false;
            objective = null;
            objectiveCell = IntVec3.Invalid;
            objectiveReason = null;

            EnsureChargeHediff();
            SelectObjective();
            IssueRushOrder(now, force: true);
            return true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!HasActiveMission || Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now % 30 != 0)
                return;

            UpdateLastKnownCell();

            if (operative.Dead)
            {
                ArmDeadmanFuse(now, AsuranDemolitionStrikeUtility.DeadFuseTicks);
                if (now >= detonationTick)
                    Detonate();
                return;
            }

            if (operative.MapHeld != map)
            {
                ClearMission();
                return;
            }

            EnsureChargeHediff();

            if (operative.Downed)
            {
                ArmDeadmanFuse(now, AsuranDemolitionStrikeUtility.DownedFuseTicks);
                if (now >= detonationTick)
                    Detonate();
                return;
            }

            if (!finalWarningPlayed &&
                detonationTick - now <= AsuranDemolitionStrikeUtility.FinalWarningTicks)
            {
                finalWarningPlayed = true;
                TryWarningPresentation();
            }

            if (!ObjectiveStillValid())
                SelectObjective();

            if (now >= approachInterlockUntilTick && AtObjective())
            {
                Detonate();
                return;
            }

            if (now >= detonationTick)
            {
                Detonate();
                return;
            }

            IssueRushOrder(now, force: false);
        }

        private void UpdateLastKnownCell()
        {
            if (operative == null)
                return;

            if (operative.MapHeld == map)
            {
                lastKnownCell = operative.PositionHeld;
                return;
            }

            Corpse corpse = operative.Corpse;
            if (corpse != null && corpse.Spawned && corpse.Map == map)
                lastKnownCell = corpse.Position;
        }

        private void ArmDeadmanFuse(int now, int ticks)
        {
            int emergency = SafeFutureTick(now, Math.Max(30, ticks));
            if (!deadmanFuse || detonationTick > emergency)
                detonationTick = emergency;
            deadmanFuse = true;
            approachInterlockUntilTick = now;
        }

        private void EnsureChargeHediff()
        {
            if (operative?.health?.hediffSet == null || operative.Dead)
                return;

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(
                AsuranDemolitionStrikeUtility.ChargeHediffDefName);
            if (def != null && operative.health.hediffSet.GetFirstHediffOfDef(def) == null)
                operative.health.AddHediff(def);
        }

        private void SelectObjective()
        {
            objective = FindValuableReachableTarget();
            if (objective != null)
            {
                objectiveCell = objective.Position;
                return;
            }

            objective = FindPerimeterFallback();
            if (objective != null)
            {
                objectiveCell = objective.Position;
                objectiveReason = objective is Building_Door
                    ? "perimeter access point"
                    : "reachable perimeter structure";
                return;
            }

            objectiveCell = map.Center;
            objectiveReason = "colony interior fallback";
        }

        private Thing FindValuableReachableTarget()
        {
            if (operative == null ||
                operative.Dead ||
                operative.Downed ||
                !operative.Spawned)
                return null;

            Thing best = null;
            float bestScore = 0f;
            string bestReason = null;

            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing == null ||
                    thing.Destroyed ||
                    !thing.Spawned ||
                    thing == operative ||
                    thing is Pawn ||
                    thing is Corpse)
                    continue;

                float score;
                string reason;

                if (thing is Building building)
                {
                    if (building.Faction != Faction.OfPlayer ||
                        building is Building_Door)
                        continue;

                    score = TechnologyScore(building);
                    if (score < 250f)
                        continue;
                    reason = "high-value colony technology";
                }
                else
                {
                    if (thing.def?.category != ThingCategory.Item ||
                        !(map.zoneManager?.ZoneAt(thing.Position) is Zone_Stockpile))
                        continue;

                    score = StockpileScore(thing);
                    if (score < 350f)
                        continue;
                    reason = "valuable stockpile";
                }

                if (!operative.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
                    continue;

                if (best == null ||
                    score > bestScore ||
                    (Math.Abs(score - bestScore) < 0.01f &&
                     thing.Position.DistanceToSquared(operative.Position) <
                     best.Position.DistanceToSquared(operative.Position)))
                {
                    best = thing;
                    bestScore = score;
                    bestReason = reason;
                }
            }

            objectiveReason = bestReason;
            return best;
        }

        private static float TechnologyScore(Building building)
        {
            if (building == null)
                return 0f;

            float score = Math.Max(0f, building.MarketValue);
            if (building.TryGetComp<CompPowerTrader>() != null)
                score += 350f;
            if (building is Building_WorkTable)
                score += 250f;

            string name = (building.def?.defName ?? string.Empty).ToLowerInvariant();
            foreach (string token in new[]
            {
                "research", "comms", "reactor", "battery", "shield",
                "turret", "archive", "fabricator", "grav", "stargate",
                "console", "vacuum", "forge", "chamber", "heart"
            })
            {
                if (name.Contains(token))
                    score += 650f;
            }
            return score;
        }

        private static float StockpileScore(Thing thing)
        {
            if (thing == null)
                return 0f;

            float score = Math.Max(0f, thing.MarketValue) * Math.Max(1, thing.stackCount);
            string name = (thing.def?.defName ?? string.Empty).ToLowerInvariant();
            foreach (string token in new[]
            {
                "component", "advanced", "precursor", "ancient", "vacuum",
                "grav", "tech", "weapon", "armor", "nanite", "replicator",
                "biomass", "plasteel", "uranium"
            })
            {
                if (name.Contains(token))
                    score += 500f;
            }
            return score;
        }

        private Thing FindPerimeterFallback()
        {
            if (operative == null ||
                operative.Dead ||
                operative.Downed ||
                !operative.Spawned)
                return null;

            Thing door = map.listerThings.AllThings
                .OfType<Building_Door>()
                .Where(d =>
                    d != null &&
                    !d.Destroyed &&
                    d.Spawned &&
                    (d.Faction == Faction.OfPlayer ||
                     (map.areaManager?.Home != null && map.areaManager.Home[d.Position])))
                .Where(d => operative.CanReach(d, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(d => d.Position.DistanceToSquared(operative.Position))
                .FirstOrDefault();
            if (door != null)
                return door;

            return map.listerThings.AllThings
                .OfType<Building>()
                .Where(b =>
                    b != null &&
                    !b.Destroyed &&
                    b.Spawned &&
                    b.Faction == Faction.OfPlayer &&
                    b.def.passability == Traversability.Impassable)
                .Where(b => operative.CanReach(b, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(b => b.Position.DistanceToSquared(operative.Position))
                .FirstOrDefault();
        }

        private bool ObjectiveStillValid()
        {
            if (objective != null)
            {
                if (objective.Destroyed ||
                    !objective.Spawned ||
                    objective.Map != map)
                    return false;

                if (operative == null ||
                    operative.Dead ||
                    operative.Downed ||
                    !operative.Spawned)
                    return true;

                return operative.CanReach(
                    objective,
                    PathEndMode.Touch,
                    Danger.Deadly);
            }

            return objectiveCell.IsValid && objectiveCell.InBounds(map);
        }

        private bool AtObjective()
        {
            if (operative == null ||
                operative.Dead ||
                operative.Downed ||
                operative.MapHeld != map)
                return false;

            IntVec3 cell =
                objective != null && objective.Spawned
                    ? objective.Position
                    : objectiveCell;
            if (!cell.IsValid)
                return false;

            float radius = AsuranDemolitionStrikeUtility.ObjectiveTriggerRadius;
            return operative.Position.DistanceToSquared(cell) <= radius * radius;
        }

        private void IssueRushOrder(int now, bool force)
        {
            if (operative == null ||
                operative.Dead ||
                operative.Downed ||
                !operative.Spawned ||
                operative.jobs == null)
                return;

            if (!force && now < nextOrderTick)
                return;
            nextOrderTick = SafeFutureTick(now, 240);

            if (!ObjectiveStillValid())
                SelectObjective();

            Job job;
            if (objective != null)
                job = new Job(JobDefOf.Goto, objective);
            else if (objectiveCell.IsValid &&
                     operative.CanReach(
                         objectiveCell,
                         PathEndMode.OnCell,
                         Danger.Deadly))
                job = new Job(JobDefOf.Goto, objectiveCell);
            else
                return;

            job.expiryInterval = 420;
            job.checkOverrideOnExpire = true;
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            operative.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void TryWarningPresentation()
        {
            try
            {
                SoundDef warning =
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_NaniteCopyComplete");
                if (warning != null && operative?.MapHeld == map)
                    warning.PlayOneShot(
                        new TargetInfo(
                            operative.PositionHeld,
                            map));

                if (operative?.MapHeld == map)
                    Messages.Message(
                        "The armed Asuran demolition lattice is entering its final countdown.",
                        operative,
                        MessageTypeDefOf.ThreatSmall,
                        historical: false);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Asuran demolition warning presentation failed: " +
                    ex.Message);
            }
        }

        private void Detonate()
        {
            if (detonated || map == null)
                return;

            detonated = true;
            UpdateLastKnownCell();
            IntVec3 cell =
                lastKnownCell.IsValid && lastKnownCell.InBounds(map)
                    ? lastKnownCell
                    : map.Center;
            Thing instigator = operative;

            try
            {
                if (operative != null && !operative.Dead)
                    operative.Kill(null);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Asuran demolition operative death transition failed before blast: " +
                    ex.Message);
            }

            try
            {
                GenExplosion.DoExplosion(
                    cell,
                    map,
                    blastRadius,
                    DamageDefOf.Bomb,
                    instigator,
                    damAmount: damageAmount,
                    armorPenetration: damageAmount * 0.016f,
                    explosionSound: DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_Bomb"),
                    chanceToStartFire: 0.16f,
                    damageFalloff: true,
                    screenShakeFactor: blastRadius >= 7.5f ? 1.65f : 1.25f);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[WNG] Asuran demolition mission reached its committed detonation point but explosion presentation/damage failed: " +
                    ex);
            }

            ClearMission();
        }

        private void ClearMission()
        {
            if (operative?.health?.hediffSet != null && !operative.Dead)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(
                    AsuranDemolitionStrikeUtility.ChargeHediffDefName);
                Hediff marker =
                    def == null
                        ? null
                        : operative.health.hediffSet.GetFirstHediffOfDef(def);
                if (marker != null)
                    operative.health.RemoveHediff(marker);
            }

            operative = null;
            objective = null;
            objectiveCell = IntVec3.Invalid;
            lastKnownCell = IntVec3.Invalid;
            detonationTick = -1;
            approachInterlockUntilTick = -1;
            nextOrderTick = 0;
            blastRadius = 0f;
            damageAmount = 0;
            blastClass = null;
            objectiveReason = null;
            deadmanFuse = false;
            finalWarningPlayed = false;
            detonated = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(
                ref operative,
                "wngAsuranDemolitionOperative");
            Scribe_References.Look(
                ref objective,
                "wngAsuranDemolitionObjective");
            Scribe_Values.Look(
                ref objectiveCell,
                "wngAsuranDemolitionObjectiveCell",
                IntVec3.Invalid);
            Scribe_Values.Look(
                ref lastKnownCell,
                "wngAsuranDemolitionLastCell",
                IntVec3.Invalid);
            Scribe_Values.Look(
                ref detonationTick,
                "wngAsuranDemolitionDetonationTick",
                -1);
            Scribe_Values.Look(
                ref approachInterlockUntilTick,
                "wngAsuranDemolitionApproachInterlock",
                -1);
            Scribe_Values.Look(
                ref nextOrderTick,
                "wngAsuranDemolitionNextOrder",
                0);
            Scribe_Values.Look(
                ref blastRadius,
                "wngAsuranDemolitionBlastRadius",
                0f);
            Scribe_Values.Look(
                ref damageAmount,
                "wngAsuranDemolitionDamage",
                0);
            Scribe_Values.Look(
                ref blastClass,
                "wngAsuranDemolitionBlastClass");
            Scribe_Values.Look(
                ref objectiveReason,
                "wngAsuranDemolitionObjectiveReason");
            Scribe_Values.Look(
                ref deadmanFuse,
                "wngAsuranDemolitionDeadman",
                false);
            Scribe_Values.Look(
                ref finalWarningPlayed,
                "wngAsuranDemolitionWarningPlayed",
                false);
            Scribe_Values.Look(
                ref detonated,
                "wngAsuranDemolitionDetonated",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (operative == null ||
                    detonated ||
                    detonationTick < 0)
                {
                    ClearMission();
                    return;
                }

                blastRadius = Math.Max(4.5f, Math.Min(9f, blastRadius));
                damageAmount = Math.Max(55, Math.Min(110, damageAmount));
            }
        }
    }

    public sealed class IncidentWorker_AsuranDemolitionStrike : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !map.IsPlayerHome)
                return false;

            MapComponent_AsuranDemolitionStrike mission =
                map.GetComponent<MapComponent_AsuranDemolitionStrike>();
            if (mission?.HasActiveMission == true)
                return false;

            Faction faction = AsuranDemolitionStrikeUtility.HostileCollective();
            PawnKindDef kind =
                DefDatabase<PawnKindDef>.GetNamedSilentFail(
                    AsuranDemolitionStrikeUtility.PawnKindDefName);
            return faction != null &&
                   !faction.defeated &&
                   Faction.OfPlayer != null &&
                   faction.HostileTo(Faction.OfPlayer) &&
                   kind != null &&
                   base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null || !map.IsPlayerHome)
                return false;

            MapComponent_AsuranDemolitionStrike mission =
                map.GetComponent<MapComponent_AsuranDemolitionStrike>();
            if (mission == null || mission.HasActiveMission)
                return false;

            Faction faction = AsuranDemolitionStrikeUtility.HostileCollective();
            PawnKindDef kind =
                DefDatabase<PawnKindDef>.GetNamedSilentFail(
                    AsuranDemolitionStrikeUtility.PawnKindDefName);
            if (faction == null ||
                faction.defeated ||
                Faction.OfPlayer == null ||
                !faction.HostileTo(Faction.OfPlayer) ||
                kind == null)
                return false;

            Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
            if (pawn == null)
                return false;

            bool spawned = false;
            try
            {
                pawn.equipment?.DestroyAllEquipment();

                if (!RCellFinder.TryFindRandomPawnEntryCell(
                        out IntVec3 entry,
                        map,
                        0f))
                    entry = CellFinder.RandomEdgeCell(map);

                GenSpawn.Spawn(pawn, entry, map);
                spawned = pawn.Spawned && pawn.Map == map;
                if (!spawned)
                    throw new InvalidOperationException(
                        "Asuran demolition operative did not reach the map.");

                ChooseBlastClass(
                    out float radius,
                    out int damage,
                    out string blastClass);

                int fuse = Rand.RangeInclusive(
                    AsuranDemolitionStrikeUtility.NormalFuseMinTicks,
                    AsuranDemolitionStrikeUtility.NormalFuseMaxTicks);
                if (!mission.TryBegin(
                        pawn,
                        fuse,
                        radius,
                        damage,
                        blastClass))
                    throw new InvalidOperationException(
                        "Asuran demolition mission state could not commit.");

                try
                {
                    DefDatabase<SoundDef>
                        .GetNamedSilentFail("WNG_ReplicatorAssembly")
                        ?.PlayOneShot(
                            new TargetInfo(
                                pawn.Position,
                                map));
                }
                catch { }

                parms.faction = faction;
                Find.LetterStack.ReceiveLetter(
                    "Asuran demolition strike",
                    "A Lattice Collective synthetic operative has entered the map with its own nanite reconstruction lattice converted into an armed demolition charge. " +
                    "It is rushing valuable colony technology rather than fighting as an ordinary rifleman. " +
                    "Reaching its objective can trigger the blast early; downing or killing it engages a short dead-man fuse instead of safely disarming it.",
                    LetterDefOf.ThreatBig,
                    pawn);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Asuran demolition strike aborted before a safe mission commit: " +
                    ex.Message);
                if (spawned && pawn.Spawned)
                    pawn.DeSpawn(DestroyMode.Vanish);
                if (!pawn.Destroyed && pawn.ParentHolder == null)
                    pawn.Destroy(DestroyMode.Vanish);
                return false;
            }
        }

        private static void ChooseBlastClass(
            out float radius,
            out int damage,
            out string label)
        {
            float roll = Rand.Value;
            if (roll < 0.58f)
            {
                radius = Rand.Range(4.5f, 5.7f);
                damage = Rand.RangeInclusive(55, 72);
                label = "medium";
            }
            else if (roll < 0.90f)
            {
                radius = Rand.Range(5.8f, 7.3f);
                damage = Rand.RangeInclusive(73, 92);
                label = "large";
            }
            else
            {
                radius = Rand.Range(7.4f, 9f);
                damage = Rand.RangeInclusive(93, 110);
                label = "extra-large";
            }
        }
    }
}
