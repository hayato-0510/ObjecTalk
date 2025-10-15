using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Sentis;
using TMPro;
using FF = Unity.Sentis.Functional;

// 検出モードの列挙型（クラスの外で定義）
public enum DetectionMode
{
    EveryNFrames,   // N フレームごと
    EveryNSeconds,  // N 秒ごと
    Manual          // 手動トリガー
}

public class RealtimeYoloDetector : MonoBehaviour
{
    [Header("Model & Labels")]
    [SerializeField] private ModelAsset modelAsset;
    [SerializeField] private TextAsset labelsAsset;

    [Header("Inference Settings")]
    [SerializeField] private int netWidth = 640;
    [SerializeField] private int netHeight = 640;
    [SerializeField, Range(0, 1)] private float scoreThreshold = 0.5f;
    [SerializeField, Range(0, 1)] private float iouThreshold = 0.45f;

    [Header("Detection Frequency")]
    [SerializeField] private DetectionMode detectionMode = DetectionMode.EveryNFrames;
    [SerializeField, Range(1, 30)] private int frameInterval = 3; // N フレームごとに検出
    [SerializeField, Range(0.1f, 5f)] private float timeInterval = 0.5f; // N 秒ごとに検出（秒単位）

    [Header("UI")]
    [SerializeField] private RawImage rawImage;
    [SerializeField] private AspectRatioFitter fit;
    [SerializeField] private GameObject boxPrefab;

    [Header("Debug Settings")]
    [SerializeField] private bool enableConsoleOutput = false; // デフォルトでオフ
    [SerializeField] private bool showDetailedInfo = false;

    // --- Private members ---
    private string[] labels;
    private Worker engine;
    private WebCamTexture cameraTexture;
    private bool isDetecting = false;
    private bool isInitialized = false;
    private int frameCount = 0;
    private float lastDetectionTime = 0f;

    // 最適化用のキャッシュ
    private RenderTexture persistentRenderTexture;
    private Texture2D persistentTexture2D;
    private List<BoundingBox> cachedDetections = new List<BoundingBox>();
    private Stack<GameObject> boxPool = new Stack<GameObject>();
    private List<GameObject> activeBoxes = new List<GameObject>();

    void Start()
    {
        StartCoroutine(Initialize());
    }

    IEnumerator Initialize()
    {
        yield return StartCoroutine(InitializeEngine());
        if (isInitialized)
        {
            yield return StartCoroutine(InitializeCamera());
            InitializeBoxPool();
        }
    }

    // ボックスプールを事前に作成
    void InitializeBoxPool()
    {
        if (boxPrefab == null || rawImage?.transform == null) return;

        for (int i = 0; i < 20; i++) // 最大20個のボックスを事前生成
        {
            GameObject box = Instantiate(boxPrefab, rawImage.transform);
            box.SetActive(false);
            boxPool.Push(box);
        }
    }

    IEnumerator InitializeEngine()
    {
        if (labelsAsset == null)
        {
            Debug.LogError("Labels asset is null!");
            isInitialized = false;
            yield break;
        }

        labels = labelsAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (modelAsset == null)
        {
            Debug.LogError("Model asset is null!");
            isInitialized = false;
            yield break;
        }

        var model = ModelLoader.Load(modelAsset);
        if (model == null)
        {
            Debug.LogError("Failed to load model!");
            isInitialized = false;
            yield break;
        }

        Tensor<float> centersToCornersTensor = null;

        try
        {
            var graph = new FunctionalGraph();
            var input = graph.AddInput<float>(model.inputs[0].shape);
            var modelOutput = Functional.Forward(model, input)[0];

            var boxCoords = modelOutput[0, 0..4, ..].Transpose(0, 1);
            var allScores = modelOutput[0, 4.., ..];
            var scores = FF.ReduceMax(allScores, 0);
            var classIDs = FF.ArgMax(allScores, 0);

            centersToCornersTensor = new Tensor<float>(new TensorShape(4, 4), new float[] {
                1, 0, 1, 0,
                0, 1, 0, 1,
                -0.5f, 0, 0.5f, 0,
                0, -0.5f, 0, 0.5f
            });

            var boxCorners = FF.MatMul(boxCoords, Functional.Constant(centersToCornersTensor));
            var indices = FF.NMS(boxCorners, scores, iouThreshold, scoreThreshold);

            var indices2 = indices.Unsqueeze(-1);
            var finalBoxes = FF.Gather(boxCoords, 0, indices2.BroadcastTo(new int[] { 4 }));
            var finalScores = FF.Gather(scores, 0, indices);
            var finalClassIDs = FF.Gather(classIDs, 0, indices);

            model = graph.Compile(finalBoxes, finalScores, finalClassIDs);
            engine = new Worker(model, BackendType.GPUCompute);

            isInitialized = true;
            Debug.Log("Engine initialized successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"Engine initialization failed: {e.Message}");
            isInitialized = false;
        }
        finally
        {
            centersToCornersTensor?.Dispose();
        }
    }

    IEnumerator InitializeCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No camera devices found.");
            yield break;
        }

