using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Barracuda;
using OpenCvSharp;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using System.Runtime.InteropServices;
// using UnityEngine.SceneManagement; // Panel switching, SceneManager not needed

[System.Serializable]
public class RegisteredObject
{
    public string Name;
    public float[] FeatureVector;
    public KeyPoint[] Keypoints;
    public Mat Descriptors;
    public Size ImageSize;
    public Vector2 RelativeEyePosition;
    public Vector2 RelativeMouthPosition;
    public int EyeStyleId;
    public int MouthStyleId;
}

public class EmbeddingObjectMatcher : MonoBehaviour
{
    // ������ �ǉ�: ���̔F���t���O ������
    /// <summary>
    /// ���̂����ݔF������Ă��邩�ǂ����������t���O
    /// 1 = �F����, 0 = �F���Ȃ�
    /// </summary>
    public int IsObjectRecognized { get; private set; } = 0;

    /// <summary>
    /// �Ō�ɔF�����ꂽ���̖̂��O
    /// </summary>
    public string LastRecognizedObjectName { get; private set; } = "";

    /// <summary>
    /// �F����Ԃ��ω������Ƃ��ɔ��΂���C�x���g
    /// (bool isRecognized, string objectName)
    /// </summary>
    public event Action<bool, string> OnRecognitionStateChanged;
    // ������ �ǉ��I�� ������


    // --- [Startup Mode Static Variable] ---
    public enum StartupMode
    {
        Talk,     // Conversation mode
        Register  // Registration mode
    }
    public static StartupMode DesiredMode { get; set; } = StartupMode.Talk;
    public static EmbeddingObjectMatcher Instance { get; private set; } // Singleton Instance
    public AdvancedSceneLoader sceneLoader;
    // --- [End] ---

    private enum AppMode { Recognition, Naming, Placement }
    private AppMode currentMode = AppMode.Recognition;
    private DatabaseManager dbManager;

    [Header("AI Model")]
    public NNModel modelAsset;

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
    public Button backButton;
    public TMP_InputField nameInputField;
    public TMP_InputField categoryInputField;
    public TMP_Text resultText; // Text in TalkCameraPanel

    [Header("Registration Mode UI")]
    public RectTransform placementEyeImage;
    public RectTransform placementMouthImage;

    [Header("Recognition Mode UI (TalkCameraPanel)")]
    public Image recognitionEyeImage;
    public Image recognitionMouthImage;

    [Header("Style Selection UI")]
    public ScrollRect styleScrollViewRect;
    public GameObject styleButtonPrefab;
    public Transform styleButtonParent;
    public Toggle eyeToggle;
    public Toggle mouthToggle;

    [Header("Recognition Settings")]
    [Range(0.5f, 1.0f)] public float similarityThreshold = 0.85f;
    [Range(0.1f, 2.0f)] public float compareInterval = 0.2f;

    // ������ �ǉ�: �F���r���܂ł̗P�\���� ������
    [Header("Recognition Loss Settings")]
    [Tooltip("Time in seconds to wait before considering the object lost after last recognition.")]
    [Range(0.1f, 5.0f)] public float recognitionLossDelay = 1.0f;
    private float lastRecognitionTime = 0f;
    // ������ �ǉ��I�� ������


    // --- Private Variables ---
    private List<RegisteredObject> database = new List<RegisteredObject>();
    private Texture2D tempSnapshot;
    private int currentEyeId = -1;
    private int currentMouthId = -1;
    private Vector2 tempRelativeEyePos = new Vector2(-1, -1);
    private Vector2 tempRelativeMouthPos = new Vector2(-1, -1);
    private IWorker worker;
    private AKAZE featureDetector;
    private BFMatcher matcher;
    private WebCamTexture webcamTexture;
    private float lastCompareTime = 0f;

    public void ReturnToHomeAndStopCamera(string sceneAndPanelTarget)
    {
        Debug.Log($"Returning to {sceneAndPanelTarget} and stopping camera...");

        // 1. �J�������m���ɒ�~������ (OnDisable���Ă�)
        if (objectMatcherRootPanel != null)
        {
            objectMatcherRootPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("objectMatcherRootPanel is not set!");
        }

        // 2. �V�[�����[�_�[���Ăяo��
        if (sceneLoader != null)
        {
            sceneLoader.LoadSceneAndSetPanel(sceneAndPanelTarget);
        }
        else
        {
            Debug.LogError("AdvancedSceneLoader (sceneLoader) is not assigned in the Inspector!");
        }
    }
    // --- ������ ---

    // Initialize in Awake()
    void Awake()
    {
        // --- Singleton Setup ---
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Keep across scene loads
        }
        else
        {
            Destroy(gameObject); // Destroy duplicates
            return;
        }
        // --- End Singleton ---

        dbManager = new DatabaseManager();

