using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class WebcamController : MonoBehaviour
{
    [Header("UI要素")]
    [SerializeField] private GameObject initialScreen;
    [SerializeField] private GameObject cameraScreen;
    [SerializeField] private RawImage webcamDisplay;
    [SerializeField] private Button startCameraButton;
    [SerializeField] private Button backButton;

    // ★追加: AspectRatioFitterをインスペクターから設定
    [Header("カメラ設定")]
    [SerializeField] private AspectRatioFitter aspectRatioFitter;

    private WebCamTexture webCamTexture;

    void Start()
    {
        startCameraButton.onClick.AddListener(OnStartCameraButtonClick);
        backButton.onClick.AddListener(OnBackButtonClick);

        initialScreen.SetActive(true);
        cameraScreen.SetActive(false);
    }

    private void OnStartCameraButtonClick()
    {
        StartCoroutine(StartCameraCoroutine());
    }

    private IEnumerator StartCameraCoroutine()
    {
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

        WebCamDevice device = WebCamTexture.devices[0];
        webCamTexture = new WebCamTexture(device.name);
        webcamDisplay.texture = webCamTexture;
        webCamTexture.Play();

        // ★★★★★ 以下、アスペクト比を修正する処理を追加 ★★★★★

        // カメラの準備ができるまで少し待つ
        while (webCamTexture.width < 100)
        {
            yield return null;
        }

        // --- アスペクト比と向きを調整 ---
        // 1. Webカメラの回転角度に合わせてRawImageを回転させる
        webcamDisplay.rectTransform.localEulerAngles = new Vector3(0, 0, -webCamTexture.videoRotationAngle);

        // 2. Webカメラの映像の縦横比を計算
        float videoAspectRatio = (float)webCamTexture.width / (float)webCamTexture.height;

        // 3. AspectRatioFitterに比率を設定
        aspectRatioFitter.aspectRatio = videoAspectRatio;

        // 4. インカメラの場合、映像を左右反転させる（鏡像にする）
        if (device.isFrontFacing)
        {
            // Yスケールを-1にすると上下反転なので、Xスケールを-1にして左右反転させる
            webcamDisplay.rectTransform.localScale = new Vector3(-1, 1, 1);
        }
        else
        {
            webcamDisplay.rectTransform.localScale = new Vector3(1, 1, 1);
        }

        // ★★★★★ ここまで ★★★★★

        Debug.Log("Webカメラを起動しました。");

        initialScreen.SetActive(false);
        cameraScreen.SetActive(true);
    }

    private void OnBackButtonClick()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
            Debug.Log("Webカメラを停止しました。");
        }

        cameraScreen.SetActive(false);
        initialScreen.SetActive(true);
    }

    private void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
        }
    }
}