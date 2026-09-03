using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Core.Conversation
{
    public interface IConversationObjectRepository
    {
        ConversationContext GetContext(string objectId);
    }
}
