using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Firebase.Auth;
// using UnityEngine.SceneManagement; // 1行重複していたので削除

public class IntegratedAuthManager : MonoBehaviour
{
    // シングルトン化するためのインスタンス
    public static IntegratedAuthManager Instance { get; private set; }

    // 外部から参照できる現在のユーザー情報
    public User CurrentUser { get; private set; }

    [Header("UI Handler References")]
    public LoginUIHandler loginUIHandler;
    public RegistrationUIHandler registrationUIHandler;
    public ForgotPasswordUIHandler forgotPasswordUIHandler;
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

    // ★修正点1: 新規登録処理中であることを示すフラグを追加
    private bool isHandlingRegistration = false;

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

    public void RefreshCollectionDisplay()
    {
        // (このメソッドに変更はありません)
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
        // (このメソッドに変更はありません)
        if (loginUIHandler != null)
        {
            loginUIHandler.OnLoginRequested += HandleLoginRequested;
            loginUIHandler.OnForgotPasswordClicked += HandleForgotPasswordClicked;
            loginUIHandler.OnSwitchToRegisterClicked += () => ShowPanel(registrationUIHandler.gameObject);
        }
        if (registrationUIHandler != null)
        {
            registrationUIHandler.OnRegistrationRequested += HandleRegistrationRequested;
            registrationUIHandler.OnSwitchToLoginClicked += () => ShowPanel(loginUIHandler.gameObject);
        }
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
        // (このメソッドに変更はありません)
        if (uiManager != null) { uiManager.ShowPanel(panel); }
        else
        {
            loginUIHandler?.gameObject.SetActive(panel == loginUIHandler.gameObject);
            registrationUIHandler?.gameObject.SetActive(panel == registrationUIHandler.gameObject);
        }
    }

    // ========== UIハンドラーから呼び出される処理 ==========

    private async void HandleLoginRequested(string username, string password)
    {
        // (このメソッドに変更はありません)
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

    // ★修正点2: HandleRegistrationRequested の処理を大幅に変更
    private async void HandleRegistrationRequested(string username, string email, string password)
    {
        if (isProcessing) return;

        if (databaseManager.DoesUsernameExist(username))
        {
            registrationUIHandler.ShowRegistrationError("このユーザー名は既に使用されています。");
            return;
        }

        isProcessing = true;
        isHandlingRegistration = true; // 登録処理の開始をマーク
        ShowLoading("アカウントを作成中...");

        bool success = await AuthenticationManager.Instance.RegisterAsync(email, password, username);

        if (success)
        {
            if (sendVerificationEmail && AuthenticationManager.Instance.CurrentUser != null)
            {
                await AuthenticationManager.Instance.CurrentUser.SendEmailVerificationAsync();
            }

            // 1. 最初にDBへ保存する
            User newUser = SaveUserToDatabase(username, email, AuthenticationManager.Instance.CurrentUser.UserId);

            if (newUser != null)
            {
                // 2. 保存したユーザー情報でUIを初期化する
                InitializeUIComponents(newUser);
                // 3. 図鑑も初期化する
                RefreshCollectionDisplay();
                // 4. 最後に画面遷移する
                StartCoroutine(TransitionToMainMenu());
            }
            else
            {
                // DB保存に失敗
                OnAuthError("データベースへのユーザー保存に失敗しました。");
            }
        }
        else
        {
            registrationUIHandler.ShowRegistrationError("登録に失敗しました。");
            isProcessing = false;
            HideLoading();
        }

        // 遷移処理（TransitionToMainMenu）の中で isProcessing と isHandlingRegistration はfalseにリセットされます
    }

    // (HandleForgotPasswordClicked, HandleSendResetEmailRequested, HidePanelAfterDelay に変更はありません)
    private void HandleForgotPasswordClicked()
    {
        if (forgotPasswordUIHandler != null) { forgotPasswordUIHandler.gameObject.SetActive(true); }
    }
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
        if (panel != null) { panel.SetActive(false); }
    }

    // ========== AuthenticationManager イベントハンドラー ==========

    // ★修正点3: OnUserLoggedIn が登録処理中は動かないように変更
    private void OnUserLoggedIn(FirebaseUser user)
    {
        // 新規登録処理が手動で実行中の場合は、この自動イベントを無視する
        if (isHandlingRegistration)
        {
            Debug.Log("新規登録フローが実行中のため、自動ログインイベントはスキップします。");
            return;
        }

        Debug.Log($"通常ログインフローでユーザーがログインしました: {user.Email}");

        // 通常のログイン（2回目以降）の処理
        CurrentUser = databaseManager.GetUser(user.UserId);

        if (CurrentUser != null)
        {
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
        // (このメソッドに変更はありません)
        CurrentUser = user; // CurrentUserを更新

        ToggleSwitch[] toggleSwitches = FindObjectsByType<ToggleSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ToggleSwitch toggle in toggleSwitches) { toggle.Initialize(user, databaseManager); }

        AppSettingsUIController[] settingsControllers = FindObjectsByType<AppSettingsUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (AppSettingsUIController controller in settingsControllers) { controller.Initialize(user, databaseManager); }

        ProfileEditor[] profileEditors = FindObjectsByType<ProfileEditor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ProfileEditor editor in profileEditors) { editor.Initialize(user, databaseManager); }
    }

    private void OnUserLoggedOut()
    {
        // (このメソッドに変更はありません)
        Debug.Log("ユーザーがログアウトしました");
        ShowPanel(loginUIHandler.gameObject);
        loginUIHandler?.ClearInputFields();
        HideLoading();
        CurrentUser = null;
    }

    private void OnSessionExpired()
    {
        // (このメソッドに変更はありません)
        Debug.Log("セッションが期限切れです");
        CurrentUser = null;
        HideLoading();
        if (loginUIHandler != null)
        {
            ShowPanel(loginUIHandler.gameObject);
            loginUIHandler.ShowLoginError("セッションが期限切れです。再度ログインしてください。");
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
        }
        else
        {
            PlayerPrefs.SetString("SessionErrorMessage", "セッションが期限切れです。再度ログインしてください。");
            PlayerPrefs.Save();
            SceneManager.LoadScene("LoginScene"); // ※ご自身のシーン名に合わせてください
        }
    }
    private void OnAuthError(string errorMessage)
    {
        // (このメソッドに変更はありません)
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

    // ★修正点4: 遷移完了時にフラグをリセットするように変更
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

        // 処理が完了したのでフラグをリセット
        isProcessing = false;
        isHandlingRegistration = false;
        HideLoading();
    }

    // ★修正点5: データベース保存メソッドが User オブジェクトを返すように変更
    private User SaveUserToDatabase(string name, string email, string firebaseUserId)
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
            Debug.Log($"ユーザー情報をデータベースに保存しました。Name: {name}");
            return newUser; // 保存したユーザー情報を返す
        }
        catch (System.Exception e)
        {
            Debug.LogError($"データベースへの保存でエラーが発生しました: {e.Message}");
            return null; // 失敗したらnullを返す
        }
    }

    void OnDestroy()
    {
        // (このメソッドに変更はありません)
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