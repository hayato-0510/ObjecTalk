using System;
using System.Threading;
using System.Threading.Tasks;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Core.Conversation
{
    public sealed class ObjectConversationCoordinator
    {
        private static readonly TimeSpan DefaultLossGrace = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultSwitchConfirmation = TimeSpan.FromSeconds(2);

        private readonly IClock clock;
        private readonly IConversationObjectRepository repository;
        private readonly IVoiceConversationService voiceService;
        private readonly TimeSpan lossGrace;
        private readonly TimeSpan switchConfirmation;
        private readonly SemaphoreSlim mutationGate = new SemaphoreSlim(1, 1);

        private string activeObjectId;
        private DateTimeOffset activeLastSeenAt;
        private string candidateObjectId;
        private DateTimeOffset candidateFirstSeenAt;

        public ObjectConversationCoordinator(
            IClock clock,
            IConversationObjectRepository repository,
            IVoiceConversationService voiceService,
            TimeSpan? lossGrace = null,
            TimeSpan? switchConfirmation = null)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));
            this.lossGrace = lossGrace ?? DefaultLossGrace;
            this.switchConfirmation = switchConfirmation ?? DefaultSwitchConfirmation;

            if (this.lossGrace <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lossGrace));
            }

            if (this.switchConfirmation <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(switchConfirmation));
            }
        }

        public event Action<ConversationFailure> FailureOccurred;

        public async Task ObserveAsync(RecognizedObjectState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            await mutationGate.WaitAsync();
            try
            {
                await ObserveCoreAsync(state);
            }
            finally
            {
                mutationGate.Release();
            }
        }

        public async Task TickAsync()
        {
            await mutationGate.WaitAsync();
            try
            {
                if (activeObjectId == null)
                {
                    return;
                }

                var now = clock.UtcNow;
                if (now - activeLastSeenAt >= lossGrace)
                {
                    await StopActiveAsync(ConversationStopReason.ObjectLost);
                    return;
                }

                await SwitchIfCandidateConfirmedAsync(now);
            }
            finally
            {
                mutationGate.Release();
            }
        }

        public async Task StopAsync(ConversationStopReason reason)
        {
            await mutationGate.WaitAsync();
            try
            {
                if (activeObjectId == null)
                {
                    return;
                }

                await StopActiveAsync(reason);
            }
            finally
            {
                mutationGate.Release();
            }
        }

        private async Task ObserveCoreAsync(RecognizedObjectState state)
        {
            var now = clock.UtcNow;
            if (!state.IsRecognized || string.IsNullOrWhiteSpace(state.ObjectId))
            {
                ClearCandidate();
                return;
            }

            if (activeObjectId == null)
            {
                await StartInitialConversationAsync(state.ObjectId, now);
                return;
            }

            if (string.Equals(activeObjectId, state.ObjectId, StringComparison.Ordinal))
            {
                activeLastSeenAt = now;
                ClearCandidate();
                return;
            }

            if (!string.Equals(candidateObjectId, state.ObjectId, StringComparison.Ordinal))
            {
                candidateObjectId = state.ObjectId;
                candidateFirstSeenAt = now;
            }

            await SwitchIfCandidateConfirmedAsync(now);
        }

        private async Task StartInitialConversationAsync(string objectId, DateTimeOffset now)
        {
            var context = GetContext(objectId);
            if (context == null)
            {
                return;
            }

            try
            {
                await voiceService.StartAsync(context, CancellationToken.None);
            }
            catch (Exception)
            {
                RaiseFailure("conversation_start_failed", true, "会話を開始できませんでした。");
                return;
            }

            activeObjectId = objectId;
            activeLastSeenAt = now;
            ClearCandidate();
        }

        private async Task SwitchIfCandidateConfirmedAsync(DateTimeOffset now)
        {
            if (candidateObjectId == null || now - candidateFirstSeenAt < switchConfirmation)
            {
                return;
            }

            var objectId = candidateObjectId;
            var context = GetContext(objectId);
            if (context == null)
            {
                ClearCandidate();
                return;
            }

            if (!await StopVoiceServiceAsync(ConversationStopReason.ObjectChanged))
            {
                return;
            }

            ClearActiveSession();
            ClearCandidate();

            try
            {
                await voiceService.StartAsync(context, CancellationToken.None);
            }
            catch (Exception)
            {
                RaiseFailure("conversation_start_failed", true, "会話を開始できませんでした。");
                return;
            }

            activeObjectId = objectId;
            activeLastSeenAt = now;
        }

        private async Task StopActiveAsync(ConversationStopReason reason)
        {
            if (!await StopVoiceServiceAsync(reason))
            {
                return;
            }

            ClearActiveSession();
            ClearCandidate();
        }

        private async Task<bool> StopVoiceServiceAsync(ConversationStopReason reason)
        {
            try
            {
                await voiceService.StopAsync(reason, CancellationToken.None);
                return true;
            }
            catch (Exception)
            {
                RaiseFailure("conversation_stop_failed", true, "会話を停止できませんでした。");
                return false;
            }
        }

        private ConversationContext GetContext(string objectId)
        {
            try
            {
                return repository.GetContext(objectId);
            }
            catch (Exception)
            {
                RaiseFailure("conversation_context_lookup_failed", true, "会話の対象を確認できませんでした。");
                return null;
            }
        }

        private void ClearActiveSession()
        {
            activeObjectId = null;
            activeLastSeenAt = default(DateTimeOffset);
        }

        private void ClearCandidate()
        {
            candidateObjectId = null;
            candidateFirstSeenAt = default(DateTimeOffset);
        }

        private void RaiseFailure(string code, bool isRetryable, string userMessage)
        {
            var handler = FailureOccurred;
            if (handler != null)
            {
                handler(new ConversationFailure(code, isRetryable, userMessage));
            }
        }
    }
}
