using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_SealedMachineSalvage : CompProperties
    {
        public int minimumSpawnedTicks = 12000;
        public int maximumSpawnedTicks = 24000;
        public int matterCount = 12;

        public CompProperties_SealedMachineSalvage()
        {
            compClass = typeof(CompSealedMachineSalvage);
        }
    }

    /// <summary>
    /// A bounded Replicator infiltration carrier. The package is inert while not physically spawned
    /// on a map. Only time spent spawned degrades the seal. A successful rupture creates one stack of
    /// ordinary WNG_ReplicatorMatter; all later dormancy/containment/reassembly behavior belongs to
    /// the canonical Matter component rather than this carrier.
    /// </summary>
    public sealed class CompSealedMachineSalvage : ThingComp
    {
        private const int RareTickInterval = 250;

        private int spawnedTicks;
        private int ruptureAtTicks;
        private bool ruptureCommitted;

        private CompProperties_SealedMachineSalvage Props =>
            (CompProperties_SealedMachineSalvage)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad && ruptureAtTicks <= 0)
                ruptureAtTicks = RandomRuptureTickTarget();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (ruptureCommitted ||
                parent == null ||
                !parent.Spawned ||
                parent.Map == null)
                return;

            if (ruptureAtTicks <= 0)
                ruptureAtTicks = RandomRuptureTickTarget();

            if (spawnedTicks > int.MaxValue - RareTickInterval)
                spawnedTicks = int.MaxValue;
            else
                spawnedTicks += RareTickInterval;

            if (spawnedTicks >= ruptureAtTicks)
                TryRupture();
        }

        private int RandomRuptureTickTarget()
        {
            int min = Math.Max(RareTickInterval, Props?.minimumSpawnedTicks ?? 12000);
            int max = Math.Max(min, Props?.maximumSpawnedTicks ?? 24000);
            return Rand.RangeInclusive(min, max);
        }

        private void TryRupture()
        {
            if (ruptureCommitted ||
                parent == null ||
                !parent.Spawned ||
                parent.Map == null)
                return;

            ThingDef matterDef =
                DefDatabase<ThingDef>.GetNamedSilentFail("WNG_ReplicatorMatter");
            if (matterDef == null)
                return;

            Map map = parent.Map;
            IntVec3 cell = parent.Position;

            Thing matter = ThingMaker.MakeThing(matterDef);
            if (matter == null)
                return;

            matter.stackCount = Math.Min(
                matterDef.stackLimit,
                Math.Max(10, Props?.matterCount ?? 12));

            bool placed = false;
            try
            {
                placed = GenPlace.TryPlaceThing(
                    matter,
                    cell,
                    map,
                    ThingPlaceMode.Near);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Sealed Replicator salvage could not place its Matter payload; carrier remains sealed for retry: " +
                    ex.Message);
            }

            if (!placed)
            {
                if (!matter.Destroyed && matter.ParentHolder == null)
                    matter.Destroy(DestroyMode.Vanish);
                return;
            }

            // Matter placement is the irreversible gameplay commit. Latch before presentation and
            // casing cleanup so no later exception can emit another stack from this same object.
            ruptureCommitted = true;

            try
            {
                if (!parent.Destroyed)
                    parent.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Sealed Replicator salvage ruptured, but residual casing cleanup failed: " +
                    ex.Message);
            }

            try
            {
                Find.LetterStack.ReceiveLetter(
                    "Salvage containment failure",
                    "The sealed machine salvage has ruptured into ordinary Replicator Matter. " +
                    "The Blocks are still governed by the current 30,000-tick uncontained exposure rule: " +
                    "destroy, reprocess or move them under powered nanite containment before self-assembly can begin.",
                    LetterDefOf.ThreatSmall,
                    matter);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Sealed Replicator salvage rupture committed but letter presentation failed: " +
                    ex.Message);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (ruptureCommitted)
                return "Seal integrity: ruptured.";

            if (parent?.Spawned != true)
                return "Seal integrity: dormant in transit.";

            int target = Math.Max(1, ruptureAtTicks);
            float remaining = Math.Max(0f, (target - spawnedTicks) / (float)target);
            if (remaining > 0.60f)
                return "Seal integrity: stable. Fine metallic residue is visible around several joints.";
            if (remaining > 0.30f)
                return "Seal integrity: degrading. Internal micro-mechanical movement is becoming detectable.";
            return "Seal integrity: critical. The salvage casing is flexing against itself.";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(
                ref spawnedTicks,
                "wngSealedSalvageSpawnedTicks",
                0);
            Scribe_Values.Look(
                ref ruptureAtTicks,
                "wngSealedSalvageRuptureAtTicks",
                0);
            Scribe_Values.Look(
                ref ruptureCommitted,
                "wngSealedSalvageRuptureCommitted",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                spawnedTicks = Math.Max(0, spawnedTicks);
                int min = Math.Max(
                    RareTickInterval,
                    Props?.minimumSpawnedTicks ?? 12000);
                int max = Math.Max(
                    min,
                    Props?.maximumSpawnedTicks ?? 24000);

                if (!ruptureCommitted &&
                    (ruptureAtTicks < min || ruptureAtTicks > max))
                    ruptureAtTicks = Rand.RangeInclusive(min, max);
            }
        }
    }
}
