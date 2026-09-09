using System;
using ObjecTalk.Core.Exhibition;
using ObjecTalk.Presentation.Talk;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ObjecTalk.Presentation.Exhibition
{
    /// <summary>
    /// 展示キオスクの無操作リセット。判定は <see cref="ExhibitionIdlePolicy"/> に委譲し、この
    /// コンポーネントは入力の検知、アトラクト画面の表示切替、シーン再ロードだけを行う。
    /// </summary>
    public sealed class ExhibitionSessionController : MonoBehaviour
    {
        [Tooltip("無操作でアトラクト画面へ移るまでの秒数。")]
        [SerializeField] private float idleToAttractSeconds = 45f;

        [Tooltip("アトラクト表示後、さらに無操作でシーンを再ロードするまでの秒数。")]
        [SerializeField] private float attractToResetSeconds = 30f;

        [Tooltip("待機中に前面へ出すアトラクト画面のルート。会話中・操作中は非表示になる。")]
        [SerializeField] private GameObject attractOverlay;

        [Tooltip("会話状態を参照する TalkController。未設定ならシーンから探す。")]
        [SerializeField] private TalkController talkController;

        private ExhibitionIdlePolicy _policy;
        private Action _onReset;
        private Func<DateTimeOffset> _now;
        private bool _resetRequested;

        private void Awake()
        {
            if (_policy == null)
            {
                _policy = new ExhibitionIdlePolicy(
                    TimeSpan.FromSeconds(Mathf.Max(1f, idleToAttractSeconds)),
                    TimeSpan.FromSeconds(Mathf.Max(1f, attractToResetSeconds)));
                _onReset = () => SceneManager.LoadScene("ObjectDetectionScene");
                _now = () => DateTimeOffset.UtcNow;
            }

            if (talkController == null)
            {
                talkController = FindAnyObjectByType<TalkController>();
            }
        }

        public void InitializeForTests(
            ExhibitionIdlePolicy policy,
            GameObject overlay,
            Action onReset,
            Func<DateTimeOffset> now)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            attractOverlay = overlay;
            _onReset = onReset ?? throw new ArgumentNullException(nameof(onReset));
            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        /// <summary>アトラクト画面のタップなど、外部からの操作通知。</summary>
        public void NotifyActivity()
        {
            _policy?.RecordActivity(_now());
        }

        private void Update()
        {
            if (_policy == null || _resetRequested)
            {
                return;
            }

            var now = _now();

            if (HasPointerActivityThisFrame())
            {
                _policy.RecordActivity(now);
            }

            if (talkController != null)
            {
                _policy.SetConversationActive(talkController.IsConversationActive, now);
            }

            switch (_policy.Evaluate(now))
            {
                case ExhibitionIdleState.Interacting:
                    SetAttractVisible(false);
                    break;

                case ExhibitionIdleState.Attract:
                    SetAttractVisible(true);
                    break;

                case ExhibitionIdleState.Reset:
                    _resetRequested = true;
                    _onReset();
                    break;
            }
        }

        private static bool HasPointerActivityThisFrame()
        {
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                return true;
            }

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
        }

        private void SetAttractVisible(bool visible)
        {
            if (attractOverlay != null && attractOverlay.activeSelf != visible)
            {
                attractOverlay.SetActive(visible);
            }
        }
    }
}
