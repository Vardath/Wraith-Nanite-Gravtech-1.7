using RimWorld;
using Verse;

namespace WraithNaniteGravtech.Anomaly
{
    public static class WhispersImplantUtility
    {
        // Retained so existing saves can deserialize the former castable ability.
        private const string MistAbilityDefName = "WNG_ReleaseWhispersMist";
        public static AbilityDef MistAbilityDef => DefDatabase<AbilityDef>.GetNamedSilentFail(MistAbilityDefName);
    }

    /// <summary>
    /// A permanent passive mist emitter, not an ability. Mirrors the native Whispers
    /// fog on every interval, and removes legacy castable abilities from existing saves.
    /// </summary>
    public sealed class Hediff_WhispersMistGland : Hediff_Implant
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            RemoveLegacyMistAbility();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || delta <= 0 ||
                !pawn.IsHashIntervalTick(WhispersFogUtility.IntervalTicks, delta))
                return;

            // Also reconciles saved pawns whose implant was installed before this change.
            RemoveLegacyMistAbility();

            // A Whispers hybrid already emits fog through its native gene. Do not stack
            // the same mist twice if one is also given the cultured implant.
            if (!HasActiveNativeWhispersFog())
                WhispersFogUtility.EmitPredatoryFog(pawn);
        }

        public override void PostRemoved()
        {
            RemoveLegacyMistAbility();
            base.PostRemoved();
        }

        private void RemoveLegacyMistAbility()
        {
            AbilityDef ability = WhispersImplantUtility.MistAbilityDef;
            if (ability != null && pawn?.abilities?.GetAbility(ability) != null)
                pawn.abilities.RemoveAbility(ability);
        }

        private bool HasActiveNativeWhispersFog()
        {
            var genes = pawn?.genes?.GenesListForReading;
            if (genes == null)
                return false;
            foreach (Gene gene in genes)
                if (gene is Gene_WhispersPredator native && native.Active)
                    return true;
            return false;
        }
    }

    // Legacy ability classes remain only for old save compatibility. The hediff
    // removes the obsolete ability, and it is never granted again by the implant.
    public sealed class CompProperties_AbilityWhispersMist : CompProperties_AbilityEffect
    {
        public float radius = 10f;
        public int gasPerCell = 44;
        public CompProperties_AbilityWhispersMist() { compClass = typeof(CompAbilityEffect_WhispersMist); }
    }

    public sealed class CompAbilityEffect_WhispersMist : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            return false;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            // Obsolete castable mist cannot create a second, manual emission path.
        }
    }
}
