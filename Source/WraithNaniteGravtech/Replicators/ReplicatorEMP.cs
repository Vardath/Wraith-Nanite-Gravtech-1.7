using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_ReplicatorInterference : CompProperties
    {
        public CompProperties_ReplicatorInterference()
        {
            compClass = typeof(CompReplicatorInterference);
        }
    }

    /// <summary>
    /// WNG does not replace RimWorld EMP stun/resistance/adaptation. This component records the
    /// endpoint of a native EMP disruption only after the native StunHandler has actually accepted
    /// the EMP. The marker lets WNG-specific systems query a common state and lets genuine hierarchy
    /// breakup preserve the remaining interference instead of creating instantly-clean child bodies.
    /// </summary>
    public sealed class CompReplicatorInterference : ThingComp
    {
        private int empDisruptedUntilTick;

        public int EmpDisruptedUntilTick
        {
            get
            {
                int until = empDisruptedUntilTick;
                Pawn pawn = parent as Pawn;
                StunHandler stunner = pawn?.stances?.stunner;
                if (stunner != null && stunner.Stunned && stunner.StunFromEMP)
                {
                    int now = Find.TickManager?.TicksGame ?? 0;
                    until = Math.Max(until, now + Math.Max(0, stunner.StunTicksLeft));
                }
                return until;
            }
        }

        public bool EmpDisrupted => (Find.TickManager?.TicksGame ?? 0) < EmpDisruptedUntilTick;

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);

            if (dinfo.Def != DamageDefOf.EMP)
                return;

            Pawn pawn = parent as Pawn;
            StunHandler stunner = pawn?.stances?.stunner;
            if (stunner == null || !stunner.Stunned || !stunner.StunFromEMP)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            empDisruptedUntilTick = Math.Max(empDisruptedUntilTick, now + Math.Max(0, stunner.StunTicksLeft));
        }

        public void InheritEmpDisruptionUntil(int absoluteTick, bool preservePhysicalStun)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (absoluteTick <= now)
                return;

            empDisruptedUntilTick = Math.Max(empDisruptedUntilTick, absoluteTick);

            if (!preservePhysicalStun)
                return;

            Pawn pawn = parent as Pawn;
            StunHandler stunner = pawn?.stances?.stunner;
            if (stunner != null)
            {
                int remaining = absoluteTick - now;
                stunner.StunFor(remaining, null, addBattleLog: false, showMote: true);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref empDisruptedUntilTick, "wngReplicatorEmpDisruptedUntilTick", 0);
        }

        public override string CompInspectStringExtra()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int remaining = EmpDisruptedUntilTick - now;
            if (remaining <= 0)
                return null;

            return "Replicator lattice disrupted by EMP: " + remaining + " ticks";
        }
    }

    public static class ReplicatorInterferenceUtility
    {
        public static bool IsEmpDisrupted(Pawn pawn)
        {
            return pawn?.TryGetComp<CompReplicatorInterference>()?.EmpDisrupted == true;
        }
    }
}
