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

        public void Configure(TMP_Text status, TMP_Text transcript)
        {
            statusText = status;
            transcriptText = transcript;
        }

        public void SetCurrentObjectName(string objectName)
        {
            currentObjectName = objectName ?? string.Empty;
        }

        public void ShowState(ConversationState state)
        {
            if (statusText != null)
            {
                statusText.text = MapState(state);
            }

            if (state == ConversationState.Idle && transcriptText != null)
            {
                transcriptText.text = string.Empty;
            }
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
