using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Infrastructure.Conversation.Audio;
using ObjecTalk.Infrastructure.Conversation.OpenAI;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class OpenAIRealtimeConversationServiceTests
    {
        private static readonly ConversationContext Context = new ConversationContext(
            "apple", "Apple", string.Empty, "user-1", "ja");

        [Test]
        public async Task StartAsync_RetriesTransientFailureAfterOneTwoAndFourSecondsOnly()
        {
            var permission = new FakePermissionService(MicrophonePermissionStatus.Granted);
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            var delay = new FakeDelayProvider();
            var service = CreateService(permission, transport, delay);

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(delay.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }));
            Assert.That(transport.ConnectCalls, Is.EqualTo(4));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task StartAsync_GrantedPermissionPublishesRequestConnectAndListeningStates()
        {
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), new FakeTransport(), new FakeDelayProvider());
            var states = new List<ConversationState>();
            service.StateChanged += states.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(states, Is.EqualTo(new[]
            {
                ConversationState.RequestingPermission,
                ConversationState.Connecting,
                ConversationState.Listening
            }));
        }

        [Test]
        public async Task StartAsync_PreCanceledToken_LeavesLiveSessionIntact()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                await AssertCanceledAsync(() => service.StartAsync(new ConversationContext("book", "Book", string.Empty, "user-1", "ja"), cancellation.Token));
            }

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
            Assert.That(transport.ConnectCalls, Is.EqualTo(1));
            Assert.That(transport.DisconnectCalls, Is.Zero);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Speaking));
        }

        [Test]
        public async Task StopAsync_PreCanceledToken_LeavesLiveSessionIntact()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                await AssertCanceledAsync(() => service.StopAsync(ConversationStopReason.UserRequested, cancellation.Token));
            }

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
            Assert.That(transport.DisconnectCalls, Is.Zero);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Speaking));
        }

        [Test]
        public async Task StartAsync_CancellationWhileWaitingForAdmission_DoesNotCancelActiveGeneration()
        {
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            var delay = new ControllableDelayProvider();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, delay);
            Task activeStart = service.StartAsync(Context, CancellationToken.None);
            Assert.That(delay.Started.Task.Wait(100), Is.True);

            using (var cancellation = new CancellationTokenSource())
            {
                Task waitingStart = service.StartAsync(new ConversationContext("book", "Book", string.Empty, "user-1", "ja"), cancellation.Token);
                cancellation.Cancel();

                await AssertCanceledAsync(() => waitingStart);
            }

            Assert.That(delay.ActiveToken.IsCancellationRequested, Is.False);
            delay.Release();
            await activeStart;

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
            Assert.That(transport.ConnectCalls, Is.EqualTo(2));
            Assert.That(transport.DisconnectCalls, Is.Zero);
        }

        [Test]
        public async Task StopAsync_CancellationWhileWaitingForAdmission_DoesNotCancelActiveGeneration()
        {
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            var delay = new ControllableDelayProvider();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, delay);
            Task activeStart = service.StartAsync(Context, CancellationToken.None);
            Assert.That(delay.Started.Task.Wait(100), Is.True);

            using (var cancellation = new CancellationTokenSource())
            {
                Task waitingStop = service.StopAsync(ConversationStopReason.UserRequested, cancellation.Token);
                cancellation.Cancel();

                await AssertCanceledAsync(() => waitingStop);
            }

            Assert.That(delay.ActiveToken.IsCancellationRequested, Is.False);
            delay.Release();
            await activeStart;

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
            Assert.That(transport.ConnectCalls, Is.EqualTo(2));
            Assert.That(transport.DisconnectCalls, Is.Zero);
        }

        [Test]
        public async Task StartAsync_TransientFailureThenSuccess_ReturnsToConnectingAndListening()
        {
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            var states = new List<ConversationState>();
            service.StateChanged += states.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(transport.ConnectCalls, Is.EqualTo(2));
            Assert.That(states, Is.EqualTo(new[]
            {
                ConversationState.RequestingPermission,
                ConversationState.Connecting,
                ConversationState.Reconnecting,
                ConversationState.Connecting,
                ConversationState.Listening
            }));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
        }

        [Test]
        public async Task StartAsync_AuthenticationFailurePublishesFailureWithoutRetrying()
        {
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Authentication));
            var delay = new FakeDelayProvider();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, delay);
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(transport.ConnectCalls, Is.EqualTo(1));
            Assert.That(delay.Delays, Is.Empty);
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0].IsRetryable, Is.False);
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task StartAsync_QuotaExhaustedFailurePublishesNonRetryableFailureWithoutRetrying()
        {
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.QuotaExhausted));
            var delay = new FakeDelayProvider();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, delay);
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(transport.ConnectCalls, Is.EqualTo(1));
            Assert.That(delay.Delays, Is.Empty);
            Assert.That(failures.Single().Code, Is.EqualTo("realtime_quota_exhausted"));
            Assert.That(failures.Single().IsRetryable, Is.False);
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task ApiError_ShortlyAfterABargeInCancel_IsSwallowed()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.SpeechStarted, "input_audio_buffer.speech_started", "", "", "", "", "", ""));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.ApiError, "error", "", "", "", "", "", ""));

            Assert.That(failures, Is.Empty);
        }

        [Test]
        public async Task ApiError_WithoutARecentCancel_IsStillReported()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;
            await service.StartAsync(Context, CancellationToken.None);

            transport.Emit(new RealtimeEvent(RealtimeEventKind.ApiError, "error", "", "", "", "", "", ""));

            Assert.That(failures.Single().Code, Is.EqualTo("realtime_api_error"));
        }

        [Test]
        public async Task ApiError_SuppressionIsConsumedOnce_SoALaterUnrelatedErrorStillReports()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.SpeechStarted, "input_audio_buffer.speech_started", "", "", "", "", "", ""));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.ApiError, "error", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ApiError, "error", "", "", "", "", "", ""));

            Assert.That(failures.Single().Code, Is.EqualTo("realtime_api_error"));
        }

        [Test]
        public async Task StartAsync_DeniedPermissionPublishesFailureWithoutConnectingOrRetrying()
        {
            var transport = new FakeTransport();
            var delay = new FakeDelayProvider();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Denied), transport, delay);
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(transport.ConnectCalls, Is.Zero);
            Assert.That(delay.Delays, Is.Empty);
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0].Code, Is.EqualTo("microphone_permission_denied"));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task StartAsync_PermanentlyDeniedPermissionPublishesPermanentFailure()
        {
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.DeniedPermanently), new FakeTransport(), new FakeDelayProvider());
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(failures.Single().Code, Is.EqualTo("microphone_permission_denied_permanently"));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task StartAsync_PermissionException_PublishesSafeFailureAndDisposesEventSubscription()
        {
            var permission = new FakePermissionService(MicrophonePermissionStatus.Granted) { ExceptionToThrow = new InvalidOperationException("permission diagnostic") };
            var transport = new FakeTransport();
            var service = CreateService(permission, transport, new FakeDelayProvider());
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(failures.Single().Code, Is.EqualTo("microphone_permission_failed"));
            Assert.That(failures.Single().UserMessage, Does.Not.Contain("permission diagnostic"));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
            Assert.That(transport.SubscriberCount, Is.Zero);
        }

        [Test]
        public async Task StartAsync_RetryDelayException_PublishesSafeFailureAndDisposesEventSubscription()
        {
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            var delay = new FakeDelayProvider { ExceptionToThrow = new InvalidOperationException("delay diagnostic") };
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, delay);
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(failures.Single().Code, Is.EqualTo("realtime_retry_delay_failed"));
            Assert.That(failures.Single().UserMessage, Does.Not.Contain("delay diagnostic"));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
            Assert.That(transport.SubscriberCount, Is.Zero);
        }

        [Test]
        public async Task StartAsync_CallerCancellationDuringReconnect_AbortsRetriesAndReturnsIdle()
        {
            var transport = new FakeTransport();
            transport.Failures.Enqueue(new RealtimeCallException(RealtimeCallFailureKind.Network));
            var delay = new BlockingDelayProvider();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, delay);
            using (var cancellation = new CancellationTokenSource())
            {
                Task start = service.StartAsync(Context, cancellation.Token);
                Assert.That(delay.Started.Task.Wait(100), Is.True);

                cancellation.Cancel();

                try
                {
                    await start;
                    Assert.Fail("The canceled start operation must not complete successfully.");
                }
                catch (OperationCanceledException)
                {
                }
            }

            Assert.That(transport.ConnectCalls, Is.EqualTo(1));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task SpeechStartedWhileSpeaking_CancelsResponseAndStopsRemotePlayback()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.SpeechStarted, "input_audio_buffer.speech_started", "", "", "", "", "", ""));

            Assert.That(transport.CancelResponseCalls, Is.EqualTo(1));
            Assert.That(transport.StopRemotePlaybackCalls, Is.EqualTo(1));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
        }

        [Test]
        public async Task ResponseStarted_DisablesLocalAudioCaptureToPreventEcho()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);

            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Speaking));
            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false }));
        }

        [Test]
        public async Task OutputAudioStopped_ReEnablesLocalAudioCaptureOnceTheReplyHasFinishedPlaying()
        {
            var transport = new FakeTransport();
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new TaskDelayProvider(),
                microphoneResumeGrace: TimeSpan.Zero,
                microphoneResumeFallbackGrace: TimeSpan.FromSeconds(10));
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseDone, "response.done", "", "", "", "", "", ""));

            // response.done alone leaves the mic muted - the reply is still leaving the speaker.
            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false }));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.OutputAudioStopped, "output_audio_buffer.stopped", "", "", "", "", "", ""));

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public async Task OutputAudioStarted_MutesLocalAudioCapture()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);

            transport.Emit(new RealtimeEvent(RealtimeEventKind.OutputAudioStarted, "output_audio_buffer.started", "", "", "", "", "", ""));

            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false }));
        }

        [Test]
        public async Task SpeechStartedWhileSpeaking_ReEnablesLocalAudioCapture()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.SpeechStarted, "input_audio_buffer.speech_started", "", "", "", "", "", ""));

            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public async Task OutputAudioStopped_KeepsMicrophoneMutedUntilTheAcousticTailGraceElapses()
        {
            var transport = new FakeTransport();
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new TaskDelayProvider(),
                microphoneResumeGrace: TimeSpan.FromMilliseconds(250),
                microphoneResumeFallbackGrace: TimeSpan.FromSeconds(10));
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseDone, "response.done", "", "", "", "", "", ""));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.OutputAudioStopped, "output_audio_buffer.stopped", "", "", "", "", "", ""));

            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false }), "microphone must stay muted until the tail grace elapses");
            await Task.Delay(700);
            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public async Task ResponseDoneWithoutOutputAudioStopped_ResumesMicrophoneViaFallbackGrace()
        {
            var transport = new FakeTransport();
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new TaskDelayProvider(),
                microphoneResumeGrace: TimeSpan.FromMilliseconds(50),
                microphoneResumeFallbackGrace: TimeSpan.FromMilliseconds(200));
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));

            // output_audio_buffer.stopped never arrives; only response.done does.
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseDone, "response.done", "", "", "", "", "", ""));

            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false }));
            await Task.Delay(600);
            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public async Task PushToTalk_ConnectLeavesTheMicrophoneClosed()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(), pushToTalk: true);

            await service.StartAsync(Context, CancellationToken.None);

            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false }));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
        }

        [Test]
        public async Task PushToTalk_ButtonHeldOpensTheMicrophoneAndReleaseClosesIt()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(), pushToTalk: true);
            await service.StartAsync(Context, CancellationToken.None);

            service.SetPushToTalkActive(true);
            service.SetPushToTalkActive(false);

            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false, true, false }));
        }

        [Test]
        public async Task PushToTalk_SuppressesTheAutomaticEchoGuardToggles()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(), pushToTalk: true);
            await service.StartAsync(Context, CancellationToken.None);

            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.OutputAudioStopped, "output_audio_buffer.stopped", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseDone, "response.done", "", "", "", "", "", ""));

            Assert.That(transport.LocalAudioCaptureToggles, Is.EqualTo(new[] { false }), "only the connect-time close, no automatic mute/unmute");
        }

        [Test]
        public async Task PushToTalk_PressingTheButtonWhileTheAssistantSpeaksCutsItOff()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(), pushToTalk: true);
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Speaking));

            service.SetPushToTalkActive(true);

            Assert.That(transport.StopRemotePlaybackCalls, Is.EqualTo(1));
            Assert.That(transport.CancelResponseCalls, Is.EqualTo(1));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
            Assert.That(transport.LocalAudioCaptureToggles[transport.LocalAudioCaptureToggles.Count - 1], Is.True);
        }

        [Test]
        public async Task SetPushToTalkActive_IsIgnoredWhenNotInPushToTalkMode()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);
            transport.LocalAudioCaptureToggles.Clear();

            service.SetPushToTalkActive(true);

            Assert.That(transport.LocalAudioCaptureToggles, Is.Empty);
        }

        [Test]
        public async Task ResponseStartedAfterBargeIn_ResumesRemotePlaybackForTheNewTurn()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));
            transport.Emit(new RealtimeEvent(RealtimeEventKind.SpeechStarted, "input_audio_buffer.speech_started", "", "", "", "", "", ""));
            Assert.That(transport.StopRemotePlaybackCalls, Is.EqualTo(1));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));

            Assert.That(transport.StartRemotePlaybackCalls, Is.EqualTo(2));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Speaking));
        }

        [Test]
        public async Task SpeechStartedWhenCancelThrows_StopsPlaybackReturnsToListeningAndPublishesSafeFailure()
        {
            var transport = new FakeTransport { CancelException = new InvalidOperationException("cancel diagnostic") };
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;
            await service.StartAsync(Context, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));

            transport.Emit(new RealtimeEvent(RealtimeEventKind.SpeechStarted, "input_audio_buffer.speech_started", "", "", "", "", "", ""));

            Assert.That(transport.OperationLog, Is.EqualTo(new[] { "connect:apple", "stop-playback", "cancel-response" }));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
            Assert.That(failures.Single().Code, Is.EqualTo("realtime_response_cancel_failed"));
            Assert.That(failures.Single().UserMessage, Does.Not.Contain("cancel diagnostic"));
        }

        [Test]
        public async Task StaleTransportEventsFromReplacedGeneration_AreIgnored()
        {
            var transport = new FakeTransport { RetainUnsubscribedHandlers = true };
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);
            Action<RealtimeEvent> staleHandler = transport.CapturedHandlers[0];

            await service.StartAsync(new ConversationContext("book", "Book", string.Empty, "user-1", "ja"), CancellationToken.None);
            staleHandler(new RealtimeEvent(RealtimeEventKind.ResponseStarted, "response.created", "", "", "", "", "", ""));

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Listening));
        }

        [Test]
        public async Task StartAsync_ForReplacementStopsOldTransportBeforeConnectingNewObject()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());

            await service.StartAsync(Context, CancellationToken.None);
            await service.StartAsync(new ConversationContext("book", "Book", string.Empty, "user-1", "ja"), CancellationToken.None);

            Assert.That(transport.OperationLog, Is.EqualTo(new[] { "connect:apple", "disconnect", "connect:book" }));
        }

        [Test]
        public async Task StopAsync_WhenCalledTwice_DisconnectsOnlyOnceAndRemainsIdle()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);

            await service.StopAsync(ConversationStopReason.UserRequested, CancellationToken.None);
            await service.StopAsync(ConversationStopReason.UserRequested, CancellationToken.None);

            Assert.That(transport.DisconnectCalls, Is.EqualTo(1));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task StopAsync_WhenCalledConcurrently_DisconnectsOnlyOnce()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);

            await Task.WhenAll(
                service.StopAsync(ConversationStopReason.ApplicationPaused, CancellationToken.None),
                service.StopAsync(ConversationStopReason.ApplicationPaused, CancellationToken.None));

            Assert.That(transport.DisconnectCalls, Is.EqualTo(1));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public async Task StopAsync_DisconnectException_PublishesSafeFailureAndStillCleansUp()
        {
            var transport = new FakeTransport { DisconnectException = new InvalidOperationException("disconnect diagnostic") };
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            var failures = new List<ConversationFailure>();
            service.ErrorOccurred += failures.Add;
            await service.StartAsync(Context, CancellationToken.None);

            await service.StopAsync(ConversationStopReason.UserRequested, CancellationToken.None);

            Assert.That(failures.Single().Code, Is.EqualTo("realtime_disconnect_failed"));
            Assert.That(failures.Single().UserMessage, Does.Not.Contain("disconnect diagnostic"));
            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
            Assert.That(transport.SubscriberCount, Is.Zero);
        }

        [Test]
        public async Task StateChangedHandler_CanSynchronouslyStopWithoutDeadlock()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            service.StateChanged += state =>
            {
                if (state == ConversationState.Listening)
                {
                    service.StopAsync(ConversationStopReason.UserRequested, CancellationToken.None).GetAwaiter().GetResult();
                }
            };

            Task start = Task.Run(async () => await service.StartAsync(Context, CancellationToken.None));
            Assert.That(await Task.WhenAny(start, Task.Delay(250)), Is.SameAs(start));
            await start;

            Assert.That(service.CurrentState, Is.EqualTo(ConversationState.Idle));
            Assert.That(transport.DisconnectCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task TranscriptEvents_AreForwardedWithoutRetainingThemAfterStop()
        {
            var transport = new FakeTransport();
            var service = CreateService(new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            var transcripts = new List<string>();
            service.UserTranscriptReceived += transcripts.Add;
            await service.StartAsync(Context, CancellationToken.None);

            transport.Emit(new RealtimeEvent(RealtimeEventKind.UserTranscriptCompleted, "conversation.item.input_audio_transcription.completed", "", "", "", "one-time text", "", ""));
            await service.StopAsync(ConversationStopReason.UserRequested, CancellationToken.None);
            transport.Emit(new RealtimeEvent(RealtimeEventKind.UserTranscriptCompleted, "conversation.item.input_audio_transcription.completed", "", "", "", "second text", "", ""));

            Assert.That(transcripts, Is.EqualTo(new[] { "one-time text" }));
        }

        [Test]
        public async Task FunctionCall_RunsTheToolAndSendsItsOutputWithActivityToggling()
        {
            var transport = new FakeTransport();
            var invoker = new FakeToolInvoker("鈴鹿は晴れ。");
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(),
                toolInvoker: invoker);
            var activity = new List<bool>();
            service.ToolActivityChanged += activity.Add;
            await service.StartAsync(Context, CancellationToken.None);

            transport.EmitFunctionCall(new FunctionCallRequest("get_weather", "call_1", "{\"location\":\"鈴鹿\"}"));
            await Task.Yield();

            Assert.That(invoker.Calls, Is.EqualTo(new[] { ("get_weather", "{\"location\":\"鈴鹿\"}") }));
            Assert.That(transport.FunctionOutputs, Is.EqualTo(new[] { ("call_1", "鈴鹿は晴れ。") }));
            Assert.That(activity, Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public async Task FunctionCall_WithNoInvoker_SendsAnUnavailableMessage()
        {
            var transport = new FakeTransport();
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider());
            await service.StartAsync(Context, CancellationToken.None);

            transport.EmitFunctionCall(new FunctionCallRequest("get_weather", "call_1", "{}"));
            await Task.Yield();

            Assert.That(transport.FunctionOutputs.Count, Is.EqualTo(1));
            Assert.That(transport.FunctionOutputs[0].Output, Does.Contain("道具"));
        }

        [Test]
        public async Task FunctionCall_WhenTheToolThrows_SendsAFallbackAndKeepsGoing()
        {
            var transport = new FakeTransport();
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(),
                toolInvoker: new FakeToolInvoker(_ => throw new Exception("boom")));
            await service.StartAsync(Context, CancellationToken.None);

            transport.EmitFunctionCall(new FunctionCallRequest("get_weather", "call_1", "{}"));
            await Task.Yield();

            Assert.That(transport.FunctionOutputs.Count, Is.EqualTo(1));
            Assert.That(transport.FunctionOutputs[0].Output, Does.Contain("調べられなかった"));
        }

        [Test]
        public async Task FunctionCall_BeyondThePerTurnBudget_IsRefusedWithoutCallingTheTool()
        {
            var transport = new FakeTransport();
            var invoker = new FakeToolInvoker("ok");
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(),
                toolInvoker: invoker, maxToolCallsPerTurn: 2);
            await service.StartAsync(Context, CancellationToken.None);

            for (var i = 0; i < 3; i++)
            {
                transport.EmitFunctionCall(new FunctionCallRequest("get_weather", "call_" + i, "{}"));
                await Task.Yield();
            }

            Assert.That(invoker.Calls.Count, Is.EqualTo(2));
            Assert.That(transport.FunctionOutputs.Count, Is.EqualTo(3));
            Assert.That(transport.FunctionOutputs[2].Output, Does.Contain("これ以上は"));

            // A new user turn resets the budget.
            transport.Emit(new RealtimeEvent(RealtimeEventKind.SpeechStarted, "input_audio_buffer.speech_started", "", "", "", "", "", ""));
            transport.EmitFunctionCall(new FunctionCallRequest("get_weather", "call_x", "{}"));
            await Task.Yield();
            Assert.That(invoker.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task FunctionCall_AfterStop_IsIgnored()
        {
            var transport = new FakeTransport();
            var invoker = new FakeToolInvoker("ok");
            var service = CreateService(
                new FakePermissionService(MicrophonePermissionStatus.Granted), transport, new FakeDelayProvider(),
                toolInvoker: invoker);
            await service.StartAsync(Context, CancellationToken.None);
            await service.StopAsync(ConversationStopReason.UserRequested, CancellationToken.None);

            transport.EmitFunctionCall(new FunctionCallRequest("get_weather", "call_1", "{}"));
            await Task.Yield();

            Assert.That(invoker.Calls, Is.Empty);
        }

        private sealed class FakeToolInvoker : IToolInvoker
        {
            private readonly Func<string, string> _respond;
            public List<(string Name, string Args)> Calls { get; } = new List<(string, string)>();

            public FakeToolInvoker(string result) : this(_ => result) { }
            public FakeToolInvoker(Func<string, string> respond) => _respond = respond;

            public Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)
            {
                Calls.Add((name, argumentsJson));
                return Task.FromResult(_respond(argumentsJson));
            }
        }

        private static OpenAIRealtimeConversationService CreateService(
            IMicrophonePermissionService permission,
            IRealtimeConversationTransport transport,
            IConversationDelayProvider delay,
            TimeSpan? microphoneResumeGrace = null,
            TimeSpan? microphoneResumeFallbackGrace = null,
            bool pushToTalk = false,
            IToolInvoker toolInvoker = null,
            int maxToolCallsPerTurn = 4)
        {
            // Tests default both microphone-resume graces to zero so the resume is synchronous and
            // deterministic; the timing itself is covered by dedicated tests.
            return new OpenAIRealtimeConversationService(
                new ConversationStateMachine(), permission, transport, delay,
                microphoneResumeGrace ?? TimeSpan.Zero,
                microphoneResumeFallbackGrace ?? TimeSpan.Zero,
                pushToTalk,
                toolInvoker,
                maxToolCallsPerTurn);
        }

        private static async Task AssertCanceledAsync(Func<Task> operation)
        {
            try
            {
                await operation();
                Assert.Fail("The pre-canceled operation must not complete successfully.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private sealed class FakePermissionService : IMicrophonePermissionService
        {
            private readonly MicrophonePermissionStatus _status;

            public FakePermissionService(MicrophonePermissionStatus status)
            {
                _status = status;
            }

            public Exception ExceptionToThrow { get; set; }

            public Task<MicrophonePermissionStatus> RequestAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }

                return Task.FromResult(_status);
            }
        }

        private sealed class FakeDelayProvider : IConversationDelayProvider
        {
            public List<TimeSpan> Delays { get; } = new List<TimeSpan>();
            public Exception ExceptionToThrow { get; set; }

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Delays.Add(delay);
                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }

                return Task.CompletedTask;
            }
        }

        private sealed class BlockingDelayProvider : IConversationDelayProvider
        {
            public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>();

            public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        private sealed class ControllableDelayProvider : IConversationDelayProvider
        {
            private readonly TaskCompletionSource<bool> _released = new TaskCompletionSource<bool>();

            public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>();

            public CancellationToken ActiveToken { get; private set; }

            public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                ActiveToken = cancellationToken;
                Started.TrySetResult(true);
                await _released.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }

            public void Release()
            {
                _released.TrySetResult(true);
            }
        }

        private sealed class FakeTransport : IRealtimeConversationTransport
        {
            private event Action<RealtimeEvent> _eventReceived;

            public event Action<FunctionCallRequest> FunctionCallReceived;
            public List<(string CallId, string Output)> FunctionOutputs { get; } = new List<(string, string)>();
            public Exception SendFunctionOutputException { get; set; }
            public bool Connected { get; set; } = true;

            public void EmitFunctionCall(FunctionCallRequest request) => FunctionCallReceived?.Invoke(request);

            public Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OperationLog.Add("function-output:" + callId);
                if (SendFunctionOutputException != null)
                {
                    throw SendFunctionOutputException;
                }

                if (!Connected)
                {
                    throw new InvalidOperationException("not connected");
                }

                FunctionOutputs.Add((callId, output));
                return Task.CompletedTask;
            }

            public Queue<Exception> Failures { get; } = new Queue<Exception>();
            public List<Action<RealtimeEvent>> CapturedHandlers { get; } = new List<Action<RealtimeEvent>>();
            public List<string> OperationLog { get; } = new List<string>();
            public bool RetainUnsubscribedHandlers { get; set; }
            public int ConnectCalls { get; private set; }
            public int DisconnectCalls { get; private set; }
            public int CancelResponseCalls { get; private set; }
            public int StopRemotePlaybackCalls { get; private set; }
            public int StartRemotePlaybackCalls { get; private set; }
            public List<bool> LocalAudioCaptureToggles { get; } = new List<bool>();
            public Exception CancelException { get; set; }
            public Exception DisconnectException { get; set; }
            public int SubscriberCount => _eventReceived == null ? 0 : _eventReceived.GetInvocationList().Length;

            public event Action<RealtimeEvent> EventReceived
            {
                add
                {
                    _eventReceived += value;
                    CapturedHandlers.Add(value);
                }
                remove
                {
                    if (!RetainUnsubscribedHandlers)
                    {
                        _eventReceived -= value;
                    }
                }
            }

            public Task ConnectAsync(ConversationContext context, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConnectCalls++;
                OperationLog.Add("connect:" + context.ObjectId);
                if (Failures.Count > 0)
                {
                    throw Failures.Dequeue();
                }

                return Task.CompletedTask;
            }

            public Task CancelResponseAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancelResponseCalls++;
                OperationLog.Add("cancel-response");
                if (CancelException != null)
                {
                    throw CancelException;
                }

                return Task.CompletedTask;
            }

            public void StopRemotePlayback()
            {
                StopRemotePlaybackCalls++;
                OperationLog.Add("stop-playback");
            }

            public void StartRemotePlayback()
            {
                StartRemotePlaybackCalls++;
            }

            public void SetLocalAudioCaptureEnabled(bool enabled)
            {
                LocalAudioCaptureToggles.Add(enabled);
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DisconnectCalls++;
                OperationLog.Add("disconnect");
                if (DisconnectException != null)
                {
                    throw DisconnectException;
                }

                return Task.CompletedTask;
            }

            public void Emit(RealtimeEvent realtimeEvent)
            {
                _eventReceived?.Invoke(realtimeEvent);
            }
        }
    }
}
