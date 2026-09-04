using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Domain.Models;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.AI;
using ObjecTalk.Infrastructure.Database;
using ObjecTalk.Infrastructure.Vision;
using Unity.InferenceEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Presentation and lifecycle for the camera panel. It owns the camera, the panels, and the
/// registration UI; every recognition decision belongs to <see cref="ObjectRecognitionService"/>,
/// which this controller only starts, ticks, and renders.
/// </summary>
public class EmbeddingObjectMatcher : MonoBehaviour
{
    // ★★★ 物体認識フラグ ★★★
    /// <summary>
    /// 物体が現在認識されているかどうかを示すフラグ
    /// 1 = 認識中, 0 = 認識なし
    /// </summary>
    public int IsObjectRecognized { get; private set; } = 0;

    /// <summary>
    /// 最後に認識された物体の名前
    /// </summary>
    public string LastRecognizedObjectName { get; private set; } = "";

    /// <summary>
    /// 認識状態が変化したときに発火するイベント
    /// (bool isRecognized, string objectName)
    /// </summary>
    public event Action<bool, string> OnRecognitionStateChanged
    {
        add => _recognitionEvents.LegacyStateChanged += value;
        remove => _recognitionEvents.LegacyStateChanged -= value;
    }

    /// <summary>
    /// Raised when recognition changes, carrying the persisted object identity for conversation
    /// consumers. The name-only event remains for existing scene and prefab consumers.
    /// </summary>
    public event Action<RecognizedObjectState> RecognitionChanged
    {
        add => _recognitionEvents.ObservationPublished += value;
        remove => _recognitionEvents.ObservationPublished -= value;
    }

    public static StartupMode DesiredMode { get; set; } = StartupMode.Talk;
    public static EmbeddingObjectMatcher Instance { get; private set; } // Singleton Instance

    /// <summary>Scene returned to when the user leaves the camera flow.</summary>
    public const string HomeSceneName = "Myscenes";

    // --- App Mode ---
    private AppMode currentMode = AppMode.Recognition;
    private DatabaseManager dbManager;

    [Header("AI Model")]
    [Tooltip("Assets/Models/mobilenetv3-small-embedding-fp16.onnx")]
    public ModelAsset modelAsset;

    [Header("Part Styles")]
    public List<Sprite> eyeStyles;
    public List<Sprite> mouthStyles;

    [Header("UI Elements")]
    public FlexibleUIManager uiManager; // Used for Naming/Placement transition
    public GameObject registerCameraPanel; // Registration camera panel
    public GameObject talkCameraPanel;     // Conversation camera panel
    public GameObject namingPanel;
    public GameObject placementPanel;

    [Header("Panel Switching (Set in Inspector)")]
    public GameObject talkSelectionPanel;   // "Mode Selection Panel"
    public GameObject objectMatcherRootPanel; // Parent panel containing this script (entire camera panel)

    [Header("Camera Display (Set in Inspector)")]
    public RawImage registerCameraDisplay; // RawImage in RegisterCameraPanel
    public RawImage talkCameraDisplay;     // RawImage in TalkCameraPanel

    [Header("Other UI Components")]
    public Image snapshotDisplay_Naming;
    public Image snapshotDisplay_Placement;
    public Button registerStartButton; // Button in RegisterCameraPanel
    public Button nextButton;
    public Button confirmButton;

    [Tooltip("Every BackButton inside MatcherRootPanel. All routed to OnBackButtonClicked, which " +
             "picks the destination from the current mode.")]
    public Button[] backButtons;

    [Tooltip("The 'finish' button on TalkCameraPanel. Stops the camera and returns to the home scene.")]
    public Button finishButton;
    public TMP_InputField nameInputField;
    public TMP_InputField categoryInputField;
    public TMP_Text resultText; // Text in TalkCameraPanel

    [Header("Registration Mode UI")]
    public RectTransform placementEyeImage;
    public RectTransform placementMouthImage;

    [Tooltip("撮影枚数と再撮影の案内を表示するテキスト。未設定なら resultText を使う。")]
    public TMP_Text registrationStatusText;

    [Header("Recognition Mode UI (TalkCameraPanel)")]
    public Image recognitionEyeImage;
    public Image recognitionMouthImage;

    [Header("Style Selection UI")]
    public ScrollRect styleScrollViewRect;
    public GameObject styleButtonPrefab;
    public Transform styleButtonParent;
    public Toggle eyeToggle;
    public Toggle mouthToggle;

