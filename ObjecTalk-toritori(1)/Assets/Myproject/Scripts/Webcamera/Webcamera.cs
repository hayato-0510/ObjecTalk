using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class WebcamController : MonoBehaviour
{
    [Header("UI要素")]
    // ★変更: initialPanelを削除し、formerPanelを必須要素に変更
    [SerializeField] private GameObject formerPanel;
    [SerializeField] private GameObject cameraScreen;
    [SerializeField] private RawImage webcamDisplay;
    [SerializeField] private Button startCameraButton;
    [SerializeField] private Button backButton;

    [Header("カメラ設定")]
    [SerializeField] private AspectRatioFitter aspectRatioFitter;

    private WebCamTexture webCamTexture;

    void Start()
    {
        startCameraButton.onClick.AddListener(OnStartCameraButtonClick);
        backButton.onClick.AddListener(OnBackButtonClick);

        // 初期状態ではカメラ画面のみ非表示にする
        // formerPanelは表示されていることを前提とします
        if (cameraScreen != null)
        {
            cameraScreen.SetActive(false);
        }
    }

    private void OnStartCameraButtonClick()
    {
        // カメラ起動のコルーチンを開始
        StartCoroutine(StartCameraCoroutine());
    }

    private IEnumerator StartCameraCoroutine()
    {
        // --- パーミッションとカメラデバイスのチェック (変更なし) ---
#if UNITY_ANDROID || UNITY_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }
#endif

        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("エラー: Webカメラが見つかりません。");
            yield break;
        }
#if UNITY_ANDROID || UNITY_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.LogError("エラー: Webカメラへのアクセスが許可されていません。");
            yield break;
        }
#endif
        // --- ここまで ---

        // --- WebCamTextureの初期化と再生 (変更なし) ---
        WebCamDevice device = WebCamTexture.devices[0];
        webCamTexture = new WebCamTexture(device.name);
        webcamDisplay.texture = webCamTexture;
        webCamTexture.Play();

        // --- アスペクト比を修正する処理 (変更なし) ---
        while (webCamTexture.width < 100)
        {
            yield return null;
        }

        webcamDisplay.rectTransform.localEulerAngles = new Vector3(0, 0, -webCamTexture.videoRotationAngle);
        float videoAspectRatio = (float)webCamTexture.width / (float)webCamTexture.height;
        aspectRatioFitter.aspectRatio = videoAspectRatio;
        if (device.isFrontFacing)
        {
            webcamDisplay.rectTransform.localScale = new Vector3(-1, 1, 1);
        }
        else
        {
            webcamDisplay.rectTransform.localScale = new Vector3(1, 1, 1);
        }

        Debug.Log("Webカメラを起動しました。");

        // ★変更: カメラ起動が成功したら、formerPanelとcameraScreenを切り替える
        if (formerPanel != null)
        {
            formerPanel.SetActive(false);
        }
        if (cameraScreen != null)
        {
            cameraScreen.SetActive(true);
        }
    }

    private void OnBackButtonClick()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
            Debug.Log("Webカメラを停止しました。");
        }

        // ★変更: formerPanelに戻る
        if (cameraScreen != null)
        {
            cameraScreen.SetActive(false);
        }
        if (formerPanel != null)
        {
            formerPanel.SetActive(true);
        }
    }

    private void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
        }
    }
}