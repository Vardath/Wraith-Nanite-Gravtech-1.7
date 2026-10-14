using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Capture discharge for the Wraith stun staff. Ordinary RimWorld Stun impact remains
    /// authoritative; a biological humanlike target also receives a bounded motor lock and
    /// native anesthesia. Nanite synthetics and mechanoids remain outside this capture path.
    /// </summary>
    public sealed class Projectile_WraithStunStaff : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            base.Impact(hitThing, blockedByShield);

            Pawn pawn = hitThing as Pawn;
            if (pawn == null ||
                pawn.Dead ||
                pawn.health?.hediffSet == null ||
                !WraithLivingTechnologyUtility.IsBiologicalHumanlike(pawn))
                return;

            HediffDef paralysis =
                DefDatabase<HediffDef>.GetNamedSilentFail("WNG_StunStaffParalysis");
            if (paralysis != null)
            {
                Hediff existing =
                    pawn.health.hediffSet.GetFirstHediffOfDef(paralysis);
                if (existing != null)
                    existing.Severity = existing.def.initialSeverity;
                else
                    pawn.health.AddHediff(paralysis);
            }

            HealthUtility.TryAnesthetize(pawn);
        }
    }
}
