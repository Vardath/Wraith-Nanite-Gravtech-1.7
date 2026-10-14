using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Reconciles the retained Wraith visual contract after pawn generation, styling, gene
    /// replacement or save/load. The xenotype remains the biological authority; this component
    /// only corrects presentation that vanilla generation can still leave outside the intended
    /// Wraith silhouette.
    /// </summary>
    public static class WraithAppearanceUtility
    {
        private static readonly Color NearWhiteHair =
            new Color(0.965f, 0.975f, 0.985f);

        private static HairDef cachedPreferredHair;

        private static readonly string[] LongHints =
        {
            "long", "shoulder", "flow", "length"
        };

        private static readonly string[] StraightHints =
        {
            "straight", "sleek", "smooth"
        };

        private static readonly string[] RejectHairHints =
        {
            "braid", "ponytail", "locks", "dread", "curl",
            "afro", "mohawk", "spike", "bun"
        };

        public static bool IsTrueWraith(Pawn pawn)
        {
            return pawn?.genes?.Xenotype?.defName == "WNG_Wraith";
        }

        public static bool Reconcile(Pawn pawn)
        {
            if (!IsTrueWraith(pawn) || pawn.story == null)
                return false;

            bool changed = false;

            BodyTypeDef body = pawn.story.bodyType;
            if (body == null ||
                body.defName == "Fat" ||
                body.defName == "Hulk")
            {
                BodyTypeDef replacement =
                    DefDatabase<BodyTypeDef>.GetNamedSilentFail("Thin")
                    ?? DefDatabase<BodyTypeDef>.GetNamedSilentFail(
                        pawn.gender == Gender.Female ? "Female" : "Male");

                if (replacement != null && replacement != body)
                {
                    pawn.story.bodyType = replacement;
                    changed = true;
                }
            }

            HairDef preferred = PreferredHair();
            HairDef currentHair = pawn.story.hairDef;
            if (preferred != null &&
                (currentHair == null || !LooksWraithAppropriate(currentHair)))
            {
                pawn.story.hairDef = preferred;
                changed = true;
            }

            Color current = pawn.story.HairColor;
            if (Mathf.Abs(current.r - NearWhiteHair.r) > 0.002f ||
                Mathf.Abs(current.g - NearWhiteHair.g) > 0.002f ||
                Mathf.Abs(current.b - NearWhiteHair.b) > 0.002f)
            {
                pawn.story.HairColor = NearWhiteHair;
                changed = true;
            }

            if (changed)
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();

            return changed;
        }

        private static HairDef PreferredHair()
        {
            if (cachedPreferredHair != null)
                return cachedPreferredHair;

            cachedPreferredHair = DefDatabase<HairDef>.AllDefsListForReading
                .Where(h => h != null && !h.texPath.NullOrEmpty())
                .Select(h => new
                {
                    Hair = h,
                    Text = SearchText(h)
                })
                .Where(x => LongHints.Any(h => Contains(x.Text, h)))
                .Where(x => !RejectHairHints.Any(h => Contains(x.Text, h)))
                .OrderByDescending(x =>
                    StraightHints.Any(h => Contains(x.Text, h)))
                .ThenByDescending(x => Contains(x.Text, "long"))
                .ThenBy(x => x.Hair.defName, StringComparer.Ordinal)
                .Select(x => x.Hair)
                .FirstOrDefault();

            return cachedPreferredHair;
        }

        private static bool LooksWraithAppropriate(HairDef hair)
        {
            if (hair == null)
                return false;

            string text = SearchText(hair);
            return LongHints.Any(h => Contains(text, h)) &&
                   !RejectHairHints.Any(h => Contains(text, h));
        }

        private static string SearchText(HairDef hair)
        {
            return (
                (hair?.defName ?? string.Empty) + " " +
                (hair?.label ?? string.Empty) + " " +
                (hair?.texPath ?? string.Empty))
                .ToLowerInvariant();
        }

        private static bool Contains(string value, string hint)
        {
            return !value.NullOrEmpty() &&
                   value.IndexOf(
                       hint,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Low-frequency map reconciliation only. WNG does not enumerate every world pawn or mutate
    /// non-Wraith/hybrid pawns. Off-map Wraith are corrected when they next become spawned.
    /// </summary>
    public sealed class GameComponent_WraithAppearanceReconciler : GameComponent
    {
        private const int ReconcileIntervalTicks = 300;
        private int nextReconcileTick;

        public GameComponent_WraithAppearanceReconciler(Game game) { }

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
                    WraithAppearanceUtility.Reconcile(pawn);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref nextReconcileTick,
                "wngWraithAppearanceNextReconcileTick",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                nextReconcileTick = Math.Max(0, nextReconcileTick);
        }
    }
}
