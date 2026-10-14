using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Final defensive cleanup for WNG-owned construction costs after all defs and optional
    /// integrations have resolved. A dangling costList ThingDef can crash BaseMarketValue,
    /// which in turn can abort third-party bill/recipe enumeration globally.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WNGDefSanitizer
    {
        static WNGDefSanitizer()
        {
            LongEventHandler.ExecuteWhenFinished(SanitizeWngConstructionCosts);
        }

        private static void SanitizeWngConstructionCosts()
        {
            int removed = 0;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def == null ||
                    string.IsNullOrEmpty(def.defName) ||
                    !def.defName.StartsWith("WNG_", StringComparison.Ordinal) ||
                    def.costList == null)
                {
                    continue;
                }

                for (int i = def.costList.Count - 1; i >= 0; i--)
                {
                    ThingDefCountClass entry = def.costList[i];
                    if (entry == null || entry.thingDef == null || entry.count <= 0)
                    {
                        def.costList.RemoveAt(i);
                        removed++;
                    }
                }
            }

            if (removed > 0)
            {
                Log.Warning("[WNG] Removed " + removed +
                    " invalid WNG construction-cost entr" + (removed == 1 ? "y" : "ies") +
                    " after def resolution to protect market-value and bill enumeration.");
            }
        }
    }
}
