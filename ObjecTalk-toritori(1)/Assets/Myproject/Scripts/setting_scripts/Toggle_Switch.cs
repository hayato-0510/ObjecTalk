using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// どの設定項目を扱うトグルかを識別するためのenum（列挙型）
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
    private ToggleType toggleType; // このトグルがカレンダー用か天気用かを設定

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
    private DatabaseManager dbManager;
    private User currentUser;

    // 仮のユーザーID（本来はログイン機能などから取得します）
    private const string TEMP_USER_ID = "test_user_01";

    void Start()
    {
        // DatabaseManagerのインスタンスを作成
        dbManager = new DatabaseManager();

        // データベースから現在のユーザー情報を取得
        // 存在しなければ、テスト用に新しいユーザーを作成して保存
        currentUser = dbManager.GetUser(TEMP_USER_ID);
        if (currentUser == null)
        {
            currentUser = new User
            {
                user_id = TEMP_USER_ID,
                Name = "Test User",
                Age = 30,
                IsCalendarSyncEnabled = false, // 初期値はfalse
                IsWeatherSyncEnabled = false   // 初期値はfalse
            };
            dbManager.SaveUser(currentUser);
            Debug.Log($"テストユーザー(ID: {TEMP_USER_ID})を新規作成しました。");
        }

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

    /// <summary>
    /// トグルの状態（見た目）を更新します。
    /// </summary>
    /// <param name="animated">アニメーションさせるかどうか</param>
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

        // アニメーションの有無で処理を分岐
        if (animated)
        {
            StartCoroutine(SmoothMove(handleTransform.anchoredPosition, endPos));
        }
        else
        {
            handleTransform.anchoredPosition = endPos;
        }
    }

    /// <summary>
    /// ハンドルを滑らかに動かすコルーチン
    /// </summary>
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

    /// <summary>
    /// データベースのユーザー設定を更新します。
    /// </summary>
    private void UpdateDatabaseSetting()
    {
        if (currentUser == null)
        {
            Debug.LogError("ユーザー情報が取得できていません。");
            return;
        }

        // トグルの種類に応じて、対応する設定を更新
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

        // 変更したユーザー情報をデータベースに保存
        dbManager.SaveUser(currentUser);
    }
}