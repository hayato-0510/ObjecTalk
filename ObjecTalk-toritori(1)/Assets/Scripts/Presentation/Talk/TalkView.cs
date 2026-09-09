using System;
using System.Collections.Generic;
using ObjecTalk.Domain.Conversation;
using TMPro;
using UnityEngine;

namespace ObjecTalk.Presentation.Talk
{
    public sealed class TalkView : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text transcriptText;
        private string currentObjectName = string.Empty;
        private ConversationState lastState = ConversationState.Idle;
        private bool toolActive;

        // Read-aloud question prompts for the recognised object's category. Display only — no tap
        // handling. Built under the Talk canvas when there is one, otherwise under this component so
        // it stays inspectable in tests.
        private GameObject suggestionPanel;
        private IReadOnlyList<string> shownSuggestions = Array.Empty<string>();

        /// <summary>The prompts currently on screen, empty when none are shown.</summary>
        public IReadOnlyList<string> ShownSuggestions => shownSuggestions;

        public void Configure(TMP_Text status, TMP_Text transcript)
        {
            statusText = status;
            transcriptText = transcript;
        }

        /// <summary>
        /// Shows "こう聞いてみよう" plus one line per prompt. An empty list clears any current panel.
        /// </summary>
        public void ShowSuggestions(IReadOnlyList<string> suggestions)
        {
            ClearSuggestions();
            if (suggestions == null || suggestions.Count == 0)
            {
                return;
            }

            shownSuggestions = new List<string>(suggestions);

            var parent = ResolveSuggestionParent();
            suggestionPanel = new GameObject("CategorySuggestions", typeof(RectTransform));
            var rect = (RectTransform)suggestionPanel.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -160f);
            rect.sizeDelta = new Vector2(900f, 60f + 64f * suggestions.Count);

            AddLabel(rect, "こう聞いてみよう", 0f, 34f, FontStyles.Bold, new Color(0.15f, 0.35f, 0.24f));
            for (var i = 0; i < suggestions.Count; i++)
            {
                AddLabel(rect, "・" + suggestions[i], -(52f + 64f * i), 40f, FontStyles.Normal, new Color(0.1f, 0.1f, 0.12f));
            }
        }

        public void ClearSuggestions()
        {
            shownSuggestions = Array.Empty<string>();
            if (suggestionPanel != null)
            {
                Destroy(suggestionPanel);
                suggestionPanel = null;
            }
        }

        private Transform ResolveSuggestionParent()
        {
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.transform : transform;
        }

        private static void AddLabel(RectTransform parent, string text, float y, float fontSize, FontStyles style, Color color)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(16f, 0f);
            rect.offsetMax = new Vector2(-16f, 0f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, fontSize + 16f);
            try
            {
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = text;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.fontStyle = style;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 16f;
                tmp.fontSizeMax = fontSize;
                tmp.color = color;
                tmp.raycastTarget = false;
            }
            catch (Exception)
            {
                // A missing TMP font must not stop the prompts from existing; they just have no glyphs.
            }
        }

        public void SetCurrentObjectName(string objectName)
        {
            currentObjectName = objectName ?? string.Empty;
        }

        public void ShowState(ConversationState state)
        {
            lastState = state;

            if (statusText != null && !toolActive)
            {
                statusText.text = MapState(state);
            }

            if (state == ConversationState.Idle && transcriptText != null)
            {
                transcriptText.text = string.Empty;
            }
        }

        /// <summary>
        /// While a tool the model asked for is running, the status line reads "調べているよ…"; when it
        /// finishes, the normal state text comes back.
        /// </summary>
        public void ShowToolActivity(bool active)
        {
            toolActive = active;
            if (statusText == null)
            {
                return;
            }

            statusText.text = active ? "調べているよ…" : MapState(lastState);
        }

        public void ShowFailure(ConversationFailure failure)
        {
            if (statusText != null && failure != null)
            {
                statusText.text = failure.UserMessage;
            }
        }

        public void ShowTranscript(string transcript)
        {
            if (transcriptText != null)
            {
                transcriptText.text = transcript ?? string.Empty;
            }
        }

        private string MapState(ConversationState state)
        {
            switch (state)
            {
                case ConversationState.RequestingPermission:
                case ConversationState.Connecting:
                    return "接続中";
                case ConversationState.Listening:
                    return "聞いています";
                case ConversationState.Speaking:
                    return string.IsNullOrWhiteSpace(currentObjectName) ? "話しています" : currentObjectName + "が話しています";
                case ConversationState.Reconnecting:
                    return "再接続中";
                case ConversationState.Stopping:
                    return "終了しています";
                default:
                    return string.Empty;
            }
        }
    }
}