        // 解像度を下げることでパフォーマンス向上
        cameraTexture = new WebCamTexture(devices[0].name, 1280, 720, 30);
        cameraTexture.Play();

        yield return new WaitUntil(() => cameraTexture.width > 100);

        if (rawImage != null)
        {
            rawImage.texture = cameraTexture;

            // 永続的なRenderTextureとTexture2Dを作成
            persistentRenderTexture = new RenderTexture(netWidth, netHeight, 0, RenderTextureFormat.ARGB32);
            persistentTexture2D = new Texture2D(netWidth, netHeight, TextureFormat.RGB24, false);

            Debug.Log("Camera initialized successfully");
        }
        else
        {
            Debug.LogError("RawImage is null!");
        }
    }

    void Update()
    {
        if (!isInitialized || cameraTexture == null || !cameraTexture.isPlaying ||
            isDetecting || engine == null || rawImage == null || fit == null)
        {
            return;
        }

        // 検出頻度のチェック
        bool shouldDetect = false;

        switch (detectionMode)
        {
            case DetectionMode.EveryNFrames:
                frameCount++;
                shouldDetect = (frameCount % frameInterval == 0);
                break;

            case DetectionMode.EveryNSeconds:
                float currentTime = Time.time;
                if (currentTime - lastDetectionTime >= timeInterval)
                {
                    shouldDetect = true;
                    lastDetectionTime = currentTime;
                }
                break;

            case DetectionMode.Manual:
                // 手動モード - 外部から制御
                shouldDetect = false;
                break;
        }

        if (!shouldDetect)
        {
            return;
        }

        try
        {
            fit.aspectRatio = (float)cameraTexture.width / cameraTexture.height;
            float scaleY = cameraTexture.videoVerticallyMirrored ? -1f : 1f;
            rawImage.rectTransform.localScale = new Vector3(1f, scaleY, 1f);
            rawImage.rectTransform.localEulerAngles = new Vector3(0, 0, -cameraTexture.videoRotationAngle);

            StartCoroutine(RunDetection());
        }
        catch (Exception e)
        {
            Debug.LogError($"Update error: {e.Message}");
        }
    }

    IEnumerator RunDetection()
    {
        isDetecting = true;

        Tensor<float> inputTensor = null;
        Tensor<float> outputBoxes = null;
        Tensor<float> outputScores = null;
        Tensor<int> outputLabels = null;

        bool hasError = false;

        if (!hasError)
        {
            try
            {
                inputTensor = TextureToTensorOptimized(cameraTexture);
                engine.Schedule(inputTensor);
            }
            catch (Exception e)
            {
                Debug.LogError($"Input tensor creation error: {e.Message}");
                hasError = true;
            }
        }

        if (!hasError)
        {
            yield return new WaitForEndOfFrame();
        }

        if (!hasError)
        {
            try
            {
                var rawOutputBoxes = engine.PeekOutput("output_0") as Tensor<float>;
                var rawOutputScores = engine.PeekOutput("output_1") as Tensor<float>;
                var rawOutputLabels = engine.PeekOutput("output_2") as Tensor<int>;

                if (rawOutputBoxes == null || rawOutputScores == null || rawOutputLabels == null)
                {
                    Debug.LogError("Failed to get model outputs!");
                    hasError = true;
                }
                else
                {
                    outputBoxes = rawOutputBoxes.ReadbackAndClone();
                    outputScores = rawOutputScores.ReadbackAndClone();
                    outputLabels = rawOutputLabels.ReadbackAndClone();

                    cachedDetections = ProcessDetections(outputBoxes, outputScores, outputLabels);

                    if (enableConsoleOutput && cachedDetections.Count > 0)
                    {
                        LogDetectionResults(cachedDetections);
                    }

                    DrawBoundingBoxesOptimized(cachedDetections);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Detection processing error: {e.Message}");
                hasError = true;
            }
        }

        try
        {
            inputTensor?.Dispose();
            outputBoxes?.Dispose();
            outputScores?.Dispose();
            outputLabels?.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogError($"Tensor disposal error: {e.Message}");
        }

        isDetecting = false;
    }

    void LogDetectionResults(List<BoundingBox> detectionResults)
    {
        if (detectionResults.Count == 0) return;

        System.Text.StringBuilder logMessage = new System.Text.StringBuilder();
        logMessage.AppendLine($"=== Frame {frameCount} - 検出: {detectionResults.Count}個 ===");

        Dictionary<string, int> objectCounts = new Dictionary<string, int>();
        Dictionary<string, float> maxScores = new Dictionary<string, float>();

        foreach (var detection in detectionResults)
        {
            if (objectCounts.ContainsKey(detection.Label))
            {
                objectCounts[detection.Label]++;
            }
            else
            {
                objectCounts[detection.Label] = 1;
            }

            if (!maxScores.ContainsKey(detection.Label) || detection.Score > maxScores[detection.Label])
            {
                maxScores[detection.Label] = detection.Score;
            }

            if (showDetailedInfo)
            {
                logMessage.AppendLine($"  • {detection.Label}: {detection.Score:P1} " +
                                    $"(位置: {detection.CenterX:F0}, {detection.CenterY:F0}, " +
                                    $"サイズ: {detection.Width:F0}×{detection.Height:F0})");
            }
        }

        logMessage.AppendLine("--- サマリー ---");
        foreach (var kvp in objectCounts)
        {
            logMessage.AppendLine($"  {kvp.Key}: {kvp.Value}個 (最高: {maxScores[kvp.Key]:P1})");
        }

        Debug.Log(logMessage.ToString());
    }

    // 最適化されたテクスチャ変換（再利用可能なリソースを使用）
    Tensor<float> TextureToTensorOptimized(WebCamTexture texture)
    {
        // 永続的なRenderTextureを再利用
        Graphics.Blit(texture, persistentRenderTexture);

        RenderTexture.active = persistentRenderTexture;
        persistentTexture2D.ReadPixels(new Rect(0, 0, netWidth, netHeight), 0, 0, false);
        persistentTexture2D.Apply(false);
        RenderTexture.active = null;

        Color32[] pixels = persistentTexture2D.GetPixels32();
        float[] tensorData = new float[3 * netHeight * netWidth];

        // 最適化されたピクセル処理
        int channelSize = netHeight * netWidth;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            tensorData[i] = pixel.r / 255.0f;
            tensorData[channelSize + i] = pixel.g / 255.0f;
            tensorData[2 * channelSize + i] = pixel.b / 255.0f;
        }

        return new Tensor<float>(new TensorShape(1, 3, netHeight, netWidth), tensorData);
    }

    List<BoundingBox> ProcessDetections(Tensor<float> outputBoxes, Tensor<float> outputScores, Tensor<int> outputLabels)
    {
        var detectionResults = new List<BoundingBox>();

        if (rawImage?.rectTransform == null) return detectionResults;

        float scaleX = rawImage.rectTransform.rect.width / netWidth;
        float scaleY = rawImage.rectTransform.rect.height / netHeight;
        float offsetX = rawImage.rectTransform.rect.width / 2;
        float offsetY = rawImage.rectTransform.rect.height / 2;

        int count = outputBoxes.shape[0];

        for (int i = 0; i < count; i++)
        {
            try
            {
                int labelIndex = outputLabels[i];

                if (labels == null || labelIndex < 0 || labelIndex >= labels.Length)
                {
                    continue;
                }

                float score = outputScores[i];
                if (score < scoreThreshold)
                {
                    continue;
                }

                detectionResults.Add(new BoundingBox
                {
                    CenterX = outputBoxes[i, 0] * scaleX - offsetX,
                    CenterY = outputBoxes[i, 1] * scaleY - offsetY,
                    Width = outputBoxes[i, 2] * scaleX,
                    Height = outputBoxes[i, 3] * scaleY,
                    Label = labels[labelIndex],
                    Score = score
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"Error processing detection {i}: {e.Message}");
            }
        }

        return detectionResults;
    }

    // オブジェクトプールを使用した最適化された描画
    void DrawBoundingBoxesOptimized(List<BoundingBox> boxes)
    {
        try
        {
            if (rawImage?.transform == null || boxPrefab == null) return;

            // 既存のアクティブボックスをプールに戻す
            foreach (var box in activeBoxes)
            {
                if (box != null)
                {
                    box.SetActive(false);
                    boxPool.Push(box);
                }
            }
            activeBoxes.Clear();

            // 新しいボックスを作成または再利用
            foreach (var box in boxes)
            {
                GameObject boxObj;

                if (boxPool.Count > 0)
                {
                    boxObj = boxPool.Pop();
                }
                else
                {
                    boxObj = Instantiate(boxPrefab, rawImage.transform);
                }

                if (boxObj == null) continue;

                var rt = boxObj.GetComponent<RectTransform>();
                var text = boxObj.GetComponentInChildren<TextMeshProUGUI>();

                if (rt != null)
                {
                    rt.localPosition = new Vector3(box.CenterX, -box.CenterY, 0);
                    rt.sizeDelta = new Vector2(box.Width, box.Height);
                }

                if (text != null)
                {
                    text.text = $"{box.Label} ({box.Score:P0})";
                }

                boxObj.SetActive(true);
                activeBoxes.Add(boxObj);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in DrawBoundingBoxesOptimized: {e.Message}");
        }
    }

    void OnDestroy()
    {
        try
        {
            isInitialized = false;
            engine?.Dispose();

            if (cameraTexture != null)
            {
                cameraTexture.Stop();
                cameraTexture = null;
            }

            // 永続リソースの解放
            if (persistentRenderTexture != null)
            {
                persistentRenderTexture.Release();
                Destroy(persistentRenderTexture);
            }

            if (persistentTexture2D != null)
            {
                Destroy(persistentTexture2D);
            }

            // プール内のオブジェクトを破棄
            foreach (var box in boxPool)
            {
                if (box != null) Destroy(box);
            }
            boxPool.Clear();

            foreach (var box in activeBoxes)
            {
                if (box != null) Destroy(box);
            }
            activeBoxes.Clear();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in OnDestroy: {e.Message}");
        }
    }

    // 手動で検出を実行するためのパブリックメソッド
    public void TriggerDetection()
    {
        if (detectionMode == DetectionMode.Manual && !isDetecting && isInitialized)
        {
            StartCoroutine(RunDetection());
        }
    }

    // 検出モードを実行時に変更
    public void SetDetectionMode(DetectionMode mode)
    {
        detectionMode = mode;
        if (mode == DetectionMode.EveryNSeconds)
        {
            lastDetectionTime = Time.time;
        }
    }
}

// Bounding Boxデータ構造
public struct BoundingBox
{
    public float CenterX;
    public float CenterY;
    public float Width;
    public float Height;
    public string Label;
    public float Score;
}