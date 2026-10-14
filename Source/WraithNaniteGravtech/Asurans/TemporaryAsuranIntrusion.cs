using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorTemporaryAsuranState : CompProperties
    {
        public CompProperties_ReplicatorTemporaryAsuranState()
        {
            compClass = typeof(CompReplicatorTemporaryAsuranState);
        }
    }

    /// <summary>
    /// Reversible Temporary-Asuran state only. CompReplicatorDomain remains the single owner of
    /// controller authority/domain identity; this component stores the exact pre-intrusion snapshot
    /// and the separate command-suppression state used when larger forms resist takeover.
    /// </summary>
    public sealed class CompReplicatorTemporaryAsuranState : ThingComp
    {
        private Faction priorFaction;
        private ReplicatorControlAuthority priorAuthority = ReplicatorControlAuthority.Unassigned;
        private string priorDomainId;
        private Pawn priorAuthorityPawn;

        private Faction overrideFaction;
        private Pawn overrideAuthorityPawn;
        private string temporaryDomainId;
        private int overrideUntilTick;

        private Pawn suppressingPawn;
        private int commandSuppressedUntilTick;

        private Pawn Pawn => parent as Pawn;

        public bool HasOverrideRecord => overrideFaction != null && !string.IsNullOrEmpty(temporaryDomainId);
        public bool HasSuppressionRecord => suppressingPawn != null && commandSuppressedUntilTick > 0;
        public int OverrideUntilTick => overrideUntilTick;
        public int CommandSuppressedUntilTick => commandSuppressedUntilTick;
        public Faction PriorFaction => priorFaction;
        public ReplicatorControlAuthority PriorAuthority => priorAuthority;
        public string PriorDomainId => priorDomainId;
        public Pawn PriorAuthorityPawn => priorAuthorityPawn;
        public Faction OverrideFaction => overrideFaction;
        public Pawn OverrideAuthorityPawn => overrideAuthorityPawn;
        public string TemporaryDomainId => temporaryDomainId;
        public Pawn SuppressingPawn => suppressingPawn;

        public bool ActiveOverride
        {
            get
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                return HasOverrideRecord && now < overrideUntilTick && ExpectedTemporaryStateStillOwns(Pawn);
            }
        }

        public bool CommandSuppressed
        {
            get
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                return HasSuppressionRecord && now < commandSuppressedUntilTick;
            }
        }

        public bool TryApplyOverride(Pawn caster, int durationTicks)
        {
            Pawn pawn = Pawn;
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || caster == null ||
                caster.Dead || !caster.Spawned || caster.Map != pawn.Map || caster.Faction == null ||
                pawn.Faction == null || pawn.Faction == caster.Faction || domain == null)
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            int until = SafeFutureTick(now, Math.Max(600, durationTicks));

            if (HasOverrideRecord)
            {
                if (ExpectedTemporaryStateStillOwns(pawn) && overrideAuthorityPawn == caster &&
                    overrideFaction == caster.Faction && now < overrideUntilTick)
                {
                    overrideUntilTick = Math.Max(overrideUntilTick, until);
                    return true;
                }
                return false;
            }

            domain.EnsureAutonomousIdentity();
            Faction savedFaction = pawn.Faction;
            ReplicatorControlAuthority savedAuthority = domain.Authority;
            string savedDomain = domain.DomainId;
            Pawn savedAuthorityPawn = domain.AuthorityPawn;
            string tempDomain = BuildTemporaryDomainId(caster, savedDomain);

            try
            {
                pawn.SetFaction(caster.Faction);
                domain.AssignExactState(ReplicatorControlAuthority.TemporaryAsuran, tempDomain, caster);

                priorFaction = savedFaction;
                priorAuthority = savedAuthority;
                priorDomainId = savedDomain;
                priorAuthorityPawn = savedAuthorityPawn;
                overrideFaction = caster.Faction;
                overrideAuthorityPawn = caster;
                temporaryDomainId = tempDomain;
                overrideUntilTick = until;
                ClearSuppression();
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (savedFaction != null && pawn.Faction != savedFaction)
                        pawn.SetFaction(savedFaction);
                    domain.AssignExactState(savedAuthority, savedDomain, savedAuthorityPawn);
                }
                catch (Exception rollbackEx)
                {
                    Log.Error("[WNG] Temporary Asuran intrusion failed and exact rollback also failed: " + rollbackEx);
                }
                Log.Error("[WNG] Temporary Asuran intrusion failed before commit: " + ex);
                return false;
            }
        }

        public bool TryApplyCommandSuppression(Pawn caster, int durationTicks)
        {
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || caster == null ||
                caster.Dead || !caster.Spawned || caster.Map != pawn.Map || caster.Faction == null ||
                pawn.Faction == null || pawn.Faction == caster.Faction || HasOverrideRecord)
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (HasSuppressionRecord && suppressingPawn != caster && now < commandSuppressedUntilTick)
                return false;

            suppressingPawn = caster;
            commandSuppressedUntilTick = Math.Max(
                commandSuppressedUntilTick,
                SafeFutureTick(now, Math.Max(600, durationTicks)));
            return true;
        }

        public void CopyStateTo(Pawn child)
        {
            CompReplicatorTemporaryAsuranState target = child?.TryGetComp<CompReplicatorTemporaryAsuranState>();
            if (target == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (HasOverrideRecord && now < overrideUntilTick && ExpectedTemporaryStateStillOwns(Pawn))
            {
                target.priorFaction = priorFaction;
                target.priorAuthority = priorAuthority;
                target.priorDomainId = priorDomainId;
                target.priorAuthorityPawn = priorAuthorityPawn;
                target.overrideFaction = overrideFaction;
                target.overrideAuthorityPawn = overrideAuthorityPawn;
                target.temporaryDomainId = temporaryDomainId;
                target.overrideUntilTick = overrideUntilTick;
            }

            if (HasSuppressionRecord && now < commandSuppressedUntilTick)
            {
                target.suppressingPawn = suppressingPawn;
                target.commandSuppressedUntilTick = commandSuppressedUntilTick;
            }
        }

        public bool RestorationStateCompatibleWith(CompReplicatorTemporaryAsuranState other)
        {
            bool mine = ActiveOverride;
            bool theirs = other?.ActiveOverride == true;
            if (mine != theirs)
                return false;
            if (!mine)
                return true;

            return priorFaction == other.priorFaction &&
                   priorAuthority == other.priorAuthority &&
                   priorDomainId == other.priorDomainId &&
                   priorAuthorityPawn == other.priorAuthorityPawn &&
                   overrideFaction == other.overrideFaction &&
                   overrideAuthorityPawn == other.overrideAuthorityPawn &&
                   temporaryDomainId == other.temporaryDomainId;
        }

        public void CopyMergedOverrideFrom(IEnumerable<Pawn> donors)
        {
            if (donors == null)
                return;

            List<CompReplicatorTemporaryAsuranState> states = donors
                .Select(p => p?.TryGetComp<CompReplicatorTemporaryAsuranState>())
                .Where(s => s != null && s.ActiveOverride)
                .ToList();
            if (states.Count == 0)
                return;

            CompReplicatorTemporaryAsuranState first = states[0];
            priorFaction = first.priorFaction;
            priorAuthority = first.priorAuthority;
            priorDomainId = first.priorDomainId;
            priorAuthorityPawn = first.priorAuthorityPawn;
            overrideFaction = first.overrideFaction;
            overrideAuthorityPawn = first.overrideAuthorityPawn;
            temporaryDomainId = first.temporaryDomainId;
            // Recombination never extends control beyond the earliest contributing body's expiry.
            overrideUntilTick = states.Min(s => s.overrideUntilTick);
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (((long)now + pawn.thingIDNumber) % 60L != 0L)
                return;

            if (HasOverrideRecord)
            {
                if (!ExpectedTemporaryStateStillOwns(pawn))
                    ClearOverride(); // a later legitimate authority/domain change wins
                else if (overrideAuthorityPawn?.Faction != overrideFaction ||
                         now >= overrideUntilTick || IsIntrusionInterrupted(pawn, overrideAuthorityPawn))
                    RestoreExactPriorState();
            }

            if (HasSuppressionRecord &&
                (now >= commandSuppressedUntilTick || IsIntrusionInterrupted(pawn, suppressingPawn)))
            {
                ClearSuppression();
            }
        }

        private bool ExpectedTemporaryStateStillOwns(Pawn pawn)
        {
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            return pawn != null && domain != null && pawn.Faction == overrideFaction &&
                   domain.Authority == ReplicatorControlAuthority.TemporaryAsuran &&
                   domain.DomainId == temporaryDomainId &&
                   domain.AuthorityPawn == overrideAuthorityPawn;
        }

        private void RestoreExactPriorState()
        {
            Pawn pawn = Pawn;
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            if (pawn == null || domain == null || priorFaction == null)
            {
                ClearOverride();
                return;
            }

            try
            {
                if (pawn.Faction != priorFaction)
                    pawn.SetFaction(priorFaction);
                domain.AssignExactState(priorAuthority, priorDomainId, priorAuthorityPawn);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Temporary Asuran intrusion ended but exact restoration failed; snapshot retained for retry: " + ex.Message);
                return;
            }

            if (pawn.Faction == priorFaction &&
                domain.Authority == priorAuthority &&
                domain.DomainId == priorDomainId &&
                domain.AuthorityPawn == priorAuthorityPawn)
            {
                ClearOverride();
            }
        }

        private static bool IsIntrusionInterrupted(Pawn target, Pawn controller)
        {
            if (target == null || target.Dead || !target.Spawned || target.Map == null)
                return true;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(target) ||
                ReplicatorContainmentUtility.IsContained(target.Map, target.Position))
                return true;

            if (controller == null || controller.Dead || !controller.Spawned || controller.Map != target.Map ||
                controller.Faction == null || !AsuranCollectiveUtility.IsLinked(controller) ||
                AsuranCollectiveUtility.IsDisrupted(controller) ||
                ReplicatorContainmentUtility.IsContained(controller.Map, controller.Position))
                return true;

            return false;
        }

        private static string BuildTemporaryDomainId(Pawn caster, string priorDomain)
        {
            return "temp-asuran:" + (caster?.thingIDNumber ?? 0) + ":" + (priorDomain ?? "none");
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(0, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private void ClearOverride()
        {
            priorFaction = null;
            priorAuthority = ReplicatorControlAuthority.Unassigned;
            priorDomainId = null;
            priorAuthorityPawn = null;
            overrideFaction = null;
            overrideAuthorityPawn = null;
            temporaryDomainId = null;
            overrideUntilTick = 0;
        }

        private void ClearSuppression()
        {
            suppressingPawn = null;
            commandSuppressedUntilTick = 0;
        }

        public override string CompInspectStringExtra()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (ActiveOverride)
                return "Temporary Asuran lattice override: " + Math.Max(0, overrideUntilTick - now) +
                       " ticks remain; exact prior controller state will be restored.";
            if (CommandSuppressed)
                return "Asuran command suppression: " + Math.Max(0, commandSuppressedUntilTick - now) + " ticks remain.";
            return null;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref priorFaction, "wngTempAsuranPriorFaction");
            Scribe_Values.Look(ref priorAuthority, "wngTempAsuranPriorAuthority", ReplicatorControlAuthority.Unassigned);
            Scribe_Values.Look(ref priorDomainId, "wngTempAsuranPriorDomainId");
            Scribe_References.Look(ref priorAuthorityPawn, "wngTempAsuranPriorAuthorityPawn");
            Scribe_References.Look(ref overrideFaction, "wngTempAsuranOverrideFaction");
            Scribe_References.Look(ref overrideAuthorityPawn, "wngTempAsuranOverrideAuthorityPawn");
            Scribe_Values.Look(ref temporaryDomainId, "wngTempAsuranDomainId");
            Scribe_Values.Look(ref overrideUntilTick, "wngTempAsuranOverrideUntilTick", 0);
            Scribe_References.Look(ref suppressingPawn, "wngTempAsuranSuppressingPawn");
            Scribe_Values.Look(ref commandSuppressedUntilTick, "wngTempAsuranSuppressedUntilTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                overrideUntilTick = Math.Max(0, overrideUntilTick);
                commandSuppressedUntilTick = Math.Max(0, commandSuppressedUntilTick);
            }
        }
    }

    public static class TemporaryAsuranIntrusionUtility
    {
        public static bool IsTemporarilyOverridden(Pawn pawn)
        {
            return pawn?.TryGetComp<CompReplicatorTemporaryAsuranState>()?.ActiveOverride == true;
        }

        public static bool IsCommandSuppressed(Pawn pawn)
        {
            return pawn?.TryGetComp<CompReplicatorTemporaryAsuranState>()?.CommandSuppressed == true;
        }

        public static bool TransformationStatesCompatible(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
                return false;

            CompReplicatorTemporaryAsuranState first = null;
            bool any = false;
            foreach (Pawn pawn in pawns)
            {
                CompReplicatorTemporaryAsuranState state = pawn?.TryGetComp<CompReplicatorTemporaryAsuranState>();
                if (!any)
                {
                    first = state;
                    any = true;
                    continue;
                }

                if (first == null)
                {
                    if (state?.ActiveOverride == true)
                        return false;
                }
                else if (!first.RestorationStateCompatibleWith(state))
                    return false;
            }
            return any;
        }

        public static void CopyState(Pawn source, Pawn target)
        {
            source?.TryGetComp<CompReplicatorTemporaryAsuranState>()?.CopyStateTo(target);
        }

        public static void CopyMergedState(IEnumerable<Pawn> donors, Pawn target)
        {
            target?.TryGetComp<CompReplicatorTemporaryAsuranState>()?.CopyMergedOverrideFrom(donors);
        }
    }

    /// <summary>
    /// Sparse non-player use of the same temporary intrusion contract. This is not a second control
    /// implementation: it calls the same reversible state component and uses the same first-build
    /// duration values. Player pawns use the explicit ability gizmo instead.
    /// </summary>
    public sealed class MapComponent_AsuranReplicatorCountermeasureAI : MapComponent
    {
        private const int ScanIntervalTicks = 300;
        private const int AutonomousCooldownTicks = 18000;
        private const int MaxActionsPerScan = 2;
        private const float Range = 18f;

        private const int DroneOverrideTicks = 15000;
        private const int HunterOverrideTicks = 9000;
        private const int BulwarkSuppressionTicks = 6000;
        private const int TitanSuppressionTicks = 4500;
        private const int ControllerSuppressionTicks = 7500;
        private const int SiegeSuppressionTicks = 3000;
        private const int SpecialistSuppressionTicks = 6000;
        private const float PeerDurationBonus = 0.08f;
        private const int MaxPeerBonusCount = 3;
        private const float ShieldResistanceFactor = 0.75f;

        private int nextScanTick;
        private Dictionary<int, int> nextUseByPawnId = new Dictionary<int, int>();

        public MapComponent_AsuranReplicatorCountermeasureAI(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextScanTick)
                return;
            nextScanTick = SafeFutureTick(now, ScanIntervalTicks);
            PruneCooldowns(now);

            int actions = 0;
            List<Pawn> casters = map.mapPawns.AllPawnsSpawned
                .Where(IsEligibleNpcCaster)
                .OrderBy(pawn => pawn.thingIDNumber)
                .ToList();

            for (int i = 0; i < casters.Count && actions < MaxActionsPerScan; i++)
            {
                Pawn caster = casters[i];
                int readyTick;
                if (nextUseByPawnId.TryGetValue(caster.thingIDNumber, out readyTick) && now < readyTick)
                    continue;

                Pawn target = FindTarget(caster);
                if (target == null)
                    continue;

                if (!TryApplyNpcEffect(caster, target))
                    continue;

                // Effect is the gameplay commit. Persist cooldown before optional presentation so
                // a message failure cannot cause a duplicate intrusion at the next scan.
                nextUseByPawnId[caster.thingIDNumber] = SafeFutureTick(now, AutonomousCooldownTicks);
                actions++;
                PresentBestEffort(target);
            }
        }

        private static bool IsEligibleNpcCaster(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.Spawned && pawn.Map != null && pawn.Faction != null &&
                   pawn.Faction != Faction.OfPlayer &&
                   AsuranCollectiveUtility.IsLinked(pawn) &&
                   !AsuranCollectiveUtility.IsDisrupted(pawn) &&
                   !ReplicatorContainmentUtility.IsContained(pawn.Map, pawn.Position);
        }

        private Pawn FindTarget(Pawn caster)
        {
            float rangeSq = Range * Range;
            Pawn best = null;
            int bestPriority = int.MaxValue;
            int bestDistance = int.MaxValue;

            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn target = spawned[i];
                if (!IsEligibleHostileBlock(caster, target))
                    continue;

                int distance = target.Position.DistanceToSquared(caster.Position);
                if (distance > rangeSq)
                    continue;

                int priority = TargetPriority(target);
                if (best == null || priority < bestPriority ||
                    (priority == bestPriority && distance < bestDistance) ||
                    (priority == bestPriority && distance == bestDistance && target.thingIDNumber < best.thingIDNumber))
                {
                    best = target;
                    bestPriority = priority;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static bool IsEligibleHostileBlock(Pawn caster, Pawn target)
        {
            if (caster == null || target == null || target.Dead || !target.Spawned || target.Faction == null ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(target) || target.Faction == caster.Faction ||
                !target.Faction.HostileTo(caster.Faction) ||
                ReplicatorInterferenceUtility.IsEmpDisrupted(target) ||
                ReplicatorContainmentUtility.IsContained(target.Map, target.Position))
                return false;

            CompReplicatorTemporaryAsuranState state = target.TryGetComp<CompReplicatorTemporaryAsuranState>();
            if (state == null)
                return false;

            string defName = target.def?.defName ?? string.Empty;
            if (defName == "WNG_ReplicatorDrone" || defName == "WNG_ReplicatorHunter")
                return !state.ActiveOverride;

            return !state.CommandSuppressed;
        }

        private static int TargetPriority(Pawn pawn)
        {
            string defName = pawn?.def?.defName ?? string.Empty;
            switch (defName)
            {
                case "WNG_ReplicatorController": return 0;
                case "WNG_ReplicatorTitan": return 1;
                case "WNG_ReplicatorBulwark": return 2;
                case "WNG_ReplicatorSiegeMass": return 3;
                case "WNG_ReplicatorHunter": return 4;
                default: return 5;
            }
        }

        private static bool TryApplyNpcEffect(Pawn caster, Pawn target)
        {
            if (!IsEligibleNpcCaster(caster) || !IsEligibleHostileBlock(caster, target))
                return false;

            CompReplicatorTemporaryAsuranState state = target.TryGetComp<CompReplicatorTemporaryAsuranState>();
            if (state == null)
                return false;

            string defName = target.def?.defName ?? string.Empty;
            float factor = DurationFactor(caster, target);

            if (defName == "WNG_ReplicatorDrone" || defName == "WNG_ReplicatorHunter")
            {
                int baseTicks = defName == "WNG_ReplicatorDrone" ? DroneOverrideTicks : HunterOverrideTicks;
                return state.TryApplyOverride(caster, Math.Max(600, (int)Math.Round(baseTicks * factor)));
            }

            int suppressionTicks;
            switch (defName)
            {
                case "WNG_ReplicatorController": suppressionTicks = ControllerSuppressionTicks; break;
                case "WNG_ReplicatorTitan": suppressionTicks = TitanSuppressionTicks; break;
                case "WNG_ReplicatorSiegeMass": suppressionTicks = SiegeSuppressionTicks; break;
                case "WNG_ReplicatorBulwark": suppressionTicks = BulwarkSuppressionTicks; break;
                default: suppressionTicks = SpecialistSuppressionTicks; break;
            }
            return state.TryApplyCommandSuppression(
                caster,
                Math.Max(600, (int)Math.Round(suppressionTicks * factor)));
        }

        private static float DurationFactor(Pawn caster, Pawn target)
        {
            int peers = Math.Min(MaxPeerBonusCount, Math.Max(0, AsuranCollectiveUtility.CountLocalNetworkPeers(caster)));
            float factor = 1f + peers * PeerDurationBonus;
            if (target.TryGetComp<CompReplicatorAdaptation>()?.Has(ReplicatorAdaptationFlags.Shield) == true)
                factor *= ShieldResistanceFactor;
            return Math.Max(0.1f, factor);
        }

        private static void PresentBestEffort(Pawn target)
        {
            if (target?.Map?.IsPlayerHome != true || target.def?.defName != "WNG_ReplicatorController")
                return;

            try
            {
                Messages.Message(
                    "An Asuran collective has temporarily severed a hostile Replicator Controller's coordination lattice. The swarm remains active.",
                    target,
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Asuran countermeasure committed but presentation failed: " + ex.Message);
            }
        }

        private void PruneCooldowns(int now)
        {
            if (nextUseByPawnId == null)
                nextUseByPawnId = new Dictionary<int, int>();
            if (nextUseByPawnId.Count < 32)
                return;

            HashSet<int> liveCasterIds = new HashSet<int>();
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (IsEligibleNpcCaster(pawn))
                    liveCasterIds.Add(pawn.thingIDNumber);
            }

            List<int> keys = nextUseByPawnId.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                int key = keys[i];
                if (!liveCasterIds.Contains(key) && nextUseByPawnId[key] <= now)
                    nextUseByPawnId.Remove(key);
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
            Scribe_Values.Look(ref nextScanTick, "wngAsuranReplicatorNextScan", 0);
            Scribe_Collections.Look(ref nextUseByPawnId, "wngAsuranReplicatorNpcCooldowns", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                nextScanTick = Math.Max(0, nextScanTick);
                if (nextUseByPawnId == null)
                    nextUseByPawnId = new Dictionary<int, int>();
                List<int> keys = nextUseByPawnId.Keys.ToList();
                for (int i = 0; i < keys.Count; i++)
                    nextUseByPawnId[keys[i]] = Math.Max(0, nextUseByPawnId[keys[i]]);
            }
        }
    }

    public sealed class CompProperties_AbilityAsuranLatticeIntrusion : CompProperties_AbilityEffect
    {
        public int droneOverrideTicks = 15000;
        public int hunterOverrideTicks = 9000;
        public int bulwarkSuppressionTicks = 6000;
        public int titanSuppressionTicks = 4500;
        public int controllerSuppressionTicks = 7500;
        public int siegeSuppressionTicks = 3000;
        public int specialistSuppressionTicks = 6000;
        public float peerDurationBonus = 0.08f;
        public int maxPeerBonusCount = 3;
        public float shieldResistanceFactor = 0.75f;

        public CompProperties_AbilityAsuranLatticeIntrusion()
        {
            compClass = typeof(CompAbilityEffect_AsuranLatticeIntrusion);
        }
    }

    public sealed class CompAbilityEffect_AsuranLatticeIntrusion : CompAbilityEffect
    {
        public new CompProperties_AbilityAsuranLatticeIntrusion Props =>
            (CompProperties_AbilityAsuranLatticeIntrusion)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Pawn targetPawn = target.Pawn;

            if (!CasterCanIntrude(caster))
            {
                if (throwMessages && caster != null)
                    Reject(caster, "Lattice intrusion requires an intact, uncontained Asuran Collective link.");
                return false;
            }

            if (targetPawn == null || targetPawn.Dead || !targetPawn.Spawned || targetPawn.Map != caster.Map ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(targetPawn) || targetPawn.Faction == null ||
                targetPawn.Faction == caster.Faction || !targetPawn.Faction.HostileTo(caster.Faction) ||
                ReplicatorInterferenceUtility.IsEmpDisrupted(targetPawn) ||
                ReplicatorContainmentUtility.IsContained(targetPawn.Map, targetPawn.Position))
            {
                if (throwMessages)
                    Reject(caster, "Lattice intrusion requires a hostile, unsuppressed block Replicator outside containment.");
                return false;
            }

            CompReplicatorTemporaryAsuranState state = targetPawn.TryGetComp<CompReplicatorTemporaryAsuranState>();
            if (state == null)
                return false;
            if (state.ActiveOverride && state.OverrideAuthorityPawn != caster)
            {
                if (throwMessages)
                    Reject(caster, "Another temporary Asuran authority already owns this block's restoration record.");
                return false;
            }
            if (state.CommandSuppressed && state.SuppressingPawn != caster)
            {
                if (throwMessages)
                    Reject(caster, "Another Asuran intrusion is already suppressing this block's command lattice.");
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent?.pawn;
            Pawn targetPawn = target.Pawn;
            if (!CasterCanIntrude(caster) || targetPawn == null || targetPawn.Dead || !targetPawn.Spawned ||
                targetPawn.Map != caster.Map || targetPawn.Faction == null || targetPawn.Faction == caster.Faction ||
                !targetPawn.Faction.HostileTo(caster.Faction) ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(targetPawn) ||
                ReplicatorInterferenceUtility.IsEmpDisrupted(targetPawn) ||
                ReplicatorContainmentUtility.IsContained(targetPawn.Map, targetPawn.Position))
                return;

            CompReplicatorTemporaryAsuranState state = targetPawn.TryGetComp<CompReplicatorTemporaryAsuranState>();
            if (state == null)
                return;

            string defName = targetPawn.def?.defName ?? string.Empty;
            float factor = DurationFactor(caster, targetPawn);
            bool success;
            string outcome;

            if (defName == "WNG_ReplicatorDrone" || defName == "WNG_ReplicatorHunter")
            {
                int baseTicks = defName == "WNG_ReplicatorDrone" ? Props.droneOverrideTicks : Props.hunterOverrideTicks;
                int duration = Math.Max(600, (int)Math.Round(Math.Max(0, baseTicks) * factor));
                success = state.TryApplyOverride(caster, duration);
                outcome = defName == "WNG_ReplicatorDrone"
                    ? "The Drone's command lattice has been temporarily overwritten."
                    : "The Hunter's command lattice has been temporarily overwritten; its exact prior authority will return when the intrusion ends.";
            }
            else
            {
                int duration = Math.Max(600, (int)Math.Round(Math.Max(0, SuppressionTicksFor(defName)) * factor));
                success = state.TryApplyCommandSuppression(caster, duration);
                outcome = defName == "WNG_ReplicatorController"
                    ? "The Controller resisted takeover, but its coordination lattice has been temporarily command-suppressed."
                    : "The larger Replicator resisted takeover, but its replication and coordination functions have been temporarily command-suppressed.";
            }

            if (success && (caster.Faction == Faction.OfPlayer || caster.Map?.IsPlayerHome == true))
                Messages.Message(outcome, targetPawn, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        private int SuppressionTicksFor(string defName)
        {
            switch (defName)
            {
                case "WNG_ReplicatorController":
                    return Props.controllerSuppressionTicks;
                case "WNG_ReplicatorTitan":
                    return Props.titanSuppressionTicks;
                case "WNG_ReplicatorSiegeMass":
                    return Props.siegeSuppressionTicks;
                case "WNG_ReplicatorBulwark":
                    return Props.bulwarkSuppressionTicks;
                default:
                    return Props.specialistSuppressionTicks;
            }
        }

        private float DurationFactor(Pawn caster, Pawn target)
        {
            int peers = Math.Min(
                Math.Max(0, Props.maxPeerBonusCount),
                Math.Max(0, AsuranCollectiveUtility.CountLocalNetworkPeers(caster)));
            float factor = 1f + peers * Math.Max(0f, Props.peerDurationBonus);
            if (target.TryGetComp<CompReplicatorAdaptation>()?.Has(ReplicatorAdaptationFlags.Shield) == true)
                factor *= Math.Max(0.25f, Props.shieldResistanceFactor);
            return Math.Max(0.1f, factor);
        }

        private static bool CasterCanIntrude(Pawn caster)
        {
            return caster != null && !caster.Dead && caster.Spawned && caster.Map != null &&
                   caster.Faction != null && AsuranCollectiveUtility.IsLinked(caster) &&
                   !AsuranCollectiveUtility.IsDisrupted(caster) &&
                   !ReplicatorContainmentUtility.IsContained(caster.Map, caster.Position);
        }

        private static void Reject(Pawn caster, string text)
        {
            if (caster != null)
                Messages.Message(text, caster, MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
