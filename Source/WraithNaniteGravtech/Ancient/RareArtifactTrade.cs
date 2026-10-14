using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Restored rare acquisition route for one physical Ancient vacuum-energy specimen.
    /// Buying a module does not unlock research or manufacture anything automatically; the
    /// current native analyzable/research gate remains authoritative.
    /// </summary>
    public sealed class StockGenerator_WNGRareVacuumModule : StockGenerator
    {
        public ThingDef thingDef;
        public float chance = 0.04f;

        public override IEnumerable<Thing> GenerateThings(
            PlanetTile forTile,
            Faction faction = null)
        {
            if (thingDef == null ||
                Rand.Value > Math.Max(0f, Math.Min(1f, chance)))
                yield break;

            Thing item = ThingMaker.MakeThing(thingDef);
            if (item != null)
            {
                item.stackCount = 1;
                yield return item;
            }
        }

        public override bool HandlesThingDef(ThingDef def)
        {
            return def == thingDef;
        }

        public override IEnumerable<string> ConfigErrors(
            TraderKindDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;

            if (thingDef == null)
                yield return "StockGenerator_WNGRareVacuumModule requires thingDef.";
            else if (thingDef.defName != "WNG_VacuumEnergyModule")
                yield return "StockGenerator_WNGRareVacuumModule must target WNG_VacuumEnergyModule.";
            else if (!thingDef.tradeability.TraderCanSell())
                yield return thingDef.defName + " must remain trader-sellable.";

            if (chance < 0f || chance > 1f)
                yield return "StockGenerator_WNGRareVacuumModule chance must be between 0 and 1.";
        }
    }
}
