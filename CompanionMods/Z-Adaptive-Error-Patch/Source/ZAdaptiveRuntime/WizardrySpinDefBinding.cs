using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace ZAdaptiveRuntime
{
    /// <summary>
    /// Late, optional DefOf compatibility for the Wizardry/RimWorld of Magic
    /// FlyingObject_Spinning definition name collision. XML renames only
    /// Wizardry's duplicated ThingDef; its compiled WizardryDefOf field still
    /// has the original C# name and must point to the now uniquely named Def.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WizardrySpinDefBinding
    {
        static WizardrySpinDefBinding()
        {
            try
            {
                ThingDef wizardrySpin =
                    DefDatabase<ThingDef>.GetNamedSilentFail("LotRW_FlyingObject_Spinning");
                ThingDef torannSpin =
                    DefDatabase<ThingDef>.GetNamedSilentFail("FlyingObject_Spinning");

                // Do nothing if either mod is missing, has been updated to
                // solve the collision, or our guarded XML patch did not apply.
                if (wizardrySpin?.thingClass?.FullName != "Wizardry.FlyingObject_Spinning" ||
                    torannSpin?.thingClass?.FullName != "TorannMagic.FlyingObject_Spinning")
                    return;

                Type wizardryDefOf = AccessTools.TypeByName("Wizardry.WizardryDefOf");
                FieldInfo spinField = wizardryDefOf == null ? null :
                    AccessTools.Field(wizardryDefOf, "FlyingObject_Spinning");
                if (spinField == null ||
                    !typeof(ThingDef).IsAssignableFrom(spinField.FieldType))
                {
                    Log.Warning("[Z Adaptive] Wizardry projectile definitions were " +
                        "separated, but the expected WizardryDefOf field was not " +
                        "found; inspect updated Wizardry code.");
                    return;
                }

                spinField.SetValue(null, wizardrySpin);
                Log.Message("[Z Adaptive] A RimWorld of Magic / Wizardry " +
                    "FlyingObject_Spinning collision resolved: TorannMagic's " +
                    "original projectile is retained and WizardryDefOf now " +
                    "references its unique projectile.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Z Adaptive] Wizardry spinning projectile " +
                    "DefOf compatibility failed open: " + ex);
            }
        }
    }
}
