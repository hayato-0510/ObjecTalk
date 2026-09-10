using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Presentation.Talk;
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
public class EmbeddingObjectMatcher : MonoBehaviour, IRecognitionObservationSource
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

    public string CurrentUserId => IntegratedAuthManager.Instance?.CurrentUser?.user_id;

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

    [Tooltip("登録モードの読み上げ枠。RegisterCameraPanel の中に置く。撮影進捗（n / 3 枚目、撮影完了）に加え、" +
             "登録中に出るエラーもここに表示する。Talk モードのメッセージは resultText 側に出る。")]
    public TMP_Text registrationStatusText;

    [Tooltip("撮影進捗のドット3個。左から撮影済みを点灯する。")]
    public Image[] registrationDots;

    [Tooltip("撮影済みの写真を表示するサムネイル3枚（左から1枚目→3枚目）。撮影が受理されるたびに該当枠へ表示する。" +
             "表示のみで、タップでの再撮影はできない。")]
    public Image[] registrationThumbnails;

    [Tooltip("撮影の狙いを説明するテキスト。")]
    public TMP_Text registrationInstructionText;

    [Range(0.3f, 1f)]
    [Tooltip("中央クロップの唯一の基準値（短辺に対する割合）。embedding が実際に切り出す箱と、各カメラ面の" +
             "ガイド枠／緑マスクの両方をこの値が駆動する。ImageGeometry.DefaultCropFraction に合わせる。")]
    public float guideFractionOfPreview = ImageGeometry.DefaultCropFraction;

    [Range(0.3f, 1f)]
    [Tooltip("背景抑制マスクの半径（クロップ枠の内接円に対する割合）。この内側だけを埋め込み・AKAZE照合に渡し、" +
             "外側は単色でつぶす。登録時と認識時に同じ値を使うことで、背景の写り込みによる誤認識/未認識を減らす。")]
    public float backgroundMaskFraction = ImageGeometry.DefaultMaskFraction;

    [Tooltip("登録カメラプレビューの切り出しガイド（枠＋緑マスク）。CameraDisplay(reg) の子。")]
    public CropGuideOverlay cropOverlayRegister;

    [Tooltip("配置スナップショットの切り出しガイド（枠＋緑マスク）。SnapshotDisplay の子。")]
    public CropGuideOverlay cropOverlayPlacement;

    [Tooltip("Talk カメラプレビューの切り出しガイド（枠＋緑マスク）。CameraDisplay(talk) の子。")]
    public CropGuideOverlay cropOverlayTalk;

    [Tooltip("撮影が受理された瞬間に一瞬光る全画面の白オーバーレイ。Canvas 直下・raycastTarget off・初期 alpha 0。")]
    public Image captureFlashOverlay;

    [Range(0.05f, 0.5f)]
    [Tooltip("撮影フラッシュのフェード時間（秒）。")]
    public float captureFlashDuration = 0.15f;

    [Header("Recognition Mode UI (TalkCameraPanel)")]
    public Image recognitionEyeImage;
    public Image recognitionMouthImage;

    [Range(0.05f, 0.5f)]
    [Tooltip("認識中の目オーバーレイの大きさ（プレビュー短辺に対する割合）。")]
    public float overlayEyeFraction = 0.18f;

    [Range(0.05f, 0.5f)]
    [Tooltip("認識中の口オーバーレイの大きさ（プレビュー短辺に対する割合）。")]
    public float overlayMouthFraction = 0.22f;

    [Range(0f, 0.5f)]
    [Tooltip("顔オーバーレイが物体を追いかける滑らかさ（秒）。認識は毎秒2〜5回しか位置を出さないので、その間を補間する。0でスナップ。")]
    public float faceTrackingTimeConstant = 0.12f;

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
    // Captured synchronously when the shutter is pressed, before the async embedding run confirms
    // whether the view was actually accepted. Promoted into tempSnapshot / a thumbnail slot on
    // CaptureAccepted, or destroyed on any other outcome (Featureless/Unavailable/NotAccepting).
    private Texture2D _pendingThumbnail;
    // One thumbnail per required view, aligned by index (view 1 at [0], etc.). Slot 0 shares its
    // texture with tempSnapshot rather than duplicating it.
    private readonly Texture2D[] _registrationSnapshots = new Texture2D[RecognitionSettings.RequiredSamplesPerObject];
    private int currentEyeId = -1;
    private int currentMouthId = -1;
    private Vector2 tempRelativeEyePos = new Vector2(-1, -1);
    private Vector2 tempRelativeMouthPos = new Vector2(-1, -1);
    private WebCamTexture webcamTexture;
    private string _registrationObjectId;
    private bool _cameraAuthorized;

    // --- Upright camera frame ---
    // Recognition, registration capture, the placement snapshot, and both preview RawImages all
    // read _orientedFrame so the AI and the user see the same upright, un-mirrored image. It is
    // rebuilt whenever the camera size or reported orientation changes.
    private RenderTexture _orientedFrame;
    private Material _orientBlitMaterial;
    private Mesh _orientQuad;
    private FrameOrientation _frameOrientation;
    private int _orientedSourceWidth;
    private int _orientedSourceHeight;
    // Front cameras are horizontally mirrored; back cameras are not. This tracks the active device
    // so the oriented frame flips to match, the same rule as Webcamera.cs (localScale.x = -1 when
    // device.isFrontFacing). WebCamTexture.videoVerticallyMirrored is a different signal and is not
    // used here.
    private bool _cameraIsFrontFacing;
    private bool _warnedNoBlitMaterial;
    // Set when the render texture cannot be created for the current camera size/orientation, so the
    // rebuild is not re-attempted (and re-logged) every frame; cleared when the source changes and
    // whenever the panel or the application comes back to the foreground, so a transient failure
    // does not degrade the rest of the process's life.
    private bool _orientedFrameUnavailable;

    // One-shot so an unwired registration readout does not log on every capture.
    private bool _warnedNoStatusText;

    // Running capture-flash fade; see FlashCapture.
    private Coroutine _flashRoutine;

    // --- Debug: wipe database ---
    // A runtime-built button (no scene wiring, matching this scene's all-code-bound convention and
    // TalkController.CreateRuntimePushToTalkButton). Tapping it swaps the button for an inline
    // "delete everything?" confirm; confirming clears every table via DatabaseManager.ClearAllTables()
    // and returns to the home scene so auth and the UI rebuild from an empty database. Update()
    // rebuilds it lazily, so it survives this DontDestroyOnLoad singleton outliving its scene canvas.
    private GameObject _debugWipeRoot;
    private GameObject _debugWipeButtonGo;
    private GameObject _debugSeedButtonGo;
    private GameObject _debugWipeConfirmGo;
    private bool _debugWipeInProgress;

    /// <summary>
    /// The "fin" button: ends the current visitor's session and reloads ObjectDetectionScene fresh,
    /// so the next visitor starts from a clean scan-ready state without going back through the home
    /// screen. A full scene reload (rather than just re-showing the selection panel) also drops all
    /// recognition / conversation state.
    /// </summary>
    public void ReturnToHome()
    {
        Debug.Log("fin: reloading ObjectDetectionScene for the next session...");

        // Deactivating the root panel runs OnDisable, which stops the webcam.
        if (objectMatcherRootPanel != null)
        {
            objectMatcherRootPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("objectMatcherRootPanel is not set!");
        }

        SceneManager.LoadScene("ObjectDetectionScene");
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

        // guideFractionOfPreview is the single source of truth for the centre crop: the same value
        // drives the on-preview guide frame and the box the embedding actually keeps, so nudging the
        // Inspector field cannot silently desync them. The [Range] attribute keeps it inside the
        // (0, 1] the preprocessor requires.
        _preprocessor = new CameraFramePreprocessor(
            ImageGeometry.EmbeddingImageSize,
            ImageGeometry.MaximumVerificationImageSize,
            guideFractionOfPreview,
            backgroundMaskFraction);
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
        // A failed RenderTexture.Create() is usually transient (a memory spike, a lost device), so
        // give the oriented frame a fresh attempt every time the panel comes back rather than
        // leaving the session on the raw camera texture for good.
        _orientedFrameUnavailable = false;

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

            var device = WebCamTexture.devices[0];
            _cameraIsFrontFacing = device.isFrontFacing;
            webcamTexture = new WebCamTexture(device.name, 1280, 720, 30);
        }

        // --- Load latest data from DB ---
        Debug.Log("OnEnable: Loading database...");
        LoadRegisteredObjects();
        // --- End Load ---

        // 1. Start the camera if it's not already playing
        if (!webcamTexture.isPlaying)
        {
            Debug.Log("OnEnable: Starting Webcam...");
            webcamTexture.Play();
        }

        // 2. Point both previews at the active camera frame. Until the camera delivers a real
        //    frame and EnsureOrientedFrame() builds _orientedFrame, ActiveCameraFrame() returns
        //    the raw texture; Update() re-applies the oriented frame once it exists.
        ApplyCameraDisplay(registerCameraDisplay);
        ApplyCameraDisplay(talkCameraDisplay);

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
            HideFaceOverlay();
            ShowOverlay(cropOverlayTalk);
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
        UpdateRecognitionState(false, null, "", 0f);
        HideAllOverlays();
    }

    /// <summary>Releases camera and inference work while the application is backgrounded.</summary>
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            if (webcamTexture != null && webcamTexture.isPlaying) webcamTexture.Stop();
            _recognitionService?.Pause();
            UpdateRecognitionState(false, null, "", 0f);
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
        // Same as EnablePanelRoutine: retry the oriented frame now the app is in the foreground.
        _orientedFrameUnavailable = false;

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

    #region Upright camera frame

    /// <summary>
    /// The frame recognition, registration, and the preview all read: <see cref="_orientedFrame"/>
    /// once it is built for the current camera size/orientation; the raw texture until then (or on
    /// fallback).
    /// </summary>
    private Texture ActiveCameraFrame()
    {
        return _orientedFrame != null ? (Texture)_orientedFrame : webcamTexture;
    }

    /// <summary>
    /// Builds (or rebuilds) <see cref="_orientedFrame"/> for the camera's current size and reported
    /// orientation. A no-op until the camera delivers a real frame; on a failure to create the
    /// render texture it warns and leaves the previews on the raw texture.
    /// </summary>
    private void EnsureOrientedFrame()
    {
        if (webcamTexture == null || webcamTexture.width <= 16 || webcamTexture.height <= 16)
        {
            return; // camera has not delivered a real frame yet
        }

        var orientation = FrameOrientation.FromWebCam(
            (int)webcamTexture.videoRotationAngle, _cameraIsFrontFacing);

        var sourceUnchanged = _orientedSourceWidth == webcamTexture.width
            && _orientedSourceHeight == webcamTexture.height
            && _frameOrientation.Equals(orientation);

        if (!sourceUnchanged)
        {
            // One line per orientation change: enough to diagnose an upside-down or mirrored
            // preview on a device from logcat without a rebuild.
            Debug.Log($"EmbeddingObjectMatcher camera frame: {webcamTexture.width}x{webcamTexture.height} "
                + $"videoRotationAngle={webcamTexture.videoRotationAngle} "
                + $"videoVerticallyMirrored={webcamTexture.videoVerticallyMirrored} "
                + $"isFrontFacing={_cameraIsFrontFacing} -> {orientation} | "
                + $"graphicsUVStartsAtTop={SystemInfo.graphicsUVStartsAtTop} "
                + $"device={SystemInfo.graphicsDeviceType}");
        }

        // Nothing to do while the source is unchanged and we already have a frame (or have already
        // given up on this source). Re-attempting a failed Create() every frame would leak GPU
        // memory and spam the log.
        if (sourceUnchanged && (_orientedFrame != null || _orientedFrameUnavailable))
        {
            return;
        }

        ReleaseOrientedFrame();
        _orientedFrameUnavailable = false;
        _frameOrientation = orientation;
        _orientedSourceWidth = webcamTexture.width;
        _orientedSourceHeight = webcamTexture.height;
        var (w, h) = orientation.OrientedSize(webcamTexture.width, webcamTexture.height);

        try
        {
            _orientedFrame = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "ObjecTalk Oriented Camera Frame",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _orientedFrame.Create();
        }
        catch (Exception e)
        {
            Debug.LogWarning("EmbeddingObjectMatcher: could not create the oriented camera frame "
                + $"({e.GetType().Name}: {e.Message}); recognition and preview fall back to the raw "
                + "camera texture.");
            ReleaseOrientedFrame();
            _orientedFrameUnavailable = true;
            ApplyCameraDisplay(registerCameraDisplay);
            ApplyCameraDisplay(talkCameraDisplay);
            return;
        }

        if (!_orientedFrame.IsCreated())
        {
            Debug.LogWarning("EmbeddingObjectMatcher: could not create the oriented camera frame; "
                + "recognition and preview fall back to the raw camera texture.");
            ReleaseOrientedFrame();
            _orientedFrameUnavailable = true;
            ApplyCameraDisplay(registerCameraDisplay);
            ApplyCameraDisplay(talkCameraDisplay);
            return;
        }

        if (_orientBlitMaterial == null)
        {
            // Unlit/Texture is in Always Included Shaders (ProjectSettings/GraphicsSettings.asset),
            // which is what makes Shader.Find resolve it in a player build; the rest of the chain is
            // defence in depth. Shader.Find returns a genuine null on miss, so ?? is safe here.
            var shader = Shader.Find("Unlit/Texture")
                ?? Shader.Find("Hidden/BlitCopy")
                ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _orientBlitMaterial = new Material(shader);
            }
        }
        if (_orientQuad == null)
        {
            _orientQuad = BuildUnitQuad();
        }

        // UVs are constant per orientation, so upload them once here rather than every frame.
        ApplyOrientedQuadUVs();

        ApplyCameraDisplay(registerCameraDisplay);
        ApplyCameraDisplay(talkCameraDisplay);
    }

    /// <summary>
    /// Draws the raw <see cref="webcamTexture"/> into <see cref="_orientedFrame"/> the right way up.
    /// Uses a plain identity blit when the source is already upright, or when no blit material could
    /// be created (a warning is logged once) so recognition never NullRefs.
    /// </summary>
    private void RenderOrientedFrame()
    {
        if (_orientedFrame == null || webcamTexture == null)
        {
            return;
        }

        if (_frameOrientation.IsIdentity || _orientBlitMaterial == null || _orientQuad == null)
        {
            if (!_frameOrientation.IsIdentity && _orientBlitMaterial == null && !_warnedNoBlitMaterial)
            {
                Debug.LogWarning("EmbeddingObjectMatcher: no blit material for the oriented camera "
                    + "frame; preview and recognition use the raw camera orientation.");
                _warnedNoBlitMaterial = true;
            }
            Graphics.Blit(webcamTexture, _orientedFrame);
            return;
        }

        var previous = RenderTexture.active;
        RenderTexture.active = _orientedFrame;
        GL.PushMatrix();
        try
        {
            GL.LoadOrtho();
            _orientBlitMaterial.mainTexture = webcamTexture;
            _orientBlitMaterial.SetPass(0);
            Graphics.DrawMeshNow(_orientQuad, Vector3.zero, Quaternion.identity);
        }
        finally
        {
            GL.PopMatrix();
            RenderTexture.active = previous;
        }
    }

    /// <summary>Uploads the source-sample UVs for the current orientation onto the blit quad.</summary>
    private void ApplyOrientedQuadUVs()
    {
        if (_orientQuad == null)
        {
            return;
        }

        var uv = _frameOrientation.SourceUVsForOrientedCorners(); // (float u, float v)[]
        var uvList = new List<Vector2>(4);
        foreach (var corner in uv) uvList.Add(new Vector2(corner.u, corner.v));
        _orientQuad.SetUVs(0, uvList);
        _orientQuad.UploadMeshData(false);
    }

    // Vertices are the unit square in BL, BR, TR, TL order; triangles are wound clockwise so the
    // quad is front-facing under GL.LoadOrtho() and survives a Cull Back blit shader.
    private static Mesh BuildUnitQuad()
    {
        return new Mesh
        {
            vertices = new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0),
                new Vector3(1, 1, 0), new Vector3(0, 1, 0),
            },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
        };
    }

    private void ApplyCameraDisplay(RawImage display)
    {
        if (display == null)
        {
            return;
        }

        display.texture = ActiveCameraFrame();
        display.color = Color.white;

        var fitter = display.GetComponent<AspectRatioFitter>();
        if (fitter != null && _orientedFrame != null && _orientedFrame.height > 0)
        {
            fitter.aspectRatio = _orientedFrame.width / (float)_orientedFrame.height;

            // The fitter has just resized the preview, so a guide square fitted against the
            // pre-layout rect at panel activation is now stale.
            var overlay = display == registerCameraDisplay ? cropOverlayRegister
                : display == talkCameraDisplay ? cropOverlayTalk
                : null;
            overlay?.Refresh();
        }
    }

    private void ReleaseOrientedFrame()
    {
        if (_orientedFrame != null)
        {
            _orientedFrame.Release();
            Destroy(_orientedFrame);
            _orientedFrame = null;
        }
    }

    #endregion

    void Update()
    {
        EnsureDebugWipeButton();

        // Keep the upright frame current for the preview even when recognition is unavailable.
        if (webcamTexture != null && webcamTexture.didUpdateThisFrame)
        {
            EnsureOrientedFrame();
            RenderOrientedFrame();
        }

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
            _recognitionService.StartRecognition(ActiveCameraFrame(), Time.timeAsDouble);
        }

        // One bounded slice of work per frame, whichever kind of run is in flight.
        var result = _recognitionService.Tick(Time.timeAsDouble);
        HandleTickResult(result);
        AdvanceFaceOverlayTracking();
    }

    private void HandleTickResult(RecognitionTickResult result)
    {
        switch (result.Status)
        {
            case RecognitionTickStatus.Recognized:
                // Set before the state change, so an object recognised this tick is placed at the
                // spot it was actually found rather than gliding in from the centre.
                _faceTrackedTarget = result.HasLocation
                    ? new Vector2(result.RelativeX, result.RelativeY)
                    : FrameCentre;
                if (_objectsById.TryGetValue(result.ObjectId, out var recognized))
                {
                    UpdateRecognitionState(true, recognized, result.ObjectId, result.Score);
                }
                break;

            case RecognitionTickStatus.NotRecognized:
                UpdateRecognitionState(false, null, "", 0f);
                break;

            case RecognitionTickStatus.CaptureAccepted:
                FlashCapture();
                PromoteCapturedThumbnail(result.CapturedViewCount);
                SetRegistrationProgress(result.CapturedViewCount);
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

    // --- Debug / exhibition ops: visitor wipe + seed marking --------------------------------------
    // Builds two always-visible runtime buttons on the recognition panel:
    //   "来場者データ消去" — two-step confirm, then DatabaseManager.ClearVisitorRegistrations
    //     (drops non-seed objects + their samples; keeps seed objects and the users row), then
    //     refreshes recognition in place.
    //   "今の登録をシード化" — DatabaseManager.MarkAllObjectsAsSeed, for exhibition setup: register
    //     the 5-department showcase objects, then tap once so they survive the visitor wipe.
    // Everything is created in code so there is no scene edit or Inspector wiring, matching this
    // scene's convention and TalkController's runtime button.

    private void EnsureDebugWipeButton()
    {
        if (_debugWipeRoot != null || _debugWipeInProgress || objectMatcherRootPanel == null)
        {
            return;
        }

        _debugWipeRoot = new GameObject("DebugWipeDatabase", typeof(RectTransform));
        var rootRect = (RectTransform)_debugWipeRoot.transform;
        rootRect.SetParent(objectMatcherRootPanel.transform, false);
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(24f, -24f);
        rootRect.sizeDelta = new Vector2(560f, 240f);
        rootRect.SetAsLastSibling();

        _debugWipeButtonGo = CreateDebugWipeButton(
            rootRect, "来場者データ消去", new Color(0.75f, 0.15f, 0.15f, 0.92f),
            new Vector2(320f, 88f), new Vector2(0f, 0f), OnDebugWipeRequested);

        // Exhibition setup: after registering the 5-department showcase objects, tap this once so
        // every current registration is marked seed and survives "来場者データ消去".
        _debugSeedButtonGo = CreateDebugWipeButton(
            rootRect, "今の登録をシード化", new Color(0.15f, 0.35f, 0.55f, 0.92f),
            new Vector2(320f, 88f), new Vector2(0f, -100f), OnSeedAllRequested);

        _debugWipeConfirmGo = new GameObject("Confirm", typeof(RectTransform));
        var confirmRect = (RectTransform)_debugWipeConfirmGo.transform;
        confirmRect.SetParent(rootRect, false);
        confirmRect.anchorMin = Vector2.zero;
        confirmRect.anchorMax = Vector2.one;
        confirmRect.offsetMin = Vector2.zero;
        confirmRect.offsetMax = Vector2.zero;

        CreateDebugWipeLabel(confirmRect, "来場者が登録した物体を消します（シード物体は残します）。よろしいですか？",
            new Vector2(560f, 44f), new Vector2(0f, 0f));
        CreateDebugWipeButton(confirmRect, "削除する", new Color(0.75f, 0.15f, 0.15f, 0.95f),
            new Vector2(270f, 84f), new Vector2(0f, -50f), ConfirmDebugWipe);
        CreateDebugWipeButton(confirmRect, "やめる", new Color(0.30f, 0.30f, 0.32f, 0.95f),
            new Vector2(270f, 84f), new Vector2(290f, -50f), CancelDebugWipe);

        _debugWipeConfirmGo.SetActive(false);
    }

    private static GameObject CreateDebugWipeButton(
        Transform parent, string label, Color background, Vector2 size, Vector2 anchoredPos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPos;

        var image = go.GetComponent<Image>();
        image.color = background;
        image.raycastTarget = true;

        go.GetComponent<Button>().onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);
        try
        {
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 16f;
            tmp.fontSizeMax = 34f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }
        catch (Exception)
        {
            // A missing TMP font must not stop the button working; it just has no label.
        }

        return go;
    }

    private static void CreateDebugWipeLabel(Transform parent, string text, Vector2 size, Vector2 anchoredPos)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPos;
        try
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 14f;
            tmp.fontSizeMax = 26f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }
        catch (Exception)
        {
            // No font: the buttons still carry enough meaning on their own.
        }
    }

    private void OnDebugWipeRequested()
    {
        if (_debugWipeButtonGo != null) _debugWipeButtonGo.SetActive(false);
        if (_debugSeedButtonGo != null) _debugSeedButtonGo.SetActive(false);
        if (_debugWipeConfirmGo != null) _debugWipeConfirmGo.SetActive(true);
    }

    private void CancelDebugWipe()
    {
        if (_debugWipeConfirmGo != null) _debugWipeConfirmGo.SetActive(false);
        if (_debugWipeButtonGo != null) _debugWipeButtonGo.SetActive(true);
        if (_debugSeedButtonGo != null) _debugSeedButtonGo.SetActive(true);
    }

    private void OnSeedAllRequested()
    {
        if (dbManager == null)
        {
            SetStatusText("DBに接続できていないためシード化できません。");
            return;
        }

        try
        {
            dbManager.MarkAllObjectsAsSeed();
        }
        catch (Exception e)
        {
            SetStatusText($"シード化に失敗しました: {e.Message}");
            Debug.LogError($"Seed-all failed: {e}");
            return;
        }

        LoadRegisteredObjects();
        SetStatusText("今の登録をすべてシード（削除対象外）にしました。");
    }

    private void ConfirmDebugWipe()
    {
        if (_debugWipeInProgress)
        {
            return;
        }

        if (dbManager == null)
        {
            CancelDebugWipe();
            SetStatusText("DBに接続できていないため消去できません。");
            return;
        }

        _debugWipeInProgress = true;
        try
        {
            dbManager.ClearVisitorRegistrations();
        }
        catch (Exception e)
        {
            _debugWipeInProgress = false;
            CancelDebugWipe();
            SetStatusText($"来場者データの消去に失敗しました: {e.Message}");
            Debug.LogError($"Visitor wipe failed: {e}");
            return;
        }

        Debug.Log("Visitor wipe: non-seed objects and samples cleared; refreshing recognition in place.");
        LoadRegisteredObjects();
        SetStatusText("来場者データを消去しました。シード物体は残っています。");
        _debugWipeInProgress = false;
        CancelDebugWipe();
    }

    void OnDestroy()
    {
        if (_debugWipeRoot != null)
        {
            Destroy(_debugWipeRoot);
            _debugWipeRoot = null;
        }

        DisposeRecognitionPipeline();

        if (webcamTexture != null)
        {
            if (webcamTexture.isPlaying) webcamTexture.Stop();
        }

        ReleaseOrientedFrame();
        if (_orientBlitMaterial != null)
        {
            Destroy(_orientBlitMaterial);
            _orientBlitMaterial = null;
        }
        if (_orientQuad != null)
        {
            Destroy(_orientQuad);
            _orientQuad = null;
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
            UpdateRecognitionState(false, null, "", 0f, false);
            return;
        }

        if (_objectsById.TryGetValue(change.ObjectId, out var registered))
        {
            UpdateRecognitionState(true, registered, change.ObjectId, change.Score, false);
        }
    }

    /// <summary>
    /// 物体認識状態を更新し、変化があればイベントを発火します。認識中は登録済みの目・口を
    /// Talk プレビューに重ねます（<paramref name="recognized"/> が null なら非表示）。
    /// </summary>
    private void UpdateRecognitionState(
        bool isRecognized,
        RegisteredObject recognized,
        string objectId,
        float confidence,
        bool publishObservation = true)
    {
        int newState = isRecognized ? 1 : 0;
        string newName = isRecognized ? (recognized != null ? recognized.Name : "") : "";
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

            if (isRecognized && recognized != null)
            {
                ShowFaceOverlay(recognized);
                Debug.Log($"★ 物体認識: ON (1) - 物体名: {newName}");
            }
            else
            {
                HideFaceOverlay();
                Debug.Log($"★ 物体認識: OFF (0)");
            }
        }

        void ApplyLegacyRecognitionState()
        {
            IsObjectRecognized = newState;
            LastRecognizedObjectName = newName;
        }
    }

    /// <summary>
    /// Places the registered eye / mouth sprites over the recognition preview at their stored
    /// relative positions. There is no bounding-box tracking, so the pieces sit at a fixed spot on
    /// the preview — correct as long as the object fills the guide square it was captured in.
    /// </summary>
    // Objects whose registration skipped the eye/mouth placement step store (-1, -1) / style -1.
    // Rather than showing no face at all, fall back to a centred default so every recognised object
    // gets one (matches the placement a fully-registered object lands near).
    private static readonly Vector2 DefaultRelativeEyePosition = new Vector2(0.5f, 0.54f);
    private static readonly Vector2 DefaultRelativeMouthPosition = new Vector2(0.5f, 0.46f);

    /// <summary>Where a registration capture puts its object, and so the "no tracking" position.</summary>
    private static readonly Vector2 FrameCentre = new Vector2(0.5f, 0.5f);

    // The object the overlay currently belongs to, where recognition last saw it, and where the
    // overlay has eased to. Recognition only reports a position a few times a second, so the
    // target and the drawn position are kept apart and bridged in Update.
    private RegisteredObject _faceOverlayObject;
    private Vector2 _faceTrackedTarget = FrameCentre;
    private Vector2 _faceTrackedCurrent = FrameCentre;
    private Vector2 _facePlacedAt = FrameCentre;

    private void ShowFaceOverlay(RegisteredObject obj)
    {
        if (DesiredMode != StartupMode.Talk || talkCameraDisplay == null)
        {
            Debug.LogWarning($"[FaceDiag] ShowFaceOverlay early-out: DesiredMode={DesiredMode}, "
                + $"talkCameraDisplay={(talkCameraDisplay == null ? "NULL" : "ok")}, "
                + $"eyeImg={(recognitionEyeImage == null ? "NULL" : "ok")}, "
                + $"mouthImg={(recognitionMouthImage == null ? "NULL" : "ok")}, "
                + $"eyeStyles={(eyeStyles == null ? "NULL" : eyeStyles.Count.ToString())}, "
                + $"mouthStyles={(mouthStyles == null ? "NULL" : mouthStyles.Count.ToString())}.");
            HideFaceOverlay();
            return;
        }

        _faceOverlayObject = obj;

        // Snap to wherever recognition just found the object; easing only applies to later moves.
        _faceTrackedCurrent = _faceTrackedTarget;

        Debug.Log($"[FaceDiag] ShowFaceOverlay: eyePos={obj.RelativeEyePosition} "
            + $"(placed={IsPlaced(obj.RelativeEyePosition)}) mouthPos={obj.RelativeMouthPosition} "
            + $"tracked={_faceTrackedTarget} "
            + $"rect={talkCameraDisplay.rectTransform.rect.width}x{talkCameraDisplay.rectTransform.rect.height} "
            + $"eyeStyles={(eyeStyles == null ? "NULL" : eyeStyles.Count.ToString())} "
            + $"mouthStyles={(mouthStyles == null ? "NULL" : mouthStyles.Count.ToString())}.");

        PlaceFaceOverlay(reportProblems: true);
    }

    /// <summary>
    /// Eases the overlay toward the position recognition last reported. Recognition runs at two to
    /// five hertz, so without this the face would step rather than follow.
    /// </summary>
    private void AdvanceFaceOverlayTracking()
    {
        if (_faceOverlayObject == null)
        {
            return;
        }

        _faceTrackedCurrent = new Vector2(
            FaceOverlayTracking.Smooth(
                _faceTrackedCurrent.x, _faceTrackedTarget.x, Time.deltaTime, faceTrackingTimeConstant),
            FaceOverlayTracking.Smooth(
                _faceTrackedCurrent.y, _faceTrackedTarget.y, Time.deltaTime, faceTrackingTimeConstant));

        // A still object must not rewrite its RectTransforms every frame.
        if ((_faceTrackedCurrent - _facePlacedAt).sqrMagnitude < 1e-8f)
        {
            return;
        }

        PlaceFaceOverlay(reportProblems: false);
    }

    /// <summary>
    /// Draws both pieces at their registered spot shifted by the object's offset from the centre.
    /// <paramref name="reportProblems"/> is false on the per-frame path so a misconfigured style
    /// cannot fill the console.
    /// </summary>
    private void PlaceFaceOverlay(bool reportProblems)
    {
        var obj = _faceOverlayObject;
        if (obj == null || talkCameraDisplay == null)
        {
            return;
        }

        var previewRect = talkCameraDisplay.rectTransform.rect;

        Vector2 eyePos = IsPlaced(obj.RelativeEyePosition) ? obj.RelativeEyePosition : DefaultRelativeEyePosition;
        Vector2 mouthPos = IsPlaced(obj.RelativeMouthPosition) ? obj.RelativeMouthPosition : DefaultRelativeMouthPosition;
        int eyeStyle = obj.EyeStyleId >= 0 ? obj.EyeStyleId : 0;
        int mouthStyle = obj.MouthStyleId >= 0 ? obj.MouthStyleId : 0;

        PlaceOverlayPiece(
            recognitionEyeImage, eyeStyles, eyeStyle, Tracked(eyePos), overlayEyeFraction,
            previewRect, reportProblems);
        PlaceOverlayPiece(
            recognitionMouthImage, mouthStyles, mouthStyle, Tracked(mouthPos), overlayMouthFraction,
            previewRect, reportProblems);

        _facePlacedAt = _faceTrackedCurrent;
    }

    /// <summary>
    /// Shifts one registered piece by the tracked offset. Clamped because a piece outside the
    /// preview would otherwise trip the "never placed" sentinel and hide itself.
    /// </summary>
    private Vector2 Tracked(Vector2 registered)
    {
        var (x, y) = FaceOverlayTracking.TrackedPosition(
            registered.x, registered.y, _faceTrackedCurrent.x, _faceTrackedCurrent.y);

        return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
    }

    private static bool IsPlaced(Vector2 relative) => relative.x >= 0f && relative.y >= 0f;

    private static void PlaceOverlayPiece(Image image, List<Sprite> styles, int styleId,
        Vector2 relative, float fraction, Rect previewRect, bool reportProblems)
    {
        if (image == null)
        {
            return;
        }

        bool placeable = styleId >= 0 && styles != null && styleId < styles.Count
                         && styles[styleId] != null
                         && relative.x >= 0f && relative.y >= 0f
                         && previewRect.width > 0f && previewRect.height > 0f;
        if (!placeable)
        {
            if (reportProblems)
            {
                Debug.LogWarning($"[FaceDiag] PlaceOverlayPiece not placeable: styleId={styleId}, "
                    + $"styles={(styles == null ? "NULL" : styles.Count.ToString())}, "
                    + $"styleOk={(styles != null && styleId >= 0 && styleId < styles.Count && styles[styleId] != null)}, "
                    + $"relative={relative}, rect={previewRect.width}x{previewRect.height}.");
            }

            image.gameObject.SetActive(false);
            return;
        }

        image.sprite = styles[styleId];

        var (x, y) = FaceOverlayLayout.AnchoredPosition(
            relative.x, relative.y, previewRect.width, previewRect.height);

        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);

        float side = Mathf.Min(previewRect.width, previewRect.height) * fraction;
        rt.sizeDelta = new Vector2(side, side);

        image.gameObject.SetActive(true);
    }

    private void HideFaceOverlay()
    {
        _faceOverlayObject = null;
        _faceTrackedTarget = FrameCentre;
        _faceTrackedCurrent = FrameCentre;
        _facePlacedAt = FrameCentre;
        if (recognitionEyeImage != null) recognitionEyeImage.gameObject.SetActive(false);
        if (recognitionMouthImage != null) recognitionMouthImage.gameObject.SetActive(false);
    }

    #region Mode Switching
    private void SwitchToNamingMode()
    {
        currentMode = AppMode.Naming;
        if (tempSnapshot == null) { Debug.LogError("Snapshot is missing!"); return; }

        // The naming screen only confirms the object, so show the centre square that was actually
        // registered rather than the full frame (which would spill past the square photo frame).
        var crop = ImageGeometry.CenterCrop(tempSnapshot.width, tempSnapshot.height, guideFractionOfPreview);
        var sprite = Sprite.Create(
            tempSnapshot,
            new UnityEngine.Rect(crop.X, crop.Y, crop.Width, crop.Height),
            new Vector2(0.5f, 0.5f));
        if (snapshotDisplay_Naming != null) snapshotDisplay_Naming.sprite = sprite;
        else Debug.LogError("SnapshotDisplay_Naming is not assigned!");
        FitGraphicAspect(snapshotDisplay_Naming, crop.Width / (float)crop.Height);

        if (namingPanel != null) namingPanel.SetActive(true);
        if (placementPanel != null) placementPanel.SetActive(false);
        cropOverlayPlacement?.Hide();

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
        FitSnapshotAspect(snapshotDisplay_Placement, snapshot);

        if (mouthToggle != null) mouthToggle.isOn = false;
        if (eyeToggle != null) eyeToggle.isOn = true; // Default to eye styles
        PopulateStyleScrollView(true); // Populate eye styles

        // Ensure parts are initially hidden
        if (placementEyeImage != null) placementEyeImage.gameObject.SetActive(false);
        if (placementMouthImage != null) placementMouthImage.gameObject.SetActive(false);
        // Reset IDs
        currentEyeId = -1;
        currentMouthId = -1;
        ShowOverlay(cropOverlayPlacement);
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
        SetRegistrationProgress(0);
        ShowOverlay(cropOverlayRegister);
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

        // Captured up front for every attempt (not just the first) so a thumbnail is available
        // whichever view this turns out to be. It must match the upright frame the recognition
        // sees, so it reads from ActiveCameraFrame(). Only promoted into tempSnapshot / a thumbnail
        // slot once CaptureAccepted confirms the view below; discarded otherwise.
        DestroyThumbnail(ref _pendingThumbnail);
        _pendingThumbnail = CaptureActiveFrameSnapshot();
        if (_pendingThumbnail == null)
        {
            SetStatusText(RecognitionMessages.CameraUnavailable);
            return;
        }

        var status = _recognitionService.CaptureRegistrationView(ActiveCameraFrame());
        switch (status)
        {
            case RegistrationCaptureStatus.Started:
                // Block the button until the run finishes so captures cannot queue up.
                SetRegisterButtonInteractable(false);
                break;

            case RegistrationCaptureStatus.Featureless:
                DestroyThumbnail(ref _pendingThumbnail);
                SetStatusText(RecognitionMessages.RegistrationRetry);
                break;

            case RegistrationCaptureStatus.Unavailable:
                DestroyThumbnail(ref _pendingThumbnail);
                SetStatusText(RecognitionMessages.RecognitionUnavailable);
                break;

            case RegistrationCaptureStatus.NotAccepting:
                DestroyThumbnail(ref _pendingThumbnail);
                EnterPlacementFromCaptures();
                break;
        }
    }

    /// <summary>
    /// Reads <see cref="ActiveCameraFrame"/> into a brand-new RGB24 <see cref="Texture2D"/>, or null
    /// if the graphics device could not service the read this frame. The caller owns the result.
    /// </summary>
    private Texture2D CaptureActiveFrameSnapshot()
    {
        try
        {
            var active = ActiveCameraFrame();
            var readable = RenderTexture.GetTemporary(active.width, active.height, 0, RenderTextureFormat.ARGB32);
            var previousActive = RenderTexture.active;
            Texture2D snapshot;
            try
            {
                Graphics.Blit(active, readable);
                RenderTexture.active = readable;
                snapshot = new Texture2D(readable.width, readable.height, TextureFormat.RGB24, false);
                snapshot.ReadPixels(new UnityEngine.Rect(0, 0, readable.width, readable.height), 0, 0);
                snapshot.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(readable);
            }
            return snapshot;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error taking snapshot: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Moves the pending snapshot into the confirmed slot for the view <see cref="Update"/> just
    /// learned was accepted: the first accepted view also becomes <see cref="tempSnapshot"/> (the
    /// naming/placement/DB image), and every accepted view fills its thumbnail slot. Ownership of
    /// the pending texture transfers here; nothing else may destroy it afterwards.
    /// </summary>
    private void PromoteCapturedThumbnail(int capturedViewCount)
    {
        var snapshot = _pendingThumbnail;
        _pendingThumbnail = null;
        if (snapshot == null)
        {
            Debug.LogWarning("A capture was accepted but no pending snapshot was kept for its thumbnail.");
            return;
        }

        if (tempSnapshot == null)
        {
            tempSnapshot = snapshot;
        }

        var index = capturedViewCount - 1;
        if (index < 0 || index >= _registrationSnapshots.Length)
        {
            return;
        }

        _registrationSnapshots[index] = snapshot;
        ShowRegistrationThumbnail(index, snapshot);
    }

    private void EnterPlacementFromCaptures()
    {
        SetRegisterButtonInteractable(true);
        cropOverlayRegister?.Hide();
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
            HideAllOverlays();

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
            ShowOverlay(cropOverlayPlacement);
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
            cropOverlayPlacement?.Hide();

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
            SetRegistrationProgress(_recognitionService?.CapturedViewCount ?? 0);
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
    /// <summary>
    /// Status, error, and recognition messages. The two readouts sit on different panels —
    /// <see cref="registrationStatusText"/> under RegisterCameraPanel, <see cref="resultText"/> under
    /// TalkCameraPanel — and each panel hides the other's text, so a single fixed target is invisible
    /// in one of the two modes. The target is therefore chosen by which camera panel is actually
    /// active. Routing by activation rather than by <see cref="DesiredMode"/> also gets the
    /// EnablePanelRoutine permission/camera failures right: they run before the mode switch has
    /// activated either panel, so "whatever is on screen right now" is the only correct answer there.
    /// </summary>
    private void SetStatusText(string message)
    {
        var registering = registerCameraPanel != null && registerCameraPanel.activeInHierarchy;
        var target = registering ? registrationStatusText : resultText;
        if (target == null)
        {
            // The preferred readout is unwired; the other one is still better than nothing.
            target = registering ? resultText : registrationStatusText;
        }

        if (target != null)
        {
            target.text = message;
        }
        else
        {
            Debug.Log(message);
        }
    }

    /// <summary>
    /// Updates the "n / 3 枚目" readout, the angle hint, and the progress dots for the number of
    /// views captured so far. The decision is <see cref="RegistrationProgressView"/>'s; this method
    /// only applies it. Every reference is optional; a missing one is skipped, not fatal.
    /// </summary>
    private void SetRegistrationProgress(int capturedViews)
    {
        var view = RegistrationProgressView.For(capturedViews, RecognitionSettings.RequiredSamplesPerObject);

        if (registrationStatusText != null)
        {
            registrationStatusText.text = view.Status;
        }
        else if (!_warnedNoStatusText)
        {
            Debug.LogWarning("registrationStatusText is not set; registration progress is not shown.");
            _warnedNoStatusText = true;
        }

        if (registrationInstructionText != null)
        {
            registrationInstructionText.text = view.Instruction;
        }

        if (registrationDots != null)
        {
            for (var i = 0; i < registrationDots.Length; i++)
            {
                if (registrationDots[i] != null)
                {
                    registrationDots[i].enabled = i < view.LitDots;
                }
            }
        }
    }

    /// <summary>
    /// Shows one crop-guide overlay, first syncing its fraction to <see cref="guideFractionOfPreview"/>
    /// so the frame and green surround always match the box the embedding actually keeps.
    /// </summary>
    private void ShowOverlay(CropGuideOverlay overlay)
    {
        if (overlay == null)
        {
            return;
        }

        overlay.fraction = guideFractionOfPreview;
        overlay.Show();
    }

    private void HideAllOverlays()
    {
        cropOverlayRegister?.Hide();
        cropOverlayPlacement?.Hide();
        cropOverlayTalk?.Hide();
    }

    /// <summary>
    /// Keeps a snapshot Image from stretching by feeding its <see cref="AspectRatioFitter"/> the
    /// captured texture's aspect. Also keeps the placement eye/mouth position maths honest, since
    /// those read the snapshot rect.
    /// </summary>
    private static void FitSnapshotAspect(Graphic display, Texture2D texture)
    {
        if (texture == null || texture.height == 0)
        {
            return;
        }

        FitGraphicAspect(display, texture.width / (float)texture.height);
    }

    private static void FitGraphicAspect(Graphic display, float ratio)
    {
        if (display == null || ratio <= 0f)
        {
            return;
        }

        var fitter = display.GetComponent<AspectRatioFitter>();
        if (fitter != null)
        {
            fitter.aspectRatio = ratio;
        }
    }

    /// <summary>Flashes the full-screen white overlay once so a capture is unmistakable.</summary>
    private void FlashCapture()
    {
        if (captureFlashOverlay == null)
        {
            return;
        }

        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
        }

        _flashRoutine = StartCoroutine(FlashCaptureRoutine());
    }

    private IEnumerator FlashCaptureRoutine()
    {
        var image = captureFlashOverlay;
        image.gameObject.SetActive(true);

        var colour = image.color;
        var elapsed = 0f;
        var duration = Mathf.Max(0.01f, captureFlashDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            colour.a = Mathf.Lerp(1f, 0f, elapsed / duration);
            image.color = colour;
            yield return null;
        }

        colour.a = 0f;
        image.color = colour;
        image.gameObject.SetActive(false);
        _flashRoutine = null;
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

    /// <summary>
    /// Clears every image the current registration session holds: the pending (unconfirmed)
    /// capture, all three thumbnail slots, and <see cref="tempSnapshot"/>. Slot 0 shares its texture
    /// with tempSnapshot, so it is skipped here to avoid a double-destroy.
    /// </summary>
    private void DestroySnapshot()
    {
        DestroyThumbnail(ref _pendingThumbnail);

        for (var i = 0; i < _registrationSnapshots.Length; i++)
        {
            var snapshot = _registrationSnapshots[i];
            if (snapshot != null && snapshot != tempSnapshot)
            {
                Destroy(snapshot);
            }
            _registrationSnapshots[i] = null;
            HideRegistrationThumbnail(i);
        }

        if (tempSnapshot == null)
        {
            return;
        }

        Destroy(tempSnapshot);
        tempSnapshot = null;
    }

    private static void DestroyThumbnail(ref Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        Destroy(texture);
        texture = null;
    }

    /// <summary>
    /// Shows the crop the recognition pipeline actually kept (matching the naming-screen preview),
    /// not the raw uncropped frame, so the thumbnail reads as "what got registered".
    /// </summary>
    private void ShowRegistrationThumbnail(int index, Texture2D snapshot)
    {
        if (registrationThumbnails == null || index < 0 || index >= registrationThumbnails.Length)
        {
            return;
        }

        var image = registrationThumbnails[index];
        if (image == null || snapshot == null)
        {
            return;
        }

        var crop = ImageGeometry.CenterCrop(snapshot.width, snapshot.height, guideFractionOfPreview);
        image.sprite = Sprite.Create(
            snapshot,
            new UnityEngine.Rect(crop.X, crop.Y, crop.Width, crop.Height),
            new Vector2(0.5f, 0.5f));
        image.color = Color.white;
        FitGraphicAspect(image, crop.Width / (float)crop.Height);
        image.gameObject.SetActive(true);
    }

    private void HideRegistrationThumbnail(int index)
    {
        if (registrationThumbnails == null || index < 0 || index >= registrationThumbnails.Length)
        {
            return;
        }

        var image = registrationThumbnails[index];
        if (image == null)
        {
            return;
        }

        image.sprite = null;
        image.gameObject.SetActive(false);
    }
    #endregion
}
