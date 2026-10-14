using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Vanilla Odyssey recalculates goodwill while a gravship finishes ArriveNewMap.
    /// GoodwillSituationWorker_SameIdeo assumes every visible non-temporary faction that
    /// participates in goodwill owns a FactionIdeosTracker. Humanlike factions receive one
    /// from FactionGenerator; non-humanlike factions do not.
    ///
    /// WNG deliberately keeps its Replicator faction visible/configurable so ordinary RaidEnemy
    /// selection can use it. Give that exact non-humanlike faction an empty tracker: it still has
    /// no ideology and no ideological gameplay, but vanilla goodwill/gravship arrival can safely
    /// observe PrimaryIdeo == null instead of dereferencing a null tracker.
    ///
    /// This also repairs existing saves created before this safeguard was added.
    /// </summary>
    public sealed class GameComponent_WNGReplicatorFactionVanillaArrivalCompatibility : GameComponent
    {
        private const string ReplicatorFactionDefName = "WNG_ReplicatorSwarm";
        private bool checkedThisSession;

        public GameComponent_WNGReplicatorFactionVanillaArrivalCompatibility(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureTracker();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            EnsureTracker();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            EnsureTracker();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // One late pass covers unusual mod load/faction creation ordering without
            // doing continuous work during play.
            if (!checkedThisSession)
                EnsureTracker();
        }

        private void EnsureTracker()
        {
            if (Find.FactionManager == null)
                return;

            bool found = false;
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction?.def?.defName != ReplicatorFactionDefName)
                    continue;

                found = true;

                if (faction.ideos == null)
                {
                    faction.ideos = new FactionIdeosTracker(faction);
                    Log.Message(
                        "[WNG] Restored vanilla gravship-arrival compatibility for the Replicator faction " +
                        "with an empty ideology tracker (no ideology assigned).");
                }
            }

            checkedThisSession = found;
        }
    }
}
