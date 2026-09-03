using System;

namespace ObjecTalk.Domain.Conversation
{
    public sealed class ConversationFailure
    {
        public ConversationFailure(string code, bool isRetryable, string userMessage)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            IsRetryable = isRetryable;
            UserMessage = userMessage ?? throw new ArgumentNullException(nameof(userMessage));
        }

        public string Code { get; }

        public bool IsRetryable { get; }

        public string UserMessage { get; }
    }
}
