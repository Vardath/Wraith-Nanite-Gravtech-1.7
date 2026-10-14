using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Shared identity helpers for the four first-build Wraith lineages.
    /// Vanilla Faction goodwill remains authoritative after the one-time initial
    /// inter-lineage relationships are established.
    /// </summary>
    public static class WraithLineageUtility
    {
        public const string SableBroodDefName = "WNG_WraithSableBrood";
        public const string CinderCourtDefName = "WNG_WraithCinderCourt";
        public const string VeiledHiveDefName = "WNG_WraithVeiledHive";
        public const string PaleCovenantDefName = "WNG_WraithPaleCovenant";

        public const int CinderVeiledInitialGoodwill = -35;
        public const int CinderPaleInitialGoodwill = -65;
        public const int VeiledPaleInitialGoodwill = 25;

        private static readonly HashSet<string> LineageDefNames = new HashSet<string>
        {
            SableBroodDefName,
            CinderCourtDefName,
            VeiledHiveDefName,
            PaleCovenantDefName
        };

        public static bool IsWraithLineage(Faction faction)
        {
            return faction?.def != null && LineageDefNames.Contains(faction.def.defName);
        }

        public static IEnumerable<Faction> ActiveLineages()
        {
            return Find.FactionManager?.AllFactions
                       .Where(f => f != null && !f.defeated && IsWraithLineage(f))
                   ?? Enumerable.Empty<Faction>();
        }

        public static Faction Resolve(string defName)
        {
            if (defName.NullOrEmpty() || Find.FactionManager == null)
                return null;

            return Find.FactionManager.AllFactions
                .FirstOrDefault(f => f != null && !f.defeated && f.def?.defName == defName);
        }

        public static bool TrySetInitialGoodwill(Faction a, Faction b, int targetGoodwill)
        {
            if (a == null || b == null || a == b || a.defeated || b.defeated)
                return false;

            int target = Math.Max(-100, Math.Min(100, targetGoodwill));
            int delta = target - a.BaseGoodwillWith(b);
            if (delta == 0)
                return true;
            if (!a.CanChangeGoodwillFor(b, delta))
                return false;

            return a.TryAffectGoodwillWith(
                b,
                delta,
                canSendMessage: false,
                canSendHostilityLetter: false);
        }
    }

    /// <summary>
    /// Establishes only the mutable Wraith lineages' starting rivalries, once.
    /// Sable Brood is excluded because its FactionDef permanentEnemy flag is the
    /// native authority. After initialization this component never forces goodwill.
    /// </summary>
    public sealed class WraithLineagePoliticsRegistry : GameComponent
    {
        private const int InitializationRetryTicks = 600;
        private bool relationsInitialized;

        public WraithLineagePoliticsRegistry(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (relationsInitialized || Find.TickManager == null)
                return;
            if (Find.TickManager.TicksGame % InitializationRetryTicks != 0)
                return;

            Faction cinder = WraithLineageUtility.Resolve(WraithLineageUtility.CinderCourtDefName);
            Faction veiled = WraithLineageUtility.Resolve(WraithLineageUtility.VeiledHiveDefName);
            Faction pale = WraithLineageUtility.Resolve(WraithLineageUtility.PaleCovenantDefName);
            if (cinder == null || veiled == null || pale == null)
                return;

            bool cinderVeiled = WraithLineageUtility.TrySetInitialGoodwill(
                cinder, veiled, WraithLineageUtility.CinderVeiledInitialGoodwill);
            bool cinderPale = WraithLineageUtility.TrySetInitialGoodwill(
                cinder, pale, WraithLineageUtility.CinderPaleInitialGoodwill);
            bool veiledPale = WraithLineageUtility.TrySetInitialGoodwill(
                veiled, pale, WraithLineageUtility.VeiledPaleInitialGoodwill);

            relationsInitialized = cinderVeiled && cinderPale && veiledPale;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref relationsInitialized, "wngWraithLineageRelationsInitialized", false);
        }
    }
}
