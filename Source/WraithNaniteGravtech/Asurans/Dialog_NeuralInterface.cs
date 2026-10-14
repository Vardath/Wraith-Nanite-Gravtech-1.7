using RimWorld;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Modal operation chooser. forcePause uses RimWorld window ownership so closing with an
    /// operation, X or Escape releases only the pause owned by this window.
    /// </summary>
    public sealed class Dialog_NeuralInterface : Window
    {
        private readonly Pawn caster;
        private readonly Pawn subject;
        private readonly float copyReserveCost;
        private readonly PawnKindDef fallbackCopyPawnKind;

        public override Vector2 InitialSize => new Vector2(580f, 445f);

        public Dialog_NeuralInterface(Pawn caster, Pawn subject, float copyReserveCost, PawnKindDef fallbackCopyPawnKind)
        {
            this.caster = caster;
            this.subject = subject;
            this.copyReserveCost = copyReserveCost;
            this.fallbackCopyPawnKind = fallbackCopyPawnKind;

            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "Neural Interface");

            Text.Font = GameFont.Small;
            string targetName = subject?.LabelShortCap ?? "Unavailable target";
            Widgets.Label(
                new Rect(0f, 42f, inRect.width, 52f),
                $"Target: {targetName}\nChoose the operation to perform through the nanite interface.");

            float y = 104f;
            DrawActionButton(inRect, ref y,
                "Rewrite allegiance",
                "Rewrite the target into the operator's faction.",
                () => NeuralInterfaceUtility.TryRecruit(caster, subject));
            DrawActionButton(inRect, ref y,
                "Impose prisoner status",
                "Convert a valid downed/captive target into a native prisoner state.",
                () => NeuralInterfaceUtility.TryImprison(caster, subject));

            if (ModsConfig.IdeologyActive)
            {
                DrawActionButton(inRect, ref y,
                    "Impose slave status",
                    "Convert a valid downed/captive target into a native Ideology slave state.",
                    () => NeuralInterfaceUtility.TryEnslave(caster, subject));
            }

            DrawActionButton(inRect, ref y,
                "Copy skills and passions",
                "Non-destructively acquire the strongest learned skill pattern from the target.",
                () => NeuralInterfaceUtility.TryCopySkills(caster, subject));

            Gene_Resource_NaniteReserve reserve = caster?.genes?.GetFirstGeneOfType<Gene_Resource_NaniteReserve>();
            string reserveText = reserve == null ? "no active reserve" : $"{reserve.Value:P0} reserve";
            if (WNGSettingsUtility.HumanFormCopyingEnabled)
            {
                DrawActionButton(inRect, ref y,
                    $"Build human-form copy — {copyReserveCost:P0} reserve",
                    $"Reconstruct a naked nanite copy while preserving compatible source identity, biography, skills, appearance and genome. Current operator: {reserveText}.",
                    () => NeuralInterfaceUtility.TryBuildCopy(caster, subject, copyReserveCost, fallbackCopyPawnKind));
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(
                    new Rect(8f, y, inRect.width - 16f, 46f),
                    "Human-form reconstruction is disabled in Wraith & Nanite Gravtech mod settings. Other Neural Interface operations remain available.");
                GUI.color = Color.white;
                y += 52f;
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 40f, 120f, 40f), "Close"))
                Close();
        }

        private void DrawActionButton(Rect inRect, ref float y, string label, string description, System.Action action)
        {
            Rect button = new Rect(0f, y, inRect.width, 36f);
            if (Widgets.ButtonText(button, label))
            {
                Close();
                action?.Invoke();
                return;
            }

            y += 39f;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(8f, y, inRect.width - 16f, 28f), description);
            GUI.color = Color.white;
            y += 33f;
        }
    }
}