    // Every matching threshold the pipeline uses lives here; nothing downstream hard-codes one.
    [Header("Recognition Settings")]
    [Range(0.5f, 1.0f)] public float similarityThreshold = 0.85f;
    [Tooltip("最良候補が2位をこれだけ上回らなければ、曖昧として棄却する。")]
    [Range(0.0f, 0.5f)] public float ambiguityMargin = 0.05f;
    [Tooltip("AKAZEが一致とみなすのに必要な記述子の数。")]
    [Range(1, 64)] public int minimumLocalMatchCount = RecognitionSettings.DefaultMinimumLocalMatchCount;
    [Tooltip("認識状態に入るために必要な連続ヒット数。")]
    [Range(1, 10)] public int requiredConsecutiveHits = RecognitionSettings.DefaultRequiredConsecutiveHits;
    [Range(0.2f, 0.5f)] public float minimumRecognitionInterval = RecognitionSettings.MinimumSupportedRecognitionIntervalSeconds;
    [Range(0.2f, 0.5f)] public float maximumRecognitionInterval = RecognitionSettings.MaximumSupportedRecognitionIntervalSeconds;

    [Header("Recognition Loss Settings")]
    [Tooltip("物体が見えなくなってから認識フラグを0にするまでの猶予時間(秒)")]
    [Range(0.1f, 5.0f)] public float recognitionLossDelay = 1.0f;

    // --- Recognition pipeline ---
    private ObjectRecognitionService _recognitionService;
    private CameraFramePreprocessor _preprocessor;
    private ILocalFeatureVerifier _verifier;
    private SentisEmbeddingExtractor _extractor;
    private bool _recognitionAvailable;

    // --- Private Variables ---
    private readonly Dictionary<string, RegisteredObject> _objectsById =
        new Dictionary<string, RegisteredObject>(StringComparer.Ordinal);
    private readonly RecognitionEventPublisher _recognitionEvents = new RecognitionEventPublisher();
    private Texture2D tempSnapshot;
    private int currentEyeId = -1;
    private int currentMouthId = -1;
    private Vector2 tempRelativeEyePos = new Vector2(-1, -1);
    private Vector2 tempRelativeMouthPos = new Vector2(-1, -1);
    private WebCamTexture webcamTexture;
    private string _registrationObjectId;
    private bool _cameraAuthorized;

    /// <summary>Leaves the camera flow: stops the camera and loads the home scene.</summary>
    public void ReturnToHome()
    {
        Debug.Log("Returning to the home scene and stopping camera...");

        // Deactivating the root panel runs OnDisable, which stops the webcam.
        if (objectMatcherRootPanel != null)
        {
            objectMatcherRootPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("objectMatcherRootPanel is not set!");
        }

        SceneManager.LoadScene(HomeSceneName);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        dbManager = new DatabaseManager();
        BuildRecognitionPipeline();

        // --- Register Listeners ---
        if (registerStartButton != null) registerStartButton.onClick.AddListener(OnRegisterStart);
        else Debug.LogError("Register Start Button is not assigned!");

        if (nextButton != null) nextButton.onClick.AddListener(OnPlacementConfirmed);
        else Debug.LogError("Next Button is not assigned!");

        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmRegistration);
        else Debug.LogError("Confirm Button is not assigned!");

        if (backButtons != null)
        {
            foreach (var back in backButtons)
            {
                if (back != null) back.onClick.AddListener(OnBackButtonClicked);
            }
        }
        else Debug.LogError("Back Buttons are not assigned!");

        if (finishButton != null) finishButton.onClick.AddListener(ReturnToHome);
        else Debug.LogError("Finish Button is not assigned!");

