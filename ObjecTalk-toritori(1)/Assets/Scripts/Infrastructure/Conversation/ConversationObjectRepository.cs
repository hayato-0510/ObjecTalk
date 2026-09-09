using System;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;
using UnityEngine;

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
                Debug.LogWarning("[TalkDiag] GetContext: objectId is blank.");
                return null;
            }

            string currentUserId = _currentUserId();
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                Debug.LogWarning($"[TalkDiag] GetContext({objectId}): currentUserId is blank (not logged in?).");
                return null;
            }

            ObjectItem item = _database.GetObjectWithDetails(objectId);
            if (item == null)
            {
                Debug.LogWarning($"[TalkDiag] GetContext({objectId}): GetObjectWithDetails returned null.");
                return null;
            }

            if (!string.Equals(item.user_id, currentUserId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[TalkDiag] GetContext({objectId}): user_id mismatch, item.user_id={item.user_id} currentUserId={currentUserId}.");
                return null;
            }

            Debug.Log($"[TalkDiag] GetContext({objectId}): OK, name={item.Name}, promptLength={(item.PersonalityPrompt ?? string.Empty).Length}.");
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
