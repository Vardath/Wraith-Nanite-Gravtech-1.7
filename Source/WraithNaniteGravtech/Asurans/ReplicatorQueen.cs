using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Global exact-identity state for the one Replicator Queen. The gene marks the phenotype;
    /// sovereign authority belongs only to the exact Pawn reference stored here.
    /// </summary>
    public sealed class GameComponent_ReplicatorQueenState : GameComponent
    {
        private Pawn exactQueen;
        private bool vaultDiscovered;
        private bool released;
        private bool captured;
        private int vaultSiteId = -1;
        private bool firstRecoveryScheduled;
        private int firstRecoveryDueTick = -1;
        private bool firstRecoveryStarted;
        private bool firstRecoveryResolved;
        private int firstRecoveryMapId = -1;
        private bool captureDeparturePending;
        private Faction captureFaction;
        private bool recurringRecoveryScheduled;
        private int recurringRecoveryDueTick = -1;
        private bool recurringRecoveryStarted;
        private int recurringRecoveryMapId = -1;
        private int recurringRecoveryAttemptCount;

        public Pawn ExactQueen => exactQueen;
        public bool VaultDiscovered => vaultDiscovered;
        public bool Released => released;
        public bool Captured => captured;
        public int VaultSiteId => vaultSiteId;
        public bool FirstRecoveryStarted => firstRecoveryStarted;
        public bool FirstRecoveryResolved => firstRecoveryResolved;
        public bool RecurringRecoveryStarted => recurringRecoveryStarted;
        public int RecurringRecoveryAttemptCount => recurringRecoveryAttemptCount;

        public GameComponent_ReplicatorQueenState(Game game) { }

        public bool TryRegisterExactQueen(Pawn queen, int siteId)
        {
            if (queen == null)
                return false;
            if (exactQueen != null && exactQueen != queen)
                return false;
            exactQueen = queen;
            vaultDiscovered = true;
            if (siteId >= 0)
                vaultSiteId = siteId;
            return true;
        }

        public void MarkVaultDiscovered(int siteId)
        {
            vaultDiscovered = true;
            if (siteId >= 0)
                vaultSiteId = siteId;
        }

        public void MarkReleased()
        {
            released = true;
            captured = false;
            if (!firstRecoveryScheduled && !firstRecoveryResolved)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                firstRecoveryScheduled = true;
                firstRecoveryDueTick = SafeFutureTick(now, ReplicatorQueenRecoveryUtility.InitialRecoveryDelayTicks);
            }
        }

        public bool FirstRecoveryReady(int now)
        {
            return released && !captured && firstRecoveryScheduled && !firstRecoveryStarted &&
                   !firstRecoveryResolved && firstRecoveryDueTick >= 0 && now >= firstRecoveryDueTick;
        }

        public bool TryClaimFirstRecovery(int mapId)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!FirstRecoveryReady(now))
                return false;
            firstRecoveryStarted = true;
            firstRecoveryMapId = mapId;
            return true;
        }

        public bool FirstRecoveryOwnedByMap(int mapId)
        {
            return firstRecoveryStarted && !firstRecoveryResolved && firstRecoveryMapId == mapId;
        }

        public void RollBackFirstRecoveryClaim(int retryDelayTicks)
        {
            if (firstRecoveryResolved || captured)
                return;
            firstRecoveryStarted = false;
            firstRecoveryMapId = -1;
            int now = Find.TickManager?.TicksGame ?? 0;
            firstRecoveryDueTick = SafeFutureTick(now, Math.Max(60, retryDelayTicks));
            firstRecoveryScheduled = true;
        }

        public void MarkFirstRecoveryDefeated()
        {
            if (captured)
                return;
            firstRecoveryResolved = true;
            firstRecoveryStarted = false;
            firstRecoveryMapId = -1;
            ScheduleRecurringRecovery(WNGSettingsUtility.ReplicatorQueenRecurringRecoveryTicks);
        }

        public bool RecurringRecoveryReady(int now)
        {
            return released && !captured && !captureDeparturePending && firstRecoveryResolved && exactQueen != null && !exactQueen.Dead &&
                   recurringRecoveryScheduled && !recurringRecoveryStarted && recurringRecoveryDueTick >= 0 &&
                   now >= recurringRecoveryDueTick;
        }

        public bool TryClaimRecurringRecovery(int mapId)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!RecurringRecoveryReady(now))
                return false;
            recurringRecoveryStarted = true;
            recurringRecoveryMapId = mapId;
            return true;
        }

        public bool RecoveryOwnedByMap(int mapId, out bool recurring)
        {
            if (firstRecoveryStarted && !firstRecoveryResolved && firstRecoveryMapId == mapId)
            {
                recurring = false;
                return true;
            }
            if (recurringRecoveryStarted && !captured && recurringRecoveryMapId == mapId)
            {
                recurring = true;
                return true;
            }
            recurring = false;
            return false;
        }

        public void RollBackRecurringRecoveryClaim(int retryDelayTicks)
        {
            if (captured)
                return;
            recurringRecoveryStarted = false;
            recurringRecoveryMapId = -1;
            int now = Find.TickManager?.TicksGame ?? 0;
            recurringRecoveryDueTick = SafeFutureTick(now, Math.Max(60, retryDelayTicks));
            recurringRecoveryScheduled = true;
        }

        public void MarkRecurringRecoveryDefeated()
        {
            if (captured)
                return;
            recurringRecoveryStarted = false;
            recurringRecoveryMapId = -1;
            recurringRecoveryAttemptCount = Math.Max(0, recurringRecoveryAttemptCount + 1);
            ScheduleRecurringRecovery(WNGSettingsUtility.ReplicatorQueenRecurringRecoveryTicks);
        }

        public void MarkRecoveryEndedByQueenDeath()
        {
            firstRecoveryResolved = true;
            firstRecoveryStarted = false;
            firstRecoveryMapId = -1;
            recurringRecoveryScheduled = false;
            recurringRecoveryStarted = false;
            recurringRecoveryMapId = -1;
            recurringRecoveryDueTick = -1;
        }

        private void ScheduleRecurringRecovery(int delayTicks)
        {
            if (captured || !released || exactQueen == null || exactQueen.Dead)
                return;
            int now = Find.TickManager?.TicksGame ?? 0;
            recurringRecoveryScheduled = true;
            recurringRecoveryStarted = false;
            recurringRecoveryMapId = -1;
            recurringRecoveryDueTick = SafeFutureTick(now, Math.Max(60, delayTicks));
        }

        public void MarkCaptureDeparture(Faction captor)
        {
            if (exactQueen == null || captor == null)
                return;
            captureFaction = captor;
            captureDeparturePending = true;
            firstRecoveryResolved = true;
            firstRecoveryStarted = false;
            firstRecoveryMapId = -1;
            recurringRecoveryScheduled = false;
            recurringRecoveryStarted = false;
            recurringRecoveryMapId = -1;
            recurringRecoveryDueTick = -1;
            TryFinalizeCapture();
        }

        public void MarkCaptured()
        {
            if (exactQueen != null)
            {
                captured = true;
                captureDeparturePending = false;
                firstRecoveryResolved = true;
                firstRecoveryStarted = false;
                firstRecoveryMapId = -1;
                recurringRecoveryScheduled = false;
                recurringRecoveryStarted = false;
                recurringRecoveryMapId = -1;
                recurringRecoveryDueTick = -1;
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (captureDeparturePending && (Find.TickManager?.TicksGame ?? 0) % 60 == 0)
                TryFinalizeCapture();
        }

        private void TryFinalizeCapture()
        {
            if (!captureDeparturePending || exactQueen == null || captureFaction == null)
                return;
            try
            {
                if (exactQueen.Faction != captureFaction)
                    exactQueen.SetFaction(captureFaction);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Queen carrier departed but hostile faction reconciliation is still pending; exact capture state retained for retry: " + ex.Message);
                return;
            }
            if (exactQueen.Faction == captureFaction)
            {
                captured = true;
                captureDeparturePending = false;
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref exactQueen, "wngExactReplicatorQueen");
            Scribe_Values.Look(ref vaultDiscovered, "wngReplicatorQueenVaultDiscovered", false);
            Scribe_Values.Look(ref released, "wngReplicatorQueenReleased", false);
            Scribe_Values.Look(ref captured, "wngReplicatorQueenCaptured", false);
            Scribe_Values.Look(ref vaultSiteId, "wngReplicatorQueenVaultSiteId", -1);
            Scribe_Values.Look(ref firstRecoveryScheduled, "wngReplicatorQueenFirstRecoveryScheduled", false);
            Scribe_Values.Look(ref firstRecoveryDueTick, "wngReplicatorQueenFirstRecoveryDueTick", -1);
            Scribe_Values.Look(ref firstRecoveryStarted, "wngReplicatorQueenFirstRecoveryStarted", false);
            Scribe_Values.Look(ref firstRecoveryResolved, "wngReplicatorQueenFirstRecoveryResolved", false);
            Scribe_Values.Look(ref firstRecoveryMapId, "wngReplicatorQueenFirstRecoveryMapId", -1);
            Scribe_Values.Look(ref captureDeparturePending, "wngReplicatorQueenCaptureDeparturePending", false);
            Scribe_References.Look(ref captureFaction, "wngReplicatorQueenCaptureFaction");
            Scribe_Values.Look(ref recurringRecoveryScheduled, "wngReplicatorQueenRecurringRecoveryScheduled", false);
            Scribe_Values.Look(ref recurringRecoveryDueTick, "wngReplicatorQueenRecurringRecoveryDueTick", -1);
            Scribe_Values.Look(ref recurringRecoveryStarted, "wngReplicatorQueenRecurringRecoveryStarted", false);
            Scribe_Values.Look(ref recurringRecoveryMapId, "wngReplicatorQueenRecurringRecoveryMapId", -1);
            Scribe_Values.Look(ref recurringRecoveryAttemptCount, "wngReplicatorQueenRecurringRecoveryAttemptCount", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (firstRecoveryDueTick < -1) firstRecoveryDueTick = -1;
                if (firstRecoveryMapId < -1) firstRecoveryMapId = -1;
                if (recurringRecoveryDueTick < -1) recurringRecoveryDueTick = -1;
                if (recurringRecoveryMapId < -1) recurringRecoveryMapId = -1;
                if (recurringRecoveryAttemptCount < 0) recurringRecoveryAttemptCount = 0;
                if (released && !captured && !captureDeparturePending && firstRecoveryResolved &&
                    !recurringRecoveryScheduled && !recurringRecoveryStarted && exactQueen != null && !exactQueen.Dead)
                {
                    int now = Find.TickManager?.TicksGame ?? 0;
                    recurringRecoveryScheduled = true;
                    recurringRecoveryDueTick = SafeFutureTick(now, WNGSettingsUtility.ReplicatorQueenRecurringRecoveryTicks);
                }
            }
        }
    }

    public static class ReplicatorQueenUtility
    {
        public static GameComponent_ReplicatorQueenState State => Current.Game?.GetComponent<GameComponent_ReplicatorQueenState>();

        public static void NormalizeGeneratedQueenChild(Pawn queen)
        {
            if (queen == null || queen.kindDef?.defName != "WNG_ReplicatorQueenChild")
                return;

            XenotypeDef expected = DefDatabase<XenotypeDef>.GetNamedSilentFail("WNG_HumanFormReplicator");
            if (expected != null && queen.genes != null && queen.genes.Xenotype != expected)
                queen.genes.SetXenotype(expected);

            queen.gender = Gender.Female;
            if (queen.ageTracker != null)
            {
                const long ticksAtThirteen = 13L * 3600000L;
                queen.ageTracker.AgeBiologicalTicks = ticksAtThirteen;
                if (queen.ageTracker.AgeChronologicalTicks < ticksAtThirteen)
                    queen.ageTracker.AgeChronologicalTicks = ticksAtThirteen;
            }

            AsuranDefaultApparelUtility.EnsureRoleApparel(queen);
        }

        public static bool IsExactQueen(Pawn pawn)
        {
            return pawn != null && State?.ExactQueen == pawn;
        }

        public static bool HasQueenMarker(Pawn pawn)
        {
            GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail("WNG_ReplicatorQueenLink");
            return pawn?.genes != null && def != null && pawn.genes.GenesListForReading.Any(g => g.def == def && g.Active);
        }
    }

    public sealed class CompProperties_AbilityReplicatorQueenDirective : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityReplicatorQueenDirective()
        {
            compClass = typeof(CompAbilityEffect_ReplicatorQueenDirective);
        }
    }

    /// <summary>
    /// Exact Queen targeted sovereignty. Using the ability on an uncontrolled block captures that
    /// exact body into the Queen domain; using it again on one of her own controlled blocks releases
    /// the exact pre-control faction/authority/domain/controller state.
    /// </summary>
    public sealed class CompAbilityEffect_ReplicatorQueenDirective : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn queen = parent.pawn;
            Pawn block = target.Pawn;
            if (!ReplicatorQueenUtility.IsExactQueen(queen) || queen == null || queen.Dead || !queen.Spawned)
            {
                if (throwMessages)
                    Messages.Message("Only the exact Replicator Queen can assert this sovereign lattice.", queen, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (block == null || block.Dead || !block.Spawned || block.Map != queen.Map ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(block))
            {
                if (throwMessages)
                    Messages.Message("Sovereign control can only target a living block Replicator on the Queen's map.", queen, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(queen) || ReplicatorInterferenceUtility.IsEmpDisrupted(block) ||
                ReplicatorContainmentUtility.IsContained(queen.Map, queen.Position) ||
                ReplicatorContainmentUtility.IsContained(block.Map, block.Position))
            {
                if (throwMessages)
                    Messages.Message("EMP disruption or active containment is blocking the sovereign lattice.", block, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (block.TryGetComp<CompReplicatorTemporaryAsuranState>()?.HasOverrideRecord == true)
            {
                if (throwMessages)
                    Messages.Message("A temporary Asuran intrusion must resolve before Queen authority can change this block.", block, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn queen = parent.pawn;
            Pawn block = target.Pawn;
            if (queen == null || block == null)
                return;

            CompReplicatorSovereignState state = block.TryGetComp<CompReplicatorSovereignState>();
            if (state == null)
                return;

            bool success;
            string message;
            if (state.HasRecord && state.ControllerPawn == queen && state.ControlAuthority == ReplicatorControlAuthority.ExactQueen)
            {
                success = state.TryReleaseToExactPriorState();
                message = success
                    ? block.LabelShortCap + " has been released from the Queen's sovereign lattice and restored to its exact prior controller state."
                    : "The Queen could not safely release " + block.LabelShortCap + ".";
            }
            else
            {
                success = state.TryAssignExactQueen(queen);
                message = success
                    ? block.LabelShortCap + " has entered the exact Replicator Queen's sovereign domain."
                    : "The Queen could not acquire sovereign control of " + block.LabelShortCap + ".";
            }

            if (queen.Faction == Faction.OfPlayer)
                Messages.Message(message, block, success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }
    }

    /// <summary>
    /// Native neutral world site containing the exact Queen in a real AncientCryptosleepCasket.
    /// Registration occurs only after the exact pawn has successfully entered that exact casket.
    /// </summary>
    public sealed class SitePartWorker_ReplicatorQueenVault : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            Site site = map?.Parent as Site;
            GameComponent_ReplicatorQueenState state = ReplicatorQueenUtility.State;
            if (map == null || site == null || state == null || state.ExactQueen != null)
                return;

            ThingDef casketDef = DefDatabase<ThingDef>.GetNamedSilentFail("AncientCryptosleepCasket");
            PawnKindDef queenKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("WNG_ReplicatorQueenChild");
            GeneDef queenGene = DefDatabase<GeneDef>.GetNamedSilentFail("WNG_ReplicatorQueenLink");
            if (casketDef == null || queenKind == null || queenGene == null)
                return;

            IntVec3 cell = FindCasketCell(map);
            if (!cell.IsValid)
                return;

            Building_CryptosleepCasket casket = ThingMaker.MakeThing(casketDef) as Building_CryptosleepCasket;
            if (casket == null)
                return;
            GenSpawn.Spawn(casket, cell, map);

            Pawn queen = null;
            try
            {
                queen = PawnGenerator.GeneratePawn(queenKind, null);
                ReplicatorQueenUtility.NormalizeGeneratedQueenChild(queen);
                if (queen?.genes == null)
                    throw new InvalidOperationException("Generated Queen has no gene tracker.");
                if (!ReplicatorQueenUtility.HasQueenMarker(queen))
                    queen.genes.AddGene(queenGene, true);
                if (!casket.TryAcceptThing(queen, false))
                    throw new InvalidOperationException("Native cryptosleep casket rejected exact Queen pawn.");
                if (!state.TryRegisterExactQueen(queen, site.ID))
                    throw new InvalidOperationException("Exact Queen registry rejected generated pawn.");
            }
            catch (Exception ex)
            {
                if (queen != null && !queen.Destroyed)
                    queen.Destroy(DestroyMode.Vanish);
                if (!casket.Destroyed)
                    casket.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Replicator Queen vault generation failed before exact-pawn commit: " + ex);
            }
        }

        private static IntVec3 FindCasketCell(Map map)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(map.Center, 14f, true))
            {
                if (cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null)
                    return cell;
            }
            return IntVec3.Invalid;
        }
    }

    /// <summary>
    /// Watches only for the exact registered Queen to become physically spawned from her casket.
    /// Faction transfer to the player is the release commit; story/UI cleanup is best-effort after.
    /// </summary>
    public sealed class MapComponent_ReplicatorQueenRelease : MapComponent
    {
        public MapComponent_ReplicatorQueenRelease(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;
            if (!map.IsHashIntervalTick(60))
                return;

            GameComponent_ReplicatorQueenState state = ReplicatorQueenUtility.State;
            Pawn queen = state?.ExactQueen;
            if (state == null || state.Released || queen == null || queen.Dead || !queen.Spawned || queen.Map != map)
                return;

            try
            {
                if (queen.Faction != Faction.OfPlayer)
                    queen.SetFaction(Faction.OfPlayer);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Exact Replicator Queen emerged but player recruitment failed; release remains pending: " + ex.Message);
                return;
            }

            state.MarkReleased();

            try
            {
                HediffDef sickness = DefDatabase<HediffDef>.GetNamedSilentFail("CryptosleepSickness");
                Hediff hediff = sickness == null ? null : queen.health?.hediffSet?.GetFirstHediffOfDef(sickness);
                if (hediff != null)
                    queen.health.RemoveHediff(hediff);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Queen release committed but synthetic cryptosleep cleanup failed: " + ex.Message);
            }

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Replicator Queen released",
                    "The child-sized synthetic has emerged from the ancient casket and immediately aligned herself with your colony. She is the one exact Replicator Queen: copies of her body or genome do not inherit her sovereign block-Replicator authority.",
                    LetterDefOf.PositiveEvent,
                    queen);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Queen release committed but presentation failed: " + ex.Message);
            }
        }
    }

    public sealed class IncidentWorker_ReplicatorQueenVaultDiscovery : IncidentWorker
    {
        private const int MinSiteDistance = 8;
        private const int MaxSiteDistance = 24;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            GameComponent_ReplicatorQueenState state = ReplicatorQueenUtility.State;
            return map != null && map.IsPlayerHome && state != null && !state.VaultDiscovered && state.ExactQueen == null;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms?.target as Map;
            GameComponent_ReplicatorQueenState state = ReplicatorQueenUtility.State;
            SitePartDef siteDef = DefDatabase<SitePartDef>.GetNamedSilentFail("WNG_ReplicatorQueenVault");
            if (map == null || !map.IsPlayerHome || state == null || state.VaultDiscovered || state.ExactQueen != null || siteDef == null)
                return false;

            if (Find.WorldObjects.AllWorldObjects.OfType<Site>().Any(s => s.parts != null && s.parts.Any(p => p.def == siteDef)))
                return false;

            PlanetTile tile;
            if (!TileFinder.TryFindNewSiteTile(out tile, MinSiteDistance, MaxSiteDistance, allowCaravans: false))
                return false;

            float threatPoints = parms.points > 0f ? parms.points : StorytellerUtility.DefaultSiteThreatPointsNow();
            Site site = SiteMaker.MakeSite(siteDef, tile, faction: null, ifHostileThenMustRemainHostile: false,
                threatPoints: Mathf.Max(350f, threatPoints * 0.75f));
            if (site == null)
                return false;

            site.customLabel = "Sealed Nanite Vault";
            try
            {
                Find.WorldObjects.Add(site);
            }
            catch
            {
                return false;
            }

            state.MarkVaultDiscovered(site.ID);
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Impossible nanite signal",
                    "Long-range instruments have isolated an exceptionally stable human-form nanite pattern inside an unclaimed precursor-era vault. The body profile appears to be that of a thirteen-year-old child held in an intact ancient cryptosleep chamber.",
                    LetterDefOf.PositiveEvent,
                    site);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Queen vault discovery committed but presentation failed: " + ex.Message);
            }
            return true;
        }
    }
}
