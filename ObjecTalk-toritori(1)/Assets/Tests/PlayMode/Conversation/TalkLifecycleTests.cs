using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Infrastructure.Conversation.OpenAI;
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

            public FakePeerConnectionAdapter()
            {
                Current = this;
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

            private sealed class FakePeerConnection : IRealtimePeerConnection
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

                public void Close() => CloseCalls++;
                public void Dispose() => DisposeCalls++;
            }

            private sealed class FakeAudioTrack : IRealtimeAudioTrack
            {
                public int DisposeCalls { get; private set; }
                public void AttachTo(UnityEngine.AudioSource audioSource) { }
                public void Detach() { }
                public void Dispose() => DisposeCalls++;
            }

            private sealed class FakeDataChannel : IRealtimeDataChannel
            {
                public event Action<string> MessageReceived;
                public readonly List<string> SentMessages = new List<string>();
                public int CloseCalls { get; private set; }
                public int DisposeCalls { get; private set; }
                public void Send(string message) => SentMessages.Add(message);
                public void RaiseMessage(string message) => MessageReceived?.Invoke(message);
                public void Close() => CloseCalls++;
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
