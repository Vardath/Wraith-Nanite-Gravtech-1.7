using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class WraithLeadershipRecord : IExposable
    {
        public Faction faction;
        public bool queenEstablished;
        public int queenLostTick = -1;
        public Pawn actingRegent;

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref queenEstablished, "queenEstablished", false);
            Scribe_Values.Look(ref queenLostTick, "queenLostTick", -1);
            Scribe_References.Look(ref actingRegent, "actingRegent");
        }
    }

    /// <summary>
    /// Local Wraith command continuity. Leadership is map- and faction-local: a true Queen grants
    /// the strongest coordination; losing a Queen that was actually established on this map causes
    /// one full day of disruption before a Keeper-preferred / Commander-fallback regency can form.
    /// A returning Queen supersedes any regent immediately. Losing a regent starts another full
    /// succession delay. Ordinary Wraith groups that never had a Queen here are not penalized.
    /// </summary>
    public sealed class MapComponent_WraithQueenLeadership : MapComponent
    {
        public const int CheckIntervalTicks = 250;
        public const int SuccessionDelayTicks = 60000;

        private const string QueenKindDefName = "WNG_WraithQueen";
        private const string KeeperKindDefName = "WNG_WraithKeeper";
        private const string CommanderKindDefName = "WNG_WraithCommander";
        private const string WraithXenotypeDefName = "WNG_Wraith";
        private const string LifeForceGeneDefName = "WNG_LifeForceMetabolism";

        private List<WraithLeadershipRecord> records =
            new List<WraithLeadershipRecord>();
        private int nextCheckTick;

        private HediffDef queenAuthorityDef;
        private HediffDef regentAuthorityDef;
        private HediffDef queenlessDisruptionDef;
        private HediffDef actingRegentDef;

        public MapComponent_WraithQueenLeadership(Map map) : base(map) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ResolveDefs();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            if (Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < nextCheckTick)
                return;
            nextCheckTick = SafeFutureTick(now, CheckIntervalTicks);

            ResolveDefs();
            if (queenAuthorityDef == null ||
                regentAuthorityDef == null ||
                queenlessDisruptionDef == null ||
                actingRegentDef == null)
            {
                return;
            }

            List<Pawn> wraiths = map.mapPawns.AllPawnsSpawned
                .Where(IsLivingWraith)
                .ToList();

            HashSet<Faction> activeFactions = new HashSet<Faction>(
                wraiths
                    .Where(pawn => pawn.Faction != null)
                    .Select(pawn => pawn.Faction));

            records ??= new List<WraithLeadershipRecord>();
            records.RemoveAll(record =>
                record == null ||
                record.faction == null ||
                (!activeFactions.Contains(record.faction) &&
                 !ValidRegent(record.actingRegent, record.faction)));

            foreach (Faction faction in activeFactions)
            {
                List<Pawn> factionWraiths = wraiths
                    .Where(pawn => pawn.Faction == faction)
                    .ToList();
                if (factionWraiths.Count == 0)
                    continue;

                WraithLeadershipRecord record = GetOrCreateRecord(faction);
                Pawn queen = factionWraiths
                    .Where(IsTrueQueen)
                    .OrderBy(pawn => pawn.thingIDNumber)
                    .FirstOrDefault();

                Pawn regent = ValidRegent(record.actingRegent, faction)
                    ? record.actingRegent
                    : null;

                if (queen != null)
                {
                    bool returningAfterLoss =
                        record.queenEstablished &&
                        record.queenLostTick >= 0;

                    if (regent != null)
                        RemoveHediff(regent, actingRegentDef);

                    record.queenEstablished = true;
                    record.queenLostTick = -1;
                    record.actingRegent = null;

                    ApplyLeadershipState(
                        factionWraiths,
                        WraithLeadershipState.TrueQueen);

                    if (returningAfterLoss)
                    {
                        Announce(
                            faction,
                            queen,
                            "A true Wraith Queen has re-established direct local hive coordination.",
                            playerPositive: faction == Faction.OfPlayer);
                    }
                    continue;
                }

                // No Queen has ever been established on this map for this faction. Hunting parties,
                // visiting groups and small detachments therefore retain their ordinary behavior.
                if (!record.queenEstablished)
                {
                    ClearLeadershipState(factionWraiths);
                    continue;
                }

                if (regent != null)
                {
                    ApplyLeadershipState(
                        factionWraiths,
                        WraithLeadershipState.Regent);
                    EnsureHediff(regent, actingRegentDef);
                    continue;
                }

                // A previously recorded regent has disappeared/died/changed faction. Re-enter the
                // full disruption window instead of promoting another pawn instantly.
                if (record.actingRegent != null)
                {
                    record.actingRegent = null;
                    record.queenLostTick = now;
                }

                if (record.queenLostTick < 0)
                {
                    record.queenLostTick = now;
                    Announce(
                        faction,
                        null,
                        "A Wraith Queen has been lost. Local hive coordination has fractured while succession remains unresolved.",
                        playerPositive: false);
                }

                ApplyLeadershipState(
                    factionWraiths,
                    WraithLeadershipState.Disrupted);

                if ((long)now - record.queenLostTick < SuccessionDelayTicks)
                    continue;

                Pawn successor = ChooseSuccessor(factionWraiths);
                if (successor == null)
                    continue;

                record.actingRegent = successor;
                EnsureHediff(successor, actingRegentDef);
                ApplyLeadershipState(
                    factionWraiths,
                    WraithLeadershipState.Regent);

                Announce(
                    faction,
                    successor,
                    successor.LabelShortCap +
                    " has assumed provisional Wraith regency. Local coordination is restored, but remains weaker than true Queen authority.",
                    playerPositive: faction == Faction.OfPlayer);
            }
        }

        private WraithLeadershipRecord GetOrCreateRecord(Faction faction)
        {
            WraithLeadershipRecord existing = records
                .FirstOrDefault(record => record?.faction == faction);
            if (existing != null)
                return existing;

            WraithLeadershipRecord created =
                new WraithLeadershipRecord { faction = faction };
            records.Add(created);
            return created;
        }

        private static Pawn ChooseSuccessor(IEnumerable<Pawn> pawns)
        {
            return pawns
                .Where(IsEligibleSuccessor)
                .OrderByDescending(pawn =>
                    pawn.kindDef?.defName == KeeperKindDefName)
                .ThenByDescending(SuccessionScore)
                .ThenBy(pawn => pawn.thingIDNumber)
                .FirstOrDefault();
        }

        private static int SuccessionScore(Pawn pawn)
        {
            int social = pawn?.skills == null
                ? 0
                : pawn.skills.GetSkill(SkillDefOf.Social).Level;
            int intellectual = pawn?.skills == null
                ? 0
                : pawn.skills.GetSkill(SkillDefOf.Intellectual).Level;
            return social * 3 + intellectual * 2;
        }

        private static bool IsEligibleSuccessor(Pawn pawn)
        {
            if (!IsLivingWraith(pawn) || pawn.Downed)
                return false;

            string kind = pawn.kindDef?.defName;
            return kind == KeeperKindDefName ||
                   kind == CommanderKindDefName;
        }

        private static bool IsTrueQueen(Pawn pawn)
        {
            return IsLivingWraith(pawn) &&
                   pawn.kindDef?.defName == QueenKindDefName;
        }

        private bool ValidRegent(Pawn pawn, Faction faction)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   pawn.Spawned &&
                   pawn.Map == map &&
                   pawn.Faction == faction &&
                   IsEligibleSuccessor(pawn);
        }

        private static bool IsLivingWraith(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Dead ||
                !pawn.Spawned ||
                pawn.genes == null)
            {
                return false;
            }

            if (pawn.genes.Xenotype?.defName == WraithXenotypeDefName)
                return true;

            return pawn.genes.GenesListForReading.Any(gene =>
                gene?.def?.defName == LifeForceGeneDefName &&
                gene.Active);
        }

        private void ApplyLeadershipState(
            IEnumerable<Pawn> pawns,
            WraithLeadershipState state)
        {
            foreach (Pawn pawn in pawns)
            {
                switch (state)
                {
                    case WraithLeadershipState.TrueQueen:
                        EnsureHediff(pawn, queenAuthorityDef);
                        RemoveHediff(pawn, regentAuthorityDef);
                        RemoveHediff(pawn, queenlessDisruptionDef);
                        break;

                    case WraithLeadershipState.Regent:
                        RemoveHediff(pawn, queenAuthorityDef);
                        EnsureHediff(pawn, regentAuthorityDef);
                        RemoveHediff(pawn, queenlessDisruptionDef);
                        break;

                    default:
                        RemoveHediff(pawn, queenAuthorityDef);
                        RemoveHediff(pawn, regentAuthorityDef);
                        EnsureHediff(pawn, queenlessDisruptionDef);
                        break;
                }
            }
        }

        private void ClearLeadershipState(IEnumerable<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns)
            {
                RemoveHediff(pawn, queenAuthorityDef);
                RemoveHediff(pawn, regentAuthorityDef);
                RemoveHediff(pawn, queenlessDisruptionDef);
                RemoveHediff(pawn, actingRegentDef);
            }
        }

        private static void EnsureHediff(Pawn pawn, HediffDef def)
        {
            if (pawn?.health?.hediffSet == null ||
                def == null ||
                pawn.health.hediffSet.HasHediff(def))
            {
                return;
            }

            pawn.health.AddHediff(def);
        }

        private static void RemoveHediff(Pawn pawn, HediffDef def)
        {
            if (pawn?.health?.hediffSet == null || def == null)
                return;

            Hediff hediff =
                pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
                pawn.health.RemoveHediff(hediff);
        }

        private void ResolveDefs()
        {
            queenAuthorityDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(
                    "WNG_QueenAuthority");
            regentAuthorityDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(
                    "WNG_RegentAuthority");
            queenlessDisruptionDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(
                    "WNG_QueenlessDisruption");
            actingRegentDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(
                    "WNG_ActingHiveRegent");
        }

        private static void Announce(
            Faction faction,
            Pawn target,
            string text,
            bool playerPositive)
        {
            if (faction == null || Faction.OfPlayer == null)
                return;

            bool relevant =
                faction == Faction.OfPlayer ||
                faction.HostileTo(Faction.OfPlayer);
            if (!relevant)
                return;

            try
            {
                Messages.Message(
                    text,
                    target,
                    playerPositive
                        ? MessageTypeDefOf.PositiveEvent
                        : MessageTypeDefOf.NeutralEvent,
                    historical: true);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Wraith leadership state committed but presentation failed: " +
                    ex.Message);
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long value =
                (long)Math.Max(0, now) +
                Math.Max(1, delay);
            return value >= int.MaxValue
                ? int.MaxValue
                : (int)value;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref records,
                "wngWraithLeadershipRecords",
                LookMode.Deep);
            Scribe_Values.Look(
                ref nextCheckTick,
                "wngWraithLeadershipNextCheck",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<WraithLeadershipRecord>();
                records.RemoveAll(record =>
                    record == null ||
                    record.faction == null);

                int now = Find.TickManager?.TicksGame ?? 0;
                nextCheckTick = Math.Max(0, nextCheckTick);

                foreach (WraithLeadershipRecord record in records)
                {
                    if (record.queenLostTick > now)
                        record.queenLostTick = now;

                    if (!ValidRegent(
                            record.actingRegent,
                            record.faction))
                    {
                        record.actingRegent = null;
                    }
                }
            }
        }

        private enum WraithLeadershipState
        {
            TrueQueen,
            Regent,
            Disrupted
        }
    }
}
