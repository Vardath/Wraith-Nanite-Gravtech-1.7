using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    internal static class WNGImplantRegenerationUtility
    {
        private const string NaniteEmpDisruptionDefName = "WNG_NaniteEMPDisruption";

        public static Hediff_Injury WorstRepairableInjury(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.hediffs
                ?.OfType<Hediff_Injury>()
                .Where(x => x != null && !x.IsPermanent() && x.Severity > 0f)
                .OrderByDescending(x => x.Severity)
                .FirstOrDefault();
        }

        public static bool HasNaniteEmpDisruption(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return false;

            HediffDef disruption =
                DefDatabase<HediffDef>.GetNamedSilentFail(NaniteEmpDisruptionDefName);
            return disruption != null &&
                pawn.health.hediffSet.GetFirstHediffOfDef(disruption) != null;
        }
    }

    public sealed class HediffCompProperties_ImplantRegeneration : HediffCompProperties
    {
        public int intervalTicks = 2400;
        public float healAmount = 0.55f;
        public bool pauseDuringNaniteEMP;

        public HediffCompProperties_ImplantRegeneration()
        {
            compClass = typeof(HediffComp_ImplantRegeneration);
        }
    }

    /// <summary>
    /// Bounded active repair for retained regenerative implants. It heals only existing
    /// non-permanent injuries and never reconstructs missing anatomy or resurrects a pawn.
    /// Individual implants decide whether human-form nanite EMP disruption pauses the pulse.
    /// </summary>
    public sealed class HediffComp_ImplantRegeneration : HediffComp
    {
        private HediffCompProperties_ImplantRegeneration Props =>
            (HediffCompProperties_ImplantRegeneration)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null)
                return;

            int interval = Props?.intervalTicks ?? 2400;
            if (interval < 1 || !pawn.IsHashIntervalTick(interval))
                return;

            if ((Props?.pauseDuringNaniteEMP ?? false) &&
                WNGImplantRegenerationUtility.HasNaniteEmpDisruption(pawn))
            {
                return;
            }

            float heal = Props?.healAmount ?? 0.55f;
            if (heal <= 0f)
                return;

            WNGImplantRegenerationUtility.WorstRepairableInjury(pawn)?.Heal(heal);
        }
    }

    /// <summary>
    /// Provisional reconstruction-microforge tuning for D108. The implant repairs only ordinary
    /// non-permanent injuries. It does not remove scars, restore missing parts, resurrect pawns,
    /// or replace the stronger native human-form Replicator reconstruction system.
    /// Human-form nanite EMP disruption suspends the implant's nanite repair pulse.
    /// </summary>
    public sealed class Hediff_ReconstructionMicroforge : HediffWithComps
    {
        private const int RepairIntervalTicks = 2500;
        private const float HealPerPulse = 0.75f;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null || delta <= 0)
                return;
            if (WNGImplantRegenerationUtility.HasNaniteEmpDisruption(pawn))
                return;
            if (!pawn.IsHashIntervalTick(RepairIntervalTicks, delta))
                return;

            WNGImplantRegenerationUtility.WorstRepairableInjury(pawn)?.Heal(HealPerPulse);
        }
    }
}
