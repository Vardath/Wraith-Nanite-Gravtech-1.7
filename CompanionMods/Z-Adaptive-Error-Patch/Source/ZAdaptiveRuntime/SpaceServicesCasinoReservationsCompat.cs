using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// Space Services 1.6 CasinoPatches.Install requests an inherited virtual
    /// reservation method via AccessTools.Method for slot/poker/roulette drivers.
    /// When the returned MethodInfo is not the actual declared implementation,
    /// its OptionalModPatches.PatchIfExists logs a Harmony "only patch implemented
    /// methods" failure and leaves that casino job's reservation unguarded.
    ///
    /// Re-use Space Services' OWN type-gated prefix on vanilla's concrete,
    /// declared JobDriver_SitFacingBuilding reservation implementation.
    /// The handler already returns true for every non-casino driver, and
    /// allows ordinary casino jobs for non-service pawns. No vanilla
    /// reservation or arbitrary gambling behaviour is changed here.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class SpaceServicesCasinoReservationsCompat
    {
        static SpaceServicesCasinoReservationsCompat()
        {
            // Defer until other mods have finished their bootstrap patches.
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        private static void Install()
        {
            try
            {
                if (!ModsConfig.IsActive("jdubarb.spaceservices"))
                    return;

                Type handlers = AccessTools.TypeByName("SpaceServices.CasinoPatchHandlers");
                MethodInfo prefix = handlers == null ? null :
                    AccessTools.Method(handlers, "TryReserveCasinoJobPrefix",
                        new[] { typeof(JobDriver), typeof(bool).MakeByRefType() });

                // DeclaredMethod, rather than inherited AccessTools.Method, is
                // critical: Harmony cannot patch a reflected inherited stub.
                MethodInfo target = AccessTools.DeclaredMethod(
                    typeof(JobDriver_SitFacingBuilding),
                    "TryMakePreToilReservations",
                    new[] { typeof(bool) });

                if (prefix == null || target == null || target.IsAbstract ||
                    target.GetMethodBody() == null)
                {
                    Log.Warning("[Z Adaptive] Space Services casino reservation " +
                        "bridge unavailable: no concrete 1.6 method or type-gated handler. " +
                        "Leaving vanilla behaviour intact.");
                    return;
                }

                // Avoid double invocation when Space Services updates to use
                // the declared method itself in a later upstream release.
                Patches existing = Harmony.GetPatchInfo(target);
                if (existing != null)
                {
                    foreach (Patch patch in existing.Prefixes)
                    {
                        if (patch.PatchMethod == prefix)
                            return;
                    }
                }

                new Harmony("vardath.zadaptive.spaceservices.casino.20261012")
                    .Patch(target, prefix: new HarmonyMethod(prefix));

                Log.Message("[Z Adaptive] Space Services casino reservation guard " +
                    "rerouted to declared JobDriver_SitFacingBuilding." +
                    "TryMakePreToilReservations; ordinary jobs unaffected.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Space Services casino reservation " +
                    "bridge not installed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
