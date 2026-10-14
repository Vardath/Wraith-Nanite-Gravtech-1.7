using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Native RimWorld shield properties with a WNG-specific comp class.
    /// Charge/reset serialization and EMP collapse remain owned by CompShield.
    /// </summary>
    public sealed class CompProperties_PrecursorPersonalShield : CompProperties_Shield
    {
        public CompProperties_PrecursorPersonalShield()
        {
            compClass = typeof(CompPrecursorPersonalShield);
        }
    }

    /// <summary>
    /// Retained Precursor personal field implemented on RimWorld's native CompShield.
    /// The only WNG-specific behavior is feeding a real hostile block-Replicator
    /// shield engagement into the current cumulative adaptation/domain system.
    /// </summary>
    public sealed class CompPrecursorPersonalShield : CompShield
    {
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            if (!absorbed)
                return;

            Pawn attacker = dinfo.Instigator as Pawn;
            Pawn wearer = PawnOwner;
            if (attacker == null || wearer == null || attacker.Dead || wearer.Dead)
                return;
            if (!ReplicatorAssimilationUtility.IsBlockReplicator(attacker))
                return;
            if (attacker.Faction == null || wearer.Faction == null || !attacker.Faction.HostileTo(wearer.Faction))
                return;

            ReplicatorAdaptationUtility.ShareShieldEngagement(attacker);
        }
    }
}
