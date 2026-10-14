using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    [StaticConstructorOnStartup]
    public static class WNGGameplayAccessibility
    {
        static WNGGameplayAccessibility()
        {
            int exposed = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def == null || string.IsNullOrEmpty(def.defName) || !def.defName.StartsWith("WNG_", StringComparison.Ordinal))
                    continue;
                if (def.category != ThingCategory.Item && def.category != ThingCategory.Building)
                    continue;
                def.forceDebugSpawnable = true;
                exposed++;
            }
            Log.Message("[WNG] Direct developer spawning enabled for " + exposed + " physical WNG ThingDefs.");
        }
    }
}
