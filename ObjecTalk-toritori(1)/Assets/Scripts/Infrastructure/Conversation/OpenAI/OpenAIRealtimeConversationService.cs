using System;
using System.Collections.Generic;
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

        // Resumes the remote audio sink after a barge-in stopped it, so a later assistant turn on
        // the same WebRTC track is audible again.
        void StartRemotePlayback();

        // Half-duplex echo guard: the microphone track is silenced while the assistant speaks so its
        // own voice, played through the device speaker, cannot loop back and trip server-side VAD.
        void SetLocalAudioCaptureEnabled(bool enabled);

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

        // The client has no acoustic echo cancellation, so the microphone stays muted while the
        // assistant is audible. It reopens this long after output_audio_buffer.stopped (the reply
        // has actually finished leaving the speaker), leaving room only for the acoustic tail.
        private static readonly TimeSpan DefaultMicrophoneResumeGrace = TimeSpan.FromMilliseconds(400);

        // Fallback only: output_audio_buffer.stopped is a WebRTC event that should always arrive,
        // but if it is ever dropped the mic must not stay muted forever, so response.done arms a
        // slower resume as a safety net.
        private static readonly TimeSpan DefaultMicrophoneResumeFallbackGrace = TimeSpan.FromMilliseconds(3000);

        private readonly ConversationStateMachine _stateMachine;
        private readonly IMicrophonePermissionService _microphonePermission;
        private readonly IRealtimeConversationTransport _transport;
        private readonly IConversationDelayProvider _delayProvider;
        private readonly TimeSpan _microphoneResumeGrace;
        private readonly TimeSpan _microphoneResumeFallbackGrace;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly object _sync = new object();
        private readonly object _publishedEventsSync = new object();
        private readonly Queue<Action> _publishedEvents = new Queue<Action>();
        private CancellationTokenSource _activeOperation;
        private Action<RealtimeEvent> _transportEventHandler;
        private long _transportGeneration;
        private int _lifecycleOperationCount;
        private int _isDispatchingPublishedEvents;

        // When a barge-in makes us send response.cancel, the server may already have finished that
        // response and answers with an "error" event ("cancellation failed: no active response").
        // That race is benign, so an ApiError arriving within this window of our cancel is swallowed
        // instead of shown to the user. UTC ticks; 0 means no cancel is outstanding.
        private static readonly TimeSpan ResponseCancelErrorGrace = TimeSpan.FromSeconds(3);
        private long _responseCancelErrorSuppressedUntilTicks;

        // Push-to-talk: microphone capture is driven solely by SetPushToTalkActive, and every
        // automatic echo-guard mute/unmute is skipped.
        private readonly bool _pushToTalk;

        public OpenAIRealtimeConversationService(
            ConversationStateMachine stateMachine,
            IMicrophonePermissionService microphonePermission,
            IRealtimeConversationTransport transport,
            IConversationDelayProvider delayProvider,
            TimeSpan? microphoneResumeGrace = null,
            TimeSpan? microphoneResumeFallbackGrace = null,
            bool pushToTalk = false)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _microphonePermission = microphonePermission ?? throw new ArgumentNullException(nameof(microphonePermission));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _delayProvider = delayProvider ?? throw new ArgumentNullException(nameof(delayProvider));
            _microphoneResumeGrace = microphoneResumeGrace ?? DefaultMicrophoneResumeGrace;
            _microphoneResumeFallbackGrace = microphoneResumeFallbackGrace ?? DefaultMicrophoneResumeFallbackGrace;
            _pushToTalk = pushToTalk;
            if (_microphoneResumeGrace < TimeSpan.Zero || _microphoneResumeFallbackGrace < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(microphoneResumeGrace));
            }
        }

        public void SetPushToTalkActive(bool active)
        {
            if (!_pushToTalk)
            {
                return;
            }

            TrySetTransportAudioCapture(active);

            // Pressing the button to talk means "my turn now" — cut the assistant off if it is
            // still speaking, the same way a detected barge-in would.
            if (active && CurrentState == ConversationState.Speaking)
            {
                long generation;
                lock (_sync)
                {
                    generation = _stateMachine.CurrentGeneration;
                }

                InterruptAssistant(generation);
            }
        }

        // Stop the current assistant turn: silence playback, tell the server to cancel, return to
        // Listening. Shared by a detected barge-in and the push-to-talk button.
        private void InterruptAssistant(long generation)
        {
            try
            {
                _transport.StopRemotePlayback();
            }
            catch (Exception)
            {
                PublishFailure(CreateOperationFailure(
                    "realtime_playback_stop_failed",
                    "音声出力を停止できませんでした。"));
            }

            try
            {
                ArmResponseCancelErrorSuppression();
                ObserveCancellationFailure(_transport.CancelResponseAsync(CancellationToken.None));
            }
            catch (Exception)
            {
                PublishFailure(CreateOperationFailure(
                    "realtime_response_cancel_failed",
                    "応答の停止を確認できませんでした。"));
            }
            finally
            {
                TryPublishState(ConversationState.Listening, generation);
            }
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
            await _operationGate.WaitAsync(cancellationToken);
            Interlocked.Increment(ref _lifecycleOperationCount);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancelActiveOperation();
                if (!await StopActiveSessionAsync())
                {
                    return;
                }

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

                MicrophonePermissionStatus permission;
                try
                {
                    permission = await _microphonePermission.RequestAsync(operationToken);
                    UnityEngine.Debug.Log($"[TalkDiag] StartAsync: microphone permission = {permission}.");
                }
                catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                {
                    UnityEngine.Debug.LogWarning("[TalkDiag] StartAsync: microphone permission request cancelled.");
                    FinishSession(generation);
                    throw;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[TalkDiag] StartAsync: microphone permission request threw: {ex}");
                    PublishFailure(CreateOperationFailure(
                        "microphone_permission_failed",
                        "マイクの利用を確認できませんでした。もう一度お試しください。"));
                    FinishSession(generation);
                    return;
                }

                if (permission != MicrophonePermissionStatus.Granted)
                {
                    UnityEngine.Debug.LogWarning($"[TalkDiag] StartAsync: permission not granted ({permission}); publishing failure and stopping.");
                    PublishFailure(CreatePermissionFailure(permission));
                    FinishSession(generation);
                    return;
                }

                UnityEngine.Debug.Log("[TalkDiag] StartAsync: permission granted, connecting transport.");
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

                        if (_pushToTalk)
                        {
                            // The talk button will open the microphone; it starts closed.
                            TrySetTransportAudioCapture(false);
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
                        try
                        {
                            await _delayProvider.DelayAsync(ReconnectDelays[attempt], operationToken);
                        }
                        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                        {
                            FinishSession(generation);
                            throw;
                        }
                        catch (Exception)
                        {
                            PublishFailure(CreateOperationFailure(
                                "realtime_retry_delay_failed",
                                "再接続を続けられませんでした。もう一度お試しください。"));
                            FinishSession(generation);
                            return;
                        }

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
                Interlocked.Decrement(ref _lifecycleOperationCount);
                _operationGate.Release();
                DispatchPublishedEvents();
            }
        }

        public async Task StopAsync(ConversationStopReason reason, CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken);
            Interlocked.Increment(ref _lifecycleOperationCount);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancelActiveOperation();
                await StopActiveSessionAsync();
            }
            finally
            {
                Interlocked.Decrement(ref _lifecycleOperationCount);
                _operationGate.Release();
                DispatchPublishedEvents();
            }
        }

        private async Task<bool> StopActiveSessionAsync()
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
                return true;
            }

            TryPublishState(ConversationState.Stopping, generation);
            try
            {
                await _transport.DisconnectAsync(CancellationToken.None);
                return true;
            }
            catch (Exception)
            {
                PublishFailure(CreateOperationFailure(
                    "realtime_disconnect_failed",
                    "音声会話を安全に終了できませんでした。もう一度お試しください。"));
                return false;
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
                        InterruptAssistant(generation);
                        SafeSetLocalAudioCaptureEnabled(true);
                    }
                    break;
                case RealtimeEventKind.UserTranscriptCompleted:
                    PublishEvent(() => UserTranscriptReceived?.Invoke(realtimeEvent.Transcript));
                    break;
                case RealtimeEventKind.AssistantTranscriptDelta:
                case RealtimeEventKind.AssistantTranscriptDone:
                    PublishEvent(() => AssistantTranscriptReceived?.Invoke(realtimeEvent.Transcript));
                    break;
                case RealtimeEventKind.ResponseStarted:
                    TryPublishState(ConversationState.Speaking, generation);
                    SafeSetLocalAudioCaptureEnabled(false);
                    SafeStartRemotePlayback();
                    break;
                case RealtimeEventKind.OutputAudioStarted:
                    SafeSetLocalAudioCaptureEnabled(false);
                    SafeStartRemotePlayback();
                    break;
                case RealtimeEventKind.OutputAudioStopped:
                    // The reply has finished leaving the speaker: reopen the mic after only the
                    // short acoustic-tail grace.
                    ScheduleMicrophoneResume(generation, _microphoneResumeGrace);
                    break;
                case RealtimeEventKind.ResponseDone:
                    TryPublishState(ConversationState.Listening, generation);
                    // Safety net in case output_audio_buffer.stopped never arrives.
                    ScheduleMicrophoneResume(generation, _microphoneResumeFallbackGrace);
                    break;
                case RealtimeEventKind.ApiError:
                    if (ConsumeSuppressedResponseCancelError())
                    {
                        break;
                    }

                    PublishFailure(new ConversationFailure(
                        "realtime_api_error",
                        false,
                        "音声会話で問題が発生しました。もう一度お試しください。"));
                    break;
            }
        }

        private void ArmResponseCancelErrorSuppression()
        {
            long until = DateTimeOffset.UtcNow.Add(ResponseCancelErrorGrace).UtcTicks;
            Interlocked.Exchange(ref _responseCancelErrorSuppressedUntilTicks, until);
        }

        private bool ConsumeSuppressedResponseCancelError()
        {
            long until = Interlocked.Exchange(ref _responseCancelErrorSuppressedUntilTicks, 0);
            return until != 0 && DateTimeOffset.UtcNow.UtcTicks <= until;
        }

        private void SafeSetLocalAudioCaptureEnabled(bool enabled)
        {
            // In push-to-talk the button is the sole authority over the microphone; the automatic
            // echo-guard toggles must not fight it.
            if (_pushToTalk)
            {
                return;
            }

            TrySetTransportAudioCapture(enabled);
        }

        private void TrySetTransportAudioCapture(bool enabled)
        {
            try
            {
                _transport.SetLocalAudioCaptureEnabled(enabled);
            }
            catch (Exception)
            {
                // Best-effort: a mute-toggle failure must not derail the conversation, and there is
                // nothing the user can do about it.
            }
        }

        private void SafeStartRemotePlayback()
        {
            try
            {
                _transport.StartRemotePlayback();
            }
            catch (Exception)
            {
                // Best-effort playback resume; a failure here must not derail the conversation.
            }
        }

        private async void ScheduleMicrophoneResume(long generation, TimeSpan grace)
        {
            if (_pushToTalk)
            {
                return;
            }

            if (grace <= TimeSpan.Zero)
            {
                if (IsActiveGeneration(generation) && CurrentState == ConversationState.Listening)
                {
                    SafeSetLocalAudioCaptureEnabled(true);
                }

                return;
            }

            try
            {
                await _delayProvider.DelayAsync(grace, CancellationToken.None);
            }
            catch (Exception)
            {
                // Fall through: a failed grace delay must not leave the microphone muted forever.
            }

            // Only reopen the microphone if this session is still the live one and the assistant has
            // not started another turn in the meantime (that turn owns the mute).
            if (IsActiveGeneration(generation) && CurrentState == ConversationState.Listening)
            {
                SafeSetLocalAudioCaptureEnabled(true);
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
                PublishEvent(() => StateChanged?.Invoke(state));
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
                PublishEvent(() => StateChanged?.Invoke(ConversationState.Idle));
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
                case RealtimeCallFailureKind.QuotaExhausted:
                    // Non-retryable on purpose: the OpenAI credit balance is exhausted, so the
                    // reconnect loop would only burn time.
                    return new ConversationFailure("realtime_quota_exhausted", false, "音声会話サービスをただいまご利用いただけません。");
                default:
                    return new ConversationFailure("realtime_connection_failed", false, "音声会話に接続できませんでした。");
            }
        }

        private void PublishFailure(ConversationFailure failure)
        {
            PublishEvent(() => ErrorOccurred?.Invoke(failure));
        }

        private static ConversationFailure CreateOperationFailure(string code, string userMessage)
        {
            return new ConversationFailure(code, false, userMessage);
        }

        private void ObserveCancellationFailure(Task task)
        {
            if (task == null)
            {
                PublishFailure(CreateOperationFailure(
                    "realtime_response_cancel_failed",
                    "応答の停止を確認できませんでした。"));
                return;
            }

            if (task.IsFaulted)
            {
                PublishFailure(CreateOperationFailure(
                    "realtime_response_cancel_failed",
                    "応答の停止を確認できませんでした。"));
                return;
            }

            if (task.IsCompleted)
            {
                return;
            }

            task.ContinueWith(
                completed => PublishFailure(CreateOperationFailure(
                    "realtime_response_cancel_failed",
                    "応答の停止を確認できませんでした。")),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private void PublishEvent(Action publishedEvent)
        {
            if (publishedEvent == null)
            {
                return;
            }

            lock (_publishedEventsSync)
            {
                _publishedEvents.Enqueue(publishedEvent);
            }

            DispatchPublishedEvents();
        }

        private void DispatchPublishedEvents()
        {
            if (Volatile.Read(ref _lifecycleOperationCount) != 0 ||
                Interlocked.CompareExchange(ref _isDispatchingPublishedEvents, 1, 0) != 0)
            {
                return;
            }

            try
            {
                while (Volatile.Read(ref _lifecycleOperationCount) == 0)
                {
                    Action publishedEvent;
                    lock (_publishedEventsSync)
                    {
                        if (_publishedEvents.Count == 0)
                        {
                            return;
                        }

                        publishedEvent = _publishedEvents.Dequeue();
                    }

                    publishedEvent();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isDispatchingPublishedEvents, 0);
                if (Volatile.Read(ref _lifecycleOperationCount) == 0)
                {
                    bool hasPendingEvents;
                    lock (_publishedEventsSync)
                    {
                        hasPendingEvents = _publishedEvents.Count > 0;
                    }

                    if (hasPendingEvents)
                    {
                        DispatchPublishedEvents();
                    }
                }
            }
        }
    }
}
