using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WraithNaniteGravtech
{
    public sealed class CompProperties_DormantChildsToyArtifact : CompProperties
    {
        public PawnKindDef pawnKindDef;

        public CompProperties_DormantChildsToyArtifact()
        {
            compClass = typeof(CompDormantChildsToyArtifact);
        }
    }

    /// <summary>
    /// One-use archaeological Child's Toy casing. Activation creates the current dedicated
    /// player Toy body, then consumes the casing. It deliberately grants no Overseer relation:
    /// native Mechanitor control remains a separate player action and the existing Toy feral
    /// timer is authoritative if control is never established.
    /// </summary>
    public sealed class CompDormantChildsToyArtifact : ThingComp
    {
        private bool activationCommitted;

        private CompProperties_DormantChildsToyArtifact Props =>
            (CompProperties_DormantChildsToyArtifact)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            Command_Action command = new Command_Action
            {
                defaultLabel = "Activate Child's Toy",
                defaultDesc = "Wake the dormant Replicator artifact. If activation succeeds, the casing unfolds into one player-faction Child's Toy. The new Toy is not automatically assigned an Overseer; establish normal Mechanitor control before its uncontrolled feral timer expires.",
                icon = TexCommand.Install,
                action = TryActivate
            };

            if (activationCommitted)
                command.Disable("The artifact has already committed its activation.");
            else if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                command.Disable("The artifact must be physically placed on a map.");

            yield return command;
        }

        public override string CompInspectStringExtra()
        {
            return activationCommitted
                ? "Activation: spent; residual casing inert."
                : "Activation: dormant.";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref activationCommitted, "wngDormantChildsToyActivationCommitted", false);
        }

        private void TryActivate()
        {
            if (activationCommitted || parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
                return;

            PawnKindDef toyKind = Props.pawnKindDef;
            if (toyKind == null || toyKind.defName != "WNG_ChildsToy")
            {
                Messages.Message(
                    "The dormant Child's Toy cannot activate because its current Toy definition is unavailable.",
                    parent,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            Pawn toy = null;

            try
            {
                toy = PawnGenerator.GeneratePawn(toyKind, Faction.OfPlayer);
                if (toy == null || !GenPlace.TryPlaceThing(toy, origin, map, ThingPlaceMode.Near))
                {
                    if (toy != null && !toy.Destroyed)
                        toy.Destroy(DestroyMode.Vanish);
                    Messages.Message(
                        "The Child's Toy could not unfold here. The dormant artifact remains intact.",
                        parent,
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }

                // Placement is the commit point. Latch before cleanup so a post-placement source
                // cleanup exception can never produce a second Toy from the same exact artifact.
                activationCommitted = true;

                try
                {
                    parent.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    Log.Error("[WNG] Dormant Child's Toy activation committed, but casing cleanup failed: " + ex);
                }

                try
                {
                    Messages.Message(
                        "The dormant Child's Toy has unfolded into a colony mech. It still requires normal Mechanitor control; if left uncontrolled for the configured delay, it will become a hostile Replicator Drone.",
                        toy,
                        MessageTypeDefOf.PositiveEvent,
                        true);
                }
                catch (Exception ex)
                {
                    Log.Warning("[WNG] Dormant Child's Toy activation committed, but presentation failed: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                if (!activationCommitted && toy != null && !toy.Destroyed)
                    toy.Destroy(DestroyMode.Vanish);
                Log.Error("[WNG] Dormant Child's Toy activation failed before commit: " + ex);
            }
        }
    }

    /// <summary>
    /// Rare Exotic-trader acquisition for one dormant Child's Toy artifact. This is independent
    /// of the normal Replicator Gestation recipe: buying an artifact does not unlock research,
    /// supply a Mechanitor, or bypass the current control/feral contract after activation.
    /// </summary>
    public sealed class StockGenerator_WNGRareChildsToyArtifact : StockGenerator
    {
        public ThingDef thingDef;
        public float chance = 0.07f;

        public override IEnumerable<Thing> GenerateThings(
            PlanetTile forTile,
            Faction faction = null)
        {
            if (thingDef == null || Rand.Value > Math.Max(0f, Math.Min(1f, chance)))
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

        public override IEnumerable<string> ConfigErrors(TraderKindDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;

            if (thingDef == null)
                yield return "StockGenerator_WNGRareChildsToyArtifact requires thingDef.";
            else if (thingDef.defName != "WNG_DormantChildsToy")
                yield return "StockGenerator_WNGRareChildsToyArtifact must target WNG_DormantChildsToy.";
            else if (!thingDef.tradeability.TraderCanSell())
                yield return thingDef.defName + " must remain trader-sellable.";

            if (chance < 0f || chance > 1f)
                yield return "StockGenerator_WNGRareChildsToyArtifact chance must be between 0 and 1.";
        }
    }
}
