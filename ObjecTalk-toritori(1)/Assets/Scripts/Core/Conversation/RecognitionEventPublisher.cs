using System;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Core.Conversation
{
    public sealed class RecognitionEventPublisher
    {
        private bool _legacyIsRecognized;
        private string _legacyObjectName;

        public RecognitionEventPublisher(bool isRecognized = false, string objectName = "")
        {
            _legacyIsRecognized = isRecognized;
            _legacyObjectName = isRecognized ? objectName ?? string.Empty : string.Empty;
        }

        public event Action<RecognizedObjectState> ObservationPublished;

        public event Action<bool, string> LegacyStateChanged;

        public bool PublishObservation(
            bool isRecognized,
            string objectId,
            string objectName,
            float confidence,
            DateTimeOffset observedAt)
        {
            string normalizedId = isRecognized ? objectId ?? string.Empty : string.Empty;
            string normalizedName = isRecognized ? objectName ?? string.Empty : string.Empty;

            ObservationPublished?.Invoke(new RecognizedObjectState(
                normalizedId,
                normalizedName,
                isRecognized,
                confidence,
                observedAt));

            return PublishLegacyState(isRecognized, normalizedName);
        }

        public bool PublishLegacyState(bool isRecognized, string objectName)
        {
            string normalizedName = isRecognized ? objectName ?? string.Empty : string.Empty;

            bool legacyChanged = _legacyIsRecognized != isRecognized
                || !string.Equals(_legacyObjectName, normalizedName, StringComparison.Ordinal);
            if (!legacyChanged)
            {
                return false;
            }

            _legacyIsRecognized = isRecognized;
            _legacyObjectName = normalizedName;
            LegacyStateChanged?.Invoke(isRecognized, normalizedName);
            return true;
        }
    }
}
