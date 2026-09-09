using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Infrastructure.Conversation.OpenAI;
using ObjecTalk.Presentation.Talk;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace ObjecTalk.Tests.PlayMode.Conversation
{
    public sealed class TalkLifecycleTests
    {
        private static readonly ConversationContext Context = new ConversationContext("object-1", "Cup", "friendly", "user-1", "ja");

        [UnityTest]
        public IEnumerator ConnectAsync_CreatesOfferAndExchangesSdpInWebRtcOrder()
        {
            var adapter = new FakePeerConnectionAdapter();
            var exchange = new FakeSdpExchange { Answer = "remote-answer" };
            var transport = new RealtimeWebRtcTransport(adapter, exchange, null, null);

            yield return transport.ConnectAsync(Context, CancellationToken.None).AsCoroutine();

            CollectionAssert.AreEqual(
                new[] { "create-peer", "create-local-track", "add-track", "create-channel:oai-events", "create-offer", "set-local:local-offer", "exchange:local-offer", "set-remote:remote-answer" },
                adapter.Calls);
            Assert.That(transport.IsConnected, Is.True);
        }

        [UnityTest]
        public IEnumerator CancelResponseAsync_SendsOfficialCancellationEventJson()
        {
            var adapter = new FakePeerConnectionAdapter();
            var transport = new RealtimeWebRtcTransport(adapter, new FakeSdpExchange(), null, null);
            yield return transport.ConnectAsync(Context, CancellationToken.None).AsCoroutine();

            yield return transport.CancelResponseAsync(CancellationToken.None).AsCoroutine();

            Assert.That(adapter.Channel.SentMessages, Is.EqualTo(new[] { "{\"type\":\"response.cancel\"}" }));
        }

        [UnityTest]
        public IEnumerator DisconnectAsync_DisposesTracksPeerAndDataChannel()
        {
            var adapter = new FakePeerConnectionAdapter { RaiseRemoteTrackOnSetRemote = true };
            var transport = new RealtimeWebRtcTransport(adapter, new FakeSdpExchange(), null, null);
            yield return transport.ConnectAsync(Context, CancellationToken.None).AsCoroutine();

            yield return transport.DisconnectAsync(CancellationToken.None).AsCoroutine();

            Assert.That(adapter.LocalTrack.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.RemoteTrack.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Channel.CloseCalls, Is.EqualTo(1));
            Assert.That(adapter.Channel.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Peer.CloseCalls, Is.EqualTo(1));
            Assert.That(adapter.Peer.DisposeCalls, Is.EqualTo(1));
            Assert.That(transport.IsConnected, Is.False);
        }

        [UnityTest]
        public IEnumerator DisconnectAsync_IsIdempotent()
        {
            var adapter = new FakePeerConnectionAdapter();
            var transport = new RealtimeWebRtcTransport(adapter, new FakeSdpExchange(), null, null);
            yield return transport.ConnectAsync(Context, CancellationToken.None).AsCoroutine();

            yield return transport.DisconnectAsync(CancellationToken.None).AsCoroutine();
            yield return transport.DisconnectAsync(CancellationToken.None).AsCoroutine();

            Assert.That(adapter.Peer.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Channel.DisposeCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DisconnectAsync_ContinuesCleanupWhenIndividualResourcesThrow()
        {
            var adapter = new FakePeerConnectionAdapter
            {
                RaiseRemoteTrackOnSetRemote = true,
                ThrowOnChannelClose = true,
                ThrowOnRemoteDetach = true,
                ThrowOnPeerClose = true
            };
            var transport = new RealtimeWebRtcTransport(adapter, new FakeSdpExchange(), null, null);
            yield return transport.ConnectAsync(Context, CancellationToken.None).AsCoroutine();

            yield return transport.DisconnectAsync(CancellationToken.None).AsCoroutine();

            Assert.That(adapter.Channel.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.RemoteTrack.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.LocalTrack.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Peer.DisposeCalls, Is.EqualTo(1));
            Assert.That(transport.IsConnected, Is.False);
        }

        [UnityTest]
        public IEnumerator ConnectAsync_CancellationDuringSdpExchange_DisposesPartialConnection()
        {
            var adapter = new FakePeerConnectionAdapter();
            var exchange = new FakeSdpExchange { WaitForCancellation = true };
            var transport = new RealtimeWebRtcTransport(adapter, exchange, null, null);
            using (var cancellation = new CancellationTokenSource())
            {
                Task connect = transport.ConnectAsync(Context, cancellation.Token);
                yield return null;
                cancellation.Cancel();
                yield return connect.AsCoroutine();

                Assert.That(connect.IsCanceled, Is.True);
            }

            Assert.That(adapter.LocalTrack.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Channel.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Peer.DisposeCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ConnectAsync_PartialFailure_DisposesAllocatedResources()
        {
            var adapter = new FakePeerConnectionAdapter { ThrowOnSetRemote = true };
            var transport = new RealtimeWebRtcTransport(adapter, new FakeSdpExchange(), null, null);

            Task connect = transport.ConnectAsync(Context, CancellationToken.None);
            yield return connect.AsCoroutine();

            Assert.That(connect.IsFaulted, Is.True);
            Assert.That(adapter.LocalTrack.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Channel.DisposeCalls, Is.EqualTo(1));
            Assert.That(adapter.Peer.DisposeCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DataChannelMessages_AreForwardedOnlyForCurrentConnectedGeneration()
        {
            var firstAdapter = new FakePeerConnectionAdapter();
            var transport = new RealtimeWebRtcTransport(firstAdapter, new FakeSdpExchange(), null, null);
            var received = new List<RealtimeEvent>();
            transport.EventReceived += received.Add;
            yield return transport.ConnectAsync(Context, CancellationToken.None).AsCoroutine();
            firstAdapter.Channel.RaiseMessage("{\"type\":\"response.created\",\"response\":{\"id\":\"response-one\"}}");

            yield return transport.DisconnectAsync(CancellationToken.None).AsCoroutine();
            firstAdapter.Channel.RaiseMessage("{\"type\":\"response.done\"}");

            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(received[0].ResponseId, Is.EqualTo("response-one"));
        }

        [UnityTest]
        public IEnumerator TalkController_RecognizedObjectStartsConversationAndUpdatesJapaneseState()
        {
            var root = new GameObject("TalkConversationRoot");
            root.SetActive(false);
            var source = root.AddComponent<FakeRecognitionSource>();
            var status = root.AddComponent<TextMeshProUGUI>();
            var view = root.AddComponent<TalkView>();
            view.Configure(status, null);
            var voice = new FakeVoiceConversationService();
            var coordinator = CreateCoordinator(voice);
            var controller = root.AddComponent<TalkController>();
            controller.InitializeForTests(source, coordinator, voice, view, new FakeConversationObjectRepository());

            root.SetActive(true);
            source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => voice.StartedObjectIds.Count == 1);

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "object-1" }));
            Assert.That(status.text, Is.EqualTo("聞いています"));

            voice.PublishState(ConversationState.Speaking);
            Assert.That(status.text, Is.EqualTo("Cupが話しています"));

            UnityEngine.Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator TalkController_IsConversationActive_TracksVoiceServiceState()
        {
            var fixture = CreateTalkFixture();
            var controller = fixture.Root.GetComponent<TalkController>();
            fixture.Root.SetActive(true);

            Assert.That(controller.IsConversationActive, Is.False);

            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);
            Assert.That(controller.IsConversationActive, Is.True);

            fixture.Voice.PublishState(ConversationState.Idle);
            Assert.That(controller.IsConversationActive, Is.False);

            UnityEngine.Object.Destroy(fixture.Root);
        }

        [UnityTest]
        public IEnumerator TalkController_ShowsCategoryQuestionPrompts_UntilTheAssistantSpeaks()
        {
            var fixture = CreateTalkFixture();
            var view = fixture.Root.GetComponent<TalkView>();
            fixture.Root.SetActive(true);

            // object-1 -> category 本
            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);
            Assert.That(view.ShownSuggestions, Is.Not.Empty);
            Assert.That(view.ShownSuggestions, Has.Some.Contains("書いてある"));

            // switching to object-2 -> category 電子機器 swaps the prompts
            fixture.Source.Emit(Seen("object-2", "Board"));
            yield return null;
            Assert.That(view.ShownSuggestions, Has.Some.Contains("AI"));

            // the assistant starting to talk dismisses them
            fixture.Voice.PublishState(ConversationState.Speaking);
            Assert.That(view.ShownSuggestions, Is.Empty);

            UnityEngine.Object.Destroy(fixture.Root);
        }

        [UnityTest]
        public IEnumerator TalkController_ShowsNoPrompts_ForAnObjectWithoutAKnownCategory()
        {
            var fixture = CreateTalkFixture();
            var view = fixture.Root.GetComponent<TalkView>();
            fixture.Root.SetActive(true);

            fixture.Source.Emit(Seen("object-9", "Mystery"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);

            Assert.That(view.ShownSuggestions, Is.Empty);

            UnityEngine.Object.Destroy(fixture.Root);
        }

        [UnityTest]
        public IEnumerator TalkController_ApplicationPauseStopsWithApplicationPaused()
        {
            var fixture = CreateTalkFixture();
            fixture.Root.SetActive(true);
            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);

            fixture.Root.SendMessage("OnApplicationPause", true);
            yield return WaitUntil(() => fixture.Voice.StopReasons.Count == 1);

            Assert.That(fixture.Voice.StopReasons.Single(), Is.EqualTo(ConversationStopReason.ApplicationPaused));
            UnityEngine.Object.Destroy(fixture.Root);
        }

        [UnityTest]
        public IEnumerator TalkController_IgnoresPauseWhileRequestingMicrophonePermission()
        {
            var fixture = CreateTalkFixture();
            fixture.Root.SetActive(true);
            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);

            // The OS permission dialog pauses the app while the service is still asking for the grant.
            fixture.Voice.PublishState(ConversationState.RequestingPermission);
            fixture.Root.SendMessage("OnApplicationPause", true);
            yield return null;
            yield return null;
            Assert.That(fixture.Voice.StopReasons, Is.Empty);

            // A pause in any other state still ends the conversation.
            fixture.Voice.PublishState(ConversationState.Listening);
            fixture.Root.SendMessage("OnApplicationPause", true);
            yield return WaitUntil(() => fixture.Voice.StopReasons.Count == 1);
            Assert.That(fixture.Voice.StopReasons.Single(), Is.EqualTo(ConversationStopReason.ApplicationPaused));
            UnityEngine.Object.Destroy(fixture.Root);
        }

        [UnityTest]
        public IEnumerator TalkController_DisableUnsubscribesAndStopsOnlyOnce()
        {
            var fixture = CreateTalkFixture();
            fixture.Root.SetActive(true);
            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);

            fixture.Root.SetActive(false);
            yield return WaitUntil(() => fixture.Voice.StopReasons.Count == 1);
            fixture.Source.Emit(Seen("object-2", "Book"));
            yield return null;

            Assert.That(fixture.Voice.StopReasons, Is.EqualTo(new[] { ConversationStopReason.UserRequested }));
            Assert.That(fixture.Voice.StartedObjectIds, Is.EqualTo(new[] { "object-1" }));
            UnityEngine.Object.Destroy(fixture.Root);
        }

        [UnityTest]
        public IEnumerator TalkController_CanStopAgainAfterConversationRestarts()
        {
            var fixture = CreateTalkFixture();
            fixture.Root.SetActive(true);
            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 1);

            fixture.Root.SendMessage("OnApplicationPause", true);
            yield return WaitUntil(() => fixture.Voice.StopReasons.Count == 1);

            fixture.Source.Emit(Seen("object-1", "Cup"));
            yield return WaitUntil(() => fixture.Voice.StartedObjectIds.Count == 2);
            fixture.Root.SendMessage("OnApplicationPause", true);
            yield return WaitUntil(() => fixture.Voice.StopReasons.Count == 2);

            Assert.That(fixture.Voice.StopReasons, Is.EqualTo(new[]
            {
                ConversationStopReason.ApplicationPaused,
                ConversationStopReason.ApplicationPaused
            }));
            UnityEngine.Object.Destroy(fixture.Root);
        }

        private static TalkFixture CreateTalkFixture()
        {
            var root = new GameObject("TalkConversationRoot");
            root.SetActive(false);
            var source = root.AddComponent<FakeRecognitionSource>();
            var status = root.AddComponent<TextMeshProUGUI>();
            var view = root.AddComponent<TalkView>();
            view.Configure(status, null);
            var voice = new FakeVoiceConversationService();
            var coordinator = CreateCoordinator(voice);
            var controller = root.AddComponent<TalkController>();
            controller.InitializeForTests(source, coordinator, voice, view, new FakeConversationObjectRepository());
            return new TalkFixture(root, source, voice);
        }

        private static ObjectConversationCoordinator CreateCoordinator(FakeVoiceConversationService voice)
        {
            return new ObjectConversationCoordinator(
                new FixedClock(),
                new FakeConversationObjectRepository(),
                voice,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2));
        }

        private static RecognizedObjectState Seen(string objectId, string objectName)
        {
            return new RecognizedObjectState(objectId, objectName, true, 0.95f, DateTimeOffset.UtcNow);
        }

        private static IEnumerator WaitUntil(Func<bool> predicate)
        {
            var deadline = Time.realtimeSinceStartup + 1f;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "The asynchronous Talk operation did not finish in time.");
        }

        private sealed class TalkFixture
        {
            public TalkFixture(GameObject root, FakeRecognitionSource source, FakeVoiceConversationService voice)
            {
                Root = root;
                Source = source;
                Voice = voice;
            }

            public GameObject Root { get; }
            public FakeRecognitionSource Source { get; }
            public FakeVoiceConversationService Voice { get; }
        }

        private sealed class FakeRecognitionSource : MonoBehaviour, IRecognitionObservationSource
        {
            public event Action<RecognizedObjectState> RecognitionChanged;
            public string CurrentUserId => "user-1";
            public void Emit(RecognizedObjectState state) => RecognitionChanged?.Invoke(state);
        }

        private sealed class FixedClock : IClock
        {
            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        }

        private sealed class FakeConversationObjectRepository : IConversationObjectRepository
        {
            public ConversationContext GetContext(string objectId)
            {
                var name = objectId == "object-1" ? "Cup" : "Book";
                var category = objectId == "object-1" ? "本"
                    : objectId == "object-2" ? "電子機器"
                    : string.Empty;
                return new ConversationContext(objectId, name, string.Empty, "user-1", "ja", category);
            }
        }

        private sealed class FakeVoiceConversationService : IVoiceConversationService
        {
            public readonly List<string> StartedObjectIds = new List<string>();
            public readonly List<ConversationStopReason> StopReasons = new List<ConversationStopReason>();
            public readonly List<bool> PushToTalkStates = new List<bool>();
            public ConversationState CurrentState { get; private set; } = ConversationState.Idle;
            public event Action<ConversationState> StateChanged;
            public event Action<string> UserTranscriptReceived { add { } remove { } }
            public event Action<string> AssistantTranscriptReceived { add { } remove { } }
            public event Action<ConversationFailure> ErrorOccurred { add { } remove { } }

            public void SetPushToTalkActive(bool active) => PushToTalkStates.Add(active);

            public Task StartAsync(ConversationContext context, CancellationToken cancellationToken)
            {
                StartedObjectIds.Add(context.ObjectId);
                CurrentState = ConversationState.Listening;
                StateChanged?.Invoke(CurrentState);
                return Task.CompletedTask;
            }

            public Task StopAsync(ConversationStopReason reason, CancellationToken cancellationToken)
            {
                StopReasons.Add(reason);
                CurrentState = ConversationState.Idle;
                StateChanged?.Invoke(CurrentState);
                return Task.CompletedTask;
            }

            public void PublishState(ConversationState state)
            {
                CurrentState = state;
                StateChanged?.Invoke(state);
            }
        }

        private sealed class FakeSdpExchange : IRealtimeSdpExchange
        {
            public string Answer = "remote-answer";
            public bool WaitForCancellation;

            public async Task<string> ExchangeSdpAsync(string localSdp, ConversationContext context, CancellationToken cancellationToken)
            {
                FakePeerConnectionAdapter.Current.Calls.Add("exchange:" + localSdp);
                if (WaitForCancellation)
                {
                    var cancelled = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancelled.TrySetCanceled()))
                    {
                        await cancelled.Task;
                    }
                }

                return Answer;
            }
        }

        private sealed class FakePeerConnectionAdapter : IRealtimePeerConnectionAdapter
        {
            public static FakePeerConnectionAdapter Current;
            public readonly List<string> Calls = new List<string>();
            public readonly FakeAudioTrack LocalTrack = new FakeAudioTrack();
            public readonly FakeAudioTrack RemoteTrack = new FakeAudioTrack();
            public readonly FakeDataChannel Channel = new FakeDataChannel();
            public readonly FakePeerConnection Peer;
            public bool RaiseRemoteTrackOnSetRemote;
            public bool ThrowOnSetRemote;
            public bool ThrowOnChannelClose;
            public bool ThrowOnRemoteDetach;
            public bool ThrowOnPeerClose;

            public FakePeerConnectionAdapter()
            {
                Current = this;
                RemoteTrack.ThrowOnDetach = () => ThrowOnRemoteDetach;
                Peer = new FakePeerConnection(this);
            }

            public IRealtimePeerConnection CreatePeerConnection()
            {
                Calls.Add("create-peer");
                return Peer;
            }

            public IRealtimeAudioTrack CreateMicrophoneTrack(UnityEngine.AudioSource microphoneSource)
            {
                Calls.Add("create-local-track");
                return LocalTrack;
            }

            public sealed class FakePeerConnection : IRealtimePeerConnection
            {
                private readonly FakePeerConnectionAdapter _owner;

                public FakePeerConnection(FakePeerConnectionAdapter owner)
                {
                    _owner = owner;
                }

                public event Action<IRealtimeAudioTrack> RemoteAudioTrackReceived;
                public int CloseCalls { get; private set; }
                public int DisposeCalls { get; private set; }

                public void AddTrack(IRealtimeAudioTrack track) => _owner.Calls.Add("add-track");

                public IRealtimeDataChannel CreateDataChannel(string label)
                {
                    _owner.Calls.Add("create-channel:" + label);
                    return _owner.Channel;
                }

                public Task<string> CreateOfferAsync(CancellationToken cancellationToken)
                {
                    _owner.Calls.Add("create-offer");
                    return Task.FromResult("local-offer");
                }

                public Task SetLocalDescriptionAsync(string sdp, CancellationToken cancellationToken)
                {
                    _owner.Calls.Add("set-local:" + sdp);
                    return Task.CompletedTask;
                }

                public Task SetRemoteDescriptionAsync(string sdp, CancellationToken cancellationToken)
                {
                    _owner.Calls.Add("set-remote:" + sdp);
                    if (_owner.ThrowOnSetRemote)
                    {
                        throw new InvalidOperationException("test failure");
                    }

                    if (_owner.RaiseRemoteTrackOnSetRemote)
                    {
                        RemoteAudioTrackReceived?.Invoke(_owner.RemoteTrack);
                    }

                    return Task.CompletedTask;
                }

                public void Close()
                {
                    CloseCalls++;
                    if (_owner.ThrowOnPeerClose) throw new InvalidOperationException("peer close failure");
                }
                public void Dispose() => DisposeCalls++;
            }

            public sealed class FakeAudioTrack : IRealtimeAudioTrack
            {
                public Func<bool> ThrowOnDetach;
                public int DisposeCalls { get; private set; }
                public readonly List<bool> EnabledToggles = new List<bool>();
                public void AttachTo(UnityEngine.AudioSource audioSource) { }
                public void Detach()
                {
                    if (ThrowOnDetach?.Invoke() == true) throw new InvalidOperationException("detach failure");
                }
                public void SetEnabled(bool enabled) => EnabledToggles.Add(enabled);
                public void Dispose() => DisposeCalls++;
            }

            public sealed class FakeDataChannel : IRealtimeDataChannel
            {
                public event Action<string> MessageReceived;
                public readonly List<string> SentMessages = new List<string>();
                public int CloseCalls { get; private set; }
                public int DisposeCalls { get; private set; }
                public void Send(string message) => SentMessages.Add(message);
                public void RaiseMessage(string message) => MessageReceived?.Invoke(message);
                public void Close()
                {
                    CloseCalls++;
                    if (FakePeerConnectionAdapter.Current.ThrowOnChannelClose) throw new InvalidOperationException("channel close failure");
                }
                public void Dispose() => DisposeCalls++;
            }
        }
    }

    internal static class TaskCoroutineExtensions
    {
        public static IEnumerator AsCoroutine(this Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }
    }
}
