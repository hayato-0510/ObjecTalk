using System;
using UnityEngine;

namespace ObjecTalk.Presentation.Talk
{
    public sealed class TalkConfiguration : MonoBehaviour
    {
        [SerializeField] private string developmentBackendBaseUrl = "http://localhost:3000";
        [SerializeField] private string productionBackendBaseUrl = "";
        [SerializeField] private bool useDevelopmentBackend = true;
        [SerializeField] private float recognitionLossGraceSeconds = 10f;
        [SerializeField] private float objectSwitchConfirmationSeconds = 2f;
        [SerializeField] private string language = "ja";
        [SerializeField] private bool debugLogging;

        public TimeSpan RecognitionLossGrace => TimeSpan.FromSeconds(recognitionLossGraceSeconds);
        public TimeSpan ObjectSwitchConfirmation => TimeSpan.FromSeconds(objectSwitchConfirmationSeconds);
        public string Language => string.IsNullOrWhiteSpace(language) ? "ja" : language.Trim();
        public bool DebugLogging => debugLogging;

        public string ResolveBackendBaseUrl()
        {
            string value = useDevelopmentBackend ? developmentBackendBaseUrl : productionBackendBaseUrl;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
            {
                throw new InvalidOperationException("音声会話の接続先が設定されていません。");
            }

            if (useDevelopmentBackend)
            {
                bool allowedBuild = Application.isEditor || Debug.isDebugBuild;
                if (!allowedBuild || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                                      !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback)))
                {
                    throw new InvalidOperationException("開発用の接続先にはHTTPSまたはループバックHTTPを指定してください。");
                }
            }
            else if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("本番用の接続先にはHTTPSを指定してください。");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }

        private void OnValidate()
        {
            recognitionLossGraceSeconds = Mathf.Max(0.1f, recognitionLossGraceSeconds);
            objectSwitchConfirmationSeconds = Mathf.Max(0.1f, objectSwitchConfirmationSeconds);
            language = string.IsNullOrWhiteSpace(language) ? "ja" : language.Trim();
        }
    }
}
