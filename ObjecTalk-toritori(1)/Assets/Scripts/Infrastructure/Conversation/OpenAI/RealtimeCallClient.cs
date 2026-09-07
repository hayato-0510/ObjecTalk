using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ObjecTalk.Domain.Conversation;
#if UNITY_5_3_OR_NEWER
using UnityEngine.Networking;
#endif

namespace ObjecTalk.Infrastructure.Conversation.OpenAI
{
    public interface IIdTokenProvider
    {
        Task<string> GetAsync(bool forceRefresh, CancellationToken cancellationToken);
    }

    public interface IRealtimeHttpClient
    {
        Task<RealtimeHttpResponse> SendAsync(RealtimeHttpRequest request, CancellationToken cancellationToken);
    }

    public sealed class RealtimeHttpRequest
    {
        public RealtimeHttpRequest(Uri uri, string method, string body, IReadOnlyDictionary<string, string> headers)
        {
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Body = body ?? string.Empty;
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        }

        public Uri Uri { get; }

        public string Method { get; }

        public string Body { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }
    }

    public sealed class RealtimeHttpResponse
    {
        public RealtimeHttpResponse(long statusCode, string contentType, string body, Uri finalUri = null)
        {
            StatusCode = statusCode;
            ContentType = contentType ?? string.Empty;
            Body = body ?? string.Empty;
            FinalUri = finalUri;
        }

        public long StatusCode { get; }

        public string ContentType { get; }

        public string Body { get; }

        public Uri FinalUri { get; }
    }

    public enum RealtimeCallFailureKind
    {
        InvalidConfiguration,
        Authentication,
        Network,
        Timeout,
        ServiceUnavailable,
        QuotaExhausted,
        InvalidResponse
    }

    public sealed class RealtimeCallException : Exception
    {
        public RealtimeCallException(RealtimeCallFailureKind kind)
            : base(GetUserMessage(kind))
        {
            Kind = kind;
        }

        public RealtimeCallFailureKind Kind { get; }

        public bool IsRetryable => Kind == RealtimeCallFailureKind.Network ||
                                   Kind == RealtimeCallFailureKind.Timeout ||
                                   Kind == RealtimeCallFailureKind.ServiceUnavailable;

        private static string GetUserMessage(RealtimeCallFailureKind kind)
        {
            switch (kind)
            {
                case RealtimeCallFailureKind.InvalidConfiguration:
                    return "接続先の設定が無効です。";
                case RealtimeCallFailureKind.Authentication:
                    return "認証を確認してからもう一度お試しください。";
                case RealtimeCallFailureKind.Network:
                    return "ネットワークに接続できませんでした。";
                case RealtimeCallFailureKind.Timeout:
                    return "接続がタイムアウトしました。";
                case RealtimeCallFailureKind.ServiceUnavailable:
                    return "音声会話サービスが一時的に利用できません。";
                case RealtimeCallFailureKind.QuotaExhausted:
                    return "音声会話サービスをただいまご利用いただけません。";
                default:
                    return "音声会話サービスから正しい応答を受信できませんでした。";
            }
        }
    }

    public sealed class RealtimeCallClient
    {
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
        private readonly string _backendBaseUrl;
        private readonly IIdTokenProvider _tokenProvider;
        private readonly IRealtimeHttpClient _httpClient;
        private readonly TimeSpan _requestTimeout;

        public RealtimeCallClient(string backendBaseUrl, IIdTokenProvider tokenProvider, IRealtimeHttpClient httpClient)
            : this(backendBaseUrl, tokenProvider, httpClient, DefaultRequestTimeout)
        {
        }

        public RealtimeCallClient(string backendBaseUrl, IIdTokenProvider tokenProvider, IRealtimeHttpClient httpClient, TimeSpan requestTimeout)
        {
            _backendBaseUrl = backendBaseUrl;
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (requestTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            }

            _requestTimeout = requestTimeout;
        }

        public async Task<string> ExchangeSdpAsync(string localSdp, ConversationContext context, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(localSdp) || context == null)
            {
                throw new RealtimeCallException(RealtimeCallFailureKind.InvalidConfiguration);
            }

            Uri endpoint = BuildEndpoint(_backendBaseUrl);
            cancellationToken.ThrowIfCancellationRequested();

            using (var timeoutSource = new CancellationTokenSource(_requestTimeout))
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
            {
                CancellationToken requestToken = linkedSource.Token;
                string firebaseToken;
                try
                {
                    firebaseToken = await AwaitWithCancellation(_tokenProvider.GetAsync(false, requestToken), requestToken);
                }
                catch (OperationCanceledException)
                {
                    ThrowForCancellation(cancellationToken);
                    throw new RealtimeCallException(RealtimeCallFailureKind.Timeout);
                }
                catch
                {
                    throw new RealtimeCallException(RealtimeCallFailureKind.Authentication);
                }

                if (string.IsNullOrWhiteSpace(firebaseToken) || ContainsHeaderControlCharacter(firebaseToken))
                {
                    throw new RealtimeCallException(RealtimeCallFailureKind.Authentication);
                }

                var headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "Authorization", "Bearer " + firebaseToken },
                    { "Content-Type", "application/json" }
                };
                var request = new RealtimeHttpRequest(endpoint, "POST", CreateRequestBody(localSdp, context), headers);
                RealtimeHttpResponse response;
                try
                {
                    response = await AwaitWithCancellation(_httpClient.SendAsync(request, requestToken), requestToken);
                }
                catch (OperationCanceledException)
                {
                    ThrowForCancellation(cancellationToken);
                    throw new RealtimeCallException(RealtimeCallFailureKind.Timeout);
                }
                catch
                {
                    throw new RealtimeCallException(RealtimeCallFailureKind.Network);
                }

