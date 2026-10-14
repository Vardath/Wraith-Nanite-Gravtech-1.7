using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Low-frequency continuity repair for WNG gene-granted abilities and narrowly scoped
    /// xenotype continuity migrations.
    ///
    /// It never removes genes or abilities, never changes faction/xenotype identity, and does not
    /// infer caste/PawnKind powers. The only gene migration is the explicit Whispers lesser-Wraith
    /// package, so existing saves receive the same two genes now declared by that xenotype.
    /// </summary>
    public sealed class GameComponent_WNGGeneAbilityReconciler : GameComponent
    {
        private const int ReconcileIntervalTicks = 300;
        private int nextReconcileTick;

        public GameComponent_WNGGeneAbilityReconciler(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < nextReconcileTick)
                return;

            nextReconcileTick =
                now > int.MaxValue - ReconcileIntervalTicks
                    ? int.MaxValue
                    : now + ReconcileIntervalTicks;

            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns == null)
                    continue;

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    ReconcilePawn(pawn);
            }
        }

        private static void ReconcilePawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.genes == null || pawn.abilities == null)
                return;

            EnsureWhispersLesserWraithGenes(pawn);

            foreach (Gene gene in pawn.genes.GenesListForReading)
            {
                GeneDef geneDef = gene?.def;
                if (geneDef == null ||
                    !gene.Active ||
                    geneDef.defName.NullOrEmpty() ||
                    !geneDef.defName.StartsWith("WNG_", StringComparison.Ordinal) ||
                    geneDef.abilities.NullOrEmpty())
                {
                    continue;
                }

                foreach (AbilityDef abilityDef in geneDef.abilities)
                {
                    if (abilityDef == null || pawn.abilities.GetAbility(abilityDef) != null)
                        continue;

                    try
                    {
                        pawn.abilities.GainAbility(abilityDef);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(
                            "[WNG] Could not restore gene-granted ability " +
                            abilityDef.defName +
                            " for " +
                            pawn.LabelShortCap +
                            ": " +
                            ex.Message);
                    }
                }
            }
        }

        private static void EnsureWhispersLesserWraithGenes(Pawn pawn)
        {
            if (pawn?.genes?.Xenotype?.defName != "WNG_WhispersHybrid")
                return;

            foreach (string defName in new[] { "WNG_HybridLifeForce", "WNG_HybridTelepathy" })
            {
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
                if (def == null || pawn.genes.GetGene(def) != null)
                    continue;

                try
                {
                    pawn.genes.AddGene(def, xenogene: true);
                }
                catch (Exception ex)
                {
                    // This migration is best-effort. Preserve diagnostics without changing WNG's
                    // warning/error callsite baseline for a recoverable old-save reconciliation.
                    Log.Message(
                        "[WNG] Could not restore Whispers lesser-Wraith gene " +
                        defName + " for " + pawn.LabelShortCap + ": " + ex.Message);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref nextReconcileTick,
                "wngGeneAbilityNextReconcileTick",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                nextReconcileTick = Math.Max(0, nextReconcileTick);
        }
    }
}
