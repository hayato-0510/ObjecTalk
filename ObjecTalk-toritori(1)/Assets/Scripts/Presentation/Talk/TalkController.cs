using System;
using System.Threading;
using System.Threading.Tasks;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Infrastructure.Conversation;
using ObjecTalk.Infrastructure.Conversation.Audio;
using ObjecTalk.Infrastructure.Conversation.OpenAI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ObjecTalk.Presentation.Talk
{
    public interface IRecognitionObservationSource
    {
        event Action<RecognizedObjectState> RecognitionChanged;
        string CurrentUserId { get; }
    }

    public sealed class TalkController : MonoBehaviour
    {
        [SerializeField] private TalkConfiguration configuration;
        [SerializeField] private MonoBehaviour recognitionSourceBehaviour;
        [SerializeField] private TalkView view;
        [SerializeField] private AudioSource microphoneSource;
        [SerializeField] private AudioSource remoteAudioSource;
        [Tooltip("Optional. Push-to-talk mode uses this if set; otherwise a hold button is created at runtime.")]
        [SerializeField] private PushToTalkButton pushToTalkButton;

        private IRecognitionObservationSource recognitionSource;
        private ObjectConversationCoordinator coordinator;
        private IVoiceConversationService voiceService;
        private DatabaseManager database;
        private bool subscribed;
        private bool tickRunning;
        private int stopRequested;
        private GameObject runtimePushToTalkButton;
        private UnityEngine.UI.Image pushToTalkImage;
        private TMPro.TextMeshProUGUI pushToTalkLabel;
        private float conversationActiveSince = -1f;

        private static readonly Color PushToTalkIdleColor = new Color(0.12f, 0.44f, 0.36f);
        private static readonly Color PushToTalkHeldColor = new Color(0.96f, 0.80f, 0.20f);

        private void Awake()
        {
            if (coordinator == null)
            {
                ComposeRuntimeGraph();
            }
        }

        private void OnEnable()
        {
            Interlocked.Exchange(ref stopRequested, 0);
            Subscribe();
            ShowPushToTalkButton(false);
        }

        private void Update()
        {
            if (!tickRunning && coordinator != null)
            {
                TickAsync();
            }

            EnforceSessionTimeLimit();
        }

        // Exhibition guard: cap one visitor's conversation so the booth keeps moving and the daily
        // budget lasts. 0 = off.
        private void EnforceSessionTimeLimit()
        {
            if (configuration == null || configuration.SessionMaxSeconds <= 0f || conversationActiveSince < 0f)
            {
                return;
            }

            if (Time.realtimeSinceStartup - conversationActiveSince > configuration.SessionMaxSeconds)
            {
                conversationActiveSince = -1f;
                view?.ShowFailure(new ConversationFailure("talk_session_time_limit", true, "体験時間になりました。次の人にかわってね。"));
                RequestStop(ConversationStopReason.UserRequested);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                return;
            }

            // The OS microphone-permission dialog pauses the app while the first StartAsync is still
            // awaiting the grant. Stopping here would cancel that start, and the grant would then
            // race a teardown. A real background (any other state) still ends the conversation.
            if (voiceService != null && voiceService.CurrentState == ConversationState.RequestingPermission)
            {
                return;
            }

            RequestStop(ConversationStopReason.ApplicationPaused);
        }

        private void OnDisable()
        {
            // Hide first: Unsubscribe drops the state handler, so the Idle transition from the stop
            // below would not reach OnStateChanged to hide the button (it lives on the Canvas, not
            // under this panel, so it does not deactivate with us).
            ShowPushToTalkButton(false);
            Unsubscribe();
            RequestStop(ConversationStopReason.UserRequested);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            RequestStop(ConversationStopReason.UserRequested);
            if (pushToTalkButton != null)
            {
                pushToTalkButton.HeldChanged -= OnPushToTalkHeldChanged;
            }

            if (runtimePushToTalkButton != null)
            {
                Destroy(runtimePushToTalkButton);
            }

            database?.Dispose();
            database = null;
        }

        public void InitializeForTests(
            IRecognitionObservationSource source,
            ObjectConversationCoordinator conversationCoordinator,
            IVoiceConversationService conversationService,
            TalkView talkView)
        {
            Unsubscribe();
            recognitionSource = source ?? throw new ArgumentNullException(nameof(source));
            coordinator = conversationCoordinator ?? throw new ArgumentNullException(nameof(conversationCoordinator));
            voiceService = conversationService ?? throw new ArgumentNullException(nameof(conversationService));
            view = talkView ?? throw new ArgumentNullException(nameof(talkView));
            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void ComposeRuntimeGraph()
        {
            recognitionSource = recognitionSourceBehaviour as IRecognitionObservationSource;
            if (recognitionSource == null || configuration == null || view == null)
            {
                Debug.LogError(
                    "TalkController is missing a serialized reference " +
                    $"(recognitionSource={(recognitionSource == null ? "NULL" : "ok")}, " +
                    $"configuration={(configuration == null ? "NULL" : "ok")}, view={(view == null ? "NULL" : "ok")}).");
                enabled = false;
                return;
            }

            try
            {
                database = new DatabaseManager();
                var repository = new ConversationObjectRepository(database, () => recognitionSource.CurrentUserId);
                var callClient = new RealtimeCallClient(
                    configuration.ResolveBackendBaseUrl(),
                    new FirebaseIdTokenProvider(),
                    new UnityWebRequestRealtimeHttpClient(),
                    configuration.ExhibitionMode);
                var transport = new RealtimeWebRtcTransport(
                    new UnityWebRtcPeerConnectionAdapter(),
                    new RealtimeCallSdpExchange(callClient),
                    microphoneSource,
                    ResolveRemoteAudioSource());
                voiceService = new OpenAIRealtimeConversationService(
                    new ConversationStateMachine(),
                    new MicrophonePermissionService(),
                    transport,
                    new TaskDelayProvider(),
                    pushToTalk: !configuration.HandsFree);
                coordinator = new ObjectConversationCoordinator(
                    new SystemClock(),
                    repository,
                    voiceService,
                    configuration.RecognitionLossGrace,
                    configuration.ObjectSwitchConfirmation);
                SetUpPushToTalk();
            }
            catch (Exception ex)
            {
                Debug.LogError($"TalkController failed to initialise the conversation graph: {ex}");
                view.ShowFailure(new ConversationFailure("talk_initialization_failed", false, "音声会話を初期化できませんでした。"));
                enabled = false;
            }
        }

        // Push-to-talk mode: the microphone is closed unless a hold button is pressed. Use a button
        // wired in the scene if one is set, otherwise build a plain full-width one at the bottom so
        // the mode works with no scene edit.
        private void SetUpPushToTalk()
        {
            if (configuration.HandsFree || voiceService == null)
            {
                return;
            }

            if (pushToTalkButton == null)
            {
                pushToTalkButton = CreateRuntimePushToTalkButton();
                if (pushToTalkButton != null)
                {
                    runtimePushToTalkButton = pushToTalkButton.gameObject;
                }
            }

            if (pushToTalkButton == null)
            {
                Debug.LogWarning("Push-to-talk is enabled but no button could be set up; the microphone will stay closed.");
                return;
            }

            pushToTalkButton.HeldChanged += OnPushToTalkHeldChanged;
            ShowPushToTalkButton(false);
        }

        private void OnPushToTalkHeldChanged(bool held)
        {
            voiceService?.SetPushToTalkActive(held);

            if (pushToTalkImage != null)
            {
                pushToTalkImage.color = held ? PushToTalkHeldColor : PushToTalkIdleColor;
            }

            if (pushToTalkLabel != null)
            {
                pushToTalkLabel.text = held ? "はなす（指をはなすと返事）" : "おして話す";
                pushToTalkLabel.color = held ? new Color(0.14f, 0.30f, 0.24f) : Color.white;
            }
        }

        private void ShowPushToTalkButton(bool visible)
        {
            if (pushToTalkButton != null && pushToTalkButton.gameObject.activeSelf != visible)
            {
                pushToTalkButton.gameObject.SetActive(visible);
            }
        }

        private PushToTalkButton CreateRuntimePushToTalkButton()
        {
            var canvas = view != null ? view.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                canvas = FindAnyObjectByType<Canvas>();
            }

            if (canvas == null)
            {
                return null;
            }

            if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var es = new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
                es.transform.SetParent(canvas.transform.root, false);
            }

            var go = new GameObject("PushToTalkButton (runtime)",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(PushToTalkButton));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(40f, 48f);
            rect.offsetMax = new Vector2(-40f, 48f + 300f);
            rect.SetAsLastSibling();

            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.color = PushToTalkIdleColor;
            image.raycastTarget = true;
            pushToTalkImage = image;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            try
            {
                var label = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
                label.text = "おして話す";
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.enableAutoSizing = true;
                label.fontSizeMin = 24f;
                label.fontSizeMax = 64f;
                label.color = Color.white;
                label.raycastTarget = false;
                pushToTalkLabel = label;
            }
            catch (Exception)
            {
                // A missing TMP font must not stop the button working; it just has no label.
            }

            return go.GetComponent<PushToTalkButton>();
        }

        // Unity WebRTC binds one OnAudioFilterRead filter per AudioSource, so the microphone capture
        // track and the remote playback track cannot share a GameObject — the second one to attach
        // is refused ("GameObject has multiple AudioSources ... AudioCustomFilter"). When the scene
        // wires both fields to the same object, move remote playback onto a dedicated child.
        private AudioSource ResolveRemoteAudioSource()
        {
            if (remoteAudioSource == null || microphoneSource == null ||
                remoteAudioSource.gameObject != microphoneSource.gameObject)
            {
                return remoteAudioSource;
            }

            var host = new GameObject("RemoteConversationAudio");
            host.transform.SetParent(transform, false);
            var dedicated = host.AddComponent<AudioSource>();
            dedicated.playOnAwake = false;
            dedicated.spatialize = false;
            dedicated.spatialBlend = 0f;
            remoteAudioSource = dedicated;
            return dedicated;
        }

        private void Subscribe()
        {
            if (subscribed || recognitionSource == null || coordinator == null || voiceService == null)
            {
                return;
            }

            recognitionSource.RecognitionChanged += OnRecognitionChanged;
            coordinator.FailureOccurred += OnFailure;
            voiceService.StateChanged += OnStateChanged;
            voiceService.UserTranscriptReceived += OnTranscriptReceived;
            voiceService.AssistantTranscriptReceived += OnTranscriptReceived;
            voiceService.ErrorOccurred += OnFailure;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            recognitionSource.RecognitionChanged -= OnRecognitionChanged;
            coordinator.FailureOccurred -= OnFailure;
            voiceService.StateChanged -= OnStateChanged;
            voiceService.UserTranscriptReceived -= OnTranscriptReceived;
            voiceService.AssistantTranscriptReceived -= OnTranscriptReceived;
            voiceService.ErrorOccurred -= OnFailure;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            subscribed = false;
        }

        private async void OnRecognitionChanged(RecognizedObjectState state)
        {
            if (state != null && state.IsRecognized)
            {
                view.SetCurrentObjectName(state.ObjectName);
            }

            try
            {
                await coordinator.ObserveAsync(state);
            }
            catch (Exception)
            {
                OnFailure(new ConversationFailure("talk_observation_failed", true, "物体の認識結果を会話に反映できませんでした。"));
            }
        }

        private async void TickAsync()
        {
            tickRunning = true;
            try
            {
                await coordinator.TickAsync();
            }
            catch (Exception)
            {
                OnFailure(new ConversationFailure("talk_tick_failed", true, "音声会話の状態を更新できませんでした。"));
            }
            finally
            {
                tickRunning = false;
            }
        }

        private async void RequestStop(ConversationStopReason reason)
        {
            if (coordinator == null || Interlocked.Exchange(ref stopRequested, 1) != 0)
            {
                return;
            }

            try
            {
                await coordinator.StopAsync(reason);
            }
            catch (Exception)
            {
                OnFailure(new ConversationFailure("talk_stop_failed", true, "音声会話を終了できませんでした。"));
            }
            finally
            {
                view?.ShowState(ConversationState.Idle);
                Interlocked.Exchange(ref stopRequested, 0);
            }
        }

        private void OnStateChanged(ConversationState state)
        {
            if (state == ConversationState.Idle)
            {
                conversationActiveSince = -1f;
            }
            else if (conversationActiveSince < 0f)
            {
                conversationActiveSince = Time.realtimeSinceStartup;
            }

            bool conversationLive = state == ConversationState.Listening
                || state == ConversationState.Speaking
                || state == ConversationState.Reconnecting;
            ShowPushToTalkButton(conversationLive);

            view?.ShowState(state);
        }

        private void OnTranscriptReceived(string transcript) => view?.ShowTranscript(transcript);

        private void OnFailure(ConversationFailure failure) => view?.ShowFailure(failure);
        private void OnSceneUnloaded(Scene scene) => RequestStop(ConversationStopReason.UserRequested);

        private sealed class SystemClock : IClock
        {
            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        }
    }
}
