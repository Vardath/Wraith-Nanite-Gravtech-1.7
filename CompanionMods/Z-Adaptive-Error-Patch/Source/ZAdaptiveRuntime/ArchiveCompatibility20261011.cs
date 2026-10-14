using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZAdaptiveRuntime
{
    // Narrow compatibility for the Oct 11 GravTide/Ocean Floor/Saiyan/Jurassic sources.
    // Does not modify saves, pawn ownership, or AI trees.
    [StaticConstructorOnStartup]
    public static class ArchiveCompatibility20261011
    {
        private static bool jurassicTagReported;
        private static bool jurassicFallbackReported;
        private static bool depthsWarmupReported;
        private static readonly string[] Figureheads =
        {
            "Depths/Building/Figurehead_B",
            "Depths/Building/Figurehead_C",
            "Depths/Building/Figurehead_D"
        };

        static ArchiveCompatibility20261011()
        {
            var harmony = new Harmony("vardath.zadaptive.archives.20261011");
            Install("Jurassic caravan pack-animal tags", () =>
            {
                Type owner = AccessTools.TypeByName(
                    "JurassicRimworld.PawnGroupKindWorker_Trader_GenerateCarriers");
                if (owner == null) return;
                MethodInfo prefix = AccessTools.Method(owner, "Prefix");
                MethodInfo vanilla = AccessTools.Method(typeof(PawnGroupKindWorker_Trader),
                    "GenerateCarriers");
                if (prefix == null || vanilla == null)
                {
                    Log.Warning("[Z Adaptive] Jurassic carrier target signature missing; leaving upstream unchanged.");
                    return;
                }

                // Priority.First: initialize missing tradeTags before Jurassic's original
                // Prefix enumerates every pack-animal ThingDef and calls .Any on it.
                harmony.Patch(vanilla,
                    prefix: new HarmonyMethod(typeof(ArchiveCompatibility20261011),
                        nameof(NormalizeJurassicPackTags)) { priority = Priority.First });
                // The Jurassic Prefix can also select no matching dinosaur. Its own
                // filter runs before any carrier is generated, so only this exception
                // can safely fall through to vanilla trader-carrier generation.
                harmony.Patch(prefix,
                    finalizer: new HarmonyMethod(typeof(ArchiveCompatibility20261011),
                        nameof(JurassicCarrierFallback)));
                Log.Message("[Z Adaptive] Jurassic caravan tag guard and selection fallback installed.");
            });

            Install("Ocean Floor figurehead cache", () =>
            {
                if (AccessTools.TypeByName("Depths.LooksDef") == null) return;
                PrewarmFigureheads();
                MethodInfo target = AccessTools.Method(typeof(Game), "FinalizeInit");
                if (target != null)
                    harmony.Patch(target, postfix: new HarmonyMethod(
                        typeof(ArchiveCompatibility20261011), nameof(PrewarmFigureheads)));
                else
                    Log.Warning("[Z Adaptive] Game.FinalizeInit missing; Ocean Floor graphics prewarmed at startup only.");
            });
        }

        private static void Install(string label, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] " + label + " install failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void NormalizeJurassicPackTags(Pawn trader)
        {
            try
            {
                if (trader?.TraderKind == null) return;
                string name = trader.TraderKind.defName;
                if (name != "Caravan_Outlander_Carnivore" &&
                    name != "Caravan_Outlander_Herbivore") return;

                int initialized = 0;
                foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefs)
                {
                    ThingDef race = kind?.race;
                    if (race?.race == null || !race.race.packAnimal ||
                        race.tradeTags != null) continue;
                    // Null and empty mean no explicitly authored tags. This
                    // does not invent a carrier tag or alter Jurassic's matches.
                    race.tradeTags = new List<string>();
                    initialized++;
                }
                if (initialized > 0 && !jurassicTagReported)
                {
                    jurassicTagReported = true;
                    Log.Warning("[Z Adaptive] Jurassic: initialized " + initialized +
                        " null pack-animal tradeTags lists without assigning new tags.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Jurassic trade-tag inspection failed open: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static Exception JurassicCarrierFallback(Exception __exception, ref bool __result)
        {
            if (__exception == null) return null;
            string trace = __exception.StackTrace ?? string.Empty;
            bool missingTags = __exception is NullReferenceException &&
                trace.IndexOf("GenCollection.Any", StringComparison.Ordinal) >= 0;
            bool noCarrier = __exception is InvalidOperationException &&
                trace.IndexOf("RandomElement", StringComparison.Ordinal) >= 0;
            if (!missingTags && !noCarrier) return __exception;

            __result = true; // Let vanilla GenerateCarriers run for this caravan.
            if (!jurassicFallbackReported)
            {
                jurassicFallbackReported = true;
                Log.Warning("[Z Adaptive] Jurassic carrier selection had missing tags " +
                    "or no eligible tagged animal; using normal vanilla carriers for this caravan.");
            }
            return null;
        }

        private static void PrewarmFigureheads()
        {
            if (!UnityData.IsInMainThread) return;
            try
            {
                // Original Ocean Floor 1.6 archive includes all three PNGs.
                // GraphicDatabase returns null for a cache miss on a worker thread.
                // Pre-create normal Graphic_Single entries on the Unity main thread.
                foreach (string path in Figureheads)
                {
                    if (ContentFinder<Texture2D>.Get(path, false) != null)
                        GraphicDatabase.Get<Graphic_Single>(path, ShaderDatabase.Cutout);
                }
                if (!depthsWarmupReported)
                {
                    depthsWarmupReported = true;
                    Log.Message("[Z Adaptive] Ocean Floor figurehead variants prewarmed on Unity main thread.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Ocean Floor figurehead preload failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
