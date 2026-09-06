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
            if (paused)
            {
                RequestStop(ConversationStopReason.ApplicationPaused);
            }
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
                    new UnityWebRequestRealtimeHttpClient());
                var transport = new RealtimeWebRtcTransport(
                    new UnityWebRtcPeerConnectionAdapter(),
                    new RealtimeCallSdpExchange(callClient),
                    microphoneSource,
                    remoteAudioSource);
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
            }
            catch (Exception)
            {
                view.ShowFailure(new ConversationFailure("talk_initialization_failed", false, "音声会話を初期化できませんでした。"));
                enabled = false;
            }
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

        private void OnStateChanged(ConversationState state) => view?.ShowState(state);
        private void OnTranscriptReceived(string transcript) => view?.ShowTranscript(transcript);
        private void OnFailure(ConversationFailure failure) => view?.ShowFailure(failure);
        private void OnSceneUnloaded(Scene scene) => RequestStop(ConversationStopReason.UserRequested);

        private sealed class SystemClock : IClock
        {
            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        }
    }
}
