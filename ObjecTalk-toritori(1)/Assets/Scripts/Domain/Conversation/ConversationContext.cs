using System;

namespace ObjecTalk.Domain.Conversation
{
    public sealed class ConversationContext
    {
        public ConversationContext(
            string objectId,
            string objectName,
            string personalityPrompt,
            string userId,
            string language)
        {
            ObjectId = objectId ?? throw new ArgumentNullException(nameof(objectId));
            ObjectName = objectName ?? throw new ArgumentNullException(nameof(objectName));
            PersonalityPrompt = personalityPrompt ?? string.Empty;
            UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            Language = string.IsNullOrWhiteSpace(language) ? "ja" : language;
        }

        public string ObjectId { get; }

        public string ObjectName { get; }

        public string PersonalityPrompt { get; }

        public string UserId { get; }

        public string Language { get; }
    }
}
