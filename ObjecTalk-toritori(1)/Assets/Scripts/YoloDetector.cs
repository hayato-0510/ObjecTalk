using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Sentis;
using TMPro;
using FF = Unity.Sentis.Functional;

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

    [Header("UI")]
    [SerializeField] private RawImage rawImage;
    [SerializeField] private AspectRatioFitter fit;
    [SerializeField] private GameObject boxPrefab;

    // --- Private members ---
    private string[] labels;
    private Worker engine;
    private WebCamTexture cameraTexture;
    private bool isDetecting = false;
    private bool isInitialized = false;

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
        }
    }

    IEnumerator InitializeEngine()
    {
        // 1. ラベルを読み込む
        if (labelsAsset == null)
        {
            Debug.LogError("Labels asset is null!");
            isInitialized = false;
            yield break;
        }

        labels = labelsAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        Debug.Log($"Loaded {labels.Length} labels");

        if (modelAsset == null)
        {
            Debug.LogError("Model asset is null!");
            isInitialized = false;
            yield break;
        }

        // 2. モデルをロードし、後処理を組み込む
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

            // 3. 最終的なモデルをコンパイルし、Workerを作成
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
            // centersToCornersTensorを破棄
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

        cameraTexture = new WebCamTexture(devices[0].name, 1280, 720);
        cameraTexture.Play();

        yield return new WaitUntil(() => cameraTexture.width > 100);

        if (rawImage != null)
        {
            rawImage.texture = cameraTexture;
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

        try
        {
            // 画面の向きやアスペクト比を調整
            fit.aspectRatio = (float)cameraTexture.width / cameraTexture.height;
            float scaleY = cameraTexture.videoVerticallyMirrored ? -1f : 1f;
            rawImage.rectTransform.localScale = new Vector3(1f, scaleY, 1f);
            rawImage.rectTransform.localEulerAngles = new Vector3(0, 0, -cameraTexture.videoRotationAngle);

            // 検出を実行
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

        // --- Input tensor creation ---
        if (!hasError)
        {
            try
            {
                // 新しいTextureToTensorメソッドを使用
                inputTensor = TextureToTensor(cameraTexture, netWidth, netHeight);
                engine.Schedule(inputTensor);
            }
            catch (Exception e)
            {
                Debug.LogError($"Input tensor creation error: {e.Message}");
                hasError = true;
            }
        }

        // 推論完了を待機
        if (!hasError)
        {
            yield return new WaitForEndOfFrame();
        }

        // --- Output processing ---
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
                    // CPU側にデータをコピーして読み取り可能にする
                    outputBoxes = rawOutputBoxes.ReadbackAndClone();
                    outputScores = rawOutputScores.ReadbackAndClone();
                    outputLabels = rawOutputLabels.ReadbackAndClone();

                    var detectionResults = ProcessDetections(outputBoxes, outputScores, outputLabels);
                    DrawBoundingBoxes(detectionResults);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Detection processing error: {e.Message}");
                hasError = true;
            }
        }

        // --- Cleanup ---
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

    // TextureConverter.ToTensorの代替実装
    Tensor<float> TextureToTensor(WebCamTexture texture, int width, int height)
    {
        // RenderTextureを使用してテクスチャをリサイズ
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(texture, renderTexture);

        // RenderTextureからピクセルデータを取得
        RenderTexture.active = renderTexture;
        Texture2D texture2D = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture2D.Apply();
        RenderTexture.active = null;

        // ピクセルデータを取得
        Color32[] pixels = texture2D.GetPixels32();

        // テンソル用のデータ配列を作成 (NCHW形式: [1, 3, height, width])
        float[] tensorData = new float[1 * 3 * height * width];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = y * width + x;
                Color32 pixel = pixels[pixelIndex];

                // RGB値を0-1の範囲に正規化してテンソルに格納
                int baseIndex = y * width + x;
                tensorData[0 * height * width + baseIndex] = pixel.r / 255.0f; // R チャンネル
                tensorData[1 * height * width + baseIndex] = pixel.g / 255.0f; // G チャンネル
                tensorData[2 * height * width + baseIndex] = pixel.b / 255.0f; // B チャンネル
            }
        }

        // リソースを解放
        RenderTexture.ReleaseTemporary(renderTexture);
        DestroyImmediate(texture2D);

        // テンソルを作成して返す
        return new Tensor<float>(new TensorShape(1, 3, height, width), tensorData);
    }

    List<BoundingBox> ProcessDetections(Tensor<float> outputBoxes, Tensor<float> outputScores, Tensor<int> outputLabels)
    {
        var detectionResults = new List<BoundingBox>();

        if (rawImage?.rectTransform == null)
        {
            Debug.LogError("RawImage or its RectTransform is null!");
            return detectionResults;
        }

        float scaleX = rawImage.rectTransform.rect.width / netWidth;
        float scaleY = rawImage.rectTransform.rect.height / netHeight;
        float offsetX = rawImage.rectTransform.rect.width / 2;
        float offsetY = rawImage.rectTransform.rect.height / 2;

        int count = outputBoxes.shape[0];
        Debug.Log($"Detection count: {count}");

        for (int i = 0; i < count; i++)
        {
            try
            {
                int labelIndex = outputLabels[i];

                // ラベルインデックスの範囲チェック
                if (labels == null || labelIndex < 0 || labelIndex >= labels.Length)
                {
                    Debug.LogWarning($"Invalid label index: {labelIndex}, labels array length: {labels?.Length}");
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

    // 結果を描画する
    void DrawBoundingBoxes(List<BoundingBox> boxes)
    {
        try
        {
            // nullチェックを追加
            if (rawImage?.transform == null)
            {
                Debug.LogError("RawImage or its transform is null!");
                return;
            }

            if (boxPrefab == null)
            {
                Debug.LogError("BoxPrefab is null!");
                return;
            }

            Debug.Log($"Drawing {boxes.Count} bounding boxes");

            // 既存のボックスを削除
            foreach (Transform child in rawImage.transform)
            {
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }

            // 新しいボックスを作成
            foreach (var box in boxes)
            {
                try
                {
                    GameObject newBox = Instantiate(boxPrefab, rawImage.transform);
                    if (newBox == null) continue;

                    var rt = newBox.GetComponent<RectTransform>();
                    var text = newBox.GetComponentInChildren<TextMeshProUGUI>();

                    if (rt == null)
                    {
                        Debug.LogError("RectTransform component not found in boxPrefab!");
                        Destroy(newBox);
                        continue;
                    }

                    rt.localPosition = new Vector3(box.CenterX, -box.CenterY, 0);
                    rt.sizeDelta = new Vector2(box.Width, box.Height);

                    if (text != null)
                    {
                        text.text = $"{box.Label} ({box.Score:P0})";
                    }
                    else
                    {
                        Debug.LogWarning("Text component not found in boxPrefab!");
                    }

                    newBox.SetActive(true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error creating bounding box: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in DrawBoundingBoxes: {e.Message}");
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
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in OnDestroy: {e.Message}");
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