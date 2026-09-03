using System;
using System.Threading;
using System.Threading.Tasks;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Core.Conversation
{
    public interface IVoiceConversationService
    {
        ConversationState CurrentState { get; }

        event Action<ConversationState> StateChanged;

        event Action<string> UserTranscriptReceived;

        event Action<string> AssistantTranscriptReceived;

        event Action<ConversationFailure> ErrorOccurred;

        Task StartAsync(ConversationContext context, CancellationToken cancellationToken);

        Task StopAsync(ConversationStopReason reason, CancellationToken cancellationToken);
    }
}
