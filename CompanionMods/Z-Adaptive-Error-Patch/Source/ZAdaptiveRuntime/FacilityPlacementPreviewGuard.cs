using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// RimWorld 1.7, Player(20261017-055007).log 15:44:34:
    /// the OPTIONAL facility-link tooltip raised an NRE during building placement.
    /// Preserve valid previews; repair unresolved facility candidate lists if possible.
    /// The finalizer is limited to this placement-preview routine and only a null
    /// exception; it never affects construction, facility gameplay or other UI.
    /// Report the actual ThingDef to make an upstream data fix possible.
    /// Compatible with the 1.6 API, where this exact preview method also exists.
    /// </summary>
    internal static class FacilityPlacementPreviewGuard
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        internal static void Install(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(CompFacility),
                "DrawPlaceMouseAttachmentsToPotentialThingsToLinkTo",
                new[]
                {
                    typeof(float), typeof(float).MakeByRefType(), typeof(ThingDef),
                    typeof(IntVec3), typeof(Rot4), typeof(Map)
                });
            MethodInfo prefix = AccessTools.Method(typeof(FacilityPlacementPreviewGuard), nameof(Prefix));
            MethodInfo finalizer = AccessTools.Method(typeof(FacilityPlacementPreviewGuard), nameof(Finalizer));
            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning("[Z Adaptive] Facility placement preview signature changed; guard not installed.");
                return;
            }

            harmony.Patch(target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                finalizer: new HarmonyMethod(finalizer));
        }

        private static bool Prefix(ThingDef myDef, Map map)
        {
            if (myDef == null || map == null || map.listerThings == null)
            {
                WarnOnce(myDef, "null building definition, map or thing lister; omitted invalid preview");
                return false;
            }

            CompProperties_Facility props = myDef.GetCompProperties<CompProperties_Facility>();
            if (props == null)
            {
                WarnOnce(myDef, "facility-connection preview requested without CompProperties_Facility");
                return false;
            }

            if (props.linkableBuildings == null)
            {
                // ResolveReferences normally supplies this derived list. Some
                // inherited or late-modified defs lack it in mixed-version stacks.
                // Repair the data first rather than hiding the whole preview.
                try
                {
                    props.ResolveReferences(myDef);
                }
                catch (Exception ex)
                {
                    WarnOnce(myDef, "facility link resolution failed: " + ex);
                    return false;
                }
            }

            if (props.linkableBuildings == null)
            {
                WarnOnce(myDef, "facility links remain null after resolution; omitted invalid preview");
                return false;
            }

            int missing = props.linkableBuildings.RemoveAll(def => def == null);
            if (missing > 0)
                WarnOnce(myDef, "removed " + missing + " null facility target definition(s)");

            return true;
        }

        private static Exception Finalizer(Exception __exception, ThingDef myDef)
        {
            if (!(__exception is NullReferenceException))
                return __exception;

            // No gameplay logic runs in this method; the exception is restricted
            // to the optional mouse tooltip. Keep placement usable, record the
            // building and the stack trace, and leave other errors visible.
            WarnOnce(myDef, "facility-link mouse preview threw a null reference; " +
                "placement itself was not blocked. Trace: " + __exception);
            return null;
        }

        private static void WarnOnce(ThingDef def, string issue)
        {
            string name = def == null ? "(null ThingDef)" : (def.defName ?? "(unnamed ThingDef)");
            string key = name + "|" + issue.Split(':')[0];
            if (Reported.Add(key))
                Log.Warning("[Z Adaptive] Facility placement preview for " + name + ": " + issue);
        }
    }
}
