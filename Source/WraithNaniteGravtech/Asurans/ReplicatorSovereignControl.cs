using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorSovereignState : CompProperties
    {
        public CompProperties_ReplicatorSovereignState()
        {
            compClass = typeof(CompReplicatorSovereignState);
        }
    }

    /// <summary>
    /// Persistent reversible sovereignty metadata for Exact Queen and, later, Sovereign Neural
    /// Lattice authority. CompReplicatorDomain remains the single owner of current authority/domain;
    /// this component stores the exact pre-sovereign state required for a valid release.
    /// </summary>
    public sealed class CompReplicatorSovereignState : ThingComp
    {
        private Faction priorFaction;
        private ReplicatorControlAuthority priorAuthority = ReplicatorControlAuthority.Unassigned;
        private string priorDomainId;
        private Pawn priorAuthorityPawn;

        private Faction controlFaction;
        private ReplicatorControlAuthority controlAuthority = ReplicatorControlAuthority.Unassigned;
        private string controlDomainId;
        private Pawn controllerPawn;

        private Pawn Pawn => parent as Pawn;

        public bool HasRecord => controllerPawn != null && controlFaction != null && !string.IsNullOrEmpty(controlDomainId);
        public Faction PriorFaction => priorFaction;
        public ReplicatorControlAuthority PriorAuthority => priorAuthority;
        public string PriorDomainId => priorDomainId;
        public Pawn PriorAuthorityPawn => priorAuthorityPawn;
        public Faction ControlFaction => controlFaction;
        public ReplicatorControlAuthority ControlAuthority => controlAuthority;
        public string ControlDomainId => controlDomainId;
        public Pawn ControllerPawn => controllerPawn;

        public bool StateOwnsNow => HasRecord && ExpectedSovereignStateStillOwns(Pawn);

        public bool SignalOperational
        {
            get
            {
                Pawn pawn = Pawn;
                return StateOwnsNow && ControllerContextValid(pawn, controllerPawn, controlAuthority) &&
                       !SignalSuppressed(pawn, controllerPawn);
            }
        }

        public bool TryAssignExactQueen(Pawn queen)
        {
            return TryAssign(queen, ReplicatorControlAuthority.ExactQueen, "queen:");
        }

        public bool TryAssignSovereignNeuralLattice(Pawn bearer)
        {
            if (!SovereignLatticeUtility.HasInstalledLattice(bearer))
                return false;
            return TryAssign(bearer, ReplicatorControlAuthority.SovereignNeuralLattice, "sovereign-lattice:");
        }

        public bool TryAssignCapturedQueen(Pawn queen, Faction retainingFaction, string exactDomainId)
        {
            Pawn pawn = Pawn;
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || queen == null || queen.Dead ||
                retainingFaction == null || string.IsNullOrWhiteSpace(exactDomainId) || domain == null ||
                !ReplicatorAssimilationUtility.IsBlockReplicator(pawn) ||
                !CapturedQueenSovereignUtility.IsValidRetainedQueen(queen, retainingFaction))
                return false;

            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn) ||
                ReplicatorContainmentUtility.IsContained(pawn.Map, pawn.Position))
                return false;

            CompReplicatorTemporaryAsuranState temporary = pawn.TryGetComp<CompReplicatorTemporaryAsuranState>();
            if (temporary?.HasOverrideRecord == true)
                return false;

            if (HasRecord)
                return controllerPawn == queen && controlFaction == retainingFaction &&
                       controlAuthority == ReplicatorControlAuthority.CapturedQueenSovereign &&
                       controlDomainId == exactDomainId && StateOwnsNow;

            domain.EnsureAutonomousIdentity();
            if (domain.Authority == ReplicatorControlAuthority.ExactQueen ||
                domain.Authority == ReplicatorControlAuthority.SovereignNeuralLattice ||
                domain.Authority == ReplicatorControlAuthority.CapturedQueenSovereign)
                return false;

            Faction savedFaction = pawn.Faction;
            ReplicatorControlAuthority savedAuthority = domain.Authority;
            string savedDomain = domain.DomainId;
            Pawn savedAuthorityPawn = domain.AuthorityPawn;

            try
            {
                if (pawn.Faction != retainingFaction)
                    pawn.SetFaction(retainingFaction);
                domain.AssignExactState(ReplicatorControlAuthority.CapturedQueenSovereign, exactDomainId, queen);

                priorFaction = savedFaction;
                priorAuthority = savedAuthority;
                priorDomainId = savedDomain;
                priorAuthorityPawn = savedAuthorityPawn;
                controlFaction = retainingFaction;
                controlAuthority = ReplicatorControlAuthority.CapturedQueenSovereign;
                controlDomainId = exactDomainId;
                controllerPawn = queen;
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
                    Log.Error("[WNG] Captured-Queen sovereign assignment failed and exact rollback also failed: " + rollbackEx);
                }
                Log.Error("[WNG] Captured-Queen sovereign assignment failed before commit: " + ex);
                return false;
            }
        }

        private bool TryAssign(Pawn controller, ReplicatorControlAuthority authority, string domainPrefix)
        {
            Pawn pawn = Pawn;
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || controller == null ||
                controller.Dead || !controller.Spawned || controller.Map != pawn.Map || controller.Faction == null ||
                domain == null || !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                return false;

            if (authority == ReplicatorControlAuthority.ExactQueen && !ReplicatorQueenUtility.IsExactQueen(controller))
                return false;

            if (ReplicatorInterferenceUtility.IsEmpDisrupted(pawn) || ReplicatorInterferenceUtility.IsEmpDisrupted(controller) ||
                ReplicatorContainmentUtility.IsContained(pawn.Map, pawn.Position) ||
                ReplicatorContainmentUtility.IsContained(controller.Map, controller.Position))
                return false;

            CompReplicatorTemporaryAsuranState temporary = pawn.TryGetComp<CompReplicatorTemporaryAsuranState>();
            if (temporary?.HasOverrideRecord == true)
                return false;

            if (HasRecord)
                return controllerPawn == controller && controlAuthority == authority && StateOwnsNow;

            domain.EnsureAutonomousIdentity();
            if (domain.Authority == ReplicatorControlAuthority.ExactQueen ||
                domain.Authority == ReplicatorControlAuthority.SovereignNeuralLattice ||
                domain.Authority == ReplicatorControlAuthority.CapturedQueenSovereign)
                return false;

            Faction savedFaction = pawn.Faction;
            ReplicatorControlAuthority savedAuthority = domain.Authority;
            string savedDomain = domain.DomainId;
            Pawn savedAuthorityPawn = domain.AuthorityPawn;
            string sovereignDomain = domainPrefix + controller.thingIDNumber;

            try
            {
                if (pawn.Faction != controller.Faction)
                    pawn.SetFaction(controller.Faction);
                domain.AssignExactState(authority, sovereignDomain, controller);

                priorFaction = savedFaction;
                priorAuthority = savedAuthority;
                priorDomainId = savedDomain;
                priorAuthorityPawn = savedAuthorityPawn;
                controlFaction = controller.Faction;
                controlAuthority = authority;
                controlDomainId = sovereignDomain;
                controllerPawn = controller;
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
                    Log.Error("[WNG] Sovereign Replicator assignment failed and exact rollback also failed: " + rollbackEx);
                }
                Log.Error("[WNG] Sovereign Replicator assignment failed before commit: " + ex);
                return false;
            }
        }

        public bool TryReleaseToExactPriorState()
        {
            if (!HasRecord)
                return false;

            // Temporary Asuran state owns the current domain while active and itself promises to
            // restore this exact sovereign state. Do not erase the underlying release snapshot.
            if (IsTemporarilyShadowed())
                return false;

            Pawn pawn = Pawn;
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            if (pawn == null || domain == null || priorFaction == null)
                return false;

            try
            {
                if (pawn.Faction != priorFaction)
                    pawn.SetFaction(priorFaction);
                domain.AssignExactState(priorAuthority, priorDomainId, priorAuthorityPawn);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Sovereign Replicator release failed; exact prior-state snapshot retained for retry: " + ex.Message);
                return false;
            }

            if (pawn.Faction == priorFaction && domain.Authority == priorAuthority &&
                domain.DomainId == priorDomainId && domain.AuthorityPawn == priorAuthorityPawn)
            {
                ClearRecord();
                return true;
            }
            return false;
        }

        public void CopyStateTo(Pawn targetPawn)
        {
            CompReplicatorSovereignState target = targetPawn?.TryGetComp<CompReplicatorSovereignState>();
            if (target == null || !HasRecord)
                return;

            target.priorFaction = priorFaction;
            target.priorAuthority = priorAuthority;
            target.priorDomainId = priorDomainId;
            target.priorAuthorityPawn = priorAuthorityPawn;
            target.controlFaction = controlFaction;
            target.controlAuthority = controlAuthority;
            target.controlDomainId = controlDomainId;
            target.controllerPawn = controllerPawn;
        }

        public bool RestorationStateCompatibleWith(CompReplicatorSovereignState other)
        {
            bool mine = HasRecord;
            bool theirs = other?.HasRecord == true;
            if (mine != theirs)
                return false;
            if (!mine)
                return true;

            return priorFaction == other.priorFaction && priorAuthority == other.priorAuthority &&
                   priorDomainId == other.priorDomainId && priorAuthorityPawn == other.priorAuthorityPawn &&
                   controlFaction == other.controlFaction && controlAuthority == other.controlAuthority &&
                   controlDomainId == other.controlDomainId && controllerPawn == other.controllerPawn;
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || !HasRecord)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (((long)now + pawn.thingIDNumber) % 60L != 0L)
                return;

            if (IsTemporarilyShadowed())
                return;

            if (!ExpectedSovereignStateStillOwns(pawn))
            {
                // A later legitimate authority/domain transition superseded this record.
                ClearRecord();
                return;
            }

            // EMP/containment suppress commands but deliberately do not erase sovereignty.
            // Context loss is different: a dead/lost controller or separation from the target map
            // ends this authority and restores the exact prior state.
            if (!ControllerContextValid(pawn, controllerPawn, controlAuthority))
                TryReleaseToExactPriorState();
        }

        private bool ExpectedSovereignStateStillOwns(Pawn pawn)
        {
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            return pawn != null && domain != null && pawn.Faction == controlFaction &&
                   domain.Authority == controlAuthority && domain.DomainId == controlDomainId &&
                   domain.AuthorityPawn == controllerPawn;
        }

        private bool IsTemporarilyShadowed()
        {
            CompReplicatorTemporaryAsuranState temporary = Pawn?.TryGetComp<CompReplicatorTemporaryAsuranState>();
            return temporary != null && temporary.HasOverrideRecord &&
                   temporary.PriorFaction == controlFaction && temporary.PriorAuthority == controlAuthority &&
                   temporary.PriorDomainId == controlDomainId && temporary.PriorAuthorityPawn == controllerPawn;
        }

        private static bool ControllerContextValid(Pawn target, Pawn controller, ReplicatorControlAuthority authority)
        {
            if (target == null || controller == null || controller.Dead || controller.Faction == null)
                return false;
            if (authority == ReplicatorControlAuthority.ExactQueen)
            {
                if (!ReplicatorQueenUtility.IsExactQueen(controller))
                    return false;
                if (target.Spawned)
                    return controller.Spawned && controller.Map == target.Map;
                return true;
            }
            if (authority == ReplicatorControlAuthority.CapturedQueenSovereign)
                return CapturedQueenSovereignUtility.IsValidRetainedQueen(controller, target.Faction);
            if (authority == ReplicatorControlAuthority.SovereignNeuralLattice)
            {
                if (!SovereignLatticeUtility.HasInstalledLattice(controller))
                    return false;
                if (target.Spawned)
                    return controller.Spawned && controller.Map == target.Map;
                return true;
            }
            if (target.Spawned)
                return controller.Spawned && controller.Map == target.Map;
            return true;
        }

        private static bool SignalSuppressed(Pawn target, Pawn controller)
        {
            if (target == null || controller == null)
                return true;
            if (ReplicatorInterferenceUtility.IsEmpDisrupted(target) ||
                ReplicatorInterferenceUtility.IsEmpDisrupted(controller) ||
                SovereignLatticeUtility.BearerEmpDisrupted(controller))
                return true;
            if (target.Spawned && ReplicatorContainmentUtility.IsContained(target.Map, target.Position))
                return true;
            if (controller.Spawned && ReplicatorContainmentUtility.IsContained(controller.Map, controller.Position))
                return true;
            return false;
        }

        private void ClearRecord()
        {
            priorFaction = null;
            priorAuthority = ReplicatorControlAuthority.Unassigned;
            priorDomainId = null;
            priorAuthorityPawn = null;
            controlFaction = null;
            controlAuthority = ReplicatorControlAuthority.Unassigned;
            controlDomainId = null;
            controllerPawn = null;
        }

        public override string CompInspectStringExtra()
        {
            if (!HasRecord)
                return null;
            string owner = controllerPawn?.LabelShortCap ?? "missing controller";
            string status = SignalOperational ? "active" : "suppressed";
            return "Sovereign Replicator authority: " + controlAuthority + " / " + owner + " (" + status + ").";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref priorFaction, "wngSovereignPriorFaction");
            Scribe_Values.Look(ref priorAuthority, "wngSovereignPriorAuthority", ReplicatorControlAuthority.Unassigned);
            Scribe_Values.Look(ref priorDomainId, "wngSovereignPriorDomainId");
            Scribe_References.Look(ref priorAuthorityPawn, "wngSovereignPriorAuthorityPawn");
            Scribe_References.Look(ref controlFaction, "wngSovereignControlFaction");
            Scribe_Values.Look(ref controlAuthority, "wngSovereignControlAuthority", ReplicatorControlAuthority.Unassigned);
            Scribe_Values.Look(ref controlDomainId, "wngSovereignControlDomainId");
            Scribe_References.Look(ref controllerPawn, "wngSovereignControllerPawn");
        }
    }

    public static class ReplicatorSovereignControlUtility
    {
        public static bool SignalOperationalForCurrentDomain(Pawn pawn)
        {
            CompReplicatorDomain domain = ReplicatorDomainUtility.Domain(pawn);
            if (domain == null)
                return true;
            if (domain.Authority != ReplicatorControlAuthority.ExactQueen &&
                domain.Authority != ReplicatorControlAuthority.SovereignNeuralLattice &&
                domain.Authority != ReplicatorControlAuthority.CapturedQueenSovereign)
                return true;
            return pawn?.TryGetComp<CompReplicatorSovereignState>()?.SignalOperational == true;
        }

        public static bool TransformationStatesCompatible(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
                return false;
            CompReplicatorSovereignState first = null;
            bool any = false;
            foreach (Pawn pawn in pawns)
            {
                CompReplicatorSovereignState state = pawn?.TryGetComp<CompReplicatorSovereignState>();
                if (!any)
                {
                    first = state;
                    any = true;
                    continue;
                }
                if (first == null)
                {
                    if (state?.HasRecord == true)
                        return false;
                }
                else if (!first.RestorationStateCompatibleWith(state))
                    return false;
            }
            return any;
        }

        public static void CopyState(Pawn source, Pawn target)
        {
            source?.TryGetComp<CompReplicatorSovereignState>()?.CopyStateTo(target);
        }
    }
}