        var model = ModelLoader.Load(modelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
        featureDetector = AKAZE.Create();
        matcher = new BFMatcher(NormTypes.Hamming);

        if (WebCamTexture.devices.Length > 0)
        {
            webcamTexture = new WebCamTexture(WebCamTexture.devices[0].name, 1280, 720, 30);
            // Don't set texture here; set in OnEnable
        }
        else
        {
            Debug.LogError("No webcam devices found!");
        }

        // --- Register Listeners ---
        if (registerStartButton != null) registerStartButton.onClick.AddListener(OnRegisterStart);
        else Debug.LogError("Register Start Button is not assigned!");

        if (nextButton != null) nextButton.onClick.AddListener(OnPlacementConfirmed);
        else Debug.LogError("Next Button is not assigned!");

        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmRegistration);
        else Debug.LogError("Confirm Button is not assigned!");

        if (backButton != null) backButton.onClick.AddListener(OnBackButtonClicked);
        else Debug.LogError("Back Button is not assigned!");

        // Toggles
        if (eyeToggle != null) eyeToggle.onValueChanged.AddListener(OnCategoryChanged);
        if (mouthToggle != null) mouthToggle.onValueChanged.AddListener(OnCategoryChanged);
        // --- End Listeners ---
    }

    // Start camera, load data, and set UI when the panel becomes active
    void OnEnable()
    {
        // --- Load latest data from DB ---
        Debug.Log("OnEnable: Loading database...");
        LoadRegisteredObjectsFromDatabase();
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
        if (webcamTexture != null && !webcamTexture.isPlaying)
        {
            Debug.Log("OnEnable: Starting Webcam...");
            webcamTexture.Play();
        }

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
            if (resultText != null) resultText.text = "�{����...";
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

        // ������ �ǉ�: �J������~���ɔF���t���O�����Z�b�g ������
        UpdateRecognitionState(false, "");
        // ������ �ǉ��I�� ������
    }


    void Update()
    {

        if (webcamTexture != null && webcamTexture.isPlaying && webcamTexture.didUpdateThisFrame)
        {
            UpdateCameraDisplayOrientation();
        }

        // Only run FindObject() in Talk mode
        if (currentMode == AppMode.Recognition && DesiredMode == StartupMode.Talk &&
            webcamTexture != null && webcamTexture.didUpdateThisFrame && database != null && database.Count > 0)
        {
            if (Time.time - lastCompareTime > compareInterval)
            {
                lastCompareTime = Time.time;

                // ������ �ύX: FindObject�̌Ăяo���Ǝ��㏈�� ������
                bool recognized = FindObject();

                // �F������Ă��Ȃ��ꍇ�A�P�\���Ԃ��`�F�b�N
                if (!recognized)
                {
                    // ���ɔF����(1)�ł���A���Ō�̔F������P�\���Ԃ𒴂����ꍇ
                    if (IsObjectRecognized == 1 && Time.time - lastRecognitionTime > recognitionLossDelay)
                    {
                        UpdateRecognitionState(false, "");
                    }
                }
                // ������ �ύX�I�� ������
            }
        }
    }

    void OnDestroy()
    {
        // Remove listeners to prevent memory leaks
        if (registerStartButton != null) registerStartButton.onClick.RemoveListener(OnRegisterStart);
        if (nextButton != null) nextButton.onClick.RemoveListener(OnPlacementConfirmed);
        if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmRegistration);
        if (backButton != null) backButton.onClick.RemoveListener(OnBackButtonClicked);
        if (eyeToggle != null) eyeToggle.onValueChanged.RemoveListener(OnCategoryChanged);
        if (mouthToggle != null) mouthToggle.onValueChanged.RemoveListener(OnCategoryChanged);

        // Dispose resources
        worker?.Dispose();
        matcher?.Dispose();
        featureDetector?.Dispose();
        if (webcamTexture != null)
        {
            if (webcamTexture.isPlaying) webcamTexture.Stop();
            // Destroy(webcamTexture); // Optional: Destroy the texture object itself
        }
        Debug.Log("EmbeddingObjectMatcher OnDestroy completed.");
    }

    // ������ �ǉ�: �F����Ԃ��X�V���郁�\�b�h ������
    /// <summary>
    /// ���̔F����Ԃ��X�V���A�ω�������΃C�x���g�𔭉΂��܂�
    /// </summary>
    /// <param name="isRecognized">�F�����ꂽ���ǂ���</param>
    /// <param name="objectName">�F�����ꂽ���̖̂��O (�F��OFF���� "" ������)</param>
    private void UpdateRecognitionState(bool isRecognized, string objectName)
    {
        int newState = isRecognized ? 1 : 0;
        string newName = isRecognized ? objectName : "";

        // ��Ԃ��ω������ꍇ�̂ݏ��� (0->1, 1->0, or 1->"A" -> 1->"B")
        if (IsObjectRecognized != newState || LastRecognizedObjectName != newName)
        {
            IsObjectRecognized = newState;
            LastRecognizedObjectName = newName;

            // �C�x���g�𔭉�
            OnRecognitionStateChanged?.Invoke(isRecognized, newName);

            // ���O�o��
            if (isRecognized)
            {
                Debug.Log($"★ 物体認識: ON (1) - 物体名: {objectName}");
            }
            else
            {
                Debug.Log($"★ 物体認識: OFF (0)");
            }
        }
    }
    // ������ �ǉ��I�� ������

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

    public void OnRegisterStart()
    {
        if (confirmButton != null) { confirmButton.interactable = true; }
        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            Debug.LogError("Webcam not available or not playing!");
            return;
        }
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
            return;
        }

        // Don't stop camera here; let OnDisable handle it

        if (registerCameraPanel != null) registerCameraPanel.SetActive(false);
        if (placementPanel != null) placementPanel.SetActive(true);

        SwitchToPlacementMode(tempSnapshot);
    }

    public void OnPlacementConfirmed()
    {
        Debug.LogError("<<<<< OnPlacementConfirmed �����s����܂��� >>>>>"); // Keep your debug log
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
            // [�C���O�̃��W�b�N]
            // �u�F���v��ʁiRegisterCameraPanel �܂��� TalkCameraPanel�j����u���[�h�I���v��ʂɖ߂�

            Debug.Log("Back button pressed on Recognition panel. Returning to selection panel.");

            // 1. ���[�h�I���p�l����\������
            if (talkSelectionPanel != null)
            {
                talkSelectionPanel.SetActive(true);
            }
            else
            {
                Debug.LogError("talkSelectionPanel ���ݒ肳��Ă��܂���I");
            }

            // 2. ���̃J�����p�l���i�̐e�j���\���ɂ��� (����ɂ�� OnDisable ���Ă΂�J��������~����)
            if (objectMatcherRootPanel != null)
            {
                Debug.Log("Deactivating objectMatcherRootPanel...");
                objectMatcherRootPanel.SetActive(false);
            }
            else
            {
                Debug.LogError("objectMatcherRootPanel ���ݒ肳��Ă��܂���I");
            }

            // ������ �V�[�������[�h���鏈���͂����ɂ͖��� ������
        }
        else if (currentMode == AppMode.Naming)
        {
            // Naming -> Placement
            Debug.Log("Back button pressed on Naming panel. Returning to placement.");
            currentMode = AppMode.Placement;
            if (namingPanel != null) namingPanel.SetActive(false);
            if (placementPanel != null) placementPanel.SetActive(true);
            // Re-populate styles
            if (eyeToggle != null && mouthToggle != null) // Add null checks
            {
                if (eyeToggle.isOn) PopulateStyleScrollView(true); else PopulateStyleScrollView(false);
            }

        }
        else if (currentMode == AppMode.Placement)
        {
            // Placement -> RegisterCameraPanel
            Debug.Log("Back button pressed on Placement panel. Returning to register camera.");
            currentMode = AppMode.Recognition;
            if (placementPanel != null) placementPanel.SetActive(false);

            if (registerCameraPanel != null) registerCameraPanel.SetActive(true);
            if (talkCameraPanel != null) talkCameraPanel.SetActive(false);

            DesiredMode = StartupMode.Register;
            // OnEnable will handle camera Play if needed
        }
    }

    public void OnConfirmRegistration()
    {
        Debug.LogError("<<<<< !!! OnConfirmRegistration �����s����܂��� !!! >>>>>"); // Keep your debug log
        Debug.Log("OnConfirmRegistration called.");

        if (confirmButton != null) confirmButton.interactable = false;

        string objectName = nameInputField?.text ?? ""; // Null check
        string objectCategory = categoryInputField?.text ?? "Default"; // Null check

        Debug.Log($"Name entered: '{objectName}', Snapshot null?: {tempSnapshot == null}");

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

        Debug.Log("Checking CurrentUser...");
        User currentUser = IntegratedAuthManager.Instance?.CurrentUser; // Null check on Instance
        Debug.Log($"CurrentUser null?: {currentUser == null}");

        if (currentUser == null)
        {
            Debug.LogError("User not logged in. Cannot register object. Redirecting to Login.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("LoginScene"); // Ensure scene name is correct
            return;
        }

        Debug.Log($"CurrentUser ID: {currentUser.user_id}");
        Debug.Log("Proceeding with feature extraction and DB save...");
        Debug.Log($"Using tempRelativeEyePos: ({tempRelativeEyePos.x:F2}, {tempRelativeEyePos.y:F2})");
        Debug.Log($"Using tempRelativeMouthPos: ({tempRelativeMouthPos.x:F2}, {tempRelativeMouthPos.y:F2})");
        Debug.Log($"Using currentEyeId: {currentEyeId}, currentMouthId: {currentMouthId}");

        Mat snapshotMat = null;
        Mat descriptors = null; // Declare here for Dispose in finally
        try
        {
            Debug.Log("Getting feature vector...");
            float[] featureVector = GetFeatureVectorFromTexture(tempSnapshot);
            if (featureVector == null) throw new Exception("Feature vector is null.");
            Debug.Log($"Got feature vector, length: {featureVector.Length}");

            Debug.Log("Creating snapshot Mat...");
            snapshotMat = OpenCvSharp.Unity.TextureToMat(tempSnapshot);
            if (snapshotMat == null || snapshotMat.Empty()) throw new Exception("snapshotMat is null or empty.");
            Debug.Log($"Created snapshot Mat: {snapshotMat.Width}x{snapshotMat.Height}");

            Debug.Log("Detecting keypoints and descriptors...");
            KeyPoint[] keypoints;
            descriptors = new Mat(); // Initialize here
            featureDetector.DetectAndCompute(snapshotMat, null, out keypoints, descriptors);

            if (keypoints == null) throw new Exception("Keypoints are null.");
            Debug.Log($"Detected keypoints: {keypoints.Length}");
            if (descriptors == null || descriptors.Empty()) throw new Exception("Descriptors are null or empty.");
            Debug.Log($"Created descriptors: {descriptors.Rows}x{descriptors.Cols}");

            Debug.Log("Converting features to byte arrays...");
            byte[] featureVectorBytes = new byte[featureVector.Length * sizeof(float)];
            Buffer.BlockCopy(featureVector, 0, featureVectorBytes, 0, featureVectorBytes.Length);
            byte[] keypointsBytes = SerializeKeyPoints(keypoints);
            byte[] descriptorsBytes = SerializeMat(descriptors);
            if (keypointsBytes == null || descriptorsBytes == null) throw new Exception("Serialization failed.");
            Debug.Log($"Converted: FV={featureVectorBytes.Length}, KP={keypointsBytes.Length}, Desc={descriptorsBytes.Length}");

            Debug.Log("Adding to memory database...");
            database.Add(new RegisteredObject
            {
                Name = objectName,
                FeatureVector = featureVector,
                Keypoints = keypoints,
                Descriptors = descriptors.Clone(), // Clone descriptors for the in-memory list
                ImageSize = snapshotMat.Size(),
                RelativeEyePosition = tempRelativeEyePos,
                RelativeMouthPosition = tempRelativeMouthPos,
                EyeStyleId = currentEyeId,
                MouthStyleId = currentMouthId
            });
            Debug.Log("Added to memory database.");

            Debug.Log("Creating ObjectItem for DB...");
            var newObjectItem = new ObjectItem
            {
                object_id = Guid.NewGuid().ToString(),
                user_id = currentUser.user_id,
                Name = objectName,
                ImageData = tempSnapshot.EncodeToJPG(),
                FeatureVectorBlob = featureVectorBytes,
                KeyPointsBlob = keypointsBytes,
                DescriptorsBlob = descriptorsBytes,
                EyePositionX = tempRelativeEyePos.x,
                EyePositionY = tempRelativeEyePos.y,
                MouthPositionX = tempRelativeMouthPos.x,
                MouthPositionY = tempRelativeMouthPos.y,
                ImageWidth = snapshotMat.Size().Width,
                ImageHeight = snapshotMat.Size().Height,
                EyeStyleId = currentEyeId,
                MouthStyleId = currentMouthId
            };
            Debug.Log($"Created ObjectItem, ID: {newObjectItem.object_id}");

            List<string> categories = new List<string>();
            if (!string.IsNullOrEmpty(objectCategory))
            {
                categories.Add(objectCategory.Trim());
            }

            Debug.Log("Saving to database...");
            if (dbManager == null) throw new Exception("dbManager is null.");

            dbManager.SaveObjectWithFeatures(newObjectItem, categories);
            Debug.Log($"Object '{objectName}' saved to DB successfully.");

        }
        catch (Exception e)
        {
            Debug.LogError($"Error during registration process: {e.ToString()}"); // Log full exception
            if (confirmButton != null) confirmButton.interactable = true; // Re-enable button on error
            return; // Stop execution on error
        }
        finally
        {
            // Ensure OpenCV Mats are disposed even if errors occur
            snapshotMat?.Dispose();
            descriptors?.Dispose(); // Dispose descriptors here
        }

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
        if (eyeToggle != null && eyeToggle.isOn) // Add null check
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
                buttonComp.onClick.RemoveAllListeners(); // Clear previous listeners if any
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

    #region Recognition Logic

    // ���������̃��\�b�h���ۂ��ƃN���X���ɒǉ�������
    /// <summary>
    /// �J�����f����\������RawImage�̌����ƃA�X�y�N�g���␳���܂�
    /// </summary>
    private void UpdateCameraDisplayOrientation()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying) return;

        // ���݃A�N�e�B�u�ȃJ�����\��UI��I�� (Talk���[�h��Register���[�h���ŕ���)
        RawImage activeCameraDisplay = null;
        if (DesiredMode == StartupMode.Talk && talkCameraPanel != null && talkCameraPanel.activeInHierarchy)
        {
            activeCameraDisplay = talkCameraDisplay;
        }
        else if (DesiredMode == StartupMode.Register && registerCameraPanel != null && registerCameraPanel.activeInHierarchy)
        {
            activeCameraDisplay = registerCameraDisplay;
        }

        // �A�N�e�B�u�ȕ\��UI���Ȃ���Ώ������Ȃ�
        if (activeCameraDisplay == null) return;

        // --- �A�X�y�N�g��̒��� ---
        float videoRotationAngle = webcamTexture.videoRotationAngle;
        float videoAspectRatio = (float)webcamTexture.width / webcamTexture.height;

        AspectRatioFitter fitter = activeCameraDisplay.GetComponent<AspectRatioFitter>();
        if (fitter != null)
        {
            // 90�x�܂���270�x��]���Ă���ꍇ�̓A�X�y�N�g����t���ɂ���
            if (Mathf.Abs(videoRotationAngle) == 90 || Mathf.Abs(videoRotationAngle) == 270)
            {
                fitter.aspectRatio = 1.0f / videoAspectRatio;
            }
            else
            {
                fitter.aspectRatio = videoAspectRatio;
            }
        }

        // --- ��]�̒��� ---
        // RawImage���̂̌������J�����̉�]�̋t�����ɐݒ�
        activeCameraDisplay.rectTransform.localEulerAngles = new Vector3(0, 0, -videoRotationAngle);

        // --- �~���[�����O�̒��� (�C���J�����̏ꍇ�ɕK�v) ---
        // WebCamTexture���O�ʃJ�������g���Ă��邩�ǂ����̔��� (�f�o�C�X����"Front"���܂܂�邩�Ȃ�)
        bool isFrontFacing = webcamTexture.deviceName.ToLower().Contains("front"); // ���̔�����@�̓f�o�C�X�ˑ�
        if (webcamTexture.videoVerticallyMirrored) // Texture���̂����]���Ă��邩
        {
            // Y�X�P�[���𔽓] (�㉺���]) - �ʏ�A�����~���[�����O�͕s�v�Ȃ��Ƃ�����
            // activeCameraDisplay.rectTransform.localScale = new Vector3(isFrontFacing ? -1 : 1, -1, 1);
            activeCameraDisplay.rectTransform.localScale = new Vector3(isFrontFacing ? -1 : 1, 1, 1);
        }
        else
        {
            // X�X�P�[���𔽓] (���E���]) - �C���J�����̏ꍇ
            activeCameraDisplay.rectTransform.localScale = new Vector3(isFrontFacing ? -1 : 1, 1, 1);
        }
    }
    // �������ǉ������܂Ł�����
    private void LoadRegisteredObjectsFromDatabase()
    {
        if (dbManager == null) { Debug.LogError("DatabaseManager is null! Cannot load objects."); return; }
        User currentUser = IntegratedAuthManager.Instance?.CurrentUser;
        if (currentUser == null)
        {
            Debug.LogWarning("User not logged in, cannot load detection data from DB.");
            database.Clear();
            return;
        }

        List<ObjectItem> userObjects = dbManager.GetAllUserObjectsWithDetails(currentUser.user_id);
        database.Clear();

        if (userObjects == null)
        {
            Debug.LogWarning("GetAllUserObjectsWithDetails returned null.");
            return;
        }

        Debug.Log($"Attempting to load {userObjects.Count} objects from DB...");
        foreach (var item in userObjects)
        {
            if (item == null || item.FeatureVectorBlob == null || item.FeatureVectorBlob.Length == 0 || item.KeyPointsBlob == null || item.DescriptorsBlob == null)
            {
                Debug.LogWarning($"Skipping incomplete object data (ID: {item?.object_id ?? "N/A"}, Name: {item?.Name ?? "N/A"}).");
                continue;
            }

            Mat itemDescriptors = null; // Declare here for Dispose in catch/finally
            try
            {
                float[] featureVector = new float[item.FeatureVectorBlob.Length / sizeof(float)];
                Buffer.BlockCopy(item.FeatureVectorBlob, 0, featureVector, 0, item.FeatureVectorBlob.Length);

                KeyPoint[] keypoints = DeserializeKeyPoints(item.KeyPointsBlob);
                itemDescriptors = DeserializeMat(item.DescriptorsBlob); // Assign here

                if (keypoints == null || keypoints.Length == 0 || itemDescriptors == null || itemDescriptors.Empty())
                {
                    Debug.LogWarning($"Failed to deserialize features for object: {item.Name}. Skipping.");
                    continue; // Skip if deserialization failed
                }

                Vector2 eyePos = new Vector2(item.EyePositionX, item.EyePositionY);
                Vector2 mouthPos = new Vector2(item.MouthPositionX, item.MouthPositionY);
                OpenCvSharp.Size imageSize = new OpenCvSharp.Size(item.ImageWidth, item.ImageHeight);

                database.Add(new RegisteredObject
                {
                    Name = item.Name,
                    FeatureVector = featureVector,
                    Keypoints = keypoints,
                    Descriptors = itemDescriptors, // Add the Mat directly
                    RelativeEyePosition = eyePos,
                    RelativeMouthPosition = mouthPos,
                    ImageSize = imageSize,
                    EyeStyleId = item.EyeStyleId,
                    MouthStyleId = item.MouthStyleId
                });
                // IMPORTANT: Do NOT dispose itemDescriptors here, it's stored in the database list
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading object '{item.Name}' from DB: {e.ToString()}");
                itemDescriptors?.Dispose(); // Dispose if an error occurred during processing
            }
        }
        Debug.Log($"Successfully loaded {database.Count} objects into memory database.");
    }

    // [�C���ς�] �C���f�b�N�X�`�F�b�N��ǉ�
    private bool FindObject()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying || database == null || database.Count == 0 || featureDetector == null || matcher == null) return false;

        float[] currentVector = GetFeatureVectorFromTexture(webcamTexture);
        if (currentVector == null) return false;

        float bestSimilarity = -1f;
        RegisteredObject potentialMatch = null;
        foreach (var obj in database)
        {
            if (obj?.FeatureVector == null) continue;
            float similarity = CalculateCosineSimilarity(currentVector, obj.FeatureVector);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                potentialMatch = obj;
            }
        }

        if (bestSimilarity > similarityThreshold && potentialMatch != null)
        {
            Mat liveFrameMat = null;
            Mat liveDescriptors = null;
            Mat homography = null;
            try
            {
                liveFrameMat = OpenCvSharp.Unity.TextureToMat(webcamTexture);
                KeyPoint[] liveKeypoints;
                liveDescriptors = new Mat();
                featureDetector.DetectAndCompute(liveFrameMat, null, out liveKeypoints, liveDescriptors);

                if (liveKeypoints != null && liveKeypoints.Length > 20 &&
                    potentialMatch.Keypoints != null && // Check registered keypoints
                    potentialMatch.Descriptors != null && !potentialMatch.Descriptors.Empty())
                {
                    var matches = matcher.KnnMatch(potentialMatch.Descriptors, liveDescriptors, 2);
                    var goodMatches = matches.Where(m => m != null && m.Length >= 2 && m[0].Distance < 0.75 * m[1].Distance)
                                            .Select(m => m[0])
                                            .ToList();

                    if (goodMatches.Count > 20)
                    {
                        // Index out of bounds check
                        var validMatches = goodMatches.Where(m =>
                            m.QueryIdx >= 0 && m.QueryIdx < potentialMatch.Keypoints.Length &&
                            m.TrainIdx >= 0 && m.TrainIdx < liveKeypoints.Length
                        ).ToList();

                        if (validMatches.Count > 20)
                        {
                            var srcPts = validMatches.Select(m => potentialMatch.Keypoints[m.QueryIdx].Pt).ToArray();
                            var dstPts = validMatches.Select(m => liveKeypoints[m.TrainIdx].Pt).ToArray();

                            homography = Cv2.FindHomography(InputArray.Create(srcPts), InputArray.Create(dstPts), HomographyMethods.Ransac, 5.0);

                            if (homography != null && !homography.Empty())
                            {
                                UpdateFacialFeatures(potentialMatch, homography);
                                if (resultText != null) resultText.text = $"発見: {potentialMatch.Name}!";
                                Debug.Log($"FindObject finished. Found: True - {potentialMatch.Name}");

                                // ������ �ǉ�: �F���������Ƀt���O���X�V ������
                                lastRecognitionTime = Time.time;
                                UpdateRecognitionState(true, potentialMatch.Name);
                                // ������ �ǉ��I�� ������

                                return true; // Success
                            }
                        } // end if (validMatches.Count > 20)
                    } // end if (goodMatches.Count > 20)
                } // end if (liveKeypoints != null ...)
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during FindObject feature matching: {e.ToString()}");
            }
            finally
            {
                homography?.Dispose();
                liveFrameMat?.Dispose();
                liveDescriptors?.Dispose();
            }
        } // end if (bestSimilarity > similarityThreshold ...)

        // Reset UI if no match
        if (resultText != null) resultText.text = "捜索中...";
        if (recognitionEyeImage != null) recognitionEyeImage.gameObject.SetActive(false);
        if (recognitionMouthImage != null) recognitionMouthImage.gameObject.SetActive(false);
        // Debug.Log("FindObject finished. Found: False");
        return false;
    }


    private float[] GetFeatureVectorFromTexture(Texture inputTexture)
    {
        if (inputTexture == null || worker == null) return null;
        RenderTexture tempRT = RenderTexture.GetTemporary(224, 224, 0, RenderTextureFormat.Default);
        Graphics.Blit(inputTexture, tempRT);
        Tensor tensor = null;
        float[] featureVector = null;
        try
        {
            tensor = new Tensor(tempRT, 3);
            worker.Execute(tensor);
            Tensor outputTensor = worker.PeekOutput(); // Use PeekOutput
            featureVector = outputTensor.AsFloats();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting feature vector: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.ReleaseTemporary(tempRT);
            tensor?.Dispose();
        }
        return featureVector;
    }

    private void UpdateFacialFeatures(RegisteredObject obj, Mat homography)
    {
        // Add null checks for safety
        bool eyeSet = false;
        if (obj.EyeStyleId != -1 && recognitionEyeImage != null && eyeStyles != null && obj.EyeStyleId >= 0 && obj.EyeStyleId < eyeStyles.Count)
        {
            recognitionEyeImage.sprite = eyeStyles[obj.EyeStyleId];
            UpdatePartPosition(obj, homography, obj.RelativeEyePosition, recognitionEyeImage.rectTransform);
            recognitionEyeImage.gameObject.SetActive(true);
            eyeSet = true;
        }
        if (!eyeSet && recognitionEyeImage != null)
        {
            recognitionEyeImage.gameObject.SetActive(false);
        }

        bool mouthSet = false;
        if (obj.MouthStyleId != -1 && recognitionMouthImage != null && mouthStyles != null && obj.MouthStyleId >= 0 && obj.MouthStyleId < mouthStyles.Count)
        {
            recognitionMouthImage.sprite = mouthStyles[obj.MouthStyleId];
            UpdatePartPosition(obj, homography, obj.RelativeMouthPosition, recognitionMouthImage.rectTransform);
            recognitionMouthImage.gameObject.SetActive(true);
            mouthSet = true;
        }
        if (!mouthSet && recognitionMouthImage != null)
        {
            recognitionMouthImage.gameObject.SetActive(false);
        }
    }


    private void UpdatePartPosition(RegisteredObject obj, Mat homography, Vector2 relativePos, RectTransform partTransform)
    {
        if (talkCameraDisplay == null || webcamTexture == null || homography == null || homography.Empty() || obj == null || obj.ImageSize.Width <= 0 || obj.ImageSize.Height <= 0 || partTransform == null) return;

        try
        {
            Point2f[] srcCorners = { new Point2f(0, 0), new Point2f(obj.ImageSize.Width, 0), new Point2f(obj.ImageSize.Width, obj.ImageSize.Height), new Point2f(0, obj.ImageSize.Height) };
            Point2f[] dstCornersF = Cv2.PerspectiveTransform(srcCorners, homography);

            if (dstCornersF == null || dstCornersF.Length != 4) return;

            Vector2[] dstCorners = new Vector2[4];
            for (int i = 0; i < 4; i++) { dstCorners[i] = new Vector2(dstCornersF[i].X, dstCornersF[i].Y); }

            relativePos.x = Mathf.Clamp01(relativePos.x);
            relativePos.y = Mathf.Clamp01(relativePos.y);

            Vector2 topEdge = Vector2.Lerp(dstCorners[0], dstCorners[1], relativePos.x);
            Vector2 bottomEdge = Vector2.Lerp(dstCorners[3], dstCorners[2], relativePos.x);
            Vector2 projectedPos = Vector2.Lerp(topEdge, bottomEdge, 1 - relativePos.y);

            RectTransform cameraRect = talkCameraDisplay.rectTransform;
            if (cameraRect == null) return;

            Vector2 normalizedPos = new Vector2(
                projectedPos.x / webcamTexture.width,
                1.0f - (projectedPos.y / webcamTexture.height) // Invert Y for UI
            );

            Vector2 localPos = new Vector2(
                (normalizedPos.x - cameraRect.pivot.x) * cameraRect.rect.width,
                (normalizedPos.y - cameraRect.pivot.y) * cameraRect.rect.height
            );

            partTransform.anchoredPosition = localPos;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during UpdatePartPosition calculation: {e.ToString()}");
        }
    }

    private float CalculateCosineSimilarity(float[] vecA, float[] vecB)
    {
        if (vecA == null || vecB == null || vecA.Length != vecB.Length) return 0;

        float dotProduct = 0.0f;
        float magnitudeA = 0.0f;
        float magnitudeB = 0.0f;
        int len = vecA.Length;
        for (int i = 0; i < len; i++)
        {
            dotProduct += vecA[i] * vecB[i];
            magnitudeA += vecA[i] * vecA[i];
            magnitudeB += vecB[i] * vecB[i];
        }

        if (magnitudeA == 0 || magnitudeB == 0) return 0;

        magnitudeA = Mathf.Sqrt(magnitudeA);
        magnitudeB = Mathf.Sqrt(magnitudeB);

        if (float.IsNaN(magnitudeA) || float.IsNaN(magnitudeB)) return 0;

        // Add a small epsilon to denominator to prevent potential division by extremely small numbers near zero
        float denominator = (magnitudeA * magnitudeB);
        if (denominator < float.Epsilon) return 0;

        return dotProduct / denominator;
    }
    #endregion

    #region Serialization/Deserialization Helpers
    private byte[] SerializeKeyPoints(KeyPoint[] keyPoints)
    {
        if (keyPoints == null) return null;
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(keyPoints.Length);
            foreach (var kp in keyPoints)
            {
                if (kp == null) continue; // Safety check
                writer.Write(kp.Pt.X); writer.Write(kp.Pt.Y);
                writer.Write(kp.Size); writer.Write(kp.Angle);
                writer.Write(kp.Response); writer.Write(kp.Octave);
            }
            return ms.ToArray();
        }
    }

    private KeyPoint[] DeserializeKeyPoints(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return new KeyPoint[0];
        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
                if (ms.Length - ms.Position < sizeof(int)) return new KeyPoint[0];
                var len = reader.ReadInt32();
                if (len < 0 || len > 100000) return new KeyPoint[0]; // More reasonable max check
                if (len == 0) return new KeyPoint[0];
                // More accurate size check
                long expectedBytes = (long)len * (sizeof(float) * 5 + sizeof(int)); // 5 floats (X, Y, Size, Angle, Response) + 1 int (Octave)
                if (ms.Length - ms.Position < expectedBytes) return new KeyPoint[0];

                var keyPoints = new KeyPoint[len];
                for (int i = 0; i < len; i++)
                {
                    var x = reader.ReadSingle(); var y = reader.ReadSingle();
                    var size = reader.ReadSingle(); var angle = reader.ReadSingle();
                    var response = reader.ReadSingle(); var octave = reader.ReadInt32();
                    keyPoints[i] = new KeyPoint(x, y, size, angle, response, octave, -1); // Use constructor specifying all params
                }
                return keyPoints;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error deserializing KeyPoints: {e.ToString()}"); // Log stack trace
            return new KeyPoint[0];
        }
    }

    private byte[] SerializeMat(Mat mat)
    {
        if (mat == null || mat.Empty()) return null;
        try
        {
            var size = mat.Total() * mat.ElemSize();
            if (size <= 0 || size > int.MaxValue) return null;
            var data = new byte[size];
            Marshal.Copy(mat.Data, data, 0, (int)size);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(mat.Rows);
                writer.Write(mat.Cols);
                writer.Write((int)mat.Type());
                writer.Write(data.Length);
                writer.Write(data);
                return ms.ToArray();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error serializing Mat: {e.ToString()}"); // Log stack trace
            return null;
        }
    }

    private Mat DeserializeMat(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        Mat mat = null; // Declare outside try for potential Dispose in catch
        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
                if (ms.Length - ms.Position < sizeof(int) * 4) return null;
                var rows = reader.ReadInt32();
                var cols = reader.ReadInt32();
                var typeValue = reader.ReadInt32();
                var len = reader.ReadInt32();

                if (rows < 0 || cols < 0 || len < 0 || len > bytes.Length || ms.Length - ms.Position < len) return null;

                MatType type;
                try { type = (MatType)typeValue; } catch { return null; }

                var data = reader.ReadBytes(len);
                mat = new Mat(rows, cols, type); // Assign to outer variable

                long expectedSize = mat.Total() * mat.ElemSize();
                if (expectedSize != len)
                {
                    Debug.LogError($"Mat deserialization size mismatch. Expected {expectedSize}, got {len}. Rows={rows}, Cols={cols}, Type={type}");
                    mat.Dispose(); // Dispose incorrectly sized Mat
                    return null;
                }

                if (data.Length > 0) Marshal.Copy(data, 0, mat.Data, data.Length);
                return mat; // Return the created Mat
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error deserializing Mat: {e.ToString()}"); // Log stack trace
            mat?.Dispose(); // Dispose if created before error
            return null;
        }
    }
    #endregion
}