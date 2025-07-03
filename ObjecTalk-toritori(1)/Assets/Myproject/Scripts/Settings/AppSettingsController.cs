using UnityEngine;
using UnityEngine.UI; // ToggleなどUIコンポーネントを使うために必要

public class AppSettingsUIController : MonoBehaviour
{
    // Unityエディタのインスペクターから、UIのトグルをドラッグ＆ドロップで設定します
    [Header("UI Components")]
    [SerializeField] private Toggle calendarToggle;
    [SerializeField] private Toggle weatherToggle;
    // 必要であれば戻るボタンなどもここに設定できます
    // [SerializeField] private Button backButton;

    private DatabaseManager dbManager;
    private User currentUser;

    // ★変更: ユーザーIDをstring型に変更（Firebase UIDなどを想定）
    // このサンプルではユーザーIDを固定値に設定します。
    // 実際のアプリではログイン中のユーザーIDなどを動的に取得してください。
    private const string currentUserId = "default_user_001";

    void Start()
    {
        // DatabaseManagerのインスタンスを生成
        dbManager = new DatabaseManager();

        // UIの初期設定とイベントリスナーの登録
        InitializeUI();
        SetupListeners();
    }

    /// <summary>
    /// データベースから設定を読み込み、UIに反映させます。
    /// </summary>
    private void InitializeUI()
    {
        // 現在のユーザー情報をデータベースから取得
        currentUser = dbManager.GetUser(currentUserId);

        // ユーザーが存在しない場合は、新しいユーザー情報を作成
        if (currentUser == null)
        {
            Debug.LogWarning($"ユーザーID {currentUserId} が見つかりませんでした。新しいユーザーを作成します。");
            currentUser = new User
            {
                user_id = currentUserId,
                Name = "Default User",
                Age = 0,
                GmailAddress = "",
                // ★変更: 設定値をプロパティに直接設定
                IsCalendarSyncEnabled = false,
                IsWeatherSyncEnabled = false
            };
            // この時点ではまだ保存せず、最初のトグル操作時に保存します。
        }

        // ★変更: データベースの値でトグルの状態を初期化
        // SetIsOnWithoutNotifyを使うことで、この初期化時にOnValueChangedイベントが発生するのを防ぎます。
        if (calendarToggle != null)
        {
            calendarToggle.SetIsOnWithoutNotify(currentUser.IsCalendarSyncEnabled);
        }

        if (weatherToggle != null)
        {
            weatherToggle.SetIsOnWithoutNotify(currentUser.IsWeatherSyncEnabled);
        }
    }

    /// <summary>
    /// 各トグルの値が変更されたときのイベントリスナーを設定します。
    /// </summary>
    private void SetupListeners()
    {
        // calendarToggleの値が変更されたら、OnCalendarToggleChangedメソッドを呼び出す
        if (calendarToggle != null)
        {
            calendarToggle.onValueChanged.AddListener(OnCalendarToggleChanged);
        }

        // weatherToggleの値が変更されたら、OnWeatherToggleChangedメソッドを呼び出す
        if (weatherToggle != null)
        {
            weatherToggle.onValueChanged.AddListener(OnWeatherToggleChanged);
        }
    }

    /// <summary>
    /// カレンダートグルが変更されたときに呼ばれるメソッド
    /// </summary>
    /// <param name="isOn">トグルの新しい状態 (ONならtrue)</param>
    private void OnCalendarToggleChanged(bool isOn)
    {
        if (currentUser == null) return;

        Debug.Log($"カレンダー連携が {isOn} に変更されました。");

        // ★変更: 設定値を直接プロパティに保存
        currentUser.IsCalendarSyncEnabled = isOn;
        dbManager.SaveUser(currentUser); // 変更をデータベースに保存
    }

    /// <summary>
    /// 天気トグルが変更されたときに呼ばれるメソッド
    /// </summary>
    /// <param name="isOn">トグルの新しい状態 (ONならtrue)</param>
    private void OnWeatherToggleChanged(bool isOn)
    {
        if (currentUser == null) return;

        Debug.Log($"天気連携が {isOn} に変更されました。");

        // ★変更: 設定値を直接プロパティに保存
        currentUser.IsWeatherSyncEnabled = isOn;
        dbManager.SaveUser(currentUser); // 変更をデータベースに保存
    }

    // OnDestroyはオブジェクトが破棄されるときに呼ばれます
    private void OnDestroy()
    {
        // メモリリークを防ぐため、リスナーを解除しておくのが丁寧な実装です
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