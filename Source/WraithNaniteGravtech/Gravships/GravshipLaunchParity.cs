using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Keeps WNG pilot consoles on Odyssey's actual GravshipLaunch path.
    ///
    /// This does not add a WNG launch gizmo and does not integrate with another gravship mod.
    /// It repairs only the vanilla Odyssey GravshipLaunch precept instance on player ideologies:
    /// - the ritual remains an anytime ritual, as vanilla ships expect;
    /// - the target worker is RimWorld's own RitualObligationTargetWorker_GravshipLaunch;
    /// - WNG consoles are accepted only because they carry RimWorld's real CompPilotConsole.
    ///
    /// The important distinction from the previous repair is that an existing-but-corrupted or
    /// externally-replaced GravshipLaunch precept is repaired too. The old implementation skipped
    /// every ideology as soon as the precept already existed.
    /// </summary>
    public sealed class GameComponent_WNGGravshipLaunchParity : GameComponent
    {
        private bool repairedThisSession;

        public GameComponent_WNGGravshipLaunchParity(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureVanillaLaunchRitual();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            EnsureVanillaLaunchRitual();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            EnsureVanillaLaunchRitual();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // One late retry covers faction/ideology initialization ordering.
            if (!repairedThisSession)
                EnsureVanillaLaunchRitual();
        }

        private void EnsureVanillaLaunchRitual()
        {
            if (!ModsConfig.OdysseyActive || !ModsConfig.IdeologyActive)
            {
                repairedThisSession = true;
                return;
            }

            Faction player = Faction.OfPlayerSilentFail;
            if (player?.ideos == null)
                return;

            PreceptDef launchDef = PreceptDefOf.GravshipLaunch;
            RitualPatternDef vanillaPattern = launchDef?.ritualPatternBase;
            RitualObligationTargetFilterDef vanillaTargetDef =
                DefDatabase<RitualObligationTargetFilterDef>.GetNamedSilentFail("GravshipLaunch");

            if (launchDef == null || vanillaPattern == null || vanillaTargetDef == null)
                return;

            bool sawPlayerIdeology = false;

            foreach (Ideo ideo in player.ideos.AllIdeos)
            {
                if (ideo == null)
                    continue;

                sawPlayerIdeology = true;
                Precept_Ritual ritual = ideo.GetPrecept(launchDef) as Precept_Ritual;
                bool changed = false;

                if (ritual == null)
                {
                    ritual = PreceptMaker.MakePrecept(launchDef) as Precept_Ritual;
                    if (ritual == null)
                        continue;

                    ideo.AddPrecept(ritual, init: true, fillWith: vanillaPattern);
                    changed = true;
                }

                // Vanilla Odyssey ships expose Launch Gravship from an anytime GravshipLaunch ritual.
                // If an old save or another definition pass left this false, Thing.GetGizmos has no
                // ritual command to yield even though CompPilotConsole already sees its engine.
                if (!ritual.isAnytime)
                {
                    ritual.isAnytime = true;
                    changed = true;
                }

                if (!ritual.canBeAnytime)
                {
                    ritual.canBeAnytime = true;
                    changed = true;
                }

                if (ritual.sourcePattern != vanillaPattern)
                {
                    ritual.sourcePattern = vanillaPattern;
                    changed = true;
                }

                // Force the actual RimWorld worker class, not a replacement supplied through a
                // mutated Def. Its CanUseTargetInternal accepts any spawned Thing carrying the
                // real CompPilotConsole, which is exactly how WNG's three consoles are defined.
                if (!(ritual.obligationTargetFilter is RitualObligationTargetWorker_GravshipLaunch))
                {
                    ritual.obligationTargetFilter =
                        new RitualObligationTargetWorker_GravshipLaunch(vanillaTargetDef)
                        {
                            parent = ritual
                        };
                    changed = true;
                }
                else if (ritual.obligationTargetFilter.parent != ritual)
                {
                    ritual.obligationTargetFilter.parent = ritual;
                    changed = true;
                }

                // A null behavior is invalid for the launch ritual and can occur on damaged legacy
                // save state. Rebuild only that missing vanilla worker; do not replace healthy state.
                if (ritual.behavior == null && vanillaPattern.ritualBehavior != null)
                {
                    ritual.behavior = vanillaPattern.ritualBehavior.GetInstance();
                    changed = true;
                }

                if (changed)
                {
                    Log.Message(
                        "[WNG] Repaired vanilla Odyssey GravshipLaunch state for player ideology '" +
                        ideo.name + "'.");
                }
            }

            repairedThisSession = sawPlayerIdeology;
        }
    }
}
