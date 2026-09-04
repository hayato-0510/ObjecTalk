using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Infrastructure.Conversation.OpenAI;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class RealtimeCallClientTests
    {
        private const string LocalSdp = "v=0\r\no=- local offer\r\n";
        private static readonly ConversationContext Context = new ConversationContext(
            "object-\"1",
            "りんご\\案内",
            "丁寧に \"話す\"",
            "user-1",
            "ja");

        [Test]
        public async Task ExchangeSdpAsync_ValidBackendResponse_SendsExpectedRequestAndReturnsAnswer()
        {
            var tokenProvider = new FakeTokenProvider("firebase-token");
            var httpClient = new FakeHttpClient(new RealtimeHttpResponse(200, "application/sdp; charset=utf-8", "v=0\r\nanswer"));
            var client = new RealtimeCallClient("https://backend.example", tokenProvider, httpClient);

            string answer = await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None);

            Assert.That(answer, Is.EqualTo("v=0\r\nanswer"));
            Assert.That(tokenProvider.ForceRefreshValues, Is.EqualTo(new[] { false }));
            Assert.That(httpClient.Request.Uri.AbsoluteUri, Is.EqualTo("https://backend.example/api/realtime/calls"));
            Assert.That(httpClient.Request.Method, Is.EqualTo("POST"));
            Assert.That(httpClient.Request.Headers.Count(pair => string.Equals(pair.Key, "Authorization", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
            Assert.That(httpClient.Request.Headers["Authorization"], Is.EqualTo("Bearer firebase-token"));
            Assert.That(httpClient.Request.Headers["Content-Type"], Is.EqualTo("application/json"));

            JObject body = JObject.Parse(httpClient.Request.Body);
            Assert.That((string)body["objectId"], Is.EqualTo("object-\"1"));
            Assert.That((string)body["objectName"], Is.EqualTo("りんご\\案内"));
            Assert.That((string)body["personalityPrompt"], Is.EqualTo("丁寧に \"話す\""));
            Assert.That((string)body["language"], Is.EqualTo("ja"));
            Assert.That((string)body["sdp"], Is.EqualTo(LocalSdp));
        }

        [Test]
        public void ExchangeSdpAsync_RemoteHttpBackend_RejectsBeforeRequestingToken()
        {
            var tokenProvider = new FakeTokenProvider("firebase-token");
            var httpClient = new FakeHttpClient(new RealtimeHttpResponse(200, "application/sdp", "answer"));
            var client = new RealtimeCallClient("http://backend.example", tokenProvider, httpClient);

            RealtimeCallException exception = Assert.ThrowsAsync<RealtimeCallException>(async () => await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None));

            Assert.That(exception.Kind, Is.EqualTo(RealtimeCallFailureKind.InvalidConfiguration));
            Assert.That(tokenProvider.ForceRefreshValues, Is.Empty);
            Assert.That(httpClient.Request, Is.Null);
        }

        [TestCase("http://localhost:3000")]
        [TestCase("http://127.0.0.1:3000")]
        [TestCase("http://[::1]:3000")]
        public async Task ExchangeSdpAsync_LoopbackHttpBackend_IsAllowed(string backendBaseUrl)
        {
            var client = new RealtimeCallClient(backendBaseUrl, new FakeTokenProvider("firebase-token"), new FakeHttpClient(new RealtimeHttpResponse(200, "application/sdp", "answer")));

            string answer = await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None);

            Assert.That(answer, Is.EqualTo("answer"));
        }

        [Test]
        public void ExchangeSdpAsync_NonSuccessResponse_ThrowsSanitizedFailure()
        {
            const string body = "backend diagnostic firebase-token " + LocalSdp;
            var client = new RealtimeCallClient("https://backend.example", new FakeTokenProvider("firebase-token"), new FakeHttpClient(new RealtimeHttpResponse(502, "text/plain", body)));

            RealtimeCallException exception = Assert.ThrowsAsync<RealtimeCallException>(async () => await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None));

            Assert.That(exception.Kind, Is.EqualTo(RealtimeCallFailureKind.ServiceUnavailable));
            Assert.That(exception.Message, Does.Not.Contain(body));
            Assert.That(exception.Message, Does.Not.Contain("firebase-token"));
            Assert.That(exception.Message, Does.Not.Contain(LocalSdp));
        }

        [TestCase("text/plain", "answer", RealtimeCallFailureKind.InvalidResponse)]
        [TestCase("application/sdpx", "answer", RealtimeCallFailureKind.InvalidResponse)]
        [TestCase("application/sdp", "   ", RealtimeCallFailureKind.InvalidResponse)]
        public void ExchangeSdpAsync_InvalidSuccessfulResponse_ThrowsSanitizedFailure(string contentType, string body, RealtimeCallFailureKind expected)
        {
            var client = new RealtimeCallClient("https://backend.example", new FakeTokenProvider("firebase-token"), new FakeHttpClient(new RealtimeHttpResponse(200, contentType, body)));

            RealtimeCallException exception = Assert.ThrowsAsync<RealtimeCallException>(async () => await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None));

            Assert.That(exception.Kind, Is.EqualTo(expected));
            Assert.That(exception.Message, Does.Not.Contain(body));
        }

        [Test]
        public void ExchangeSdpAsync_RedirectToDifferentOrigin_RejectsResponse()
        {
            var response = new RealtimeHttpResponse(200, "application/sdp", "answer", new Uri("https://unexpected.example/api/realtime/calls"));
            var client = new RealtimeCallClient("https://backend.example", new FakeTokenProvider("firebase-token"), new FakeHttpClient(response));

            RealtimeCallException exception = Assert.ThrowsAsync<RealtimeCallException>(async () => await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None));

            Assert.That(exception.Kind, Is.EqualTo(RealtimeCallFailureKind.InvalidResponse));
        }

        [Test]
        public void ExchangeSdpAsync_CallerCancellation_InterruptsUncooperativeHttpClient()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var started = new TaskCompletionSource<bool>();
                var neverCompletes = new TaskCompletionSource<RealtimeHttpResponse>();
                var httpClient = new FakeHttpClient((request, token) =>
                {
                    started.TrySetResult(true);
                    return neverCompletes.Task;
                });
                var client = new RealtimeCallClient("https://backend.example", new FakeTokenProvider("firebase-token"), httpClient);

                Task exchange = client.ExchangeSdpAsync(LocalSdp, Context, cancellation.Token);
                Assert.That(started.Task.Wait(100), Is.True);
                cancellation.Cancel();

                Assert.ThrowsAsync<OperationCanceledException>(async () => await exchange);
            }
        }

        [Test, Timeout(1000)]
        public void ExchangeSdpAsync_UncooperativeTokenProvider_UsesConfiguredTimeout()
        {
            var neverCompletes = new TaskCompletionSource<string>();
            var client = new RealtimeCallClient(
                "https://backend.example",
                new DelegatingTokenProvider((forceRefresh, token) => neverCompletes.Task),
                new FakeHttpClient(new RealtimeHttpResponse(200, "application/sdp", "answer")),
                TimeSpan.FromMilliseconds(25));

            RealtimeCallException exception = Assert.ThrowsAsync<RealtimeCallException>(async () => await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None));

            Assert.That(exception.Kind, Is.EqualTo(RealtimeCallFailureKind.Timeout));
        }

        [Test, Timeout(1000)]
        public void ExchangeSdpAsync_UncooperativeHttpClient_UsesConfiguredTimeout()
        {
            var neverCompletes = new TaskCompletionSource<RealtimeHttpResponse>();
            var client = new RealtimeCallClient(
                "https://backend.example",
                new FakeTokenProvider("firebase-token"),
                new FakeHttpClient((request, token) => neverCompletes.Task),
                TimeSpan.FromMilliseconds(25));

            RealtimeCallException exception = Assert.ThrowsAsync<RealtimeCallException>(async () => await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None));

            Assert.That(exception.Kind, Is.EqualTo(RealtimeCallFailureKind.Timeout));
        }

        [Test, Timeout(17000)]
        public async Task ExchangeSdpAsync_RequestExceedsFifteenSeconds_ThrowsTimeoutFailure()
        {
            var cancellationObserved = new TaskCompletionSource<bool>();
            var httpClient = new FakeHttpClient((request, token) =>
            {
                token.Register(() => cancellationObserved.TrySetResult(true));
                return Task.Delay(Timeout.Infinite, token).ContinueWith(task =>
                {
                    task.GetAwaiter().GetResult();
                    return new RealtimeHttpResponse(200, "application/sdp", "answer");
                }, TaskScheduler.Default);
            });
            var client = new RealtimeCallClient("https://backend.example", new FakeTokenProvider("firebase-token"), httpClient);

            RealtimeCallException exception = Assert.ThrowsAsync<RealtimeCallException>(async () => await client.ExchangeSdpAsync(LocalSdp, Context, CancellationToken.None));

            Assert.That(exception.Kind, Is.EqualTo(RealtimeCallFailureKind.Timeout));
            Assert.That(await Task.WhenAny(cancellationObserved.Task, Task.Delay(100)), Is.SameAs(cancellationObserved.Task));
        }

        private sealed class FakeTokenProvider : IIdTokenProvider
        {
            private readonly string _token;

            public FakeTokenProvider(string token)
            {
                _token = token;
                ForceRefreshValues = new List<bool>();
            }

            public List<bool> ForceRefreshValues { get; }

            public Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
            {
                ForceRefreshValues.Add(forceRefresh);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_token);
            }
        }

        private sealed class FakeHttpClient : IRealtimeHttpClient
        {
            private readonly Func<RealtimeHttpRequest, CancellationToken, Task<RealtimeHttpResponse>> _send;

            public FakeHttpClient(RealtimeHttpResponse response)
                : this((request, token) => Task.FromResult(response))
            {
            }

            public FakeHttpClient(Func<RealtimeHttpRequest, CancellationToken, Task<RealtimeHttpResponse>> send)
            {
                _send = send;
            }

            public RealtimeHttpRequest Request { get; private set; }

            public Task<RealtimeHttpResponse> SendAsync(RealtimeHttpRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                return _send(request, cancellationToken);
            }
        }

        private sealed class DelegatingTokenProvider : IIdTokenProvider
        {
            private readonly Func<bool, CancellationToken, Task<string>> _getToken;

            public DelegatingTokenProvider(Func<bool, CancellationToken, Task<string>> getToken)
            {
                _getToken = getToken;
            }

            public Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
            {
                return _getToken(forceRefresh, cancellationToken);
            }
        }
    }
}
