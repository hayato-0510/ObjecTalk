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
                UnityEngine.Debug.LogWarning("[TalkDiag] GetContext: objectId is null/empty");
                return null;
            }

            string currentUserId = _currentUserId();
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                UnityEngine.Debug.LogWarning($"[TalkDiag] GetContext: currentUserId is null/empty for objectId='{objectId}'");
                return null;
            }

            ObjectItem item = _database.GetObjectWithDetails(objectId);
            if (item == null || !string.Equals(item.user_id, currentUserId, StringComparison.Ordinal))
            {
                UnityEngine.Debug.LogWarning(
                    $"[TalkDiag] GetContext: no match for objectId='{objectId}' " +
                    $"item={(item == null ? "NULL" : $"user_id='{item.user_id}' name='{item.Name}'")} currentUserId='{currentUserId}'");
                return null;
            }

            UnityEngine.Debug.Log($"[TalkDiag] GetContext: resolved objectId='{objectId}' name='{item.Name}' promptLen={(item.PersonalityPrompt ?? string.Empty).Length}");
            return new ConversationContext(
                item.object_id,
                item.Name,
                item.PersonalityPrompt,
                currentUserId,
                "ja");
        }
    }
}
