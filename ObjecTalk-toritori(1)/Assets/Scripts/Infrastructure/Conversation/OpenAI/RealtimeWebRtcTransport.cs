using System;
using System.Threading;
using System.Threading.Tasks;
using ObjecTalk.Domain.Conversation;
using UnityEngine;
using Unity.WebRTC;

namespace ObjecTalk.Infrastructure.Conversation.OpenAI
{
    public interface IRealtimeSdpExchange
    {
        Task<string> ExchangeSdpAsync(string localSdp, ConversationContext context, CancellationToken cancellationToken);
    }

    public interface IRealtimePeerConnectionAdapter
    {
        IRealtimePeerConnection CreatePeerConnection();

        IRealtimeAudioTrack CreateMicrophoneTrack(AudioSource microphoneSource);
    }

    public interface IRealtimePeerConnection : IDisposable
    {
        event Action<IRealtimeAudioTrack> RemoteAudioTrackReceived;

        void AddTrack(IRealtimeAudioTrack track);

        IRealtimeDataChannel CreateDataChannel(string label);

        Task<string> CreateOfferAsync(CancellationToken cancellationToken);

        Task SetLocalDescriptionAsync(string sdp, CancellationToken cancellationToken);

        Task SetRemoteDescriptionAsync(string sdp, CancellationToken cancellationToken);

        void Close();
    }

    public interface IRealtimeAudioTrack : IDisposable
    {
        void AttachTo(AudioSource audioSource);

        void Detach();
    }

    public interface IRealtimeDataChannel : IDisposable
    {
        event Action<string> MessageReceived;

        void Send(string message);

        void Close();
    }

    public sealed class RealtimeCallSdpExchange : IRealtimeSdpExchange
    {
        private readonly RealtimeCallClient _client;

        public RealtimeCallSdpExchange(RealtimeCallClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public Task<string> ExchangeSdpAsync(string localSdp, ConversationContext context, CancellationToken cancellationToken)
        {
            return _client.ExchangeSdpAsync(localSdp, context, cancellationToken);
        }
    }

    public sealed class RealtimeWebRtcTransport : IRealtimeConversationTransport
    {
        public const string EventsChannelLabel = "oai-events";
        public const string ResponseCancelEventJson = "{\"type\":\"response.cancel\"}";

        private readonly IRealtimePeerConnectionAdapter _adapter;
        private readonly IRealtimeSdpExchange _sdpExchange;
        private readonly AudioSource _microphoneSource;
        private readonly AudioSource _remoteAudioSource;
        private readonly RealtimeEventParser _eventParser = new RealtimeEventParser();
        private readonly object _sync = new object();
        private ConnectionResources _current;
        private int _nextGeneration;

        public RealtimeWebRtcTransport(
            IRealtimePeerConnectionAdapter adapter,
            IRealtimeSdpExchange sdpExchange,
            AudioSource microphoneSource,
            AudioSource remoteAudioSource)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _sdpExchange = sdpExchange ?? throw new ArgumentNullException(nameof(sdpExchange));
            _microphoneSource = microphoneSource;
            _remoteAudioSource = remoteAudioSource;
        }

        public event Action<RealtimeEvent> EventReceived;

        public bool IsConnected
        {
            get
            {
                lock (_sync)
                {
                    return _current != null && _current.Connected && !_current.Disposed;
                }
            }
        }

        public async Task ConnectAsync(ConversationContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            await DisconnectAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();

            var resources = new ConnectionResources(Interlocked.Increment(ref _nextGeneration), cancellationToken);
            lock (_sync)
            {
                _current = resources;
            }

            try
            {
                resources.Peer = _adapter.CreatePeerConnection();
                EnsureCurrent(resources);
                resources.RemoteTrackHandler = track => OnRemoteAudioTrack(resources, track);
                resources.Peer.RemoteAudioTrackReceived += resources.RemoteTrackHandler;

                resources.LocalTrack = _adapter.CreateMicrophoneTrack(_microphoneSource);
                EnsureCurrent(resources);
                resources.Peer.AddTrack(resources.LocalTrack);

                resources.DataChannel = resources.Peer.CreateDataChannel(EventsChannelLabel);
                EnsureCurrent(resources);
                resources.DataChannelMessageHandler = message => OnDataChannelMessage(resources, message);
                resources.DataChannel.MessageReceived += resources.DataChannelMessageHandler;

                string offer = await resources.Peer.CreateOfferAsync(resources.Token);
                EnsureCurrent(resources);
                await resources.Peer.SetLocalDescriptionAsync(offer, resources.Token);
                EnsureCurrent(resources);

                string answer = await _sdpExchange.ExchangeSdpAsync(offer, context, resources.Token);
                EnsureCurrent(resources);
                await resources.Peer.SetRemoteDescriptionAsync(answer, resources.Token);
                EnsureCurrent(resources);

                resources.Connected = true;
            }
            catch
            {
                await DisposeResourcesAsync(resources);
                throw;
            }
        }

