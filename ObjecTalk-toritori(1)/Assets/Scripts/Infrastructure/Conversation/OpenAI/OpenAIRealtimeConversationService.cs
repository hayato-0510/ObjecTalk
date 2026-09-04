using System;
using System.Threading;
using System.Threading.Tasks;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Infrastructure.Conversation.Audio;

namespace ObjecTalk.Infrastructure.Conversation.OpenAI
{
    public interface IRealtimeConversationTransport
    {
        event Action<RealtimeEvent> EventReceived;

        Task ConnectAsync(ConversationContext context, CancellationToken cancellationToken);

        Task CancelResponseAsync(CancellationToken cancellationToken);

        void StopRemotePlayback();

        Task DisconnectAsync(CancellationToken cancellationToken);
    }

    public interface IConversationDelayProvider
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class TaskDelayProvider : IConversationDelayProvider
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }

    public sealed class OpenAIRealtimeConversationService : IVoiceConversationService
    {
        private static readonly TimeSpan[] ReconnectDelays =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4)
        };

        private readonly ConversationStateMachine _stateMachine;
        private readonly IMicrophonePermissionService _microphonePermission;
        private readonly IRealtimeConversationTransport _transport;
        private readonly IConversationDelayProvider _delayProvider;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly object _sync = new object();
        private CancellationTokenSource _activeOperation;
        private Action<RealtimeEvent> _transportEventHandler;
        private long _transportGeneration;

        public OpenAIRealtimeConversationService(
            ConversationStateMachine stateMachine,
            IMicrophonePermissionService microphonePermission,
            IRealtimeConversationTransport transport,
            IConversationDelayProvider delayProvider)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _microphonePermission = microphonePermission ?? throw new ArgumentNullException(nameof(microphonePermission));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _delayProvider = delayProvider ?? throw new ArgumentNullException(nameof(delayProvider));
        }

        public ConversationState CurrentState
        {
            get
            {
                lock (_sync)
                {
                    return _stateMachine.State;
                }
            }
        }

        public event Action<ConversationState> StateChanged;

        public event Action<string> UserTranscriptReceived;

        public event Action<string> AssistantTranscriptReceived;

        public event Action<ConversationFailure> ErrorOccurred;

        public async Task StartAsync(ConversationContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            long generation = 0;
            CancelActiveOperation();
            await _operationGate.WaitAsync(CancellationToken.None);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await StopActiveSessionAsync();

                CancellationToken operationToken;
                lock (_sync)
                {
                    generation = _stateMachine.BeginGeneration();
                    _activeOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    operationToken = _activeOperation.Token;
                }

                SubscribeTransportEvents(generation);
                if (!TryPublishState(ConversationState.RequestingPermission, generation))
                {
                    return;
                }

                MicrophonePermissionStatus permission = await _microphonePermission.RequestAsync(operationToken);
                if (permission != MicrophonePermissionStatus.Granted)
                {
                    PublishFailure(CreatePermissionFailure(permission));
                    FinishSession(generation);
                    return;
                }

                TryPublishState(ConversationState.Connecting, generation);
                for (int attempt = 0; ; attempt++)
                {
                    operationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await _transport.ConnectAsync(context, operationToken);
                        if (!IsActiveGeneration(generation))
                        {
                            return;
                        }

                        TryPublishState(ConversationState.Listening, generation);
                        return;
                    }
                    catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                    {
                        FinishSession(generation);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        ConversationFailure failure = CreateConnectionFailure(exception);
                        if (!failure.IsRetryable || attempt >= ReconnectDelays.Length)
                        {
                            PublishFailure(failure);
                            FinishSession(generation);
                            return;
                        }

                        TryPublishState(ConversationState.Reconnecting, generation);
                        await _delayProvider.DelayAsync(ReconnectDelays[attempt], operationToken);
                        if (!IsActiveGeneration(generation))
                        {
                            return;
                        }

                        TryPublishState(ConversationState.Connecting, generation);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (generation != 0)
                {
                    FinishSession(generation);
                }

                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task StopAsync(ConversationStopReason reason, CancellationToken cancellationToken)
        {
            CancelActiveOperation();
            await _operationGate.WaitAsync(CancellationToken.None);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await StopActiveSessionAsync();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task StopActiveSessionAsync()
        {
            long generation;
            ConversationState state;
            lock (_sync)
            {
                generation = _stateMachine.CurrentGeneration;
                state = _stateMachine.State;
            }

            if (state == ConversationState.Idle)
            {
                return;
            }

            TryPublishState(ConversationState.Stopping, generation);
            try
            {
                await _transport.DisconnectAsync(CancellationToken.None);
            }
            finally
            {
                FinishSession(generation);
            }
        }

        private void SubscribeTransportEvents(long generation)
        {
            Action<RealtimeEvent> handler = realtimeEvent => HandleTransportEvent(realtimeEvent, generation);
            lock (_sync)
            {
                _transportEventHandler = handler;
                _transportGeneration = generation;
            }

            _transport.EventReceived += handler;
        }

        private void UnsubscribeTransportEvents(long generation)
        {
            Action<RealtimeEvent> handler = null;
            lock (_sync)
            {
                if (_transportGeneration == generation)
                {
                    handler = _transportEventHandler;
                    _transportEventHandler = null;
                    _transportGeneration = 0;
                }
            }

            if (handler != null)
            {
                _transport.EventReceived -= handler;
            }
        }

        private void HandleTransportEvent(RealtimeEvent realtimeEvent, long generation)
        {
            if (realtimeEvent == null || !IsActiveGeneration(generation))
            {
                return;
            }

            switch (realtimeEvent.Kind)
            {
                case RealtimeEventKind.SpeechStarted:
                    if (CurrentState == ConversationState.Speaking)
                    {
                        ObserveFault(_transport.CancelResponseAsync(CancellationToken.None));
                        _transport.StopRemotePlayback();
                        TryPublishState(ConversationState.Listening, generation);
                    }
                    break;
                case RealtimeEventKind.UserTranscriptCompleted:
                    UserTranscriptReceived?.Invoke(realtimeEvent.Transcript);
                    break;
                case RealtimeEventKind.AssistantTranscriptDelta:
                case RealtimeEventKind.AssistantTranscriptDone:
                    AssistantTranscriptReceived?.Invoke(realtimeEvent.Transcript);
                    break;
                case RealtimeEventKind.ResponseStarted:
                    TryPublishState(ConversationState.Speaking, generation);
                    break;
                case RealtimeEventKind.ResponseDone:
                    TryPublishState(ConversationState.Listening, generation);
                    break;
                case RealtimeEventKind.ApiError:
                    PublishFailure(new ConversationFailure(
                        "realtime_api_error",
                        false,
                        "音声会話で問題が発生しました。もう一度お試しください。"));
                    break;
            }
        }

        private bool TryPublishState(ConversationState state, long generation)
        {
            bool changed;
            lock (_sync)
            {
                changed = _stateMachine.TryTransition(state, generation);
            }

            if (changed)
            {
                StateChanged?.Invoke(state);
            }

            return changed;
        }

        private bool IsActiveGeneration(long generation)
        {
            lock (_sync)
            {
                return generation == _stateMachine.CurrentGeneration &&
                       _activeOperation != null &&
                       !_activeOperation.IsCancellationRequested;
            }
        }

        private void FinishSession(long generation)
        {
            UnsubscribeTransportEvents(generation);
            bool changed = false;
            CancellationTokenSource completedOperation = null;
            lock (_sync)
            {
                if (generation != _stateMachine.CurrentGeneration)
                {
                    return;
                }

                changed = _stateMachine.ForceIdle(generation);
                completedOperation = _activeOperation;
                _activeOperation = null;
            }

            completedOperation?.Dispose();
            if (changed)
            {
                StateChanged?.Invoke(ConversationState.Idle);
            }
        }

        private void CancelActiveOperation()
        {
            CancellationTokenSource operation;
            lock (_sync)
            {
                operation = _activeOperation;
            }

            if (operation != null && !operation.IsCancellationRequested)
            {
                operation.Cancel();
            }
        }

        private static ConversationFailure CreatePermissionFailure(MicrophonePermissionStatus permission)
        {
            return permission == MicrophonePermissionStatus.DeniedPermanently
                ? new ConversationFailure("microphone_permission_denied_permanently", false, "マイクの利用を許可してください。")
                : new ConversationFailure("microphone_permission_denied", false, "マイクの利用を許可してください。");
        }

        private static ConversationFailure CreateConnectionFailure(Exception exception)
        {
            var realtimeException = exception as RealtimeCallException;
            if (realtimeException == null)
            {
                return new ConversationFailure("realtime_connection_failed", false, "音声会話に接続できませんでした。");
            }

            switch (realtimeException.Kind)
            {
                case RealtimeCallFailureKind.Authentication:
                    return new ConversationFailure("authentication_failed", false, "認証を確認してからもう一度お試しください。");
                case RealtimeCallFailureKind.Network:
                    return new ConversationFailure("realtime_network_error", true, "ネットワークに接続できませんでした。再接続しています。");
                case RealtimeCallFailureKind.Timeout:
                    return new ConversationFailure("realtime_timeout", true, "接続がタイムアウトしました。再接続しています。");
                case RealtimeCallFailureKind.ServiceUnavailable:
                    return new ConversationFailure("realtime_service_unavailable", true, "音声会話サービスに接続できませんでした。再接続しています。");
                default:
                    return new ConversationFailure("realtime_connection_failed", false, "音声会話に接続できませんでした。");
            }
        }

        private void PublishFailure(ConversationFailure failure)
        {
            ErrorOccurred?.Invoke(failure);
        }

        private static void ObserveFault(Task task)
        {
            if (task == null || task.IsCompleted)
            {
                return;
            }

            task.ContinueWith(
                completed => { var ignored = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
