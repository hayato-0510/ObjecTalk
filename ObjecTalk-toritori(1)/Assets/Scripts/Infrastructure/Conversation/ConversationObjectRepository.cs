using System;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Infrastructure.Conversation
{
    public sealed class ConversationObjectRepository : IConversationObjectRepository
    {
        private readonly DatabaseManager _database;
        private readonly Func<string> _currentUserId;

        public ConversationObjectRepository(DatabaseManager database, Func<string> currentUserId)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _currentUserId = currentUserId ?? throw new ArgumentNullException(nameof(currentUserId));
        }

        public ConversationContext GetContext(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return null;
            }

            string currentUserId = _currentUserId();
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return null;
            }

            ObjectItem item = _database.GetObjectWithDetails(objectId);
            if (item == null || !string.Equals(item.user_id, currentUserId, StringComparison.Ordinal))
            {
                return null;
            }

            return new ConversationContext(
                item.object_id,
                item.Name,
                item.PersonalityPrompt,
                currentUserId,
                "ja",
                (item.Category ?? string.Empty).Trim());
        }
    }
}