        // Toggles
        if (eyeToggle != null) eyeToggle.onValueChanged.AddListener(OnCategoryChanged);
        if (mouthToggle != null) mouthToggle.onValueChanged.AddListener(OnCategoryChanged);
        // --- End Listeners ---
    }

    /// <summary>
    /// Creates the recognition pipeline. Any failure here disables recognition and shows a
    /// Japanese message rather than throwing, so the rest of the app keeps working.
    /// </summary>
    private void BuildRecognitionPipeline()
    {
        if (modelAsset == null)
        {
            Debug.LogError("modelAsset is not assigned in the Inspector!");
            ShowRecognitionUnavailable(RecognitionMessages.ModelUnavailable);
            return;
        }

        _preprocessor = new CameraFramePreprocessor();
        _verifier = BuildLocalFeatureVerifier();
        _extractor = new SentisEmbeddingExtractor();

        var spec = EmbeddingModelSpec.Default;
        if (!_extractor.Initialize(modelAsset, spec))
        {
            Debug.LogError("Failed to initialize the embedding extractor.");
            ShowRecognitionUnavailable(
                string.IsNullOrEmpty(_extractor.FailureMessage)
                    ? RecognitionMessages.RecognitionUnavailable
                    : _extractor.FailureMessage);
            DisposeRecognitionPipeline();
            return;
        }

        _recognitionService = new ObjectRecognitionService(
            _extractor,
            _preprocessor,
            _verifier,
            dbManager.RecognitionSamples,
            BuildRecognitionSettings(),
            new RecognitionDataVersion(1, spec.ModelVersion, spec.PreprocessingVersion));
        _recognitionService.RecognitionStateChanged += OnServiceRecognitionStateChanged;
        _recognitionAvailable = true;
    }

    /// <summary>
    /// AKAZE confirmation needs the OpenCV native library. An ARM64 player without an arm64
    /// OpenCvSharpExtern throws on the first OpenCV call, so fall back to embedding-only
    /// recognition rather than losing the whole pipeline. Restoring AKAZE later (adding the
    /// native library) needs no further code change.
    /// </summary>
    private ILocalFeatureVerifier BuildLocalFeatureVerifier()
    {
        try
        {
            var akaze = new AkazeVerifier();
            akaze.Initialize();
            return akaze;
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "AKAZE local-feature verification is unavailable; recognition will rely on the "
                + $"embedding model alone. ({e.GetType().Name}: {e.Message})");
            return new NullLocalFeatureVerifier();
        }
    }

    private RecognitionSettings BuildRecognitionSettings()
    {
        // The inspector can be left in an inconsistent state; clamp rather than throw in Awake.
        var minimumInterval = Mathf.Min(minimumRecognitionInterval, maximumRecognitionInterval);
        var maximumInterval = Mathf.Max(minimumRecognitionInterval, maximumRecognitionInterval);

        return new RecognitionSettings(
            similarityThreshold,
            ambiguityMargin,
            Mathf.Max(1, requiredConsecutiveHits),
            recognitionLossDelay,
            minimumInterval,
            maximumInterval,
            Mathf.Max(1, minimumLocalMatchCount));
    }

    // Start camera, load data, and set UI when the panel becomes active
    void OnEnable()
    {
        StartCoroutine(EnablePanelRoutine());
    }

    /// <summary>
    /// Camera permission can only be requested asynchronously, and the camera must not start
    /// before it is granted, so panel activation runs as a coroutine.
    /// </summary>
    private IEnumerator EnablePanelRoutine()
    {
        yield return RequestCameraAuthorization();

        if (!_cameraAuthorized)
        {
            SetStatusText(RecognitionMessages.CameraPermissionDenied);
            yield break;
        }

        if (webcamTexture == null)
        {
            if (WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("No webcam devices found.");
                SetStatusText(RecognitionMessages.CameraUnavailable);
                yield break;
            }

            webcamTexture = new WebCamTexture(WebCamTexture.devices[0].name, 1280, 720, 30);
        }

        // --- Load latest data from DB ---
        Debug.Log("OnEnable: Loading database...");
        LoadRegisteredObjects();
        // --- End Load ---

        // 1. Assign WebCamTexture to both RawImages and reset color
        if (registerCameraDisplay != null)
        {
            registerCameraDisplay.texture = webcamTexture;
            registerCameraDisplay.color = Color.white;
        }
        if (talkCameraDisplay != null)
        {
            talkCameraDisplay.texture = webcamTexture;
            talkCameraDisplay.color = Color.white;
        }

        // 2. Start the camera if it's not already playing
        if (!webcamTexture.isPlaying)
        {
            Debug.Log("OnEnable: Starting Webcam...");
            webcamTexture.Play();
        }

        _recognitionService?.Resume();

        // 3. Hide registration flow panels initially
        if (placementPanel != null) placementPanel.SetActive(false);
        if (namingPanel != null) namingPanel.SetActive(false);

        // 4. Decide which camera panel to show based on DesiredMode
        if (DesiredMode == StartupMode.Talk)
        {
            // Conversation Mode
            Debug.Log("OnEnable: Setting up Talk Mode UI.");
            if (talkCameraPanel != null) talkCameraPanel.SetActive(true);
            if (registerCameraPanel != null) registerCameraPanel.SetActive(false);

            currentMode = AppMode.Recognition;
            if (resultText != null && _recognitionAvailable) resultText.text = RecognitionMessages.Searching;
            if (recognitionEyeImage != null) recognitionEyeImage.gameObject.SetActive(false);
            if (recognitionMouthImage != null) recognitionMouthImage.gameObject.SetActive(false);
        }
        else // DesiredMode == StartupMode.Register
        {
            // Registration Mode
            Debug.Log("OnEnable: Setting up Register Mode UI.");
            if (talkCameraPanel != null) talkCameraPanel.SetActive(false);
            if (registerCameraPanel != null) registerCameraPanel.SetActive(true);

            currentMode = AppMode.Recognition;
            BeginRegistrationSession();
        }
    }

    private IEnumerator RequestCameraAuthorization()
    {
        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            _cameraAuthorized = true;
            yield break;
        }

        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        _cameraAuthorized = Application.HasUserAuthorization(UserAuthorization.WebCam);
        if (!_cameraAuthorized)
        {
            Debug.LogWarning("Camera permission was not granted.");
        }
    }

    // Stop camera when the panel becomes inactive
    void OnDisable()
    {
        Debug.Log("OnDisable: Stopping Webcam...");
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            webcamTexture.Stop();
        }

        // Pause abandons the run in flight and hands the GPU buffers back.
        _recognitionService?.Pause();
        UpdateRecognitionState(false, "", "", 0f);
    }

    /// <summary>Releases camera and inference work while the application is backgrounded.</summary>
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            if (webcamTexture != null && webcamTexture.isPlaying) webcamTexture.Stop();
            _recognitionService?.Pause();
            UpdateRecognitionState(false, "", "", 0f);
            return;
        }

        if (!isActiveAndEnabled)
        {
            return;
        }

        // Permission can be revoked while the app is in the background.
        StartCoroutine(ResumeAfterApplicationPause());
    }

    private IEnumerator ResumeAfterApplicationPause()
    {
        yield return RequestCameraAuthorization();

        if (!_cameraAuthorized)
        {
            SetStatusText(RecognitionMessages.CameraPermissionDenied);
            yield break;
        }

        if (webcamTexture != null && !webcamTexture.isPlaying)
        {
            webcamTexture.Play();
        }

        _recognitionService?.Resume();
    }

    void Update()
    {
        if (!_recognitionAvailable)
        {
            return;
        }

        // Only drive recognition in Talk mode; registration captures are user-triggered.
        if (currentMode == AppMode.Recognition
            && DesiredMode == StartupMode.Talk
            && webcamTexture != null
            && webcamTexture.didUpdateThisFrame)
        {
            _recognitionService.StartRecognition(webcamTexture, Time.timeAsDouble);
        }

        // One bounded slice of work per frame, whichever kind of run is in flight.
        var result = _recognitionService.Tick(Time.timeAsDouble);
        HandleTickResult(result);
    }

    private void HandleTickResult(RecognitionTickResult result)
    {
        switch (result.Status)
        {
            case RecognitionTickStatus.Recognized:
                if (_objectsById.TryGetValue(result.ObjectId, out var recognized))
                {
                    UpdateRecognitionState(true, result.ObjectId, recognized.Name, result.Score);
                }
                break;

            case RecognitionTickStatus.NotRecognized:
                UpdateRecognitionState(false, "", "", 0f);
                break;

            case RecognitionTickStatus.CaptureAccepted:
                SetStatusText(RecognitionMessages.RegistrationProgress(
                    result.CapturedViewCount,
                    RecognitionSettings.RequiredSamplesPerObject));
                if (result.CapturedViewCount >= RecognitionSettings.RequiredSamplesPerObject)
                {
                    EnterPlacementFromCaptures();
                }
                else
                {
                    SetRegisterButtonInteractable(true);
                }
                break;

            case RecognitionTickStatus.Failed:
                Debug.LogWarning("An inference run failed and was discarded.");
                SetRegisterButtonInteractable(true);
                break;
        }
    }

    void OnDestroy()
    {
        DisposeRecognitionPipeline();

        if (webcamTexture != null)
        {
            if (webcamTexture.isPlaying) webcamTexture.Stop();
        }

        dbManager?.Dispose();
        dbManager = null;
        Debug.Log("EmbeddingObjectMatcher OnDestroy completed.");
    }

    /// <summary>
    /// The service owns the extractor, preprocessor, and verifier, so disposing it releases them.
    /// The individual fields are only disposed when the service was never built.
    /// </summary>
    private void DisposeRecognitionPipeline()
    {
        _recognitionAvailable = false;

        if (_recognitionService != null)
        {
            _recognitionService.RecognitionStateChanged -= OnServiceRecognitionStateChanged;
            _recognitionService.Dispose();
            _recognitionService = null;
            _extractor = null;
            _preprocessor = null;
            _verifier = null;
            return;
        }

        _extractor?.Dispose();
        _extractor = null;
        _preprocessor?.Dispose();
        _preprocessor = null;
        _verifier?.Dispose();
        _verifier = null;
    }

    private void OnServiceRecognitionStateChanged(RecognitionStateChange change)
    {
        if (!change.IsRecognized)
        {
            UpdateRecognitionState(false, "", "", 0f, false);
            return;
        }

        if (_objectsById.TryGetValue(change.ObjectId, out var registered))
        {
            UpdateRecognitionState(true, change.ObjectId, registered.Name, change.Score, false);
        }
    }

    /// <summary>
    /// 物体認識状態を更新し、変化があればイベントを発火します
    /// </summary>
    private void UpdateRecognitionState(
        bool isRecognized,
        string objectId,
        string objectName,
        float confidence,
        bool publishObservation = true)
    {
        int newState = isRecognized ? 1 : 0;
        string newName = isRecognized ? objectName : "";
        string newObjectId = isRecognized ? objectId : "";
        bool legacyStateChanged = publishObservation
            ? _recognitionEvents.PublishObservation(
                isRecognized,
                newObjectId,
                newName,
                confidence,
                DateTimeOffset.UtcNow,
                ApplyLegacyRecognitionState)
            : _recognitionEvents.PublishLegacyState(
                isRecognized,
                newName,
                ApplyLegacyRecognitionState);

        // 状態が変化した場合のみ処理 (0->1, 1->0, or 1->"A" -> 1->"B")
        if (legacyStateChanged)
        {
            if (resultText != null && DesiredMode == StartupMode.Talk)
            {
                resultText.text = isRecognized ? newName : RecognitionMessages.Searching;
            }

            if (isRecognized)
            {
                Debug.Log($"★ 物体認識: ON (1) - 物体名: {objectName}");
            }
            else
            {
                Debug.Log($"★ 物体認識: OFF (0)");
            }
        }

        void ApplyLegacyRecognitionState()
        {
            IsObjectRecognized = newState;
            LastRecognizedObjectName = newName;
        }
    }

    #region Mode Switching
    private void SwitchToNamingMode()
    {
        currentMode = AppMode.Naming;
        if (tempSnapshot == null) { Debug.LogError("Snapshot is missing!"); return; }
        // Explicitly use UnityEngine.Rect
        var sprite = Sprite.Create(tempSnapshot, new UnityEngine.Rect(0, 0, tempSnapshot.width, tempSnapshot.height), new Vector2(0.5f, 0.5f));
        if (snapshotDisplay_Naming != null) snapshotDisplay_Naming.sprite = sprite;
        else Debug.LogError("SnapshotDisplay_Naming is not assigned!");

        if (namingPanel != null) namingPanel.SetActive(true);
        if (placementPanel != null) placementPanel.SetActive(false);

        if (nameInputField != null) nameInputField.text = "";
        if (categoryInputField != null) categoryInputField.text = "";
        Debug.Log("Switched to Naming Mode.");
    }

    private void SwitchToPlacementMode(Texture2D snapshot)
    {
        currentMode = AppMode.Placement;
        // Explicitly use UnityEngine.Rect
        if (snapshotDisplay_Placement != null) snapshotDisplay_Placement.sprite = Sprite.Create(snapshot, new UnityEngine.Rect(0, 0, snapshot.width, snapshot.height), new Vector2(0.5f, 0.5f));
        else Debug.LogError("SnapshotDisplay_Placement is not assigned!");

        if (mouthToggle != null) mouthToggle.isOn = false;
        if (eyeToggle != null) eyeToggle.isOn = true; // Default to eye styles
        PopulateStyleScrollView(true); // Populate eye styles

        // Ensure parts are initially hidden
        if (placementEyeImage != null) placementEyeImage.gameObject.SetActive(false);
        if (placementMouthImage != null) placementMouthImage.gameObject.SetActive(false);
        // Reset IDs
        currentEyeId = -1;
        currentMouthId = -1;
        Debug.Log("Switched to Placement Mode.");
    }
    #endregion

    #region Button Handlers & UI Logic
    public void SelectEyeStyle(int id)
    {
        if (eyeStyles == null || id < 0 || id >= eyeStyles.Count) return;
        currentEyeId = id;
        if (placementEyeImage != null)
        {
            Image img = placementEyeImage.GetComponent<Image>();
            if (img != null) img.sprite = eyeStyles[id];
            placementEyeImage.gameObject.SetActive(true);
            Debug.Log($"Selected Eye Style ID: {id}");
        }
    }

    public void SelectMouthStyle(int id)
    {
        if (mouthStyles == null || id < 0 || id >= mouthStyles.Count) return;
        currentMouthId = id;
        if (placementMouthImage != null)
        {
            Image img = placementMouthImage.GetComponent<Image>();
            if (img != null) img.sprite = mouthStyles[id];
            placementMouthImage.gameObject.SetActive(true);
            Debug.Log($"Selected Mouth Style ID: {id}");
        }
    }

    /// <summary>Opens a fresh three-view registration and resets the capture counter.</summary>
    private void BeginRegistrationSession()
    {
        if (!_recognitionAvailable)
        {
            return;
        }

        _registrationObjectId = Guid.NewGuid().ToString();
        _recognitionService.BeginRegistration(_registrationObjectId);
        DestroySnapshot();
        SetStatusText(RecognitionMessages.RegistrationProgress(0, RecognitionSettings.RequiredSamplesPerObject));
        SetRegisterButtonInteractable(true);
    }

    /// <summary>
    /// Captures one of the three required views. The embedding runs asynchronously; the capture is
    /// only counted when <see cref="Update"/> sees it complete.
    /// </summary>
    public void OnRegisterStart()
    {
        if (confirmButton != null) { confirmButton.interactable = true; }

        if (!_recognitionAvailable)
        {
            SetStatusText(RecognitionMessages.RecognitionUnavailable);
            return;
        }

        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            Debug.LogError("Webcam not available or not playing!");
            SetStatusText(RecognitionMessages.CameraUnavailable);
            return;
        }

        if (!_recognitionService.IsRegistering)
        {
            BeginRegistrationSession();
        }

        // The first view is the one the user places the face on, so keep its snapshot.
        if (tempSnapshot == null)
        {
            try
            {
                tempSnapshot = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
                tempSnapshot.SetPixels(webcamTexture.GetPixels());
                tempSnapshot.Apply();
                Debug.Log("Snapshot taken.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error taking snapshot: {e.Message}");
                DestroySnapshot();
                return;
            }
        }

        var status = _recognitionService.CaptureRegistrationView(webcamTexture);
        switch (status)
        {
            case RegistrationCaptureStatus.Started:
                // Block the button until the run finishes so captures cannot queue up.
                SetRegisterButtonInteractable(false);
                break;

            case RegistrationCaptureStatus.Featureless:
                SetStatusText(RecognitionMessages.RegistrationRetry);
                break;

            case RegistrationCaptureStatus.Unavailable:
                SetStatusText(RecognitionMessages.RecognitionUnavailable);
                break;

            case RegistrationCaptureStatus.NotAccepting:
                EnterPlacementFromCaptures();
                break;
        }
    }

    private void EnterPlacementFromCaptures()
    {
        SetRegisterButtonInteractable(true);
        if (tempSnapshot == null)
        {
            Debug.LogError("Three views were captured but no snapshot was kept.");
            return;
        }

        if (registerCameraPanel != null) registerCameraPanel.SetActive(false);
        if (placementPanel != null) placementPanel.SetActive(true);

        SwitchToPlacementMode(tempSnapshot);
    }

    public void OnPlacementConfirmed()
    {
        Debug.Log("OnPlacementConfirmed called. Calculating positions...");

        tempRelativeEyePos = new Vector2(-1, -1);
        if (placementEyeImage != null && placementEyeImage.gameObject.activeInHierarchy)
        {
            if (snapshotDisplay_Placement == null || snapshotDisplay_Placement.rectTransform == null)
            {
                Debug.LogError("snapshotDisplay_Placement reference is missing!");
                SwitchToNamingMode(); // Still proceed, but position will be invalid
                return;
            }
            Vector2 localEyePoint = snapshotDisplay_Placement.rectTransform.InverseTransformPoint(placementEyeImage.position);
            tempRelativeEyePos = new Vector2(
                Mathf.Clamp01((localEyePoint.x - snapshotDisplay_Placement.rectTransform.rect.xMin) / snapshotDisplay_Placement.rectTransform.rect.width),
                Mathf.Clamp01((localEyePoint.y - snapshotDisplay_Placement.rectTransform.rect.yMin) / snapshotDisplay_Placement.rectTransform.rect.height));
            Debug.Log($"Calculated tempRelativeEyePos: ({tempRelativeEyePos.x:F2}, {tempRelativeEyePos.y:F2})");
        }
        else
        {
            Debug.LogWarning("Eye image was not active or found when calculating position.");
        }

        tempRelativeMouthPos = new Vector2(-1, -1);
        if (placementMouthImage != null && placementMouthImage.gameObject.activeInHierarchy)
        {
            if (snapshotDisplay_Placement == null || snapshotDisplay_Placement.rectTransform == null)
            {
                Debug.LogError("snapshotDisplay_Placement reference is missing!");
                SwitchToNamingMode(); // Still proceed, but position will be invalid
                return;
            }
            Vector2 localMouthPoint = snapshotDisplay_Placement.rectTransform.InverseTransformPoint(placementMouthImage.position);
            tempRelativeMouthPos = new Vector2(
                Mathf.Clamp01((localMouthPoint.x - snapshotDisplay_Placement.rectTransform.rect.xMin) / snapshotDisplay_Placement.rectTransform.rect.width),
                Mathf.Clamp01((localMouthPoint.y - snapshotDisplay_Placement.rectTransform.rect.yMin) / snapshotDisplay_Placement.rectTransform.rect.height));
            Debug.Log($"Calculated tempRelativeMouthPos: ({tempRelativeMouthPos.x:F2}, {tempRelativeMouthPos.y:F2})");
        }
        else
        {
            Debug.LogWarning("Mouth image was not active or found when calculating position.");
        }

        SwitchToNamingMode();
    }

    public void OnBackButtonClicked()
    {
        if (currentMode == AppMode.Recognition)
        {
            // 「認識」画面（RegisterCameraPanel または TalkCameraPanel）から「モード選択」画面に戻る
            Debug.Log("Back button pressed on Recognition panel. Returning to selection panel.");

            CancelRegistrationSession();

            // 1. モード選択パネルを表示する
            if (talkSelectionPanel != null)
            {
                talkSelectionPanel.SetActive(true);
            }
            else
            {
                Debug.LogError("talkSelectionPanel が設定されていません！");
            }

            // 2. このカメラパネル（の親）を非表示にする (これにより OnDisable が呼ばれカメラが停止する)
            if (objectMatcherRootPanel != null)
            {
                Debug.Log("Deactivating objectMatcherRootPanel...");
                objectMatcherRootPanel.SetActive(false);
            }
            else
            {
                Debug.LogError("objectMatcherRootPanel が設定されていません！");
            }
        }
        else if (currentMode == AppMode.Naming)
        {
            // Naming -> Placement
            Debug.Log("Back button pressed on Naming panel. Returning to placement.");
            currentMode = AppMode.Placement;
            if (namingPanel != null) namingPanel.SetActive(false);
            if (placementPanel != null) placementPanel.SetActive(true);
            // Re-populate styles
            if (eyeToggle != null && mouthToggle != null)
            {
                if (eyeToggle.isOn) PopulateStyleScrollView(true); else PopulateStyleScrollView(false);
            }
        }
        else if (currentMode == AppMode.Placement)
        {
            // Placement -> RegisterCameraPanel. The captures are discarded, so the three views
            // always belong to one uninterrupted session.
            Debug.Log("Back button pressed on Placement panel. Returning to register camera.");
            currentMode = AppMode.Recognition;
            if (placementPanel != null) placementPanel.SetActive(false);

            if (registerCameraPanel != null) registerCameraPanel.SetActive(true);
            if (talkCameraPanel != null) talkCameraPanel.SetActive(false);

            DesiredMode = StartupMode.Register;
            CancelRegistrationSession();
            BeginRegistrationSession();
        }
    }

    private void CancelRegistrationSession()
    {
        if (_recognitionService != null && _recognitionService.IsRegistering)
        {
            _recognitionService.CancelRegistration();
        }

        _registrationObjectId = null;
        DestroySnapshot();
    }

    public void OnConfirmRegistration()
    {
        Debug.Log("OnConfirmRegistration called.");

        if (confirmButton != null) confirmButton.interactable = false;

        string objectName = nameInputField?.text ?? "";
        string objectCategory = categoryInputField?.text ?? "Default";

        if (string.IsNullOrEmpty(objectName))
        {
            Debug.LogWarning("Name is empty, aborting registration.");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }
        if (tempSnapshot == null)
        {
            Debug.LogError("tempSnapshot is null! Cannot register.");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        // Registration is only allowed to commit with all three valid views.
        if (!_recognitionAvailable
            || _recognitionService.CapturedViewCount != RecognitionSettings.RequiredSamplesPerObject)
        {
            Debug.LogWarning("Registration needs three valid captures before it can be saved.");
            SetStatusText(RecognitionMessages.RegistrationProgress(
                _recognitionService?.CapturedViewCount ?? 0,
                RecognitionSettings.RequiredSamplesPerObject));
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        User currentUser = IntegratedAuthManager.Instance?.CurrentUser;
        if (currentUser == null)
        {
            Debug.LogError("User not logged in. Cannot register object. Redirecting to Login.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("LoginScene");
            return;
        }

        var objectId = _registrationObjectId;
        try
        {
            // The samples join the objects table on object_id, so the object row is written first.
            var newObjectItem = new ObjectItem
            {
                object_id = objectId,
                user_id = currentUser.user_id,
                Name = objectName,
                ImageData = tempSnapshot.EncodeToJPG(),
                EyePositionX = tempRelativeEyePos.x,
                EyePositionY = tempRelativeEyePos.y,
                MouthPositionX = tempRelativeMouthPos.x,
                MouthPositionY = tempRelativeMouthPos.y,
                ImageWidth = tempSnapshot.width,
                ImageHeight = tempSnapshot.height,
                EyeStyleId = currentEyeId,
                MouthStyleId = currentMouthId
            };

            List<string> categories = new List<string>();
            if (!string.IsNullOrEmpty(objectCategory))
            {
                categories.Add(objectCategory.Trim());
            }

            if (dbManager == null) throw new Exception("dbManager is null.");
            dbManager.SaveObjectWithFeatures(newObjectItem, categories);

            if (!_recognitionService.CompleteRegistration())
            {
                // Never leave an object behind that recognition can never match.
                dbManager.DeleteObject(objectId);
                throw new Exception("The three recognition samples could not be committed.");
            }

            _objectsById[objectId] = new RegisteredObject
            {
                Name = objectName,
                RelativeEyePosition = tempRelativeEyePos,
                RelativeMouthPosition = tempRelativeMouthPos,
                EyeStyleId = currentEyeId,
                MouthStyleId = currentMouthId
            };

            Debug.Log($"Object '{objectName}' saved to DB successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during registration process: {e}");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        _registrationObjectId = null;
        DestroySnapshot();

        // --- Panel Transition ---
        Debug.Log("Registration successful. Returning to selection panel.");
        if (namingPanel != null) namingPanel.SetActive(false);
        if (talkSelectionPanel != null) talkSelectionPanel.SetActive(true);
        if (objectMatcherRootPanel != null) objectMatcherRootPanel.SetActive(false); // Triggers OnDisable
        // --- End ---
    }
    #endregion

    #region Style ScrollView
    void OnCategoryChanged(bool isOn)
    {
        if (!isOn) return;
        if (eyeToggle != null && eyeToggle.isOn)
        {
            PopulateStyleScrollView(true);
        }
        else
        {
            PopulateStyleScrollView(false);
        }
    }

    void PopulateStyleScrollView(bool isEyes)
    {
        if (styleButtonParent == null || styleButtonPrefab == null)
        {
            Debug.LogError("Style ScrollView references (Parent or Prefab) not set!");
            return;
        }
        foreach (Transform child in styleButtonParent)
        {
            Destroy(child.gameObject);
        }

        List<Sprite> stylesToShow = isEyes ? eyeStyles : mouthStyles;
        if (stylesToShow == null)
        {
            Debug.LogError(isEyes ? "Eye Styles list is null!" : "Mouth Styles list is null!");
            return;
        }

        for (int i = 0; i < stylesToShow.Count; i++)
        {
            if (stylesToShow[i] == null) continue; // Skip if sprite is missing

            GameObject buttonGO = Instantiate(styleButtonPrefab, styleButtonParent);
            Image buttonImage = buttonGO.GetComponent<Image>();
            Button buttonComp = buttonGO.GetComponent<Button>();

            if (buttonImage != null) buttonImage.sprite = stylesToShow[i];
            else Debug.LogWarning($"Prefab '{styleButtonPrefab.name}' is missing Image component.");

            if (buttonComp != null)
            {
                int styleId = i;
                buttonComp.onClick.RemoveAllListeners();
                if (isEyes)
                {
                    buttonComp.onClick.AddListener(() => SelectEyeStyle(styleId));
                }
                else
                {
                    buttonComp.onClick.AddListener(() => SelectMouthStyle(styleId));
                }
            }
            else Debug.LogWarning($"Prefab '{styleButtonPrefab.name}' is missing Button component.");
        }
        if (styleScrollViewRect != null) styleScrollViewRect.horizontalNormalizedPosition = 0;
    }
    #endregion

    #region Recognition Data
    /// <summary>
    /// Loads object metadata for display and hands the recognition samples to the service.
    /// Objects whose samples were written by an older model no longer match, so the user is told
    /// that re-registration is needed; their names, images, and placement are untouched.
    /// </summary>
    private void LoadRegisteredObjects()
    {
        _objectsById.Clear();

        if (dbManager == null) { Debug.LogError("DatabaseManager is null! Cannot load objects."); return; }
        User currentUser = IntegratedAuthManager.Instance?.CurrentUser;
        if (currentUser == null)
        {
            Debug.LogWarning("User not logged in, cannot load detection data from DB.");
            return;
        }

        List<ObjectItem> userObjects = dbManager.GetAllUserObjectsWithDetails(currentUser.user_id);
        if (userObjects != null)
        {
            foreach (var item in userObjects)
            {
                if (item == null || string.IsNullOrEmpty(item.object_id))
                {
                    continue;
                }

                _objectsById[item.object_id] = new RegisteredObject
                {
                    Name = item.Name,
                    RelativeEyePosition = new Vector2(item.EyePositionX, item.EyePositionY),
                    RelativeMouthPosition = new Vector2(item.MouthPositionX, item.MouthPositionY),
                    EyeStyleId = item.EyeStyleId,
                    MouthStyleId = item.MouthStyleId
                };
            }
        }

        if (!_recognitionAvailable)
        {
            return;
        }

        try
        {
            _recognitionService.LoadSamples(currentUser.user_id);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load recognition samples: {e}");
            return;
        }

        Debug.Log($"Loaded {_objectsById.Count} objects, {_recognitionService.RegisteredObjectCount} recognizable.");
        if (_objectsById.Count > 0 && _recognitionService.RegisteredObjectCount < _objectsById.Count)
        {
            SetStatusText(RecognitionMessages.ReRegistrationRequired);
        }
    }
    #endregion

    #region UI helpers
    private void SetStatusText(string message)
    {
        var target = registrationStatusText != null ? registrationStatusText : resultText;
        if (target != null)
        {
            target.text = message;
        }
        else
        {
            Debug.Log(message);
        }
    }

    private void ShowRecognitionUnavailable(string message)
    {
        _recognitionAvailable = false;
        SetStatusText(message);
    }

    private void SetRegisterButtonInteractable(bool interactable)
    {
        if (registerStartButton != null)
        {
            registerStartButton.interactable = interactable;
        }
    }

    private void DestroySnapshot()
    {
        if (tempSnapshot == null)
        {
            return;
        }

        Destroy(tempSnapshot);
        tempSnapshot = null;
    }
    #endregion
}
