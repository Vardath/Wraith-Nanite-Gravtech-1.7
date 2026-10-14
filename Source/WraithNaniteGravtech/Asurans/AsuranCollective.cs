using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Author-tunable ordinary Asuran/human-form collective coordination. This is deliberately
    /// not a sovereign Replicator-controller contract: it coordinates compatible human-form
    /// minds only. Queen, Sovereign Neural Lattice and Temporary Asuran block-control identities
    /// remain separate systems.
    /// </summary>
    public sealed class AsuranCollectiveSettingsExtension : DefModExtension
    {
        public float localRadius = 35f;
        public int peerBenefitCap = 4;
        public float reconstructionBonusPerPeer = 0.12f;
        public int stateRefreshIntervalTicks = 250;
    }

    public static class AsuranCollectiveUtility
    {
        private const string DisruptionDefName = "WNG_NaniteEMPDisruption";

        public static Gene_AsuranCollectiveLink GetActiveLinkGene(Pawn pawn)
        {
            return pawn?.genes?.GenesListForReading
                ?.OfType<Gene_AsuranCollectiveLink>()
                .FirstOrDefault(gene => gene != null && gene.Active);
        }

        public static bool IsLinked(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && GetActiveLinkGene(pawn) != null;
        }

        /// <summary>
        /// Canonical WNG human-form nanite/Asuran identity predicate. This deliberately
        /// identifies the synthetic body lineage, not current faction allegiance or
        /// collective-link availability, so captured/recruited/player Asurans remain Asurans.
        /// </summary>
        public static bool IsNaniteSynthetic(Pawn pawn)
        {
            if (pawn == null)
                return false;

            if (pawn.genes?.GenesListForReading?.Any(gene =>
                    gene?.def?.defName == "WNG_NaniteBody") == true)
                return true;

            string kind = pawn.kindDef?.defName;
            return kind == "WNG_HumanFormReplicator"
                || kind == "WNG_PlayerHumanFormReplicator"
                || kind == "WNG_HumanFormCopy"
                || kind == "WNG_PrecursorEngineer"
                || kind == "WNG_PrecursorSoldier"
                || kind == "WNG_PrecursorCommander";
        }

        public static bool IsDisrupted(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return true;
            HediffDef disruption = DefDatabase<HediffDef>.GetNamedSilentFail(DisruptionDefName);
            return disruption != null && pawn.health.hediffSet.GetFirstHediffOfDef(disruption) != null;
        }

        public static int CountLocalNetworkPeers(Pawn pawn)
        {
            Gene_AsuranCollectiveLink link = GetActiveLinkGene(pawn);
            if (link == null || pawn.Map == null || pawn.Faction == null || IsDisrupted(pawn))
                return 0;

            float radius = Math.Max(0f, link.Settings.localRadius);
            float radiusSquared = radius * radius;
            bool archiveRelay = HasPoweredArchive(pawn.Map, pawn.Faction);
            int peers = 0;
            foreach (Pawn other in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == null || other == pawn || other.Dead || other.Faction != pawn.Faction)
                    continue;
                if (!IsLinked(other) || IsDisrupted(other))
                    continue;
                if (!archiveRelay && other.Position.DistanceToSquared(pawn.Position) > radiusSquared)
                    continue;
                peers++;
            }
            return peers;
        }


        public static bool HasPoweredArchive(Map map, Faction faction, IntVec3? center = null, float radius = -1f)
        {
            if (map == null || faction == null)
                return false;
            ThingDef archiveDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_AsuranPatternArchive");
            if (archiveDef == null)
                return false;
            float radiusSquared = radius < 0f ? -1f : radius * radius;
            foreach (Thing thing in map.listerThings.ThingsOfDef(archiveDef))
            {
                if (thing == null || thing.Destroyed || !thing.Spawned || thing.Faction != faction)
                    continue;
                if (center.HasValue && radiusSquared >= 0f && thing.Position.DistanceToSquared(center.Value) > radiusSquared)
                    continue;
                CompPowerTrader power = thing.TryGetComp<CompPowerTrader>();
                if (power == null || power.PowerOn)
                    return true;
            }
            return false;
        }

        public static float ReconstructionMultiplier(Pawn pawn)
        {
            Gene_AsuranCollectiveLink link = GetActiveLinkGene(pawn);
            if (link == null || IsDisrupted(pawn))
                return 1f;

            int cap = Math.Max(0, link.Settings.peerBenefitCap);
            int physicalPeers = Math.Min(cap, CountLocalNetworkPeers(pawn));
            float perPeer = Math.Max(0f, link.Settings.reconstructionBonusPerPeer);
            return AsuranIdeologyUtility.ReconstructionMultiplier(pawn, physicalPeers, perPeer);
        }
    }

    /// <summary>
    /// Same-faction local collective link. Nanite Reserve is the sole source of the
    /// Replicator-block fabrication ability, including on custom xenotypes and copied
    /// human-form pawns. The collective link must not grant or remove that ability.
    /// The link owns only human-form coordination state;
    /// it does not change faction, grant block sovereignty, intrude on Replicator domains or
    /// replace the exact Queen/implant/Temporary-Asuran controller contracts.
    /// </summary>
    public sealed class Gene_AsuranCollectiveLink : Gene
    {
        private const string StateDefName = "WNG_AsuranCollectiveState";
        public AsuranCollectiveSettingsExtension Settings =>
            def.GetModExtension<AsuranCollectiveSettingsExtension>() ?? new AsuranCollectiveSettingsExtension();

        public override void PostAdd()
        {
            base.PostAdd();
            RefreshState();
        }

        public override void PostRemove()
        {
            RemoveState();
            base.PostRemove();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || !Active || pawn.health?.hediffSet == null || delta <= 0)
                return;

            int interval = Math.Max(1, Settings.stateRefreshIntervalTicks);
            if (pawn.IsHashIntervalTick(interval, delta))
                RefreshState();
        }

        private void RefreshState()
        {
            if (pawn?.health?.hediffSet == null)
                return;

            HediffDef stateDef = DefDatabase<HediffDef>.GetNamedSilentFail(StateDefName);
            if (stateDef == null)
                return;

            Hediff state = pawn.health.hediffSet.GetFirstHediffOfDef(stateDef);
            if (state == null)
            {
                pawn.health.AddHediff(stateDef);
                state = pawn.health.hediffSet.GetFirstHediffOfDef(stateDef);
            }
            if (state == null)
                return;

            if (AsuranCollectiveUtility.IsDisrupted(pawn) || pawn.Map == null || pawn.Faction == null)
            {
                state.Severity = 0f;
                return;
            }

            int cap = Math.Max(1, Settings.peerBenefitCap);
            int physicalPeers = Math.Min(cap, AsuranCollectiveUtility.CountLocalNetworkPeers(pawn));
            state.Severity = AsuranIdeologyUtility.CollectiveStateSeverity(pawn, physicalPeers);
        }

        private void RemoveState()
        {
            if (pawn?.health?.hediffSet == null)
                return;
            HediffDef stateDef = DefDatabase<HediffDef>.GetNamedSilentFail(StateDefName);
            Hediff state = stateDef == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(stateDef);
            if (state != null)
                pawn.health.RemoveHediff(state);
        }
    }
}
