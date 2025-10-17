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
    // フロー変更: Recognition -> Placement -> Naming
    private enum AppMode { Recognition, Naming, Placement }
    private AppMode currentMode = AppMode.Recognition;
    private DatabaseManager dbManager;

    [Header("AIモデル")]
    public NNModel modelAsset;

    [Header("パーツのスタイル")]
    public List<Sprite> eyeStyles;
    public List<Sprite> mouthStyles;

    [Header("UI要素")]
    public FlexibleUIManager uiManager;
    public GameObject recognitionPanel;
    public GameObject namingPanel;
    public GameObject placementPanel;
    public RawImage cameraDisplay;
    public Image snapshotDisplay_Naming;
    public Image snapshotDisplay_Placement;
    public Button registerStartButton;
    public Button nextButton; // Placement -> Naming へのボタン
    public Button confirmButton; // Naming -> Register へのボタン
    public Button backButton;
    public TMP_InputField nameInputField;
    public TMP_InputField categoryInputField; // <-- [変更] カテゴリ入力欄を追加
    public TMP_Text resultText;

    [Header("登録モード用UI")]
    public RectTransform placementEyeImage;
    public RectTransform placementMouthImage;

    [Header("認識モード用UI")]
    public Image recognitionEyeImage;
    public Image recognitionMouthImage;

    [Header("スタイル選択UI")]
    public ScrollRect styleScrollViewRect;
    public GameObject styleButtonPrefab;
    public Transform styleButtonParent;
    public Toggle eyeToggle;
    public Toggle mouthToggle;

    [Header("認識設定")]
    [Range(0.5f, 1.0f)] public float similarityThreshold = 0.85f;
    [Range(0.1f, 2.0f)] public float compareInterval = 0.2f;

    private List<RegisteredObject> database = new List<RegisteredObject>();
    private Texture2D tempSnapshot;
    // private string tempObjectName; // <-- [変更] 登録直前にInputFieldから読むため不要
    private int currentEyeId = -1;
    private int currentMouthId = -1;

    private IWorker worker;
    private AKAZE featureDetector;
    private BFMatcher matcher;

    private WebCamTexture webcamTexture;
    private float lastCompareTime = 0f;

    void Start()
    {
        dbManager = new DatabaseManager();

        var model = ModelLoader.Load(modelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
        featureDetector = AKAZE.Create();
        matcher = new BFMatcher(NormTypes.Hamming);

        if (WebCamTexture.devices.Length > 0)
        {
            webcamTexture = new WebCamTexture(WebCamTexture.devices[0].name, 1280, 720, 30);
            cameraDisplay.texture = webcamTexture;
            webcamTexture.Play();
        }

        LoadRegisteredObjectsFromDatabase();

        registerStartButton.onClick.AddListener(OnRegisterStart);
        // nextButton.onClick.AddListener(OnNameConfirmed); // <-- [変更] 
        nextButton.onClick.AddListener(OnPlacementConfirmed); // <-- [変更] Placement -> Naming への遷移
        // confirmButtonはInspectorで設定 (OnConfirmRegistration)
        backButton.onClick.AddListener(OnBackButtonClicked);
        eyeToggle.onValueChanged.AddListener(OnCategoryChanged);
        mouthToggle.onValueChanged.AddListener(OnCategoryChanged);

        if (uiManager != null)
        {
            uiManager.ShowPanel(recognitionPanel);
            currentMode = AppMode.Recognition;
        }
    }

    void Update()
    {
        if (currentMode == AppMode.Recognition && webcamTexture != null && webcamTexture.didUpdateThisFrame && database.Count > 0)
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
        // if (nextButton != null) nextButton.onClick.RemoveListener(OnNameConfirmed); // <-- [変更]
        if (nextButton != null) nextButton.onClick.RemoveListener(OnPlacementConfirmed); // <-- [変更]
        if (backButton != null) backButton.onClick.RemoveListener(OnBackButtonClicked);
        if (eyeToggle != null) eyeToggle.onValueChanged.RemoveListener(OnCategoryChanged);
        if (mouthToggle != null) mouthToggle.onValueChanged.RemoveListener(OnCategoryChanged);

        worker?.Dispose();
        matcher?.Dispose();
        featureDetector?.Dispose();
        if (webcamTexture != null) webcamTexture.Stop();
    }

    #region Mode Switching
    // [変更] NamingModeへの切り替え。スナップショットはtempSnapshotを流用
    private void SwitchToNamingMode()
    {
        currentMode = AppMode.Naming;
        if (tempSnapshot == null)
        {
            Debug.LogError("Snapshot is missing!");
            return;
        }
        var sprite = Sprite.Create(tempSnapshot, new UnityEngine.Rect(0, 0, tempSnapshot.width, tempSnapshot.height), new Vector2(0.5f, 0.5f));
        snapshotDisplay_Naming.sprite = sprite;
        uiManager.ShowPanel(namingPanel);

        // 入力欄をクリア
        nameInputField.text = "";
        if (categoryInputField != null) categoryInputField.text = "";
    }

    // [変更] PlacementModeへの切り替え。スナップショットを引数で受け取る
    private void SwitchToPlacementMode(Texture2D snapshot)
    {
        currentMode = AppMode.Placement;
        snapshotDisplay_Placement.sprite = Sprite.Create(snapshot, new UnityEngine.Rect(0, 0, snapshot.width, snapshot.height), new Vector2(0.5f, 0.5f));
        uiManager.ShowPanel(placementPanel);
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

    // [変更] 写真撮影時 (Recognition -> Placement)
    public void OnRegisterStart()
    {
        if (confirmButton != null)
        {
            confirmButton.interactable = true;
        }
        tempSnapshot = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
        tempSnapshot.SetPixels(webcamTexture.GetPixels());
        tempSnapshot.Apply();

        // --- [変更] カメラを停止 ---
        if (webcamTexture != null)
        {
            webcamTexture.Stop();
        }
        cameraDisplay.texture = null; // 表示をクリア
        // ---------------------

        // SwitchToNamingMode(tempSnapshot); // <-- 古い処理
        SwitchToPlacementMode(tempSnapshot); // <-- [変更] PlacementModeへ
    }

    // [変更] OnNameConfirmed は削除し、OnPlacementConfirmed を新設
    // (Placement -> Naming)
    public void OnPlacementConfirmed()
    {
        // パーツ配置完了後、名前入力画面へ
        SwitchToNamingMode();
    }

    // [変更] 戻るボタンのフローを変更
    public void OnBackButtonClicked()
    {
        if (currentMode == AppMode.Recognition)
        {
            // ホーム画面に戻る処理 (変更なし)
            // 例: UnityEngine.SceneManagement.SceneManager.LoadScene("HomeScene");
        }
        else if (currentMode == AppMode.Naming)
        {
            // Naming -> Placement へ戻る
            currentMode = AppMode.Placement;
            uiManager.ShowPanel(placementPanel);
        }
        else if (currentMode == AppMode.Placement)
        {
            // Placement -> Recognition へ戻る
            currentMode = AppMode.Recognition;
            uiManager.ShowPanel(recognitionPanel);

            // --- [変更] カメラを再開 ---
            if (webcamTexture != null && !webcamTexture.isPlaying)
            {
                webcamTexture.Play();
                cameraDisplay.texture = webcamTexture;
            }
            // ---------------------
        }
    }

    // [変更] 登録実行時 (Naming -> Recognition)
    public void OnConfirmRegistration()
    {
        if (confirmButton != null) confirmButton.interactable = false;

        // --- [変更] 名前とカテゴリをここで取得 ---
        string objectName = nameInputField.text;
        string objectCategory = (categoryInputField != null) ? categoryInputField.text : "Default"; // カテゴリ欄がなければ"Default"
        // ---------------------------------

        // if (string.IsNullOrEmpty(tempObjectName) || tempSnapshot == null) // <-- 古いチェック
        if (string.IsNullOrEmpty(objectName) || tempSnapshot == null) // <-- [変更] 新しいチェック
        {
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        User currentUser = IntegratedAuthManager.Instance.CurrentUser;
        if (currentUser == null)
        {
            Debug.LogError("ログイン情報が見つかりません。オブジェクトを登録できませんでした。");
            if (confirmButton != null) confirmButton.interactable = true;
            return;
        }

        float[] featureVector = GetFeatureVectorFromTexture(tempSnapshot);
        Mat snapshotMat = OpenCvSharp.Unity.TextureToMat(tempSnapshot);
        KeyPoint[] keypoints;
        Mat descriptors = new Mat();
        featureDetector.DetectAndCompute(snapshotMat, null, out keypoints, descriptors);

        byte[] featureVectorBytes = new byte[featureVector.Length * sizeof(float)];
        Buffer.BlockCopy(featureVector, 0, featureVectorBytes, 0, featureVectorBytes.Length);
        byte[] keypointsBytes = SerializeKeyPoints(keypoints);
        byte[] descriptorsBytes = SerializeMat(descriptors);

        Vector2 relativeEyePos = new Vector2(-1, -1);
        if (placementEyeImage.gameObject.activeInHierarchy)
        {
            Vector2 localEyePoint = snapshotDisplay_Placement.rectTransform.InverseTransformPoint(placementEyeImage.position);
            relativeEyePos = new Vector2(
                (localEyePoint.x - snapshotDisplay_Placement.rectTransform.rect.xMin) / snapshotDisplay_Placement.rectTransform.rect.width,
                (localEyePoint.y - snapshotDisplay_Placement.rectTransform.rect.yMin) / snapshotDisplay_Placement.rectTransform.rect.height);
        }

        Vector2 relativeMouthPos = new Vector2(-1, -1);
        if (placementMouthImage.gameObject.activeInHierarchy)
        {
            Vector2 localMouthPoint = snapshotDisplay_Placement.rectTransform.InverseTransformPoint(placementMouthImage.position);
            relativeMouthPos = new Vector2(
                (localMouthPoint.x - snapshotDisplay_Placement.rectTransform.rect.xMin) / snapshotDisplay_Placement.rectTransform.rect.width,
                (localMouthPoint.y - snapshotDisplay_Placement.rectTransform.rect.yMin) / snapshotDisplay_Placement.rectTransform.rect.height);
        }

        database.Add(new RegisteredObject
        {
            // Name = tempObjectName, // <-- 古い処理
            Name = objectName, // <-- [変更]
            FeatureVector = featureVector,
            Keypoints = keypoints,
            Descriptors = descriptors.Clone(),
            ImageSize = snapshotMat.Size(),
            RelativeEyePosition = relativeEyePos,
            RelativeMouthPosition = relativeMouthPos,
            EyeStyleId = currentEyeId,
            MouthStyleId = currentMouthId
        });

        var newObjectItem = new ObjectItem
        {
            object_id = Guid.NewGuid().ToString(),
            user_id = currentUser.user_id,
            // Name = tempObjectName, // <-- 古い処理
            Name = objectName, // <-- [変更]
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

        try
        {
            // --- [変更] カテゴリをリストとして渡す ---
            List<string> categories = new List<string>();
            if (!string.IsNullOrEmpty(objectCategory))
            {
                // 必要に応じてカンマ区切りなどでパースしてください
                // 例: categories = objectCategory.Split(',').Select(s => s.Trim()).ToList();
                categories.Add(objectCategory.Trim());
            }

            // dbManager.SaveObjectWithFeatures(newObjectItem, new List<string>()); // <-- 古い処理
            dbManager.SaveObjectWithFeatures(newObjectItem, categories); // <-- [変更]
            Debug.Log($"オブジェクト「{objectName}」とAIデータをDBに保存しました。"); // <-- [変更]
        }
        catch (Exception e)
        {
            Debug.LogError($"データベースへの保存に失敗しました: {e.Message}");
        }

        snapshotMat.Dispose();
        currentMode = AppMode.Recognition;
        uiManager.ShowPanel(recognitionPanel);

        // --- [変更] カメラを再開 ---
        if (webcamTexture != null && !webcamTexture.isPlaying)
        {
            webcamTexture.Play();
            cameraDisplay.texture = webcamTexture;
        }
        // ---------------------
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
            Debug.LogWarning("ログインしていないため、DBから検知用データを読み込めません。");
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
        Debug.Log($"データベースから{database.Count}件の物体データを読み込み、検知可能になりました。");
    }

    private bool FindObject()
    {
        float[] currentVector = GetFeatureVectorFromTexture(webcamTexture);
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

        if (bestSimilarity > similarityThreshold)
        {
            Mat liveFrameMat = OpenCvSharp.Unity.TextureToMat(webcamTexture);
            KeyPoint[] liveKeypoints;
            Mat liveDescriptors = new Mat();
            featureDetector.DetectAndCompute(liveFrameMat, null, out liveKeypoints, liveDescriptors);

            if (liveKeypoints.Length > 20 && potentialMatch.Descriptors != null && !potentialMatch.Descriptors.Empty())
            {
                var matches = matcher.KnnMatch(potentialMatch.Descriptors, liveDescriptors, 2);
                var goodMatches = matches.Where(m => m.Length >= 2 && m[0].Distance < 0.75 * m[1].Distance).Select(m => m[0]).ToList();
                if (goodMatches.Count > 20)
                {
                    var srcPts = goodMatches.Select(m => potentialMatch.Keypoints[m.QueryIdx].Pt).ToArray();
                    var dstPts = goodMatches.Select(m => liveKeypoints[m.TrainIdx].Pt).ToArray();
                    Mat homography = Cv2.FindHomography(InputArray.Create(srcPts), InputArray.Create(dstPts), HomographyMethods.Ransac, 5.0);
                    if (!homography.Empty())
                    {
                        UpdateFacialFeatures(potentialMatch, homography);
                        resultText.text = $"発見: {potentialMatch.Name}!";
                        homography.Dispose();
                        liveFrameMat.Dispose();
                        liveDescriptors.Dispose();
                        return true;
                    }
                }
            }
            liveFrameMat.Dispose();
            liveDescriptors.Dispose();
        }

        resultText.text = "捜索中...";
        recognitionEyeImage.gameObject.SetActive(false);
        recognitionMouthImage.gameObject.SetActive(false);
        return false;
    }

    private float[] GetFeatureVectorFromTexture(Texture inputTexture)
    {
        RenderTexture tempRT = RenderTexture.GetTemporary(224, 224, 0, RenderTextureFormat.Default);
        Graphics.Blit(inputTexture, tempRT);
        var tensor = new Tensor(tempRT, 3);
        RenderTexture.ReleaseTemporary(tempRT);
        worker.Execute(tensor);
        Tensor outputTensor = worker.PeekOutput();
        float[] featureVector = outputTensor.AsFloats();
        tensor.Dispose();
        outputTensor.Dispose();
        return featureVector;
    }

    private void UpdateFacialFeatures(RegisteredObject obj, Mat homography)
    {
        if (obj.EyeStyleId != -1)
        {
            recognitionEyeImage.sprite = eyeStyles[obj.EyeStyleId];
            UpdatePartPosition(obj, homography, obj.RelativeEyePosition, recognitionEyeImage.rectTransform);
            recognitionEyeImage.gameObject.SetActive(true);
        }
        else
        {
            recognitionEyeImage.gameObject.SetActive(false);
        }
        if (obj.MouthStyleId != -1)
        {
            recognitionMouthImage.sprite = mouthStyles[obj.MouthStyleId];
            UpdatePartPosition(obj, homography, obj.RelativeMouthPosition, recognitionMouthImage.rectTransform);
            recognitionMouthImage.gameObject.SetActive(true);
        }
        else
        {
            recognitionMouthImage.gameObject.SetActive(false);
        }
    }

    private void UpdatePartPosition(RegisteredObject obj, Mat homography, Vector2 relativePos, RectTransform partTransform)
    {
        Point2f[] srcCorners = { new Point2f(0, 0), new Point2f(obj.ImageSize.Width, 0), new Point2f(obj.ImageSize.Width, obj.ImageSize.Height), new Point2f(0, obj.ImageSize.Height) };
        Point2f[] dstCornersF = Cv2.PerspectiveTransform(srcCorners, homography);
        Vector2[] dstCorners = new Vector2[4];
        for (int i = 0; i < 4; i++) { dstCorners[i] = new Vector2(dstCornersF[i].X, dstCornersF[i].Y); }
        Vector2 topEdge = Vector2.Lerp(dstCorners[0], dstCorners[1], relativePos.x);
        Vector2 bottomEdge = Vector2.Lerp(dstCorners[3], dstCorners[2], relativePos.x);
        Vector2 projectedPos = Vector2.Lerp(topEdge, bottomEdge, 1 - relativePos.y);
        Vector3[] cameraWorldCorners = new Vector3[4];
        cameraDisplay.rectTransform.GetWorldCorners(cameraWorldCorners);
        float normalizedX = projectedPos.x / webcamTexture.width;
        float normalizedY = 1 - (projectedPos.y / webcamTexture.height);
        Vector3 finalPosition = Vector3.Lerp(Vector3.Lerp(cameraWorldCorners[0], cameraWorldCorners[3], normalizedX), Vector3.Lerp(cameraWorldCorners[1], cameraWorldCorners[2], normalizedX), normalizedY);
        partTransform.position = finalPosition;
    }

    private float CalculateCosineSimilarity(float[] vecA, float[] vecB)
    {
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
        if (bytes == null || bytes.Length == 0) return null;
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