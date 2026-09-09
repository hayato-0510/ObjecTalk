using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ObjecTalk.Core.Conversation;

namespace ObjecTalk.Infrastructure.Conversation.OpenAI
{
    /// <summary>
    /// Runs a model tool call through <c>POST &lt;backend&gt;/api/tools/invoke</c>, authenticated with
    /// the Firebase ID token, the same way <see cref="RealtimeCallClient"/> reaches the backend. Any
    /// failure — bad config, no token, non-2xx, malformed body, timeout — returns a short Japanese
    /// message rather than throwing.
    /// </summary>
    public sealed class BackendToolInvoker : IToolInvoker
    {
        public const string FallbackMessage = "うまく調べられなかったよ。";

        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(12);

        private readonly string _backendBaseUrl;
        private readonly IIdTokenProvider _tokenProvider;
        private readonly IRealtimeHttpClient _httpClient;
        private readonly TimeSpan _requestTimeout;

        public BackendToolInvoker(
            string backendBaseUrl,
            IIdTokenProvider tokenProvider,
            IRealtimeHttpClient httpClient,
            TimeSpan? requestTimeout = null)
        {
            _backendBaseUrl = backendBaseUrl;
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
            if (_requestTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            }
        }

        public async Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            try
            {
                if (!Uri.TryCreate(_backendBaseUrl, UriKind.Absolute, out Uri baseUri))
                {
                    return FallbackMessage;
                }

                var basePath = baseUri.AbsoluteUri.TrimEnd('/') + "/";
                var endpoint = new Uri(new Uri(basePath, UriKind.Absolute), "api/tools/invoke");

                using (var timeoutSource = new CancellationTokenSource(_requestTimeout))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
                {
                    var token = await _tokenProvider.GetAsync(false, linked.Token);
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        return FallbackMessage;
                    }

                    var body = new JObject
                    {
                        ["name"] = name ?? string.Empty,
                        ["arguments"] = string.IsNullOrEmpty(argumentsJson) ? "{}" : argumentsJson,
                    }.ToString(Newtonsoft.Json.Formatting.None);

                    var headers = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "Authorization", "Bearer " + token },
                        { "Content-Type", "application/json" },
                    };

                    var response = await _httpClient.SendAsync(
                        new RealtimeHttpRequest(endpoint, "POST", body, headers), linked.Token);

                    if (response == null || response.StatusCode < 200 || response.StatusCode > 299
                        || string.IsNullOrWhiteSpace(response.Body))
                    {
                        return FallbackMessage;
                    }

                    var parsed = JObject.Parse(response.Body);
                    var output = (string)parsed["output"];
                    return string.IsNullOrWhiteSpace(output) ? FallbackMessage : output;
                }
            }
            catch (Exception)
            {
                return FallbackMessage;
            }
        }
    }
}
