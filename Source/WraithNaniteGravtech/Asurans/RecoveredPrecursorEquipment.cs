using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_RecoveredPrecursorEquipment : CompProperties
    {
        public FloatRange fieldAlignmentRange = new FloatRange(0.58f, 0.88f);
        public float baseFaultChance = 0.025f;
        public float alignmentFaultScale = 0.25f;
        public float damageFaultScale = 0.12f;
        public float maximumFaultChance = 0.28f;
        public int wearPerFault = 3;

        public CompProperties_RecoveredPrecursorEquipment()
        {
            compClass = typeof(CompRecoveredPrecursorEquipment);
        }
    }

    /// <summary>
    /// Exact-item instability for an original recovered Asuran field rifle. Alignment is rolled once
    /// per physical item and saved. Newly manufactured WNG_PrecursorPulseRifle items never carry
    /// this comp and therefore remain reliable.
    /// </summary>
    public sealed class CompRecoveredPrecursorEquipment : ThingComp
    {
        private float fieldAlignment = -1f;
        private int lastFaultNoticeTick = -999999;

        public CompProperties_RecoveredPrecursorEquipment Props =>
            (CompProperties_RecoveredPrecursorEquipment)props;

        public float FieldAlignment
        {
            get
            {
                EnsureAlignment();
                return Math.Max(0f, Math.Min(1f, fieldAlignment));
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureAlignment();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureAlignment();
        }

        public float FaultChance
        {
            get
            {
                EnsureAlignment();

                float damageFraction = 0f;
                if (parent != null && parent.def.useHitPoints && parent.MaxHitPoints > 0)
                {
                    damageFraction = 1f -
                        Math.Max(0f, Math.Min(1f, parent.HitPoints / (float)parent.MaxHitPoints));
                }

                float chance =
                    Math.Max(0f, Props.baseFaultChance) +
                    (1f - FieldAlignment) * Math.Max(0f, Props.alignmentFaultScale) +
                    damageFraction * Math.Max(0f, Props.damageFaultScale);

                float maximum = Math.Max(0.01f, Props.maximumFaultChance);
                return Math.Max(0f, Math.Min(maximum, chance));
            }
        }

        public bool RollFault()
        {
            return FaultChance > 0f && Rand.Chance(FaultChance);
        }

        public void NotifyDischargeFault(Pawn wearer)
        {
            int wear = Math.Max(0, Props.wearPerFault);
            if (wear > 0 && parent != null && parent.def.useHitPoints && parent.HitPoints > 1)
                parent.HitPoints = Math.Max(1, parent.HitPoints - wear);

            try
            {
                if (wearer?.Map != null)
                    DefDatabase<SoundDef>.GetNamedSilentFail("WNG_PrecursorPilotConsole")
                        ?.PlayOneShot(new TargetInfo(wearer.Position, wearer.Map));
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Recovered precursor discharge fault committed but sound failed: " + ex.Message);
            }

            if (wearer?.Faction != Faction.OfPlayer)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - lastFaultNoticeTick < 600)
                return;

            lastFaultNoticeTick = now;
            try
            {
                Messages.Message(
                    parent.LabelShortCap +
                    " fails to maintain its recovered field alignment. The discharge collapses and the emitter suffers additional wear.",
                    wearer,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
            catch (Exception ex)
            {
                Log.Warning("[WNG] Recovered precursor fault committed but player message failed: " + ex.Message);
            }
        }

        public override string CompInspectStringExtra()
        {
            int alignmentPercent = (int)Math.Round(FieldAlignment * 100f);
            int faultPercent = (int)Math.Round(FaultChance * 100f);
            string state =
                alignmentPercent >= 80 ? "mostly coherent" :
                alignmentPercent >= 68 ? "unstable" :
                "badly degraded";

            return "Recovered field alignment: " + alignmentPercent + "% — " + state +
                   "\nDischarge fault chance: " + faultPercent + "%" +
                   "\nStabilization: reconstruct at an Asuran Fabricator after precursor energy systems";
        }

        public override string GetDescriptionPart()
        {
            return "This is recovered original field equipment rather than a stable reconstruction. " +
                   "Its saved emitter alignment and current physical damage determine a visible bounded chance " +
                   "that a fired pulse will collapse before impact. A fault wastes the discharge and causes modest " +
                   "additional weapon wear; it does not explode or create a replacement item.";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fieldAlignment, "wngRecoveredPrecursorFieldAlignment", -1f);
            Scribe_Values.Look(ref lastFaultNoticeTick, "wngRecoveredPrecursorLastFaultNoticeTick", -999999);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureAlignment();
        }

        private void EnsureAlignment()
        {
            if (fieldAlignment >= 0f)
            {
                fieldAlignment = Math.Max(0f, Math.Min(1f, fieldAlignment));
                return;
            }

            FloatRange range = Props?.fieldAlignmentRange ?? new FloatRange(0.58f, 0.88f);
            float min = Math.Max(0f, Math.Min(1f, range.min));
            float max = Math.Max(min, Math.Min(1f, range.max));
            fieldAlignment = Rand.Range(min, max);
        }
    }

    /// <summary>
    /// Cross-vanilla/CE discharge observer. Both vanilla and Combat Extended pulse shots become
    /// map Projectile things with a Pawn launcher. Processing each new projectile once lets the
    /// recovered exact weapon's saved alignment decide whether the discharge survives, without a
    /// compile-time dependency on Combat Extended or a separate CE-only weapon state.
    /// </summary>
    public sealed class MapComponent_RecoveredPrecursorDischargeFaults : MapComponent
    {
        private const int ProcessedRetentionTicks = 3600;
        private readonly Dictionary<int, int> processedProjectileTicks = new Dictionary<int, int>();
        private int nextPruneTick;

        public MapComponent_RecoveredPrecursorDischargeFaults(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            List<Thing> projectiles = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                Projectile projectile = projectiles[i] as Projectile;
                if (projectile == null ||
                    projectile.Destroyed ||
                    !projectile.Spawned ||
                    processedProjectileTicks.ContainsKey(projectile.thingIDNumber))
                {
                    continue;
                }

                processedProjectileTicks[projectile.thingIDNumber] = now;
                TryApplyRecoveredFault(projectile);
            }

            if (now < nextPruneTick)
                return;

            nextPruneTick = SafeFutureTick(now, 600);
            int cutoff = now - ProcessedRetentionTicks;
            foreach (int id in processedProjectileTicks
                         .Where(pair => pair.Value < cutoff)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                processedProjectileTicks.Remove(id);
            }
        }

        private static void TryApplyRecoveredFault(Projectile projectile)
        {
            Pawn launcher = projectile?.Launcher as Pawn;
            ThingWithComps weapon = launcher?.equipment?.Primary;
            if (weapon?.def?.defName != "WNG_RecoveredPrecursorPulseRifle")
                return;

            CompRecoveredPrecursorEquipment recovered =
                weapon.TryGetComp<CompRecoveredPrecursorEquipment>();
            if (recovered == null || !recovered.RollFault())
                return;

            // Destruction of the already-created pulse is the single failed-discharge commit.
            // CE has already spent its ammunition by this point, which correctly makes the failed
            // field alignment consume the attempted pulse rather than refunding a charge.
            projectile.Destroy(DestroyMode.Vanish);
            recovered.NotifyDischargeFault(launcher);
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long value = (long)Math.Max(0, now) + Math.Max(1, delay);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
