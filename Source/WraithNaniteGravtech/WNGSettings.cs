using System;
using System.Reflection;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    public class WNGSettings : ModSettings
    {
        public int livingForgeRawMeatCost = 20;
        public int livingForgeBiomassYield = 16;
        public int livingForgeBiomassWorkAmount = 900;
        public int wraithGravEngineBiomassCost = 120;
        public int wraithGravEngineSeedWorkAmount = 7000;
        public float wraithGravEngineIncubationDays = 1f;
        public float livingForgeIncubationDays = 1f;
        public float wraithLivingEquipmentMaturationDays = 1f;
        public int wraithBioelectricPowerOutput = 1400;
        public float wraithBioelectricBiomassPerDay = 18f;
        public int wraithLivingPowerCellCapacity = 750;
        public int hostileReplicatorCap = 120;
        public float childsToyFeralDelayDays = 1f;
        public bool enableReplicatorStoryEvents = true;
        public float replicatorQueenRecurringRecoveryDays = 4f;
        public int sovereignLatticeControlCap = 30;

        // Restored settings only where a matching rebuilt mechanic still exists.
        public float lifeDrainVictimYears = 50f;
        public float lifeDrainRejuvenationYears = 5f;
        public float minimumWraithAgeYears = 18f;
        public float partialFeedVictimYears = 10f;
        public float partialFeedRejuvenationYears = 1f;
        public float partialFeedLifeForceGain = 0.34f;

        public bool replicatorTerrainAssimilation = true;
        public bool replicatorRoofAssimilation = true;
        public bool replicatorMaterialAdaptation = true;
        public float replicatorBiologicalPredationThreshold = 0.95f;

        public bool humanFormCopying = true;
        public float humanFormCopyCost = 0.60f;
        public float asuranBlockFabricationCost = 0.50f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref livingForgeRawMeatCost, "livingForgeRawMeatCost", 20);
            Scribe_Values.Look(ref livingForgeBiomassYield, "livingForgeBiomassYield", 16);
            Scribe_Values.Look(ref livingForgeBiomassWorkAmount, "livingForgeBiomassWorkAmount", 900);
            Scribe_Values.Look(ref wraithGravEngineBiomassCost, "wraithGravEngineBiomassCost", 120);
            Scribe_Values.Look(ref wraithGravEngineSeedWorkAmount, "wraithGravEngineSeedWorkAmount", 7000);
            Scribe_Values.Look(ref wraithGravEngineIncubationDays, "wraithGravEngineIncubationDays", 1f);
            Scribe_Values.Look(ref livingForgeIncubationDays, "livingForgeIncubationDays", 1f);
            Scribe_Values.Look(ref wraithLivingEquipmentMaturationDays, "wraithLivingEquipmentMaturationDays", 1f);
            Scribe_Values.Look(ref wraithBioelectricPowerOutput, "wraithBioelectricPowerOutput", 1400);
            Scribe_Values.Look(ref wraithBioelectricBiomassPerDay, "wraithBioelectricBiomassPerDay", 18f);
            Scribe_Values.Look(ref wraithLivingPowerCellCapacity, "wraithLivingPowerCellCapacity", 750);
            Scribe_Values.Look(ref hostileReplicatorCap, "hostileReplicatorCap", 120);
            Scribe_Values.Look(ref childsToyFeralDelayDays, "childsToyFeralDelayDays", 1f);
            Scribe_Values.Look(ref enableReplicatorStoryEvents, "enableReplicatorStoryEvents", true);
            Scribe_Values.Look(ref replicatorQueenRecurringRecoveryDays, "replicatorQueenRecurringRecoveryDays", 4f);
            Scribe_Values.Look(ref sovereignLatticeControlCap, "sovereignLatticeControlCap", 30);

            // Preserve the original pre-rebuild save keys for settings that have returned.
            Scribe_Values.Look(ref lifeDrainVictimYears, "lifeDrainVictimYears", 50f);
            Scribe_Values.Look(ref lifeDrainRejuvenationYears, "lifeDrainRejuvenationYears", 5f);
            Scribe_Values.Look(ref minimumWraithAgeYears, "minimumWraithAgeYears", 18f);
            Scribe_Values.Look(ref partialFeedVictimYears, "partialFeedVictimYears", 10f);
            Scribe_Values.Look(ref partialFeedRejuvenationYears, "partialFeedRejuvenationYears", 1f);
            Scribe_Values.Look(ref partialFeedLifeForceGain, "partialFeedLifeForceGain", 0.34f);

            Scribe_Values.Look(ref replicatorTerrainAssimilation, "replicatorTerrainAssimilation", true);
            Scribe_Values.Look(ref replicatorRoofAssimilation, "replicatorRoofAssimilation", true);
            Scribe_Values.Look(ref replicatorMaterialAdaptation, "replicatorMaterialAdaptation", true);
            Scribe_Values.Look(ref replicatorBiologicalPredationThreshold, "replicatorBiologicalPredationThreshold", 0.95f);

            Scribe_Values.Look(ref humanFormCopying, "humanFormCopying", true);
            Scribe_Values.Look(ref humanFormCopyCost, "humanFormCopyCost", 0.60f);
            Scribe_Values.Look(ref asuranBlockFabricationCost, "asuranBlockFabricationCost", 0.50f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ClampValues();
        }

        internal void ClampValues()
        {
            livingForgeRawMeatCost = Mathf.Clamp(livingForgeRawMeatCost, 1, 500);
            livingForgeBiomassYield = Mathf.Clamp(livingForgeBiomassYield, 1, 500);
            livingForgeBiomassWorkAmount = Mathf.Clamp(livingForgeBiomassWorkAmount, 100, 100000);
            wraithGravEngineBiomassCost = Mathf.Clamp(wraithGravEngineBiomassCost, 1, 1000);
            wraithGravEngineSeedWorkAmount = Mathf.Clamp(wraithGravEngineSeedWorkAmount, 100, 100000);
            wraithGravEngineIncubationDays = Mathf.Clamp(wraithGravEngineIncubationDays, 0.1f, 10f);
            livingForgeIncubationDays = Mathf.Clamp(livingForgeIncubationDays, 0.1f, 10f);
            wraithLivingEquipmentMaturationDays = Mathf.Clamp(wraithLivingEquipmentMaturationDays, 0.1f, 10f);
            wraithBioelectricPowerOutput = Mathf.Clamp(wraithBioelectricPowerOutput, 100, 10000);
            wraithBioelectricBiomassPerDay = Mathf.Clamp(wraithBioelectricBiomassPerDay, 0.1f, 100f);
            wraithLivingPowerCellCapacity = Mathf.Clamp(wraithLivingPowerCellCapacity, 100, 10000);
            hostileReplicatorCap = Mathf.Clamp(hostileReplicatorCap, 20, 300);
            childsToyFeralDelayDays = Mathf.Clamp(childsToyFeralDelayDays, 0.1f, 5f);
            replicatorQueenRecurringRecoveryDays = Mathf.Clamp(replicatorQueenRecurringRecoveryDays, 1f, 10f);
            sovereignLatticeControlCap = Mathf.Clamp(sovereignLatticeControlCap, 20, 50);

            lifeDrainVictimYears = Mathf.Clamp(lifeDrainVictimYears, 1f, 100f);
            lifeDrainRejuvenationYears = Mathf.Clamp(lifeDrainRejuvenationYears, 0f, 25f);
            minimumWraithAgeYears = Mathf.Clamp(minimumWraithAgeYears, 0f, 50f);
            partialFeedVictimYears = Mathf.Clamp(partialFeedVictimYears, 0f, 50f);
            partialFeedRejuvenationYears = Mathf.Clamp(partialFeedRejuvenationYears, 0f, 10f);
            partialFeedLifeForceGain = Mathf.Clamp01(partialFeedLifeForceGain);

            replicatorBiologicalPredationThreshold = Mathf.Clamp(replicatorBiologicalPredationThreshold, 0.50f, 1f);
            humanFormCopyCost = Mathf.Clamp(humanFormCopyCost, 0.10f, 1f);
            asuranBlockFabricationCost = Mathf.Clamp01(asuranBlockFabricationCost);
        }

        internal void ResetDefaults()
        {
            livingForgeRawMeatCost = 20;
            livingForgeBiomassYield = 16;
            livingForgeBiomassWorkAmount = 900;
            wraithGravEngineBiomassCost = 120;
            wraithGravEngineSeedWorkAmount = 7000;
            wraithGravEngineIncubationDays = 1f;
            livingForgeIncubationDays = 1f;
            wraithLivingEquipmentMaturationDays = 1f;
            wraithBioelectricPowerOutput = 1400;
            wraithBioelectricBiomassPerDay = 18f;
            wraithLivingPowerCellCapacity = 750;

            hostileReplicatorCap = 120;
            childsToyFeralDelayDays = 1f;
            enableReplicatorStoryEvents = true;
            replicatorQueenRecurringRecoveryDays = 4f;
            sovereignLatticeControlCap = 30;

            lifeDrainVictimYears = 50f;
            lifeDrainRejuvenationYears = 5f;
            minimumWraithAgeYears = 18f;
            partialFeedVictimYears = 10f;
            partialFeedRejuvenationYears = 1f;
            partialFeedLifeForceGain = 0.34f;

            replicatorTerrainAssimilation = true;
            replicatorRoofAssimilation = true;
            replicatorMaterialAdaptation = true;
            replicatorBiologicalPredationThreshold = 0.95f;

            humanFormCopying = true;
            humanFormCopyCost = 0.60f;
            asuranBlockFabricationCost = 0.50f;
            ClampValues();
        }
    }

    [Obsolete("Compatibility alias for pre-rebuild settings saves.")]
    public sealed class WNGModSettings : WNGSettings
    {
    }

    public sealed class WNGMod : Mod
    {
        internal static WNGSettings Settings;
        private Vector2 settingsScroll;

        public WNGMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WNGSettings>();
            Settings?.ClampValues();
            LongEventHandler.ExecuteWhenFinished(WNGSettingsUtility.ApplyRuntimeDefSettings);
        }

        public override string SettingsCategory() => "Wraith & Nanite Gravtech";

        public override void WriteSettings()
        {
            Settings?.ClampValues();
            WNGSettingsUtility.ApplyRuntimeDefSettings();
            base.WriteSettings();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            WNGSettings settings = Settings;
            if (settings == null)
                return;

            const float contentHeight = 2110f;
            Rect view = new Rect(0f, 0f, Math.Max(100f, inRect.width - 18f), contentHeight);
            Widgets.BeginScrollView(inRect, ref settingsScroll, view);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(view);

            Heading(listing, "Wraith living technology");
            settings.livingForgeIncubationDays = DrawDaysSlider(listing, "Living Forge host incubation", settings.livingForgeIncubationDays);
            settings.livingForgeRawMeatCost = DrawIntSlider(listing, "Living Forge biomass raw-meat cost", settings.livingForgeRawMeatCost, 1, 100, 1);
            settings.livingForgeBiomassYield = DrawIntSlider(listing, "Living Forge biomass yield", settings.livingForgeBiomassYield, 1, 100, 1);
            settings.livingForgeBiomassWorkAmount = DrawIntSlider(listing, "Living Forge biomass work", settings.livingForgeBiomassWorkAmount, 100, 10000, 100);
            settings.wraithGravEngineBiomassCost = DrawIntSlider(listing, "Wraith grav-engine seed biomass cost", settings.wraithGravEngineBiomassCost, 1, 500, 1);
            settings.wraithGravEngineSeedWorkAmount = DrawIntSlider(listing, "Wraith grav-engine seed work", settings.wraithGravEngineSeedWorkAmount, 100, 20000, 100);
            settings.wraithGravEngineIncubationDays = DrawDaysSlider(listing, "Wraith grav-engine host incubation", settings.wraithGravEngineIncubationDays);
            settings.wraithLivingEquipmentMaturationDays = DrawDaysSlider(listing, "Wraith living-equipment maturation", settings.wraithLivingEquipmentMaturationDays);
            settings.wraithBioelectricPowerOutput = DrawIntSlider(listing, "Wraith bioelectric-organ power output", settings.wraithBioelectricPowerOutput, 100, 5000, 100);
            settings.wraithBioelectricBiomassPerDay = DrawFloatSlider(listing, "Wraith bioelectric-organ biomass/day", settings.wraithBioelectricBiomassPerDay, 0.5f, 60f, 0.5f);
            settings.wraithLivingPowerCellCapacity = DrawIntSlider(listing, "Wraith living-power-cell capacity", settings.wraithLivingPowerCellCapacity, 100, 5000, 50);

            listing.GapLine();
            Heading(listing, "Wraith feeding");
            settings.lifeDrainVictimYears = DrawFloatSlider(listing, "Deep feeding victim aging (years)", settings.lifeDrainVictimYears, 1f, 100f, 1f);
            settings.lifeDrainRejuvenationYears = DrawFloatSlider(listing, "Deep feeding Wraith rejuvenation (years)", settings.lifeDrainRejuvenationYears, 0f, 25f, 1f);
            settings.minimumWraithAgeYears = DrawFloatSlider(listing, "Minimum Wraith biological age (years)", settings.minimumWraithAgeYears, 0f, 50f, 1f);
            settings.partialFeedVictimYears = DrawFloatSlider(listing, "Partial feeding victim aging (years)", settings.partialFeedVictimYears, 0f, 50f, 1f);
            settings.partialFeedRejuvenationYears = DrawFloatSlider(listing, "Partial feeding Wraith rejuvenation (years)", settings.partialFeedRejuvenationYears, 0f, 10f, 0.5f);
            settings.partialFeedLifeForceGain = DrawPercentSlider(listing, "Partial feeding Life Force restored", settings.partialFeedLifeForceGain, 0f, 1f, 0.01f);

            listing.GapLine();
            Heading(listing, "Block Replicators");
            listing.CheckboxLabeled(
                "Enable Replicator storyteller events",
                ref settings.enableReplicatorStoryEvents,
                "Controls WNG storyteller-driven block-Replicator discoveries and outbreak-style events.");
            settings.hostileReplicatorCap = DrawIntSlider(listing, "Maximum hostile block Replicators per map", settings.hostileReplicatorCap, 20, 300, 5);
            listing.CheckboxLabeled(
                "Assimilate floors, foundations and ground",
                ref settings.replicatorTerrainAssimilation,
                "Restored from the older menu. When disabled, autonomous block Replicators still consume ordinary items, plants and buildings, but do not strip terrain; the terrain-stripping biological-predation phase is therefore disabled.");
            listing.CheckboxLabeled(
                "Assimilate roofs",
                ref settings.replicatorRoofAssimilation,
                "Restored from the older menu. When disabled, roofs are not directly consumed and ordinary RimWorld roof-collapse behavior is left intact.");
            listing.CheckboxLabeled(
                "Enable material adaptation",
                ref settings.replicatorMaterialAdaptation,
                "Restored from the older menu. When disabled, newly consumed material does not teach Material adaptation or alter newly produced Drone material grade.");
            settings.replicatorBiologicalPredationThreshold = DrawPercentSlider(
                listing,
                "Map stripping required before biological predation",
                settings.replicatorBiologicalPredationThreshold,
                0.50f,
                1f,
                0.01f);
            settings.childsToyFeralDelayDays = DrawFloatSlider(listing, "Child's Toy uncontrolled feral delay (days)", settings.childsToyFeralDelayDays, 0.1f, 5f, 0.1f);

            listing.GapLine();
            Heading(listing, "Replicator Queen story pacing");
            settings.replicatorQueenRecurringRecoveryDays = DrawFloatSlider(listing, "Queen recovery retry cadence", settings.replicatorQueenRecurringRecoveryDays, 1f, 10f, 0.5f);

            listing.GapLine();
            Heading(listing, "Human-form Replicators / Asurans");
            listing.CheckboxLabeled(
                "Allow Neural Interface human-form copying",
                ref settings.humanFormCopying,
                "Restored from the older menu. Other Neural Interface operations remain available when reconstruction is disabled.");
            settings.humanFormCopyCost = DrawPercentSlider(
                listing,
                "Nanite Reserve cost to build a human-form copy",
                settings.humanFormCopyCost,
                0.10f,
                1f,
                0.01f);
            settings.asuranBlockFabricationCost = DrawPercentSlider(
                listing,
                "Nanite Reserve cost to create 25 Replicator Blocks",
                settings.asuranBlockFabricationCost,
                0f,
                1f,
                0.01f);

            listing.GapLine();
            Heading(listing, "Sovereign Neural Lattice");
            settings.sovereignLatticeControlCap = DrawIntSlider(listing, "Maximum controlled block Replicators per implanted bearer", settings.sovereignLatticeControlCap, 20, 50, 1);

            listing.GapLine();
            Heading(listing, "Advanced");
            if (listing.ButtonText("Reset all WNG settings to intended defaults"))
                settings.ResetDefaults();
            listing.Label("Defaults preserve the current WNG progression and ecology. Older settings are restored only where the rebuilt mod still has a real matching mechanic.");

            listing.End();
            Widgets.EndScrollView();

            settings.ClampValues();
            WNGSettingsUtility.ApplyRuntimeDefSettings();
        }

        private static void Heading(Listing_Standard listing, string text)
        {
            Text.Font = GameFont.Medium;
            listing.Label(text);
            Text.Font = GameFont.Small;
        }

        private static float DrawDaysSlider(Listing_Standard listing, string label, float value)
        {
            listing.Label(label + ": " + value.ToString("0.0") + " days");
            float raw = listing.Slider(value, 0.1f, 10f);
            return Mathf.Round(raw * 10f) / 10f;
        }

        private static int DrawIntSlider(Listing_Standard listing, string label, int value, int min, int max, int step)
        {
            listing.Label(label + ": " + value);
            float raw = listing.Slider(value, min, max);
            int rounded = Mathf.RoundToInt(raw / step) * step;
            return Mathf.Clamp(rounded, min, max);
        }

        private static float DrawFloatSlider(Listing_Standard listing, string label, float value, float min, float max, float step)
        {
            string format = step < 1f ? "0.0" : "0";
            listing.Label(label + ": " + value.ToString(format));
            float raw = listing.Slider(value, min, max);
            float rounded = Mathf.Round(raw / step) * step;
            return Mathf.Clamp(rounded, min, max);
        }

        private static float DrawPercentSlider(Listing_Standard listing, string label, float value, float min, float max, float step)
        {
            float clamped = Mathf.Clamp(value, min, max);
            listing.Label(label + ": " + (clamped * 100f).ToString("0") + "%");
            float raw = listing.Slider(clamped, min, max);
            float rounded = Mathf.Round(raw / step) * step;
            return Mathf.Clamp(rounded, min, max);
        }
    }

    public static class WNGSettingsUtility
    {
        public const int TicksPerDay = 60000;

        public static int LivingForgeRawMeatCost => WNGMod.Settings?.livingForgeRawMeatCost ?? 20;
        public static int LivingForgeBiomassYield => WNGMod.Settings?.livingForgeBiomassYield ?? 16;
        public static int LivingForgeBiomassWorkAmount => WNGMod.Settings?.livingForgeBiomassWorkAmount ?? 900;
        public static int WraithGravEngineBiomassCost => WNGMod.Settings?.wraithGravEngineBiomassCost ?? 120;
        public static int WraithGravEngineSeedWorkAmount => WNGMod.Settings?.wraithGravEngineSeedWorkAmount ?? 7000;
        public static int WraithGravEngineIncubationTicks => DaysToTicks(WNGMod.Settings?.wraithGravEngineIncubationDays ?? 1f);
        public static int LivingForgeIncubationTicks => DaysToTicks(WNGMod.Settings?.livingForgeIncubationDays ?? 1f);
        public static int WraithLivingEquipmentMaturationTicks => DaysToTicks(WNGMod.Settings?.wraithLivingEquipmentMaturationDays ?? 1f);
        public static int WraithBioelectricPowerOutput => WNGMod.Settings?.wraithBioelectricPowerOutput ?? 1400;
        public static float WraithBioelectricBiomassPerDay => WNGMod.Settings?.wraithBioelectricBiomassPerDay ?? 18f;
        public static int WraithLivingPowerCellCapacity => WNGMod.Settings?.wraithLivingPowerCellCapacity ?? 750;
        public static int HostileReplicatorCap => Mathf.Clamp(WNGMod.Settings?.hostileReplicatorCap ?? 120, 20, 300);
        public static int ChildsToyFeralDelayTicks => DaysToTicks(Mathf.Clamp(WNGMod.Settings?.childsToyFeralDelayDays ?? 1f, 0.1f, 5f));
        public static bool ReplicatorStoryEventsEnabled => WNGMod.Settings?.enableReplicatorStoryEvents ?? true;
        public static int ReplicatorQueenRecurringRecoveryTicks => DaysToTicks(WNGMod.Settings?.replicatorQueenRecurringRecoveryDays ?? 4f);
        public static int SovereignLatticeControlCap => Mathf.Clamp(WNGMod.Settings?.sovereignLatticeControlCap ?? 30, 20, 50);

        public static bool ReplicatorTerrainAssimilationEnabled => WNGMod.Settings?.replicatorTerrainAssimilation ?? true;
        public static bool ReplicatorRoofAssimilationEnabled => WNGMod.Settings?.replicatorRoofAssimilation ?? true;
        public static bool ReplicatorMaterialAdaptationEnabled => WNGMod.Settings?.replicatorMaterialAdaptation ?? true;
        public static float ReplicatorBiologicalPredationThreshold =>
            Mathf.Clamp(WNGMod.Settings?.replicatorBiologicalPredationThreshold ?? 0.95f, 0.50f, 1f);

        public static bool HumanFormCopyingEnabled => WNGMod.Settings?.humanFormCopying ?? true;
        public static float HumanFormCopyReserveCost =>
            Mathf.Clamp(WNGMod.Settings?.humanFormCopyCost ?? 0.60f, 0.10f, 1f);
        public static float AsuranBlockFabricationReserveCost =>
            Mathf.Clamp01(WNGMod.Settings?.asuranBlockFabricationCost ?? 0.50f);

        public static int DaysToTicks(float days)
        {
            return Math.Max(250, Mathf.RoundToInt(Mathf.Clamp(days, 0.1f, 10f) * TicksPerDay));
        }

        public static void ApplyRuntimeDefSettings()
        {
            try
            {
                RecipeDef biomassRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("WNG_CultureBiomass");
                if (biomassRecipe != null)
                {
                    biomassRecipe.workAmount = LivingForgeBiomassWorkAmount;
                    if (biomassRecipe.ingredients != null && biomassRecipe.ingredients.Count > 0)
                        biomassRecipe.ingredients[0].SetBaseCount(LivingForgeRawMeatCost);
                    if (biomassRecipe.products != null && biomassRecipe.products.Count > 0)
                        biomassRecipe.products[0].count = LivingForgeBiomassYield;
                }

                RecipeDef engineRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("WNG_GrowWraithGravEngineSeed");
                if (engineRecipe != null)
                {
                    engineRecipe.workAmount = WraithGravEngineSeedWorkAmount;
                    if (engineRecipe.ingredients != null && engineRecipe.ingredients.Count > 0)
                        engineRecipe.ingredients[0].SetBaseCount(WraithGravEngineBiomassCost);
                }

                ThingDef bioelectric = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_BioelectricOrgan");
                if (bioelectric?.comps != null)
                {
                    foreach (CompProperties comp in bioelectric.comps)
                    {
                        CompProperties_Power power = comp as CompProperties_Power;
                        if (power != null && power.compClass == typeof(CompPowerPlant))
                            typeof(CompProperties_Power)
                                .GetField("basePowerConsumption", BindingFlags.Instance | BindingFlags.NonPublic)
                                ?.SetValue(power, (float)-WraithBioelectricPowerOutput);
                        CompProperties_Refuelable refuel = comp as CompProperties_Refuelable;
                        if (refuel != null)
                            refuel.fuelConsumptionRate = WraithBioelectricBiomassPerDay;
                    }
                }

                ThingDef livingCell = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_LivingPowerCell");
                if (livingCell?.comps != null)
                {
                    foreach (CompProperties comp in livingCell.comps)
                    {
                        CompProperties_Battery battery = comp as CompProperties_Battery;
                        if (battery != null)
                            battery.storedEnergyMax = WraithLivingPowerCellCapacity;
                    }
                }

                ApplyLifeDrainSettings(
                    "WNG_LifeDrain",
                    WNGMod.Settings?.lifeDrainVictimYears ?? 50f,
                    WNGMod.Settings?.lifeDrainRejuvenationYears ?? 5f,
                    WNGMod.Settings?.minimumWraithAgeYears ?? 18f,
                    null);
                ApplyLifeDrainSettings(
                    "WNG_PartialFeed",
                    WNGMod.Settings?.partialFeedVictimYears ?? 10f,
                    WNGMod.Settings?.partialFeedRejuvenationYears ?? 1f,
                    WNGMod.Settings?.minimumWraithAgeYears ?? 18f,
                    WNGMod.Settings?.partialFeedLifeForceGain ?? 0.34f);

                AbilityDef neuralInterface = DefDatabase<AbilityDef>.GetNamedSilentFail("WNG_NeuralInterface");
                CompProperties_AbilityNaniteInterface neuralProps =
                    neuralInterface?.comps?.OfType<CompProperties_AbilityNaniteInterface>().FirstOrDefault();
                if (neuralProps != null)
                    neuralProps.copyReserveCost = HumanFormCopyReserveCost;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[WNG] Could not apply runtime Wraith living-technology recipe settings: " + ex.Message, 0x574E4701);
            }
        }

        private static void ApplyLifeDrainSettings(
            string abilityDefName,
            float victimYears,
            float rejuvenationYears,
            float minimumAgeYears,
            float? lifeForceGain)
        {
            AbilityDef ability = DefDatabase<AbilityDef>.GetNamedSilentFail(abilityDefName);
            CompProperties_AbilityLifeDrain props =
                ability?.comps?.OfType<CompProperties_AbilityLifeDrain>().FirstOrDefault();
            if (props == null)
                return;

            props.victimAgeYears = Math.Max(0L, (long)Math.Round(victimYears));
            props.casterRejuvenationYears = Math.Max(0L, (long)Math.Round(rejuvenationYears));
            props.minimumCasterAgeYears = Math.Max(0L, (long)Math.Round(minimumAgeYears));
            if (lifeForceGain.HasValue)
                props.lifeForceGain = Mathf.Clamp01(lifeForceGain.Value);
        }
    }


}
