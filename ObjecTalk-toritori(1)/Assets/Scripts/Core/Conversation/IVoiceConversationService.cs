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

        // Push-to-talk: the user is holding (true) or has released (false) the talk button. Ignored
        // unless the service was created in push-to-talk mode; then the button is the sole authority
        // over microphone capture and the automatic echo-guard muting is disabled.
        void SetPushToTalkActive(bool active);
    }
}