        public Task CancelResponseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectionResources resources;
            lock (_sync)
            {
                resources = _current;
            }

            if (resources != null && resources.Connected && !resources.Disposed)
            {
                resources.DataChannel?.Send(ResponseCancelEventJson);
            }

            return Task.CompletedTask;
        }

        public void StopRemotePlayback()
        {
            _remoteAudioSource?.Stop();
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectionResources resources;
            lock (_sync)
            {
                resources = _current;
                _current = null;
            }

            if (resources != null)
            {
                resources.Cancellation.Cancel();
                await DisposeResourcesAsync(resources);
            }
        }

        private void OnRemoteAudioTrack(ConnectionResources resources, IRealtimeAudioTrack track)
        {
            if (!IsCurrent(resources) || track == null)
            {
                track?.Dispose();
                return;
            }

            IRealtimeAudioTrack previousTrack = resources.RemoteTrack;
            resources.RemoteTrack = track;
            previousTrack?.Detach();
            previousTrack?.Dispose();
            if (_remoteAudioSource != null)
            {
                track.AttachTo(_remoteAudioSource);
                _remoteAudioSource.loop = true;
                _remoteAudioSource.Play();
            }
        }

        private void OnDataChannelMessage(ConnectionResources resources, string message)
        {
            if (!IsCurrent(resources) || !resources.Connected)
            {
                return;
            }

            if (_eventParser.TryParse(message, out RealtimeEvent realtimeEvent))
            {
                EventReceived?.Invoke(realtimeEvent);
            }
        }

