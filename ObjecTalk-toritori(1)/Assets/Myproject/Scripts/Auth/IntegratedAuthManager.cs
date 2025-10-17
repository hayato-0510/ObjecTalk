using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Firebase.Auth;
using UnityEngine.SceneManagement;

public class IntegratedAuthManager : MonoBehaviour
{
    // シングルトン化するためのインスタンス
    public static IntegratedAuthManager Instance { get; private set; }

    // 外部から参照できる現在のユーザー情報
    public User CurrentUser { get; private set; }

    [Header("UI Handler References")]
    public LoginUIHandler loginUIHandler;
    public RegistrationUIHandler registrationUIHandler;
    public ForgotPasswordUIHandler forgotPasswordUIHandler; // ★追加
    public FlexibleUIManager uiManager;

    [Header("共通UI")]
    public GameObject loadingPanel;
    public TMPro.TMP_Text loadingText;

    [Header("成功時の遷移")]
    public GameObject panelToShowOnSuccess;
    public string mainMenuSceneName = "MainMenu";

    [Header("Collection Panel")]
    public SlotGrid collectionSlotGrid;

    [Header("開発設定")]
    public bool sendVerificationEmail = false;

    private bool isProcessing = false;
    private DatabaseManager databaseManager;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        databaseManager = new DatabaseManager();
    }

    void Start()
    {
        SetupEvents();
        if (loginUIHandler != null)
        {
            ShowPanel(loginUIHandler.gameObject);
        }
        HideLoading();
    }

    /// <summary>
    /// 現在ログインしているユーザーのデータで図鑑の表示を更新します。
    /// </summary>
    public void RefreshCollectionDisplay()
    {
        if (CollectionPanelManager.Instance != null)
        {
            CollectionPanelManager.Instance.ResetActiveSlotState();
        }

        if (CurrentUser == null)
        {
            Debug.LogWarning("ログイン中のユーザーがいないため、図鑑を更新できません。");
            return;
        }

        if (collectionSlotGrid == null)
        {
            Debug.Log("SlotGridへの参照が失われていたため、再検索します。");
            collectionSlotGrid = FindFirstObjectByType<SlotGrid>();
        }

        var userObjects = databaseManager.GetAllUserObjectsWithDetails(CurrentUser.user_id);

        if (collectionSlotGrid != null)
        {
            collectionSlotGrid.InitializeCollection(userObjects);
            Debug.Log("図鑑の表示を最新の情報に更新しました。");
        }
    }


    private void SetupEvents()
    {
        if (loginUIHandler != null)
        {
            loginUIHandler.OnLoginRequested += HandleLoginRequested;
            // ★変更: OnForgotPasswordClickedイベントを購読
            loginUIHandler.OnForgotPasswordClicked += HandleForgotPasswordClicked;
            loginUIHandler.OnSwitchToRegisterClicked += () => ShowPanel(registrationUIHandler.gameObject);
        }

        if (registrationUIHandler != null)
        {
            registrationUIHandler.OnRegistrationRequested += HandleRegistrationRequested;
            registrationUIHandler.OnSwitchToLoginClicked += () => ShowPanel(loginUIHandler.gameObject);
        }

        // ★追加: 新しいForgotPasswordUIHandlerのイベントを購読
        if (forgotPasswordUIHandler != null)
        {
            forgotPasswordUIHandler.OnSendResetEmailRequested += HandleSendResetEmailRequested;
            forgotPasswordUIHandler.OnCancel += () => forgotPasswordUIHandler.gameObject.SetActive(false);
        }

        AuthenticationManager.OnUserLoggedIn += OnUserLoggedIn;
        AuthenticationManager.OnUserLoggedOut += OnUserLoggedOut;
        AuthenticationManager.OnSessionExpired += OnSessionExpired;
        AuthenticationManager.OnAuthError += OnAuthError;
    }

    private void ShowPanel(GameObject panel)
    {
        if (uiManager != null)
        {
            uiManager.ShowPanel(panel);
        }
        else
        {
            loginUIHandler?.gameObject.SetActive(panel == loginUIHandler.gameObject);
            registrationUIHandler?.gameObject.SetActive(panel == registrationUIHandler.gameObject);
        }
    }

    // ========== UIハンドラーから呼び出される処理 ==========

    private async void HandleLoginRequested(string username, string password)
    {
        if (isProcessing) return;
        isProcessing = true;
        ShowLoading("ログイン中...");

        string email = databaseManager.GetEmailFromUsername(username);

        if (string.IsNullOrEmpty(email))
        {
            isProcessing = false;
            HideLoading();
            loginUIHandler.ShowLoginError("ユーザー名またはパスワードが違います。");
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
            return;
        }

        bool success = await AuthenticationManager.Instance.LoginAsync(email, password);

        isProcessing = false;
        HideLoading();
        if (!success)
        {
            loginUIHandler.ShowLoginError("ユーザー名またはパスワードが違います。");
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
        }
    }

    private async void HandleRegistrationRequested(string username, string email, string password)
    {
        if (isProcessing) return;

        if (databaseManager.DoesUsernameExist(username))
        {
            registrationUIHandler.ShowRegistrationError("このユーザー名は既に使用されています。");
            return;
        }

        isProcessing = true;
        ShowLoading("アカウントを作成中...");

        bool success = await AuthenticationManager.Instance.RegisterAsync(email, password, username);

        if (success)
        {
            if (sendVerificationEmail && AuthenticationManager.Instance.CurrentUser != null)
            {
                await AuthenticationManager.Instance.CurrentUser.SendEmailVerificationAsync();
            }
            SaveUserToDatabase(username, email, AuthenticationManager.Instance.CurrentUser.UserId);
        }
        else
        {
            registrationUIHandler.ShowRegistrationError("登録に失敗しました。");
        }

        isProcessing = false;
        HideLoading();
    }

    // ★変更: このメソッドはポップアップを表示する役割に変更
    private void HandleForgotPasswordClicked()
    {
        if (forgotPasswordUIHandler != null)
        {
            forgotPasswordUIHandler.gameObject.SetActive(true);
        }
    }

    // ★新規追加: パスワードリセットメールの送信を処理するメソッド
    private async void HandleSendResetEmailRequested(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        {
            forgotPasswordUIHandler.ShowStatus("有効なメールアドレスを入力してください。", false);
            return;
        }

        ShowLoading("送信中...");
        bool success = await AuthenticationManager.Instance.SendPasswordResetEmailAsync(email);
        HideLoading();

        if (success)
        {
            forgotPasswordUIHandler.ShowStatus("パスワードリセット用のメールを送信しました。", true);
            // 成功したら数秒後にパネルを閉じる
            StartCoroutine(HidePanelAfterDelay(forgotPasswordUIHandler.gameObject, 3f));
        }
        else
        {
            forgotPasswordUIHandler.ShowStatus("メールの送信に失敗しました。", false);
        }
    }

    private IEnumerator HidePanelAfterDelay(GameObject panel, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    // ========== AuthenticationManager イベントハンドラー ==========

    private void OnUserLoggedIn(FirebaseUser user)
    {
        Debug.Log($"ユーザーがログインしました: {user.Email}");

        CurrentUser = databaseManager.GetUser(user.UserId);

        if (CurrentUser == null)
        {
            Debug.LogWarning($"ローカルDBにユーザー(ID: {user.UserId})が見つかりませんでした。新規に作成します。");
            string name = !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Email;
            SaveUserToDatabase(name, user.Email, user.UserId);
            CurrentUser = databaseManager.GetUser(user.UserId);
        }

        if (CurrentUser != null)
        {
            // 他のUI要素（ToggleSwitchなど）の初期化処理
            InitializeUIComponents(CurrentUser);
            RefreshCollectionDisplay();
        }
        else
        {
            Debug.LogError("データベースにログインしたユーザーが見つかりません。");
        }
        StartCoroutine(TransitionToMainMenu());
    }

    private void InitializeUIComponents(User user)
    {
        ToggleSwitch[] toggleSwitches = FindObjectsByType<ToggleSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ToggleSwitch toggle in toggleSwitches)
        {
            toggle.Initialize(user, databaseManager);
        }

        AppSettingsUIController[] settingsControllers = FindObjectsByType<AppSettingsUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (AppSettingsUIController controller in settingsControllers)
        {
            controller.Initialize(user, databaseManager);
        }

        ProfileEditor[] profileEditors = FindObjectsByType<ProfileEditor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ProfileEditor editor in profileEditors)
        {
            editor.Initialize(user, databaseManager);
        }
    }

    private void OnUserLoggedOut()
    {
        Debug.Log("ユーザーがログアウトしました");
        ShowPanel(loginUIHandler.gameObject);
        loginUIHandler?.ClearInputFields();
        HideLoading();
        CurrentUser = null;
    }

    private void OnSessionExpired()
    {
        Debug.Log("セッションが期限切れです");
        CurrentUser = null;
        HideLoading(); // HideLoadingがUI操作を含む場合、これもnullチェックが必要かもしれません

        // [修正案1]
        // ログインUIハンドラがまだ存在する場合（稀ですが）
        if (loginUIHandler != null)
        {
            ShowPanel(loginUIHandler.gameObject);
            loginUIHandler.ShowLoginError("セッションが期限切れです。再度ログインしてください。");
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
        }
        // [修正案2]
        // ログインUIハンドラが存在しない（＝ログイン画面以外の画面にいる）場合
        else
        {
            // セッション切れの情報を PlayerPrefs などに一時保存
            // これにより、ログイン画面がロードされた後にエラーメッセージを表示できる
            PlayerPrefs.SetString("SessionErrorMessage", "セッションが期限切れです。再度ログインしてください。");
            PlayerPrefs.Save();

            // ログイン画面のシーン名（例: "LoginScene"）をロードする
            // ※※※ ここの "LoginScene" はご自身のプロジェクトのシーン名に書き換えてください ※※※
            SceneManager.LoadScene("LoginScene");
        }
    }
    private void OnAuthError(string errorMessage)
    {
        Debug.LogError($"認証エラー: {errorMessage}");
        string parsedMessage = AuthUtils.ParseErrorMessage(errorMessage);
        if (registrationUIHandler != null && registrationUIHandler.gameObject.activeSelf)
        {
            registrationUIHandler.ShowRegistrationError(parsedMessage);
        }
        else if (loginUIHandler != null)
        {
            loginUIHandler.ShowLoginError(parsedMessage);
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
        }
        HideLoading();
    }

    // ========== ユーティリティメソッド ==========

    private void ShowLoading(string message)
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingText != null) loadingText.text = message;
    }

    private void HideLoading()
    {
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    private IEnumerator TransitionToMainMenu()
    {
        ShowLoading("ログイン処理完了。画面を切り替えています...");
        yield return new WaitForSeconds(1f);
        if (uiManager != null && panelToShowOnSuccess != null)
        {
            uiManager.ShowPanel(panelToShowOnSuccess);
        }
        else if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            loginUIHandler.ShowLoginSuccess("ログインが完了しました。");
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(3f));
        }
        HideLoading();
    }

    private void SaveUserToDatabase(string name, string email, string firebaseUserId)
    {
        try
        {
            User newUser = new User
            {
                user_id = firebaseUserId,
                Name = name,
                DateOfBirth = "",
                GmailAddress = email,
                IsCalendarSyncEnabled = false,
                IsWeatherSyncEnabled = false
            };
            databaseManager.SaveUser(newUser);
            Debug.Log($"ユーザー情報をデータベースに保存しました。");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"データベースへの保存でエラーが発生しました: {e.Message}");
        }
    }

    void OnDestroy()
    {
        if (loginUIHandler != null)
        {
            loginUIHandler.OnLoginRequested -= HandleLoginRequested;
            loginUIHandler.OnForgotPasswordClicked -= HandleForgotPasswordClicked;
            loginUIHandler.OnSwitchToRegisterClicked -= () => ShowPanel(registrationUIHandler.gameObject);
        }
        if (registrationUIHandler != null)
        {
            registrationUIHandler.OnRegistrationRequested -= HandleRegistrationRequested;
            registrationUIHandler.OnSwitchToLoginClicked -= () => ShowPanel(loginUIHandler.gameObject);
        }
        if (forgotPasswordUIHandler != null)
        {
            forgotPasswordUIHandler.OnSendResetEmailRequested -= HandleSendResetEmailRequested;
            forgotPasswordUIHandler.OnCancel -= () => forgotPasswordUIHandler.gameObject.SetActive(false);
        }
        if (AuthenticationManager.Instance != null)
        {
            AuthenticationManager.OnUserLoggedIn -= OnUserLoggedIn;
            AuthenticationManager.OnUserLoggedOut -= OnUserLoggedOut;
            AuthenticationManager.OnSessionExpired -= OnSessionExpired;
            AuthenticationManager.OnAuthError -= OnAuthError;
        }
    }
}