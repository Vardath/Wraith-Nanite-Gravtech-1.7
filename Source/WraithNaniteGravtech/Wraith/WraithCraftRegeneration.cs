using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_WraithCraftRegeneration : CompProperties
    {
        public int repairIntervalTicks = 900;
        public int healPerPulse = 8;
        public float biomassPerPulse = 1f;
        public CompProperties_WraithCraftRegeneration() => compClass = typeof(CompWraithCraftRegeneration);
    }

    public sealed class CompWraithCraftRegeneration : ThingComp
    {
        private float fractionalBiomassDebt;
        private CompProperties_WraithCraftRegeneration Props => (CompProperties_WraithCraftRegeneration)props;

        public override void CompTick()
        {
            base.CompTick();
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.HitPoints >= parent.MaxHitPoints ||
                !parent.IsHashIntervalTick(Math.Max(60, Props.repairIntervalTicks))) return;

            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            ThingOwner cargo = transporter?.GetDirectlyHeldThings();
            if (cargo == null) return;

            float nextDebt = fractionalBiomassDebt + Math.Max(0f, Props.biomassPerPulse);
            int consume = Math.Max(1, (int)Math.Floor(nextDebt + 0.0001f));
            int available = cargo.Where(t => t?.def?.defName == "WNG_Biomass").Sum(t => t.stackCount);
            if (available < consume) return;

            int left = consume;
            foreach (Thing stack in cargo.Where(t => t?.def?.defName == "WNG_Biomass").ToList())
            {
                if (left <= 0) break;
                int take = Math.Min(left, stack.stackCount);
                Thing consumed = cargo.Take(stack, take);
                if (consumed != null && !consumed.Destroyed) consumed.Destroy(DestroyMode.Vanish);
                left -= take;
            }
            if (left > 0) return;

            fractionalBiomassDebt = Math.Max(0f, nextDebt - consume);
            parent.HitPoints = Math.Min(parent.MaxHitPoints, parent.HitPoints + Math.Max(1, Props.healPerPulse));
        }

        public override string CompInspectStringExtra()
        {
            if (parent == null || parent.HitPoints >= parent.MaxHitPoints) return null;
            CompTransporter transporter = parent.TryGetComp<CompTransporter>();
            int biomass = transporter?.GetDirectlyHeldThings()?.Where(t => t?.def?.defName == "WNG_Biomass").Sum(t => t.stackCount) ?? 0;
            return "Living-hull repair biomass: " + biomass;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fractionalBiomassDebt, "wngCraftRepairBiomassDebt", 0f);
        }
    }
}
