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

        private static OpenAIRealtimeConversationService CreateService(
            IMicrophonePermissionService permission,
            IRealtimeConversationTransport transport,
            IConversationDelayProvider delay)
        {
            return new OpenAIRealtimeConversationService(new ConversationStateMachine(), permission, transport, delay);
        }

        private sealed class FakePermissionService : IMicrophonePermissionService
        {
            private readonly MicrophonePermissionStatus _status;

            public FakePermissionService(MicrophonePermissionStatus status)
            {
                _status = status;
            }

            public Task<MicrophonePermissionStatus> RequestAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_status);
            }
        }

        private sealed class FakeDelayProvider : IConversationDelayProvider
        {
            public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Delays.Add(delay);
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

        private sealed class FakeTransport : IRealtimeConversationTransport
        {
            private event Action<RealtimeEvent> _eventReceived;

            public Queue<Exception> Failures { get; } = new Queue<Exception>();
            public List<Action<RealtimeEvent>> CapturedHandlers { get; } = new List<Action<RealtimeEvent>>();
            public List<string> OperationLog { get; } = new List<string>();
            public bool RetainUnsubscribedHandlers { get; set; }
            public int ConnectCalls { get; private set; }
            public int DisconnectCalls { get; private set; }
            public int CancelResponseCalls { get; private set; }
            public int StopRemotePlaybackCalls { get; private set; }

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
                return Task.CompletedTask;
            }

            public void StopRemotePlayback()
            {
                StopRemotePlaybackCalls++;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DisconnectCalls++;
                OperationLog.Add("disconnect");
                return Task.CompletedTask;
            }

            public void Emit(RealtimeEvent realtimeEvent)
            {
                _eventReceived?.Invoke(realtimeEvent);
            }
        }
    }
}
