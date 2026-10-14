using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Controller identity is intentionally distinct from faction. Only AutonomousSwarm is actively
    /// assigned by the current WNGv1 slice; the other values reserve the accepted persistent authority
    /// identities so Queen / Neural Lattice / temporary intrusion can extend this state without a
    /// second competing domain component or save-key migration.
    /// </summary>
    public enum ReplicatorControlAuthority
    {
        Unassigned = 0,
        AutonomousSwarm = 1,
        ExactQueen = 2,
        SovereignNeuralLattice = 3,
        TemporaryAsuran = 4,
        CapturedQueenSovereign = 5
    }

    public sealed class CompProperties_ReplicatorDomain : CompProperties
    {
        public CompProperties_ReplicatorDomain()
        {
            compClass = typeof(CompReplicatorDomain);
        }
    }

    /// <summary>
    /// Persistent controller-domain identity. Temporary Asuran intrusion now reuses this same state
    /// owner through AssignExactState; exact Queen and Sovereign Neural Lattice remain separate later
    /// authority layers rather than parallel domain components.
    /// </summary>
    public sealed class CompReplicatorDomain : ThingComp
    {
        private ReplicatorControlAuthority authority = ReplicatorControlAuthority.Unassigned;
        private string domainId;
        private Pawn authorityPawn;

        public ReplicatorControlAuthority Authority => authority;
        public string DomainId => domainId;
        public Pawn AuthorityPawn => authorityPawn;

        public void EnsureAutonomousIdentity()
        {
            if (!string.IsNullOrEmpty(domainId))
                return;

            authority = ReplicatorControlAuthority.AutonomousSwarm;
            domainId = "auto:" + (parent?.thingIDNumber ?? 0);
            authorityPawn = null;
        }

        public void AssignAutonomousDomain(string exactDomainId)
        {
            if (string.IsNullOrWhiteSpace(exactDomainId))
                return;

            authority = ReplicatorControlAuthority.AutonomousSwarm;
            domainId = exactDomainId;
            authorityPawn = null;
        }

        public void AssignExactState(ReplicatorControlAuthority exactAuthority, string exactDomainId, Pawn exactAuthorityPawn)
        {
            if (string.IsNullOrWhiteSpace(exactDomainId))
                return;

            authority = exactAuthority;
            domainId = exactDomainId;
            authorityPawn = exactAuthorityPawn;
        }

        public void CopyFrom(CompReplicatorDomain source)
        {
            if (source == null)
                return;

            source.EnsureAutonomousIdentity();
            authority = source.authority;
            domainId = source.domainId;
            authorityPawn = source.authorityPawn;
        }

        public bool CompatibleWith(CompReplicatorDomain other)
        {
            if (other == null)
                return false;

            EnsureAutonomousIdentity();
            other.EnsureAutonomousIdentity();
            return !string.IsNullOrEmpty(domainId) && domainId == other.domainId;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureAutonomousIdentity();

            // Old saves and direct developer spawns can lack a live hidden-swarm faction instance.
            // Repair that instance and bind only genuinely autonomous block forms to it.
            // Queen/Lattice/temporary-Asuran authorities retain their explicit controller faction.
            ReplicatorDomainUtility.EnsureAutonomousSwarmFaction(parent as Pawn);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref authority, "wngReplicatorControlAuthority", ReplicatorControlAuthority.Unassigned);
            Scribe_Values.Look(ref domainId, "wngReplicatorDomainId");
            Scribe_References.Look(ref authorityPawn, "wngReplicatorAuthorityPawn");

            if (Scribe.mode == LoadSaveMode.PostLoadInit && string.IsNullOrEmpty(domainId))
                EnsureAutonomousIdentity();
        }
    }

    public static class ReplicatorDomainUtility
    {
        private const string SwarmFactionDefName = "WNG_ReplicatorSwarm";
        private static bool creatingSwarmFaction;

        public static Faction EnsureSwarmFaction()
        {
            FactionManager manager = Find.FactionManager;
            FactionDef swarmDef = DefDatabase<FactionDef>.GetNamedSilentFail(SwarmFactionDefName);
            if (manager == null || swarmDef == null)
                return null;

            Faction swarm = manager.FirstFactionOfDef(swarmDef);
            if (swarm != null)
                return swarm;

            if (creatingSwarmFaction)
                return null;

            try
            {
                creatingSwarmFaction = true;
                FactionGenerator.CreateFactionAndAddToManager(swarmDef);
                swarm = manager.FirstFactionOfDef(swarmDef);
                if (swarm == null)
                    Log.Error("[WNG] Replicator swarm FactionDef exists but no live faction instance could be created.");
                else
                    Log.Message("[WNG] Restored missing autonomous Replicator swarm faction for this save.");
                return swarm;
            }
            catch (Exception ex)
            {
                Log.Error("[WNG] Failed to restore the autonomous Replicator swarm faction: " + ex);
                return null;
            }
            finally
            {
                creatingSwarmFaction = false;
            }
        }

        public static Faction EnsureAutonomousSwarmFaction(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || !ReplicatorAssimilationUtility.IsBlockReplicator(pawn))
                return pawn?.Faction;

            CompReplicatorDomain domain = pawn.TryGetComp<CompReplicatorDomain>();
            domain?.EnsureAutonomousIdentity();
            if (domain == null || domain.Authority != ReplicatorControlAuthority.AutonomousSwarm)
                return pawn.Faction;

            // A player-owned block may carry an old AutonomousSwarm domain after
            // recruitment, dev placement or save migration. Never turn a player
            // pawn hostile merely because its domain has not been reclassified.
            // True feral blocks remain assigned to the hidden swarm as before.
            if (pawn.Faction == Faction.OfPlayer)
                return pawn.Faction;

            Faction swarm = EnsureSwarmFaction();
            if (swarm != null && pawn.Faction != swarm)
                pawn.SetFaction(swarm);

            return pawn.Faction;
        }

        public static CompReplicatorDomain Domain(Pawn pawn)
        {
            CompReplicatorDomain domain = pawn?.TryGetComp<CompReplicatorDomain>();
            domain?.EnsureAutonomousIdentity();
            return domain;
        }

        public static string DomainId(Pawn pawn)
        {
            return Domain(pawn)?.DomainId;
        }

        public static bool SameDomain(Pawn a, Pawn b)
        {
            CompReplicatorDomain da = Domain(a);
            CompReplicatorDomain db = Domain(b);
            return da != null && da.CompatibleWith(db) &&
                   ReplicatorSovereignControlUtility.SignalOperationalForCurrentDomain(a) &&
                   ReplicatorSovereignControlUtility.SignalOperationalForCurrentDomain(b);
        }

        public static void CopyDomain(Pawn source, Pawn target)
        {
            if (source == null || target == null)
                return;
            target.TryGetComp<CompReplicatorDomain>()?.CopyFrom(Domain(source));
        }

        public static bool AllSameDomain(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
                return false;

            string expected = null;
            bool any = false;
            foreach (Pawn pawn in pawns)
            {
                if (!ReplicatorSovereignControlUtility.SignalOperationalForCurrentDomain(pawn))
                    return false;
                string id = DomainId(pawn);
                if (string.IsNullOrEmpty(id))
                    return false;
                if (!any)
                {
                    expected = id;
                    any = true;
                }
                else if (id != expected)
                {
                    return false;
                }
            }
            return any;
        }
    }
}
