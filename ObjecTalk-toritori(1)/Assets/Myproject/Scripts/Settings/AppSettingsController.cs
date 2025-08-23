using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMesh Proを使用するために必要

public class AppSettingsUIController : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private Toggle calendarToggle;
    [SerializeField] private Toggle weatherToggle;

    // ★変更点: DatabaseManagerとUserの変数は外部から受け取る
    private DatabaseManager dbManager;
    private User currentUser;

    // ★変更点: Start()メソッドを削除し、外部から呼び出されるInitializeメソッドを作成
    /// <summary>
    /// このコントローラーを初期化し、ユーザー情報とDBマネージャーを設定します。
    /// ログイン成功後、IntegratedAuthManagerから呼び出されます。
    /// </summary>
    public void Initialize(User user, DatabaseManager databaseManager)
    {
        if (user == null)
        {
            Debug.LogError("ユーザー情報が渡されていません。設定UIを無効化します。");
            SetUIInteractable(false); // UIを無効化
            return;
        }

        currentUser = user;
        dbManager = databaseManager;

        // UIの初期設定とイベントリスナーの登録
        InitializeUI();
        SetupListeners();
        SetUIInteractable(true); // UIを有効化
    }

    /// <summary>
    /// データベースから設定を読み込み、UIに反映させます。
    /// </summary>
    private void InitializeUI()
    {
        // ★変更点: 渡されたcurrentUserオブジェクトを使用してUIを初期化
        if (calendarToggle != null)
        {
            calendarToggle.SetIsOnWithoutNotify(currentUser.IsCalendarSyncEnabled);
        }

        if (weatherToggle != null)
        {
            weatherToggle.SetIsOnWithoutNotify(currentUser.IsWeatherSyncEnabled);
        }
    }

    private void SetUIInteractable(bool isInteractable)
    {
        if (calendarToggle != null) calendarToggle.interactable = isInteractable;
        if (weatherToggle != null) weatherToggle.interactable = isInteractable;
    }

    /// <summary>
    /// 各トグルの値が変更されたときのイベントリスナーを設定します。
    /// </summary>
    private void SetupListeners()
    {
        if (calendarToggle != null)
        {
            calendarToggle.onValueChanged.AddListener(OnCalendarToggleChanged);
        }
        if (weatherToggle != null)
        {
            weatherToggle.onValueChanged.AddListener(OnWeatherToggleChanged);
        }
    }

    /// <summary>
    /// カレンダートグルが変更されたときに呼ばれるメソッド
    /// </summary>
    private void OnCalendarToggleChanged(bool isOn)
    {
        if (currentUser == null || dbManager == null) return;
        Debug.Log($"カレンダー連携が {isOn} に変更されました。");
        currentUser.IsCalendarSyncEnabled = isOn;
        dbManager.SaveUser(currentUser);
    }

    /// <summary>
    /// 天気トグルが変更されたときに呼ばれるメソッド
    /// </summary>
    private void OnWeatherToggleChanged(bool isOn)
    {
        if (currentUser == null || dbManager == null) return;
        Debug.Log($"天気連携が {isOn} に変更されました。");
        currentUser.IsWeatherSyncEnabled = isOn;
        dbManager.SaveUser(currentUser);
    }

    private void OnDestroy()
    {
        if (calendarToggle != null)
        {
            calendarToggle.onValueChanged.RemoveListener(OnCalendarToggleChanged);
        }
        if (weatherToggle != null)
        {
            weatherToggle.onValueChanged.RemoveListener(OnWeatherToggleChanged);
        }
    }
}