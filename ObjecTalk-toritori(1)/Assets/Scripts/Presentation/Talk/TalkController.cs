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

        private IRecognitionObservationSource recognitionSource;
        private ObjectConversationCoordinator coordinator;
        private IVoiceConversationService voiceService;
        private DatabaseManager database;
        private bool subscribed;
        private bool tickRunning;
        private int stopRequested;

        private void Awake()
        {
            Debug.Log($"[TalkDiag] TalkController.Awake on '{gameObject.name}' (activeInHierarchy={gameObject.activeInHierarchy})");
            if (coordinator == null)
            {
                ComposeRuntimeGraph();
            }
        }

        private void OnEnable()
        {
            Interlocked.Exchange(ref stopRequested, 0);
            Subscribe();
        }

        private void Update()
        {
            if (!tickRunning && coordinator != null)
            {
                TickAsync();
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
            Unsubscribe();
            RequestStop(ConversationStopReason.UserRequested);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            RequestStop(ConversationStopReason.UserRequested);
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
                    $"[TalkDiag] ComposeRuntimeGraph aborted: recognitionSource={(recognitionSource == null ? "NULL" : "ok")} " +
                    $"configuration={(configuration == null ? "NULL" : "ok")} view={(view == null ? "NULL" : "ok")} " +
                    $"recognitionSourceBehaviour={(recognitionSourceBehaviour == null ? "NULL" : recognitionSourceBehaviour.GetType().Name)}");
                enabled = false;
                return;
            }

            try
            {
                Debug.Log($"[TalkDiag] ComposeRuntimeGraph: backendBaseUrl={configuration.ResolveBackendBaseUrl()}");
                database = new DatabaseManager();
                var repository = new ConversationObjectRepository(database, () => recognitionSource.CurrentUserId);
                var callClient = new RealtimeCallClient(
                    configuration.ResolveBackendBaseUrl(),
                    new FirebaseIdTokenProvider(),
                    new UnityWebRequestRealtimeHttpClient());
                var transport = new RealtimeWebRtcTransport(
                    new UnityWebRtcPeerConnectionAdapter(),
                    new RealtimeCallSdpExchange(callClient),
                    microphoneSource,
                    ResolveRemoteAudioSource());
                voiceService = new OpenAIRealtimeConversationService(
                    new ConversationStateMachine(),
                    new MicrophonePermissionService(),
                    transport,
                    new TaskDelayProvider());
                coordinator = new ObjectConversationCoordinator(
                    new SystemClock(),
                    repository,
                    voiceService,
                    configuration.RecognitionLossGrace,
                    configuration.ObjectSwitchConfirmation);
                Debug.Log("[TalkDiag] ComposeRuntimeGraph succeeded");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TalkDiag] ComposeRuntimeGraph threw: {ex}");
                view.ShowFailure(new ConversationFailure("talk_initialization_failed", false, "音声会話を初期化できませんでした。"));
                enabled = false;
            }
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
            Debug.Log("[TalkDiag] ResolveRemoteAudioSource: moved remote playback to dedicated child GameObject");
            return dedicated;
        }

        private void Subscribe()
        {
            if (subscribed || recognitionSource == null || coordinator == null || voiceService == null)
            {
                Debug.Log(
                    $"[TalkDiag] Subscribe skipped: subscribed={subscribed} " +
                    $"recognitionSource={(recognitionSource == null ? "NULL" : "ok")} " +
                    $"coordinator={(coordinator == null ? "NULL" : "ok")} " +
                    $"voiceService={(voiceService == null ? "NULL" : "ok")}");
                return;
            }

            Debug.Log("[TalkDiag] Subscribe: wired recognition + voice events");
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
            Debug.Log($"[TalkDiag] OnRecognitionChanged: recognized={state?.IsRecognized} id='{state?.ObjectId}' name='{state?.ObjectName}'");
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
            Debug.Log($"[TalkDiag] ConversationState -> {state}");
            view?.ShowState(state);
        }

        private void OnTranscriptReceived(string transcript) => view?.ShowTranscript(transcript);

        private void OnFailure(ConversationFailure failure)
        {
            Debug.LogWarning($"[TalkDiag] Failure: code={failure?.Code} retryable={failure?.IsRetryable} message={failure?.UserMessage}");
            view?.ShowFailure(failure);
        }
        private void OnSceneUnloaded(Scene scene) => RequestStop(ConversationStopReason.UserRequested);

        private sealed class SystemClock : IClock
        {
            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        }
    }
}