                if (response == null || (response.FinalUri != null && !IsSameOrigin(endpoint, response.FinalUri)))
                {
                    throw new RealtimeCallException(RealtimeCallFailureKind.InvalidResponse);
                }

                if (response.StatusCode < 200 || response.StatusCode > 299)
                {
                    throw new RealtimeCallException(GetFailureKind(response.StatusCode));
                }

                if (!IsSdpContentType(response.ContentType) || string.IsNullOrWhiteSpace(response.Body))
                {
                    throw new RealtimeCallException(RealtimeCallFailureKind.InvalidResponse);
                }

                return response.Body;
            }
        }

        private static Uri BuildEndpoint(string backendBaseUrl)
        {
            if (!Uri.TryCreate(backendBaseUrl, UriKind.Absolute, out Uri baseUri) ||
                (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                 !(string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && baseUri.IsLoopback)))
            {
                throw new RealtimeCallException(RealtimeCallFailureKind.InvalidConfiguration);
            }

            var baseBuilder = new UriBuilder(baseUri) { Query = string.Empty, Fragment = string.Empty };
            string basePath = baseBuilder.Uri.AbsoluteUri;
            if (!basePath.EndsWith("/", StringComparison.Ordinal))
            {
                basePath += "/";
            }

            return new Uri(new Uri(basePath, UriKind.Absolute), "api/realtime/calls");
        }

        private static string CreateRequestBody(string localSdp, ConversationContext context)
        {
            var payload = new JObject
            {
                ["objectId"] = context.ObjectId,
                ["objectName"] = context.ObjectName,
                ["personalityPrompt"] = context.PersonalityPrompt,
                ["language"] = context.Language,
                ["sdp"] = localSdp
            };
            return payload.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool ContainsHeaderControlCharacter(string value)
        {
            return value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0;
        }

        private static void ThrowForCancellation(CancellationToken callerToken)
        {
            callerToken.ThrowIfCancellationRequested();
        }

        private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (task == null)
            {
                throw new InvalidOperationException("Realtime operation is unavailable.");
            }

            var cancellation = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancellation.Task) == task)
                {
                    return await task;
                }
            }

            ObserveAbandonedFault(task);
            throw new OperationCanceledException(cancellationToken);
        }

        private static void ObserveAbandonedFault(Task task)
        {
            task.ContinueWith(
                completed => { var ignored = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private static bool IsSameOrigin(Uri expected, Uri actual)
        {
            return string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase) &&
                   expected.Port == actual.Port;
        }

        private static bool IsSdpContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            string mediaType = contentType.Split(';')[0].Trim();
            return string.Equals(mediaType, "application/sdp", StringComparison.OrdinalIgnoreCase);
        }

        private static RealtimeCallFailureKind GetFailureKind(long statusCode)
        {
            if (statusCode == 401 || statusCode == 403)
            {
                return RealtimeCallFailureKind.Authentication;
            }

            if (statusCode == 402)
            {
                // The backend maps an exhausted OpenAI credit balance to 402; retrying cannot help.
                return RealtimeCallFailureKind.QuotaExhausted;
            }

            return statusCode == 408
                ? RealtimeCallFailureKind.Timeout
                : RealtimeCallFailureKind.ServiceUnavailable;
        }
    }

#if UNITY_5_3_OR_NEWER
    public sealed class UnityWebRequestRealtimeHttpClient : IRealtimeHttpClient
    {
        public async Task<RealtimeHttpResponse> SendAsync(RealtimeHttpRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using (var unityRequest = new UnityWebRequest(request.Uri.AbsoluteUri, request.Method))
            {
                unityRequest.redirectLimit = 0;
                unityRequest.timeout = 15;
                unityRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
                unityRequest.downloadHandler = new DownloadHandlerBuffer();
                foreach (KeyValuePair<string, string> header in request.Headers)
                {
                    unityRequest.SetRequestHeader(header.Key, header.Value);
                }

                UnityWebRequestAsyncOperation operation = unityRequest.SendWebRequest();
                try
                {
                    while (!operation.isDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                }
                catch (OperationCanceledException)
                {
                    unityRequest.Abort();
                    throw;
                }

                if (unityRequest.result == UnityWebRequest.Result.ConnectionError ||
                    unityRequest.result == UnityWebRequest.Result.DataProcessingError)
                {
                    throw new InvalidOperationException("Realtime HTTP transport failed.");
                }

                Uri.TryCreate(unityRequest.url, UriKind.Absolute, out Uri finalUri);
                return new RealtimeHttpResponse(
                    unityRequest.responseCode,
                    unityRequest.GetResponseHeader("Content-Type"),
                    unityRequest.downloadHandler.text,
                    finalUri);
            }
        }
    }
#endif
}
