using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    internal sealed class AsuranTechnologyPatternSpec
    {
        public readonly string PatternId;
        public readonly string SpecimenDefName;
        public readonly string RecipeDefName;
        public readonly string Label;

        public AsuranTechnologyPatternSpec(string patternId, string specimenDefName, string recipeDefName, string label)
        {
            PatternId = patternId;
            SpecimenDefName = specimenDefName;
            RecipeDefName = recipeDefName;
            Label = label;
        }
    }

    /// <summary>
    /// Bounded foreign-technology pattern catalogue. The archive may learn conventional advanced
    /// human weapons/armour but cannot scan Wraith living technology, Replicator self-replication,
    /// Ancient relic systems, or arbitrary modded technology by accident.
    /// </summary>
    internal static class AsuranTechnologyPatternUtility
    {
        private const string ArchiveDefName = "WNG_AsuranPatternArchive";

        public static readonly IReadOnlyList<AsuranTechnologyPatternSpec> Specs = new[]
        {
            new AsuranTechnologyPatternSpec("core.charge_rifle", "Gun_ChargeRifle", "WNG_ReconstructChargeRifle", "charge rifle"),
            new AsuranTechnologyPatternSpec("core.charge_lance", "Gun_ChargeLance", "WNG_ReconstructChargeLance", "charge lance"),
            new AsuranTechnologyPatternSpec("core.shield_belt", "Apparel_ShieldBelt", "WNG_ReconstructShieldBelt", "shield belt"),
            new AsuranTechnologyPatternSpec("core.minigun", "Gun_Minigun", "WNG_ReconstructMinigun", "minigun"),
            new AsuranTechnologyPatternSpec("core.marine_armor", "Apparel_PowerArmor", "WNG_ReconstructMarineArmor", "marine armor"),
            new AsuranTechnologyPatternSpec("core.marine_helmet", "Apparel_PowerArmorHelmet", "WNG_ReconstructMarineHelmet", "marine helmet"),
            new AsuranTechnologyPatternSpec("core.assault_rifle", "Gun_AssaultRifle", "WNG_ReconstructAssaultRifle", "assault rifle"),
            new AsuranTechnologyPatternSpec("core.sniper_rifle", "Gun_SniperRifle", "WNG_ReconstructSniperRifle", "sniper rifle"),
            new AsuranTechnologyPatternSpec("core.lmg", "Gun_LMG", "WNG_ReconstructLMG", "light machine gun"),
            new AsuranTechnologyPatternSpec("core.chain_shotgun", "Gun_ChainShotgun", "WNG_ReconstructChainShotgun", "chain shotgun"),
            new AsuranTechnologyPatternSpec("core.recon_armor", "Apparel_ArmorRecon", "WNG_ReconstructReconArmor", "recon armor"),
            new AsuranTechnologyPatternSpec("royalty.cataphract_armor", "Apparel_ArmorCataphract", "WNG_ReconstructCataphractArmor", "cataphract armor"),
            new AsuranTechnologyPatternSpec("royalty.cataphract_helmet", "Apparel_ArmorHelmetCataphract", "WNG_ReconstructCataphractHelmet", "cataphract helmet")
        };

        public static bool TryPatternForSpecimen(ThingDef def, out AsuranTechnologyPatternSpec spec)
        {
            spec = def == null ? null : Specs.FirstOrDefault(x => x.SpecimenDefName == def.defName);
            return spec != null;
        }

        public static bool TryPatternForRecipe(RecipeDef def, out AsuranTechnologyPatternSpec spec)
        {
            spec = def == null ? null : Specs.FirstOrDefault(x => x.RecipeDefName == def.defName);
            return spec != null;
        }

        public static bool TryPatternForId(string patternId, out AsuranTechnologyPatternSpec spec)
        {
            spec = patternId.NullOrEmpty() ? null : Specs.FirstOrDefault(x => x.PatternId == patternId);
            return spec != null;
        }

        public static bool HasPoweredPatternArchive(Map map, Faction faction, string patternId)
        {
            if (map == null || faction == null || patternId.NullOrEmpty())
                return false;

            ThingDef archiveDef = DefDatabase<ThingDef>.GetNamedSilentFail(ArchiveDefName);
            if (archiveDef == null)
                return false;

            foreach (Thing archive in map.listerThings.ThingsOfDef(archiveDef))
            {
                if (archive == null || archive.Destroyed || !archive.Spawned || archive.Faction != faction)
                    continue;

                CompAsuranTechnologyPatternLibrary library = archive.TryGetComp<CompAsuranTechnologyPatternLibrary>();
                if (library != null && library.Powered && library.HasPattern(patternId))
                    return true;
            }

            return false;
        }

        public static void PropagatePattern(Map map, Faction faction, string patternId)
        {
            if (map == null || faction == null || patternId.NullOrEmpty())
                return;

            ThingDef archiveDef = DefDatabase<ThingDef>.GetNamedSilentFail(ArchiveDefName);
            if (archiveDef == null)
                return;

            foreach (Thing archive in map.listerThings.ThingsOfDef(archiveDef))
            {
                if (archive == null || archive.Destroyed || !archive.Spawned || archive.Faction != faction)
                    continue;

                archive.TryGetComp<CompAsuranTechnologyPatternLibrary>()?.LearnPattern(patternId);
            }
        }
    }

    public sealed class CompProperties_AsuranTechnologyPatternLibrary : CompProperties
    {
        public float scanRadius = 8f;
        public int scanWorkTicks = 30000;

        public CompProperties_AsuranTechnologyPatternLibrary()
        {
            compClass = typeof(CompAsuranTechnologyPatternLibrary);
        }
    }

    /// <summary>
    /// Technology-analysis half of the existing Pattern Archive. One exact nearby specimen is scanned
    /// non-destructively. Power loss pauses progress; removal/movement out of the field cancels the
    /// scan. Learned pattern IDs are save-persistent and synchronize to allied archives on the same map.
    /// </summary>
    public sealed class CompAsuranTechnologyPatternLibrary : ThingComp
    {
        private List<string> learnedPatterns = new List<string>();
        private Thing scanTarget;
        private string activePatternId;
        private int scanWorkRemainingTicks;

        public CompProperties_AsuranTechnologyPatternLibrary Props =>
            (CompProperties_AsuranTechnologyPatternLibrary)props;

        public bool Powered
        {
            get
            {
                CompPowerTrader power = parent?.TryGetComp<CompPowerTrader>();
                return power == null || power.PowerOn;
            }
        }

        public bool HasPattern(string patternId)
        {
            return !patternId.NullOrEmpty() && learnedPatterns != null && learnedPatterns.Contains(patternId);
        }

        internal void LearnPattern(string patternId)
        {
            if (!AsuranTechnologyPatternUtility.TryPatternForId(patternId, out AsuranTechnologyPatternSpec spec))
                return;

            if (learnedPatterns == null)
                learnedPatterns = new List<string>();

            if (!learnedPatterns.Contains(spec.PatternId))
                learnedPatterns.Add(spec.PatternId);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
                SynchronizeFromPeers();
        }

        public override void CompTick()
        {
            base.CompTick();

            if (activePatternId.NullOrEmpty() ||
                parent == null ||
                !parent.Spawned ||
                parent.Map == null ||
                !parent.IsHashIntervalTick(250))
            {
                return;
            }

            if (!Powered)
                return;

            if (!ScanTargetStillValid())
            {
                CancelScan("Technology pattern scan cancelled because the specimen left the archive's scan field.");
                return;
            }

            scanWorkRemainingTicks = Math.Max(0, scanWorkRemainingTicks - 250);
            if (scanWorkRemainingTicks > 0)
                return;

            string completedPattern = activePatternId;
            AsuranTechnologyPatternUtility.TryPatternForId(completedPattern, out AsuranTechnologyPatternSpec spec);
            AsuranTechnologyPatternUtility.PropagatePattern(parent.Map, parent.Faction, completedPattern);

            scanTarget = null;
            activePatternId = null;
            scanWorkRemainingTicks = 0;

            if (parent.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    "Asuran pattern archive learned the " + (spec?.Label ?? "technology") + " reconstruction pattern.",
                    parent,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (parent?.Faction != Faction.OfPlayer || !parent.Spawned || parent.Map == null)
                yield break;

            if (!activePatternId.NullOrEmpty())
            {
                yield return new Command_Action
                {
                    defaultLabel = "Cancel pattern scan",
                    defaultDesc = "Cancel the active non-destructive technology-pattern scan.",
                    action = () => CancelScan()
                };
                yield break;
            }

            Command_Action scan = new Command_Action
            {
                defaultLabel = "Scan technology pattern",
                defaultDesc =
                    "Select a supported intact technology specimen within " + Props.scanRadius.ToString("0") +
                    " cells. The specimen is not consumed. Power loss pauses progress.",
                action = BeginTargeting
            };

            if (!Powered)
                scan.Disable("Pattern archive must be powered.");

            yield return scan;
        }

        public override string CompInspectStringExtra()
        {
            int learned = learnedPatterns?.Count ?? 0;
            string text = "Technology patterns learned: " + learned + "/" + AsuranTechnologyPatternUtility.Specs.Count;

            if (!activePatternId.NullOrEmpty())
            {
                AsuranTechnologyPatternUtility.TryPatternForId(activePatternId, out AsuranTechnologyPatternSpec spec);
                text += "\nPattern scan: " + (spec?.Label ?? "technology") +
                        " (" + (scanWorkRemainingTicks / 60000f).ToString("0.0") + " days remaining)";
                if (!Powered)
                    text += " — paused, no power";
            }

            return text;
        }

        private void BeginTargeting()
        {
            if (!Powered || parent?.Map == null)
                return;

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = true,
                canTargetItems = true,
                canTargetLocations = false,
                validator = info => ValidSelectableSpecimen(info.Thing)
            };

            Find.Targeter.BeginTargeting(parameters, StartScan);
        }

        private bool ValidSelectableSpecimen(Thing thing)
        {
            if (thing == null ||
                thing.Destroyed ||
                !thing.Spawned ||
                thing.Map != parent.Map ||
                thing.Position.DistanceToSquared(parent.Position) > Props.scanRadius * Props.scanRadius)
            {
                return false;
            }

            if (!AsuranTechnologyPatternUtility.TryPatternForSpecimen(thing.def, out AsuranTechnologyPatternSpec spec))
                return false;

            return !HasPattern(spec.PatternId);
        }

        private void StartScan(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            if (!Powered || !ValidSelectableSpecimen(thing))
            {
                Messages.Message(
                    "That specimen is unavailable, unsupported, already learned, or outside the Pattern Archive scan field.",
                    parent,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (!AsuranTechnologyPatternUtility.TryPatternForSpecimen(thing.def, out AsuranTechnologyPatternSpec spec))
                return;

            scanTarget = thing;
            activePatternId = spec.PatternId;
            scanWorkRemainingTicks = Math.Max(250, Props.scanWorkTicks);
        }

        private bool ScanTargetStillValid()
        {
            if (scanTarget == null ||
                scanTarget.Destroyed ||
                !scanTarget.Spawned ||
                scanTarget.Map != parent.Map ||
                scanTarget.Position.DistanceToSquared(parent.Position) > Props.scanRadius * Props.scanRadius)
            {
                return false;
            }

            return AsuranTechnologyPatternUtility.TryPatternForSpecimen(scanTarget.def, out AsuranTechnologyPatternSpec spec) &&
                   spec.PatternId == activePatternId;
        }

        private void CancelScan(string message = null)
        {
            scanTarget = null;
            activePatternId = null;
            scanWorkRemainingTicks = 0;

            if (!message.NullOrEmpty() && parent?.Faction == Faction.OfPlayer)
                Messages.Message(message, parent, MessageTypeDefOf.NegativeEvent, historical: false);
        }

        private void SynchronizeFromPeers()
        {
            if (parent?.Map == null || parent.Faction == null)
                return;

            ThingDef archiveDef = DefDatabase<ThingDef>.GetNamedSilentFail("WNG_AsuranPatternArchive");
            if (archiveDef == null)
                return;

            foreach (Thing archive in parent.Map.listerThings.ThingsOfDef(archiveDef))
            {
                if (archive == null || archive == parent || archive.Destroyed || archive.Faction != parent.Faction)
                    continue;

                CompAsuranTechnologyPatternLibrary peer = archive.TryGetComp<CompAsuranTechnologyPatternLibrary>();
                if (peer?.learnedPatterns == null)
                    continue;

                foreach (string patternId in peer.learnedPatterns)
                    LearnPattern(patternId);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref learnedPatterns, "wngAsuranTechnologyPatterns", LookMode.Value);
            Scribe_References.Look(ref scanTarget, "wngAsuranTechnologyScanTarget");
            Scribe_Values.Look(ref activePatternId, "wngAsuranTechnologyActivePattern");
            Scribe_Values.Look(ref scanWorkRemainingTicks, "wngAsuranTechnologyScanRemaining", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                learnedPatterns = (learnedPatterns ?? new List<string>())
                    .Where(x => !x.NullOrEmpty() && AsuranTechnologyPatternUtility.TryPatternForId(x, out _))
                    .Distinct()
                    .ToList();

                if (!activePatternId.NullOrEmpty())
                {
                    if (scanTarget == null ||
                        !AsuranTechnologyPatternUtility.TryPatternForSpecimen(scanTarget.def, out AsuranTechnologyPatternSpec spec) ||
                        spec.PatternId != activePatternId)
                    {
                        scanTarget = null;
                        activePatternId = null;
                        scanWorkRemainingTicks = 0;
                    }
                    else if (scanWorkRemainingTicks <= 0)
                    {
                        scanWorkRemainingTicks = Math.Max(250, Props.scanWorkTicks);
                    }
                }
            }
        }
    }


}
