using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Stable passive identity surface for separate optional mods.
    ///
    /// This API exposes only WNG-owned identity facts and primitive/string tokens. It never changes
    /// faction relations, Pawn state, controller domains, jobs, research or ownership. External mods
    /// remain responsible for identifying their own content and for every policy decision they make.
    ///
    /// API v2 updates the historical surface to the current four-lineage Wraith identities and the
    /// current exact block/human-form/precursor distinction while keeping reflection-friendly method
    /// names from v1 where their semantics still make sense.
    /// </summary>
    public static class WNGExternalCompatibilityApi
    {
        public const int ApiVersion = 2;
        public const string WngPackageId = "vardath.wraithnanitegravtech";

        public static bool IsWngOwnedDef(Def def)
        {
            string packageId = def?.modContentPack?.PackageId;
            return !packageId.NullOrEmpty() &&
                   string.Equals(
                       packageId,
                       WngPackageId,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWraithPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;

            if (pawn.genes?.Xenotype?.defName == "WNG_Wraith")
                return true;

            return HasActiveGene(
                pawn,
                "WNG_LifeForceMetabolism");
        }

        public static bool IsWraithHiveFaction(Faction faction)
        {
            return WraithLineageUtility.IsWraithLineage(faction);
        }

        public static string GetWraithLineageToken(Faction faction)
        {
            switch (faction?.def?.defName)
            {
                case WraithLineageUtility.SableBroodDefName:
                    return "wraith.sable";
                case WraithLineageUtility.CinderCourtDefName:
                    return "wraith.cinder";
                case WraithLineageUtility.VeiledHiveDefName:
                    return "wraith.veiled";
                case WraithLineageUtility.PaleCovenantDefName:
                    return "wraith.pale";
                default:
                    return string.Empty;
            }
        }

        public static bool IsExactReplicatorQueen(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   ReplicatorQueenUtility.IsExactQueen(pawn);
        }

        public static bool IsBlockReplicatorPawn(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   ReplicatorAssimilationUtility.IsBlockReplicator(pawn);
        }

        public static bool IsHumanFormReplicatorPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;

            if (pawn.genes?.Xenotype?.defName == "WNG_HumanFormReplicator")
                return true;

            string kind = pawn.kindDef?.defName;
            return kind == "WNG_HumanFormReplicator" ||
                   kind == "WNG_PlayerHumanFormReplicator" ||
                   kind == "WNG_HumanFormCopy";
        }

        public static bool IsPrecursorSyntheticPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;

            if (pawn.genes?.Xenotype?.defName == "WNG_NanitePrecursor")
                return true;

            string kind = pawn.kindDef?.defName;
            return kind == "WNG_PrecursorEngineer" ||
                   kind == "WNG_PrecursorSoldier" ||
                   kind == "WNG_PrecursorCommander";
        }

        public static bool IsReplicatorPawn(Pawn pawn)
        {
            return IsBlockReplicatorPawn(pawn) ||
                   IsHumanFormReplicatorPawn(pawn) ||
                   IsExactReplicatorQueen(pawn);
        }

        public static bool IsNaniteSyntheticPawn(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   AsuranCollectiveUtility.IsNaniteSynthetic(pawn);
        }

        public static bool IsAutonomousReplicatorFaction(Faction faction)
        {
            return faction?.def?.defName ==
                   "WNG_ReplicatorSwarm";
        }

        public static bool IsHumanFormSyntheticFaction(Faction faction)
        {
            string defName = faction?.def?.defName;
            return defName == "WNG_PrecursorCollective" ||
                   defName == "WNG_HumanFormEnclave";
        }

        public static string GetFactionIdentityToken(Faction faction)
        {
            string lineage =
                GetWraithLineageToken(faction);
            if (!lineage.NullOrEmpty())
                return lineage;

            switch (faction?.def?.defName)
            {
                case "WNG_ReplicatorSwarm":
                    return "replicator.swarm";
                case "WNG_PrecursorCollective":
                    return "synthetic.collective.hostile";
                case "WNG_HumanFormEnclave":
                    return "synthetic.enclave.coexistence";
                default:
                    return string.Empty;
            }
        }

        public static string GetPawnIdentityToken(Pawn pawn)
        {
            if (IsExactReplicatorQueen(pawn))
                return "replicator.queen";
            if (IsBlockReplicatorPawn(pawn))
                return "replicator.block";
            if (IsHumanFormReplicatorPawn(pawn))
                return "replicator.human-form";
            if (IsPrecursorSyntheticPawn(pawn))
                return "synthetic.precursor";
            if (IsWraithPawn(pawn))
                return "wraith";
            if (IsNaniteSyntheticPawn(pawn))
                return "synthetic.nanite";
            return string.Empty;
        }

        private static bool HasActiveGene(
            Pawn pawn,
            string defName)
        {
            return pawn?.genes?.GenesListForReading?.Any(gene =>
                gene?.def?.defName == defName &&
                gene.Active) == true;
        }
    }
}
