using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorSuppressionEmitter : CompProperties
    {
        public float radius = 13f;
        public int pulseIntervalTicks = 1500;
        public int maxTargets = 6;
        public float empDamage = 12f;

        public CompProperties_ReplicatorSuppressionEmitter()
        {
            compClass = typeof(CompReplicatorSuppressionEmitter);
        }
    }

    /// <summary>
    /// Advanced bounded anti-Replicator countermeasure. It does not own a second suppression clock:
    /// each pulse applies ordinary RimWorld EMP to a small number of nearby hostile WNG Replicators.
    /// Block forms enter the existing CompReplicatorInterference state only when their native EMP
    /// stun actually accepts the hit; human-form nanite synthetics use their current EMP physiology.
    /// </summary>
    public sealed class CompReplicatorSuppressionEmitter : ThingComp
    {
        private int nextPulseTick;

        private CompProperties_ReplicatorSuppressionEmitter Props =>
            (CompProperties_ReplicatorSuppressionEmitter)props;

        private bool Active
        {
            get
            {
                if (parent == null || !parent.Spawned || parent.Map == null)
                    return false;

                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                CompFlickable flick = parent.TryGetComp<CompFlickable>();
                return power != null &&
                       power.PowerOn &&
                       (flick == null || flick.SwitchIsOn);
            }
        }

        private static int SafeFutureTick(int now, int delay)
        {
            long result = (long)Math.Max(0, now) + Math.Max(1, delay);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad && nextPulseTick <= 0)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                nextPulseTick = SafeFutureTick(
                    now,
                    Math.Max(250, Props.pulseIntervalTicks));
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (!Active || Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < nextPulseTick)
                return;

            nextPulseTick = SafeFutureTick(
                now,
                Math.Max(250, Props.pulseIntervalTicks));

            Pulse();
        }

        private void Pulse()
        {
            Map map = parent.Map;
            if (map == null)
                return;

            float radiusSq = Math.Max(1f, Props.radius) * Math.Max(1f, Props.radius);
            int limit = Math.Max(1, Props.maxTargets);

            List<Pawn> targets = map.mapPawns.AllPawnsSpawned
                .Where(IsEligibleHostileReplicator)
                .Where(p => p.Position.DistanceToSquared(parent.Position) <= radiusSq)
                .OrderByDescending(ReplicatorCoordinationUtility.IsController)
                .ThenByDescending(p =>
                    p.TryGetComp<CompReplicatorAdaptation>()
                        ?.Has(ReplicatorAdaptationFlags.Shield) == true)
                .ThenBy(p => p.Position.DistanceToSquared(parent.Position))
                .ThenBy(p => p.thingIDNumber)
                .Take(limit)
                .ToList();

            if (targets.Count == 0)
                return;

            foreach (Pawn target in targets)
            {
                try
                {
                    target.TakeDamage(
                        new DamageInfo(
                            DamageDefOf.EMP,
                            Math.Max(1f, Props.empDamage),
                            instigator: parent));
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[WNG] Anti-Replicator EMP emitter failed to pulse " +
                        target + ": " + ex.Message);
                }
            }

            BestEffortPresentation();
        }

        private static bool IsEligibleHostileReplicator(Pawn pawn)
        {
            if (pawn == null ||
                pawn.Dead ||
                !pawn.Spawned ||
                pawn.Faction == null ||
                Faction.OfPlayer == null ||
                pawn.Faction == Faction.OfPlayer ||
                !pawn.Faction.HostileTo(Faction.OfPlayer))
                return false;

            // Block Replicators own the shared interference component. Human-form/Asuran nanite
            // synthetics are included so their existing EMP-sensitive reconstruction can be interrupted.
            return pawn.TryGetComp<CompReplicatorInterference>() != null ||
                   AsuranCollectiveUtility.IsNaniteSynthetic(pawn);
        }

        private void BestEffortPresentation()
        {
            try
            {
                DefDatabase<SoundDef>
                    .GetNamedSilentFail("EMP_Discharge")
                    ?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));

                FleckMaker.ThrowLightningGlow(
                    parent.DrawPos,
                    parent.Map,
                    1.75f);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[WNG] Anti-Replicator EMP pulse committed but presentation failed: " +
                    ex.Message);
            }
        }

        public override string CompInspectStringExtra()
        {
            string state = Active ? "active" : "offline";
            return "Anti-Replicator EMP suppression: " + state +
                   ". Radius " + Props.radius.ToString("0.0") +
                   "; up to " + Math.Max(1, Props.maxTargets) +
                   " hostile WNG Replicators per pulse. Native EMP/interference rules remain authoritative.";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(
                ref nextPulseTick,
                "wngReplicatorSuppressionNextPulse",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                int interval = Math.Max(250, Props?.pulseIntervalTicks ?? 1500);
                int latest = SafeFutureTick(now, interval);
                if (nextPulseTick <= 0 || nextPulseTick > latest)
                    nextPulseTick = latest;
            }
        }
    }
}
