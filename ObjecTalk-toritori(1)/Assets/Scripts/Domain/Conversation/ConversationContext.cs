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
            string language,
            string category = "")
        {
            ObjectId = objectId ?? throw new ArgumentNullException(nameof(objectId));
            ObjectName = objectName ?? throw new ArgumentNullException(nameof(objectName));
            PersonalityPrompt = personalityPrompt ?? string.Empty;
            UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            Language = string.IsNullOrWhiteSpace(language) ? "ja" : language;
            Category = category ?? string.Empty;
        }

        public string ObjectId { get; }

        public string ObjectName { get; }

        public string PersonalityPrompt { get; }

        public string UserId { get; }

        public string Language { get; }

        /// <summary>
        /// The registered object's free-text category, trimmed. Empty when unset. The backend maps
        /// an exact match against a small label set to an extra persona line; the Talk UI uses it to
        /// pick read-aloud question prompts.
        /// </summary>
        public string Category { get; }
    }
}