        private void EnsureCurrent(ConnectionResources resources)
        {
            resources.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(resources))
            {
                throw new OperationCanceledException(resources.Token);
            }
        }

        private bool IsCurrent(ConnectionResources resources)
        {
            lock (_sync)
            {
                return ReferenceEquals(_current, resources) && !resources.Disposed;
            }
        }

        private Task DisposeResourcesAsync(ConnectionResources resources)
        {
            if (resources == null || Interlocked.Exchange(ref resources.DisposeStarted, 1) != 0)
            {
                return Task.CompletedTask;
            }

            try
            {
                // Every operation is isolated so a faulty native/plugin wrapper cannot prevent
                // the microphone track and remaining WebRTC resources from being released.
                TryCleanup(() => _remoteAudioSource?.Stop());

                if (resources.DataChannel != null)
                {
                    if (resources.DataChannelMessageHandler != null)
                    {
                        TryCleanup(() => resources.DataChannel.MessageReceived -= resources.DataChannelMessageHandler);
                    }
                    TryCleanup(resources.DataChannel.Close);
                    TryCleanup(resources.DataChannel.Dispose);
                }

                TryCleanup(() => resources.RemoteTrack?.Detach());
                TryCleanup(() => resources.RemoteTrack?.Dispose());
                TryCleanup(() => resources.LocalTrack?.Dispose());

                if (resources.Peer != null)
                {
                    if (resources.RemoteTrackHandler != null)
                    {
                        TryCleanup(() => resources.Peer.RemoteAudioTrackReceived -= resources.RemoteTrackHandler);
                    }
                    TryCleanup(resources.Peer.Close);
                    TryCleanup(resources.Peer.Dispose);
                }
            }
            finally
            {
                resources.Connected = false;
                resources.Disposed = true;
                resources.Cancellation.Dispose();
                lock (_sync)
                {
                    if (ReferenceEquals(_current, resources))
                    {
                        _current = null;
                    }
                }
            }

            return Task.CompletedTask;
        }

        private static void TryCleanup(Action cleanup)
        {
            try
            {
                cleanup?.Invoke();
            }
            catch
            {
                // Cleanup is best-effort per resource; continue to release all remaining owners.
            }
        }

        private sealed class ConnectionResources
        {
            public ConnectionResources(int generation, CancellationToken callerToken)
            {
                Generation = generation;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            }

            public int Generation { get; }
            public CancellationTokenSource Cancellation { get; }
            public CancellationToken Token => Cancellation.Token;
            public IRealtimePeerConnection Peer;
            public IRealtimeAudioTrack LocalTrack;
            public IRealtimeAudioTrack RemoteTrack;
            public IRealtimeDataChannel DataChannel;
            public Action<IRealtimeAudioTrack> RemoteTrackHandler;
            public Action<string> DataChannelMessageHandler;
            public bool Connected;
            public bool Disposed;
            public int DisposeStarted;
        }
    }

    public sealed class UnityWebRtcPeerConnectionAdapter : IRealtimePeerConnectionAdapter
    {
        public IRealtimePeerConnection CreatePeerConnection()
        {
            return new UnityPeerConnection(new RTCPeerConnection());
        }

        public IRealtimeAudioTrack CreateMicrophoneTrack(AudioSource microphoneSource)
        {
            if (microphoneSource == null || Microphone.devices.Length == 0)
            {
                throw new InvalidOperationException("Microphone is unavailable.");
            }

            string deviceName = Microphone.devices[0];
            microphoneSource.clip = Microphone.Start(deviceName, true, 1, 48000);
            microphoneSource.loop = true;
            microphoneSource.Play();
            return new UnityAudioTrack(new AudioStreamTrack(microphoneSource), microphoneSource, deviceName);
        }

        private sealed class UnityPeerConnection : IRealtimePeerConnection
        {
            private readonly RTCPeerConnection _peer;

            public UnityPeerConnection(RTCPeerConnection peer)
            {
                _peer = peer ?? throw new ArgumentNullException(nameof(peer));
                _peer.OnTrack = OnTrack;
            }

            public event Action<IRealtimeAudioTrack> RemoteAudioTrackReceived;

            public void AddTrack(IRealtimeAudioTrack track)
            {
                if (!(track is UnityAudioTrack unityTrack))
                {
                    throw new ArgumentException("The Unity WebRTC adapter requires a Unity audio track.", nameof(track));
                }

                _peer.AddTrack(unityTrack.Track);
            }

            public IRealtimeDataChannel CreateDataChannel(string label)
            {
                return new UnityDataChannel(_peer.CreateDataChannel(label));
            }

            public async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
            {
                RTCSessionDescriptionAsyncOperation operation = _peer.CreateOffer();
                await WaitForAsyncOperation(operation, cancellationToken);
                if (operation.IsError)
                {
                    throw new InvalidOperationException("Unable to create a WebRTC offer.");
                }

                return operation.Desc.sdp;
            }

            public async Task SetLocalDescriptionAsync(string sdp, CancellationToken cancellationToken)
            {
                var description = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
                RTCSetSessionDescriptionAsyncOperation operation = _peer.SetLocalDescription(ref description);
                await WaitForAsyncOperation(operation, cancellationToken);
                if (operation.IsError)
                {
                    throw new InvalidOperationException("Unable to set the local WebRTC description.");
                }
            }

            public async Task SetRemoteDescriptionAsync(string sdp, CancellationToken cancellationToken)
            {
                var description = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                RTCSetSessionDescriptionAsyncOperation operation = _peer.SetRemoteDescription(ref description);
                await WaitForAsyncOperation(operation, cancellationToken);
                if (operation.IsError)
                {
                    throw new InvalidOperationException("Unable to set the remote WebRTC description.");
                }
            }

            public void Close()
            {
                _peer.Close();
            }

            public void Dispose()
            {
                _peer.OnTrack = null;
                _peer.Dispose();
            }

            private void OnTrack(RTCTrackEvent trackEvent)
            {
                if (trackEvent.Track is AudioStreamTrack audioTrack)
                {
                    RemoteAudioTrackReceived?.Invoke(new UnityAudioTrack(audioTrack, null, null));
                }
            }

            private static async Task WaitForAsyncOperation(AsyncOperationBase operation, CancellationToken cancellationToken)
            {
                while (!operation.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private sealed class UnityAudioTrack : IRealtimeAudioTrack
        {
            private readonly AudioStreamTrack _track;
            private readonly AudioSource _microphoneSource;
            private readonly string _microphoneDeviceName;
            private AudioSource _remoteAudioSource;
            private bool _disposed;

            public UnityAudioTrack(AudioStreamTrack track, AudioSource microphoneSource, string microphoneDeviceName)
            {
                Track = track ?? throw new ArgumentNullException(nameof(track));
                _track = track;
                _microphoneSource = microphoneSource;
                _microphoneDeviceName = microphoneDeviceName;
            }

            public AudioStreamTrack Track { get; }

            public void AttachTo(AudioSource audioSource)
            {
                if (audioSource == null || _disposed)
                {
                    return;
                }

                _remoteAudioSource = audioSource;
                _remoteAudioSource.SetTrack(_track);
            }

            public void Detach()
            {
                if (_remoteAudioSource != null)
                {
                    // Unity WebRTC 3 3.0.0 SetTrack dereferences its argument and cannot accept
                    // null. Stopping the AudioSource and disposing the track releases its renderer.
                    _remoteAudioSource.Stop();
                    _remoteAudioSource = null;
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Detach();
                _track.Dispose();
                if (_microphoneSource != null)
                {
                    _microphoneSource.Stop();
                    if (!string.IsNullOrEmpty(_microphoneDeviceName))
                    {
                        Microphone.End(_microphoneDeviceName);
                    }
                }
            }
        }

        private sealed class UnityDataChannel : IRealtimeDataChannel
        {
            private readonly RTCDataChannel _channel;

            public UnityDataChannel(RTCDataChannel channel)
            {
                _channel = channel ?? throw new ArgumentNullException(nameof(channel));
                _channel.OnMessage = bytes => MessageReceived?.Invoke(System.Text.Encoding.UTF8.GetString(bytes));
            }

            public event Action<string> MessageReceived;

            public void Send(string message)
            {
                _channel.Send(message);
            }

            public void Close()
            {
                _channel.Close();
            }

            public void Dispose()
            {
                _channel.OnMessage = null;
                _channel.Dispose();
            }
        }
    }

}
