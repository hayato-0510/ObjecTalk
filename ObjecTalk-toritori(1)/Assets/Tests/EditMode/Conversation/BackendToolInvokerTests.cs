using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ObjecTalk.Infrastructure.Conversation.OpenAI;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class BackendToolInvokerTests
    {
        [Test]
        public async Task InvokeAsync_PostsNameAndArgumentsWithBearerAndReturnsOutput()
        {
            var http = new StubHttpClient(new RealtimeHttpResponse(200, "application/json", "{\"output\":\"鈴鹿は晴れ。\"}"));
            var invoker = new BackendToolInvoker("https://backend.example", new StubTokenProvider("fb-token"), http);

            string result = await invoker.InvokeAsync("get_weather", "{\"location\":\"鈴鹿\"}", CancellationToken.None);

            Assert.That(result, Is.EqualTo("鈴鹿は晴れ。"));
            Assert.That(http.Request.Uri.AbsoluteUri, Is.EqualTo("https://backend.example/api/tools/invoke"));
            Assert.That(http.Request.Method, Is.EqualTo("POST"));
            Assert.That(http.Request.Headers["Authorization"], Is.EqualTo("Bearer fb-token"));
            Assert.That(http.Request.Body, Does.Contain("\"name\":\"get_weather\""));
            Assert.That(http.Request.Body, Does.Contain("鈴鹿"));
        }

        [Test]
        public async Task InvokeAsync_EmptyArguments_SendsEmptyObject()
        {
            var http = new StubHttpClient(new RealtimeHttpResponse(200, "application/json", "{\"output\":\"ok\"}"));
            var invoker = new BackendToolInvoker("https://backend.example", new StubTokenProvider("t"), http);

            await invoker.InvokeAsync("web_search", null, CancellationToken.None);

            Assert.That(http.Request.Body, Does.Contain("\"arguments\":\"{}\""));
        }

        [TestCase(500, "{\"error\":\"tool_unavailable\"}")]
        [TestCase(200, "not json")]
        [TestCase(200, "{\"output\":\"\"}")]
        [TestCase(401, "{}")]
        public async Task InvokeAsync_BadResponse_ReturnsFallbackWithoutThrowing(long status, string body)
        {
            var http = new StubHttpClient(new RealtimeHttpResponse(status, "application/json", body));
            var invoker = new BackendToolInvoker("https://backend.example", new StubTokenProvider("t"), http);

            string result = await invoker.InvokeAsync("get_weather", "{}", CancellationToken.None);

            Assert.That(result, Is.EqualTo(BackendToolInvoker.FallbackMessage));
        }

        [Test]
        public async Task InvokeAsync_TokenFailure_ReturnsFallback()
        {
            var invoker = new BackendToolInvoker(
                "https://backend.example",
                new ThrowingTokenProvider(),
                new StubHttpClient(new RealtimeHttpResponse(200, "application/json", "{\"output\":\"x\"}")));

            Assert.That(
                await invoker.InvokeAsync("get_weather", "{}", CancellationToken.None),
                Is.EqualTo(BackendToolInvoker.FallbackMessage));
        }

        [Test]
        public async Task InvokeAsync_HttpThrows_ReturnsFallback()
        {
            var invoker = new BackendToolInvoker(
                "https://backend.example",
                new StubTokenProvider("t"),
                new StubHttpClient((_, __) => throw new Exception("network down")));

            Assert.That(
                await invoker.InvokeAsync("get_weather", "{}", CancellationToken.None),
                Is.EqualTo(BackendToolInvoker.FallbackMessage));
        }

        private sealed class StubTokenProvider : IIdTokenProvider
        {
            private readonly string _token;
            public StubTokenProvider(string token) => _token = token;
            public Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken) => Task.FromResult(_token);
        }

        private sealed class ThrowingTokenProvider : IIdTokenProvider
        {
            public Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken) => throw new Exception("no token");
        }

        private sealed class StubHttpClient : IRealtimeHttpClient
        {
            private readonly Func<RealtimeHttpRequest, CancellationToken, Task<RealtimeHttpResponse>> _send;

            public StubHttpClient(RealtimeHttpResponse response)
                : this((_, __) => Task.FromResult(response)) { }

            public StubHttpClient(Func<RealtimeHttpRequest, CancellationToken, Task<RealtimeHttpResponse>> send) => _send = send;

            public RealtimeHttpRequest Request { get; private set; }

            public Task<RealtimeHttpResponse> SendAsync(RealtimeHttpRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                return _send(request, cancellationToken);
            }
        }
    }
}
