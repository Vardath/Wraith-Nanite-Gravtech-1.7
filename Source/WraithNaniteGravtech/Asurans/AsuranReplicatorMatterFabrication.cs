using System;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_AbilityAsuranReplicatorMatter : CompProperties_AbilityEffect
    {
        public int blockCount = 25;

        public CompProperties_AbilityAsuranReplicatorMatter()
        {
            compClass = typeof(CompAbilityEffect_AsuranReplicatorMatter);
        }
    }

    public sealed class CompAbilityEffect_AsuranReplicatorMatter : CompAbilityEffect
    {
        private const string ReplicatorMatterDefName = "WNG_ReplicatorMatter";

        public new CompProperties_AbilityAsuranReplicatorMatter Props =>
            (CompProperties_AbilityAsuranReplicatorMatter)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            bool valid = CanFabricate(caster, Math.Max(1, Props.blockCount), out string reason);
            if (!valid && throwMessages && caster != null && !reason.NullOrEmpty())
                Messages.Message(reason, caster, MessageTypeDefOf.RejectInput, false);
            return valid && base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            int count = Math.Max(1, Props.blockCount);
            if (!CanFabricate(caster, count, out string reason))
            {
                if (caster?.Faction == Faction.OfPlayer && !reason.NullOrEmpty())
                    Messages.Message(reason, caster, MessageTypeDefOf.RejectInput, false);
                return;
            }

            ThingDef matterDef = DefDatabase<ThingDef>.GetNamedSilentFail(ReplicatorMatterDefName);
            Gene_Resource_NaniteReserve reserve =
                caster.genes?.GetFirstGeneOfType<Gene_Resource_NaniteReserve>();
            if (matterDef == null || reserve == null)
                return;

            Thing stack = ThingMaker.MakeThing(matterDef);
            stack.stackCount = Math.Min(count, Math.Max(1, matterDef.stackLimit));

            bool placed = false;
            try
            {
                placed = GenPlace.TryPlaceThing(stack, caster.Position, caster.Map, ThingPlaceMode.Near);
                if (!placed)
                {
                    if (!stack.Destroyed)
                        stack.Destroy(DestroyMode.Vanish);
                    if (caster.Faction == Faction.OfPlayer)
                        Messages.Message("There is no valid nearby cell for the Replicator Blocks.", caster, MessageTypeDefOf.RejectInput, false);
                    return;
                }

                float cost = WNGSettingsUtility.AsuranBlockFabricationReserveCost;
                if (!reserve.TrySpend(cost))
                {
                    if (!stack.Destroyed)
                        stack.Destroy(DestroyMode.Vanish);
                    if (caster.Faction == Faction.OfPlayer)
                        Messages.Message("Nanite Reserve changed before fabrication could commit; no Blocks were created.", caster, MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (caster.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        caster.LabelShortCap + " fabricated " + stack.stackCount +
                        " Replicator Blocks at a cost of " + (cost * 100f).ToString("0") +
                        "% Nanite Reserve.",
                        stack,
                        MessageTypeDefOf.PositiveEvent,
                        false);
                }
            }
            catch (Exception ex)
            {
                if (!placed && stack != null && !stack.Destroyed)
                    stack.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Asuran Replicator Block fabrication failed: " + ex);
            }
        }

        private static bool CanFabricate(Pawn caster, int count, out string reason)
        {
            reason = null;
            if (caster == null || caster.Destroyed || caster.Dead || !caster.Spawned || caster.Map == null)
            {
                reason = "The nanite fabricator must be alive and physically present on a map.";
                return false;
            }

            if (caster.Faction != Faction.OfPlayer)
            {
                reason = "Replicator Block fabrication requires a player-controlled nanite gene carrier.";
                return false;
            }

            // WNG_NaniteReserve is an Asuran-origin resource gene. This same
            // finite physical reserve powers fabrication for all Asuran castes,
            // human-form Replicator copies and any other pawn inheriting the
            // gene (including custom xenotypes). Never restrict by PawnKind or
            // exact xenotype: gene inheritance is the authoritative source.
            if (AsuranCollectiveUtility.IsDisrupted(caster))
            {
                reason = "EMP disruption prevents controlled Replicator Block fabrication.";
                return false;
            }

            Gene_Resource_NaniteReserve reserve =
                caster.genes?.GetFirstGeneOfType<Gene_Resource_NaniteReserve>();
            if (reserve == null || !reserve.Active)
            {
                reason = "This pawn has no active Nanite Reserve gene.";
                return false;
            }

            float cost = WNGSettingsUtility.AsuranBlockFabricationReserveCost;
            if (!reserve.CanSpend(cost))
            {
                reason = "Requires " + (cost * 100f).ToString("0") +
                         "% Nanite Reserve; current reserve is " + (reserve.ValuePercent * 100f).ToString("0") + "%.";
                return false;
            }

            ThingDef matterDef = DefDatabase<ThingDef>.GetNamedSilentFail(ReplicatorMatterDefName);
            if (matterDef == null)
            {
                reason = "The Replicator Block definition is unavailable.";
                return false;
            }
            if (count <= 0 || matterDef.stackLimit <= 0)
            {
                reason = "The configured Replicator Block output is invalid.";
                return false;
            }

            return true;
        }
    }
}
