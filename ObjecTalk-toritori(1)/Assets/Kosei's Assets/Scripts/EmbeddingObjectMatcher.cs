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
    // --- [Startup Mode Static Variable] ---
    public enum StartupMode
    {
        Talk,     // Conversation mode
        Register  // Registration mode
    }
    public static StartupMode DesiredMode { get; set; } = StartupMode.Talk;
    public static EmbeddingObjectMatcher Instance { get; private set; }
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

    private List<RegisteredObject> database = new List<RegisteredObject>();
    private Texture2D tempSnapshot;
    private int currentEyeId = -1;
    private int currentMouthId = -1;

    // --- ▼▼▼ 変数宣言を追加 ▼▼▼ ---
    private Vector2 tempRelativeEyePos = new Vector2(-1, -1); // 計算結果を一時保存
    private Vector2 tempRelativeMouthPos = new Vector2(-1, -1); // 計算結果を一時保存
    // --- ▲▲▲ ---


    private IWorker worker;
    private AKAZE featureDetector;
    private BFMatcher matcher;

    private WebCamTexture webcamTexture;
    private float lastCompareTime = 0f;

    // Initialize in Awake()
    void Awake()
    {

        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

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

        LoadRegisteredObjectsFromDatabase();

        // --- ▼▼▼ リスナーをコードで統一 ▼▼▼ ---
        if (registerStartButton != null)
        {
            registerStartButton.onClick.AddListener(OnRegisterStart);
        }
        else { Debug.LogError("Register Start Button is not assigned!"); }

        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnPlacementConfirmed);
        }
        else { Debug.LogError("Next Button is not assigned!"); }

        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmRegistration);
        }
        else { Debug.LogError("Confirm Button is not assigned!"); }

        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
        else { Debug.LogError("Back Button is not assigned!"); }

        // トグル
        if (eyeToggle != null) eyeToggle.onValueChanged.AddListener(OnCategoryChanged);
        if (mouthToggle != null) mouthToggle.onValueChanged.AddListener(OnCategoryChanged);
        // --- ▲▲▲ ---
    }

    // Start camera and set UI when the panel becomes active
    void OnEnable()
    {
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
            webcamTexture.Play();
        }

        // 3. Hide registration flow panels
        if (placementPanel != null) placementPanel.SetActive(false);
        if (namingPanel != null) namingPanel.SetActive(false);

        // 4. Decide which camera panel to show based on DesiredMode
        if (DesiredMode == StartupMode.Talk)
        {
            // Conversation Mode
            if (talkCameraPanel != null) talkCameraPanel.SetActive(true);
            if (registerCameraPanel != null) registerCameraPanel.SetActive(false);

            currentMode = AppMode.Recognition;
            if (resultText != null) resultText.text = "捜索中...";
            if (recognitionEyeImage != null) recognitionEyeImage.gameObject.SetActive(false);
            if (recognitionMouthImage != null) recognitionMouthImage.gameObject.SetActive(false);
        }
        else // DesiredMode == StartupMode.Register
        {
            // Registration Mode
            if (talkCameraPanel != null) talkCameraPanel.SetActive(false);
            if (registerCameraPanel != null) registerCameraPanel.SetActive(true);

            currentMode = AppMode.Recognition;
        }
    }

    // Stop camera when the panel becomes inactive
    void OnDisable()
    {
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            webcamTexture.Stop();
        }
    }


    void Update()
    {
        // Only run FindObject() in Talk mode
        if (currentMode == AppMode.Recognition && DesiredMode == StartupMode.Talk &&
            webcamTexture != null && webcamTexture.didUpdateThisFrame && database.Count > 0)
        {
            if (Time.time - lastCompareTime > compareInterval)
            {
                lastCompareTime = Time.time;
                FindObject();
            }
        }
    }

    void OnDestroy()
    {
        if (registerStartButton != null) registerStartButton.onClick.RemoveListener(OnRegisterStart);
        if (nextButton != null) nextButton.onClick.RemoveListener(OnPlacementConfirmed);
        if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmRegistration); // リスナー削除追加
        if (backButton != null) backButton.onClick.RemoveListener(OnBackButtonClicked);
        if (eyeToggle != null) eyeToggle.onValueChanged.RemoveListener(OnCategoryChanged);
        if (mouthToggle != null) mouthToggle.onValueChanged.RemoveListener(OnCategoryChanged);

        worker?.Dispose();
        matcher?.Dispose();
        featureDetector?.Dispose();
        if (webcamTexture != null) webcamTexture.Stop(); // Ensure stopped on destroy
    }

    #region Mode Switching
    private void SwitchToNamingMode()
    {
        currentMode = AppMode.Naming;
        if (tempSnapshot == null) { Debug.LogError("Snapshot is missing!"); return; }
        // [修正] UnityEngine.Rect を明示
        var sprite = Sprite.Create(tempSnapshot, new UnityEngine.Rect(0, 0, tempSnapshot.width, tempSnapshot.height), new Vector2(0.5f, 0.5f));
        snapshotDisplay_Naming.sprite = sprite;

        if (namingPanel != null) namingPanel.SetActive(true);
        if (placementPanel != null) placementPanel.SetActive(false);

        nameInputField.text = "";
        if (categoryInputField != null) categoryInputField.text = "";
    }
    private void SwitchToPlacementMode(Texture2D snapshot)
    {
        currentMode = AppMode.Placement;
        // [修正] UnityEngine.Rect を明示
        snapshotDisplay_Placement.sprite = Sprite.Create(snapshot, new UnityEngine.Rect(0, 0, snapshot.width, snapshot.height), new Vector2(0.5f, 0.5f));

        mouthToggle.isOn = false;
        eyeToggle.isOn = true; // デフォルトで目を表示
        PopulateStyleScrollView(true); // 目のスタイルを表示

        // パーツの初期状態を非表示にする
        placementEyeImage.gameObject.SetActive(false);
        placementMouthImage.gameObject.SetActive(false);
        // IDもリセット
        currentEyeId = -1;
        currentMouthId = -1;
    }
    #endregion

    #region Button Handlers & UI Logic
    public void SelectEyeStyle(int id)
    {
        if (id < 0 || id >= eyeStyles.Count) return;
        currentEyeId = id;
        placementEyeImage.GetComponent<Image>().sprite = eyeStyles[id];
        placementEyeImage.gameObject.SetActive(true);
    }

    public void SelectMouthStyle(int id)
    {
        if (id < 0 || id >= mouthStyles.Count) return;
        currentMouthId = id;
        placementMouthImage.GetComponent<Image>().sprite = mouthStyles[id];
        placementMouthImage.gameObject.SetActive(true);
    }

    public void OnRegisterStart()
    {
        if (confirmButton != null) { confirmButton.interactable = true; } // NamingPanelのボタン
        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            Debug.LogError("Webcam not available or not playing!");
            return;
        }
        tempSnapshot = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
        tempSnapshot.SetPixels(webcamTexture.GetPixels());
        tempSnapshot.Apply();

        // Don't stop camera here; let OnDisable handle it

        if (registerCameraPanel != null) registerCameraPanel.SetActive(false);
        if (placementPanel != null) placementPanel.SetActive(true);

        SwitchToPlacementMode(tempSnapshot);
    }

    // [修正] 位置計算を追加
    public void OnPlacementConfirmed()
    {
        Debug.LogError("<<<<< OnPlacementConfirmed が実行されました >>>>>");
        Debug.Log("OnPlacementConfirmed called. Calculating positions...");

        // --- ▼▼▼ ここで位置計算を実行 ▼▼▼ ---
        tempRelativeEyePos = new Vector2(-1, -1); // 計算前にリセット
        if (placementEyeImage != null && placementEyeImage.gameObject.activeInHierarchy)
        {
            if (snapshotDisplay_Placement == null || snapshotDisplay_Placement.rectTransform == null)
            {
                Debug.LogError("snapshotDisplay_Placement reference is missing!");
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

        tempRelativeMouthPos = new Vector2(-1, -1); // 計算前にリセット
        if (placementMouthImage != null && placementMouthImage.gameObject.activeInHierarchy)
        {
            if (snapshotDisplay_Placement == null || snapshotDisplay_Placement.rectTransform == null)
            {
                Debug.LogError("snapshotDisplay_Placement reference is missing!");
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
        // --- ▲▲▲ 計算ここまで ▲▲▲ ---

        // パーツ配置完了後、名前入力画面へ
        SwitchToNamingMode();
    }


    public void OnBackButtonClicked()
    {
        if (currentMode == AppMode.Recognition)
        {
            // Camera Panel -> Mode Selection Panel
            if (talkSelectionPanel != null) talkSelectionPanel.SetActive(true);
            if (objectMatcherRootPanel != null) objectMatcherRootPanel.SetActive(false);
        }
        else if (currentMode == AppMode.Naming)
        {
            // Naming -> Placement
            currentMode = AppMode.Placement;
            if (namingPanel != null) namingPanel.SetActive(false);
            if (placementPanel != null) placementPanel.SetActive(true);
            // Re-populate styles in case user goes back and forth
            if (eyeToggle.isOn) PopulateStyleScrollView(true); else PopulateStyleScrollView(false);

        }
        else if (currentMode == AppMode.Placement)
        {
            // Placement -> RegisterCameraPanel
            currentMode = AppMode.Recognition;
            if (placementPanel != null) placementPanel.SetActive(false);
            if (registerCameraPanel != null) registerCameraPanel.SetActive(true);
            if (talkCameraPanel != null) talkCameraPanel.SetActive(false);
            DesiredMode = StartupMode.Register;
        }
    }

    // [修正] 位置計算を削除し、一時変数を使用
    public void OnConfirmRegistration()
    {
        Debug.Log("<<<<< OnConfirmRegistration が実行されました >>>>>");

        // 各参照のnullチェックとログ出力
        Debug.Log($"confirmButton is null?: {confirmButton == null}");
        Debug.Log($"nameInputField is null?: {nameInputField == null}");
        Debug.Log($"tempSnapshot is null?: {tempSnapshot == null}");
        Debug.Log($"IntegratedAuthManager.Instance is null?: {IntegratedAuthManager.Instance == null}");

        if (confirmButton != null) confirmButton.interactable = false;

        string objectName = "";
        if (nameInputField != null)
        {
            objectName = nameInputField.text;
            Debug.Log($"入力された名前: '{objectName}'");
        }
        else
        {
            Debug.LogError("nameInputField が null です！");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        string objectCategory = "";
        if (categoryInputField != null)
        {
            objectCategory = categoryInputField.text;
            Debug.Log($"入力されたカテゴリ: '{objectCategory}'");
        }
        else
        {
            Debug.LogWarning("categoryInputField が null です（デフォルトを使用）");
            objectCategory = "Default";
        }

        if (string.IsNullOrEmpty(objectName))
        {
            Debug.LogWarning("名前が空のため中断します。");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        if (tempSnapshot == null)
        {
            Debug.LogError("tempSnapshot が null です！");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        Debug.Log("CurrentUserを取得します...");

        if (IntegratedAuthManager.Instance == null)
        {
            Debug.LogError("IntegratedAuthManager.Instance が null です！");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        User currentUser = IntegratedAuthManager.Instance.CurrentUser;
        Debug.Log($"CurrentUser は null?: {currentUser == null}");

        if (currentUser == null)
        {
            Debug.LogError("User not logged in. Cannot register object.");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        Debug.Log($"CurrentUser.user_id: {currentUser.user_id}");
        Debug.Log("特徴量抽出とDB保存に進みます...");
        Debug.Log($"tempRelativeEyePos: ({tempRelativeEyePos.x:F2}, {tempRelativeEyePos.y:F2})");
        Debug.Log($"tempRelativeMouthPos: ({tempRelativeMouthPos.x:F2}, {tempRelativeMouthPos.y:F2})");
        Debug.Log($"currentEyeId: {currentEyeId}, currentMouthId: {currentMouthId}");

        Mat snapshotMat = null;
        try
        {
            Debug.Log("特徴量ベクトルを取得中...");
            float[] featureVector = GetFeatureVectorFromTexture(tempSnapshot);
            if (featureVector == null)
            {
                Debug.LogError("特徴量ベクトルが null です！");
                throw new Exception("特徴量ベクトルがnullです。");
            }
            Debug.Log($"特徴量ベクトル取得成功: 長さ={featureVector.Length}");

            Debug.Log("snapshotMatを作成中...");
            snapshotMat = OpenCvSharp.Unity.TextureToMat(tempSnapshot);
            if (snapshotMat == null || snapshotMat.Empty())
            {
                Debug.LogError("snapshotMat の作成に失敗しました！");
                throw new Exception("snapshotMatがnullまたは空です。");
            }
            Debug.Log($"snapshotMat作成成功: {snapshotMat.Width}x{snapshotMat.Height}");

            Debug.Log("キーポイントとディスクリプタを検出中...");
            KeyPoint[] keypoints;
            Mat descriptors = new Mat();
            featureDetector.DetectAndCompute(snapshotMat, null, out keypoints, descriptors);

            if (keypoints == null)
            {
                Debug.LogError("キーポイントが null です！");
                throw new Exception("キーポイントがnullです。");
            }
            Debug.Log($"キーポイント検出成功: {keypoints.Length}個");

            if (descriptors == null || descriptors.Empty())
            {
                Debug.LogError("ディスクリプタが null または空です！");
                throw new Exception("ディスクリプタがnullまたは空です。");
            }
            Debug.Log($"ディスクリプタ作成成功: {descriptors.Rows}x{descriptors.Cols}");

            Debug.Log("バイト配列に変換中...");
            byte[] featureVectorBytes = new byte[featureVector.Length * sizeof(float)];
            Buffer.BlockCopy(featureVector, 0, featureVectorBytes, 0, featureVectorBytes.Length);
            byte[] keypointsBytes = SerializeKeyPoints(keypoints);
            byte[] descriptorsBytes = SerializeMat(descriptors);
            Debug.Log($"変換成功: featureVectorBytes={featureVectorBytes.Length}, keypointsBytes={keypointsBytes.Length}, descriptorsBytes={descriptorsBytes.Length}");

            // database リストへの追加
            Debug.Log("database リストに追加中...");
            database.Add(new RegisteredObject
            {
                Name = objectName,
                FeatureVector = featureVector,
                Keypoints = keypoints,
                Descriptors = descriptors.Clone(),
                ImageSize = snapshotMat.Size(),
                RelativeEyePosition = tempRelativeEyePos,
                RelativeMouthPosition = tempRelativeMouthPos,
                EyeStyleId = currentEyeId,
                MouthStyleId = currentMouthId
            });
            Debug.Log("database リストへの追加成功");

            // DB保存用のデータ作成
            Debug.Log("ObjectItem を作成中...");
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
            Debug.Log($"ObjectItem作成成功: object_id={newObjectItem.object_id}");

            List<string> categories = new List<string>();
            if (!string.IsNullOrEmpty(objectCategory))
            {
                categories.Add(objectCategory.Trim());
            }

            Debug.Log("データベースに保存中...");
            if (dbManager == null)
            {
                Debug.LogError("dbManager が null です！");
                throw new Exception("dbManager がnullです。");
            }

            dbManager.SaveObjectWithFeatures(newObjectItem, categories);
            Debug.Log($"オブジェクト「{objectName}」をDBに保存しました。");

        }
        catch (Exception e)
        {
            Debug.LogError($"エラーが発生しました: {e.GetType().Name}");
            Debug.LogError($"エラーメッセージ: {e.Message}");
            Debug.LogError($"スタックトレース: {e.StackTrace}");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }
        finally
        {
            snapshotMat?.Dispose();
        }

        // パネル遷移処理
        Debug.Log("パネル遷移を実行中...");
        if (namingPanel != null) namingPanel.SetActive(false);
        if (talkSelectionPanel != null) talkSelectionPanel.SetActive(true);
        if (objectMatcherRootPanel != null) objectMatcherRootPanel.SetActive(false);
        Debug.Log("登録処理が完了しました！");
    }
    #endregion

    #region Style ScrollView
    void OnCategoryChanged(bool isOn)
    {
        if (!isOn) return;
        if (eyeToggle.isOn)
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
            Debug.LogError("Style ScrollView references not set!");
            return;
        }
        // Clear existing buttons
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
            GameObject buttonGO = Instantiate(styleButtonPrefab, styleButtonParent);
            Image buttonImage = buttonGO.GetComponent<Image>();
            Button buttonComp = buttonGO.GetComponent<Button>();
            if (buttonImage != null) buttonImage.sprite = stylesToShow[i];
            if (buttonComp != null)
            {
                int styleId = i; // Local copy for the closure
                if (isEyes)
                {
                    buttonComp.onClick.AddListener(() => SelectEyeStyle(styleId));
                }
                else
                {
                    buttonComp.onClick.AddListener(() => SelectMouthStyle(styleId));
                }
            }
        }
        if (styleScrollViewRect != null) styleScrollViewRect.horizontalNormalizedPosition = 0; // Reset scroll position
    }
    #endregion

    #region Recognition Logic
    private void LoadRegisteredObjectsFromDatabase()
    {
        if (dbManager == null) { Debug.LogError("DatabaseManager is null!"); return; } // Null check
        User currentUser = IntegratedAuthManager.Instance?.CurrentUser; // Null check on Instance
        if (currentUser == null)
        {
            Debug.LogWarning("User not logged in, cannot load detection data from DB.");
            database.Clear(); // Clear list if not logged in
            return;
        }

        List<ObjectItem> userObjects = dbManager.GetAllUserObjectsWithDetails(currentUser.user_id);
        database.Clear(); // Clear before loading

        if (userObjects == null)
        {
            Debug.LogWarning("GetAllUserObjectsWithDetails returned null.");
            return;
        }


        foreach (var item in userObjects)
        {
            if (item == null || item.FeatureVectorBlob == null || item.FeatureVectorBlob.Length == 0) continue; // More null checks

            try // Add try-catch for robustness during deserialization
            {
                float[] featureVector = new float[item.FeatureVectorBlob.Length / sizeof(float)];
                Buffer.BlockCopy(item.FeatureVectorBlob, 0, featureVector, 0, item.FeatureVectorBlob.Length);

                KeyPoint[] keypoints = DeserializeKeyPoints(item.KeyPointsBlob);
                Mat descriptors = DeserializeMat(item.DescriptorsBlob);

                // Check if deserialization succeeded
                if (keypoints == null || descriptors == null)
                {
                    Debug.LogWarning($"Failed to deserialize features for object: {item.Name}. Skipping.");
                    continue;
                }


                Vector2 eyePos = new Vector2(item.EyePositionX, item.EyePositionY);
                Vector2 mouthPos = new Vector2(item.MouthPositionX, item.MouthPositionY);

                OpenCvSharp.Size imageSize = new OpenCvSharp.Size(item.ImageWidth, item.ImageHeight);


                database.Add(new RegisteredObject
                {
                    Name = item.Name,
                    FeatureVector = featureVector,
                    Keypoints = keypoints,
                    Descriptors = descriptors,
                    RelativeEyePosition = eyePos,
                    RelativeMouthPosition = mouthPos,
                    ImageSize = imageSize,
                    EyeStyleId = item.EyeStyleId,
                    MouthStyleId = item.MouthStyleId
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading object '{item.Name}' from DB: {e.Message}");
            }
        }
        Debug.Log($"Loaded {database.Count} object data from database for detection.");
    }

    private bool FindObject()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying || database == null || database.Count == 0) return false;

        float[] currentVector = GetFeatureVectorFromTexture(webcamTexture);
        if (currentVector == null) return false;

        float bestSimilarity = -1f;
        RegisteredObject potentialMatch = null;
        foreach (var obj in database)
        {
            if (obj?.FeatureVector == null) continue; // Null check
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

                if (liveKeypoints != null && liveKeypoints.Length > 20 && potentialMatch.Descriptors != null && !potentialMatch.Descriptors.Empty())
                {
                    var matches = matcher.KnnMatch(potentialMatch.Descriptors, liveDescriptors, 2);
                    var goodMatches = matches.Where(m => m != null && m.Length >= 2 && m[0].Distance < 0.75 * m[1].Distance).Select(m => m[0]).ToList();
                    if (goodMatches.Count > 20)
                    {
                        var srcPts = goodMatches.Select(m => potentialMatch.Keypoints[m.QueryIdx].Pt).ToArray();
                        var dstPts = goodMatches.Select(m => liveKeypoints[m.TrainIdx].Pt).ToArray();
                        homography = Cv2.FindHomography(InputArray.Create(srcPts), InputArray.Create(dstPts), HomographyMethods.Ransac, 5.0);
                        if (homography != null && !homography.Empty())
                        {
                            UpdateFacialFeatures(potentialMatch, homography);
                            if (resultText != null) resultText.text = $"発見: {potentialMatch.Name}!";
                            return true; // Return early on success
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during FindObject feature matching: {e.Message}");
            }
            finally // Ensure Mats are disposed
            {
                homography?.Dispose();
                liveFrameMat?.Dispose();
                liveDescriptors?.Dispose();
            }
        }

        // Reset UI if no match found or match not good enough
        if (resultText != null) resultText.text = "捜索中...";
        if (recognitionEyeImage != null) recognitionEyeImage.gameObject.SetActive(false);
        if (recognitionMouthImage != null) recognitionMouthImage.gameObject.SetActive(false);
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
            // Use PeekOutput to avoid disposing tensor managed by Barracuda
            Tensor outputTensor = worker.PeekOutput();
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
            tensor?.Dispose(); // Dispose the input tensor we created
        }
        return featureVector;
    }

    private void UpdateFacialFeatures(RegisteredObject obj, Mat homography)
    {
        if (obj.EyeStyleId != -1 && recognitionEyeImage != null && obj.EyeStyleId >= 0 && obj.EyeStyleId < eyeStyles.Count) // Index check
        {
            recognitionEyeImage.sprite = eyeStyles[obj.EyeStyleId];
            UpdatePartPosition(obj, homography, obj.RelativeEyePosition, recognitionEyeImage.rectTransform);
            recognitionEyeImage.gameObject.SetActive(true);
        }
        else if (recognitionEyeImage != null)
        {
            recognitionEyeImage.gameObject.SetActive(false);
        }
        if (obj.MouthStyleId != -1 && recognitionMouthImage != null && obj.MouthStyleId >= 0 && obj.MouthStyleId < mouthStyles.Count) // Index check
        {
            recognitionMouthImage.sprite = mouthStyles[obj.MouthStyleId];
            UpdatePartPosition(obj, homography, obj.RelativeMouthPosition, recognitionMouthImage.rectTransform);
            recognitionMouthImage.gameObject.SetActive(true);
        }
        else if (recognitionMouthImage != null)
        {
            recognitionMouthImage.gameObject.SetActive(false);
        }
    }

    private void UpdatePartPosition(RegisteredObject obj, Mat homography, Vector2 relativePos, RectTransform partTransform)
    {
        if (talkCameraDisplay == null || webcamTexture == null || homography == null || homography.Empty() || obj == null || obj.ImageSize.Width <= 0 || obj.ImageSize.Height <= 0 || partTransform == null)
        {
            return; // More robust guard clauses
        }

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

            // Convert projectedPos (pixel coords on texture) to local position within RawImage rect
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

            // Debug log removed for brevity, uncomment if needed
            // Debug.Log($"{partTransform.name} - Calculated LocalPos: ({localPos.x:F0}, {localPos.y:F0})");

            partTransform.anchoredPosition = localPos;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during UpdatePartPosition calculation: {e.ToString()}"); // Use ToString for stack trace
        }
    }

    private float CalculateCosineSimilarity(float[] vecA, float[] vecB)
    {
        if (vecA == null || vecB == null || vecA.Length != vecB.Length) return 0;

        float dotProduct = 0.0f;
        float magnitudeA = 0.0f;
        float magnitudeB = 0.0f;
        int len = vecA.Length; // Cache length
        for (int i = 0; i < len; i++)
        {
            dotProduct += vecA[i] * vecB[i];
            magnitudeA += vecA[i] * vecA[i];
            magnitudeB += vecB[i] * vecB[i];
        }

        // Avoid division by zero if magnitude is zero
        if (magnitudeA == 0 || magnitudeB == 0) return 0;

        magnitudeA = Mathf.Sqrt(magnitudeA);
        magnitudeB = Mathf.Sqrt(magnitudeB);

        // Check for NaN which can happen with invalid sqrt results, though unlikely here
        if (float.IsNaN(magnitudeA) || float.IsNaN(magnitudeB)) return 0;

        return dotProduct / (magnitudeA * magnitudeB);
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
                // Add null checks for safety? Unlikely needed for KeyPoint itself
                writer.Write(kp.Pt.X); writer.Write(kp.Pt.Y);
                writer.Write(kp.Size); writer.Write(kp.Angle);
                writer.Write(kp.Response); writer.Write(kp.Octave);
            }
            return ms.ToArray();
        }
    }

    private KeyPoint[] DeserializeKeyPoints(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return new KeyPoint[0]; // Return empty array, not null
        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
                // Check if there's enough data for the length
                if (ms.Length - ms.Position < sizeof(int)) return new KeyPoint[0];

                var len = reader.ReadInt32();
                if (len < 0 || len > bytes.Length) return new KeyPoint[0]; // Basic sanity check
                if (len == 0) return new KeyPoint[0];

                // Check if there's enough data for all keypoints
                // Size of one KeyPoint data (approx): 6 * float + 1 * int = 6*4 + 4 = 28 bytes
                if (ms.Length - ms.Position < len * 28) return new KeyPoint[0]; // Rough estimate

                var keyPoints = new KeyPoint[len];
                for (int i = 0; i < len; i++)
                {
                    var x = reader.ReadSingle(); var y = reader.ReadSingle();
                    var size = reader.ReadSingle(); var angle = reader.ReadSingle();
                    var response = reader.ReadSingle(); var octave = reader.ReadInt32();
                    keyPoints[i] = new KeyPoint(new Point2f(x, y), size, angle, response, octave);
                }
                return keyPoints;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error deserializing KeyPoints: {e.Message}");
            return new KeyPoint[0]; // Return empty on error
        }
    }

    private byte[] SerializeMat(Mat mat)
    {
        if (mat == null || mat.Empty()) return null;
        try
        {
            var size = mat.Total() * mat.ElemSize();
            if (size <= 0 || size > int.MaxValue) return null; // Check for invalid size
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
            Debug.LogError($"Error serializing Mat: {e.Message}");
            return null;
        }
    }

    private Mat DeserializeMat(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
                // Check if there's enough data for header
                if (ms.Length - ms.Position < sizeof(int) * 4) return null;

                var rows = reader.ReadInt32();
                var cols = reader.ReadInt32();
                var typeValue = reader.ReadInt32();
                var len = reader.ReadInt32();

                // Basic sanity checks
                if (rows < 0 || cols < 0 || len < 0 || len > bytes.Length) return null;
                if (ms.Length - ms.Position < len) return null; // Check if enough data for payload

                MatType type;
                try { type = (MatType)typeValue; } catch { return null; } // Ensure type is valid


                var data = reader.ReadBytes(len);
                var mat = new Mat(rows, cols, type);

                // Check if mat dimensions match data length
                long expectedSize = mat.Total() * mat.ElemSize();
                if (expectedSize != len)
                {
                    mat.Dispose(); // Dispose incorrectly sized Mat
                    return null;
                }


                if (data.Length > 0) Marshal.Copy(data, 0, mat.Data, data.Length);
                return mat;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error deserializing Mat: {e.Message}");
            return null;
        }
    }
    #endregion
}