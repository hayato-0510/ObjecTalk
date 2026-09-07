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

        [Header("Open-campus exhibition")]
        [Tooltip("鈴鹿高専 電子情報工学科のオープンキャンパス用。バックエンドに mode=exhibition を送り、" +
                 "案内役ペルソナと学科の事実を会話に足す。")]
        [SerializeField] private bool exhibitionMode;

        [Tooltip("オン（既定）: ハンズフリー。オフ: プッシュトゥトーク（「話す」ボタンを押している間だけマイクが入る）。" +
                 "騒がしい会場やエコーが強い環境ではオフにする。")]
        [SerializeField] private bool handsFree = true;

        [Tooltip("1組あたりの会話の上限秒数。0 で無制限。展示で回転と料金を管理したいとき使う。")]
        [SerializeField] private float sessionMaxSeconds;

        public TimeSpan RecognitionLossGrace => TimeSpan.FromSeconds(recognitionLossGraceSeconds);
        public TimeSpan ObjectSwitchConfirmation => TimeSpan.FromSeconds(objectSwitchConfirmationSeconds);
        public string Language => string.IsNullOrWhiteSpace(language) ? "ja" : language.Trim();
        public bool DebugLogging => debugLogging;
        public bool ExhibitionMode => exhibitionMode;
        public bool HandsFree => handsFree;
        public float SessionMaxSeconds => Mathf.Max(0f, sessionMaxSeconds);

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
