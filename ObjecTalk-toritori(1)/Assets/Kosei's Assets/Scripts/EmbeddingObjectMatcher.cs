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

    private IWorker worker;
    private AKAZE featureDetector;
    private BFMatcher matcher;

    private WebCamTexture webcamTexture;
    private float lastCompareTime = 0f;

    // Initialize in Awake()
    void Awake()
    {
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

        // Register listeners
        registerStartButton.onClick.AddListener(OnRegisterStart);
        nextButton.onClick.AddListener(OnPlacementConfirmed);
        backButton.onClick.AddListener(OnBackButtonClicked);
        eyeToggle.onValueChanged.AddListener(OnCategoryChanged);
        mouthToggle.onValueChanged.AddListener(OnCategoryChanged);
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
        snapshotDisplay_Placement.sprite = Sprite.Create(snapshot, new UnityEngine.Rect(0, 0, snapshot.width, snapshot.height), new Vector2(0.5f, 0.5f));

        mouthToggle.isOn = false;
        eyeToggle.isOn = true;
        placementEyeImage.gameObject.SetActive(false);
        placementMouthImage.gameObject.SetActive(false);
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
        if (confirmButton != null) { confirmButton.interactable = true; }
        tempSnapshot = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
        tempSnapshot.SetPixels(webcamTexture.GetPixels());
        tempSnapshot.Apply();

        // Don't stop camera here; let OnDisable handle it

        if (registerCameraPanel != null) registerCameraPanel.SetActive(false);
        if (placementPanel != null) placementPanel.SetActive(true);

        SwitchToPlacementMode(tempSnapshot);
    }

    public void OnPlacementConfirmed()
    {
        SwitchToNamingMode();
    }


    public void OnBackButtonClicked()
    {
        if (currentMode == AppMode.Recognition)
        {
            // Return from Camera Panel (Register or Talk) to Mode Selection Panel

            // 1. Show the mode selection panel
            if (talkSelectionPanel != null)
            {
                talkSelectionPanel.SetActive(true);
            }
            else
            {
                Debug.LogError("talkSelectionPanel is not set!");
            }

            // 2. Hide the parent camera panel (this will trigger OnDisable)
            if (objectMatcherRootPanel != null)
            {
                objectMatcherRootPanel.SetActive(false);
            }
            else
            {
                Debug.LogError("objectMatcherRootPanel is not set!");
            }
        }
        else if (currentMode == AppMode.Naming)
        {
            // Return from Naming -> Placement
            currentMode = AppMode.Placement;
            if (namingPanel != null) namingPanel.SetActive(false);
            if (placementPanel != null) placementPanel.SetActive(true);
        }
        else if (currentMode == AppMode.Placement)
        {
            // Return from Placement -> RegisterCameraPanel
            currentMode = AppMode.Recognition;
            if (placementPanel != null) placementPanel.SetActive(false);

            if (registerCameraPanel != null) registerCameraPanel.SetActive(true);
            // Ensure talk panel is hidden if returning here
            if (talkCameraPanel != null) talkCameraPanel.SetActive(false);

            DesiredMode = StartupMode.Register; // Reset mode just in case
            // No need to Play camera; OnEnable handles it if needed
        }
    }

    // --- [Modified Final Part] ---
    public void OnConfirmRegistration()
    {
        Debug.Log("OnConfirmRegistration が呼び出されました！");

        if (confirmButton != null) confirmButton.interactable = false;

        string objectName = nameInputField.text;
        string objectCategory = (categoryInputField != null) ? categoryInputField.text : "Default";

        Debug.Log($"入力された名前: '{objectName}', スナップショットは null?: {tempSnapshot == null}");

        if (string.IsNullOrEmpty(objectName) || tempSnapshot == null)
        {
            Debug.LogWarning("名前が空かスナップショットがnullのため中断します。");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        Debug.Log("名前とスナップショットのチェックを通過。CurrentUserを取得します...");
        User currentUser = IntegratedAuthManager.Instance.CurrentUser;
        Debug.Log($"CurrentUser は null?: {currentUser == null}");

        if (currentUser == null)
        {
            Debug.LogError("User not logged in. Cannot register object. Redirecting to Login.");
            // ユーザーにエラーメッセージを表示する処理（任意）
            // ShowSomeErrorMessage("セッションが切れました。ログインしてください。");

            // ログインシーンをロード
            UnityEngine.SceneManagement.SceneManager.LoadScene("LoginScene"); // シーン名を要確認

            // ボタンを再度有効にする（エラー表示する場合）
            // if (confirmButton != null) confirmButton.interactable = true;
            return; // 登録処理を中断
        }

        Debug.Log("CurrentUserのチェックを通過。特徴量抽出とDB保存に進みます...");

        // snapshotMat を try の外で宣言し、finally で Dispose できるようにする
        Mat snapshotMat = null;

        try
        {
            float[] featureVector = GetFeatureVectorFromTexture(tempSnapshot);
            if (featureVector == null) throw new Exception("特徴量ベクトルがnullです。GetFeatureVectorFromTexture を確認してください。");

            snapshotMat = OpenCvSharp.Unity.TextureToMat(tempSnapshot); // try の中で初期化
            KeyPoint[] keypoints;
            Mat descriptors = new Mat();
            featureDetector.DetectAndCompute(snapshotMat, null, out keypoints, descriptors);

            if (keypoints == null) throw new Exception("キーポイントがnullです。DetectAndCompute を確認してください。");
            if (descriptors == null || descriptors.Empty()) throw new Exception("ディスクリプタがnullまたは空です。DetectAndCompute を確認してください。");


            byte[] featureVectorBytes = new byte[featureVector.Length * sizeof(float)];
            Buffer.BlockCopy(featureVector, 0, featureVectorBytes, 0, featureVectorBytes.Length);
            byte[] keypointsBytes = SerializeKeyPoints(keypoints);
            byte[] descriptorsBytes = SerializeMat(descriptors);

            Vector2 relativeEyePos = new Vector2(-1, -1);
            if (placementEyeImage != null && placementEyeImage.gameObject.activeInHierarchy)
            {
                Vector2 localEyePoint = snapshotDisplay_Placement.rectTransform.InverseTransformPoint(placementEyeImage.position);
                relativeEyePos = new Vector2(
                   (localEyePoint.x - snapshotDisplay_Placement.rectTransform.rect.xMin) / snapshotDisplay_Placement.rectTransform.rect.width,
                   (localEyePoint.y - snapshotDisplay_Placement.rectTransform.rect.yMin) / snapshotDisplay_Placement.rectTransform.rect.height);
            }

            Vector2 relativeMouthPos = new Vector2(-1, -1);
            if (placementMouthImage != null && placementMouthImage.gameObject.activeInHierarchy)
            {
                Vector2 localMouthPoint = snapshotDisplay_Placement.rectTransform.InverseTransformPoint(placementMouthImage.position);
                relativeMouthPos = new Vector2(
                   (localMouthPoint.x - snapshotDisplay_Placement.rectTransform.rect.xMin) / snapshotDisplay_Placement.rectTransform.rect.width,
                   (localMouthPoint.y - snapshotDisplay_Placement.rectTransform.rect.yMin) / snapshotDisplay_Placement.rectTransform.rect.height);
            }

            // database リストへの追加 (メモリ上)
            database.Add(new RegisteredObject
            {
                Name = objectName,
                FeatureVector = featureVector,
                Keypoints = keypoints, // keypoints は Clone しなくても良い場合が多い
                Descriptors = descriptors.Clone(), // descriptors は Clone 推奨
                ImageSize = snapshotMat.Size(),
                RelativeEyePosition = relativeEyePos,
                RelativeMouthPosition = relativeMouthPos,
                EyeStyleId = currentEyeId,
                MouthStyleId = currentMouthId
            });

            // DB保存用のデータ作成
            var newObjectItem = new ObjectItem
            {
                object_id = Guid.NewGuid().ToString(),
                user_id = currentUser.user_id,
                Name = objectName,
                ImageData = tempSnapshot.EncodeToJPG(),
                FeatureVectorBlob = featureVectorBytes,
                KeyPointsBlob = keypointsBytes,
                DescriptorsBlob = descriptorsBytes,
                EyePositionX = relativeEyePos.x,
                EyePositionY = relativeEyePos.y,
                MouthPositionX = relativeMouthPos.x,
                MouthPositionY = relativeMouthPos.y,
                ImageWidth = snapshotMat.Size().Width,
                ImageHeight = snapshotMat.Size().Height,
                EyeStyleId = currentEyeId,
                MouthStyleId = currentMouthId
            };

            List<string> categories = new List<string>();
            if (!string.IsNullOrEmpty(objectCategory))
            {
                categories.Add(objectCategory.Trim());
            }

            dbManager.SaveObjectWithFeatures(newObjectItem, categories); // DB保存実行

            Debug.Log($"オブジェクト「{objectName}」とAIデータをDBに保存しました。"); // 成功ログ

        }
        catch (Exception e) // ★★★ エラーログ出力 ★★★
        {
            Debug.LogError($"データベースへの保存または特徴量抽出中にエラーが発生しました: {e.ToString()}"); // エラー詳細を出力
            if (confirmButton != null) confirmButton.interactable = true; // ボタンを再度有効にする
            return; // ★★★ エラー時はここで処理中断 ★★★
        }
        finally // tryが成功しても失敗しても実行される
        {
            snapshotMat?.Dispose(); // snapshotMatがnullでなければ解放
        }


        // --- [MODIFIED PART: Return to TalkSelectionPanel] ---

        // 1. Hide the Naming Panel
        if (namingPanel != null) namingPanel.SetActive(false);

        // 2. Show the Talk Selection Panel
        if (talkSelectionPanel != null)
        {
            talkSelectionPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("talkSelectionPanel is not set!");
        }

        // 3. Hide the parent camera panel (this triggers OnDisable to stop camera)
        if (objectMatcherRootPanel != null)
        {
            objectMatcherRootPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("objectMatcherRootPanel is not set!");
        }

        // --- [End of MODIFIED PART] ---
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
        foreach (Transform child in styleButtonParent)
        {
            Destroy(child.gameObject);
        }
        List<Sprite> stylesToShow = isEyes ? eyeStyles : mouthStyles;
        for (int i = 0; i < stylesToShow.Count; i++)
        {
            GameObject buttonGO = Instantiate(styleButtonPrefab, styleButtonParent);
            buttonGO.GetComponent<Image>().sprite = stylesToShow[i];
            int styleId = i;
            if (isEyes)
            {
                buttonGO.GetComponent<Button>().onClick.AddListener(() => SelectEyeStyle(styleId));
            }
            else
            {
                buttonGO.GetComponent<Button>().onClick.AddListener(() => SelectMouthStyle(styleId));
            }
        }
        styleScrollViewRect.horizontalNormalizedPosition = 0;
    }
    #endregion

    #region Recognition Logic
    private void LoadRegisteredObjectsFromDatabase()
    {
        User currentUser = IntegratedAuthManager.Instance.CurrentUser;
        if (currentUser == null)
        {
            Debug.LogWarning("User not logged in, cannot load detection data from DB.");
            return;
        }

        List<ObjectItem> userObjects = dbManager.GetAllUserObjectsWithDetails(currentUser.user_id);
        database.Clear();

        foreach (var item in userObjects)
        {
            if (item.FeatureVectorBlob == null || item.FeatureVectorBlob.Length == 0) continue;

            float[] featureVector = new float[item.FeatureVectorBlob.Length / sizeof(float)];
            Buffer.BlockCopy(item.FeatureVectorBlob, 0, featureVector, 0, item.FeatureVectorBlob.Length);

            KeyPoint[] keypoints = DeserializeKeyPoints(item.KeyPointsBlob);
            Mat descriptors = DeserializeMat(item.DescriptorsBlob);

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
        Debug.Log($"Loaded {database.Count} object data from database for detection.");
    }

    private bool FindObject()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying) return false; // Guard clause

        float[] currentVector = GetFeatureVectorFromTexture(webcamTexture);
        if (currentVector == null) return false; // Guard clause

        float bestSimilarity = -1f;
        RegisteredObject potentialMatch = null;
        foreach (var obj in database)
        {
            if (obj.FeatureVector == null) continue;
            float similarity = CalculateCosineSimilarity(currentVector, obj.FeatureVector);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                potentialMatch = obj;
            }
        }

        if (bestSimilarity > similarityThreshold && potentialMatch != null)
        {
            Mat liveFrameMat = OpenCvSharp.Unity.TextureToMat(webcamTexture);
            KeyPoint[] liveKeypoints;
            Mat liveDescriptors = new Mat();
            featureDetector.DetectAndCompute(liveFrameMat, null, out liveKeypoints, liveDescriptors);

            if (liveKeypoints != null && liveKeypoints.Length > 20 && potentialMatch.Descriptors != null && !potentialMatch.Descriptors.Empty())
            {
                var matches = matcher.KnnMatch(potentialMatch.Descriptors, liveDescriptors, 2);
                var goodMatches = matches.Where(m => m != null && m.Length >= 2 && m[0].Distance < 0.75 * m[1].Distance).Select(m => m[0]).ToList();
                if (goodMatches.Count > 20)
                {
                    var srcPts = goodMatches.Select(m => potentialMatch.Keypoints[m.QueryIdx].Pt).ToArray();
                    var dstPts = goodMatches.Select(m => liveKeypoints[m.TrainIdx].Pt).ToArray();
                    Mat homography = Cv2.FindHomography(InputArray.Create(srcPts), InputArray.Create(dstPts), HomographyMethods.Ransac, 5.0);
                    if (homography != null && !homography.Empty())
                    {
                        UpdateFacialFeatures(potentialMatch, homography);
                        if (resultText != null) resultText.text = $"発見: {potentialMatch.Name}!";
                        homography.Dispose();
                        liveFrameMat.Dispose();
                        liveDescriptors.Dispose();
                        return true;
                    }
                    if (homography != null) homography.Dispose(); // Dispose even if empty
                }
            }
            liveFrameMat.Dispose();
            liveDescriptors.Dispose();
        }

        // Reset UI if no match found or match not good enough
        if (resultText != null) resultText.text = "捜索中...";
        if (recognitionEyeImage != null) recognitionEyeImage.gameObject.SetActive(false);
        if (recognitionMouthImage != null) recognitionMouthImage.gameObject.SetActive(false);
        return false;
    }

    private float[] GetFeatureVectorFromTexture(Texture inputTexture)
    {
        if (inputTexture == null || worker == null) return null; // Guard clauses
        RenderTexture tempRT = RenderTexture.GetTemporary(224, 224, 0, RenderTextureFormat.Default);
        Graphics.Blit(inputTexture, tempRT);
        Tensor tensor = null;
        Tensor outputTensor = null;
        float[] featureVector = null;
        try
        {
            tensor = new Tensor(tempRT, 3);
            worker.Execute(tensor);
            outputTensor = worker.PeekOutput();
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
            // Do not dispose outputTensor here if using PeekOutput, Barracuda manages it.
        }
        return featureVector;
    }

    private void UpdateFacialFeatures(RegisteredObject obj, Mat homography)
    {
        if (obj.EyeStyleId != -1 && recognitionEyeImage != null && obj.EyeStyleId < eyeStyles.Count)
        {
            recognitionEyeImage.sprite = eyeStyles[obj.EyeStyleId];
            UpdatePartPosition(obj, homography, obj.RelativeEyePosition, recognitionEyeImage.rectTransform);
            recognitionEyeImage.gameObject.SetActive(true);
        }
        else if (recognitionEyeImage != null)
        {
            recognitionEyeImage.gameObject.SetActive(false);
        }
        if (obj.MouthStyleId != -1 && recognitionMouthImage != null && obj.MouthStyleId < mouthStyles.Count)
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
        if (talkCameraDisplay == null || webcamTexture == null || homography == null || homography.Empty()) return; // Guard clauses

        Point2f[] srcCorners = { new Point2f(0, 0), new Point2f(obj.ImageSize.Width, 0), new Point2f(obj.ImageSize.Width, obj.ImageSize.Height), new Point2f(0, obj.ImageSize.Height) };
        Point2f[] dstCornersF = Cv2.PerspectiveTransform(srcCorners, homography);
        if (dstCornersF == null || dstCornersF.Length != 4) return; // Check transform result

        Vector2[] dstCorners = new Vector2[4];
        for (int i = 0; i < 4; i++) { dstCorners[i] = new Vector2(dstCornersF[i].X, dstCornersF[i].Y); }

        // Clamp relativePos to avoid issues with Lerp if values are outside 0-1 range
        relativePos.x = Mathf.Clamp01(relativePos.x);
        relativePos.y = Mathf.Clamp01(relativePos.y);

        Vector2 topEdge = Vector2.Lerp(dstCorners[0], dstCorners[1], relativePos.x);
        Vector2 bottomEdge = Vector2.Lerp(dstCorners[3], dstCorners[2], relativePos.x);
        Vector2 projectedPos = Vector2.Lerp(topEdge, bottomEdge, 1 - relativePos.y); // Y is often inverted

        Vector3[] cameraWorldCorners = new Vector3[4];
        talkCameraDisplay.rectTransform.GetWorldCorners(cameraWorldCorners);

        // Normalize projected position relative to the camera texture dimensions
        float normalizedX = projectedPos.x / webcamTexture.width;
        float normalizedY = 1 - (projectedPos.y / webcamTexture.height); // Y might be inverted

        // Clamp normalized coordinates to prevent positioning outside the RawImage bounds
        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedY = Mathf.Clamp01(normalizedY);

        // Interpolate within the world corners of the RawImage
        Vector3 finalPosition = Vector3.Lerp(
            Vector3.Lerp(cameraWorldCorners[0], cameraWorldCorners[3], normalizedY), // Lerp vertically first based on Y
            Vector3.Lerp(cameraWorldCorners[1], cameraWorldCorners[2], normalizedY), // Lerp vertically first based on Y
            normalizedX); // Then Lerp horizontally based on X

        partTransform.position = finalPosition;
    }

    private float CalculateCosineSimilarity(float[] vecA, float[] vecB)
    {
        if (vecA == null || vecB == null || vecA.Length != vecB.Length) return 0; // Guard clauses

        float dotProduct = 0.0f;
        float magnitudeA = 0.0f;
        float magnitudeB = 0.0f;
        for (int i = 0; i < vecA.Length; i++)
        {
            dotProduct += vecA[i] * vecB[i];
            magnitudeA += vecA[i] * vecA[i];
            magnitudeB += vecB[i] * vecB[i];
        }
        magnitudeA = Mathf.Sqrt(magnitudeA);
        magnitudeB = Mathf.Sqrt(magnitudeB);
        if (magnitudeA == 0 || magnitudeB == 0) return 0;
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
        using (var ms = new MemoryStream(bytes))
        using (var reader = new BinaryReader(ms))
        {
            var len = reader.ReadInt32();
            if (len == 0) return new KeyPoint[0];
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

    private byte[] SerializeMat(Mat mat)
    {
        if (mat == null || mat.Empty()) return null;
        var size = mat.Total() * mat.ElemSize();
        if (size == 0) return null;
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

    private Mat DeserializeMat(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        using (var ms = new MemoryStream(bytes))
        using (var reader = new BinaryReader(ms))
        {
            var rows = reader.ReadInt32();
            var cols = reader.ReadInt32();
            var type = (MatType)reader.ReadInt32();
            var len = reader.ReadInt32();
            var data = reader.ReadBytes(len);
            var mat = new Mat(rows, cols, type);
            if (data.Length > 0) Marshal.Copy(data, 0, mat.Data, data.Length);
            return mat;
        }
    }
    #endregion
}