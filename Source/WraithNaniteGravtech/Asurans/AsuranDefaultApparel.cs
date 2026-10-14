using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WraithNaniteGravtech
{
    internal static class AsuranDefaultApparelUtility
    {
        public static void EnsureRoleApparel(Pawn pawn)
        {
            if (pawn?.apparel == null)
                return;

            string kind = pawn.kindDef?.defName ?? string.Empty;
            string apparelDefName = null;

            switch (kind)
            {
                case "WNG_PrecursorSoldier":
                    apparelDefName = "WNG_PrecursorFieldArmor";
                    break;
                case "WNG_PrecursorCommander":
                    apparelDefName = "WNG_PrecursorCommandArmor";
                    break;
                case "WNG_HumanFormReplicator":
                    apparelDefName = "WNG_HumanFormCombatArmor";
                    break;
                case "WNG_PrecursorEngineer":
                case "WNG_PlayerHumanFormReplicator":
                case "WNG_HumanFormCopy":
                case "WNG_ReplicatorQueenChild":
                    apparelDefName = "WNG_HumanFormUniform";
                    break;
            }

            if (apparelDefName == null)
                return;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(apparelDefName);
            if (def == null || pawn.apparel.WornApparel.Any(a => a?.def == def))
                return;

            Apparel apparel = null;
            try
            {
                apparel = ThingMaker.MakeThing(def) as Apparel;
                if (apparel == null)
                    return;
                pawn.apparel.Wear(apparel, dropReplacedApparel: false);
            }
            catch (Exception ex)
            {
                if (apparel != null && !apparel.Destroyed)
                    apparel.Destroy(DestroyMode.Vanish);
                Log.Warning("[WNG] Could not restore default Asuran apparel for " + pawn.LabelShort + ": " + ex.Message);
            }
        }
    }
}
