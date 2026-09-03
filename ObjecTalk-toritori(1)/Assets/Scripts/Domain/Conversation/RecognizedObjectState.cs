using System;

namespace ObjecTalk.Domain.Conversation
{
    public sealed class RecognizedObjectState
    {
        public RecognizedObjectState(
            string objectId,
            string objectName,
            bool isRecognized,
            float confidence,
            DateTimeOffset observedAt)
        {
            ObjectId = objectId;
            ObjectName = objectName;
            IsRecognized = isRecognized;
            Confidence = confidence;
            ObservedAt = observedAt.ToUniversalTime();
        }

        public string ObjectId { get; }

        public string ObjectName { get; }

        public bool IsRecognized { get; }

        public float Confidence { get; }

        public DateTimeOffset ObservedAt { get; }
    }
}
