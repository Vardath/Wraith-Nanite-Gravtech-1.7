using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    internal enum WNGExternalDefOrigin
    {
        None,
        Onac,
        RimGateJaffa
    }

    /// <summary>
    /// Narrow optional-mod bridge for the real ONAC + RimGate Biotech Goa'uld/Jaffa ecosystem.
    /// WNG never creates or mutates their factions, xenotypes or pawn kinds; it only recognizes
    /// exact verified package/Def identities so WNG-owned craft can interoperate with them.
    /// </summary>
    internal static class GoauldOptionalInterop
    {
        public const string OnacPackageId = "idolord.onac";
        public const string RimGateJaffaPackageId = "cravemode.rimgatejaffakreebiotech";

        public const string JaffaXenotypeDefName = "JKB_Jaffa";
        public const string JaffaFirstPrimeXenotypeDefName = "JKB_JaffaFirstPrime";
        public const string JaffaPouchGeneDefName = "JKB_JaffaPouch";
        public const string ApophisFactionDefName = "JKB_JaffaApophis";
        public const string AnubisFactionDefName = "JKB_JaffaAnubis";
        public const string RaFactionDefName = "JKB_JaffaRa";

        private static readonly HashSet<string> SystemLordFactionDefNames = new HashSet<string>(StringComparer.Ordinal)
        {
            ApophisFactionDefName,
            AnubisFactionDefName,
            RaFactionDefName
        };

        public static bool OnacLoaded()
        {
            return PackageActive(OnacPackageId);
        }

        public static bool RimGateJaffaLoaded()
        {
            return PackageActive(RimGateJaffaPackageId);
        }

        public static bool FullEcosystemActive()
        {
            return OnacLoaded() && RimGateJaffaLoaded();
        }

        public static WNGExternalDefOrigin OriginOf(Def def)
        {
            if (def == null || def.modContentPack == null)
                return WNGExternalDefOrigin.None;
            if (OnacLoaded() && PackageMatches(def.modContentPack, OnacPackageId))
                return WNGExternalDefOrigin.Onac;
            if (RimGateJaffaLoaded() && PackageMatches(def.modContentPack, RimGateJaffaPackageId))
                return WNGExternalDefOrigin.RimGateJaffa;
            return WNGExternalDefOrigin.None;
        }

        public static bool IsOnacEcosystemFaction(Faction faction)
        {
            if (!FullEcosystemActive() || faction == null || faction.defeated || faction.def == null)
                return false;
            WNGExternalDefOrigin origin = OriginOf(faction.def);
            return origin == WNGExternalDefOrigin.Onac || origin == WNGExternalDefOrigin.RimGateJaffa;
        }

        public static IEnumerable<Faction> ActiveOnacEcosystemFactions()
        {
            if (!FullEcosystemActive() || Find.FactionManager == null)
                return Enumerable.Empty<Faction>();
            return Find.FactionManager.AllFactions
                .Where(IsOnacEcosystemFaction)
                .OrderBy(f => f.loadID);
        }

        public static bool IsSystemLordFaction(Faction faction)
        {
            if (!RimGateJaffaLoaded() || faction == null || faction.defeated || faction.def == null)
                return false;
            return SystemLordFactionDefNames.Contains(faction.def.defName) &&
                   OriginOf(faction.def) == WNGExternalDefOrigin.RimGateJaffa;
        }

        public static IEnumerable<Faction> ActiveSystemLordFactions()
        {
            if (!RimGateJaffaLoaded() || Find.FactionManager == null)
                return Enumerable.Empty<Faction>();
            return Find.FactionManager.AllFactions.Where(IsSystemLordFaction).OrderBy(f => f.loadID);
        }

        public static bool IsJaffa(Pawn pawn)
        {
            if (!RimGateJaffaLoaded() || pawn == null || pawn.Dead)
                return false;

            string xenotype = pawn.genes?.Xenotype?.defName;
            if (xenotype == JaffaXenotypeDefName || xenotype == JaffaFirstPrimeXenotypeDefName)
                return true;

            return pawn.genes?.GenesListForReading?.Any(g =>
                g?.def?.defName == JaffaPouchGeneDefName && g.Active) == true;
        }

        public static List<PawnKindDef> CombatJaffaKinds(Faction faction)
        {
            List<PawnKindDef> result = new List<PawnKindDef>();
            if (!IsSystemLordFaction(faction) || faction.def?.pawnGroupMakers == null)
                return result;

            foreach (PawnGroupMaker maker in faction.def.pawnGroupMakers)
            {
                if (maker == null || maker.kindDef != PawnGroupKindDefOf.Combat || maker.options == null)
                    continue;

                foreach (PawnGenOption option in maker.options)
                {
                    PawnKindDef kind = option?.kind;
                    if (kind == null || kind.race?.race?.Humanlike != true ||
                        OriginOf(kind) != WNGExternalDefOrigin.RimGateJaffa)
                        continue;
                    if (!result.Contains(kind))
                        result.Add(kind);
                }
            }

            PawnKindDef basic = faction.def.basicMemberKind;
            if (result.Count == 0 && basic != null && basic.race?.race?.Humanlike == true &&
                OriginOf(basic) == WNGExternalDefOrigin.RimGateJaffa)
                result.Add(basic);

            return result.OrderBy(k => k.defName).ToList();
        }

        public static Pawn GenerateExactJaffa(Faction faction, List<PawnKindDef> kinds)
        {
            if (faction == null || kinds == null || kinds.Count == 0)
                return null;

            Pawn pawn = PawnGenerator.GeneratePawn(kinds.RandomElement(), faction);
            if (pawn == null)
                return null;
            if (IsJaffa(pawn))
                return pawn;

            if (!pawn.Destroyed)
                pawn.Destroy(DestroyMode.Vanish);
            return null;
        }

        private static bool PackageActive(string id)
        {
            return ModsConfig.ActiveModsInLoadOrder.Any(m => PackageMatches(m, id));
        }

        private static bool PackageMatches(ModMetaData mod, string id)
        {
            return mod != null &&
                   (string.Equals(mod.PackageId, id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mod.PackageIdPlayerFacing, id, StringComparison.OrdinalIgnoreCase));
        }

        private static bool PackageMatches(ModContentPack mod, string id)
        {
            return mod != null &&
                   (string.Equals(mod.PackageId, id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mod.PackageIdPlayerFacing, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
