using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Commander surface start: one editable Commander on the pawn-selection page
    /// and two additional correctly generated Wraith Warriors at landfall.
    /// The reinforcing pawns use the genuine Wraith Warrior PawnKind, apparel
    /// and player faction rather than relabeling selected Commander pawns.
    /// This part must follow ScenPart_PlayerPawnsArriveMethod in the ScenarioDef.
    /// </summary>
    public sealed class ScenPart_WNGStartingCasteReinforcements : ScenPart
    {
        public string warriorKindDefName = "WNG_WraithWarrior";
        public int warriorCount = 2;
        private bool delivered;

        public override string Summary(Scenario scen)
        {
            return "Two player Wraith Warriors join the Commander at landfall.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref warriorKindDefName, "warriorKindDefName", "WNG_WraithWarrior");
            Scribe_Values.Look(ref warriorCount, "warriorCount", 2);
            Scribe_Values.Look(ref delivered, "wngWarriorReinforcementsDelivered", false);
        }

        public override void GenerateIntoMap(Map map)
        {
            if (delivered || Find.GameInitData == null || map == null)
                return;

            // Do not create Warriors on an unrelated map, or before the exact
            // scenario Commander has landed and joined the player colony.
            Pawn commander = map.mapPawns?.FreeColonistsSpawned?.FirstOrDefault(p =>
                p != null && !p.Dead && p.Faction == Faction.OfPlayer &&
                p.kindDef?.defName == "WNG_WraithCommander");
            if (commander == null)
            {
                Log.Error("[WNG] Commander scenario: starting Commander was not found on the map. " +
                    "Warrior reinforcement spawning was deferred to avoid unintended extra pawns.");
                return;
            }

            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(warriorKindDefName);
            if (kind == null)
            {
                Log.Error("[WNG] Commander scenario: Wraith Warrior PawnKind is unavailable: " +
                    warriorKindDefName);
                return;
            }

            // This happens only during initial map generation. Mark as delivered
            // only after successful placement; keep retries safe if generation is
            // restarted without duplicating the first successful pawn.
            int made = 0;
            for (int i = 0; i < Math.Max(0, Math.Min(6, warriorCount)); i++)
            {
                Pawn warrior = null;
                try
                {
                    warrior = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
                    if (warrior == null)
                        throw new InvalidOperationException("PawnGenerator returned null.");

                    bool placed = GenPlace.TryPlaceThing(warrior, commander.Position,
                        map, ThingPlaceMode.Near);
                    if (!placed || !warrior.Spawned || warrior.Map != map)
                        throw new InvalidOperationException("No suitable landfall tile for Warrior.");

                    made++;
                }
                catch (Exception ex)
                {
                    if (warrior != null && !warrior.Destroyed && !warrior.Spawned)
                    {
                        try { warrior.Destroy(DestroyMode.Vanish); } catch { }
                    }
                    Log.Error("[WNG] Commander scenario Warrior generation failed: " + ex);
                }
            }

            delivered = made == warriorCount;
            if (made > 0)
                Messages.Message("The Wraith Commander landed with " + made +
                    (made == 1 ? " Warrior." : " Warriors."),
                    commander, MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
