using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WraithNaniteGravtech
{
    /// <summary>
    /// Explicit dialog-style choice list for WNG actions that previously opened RimWorld FloatMenus.
    /// Keeps multi-option actions visible in a stable reply box instead of a transient context menu.
    /// </summary>
    public sealed class Dialog_WNGChoiceList : Window
    {
        public sealed class Choice
        {
            public readonly string label;
            public readonly Action action;
            public readonly string disabledReason;

            public Choice(string label, Action action, string disabledReason = null)
            {
                this.label = label ?? string.Empty;
                this.action = action;
                this.disabledReason = disabledReason;
            }

            public bool Enabled => action != null && disabledReason.NullOrEmpty();
        }

        private readonly string title;
        private readonly string description;
        private readonly List<Choice> choices;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(760f, 520f);

        public Dialog_WNGChoiceList(string title, string description, IEnumerable<Choice> choices)
        {
            this.title = title ?? string.Empty;
            this.description = description ?? string.Empty;
            this.choices = choices == null ? new List<Choice>() : new List<Choice>(choices);

            doCloseX = true;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(0f, y, inRect.width, 34f);
            Widgets.Label(titleRect, title);
            y += 40f;

            Text.Font = GameFont.Small;
            if (!description.NullOrEmpty())
            {
                float descriptionHeight = Math.Min(120f, Text.CalcHeight(description, inRect.width));
                Rect descriptionRect = new Rect(0f, y, inRect.width, descriptionHeight);
                Widgets.Label(descriptionRect, description);
                y += descriptionHeight + 12f;
            }

            float listHeight = Math.Max(80f, inRect.height - y);
            Rect outRect = new Rect(0f, y, inRect.width, listHeight);
            float rowHeight = 58f;
            float viewHeight = Math.Max(listHeight, choices.Count * rowHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            for (int i = 0; i < choices.Count; i++)
            {
                Choice choice = choices[i];
                Rect row = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 6f);

                if (choice.Enabled)
                {
                    if (Widgets.ButtonText(row, choice.label))
                    {
                        Action action = choice.action;
                        Close();
                        action?.Invoke();
                        break;
                    }
                }
                else
                {
                    Widgets.DrawMenuSection(row);
                    GUI.color = Color.gray;
                    Rect labelRect = row.ContractedBy(8f);
                    string text = choice.label;
                    if (!choice.disabledReason.NullOrEmpty())
                        text += "\n" + choice.disabledReason;
                    Widgets.Label(labelRect, text);
                    GUI.color = Color.white;
                }
            }
            Widgets.EndScrollView();
        }
    }
}
