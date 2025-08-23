using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum ToggleType
{
    CalendarSync,
    WeatherSync
}

public class ToggleSwitch : MonoBehaviour, IPointerClickHandler
{
    // --- Inspectorで設定する項目 ---
    [Header("トグルの種類")]
    [SerializeField]
    private ToggleType toggleType;

    [Header("UIコンポーネント")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private RectTransform handleTransform;

    [Header("UIデザイン")]
    [SerializeField] private Color32 onColor = new Color32(76, 175, 80, 255);
    [SerializeField] private Color32 offColor = new Color32(189, 189, 189, 255);
    [SerializeField] private float handlePos = 50f;
    [SerializeField] private float moveSpeed = 10f;

    // --- 内部変数 ---
    private bool isOn;
    private bool isMove;
    private DatabaseManager dbManager; // データベースマネージャーを外部から受け取る
    private User currentUser;         // ユーザー情報を外部から受け取る

    /// <summary>
    /// このトグルを初期化し、ユーザー情報とDBマネージャーを設定します。
    /// ログイン成功後、IntegratedAuthManagerから呼び出されます。
    /// </summary>
    public void Initialize(User user, DatabaseManager databaseManager)
    {
        Debug.Log("ToggleSwitchが初期化されました。"); // この行を追加
        if (user == null)
        {
            if (user == null)
            {
                Debug.LogError("ユーザー情報が渡されていません。トグルを無効化します。");

                // ① まずコンポーネントを取得
                var selectable = GetComponent<Selectable>();

                // ② コンポーネントが存在するかチェックしてから、プロパティに代入
                if (selectable != null)
                {
                    selectable.interactable = false;
                }

                return;
            }
        }

        currentUser = user;
        dbManager = databaseManager;

        // データベースの値に基づいてトグルの初期状態を設定
        InitializeToggleState();

        // UIを初期状態に（アニメーションなしで）更新
        UpdateUI(false);
    }

    /// <summary>
    /// データベースの値に応じてトグルの初期状態（isOn）を設定します。
    /// </summary>
    private void InitializeToggleState()
    {
        switch (toggleType)
        {
            case ToggleType.CalendarSync:
                isOn = currentUser.IsCalendarSyncEnabled;
                break;
            case ToggleType.WeatherSync:
                isOn = currentUser.IsWeatherSyncEnabled;
                break;
        }
    }

    // ... (以下、既存のOnPointerClick, UpdateUI, SmoothMove, UpdateDatabaseSettingメソッドは変更なし)

    /// <summary>
    /// クリックされた時の処理
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (isMove) return;

        // ON/OFF状態を反転
        isOn = !isOn;

        // UIをアニメーション付きで更新
        UpdateUI(true);

        // データベースの値を更新
        UpdateDatabaseSetting();
    }

    private void UpdateUI(bool animated)
    {
        Vector2 endPos;
        if (isOn)
        {
            endPos = new Vector2(handlePos, handleTransform.anchoredPosition.y);
            backgroundImage.color = onColor;
        }
        else
        {
            endPos = new Vector2(-handlePos, handleTransform.anchoredPosition.y);
            backgroundImage.color = offColor;
        }

        if (animated)
        {
            StartCoroutine(SmoothMove(handleTransform.anchoredPosition, endPos));
        }
        else
        {
            handleTransform.anchoredPosition = endPos;
        }
    }

    private IEnumerator SmoothMove(Vector2 startPos, Vector2 endPos)
    {
        isMove = true;
        float elapsedTime = 0;
        while (elapsedTime < 1)
        {
            handleTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, elapsedTime);
            elapsedTime += Time.deltaTime * moveSpeed;
            yield return null;
        }
        handleTransform.anchoredPosition = endPos;
        isMove = false;
    }

    private void UpdateDatabaseSetting()
    {
        if (currentUser == null || dbManager == null)
        {
            Debug.LogError("ユーザー情報またはDBマネージャーが設定されていません。");
            return;
        }

        switch (toggleType)
        {
            case ToggleType.CalendarSync:
                currentUser.IsCalendarSyncEnabled = isOn;
                Debug.Log($"カレンダー同期設定を {isOn} に変更しました。");
                break;
            case ToggleType.WeatherSync:
                currentUser.IsWeatherSyncEnabled = isOn;
                Debug.Log($"天気同期設定を {isOn} に変更しました。");
                break;
        }

        dbManager.SaveUser(currentUser);
    }
}