using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public static class AncientCompatibilityUtility
    {
        public const string NaturalAffinityGeneDefName = "WNG_AncientAffinity";
        public const string ArtificialInterfaceHediffDefName = "WNG_AncientNeuralInterface";
        public const float ArtificialInterfaceTakeChance = 0.15f;

        public static bool IsCompatible(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;
            if (AsuranCollectiveUtility.IsNaniteSynthetic(pawn))
                return true;
            return HasNaturalAffinity(pawn) || HasArtificialInterface(pawn);
        }

        public static bool HasNaturalAffinity(Pawn pawn) => HasActiveGene(pawn, NaturalAffinityGeneDefName);

        public static bool HasArtificialInterface(Pawn pawn) =>
            HasHediff(pawn, ArtificialInterfaceHediffDefName);

        public static bool IsEligibleForNaturalAffinity(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.Humanlike || pawn.genes == null)
                return false;
            if (IsCompatible(pawn) || AsuranCollectiveUtility.IsNaniteSynthetic(pawn))
                return false;
            return !HasActiveGene(pawn, "WNG_LifeForceMetabolism") && !HasActiveGene(pawn, "WNG_ReplicatorQueenLink");
        }

        private static bool HasActiveGene(Pawn pawn, string defName)
        {
            return pawn?.genes?.GenesListForReading?.Any(g => g?.def?.defName == defName && g.Active) == true;
        }

        private static bool HasHediff(Pawn pawn, string defName)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            return def != null && pawn?.health?.hediffSet?.HasHediff(def) == true;
        }
    }

    /// <summary>
    /// Artificial ATA compatibility is not an 85% dangerous surgery failure.
    /// The operation itself completes cleanly; afterwards the neural handshake either seats
    /// successfully (15%) or the interface fails to integrate (85%) without injuring the pawn.
    /// The consumed interface/medicine represent a completed integration attempt.
    /// </summary>
    public sealed class Recipe_InstallAncientNeuralInterface : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null)
                return false;

            return !pawn.Dead &&
                   !AncientCompatibilityUtility.IsCompatible(pawn) &&
                   base.AvailableOnNow(thing, part);
        }

        public override AcceptanceReport AvailableReport(Thing thing, BodyPartRecord part = null)
        {
            Pawn pawn = thing as Pawn;
            if (pawn == null || pawn.Dead)
                return "Requires a living humanlike patient.";
            if (AncientCompatibilityUtility.HasNaturalAffinity(pawn))
                return pawn.LabelShortCap + " already carries the Ancient technology activation gene.";
            if (AncientCompatibilityUtility.HasArtificialInterface(pawn))
                return pawn.LabelShortCap + " already has an accepted Ancient neural interface.";
            if (AsuranCollectiveUtility.IsNaniteSynthetic(pawn))
                return pawn.LabelShortCap + " already presents a compatible synthetic Ancient-control handshake.";
            return base.AvailableReport(thing, part);
        }

        public override void ApplyOnPawn(
            Pawn pawn,
            BodyPartRecord part,
            Pawn billDoer,
            List<Thing> ingredients,
            Bill bill)
        {
            if (pawn == null ||
                pawn.Dead ||
                pawn.health?.hediffSet == null ||
                AncientCompatibilityUtility.IsCompatible(pawn))
                return;

            HediffDef interfaceDef =
                DefDatabase<HediffDef>.GetNamedSilentFail(
                    AncientCompatibilityUtility.ArtificialInterfaceHediffDefName);
            if (interfaceDef == null)
            {
                Log.Error("[WNG] Ancient neural-interface surgery completed but its HediffDef could not be resolved; no compatibility state was changed.");
                return;
            }

            // Deliberately bypass vanilla surgical-failure resolution here. The retained 85% figure
            // describes post-operation interface rejection, not a botched operation or injury roll.
            if (billDoer != null)
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);

            if (!Rand.Chance(AncientCompatibilityUtility.ArtificialInterfaceTakeChance))
            {
                if (pawn.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        pawn.LabelShortCap +
                        "'s Ancient control-interface procedure completed cleanly, but the neural handshake did not take. No interface was retained.",
                        pawn,
                        MessageTypeDefOf.NeutralEvent,
                        historical: false);
                }
                return;
            }

            pawn.health.AddHediff(interfaceDef, part);

            if (!AncientCompatibilityUtility.HasArtificialInterface(pawn))
            {
                Log.Error("[WNG] Ancient neural-interface surgery selected a successful take but the interface Hediff did not persist.");
                return;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    pawn.LabelShortCap +
                    "'s Ancient control interface accepted the neural handshake. Ancient/ATA-gated systems can now recognize this pawn.",
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
        }
    }

    // Rare natural ATA-like compatibility. Each humanlike pawn is assessed once per save.
    public sealed class AncientAffinityDistribution : GameComponent
    {
        public const float NaturalAffinityChance = 0.02f;
        private const int ScanIntervalTicks = 600;
        private List<int> assessedPawnIds = new List<int>();
        private HashSet<int> assessedLookup = new HashSet<int>();
        private int nextScanTick;

        public AncientAffinityDistribution(Game game) { }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null || Find.TickManager.TicksGame < nextScanTick)
                return;
            nextScanTick = Find.TickManager.TicksGame + ScanIntervalTicks;
            EnsureLookup();
            foreach (Map map in Find.Maps ?? Enumerable.Empty<Map>())
                foreach (Pawn pawn in map?.mapPawns?.AllPawnsSpawned ?? Enumerable.Empty<Pawn>())
                    AssessPawn(pawn);
        }

        private void AssessPawn(Pawn pawn)
        {
            if (pawn == null || pawn.thingIDNumber <= 0 || pawn.RaceProps == null || !pawn.RaceProps.Humanlike || assessedLookup.Contains(pawn.thingIDNumber))
                return;
            assessedLookup.Add(pawn.thingIDNumber);
            assessedPawnIds.Add(pawn.thingIDNumber);
            if (!AncientCompatibilityUtility.IsEligibleForNaturalAffinity(pawn) || !Rand.Chance(NaturalAffinityChance))
                return;
            GeneDef affinity = DefDatabase<GeneDef>.GetNamedSilentFail(AncientCompatibilityUtility.NaturalAffinityGeneDefName);
            if (affinity != null && pawn.genes != null)
                pawn.genes.AddGene(affinity, false);
        }

        private void EnsureLookup()
        {
            if (assessedPawnIds == null)
                assessedPawnIds = new List<int>();
            assessedPawnIds = assessedPawnIds.Where(id => id > 0).Distinct().ToList();
            assessedLookup = new HashSet<int>(assessedPawnIds);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref assessedPawnIds, "wngAncientAffinityAssessedPawnIds", LookMode.Value);
            Scribe_Values.Look(ref nextScanTick, "wngAncientAffinityNextScanTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureLookup();
        }
    }
}
