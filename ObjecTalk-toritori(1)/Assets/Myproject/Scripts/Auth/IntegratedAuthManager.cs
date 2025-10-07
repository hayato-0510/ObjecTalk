using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Firebase.Auth;

public class IntegratedAuthManager : MonoBehaviour
{
    [Header("UI Handler References")]
    public LoginUIHandler loginUIHandler;
    public RegistrationUIHandler registrationUIHandler;
    public FlexibleUIManager uiManager;

    [Header("連携する画面の管理コンポーネント")]
    [SerializeField] private ProfileEditor profileEditor;

    [Header("共通UI")]
    public GameObject loadingPanel;
    public TMPro.TMP_Text loadingText;

    [Header("成功時の遷移")]
    public GameObject panelToShowOnSuccess;
    public string mainMenuSceneName = "MainMenu";

    [Header("開発設定")]
    public bool sendVerificationEmail = false;

    private bool isProcessing = false;
    private DatabaseManager databaseManager;
    private User loggedInUser;

    // ★修正点1: 新規登録処理中であることを示すフラグを追加
    private bool isHandlingRegistration = false;

    void Awake()
    {
        databaseManager = new DatabaseManager();
    }

    void Start()
    {
        SetupEvents();
        ShowPanel(loginUIHandler.gameObject);
        HideLoading();
    }

    private void SetupEvents()
    {
        // ... (この部分に変更はありません) ...
        if (loginUIHandler != null)
        {
            loginUIHandler.OnLoginRequested += HandleLoginRequested;
            loginUIHandler.OnForgotPasswordRequested += HandleForgotPasswordRequested;
            loginUIHandler.OnSwitchToRegisterClicked += () => ShowPanel(registrationUIHandler.gameObject);
        }

        if (registrationUIHandler != null)
        {
            registrationUIHandler.OnRegistrationRequested += HandleRegistrationRequested;
            registrationUIHandler.OnSwitchToLoginClicked += () => ShowPanel(loginUIHandler.gameObject);
        }

        AuthenticationManager.OnUserLoggedIn += OnUserLoggedIn;
        AuthenticationManager.OnUserLoggedOut += OnUserLoggedOut;
        AuthenticationManager.OnSessionExpired += OnSessionExpired;
        AuthenticationManager.OnAuthError += OnAuthError;
    }

    // ... ShowPanel, HandleLoginRequested, HandleForgotPasswordRequested に変更はありません ...
    private void ShowPanel(GameObject panel)
    {
        if (uiManager != null) { uiManager.ShowPanel(panel); }
        else
        {
            loginUIHandler?.gameObject.SetActive(panel == loginUIHandler.gameObject);
            registrationUIHandler?.gameObject.SetActive(panel == registrationUIHandler.gameObject);
        }
    }

    private async void HandleLoginRequested(string email, string password)
    {
        if (isProcessing) return;
        isProcessing = true;
        ShowLoading("ログイン中...");
        bool success = await AuthenticationManager.Instance.LoginAsync(email, password);
        isProcessing = false;
        if (!success)
        {
            HideLoading();
            loginUIHandler.ShowLoginError("ログインに失敗しました。");
        }
    }

    private async void HandleForgotPasswordRequested(string email)
    {
        ShowLoading("パスワードリセットメールを送信中...");
        bool success = await AuthenticationManager.Instance.SendPasswordResetEmailAsync(email);
        HideLoading();
        if (success) { loginUIHandler.ShowLoginSuccess("パスワードリセット用のメールを送信しました。"); }
        else { loginUIHandler.ShowLoginError("パスワードリセットメールの送信に失敗しました。"); }
    }


    // ★修正点2: HandleRegistrationRequested メソッドを大幅に書き換える
    private async void HandleRegistrationRequested(string name, string email, string password)
    {
        if (isProcessing) return;

        isProcessing = true;
        isHandlingRegistration = true; // 登録処理の開始を通知
        ShowLoading("アカウントを作成中...");

        bool success = await AuthenticationManager.Instance.RegisterAsync(email, password, name);

        if (success)
        {
            Debug.Log($"Firebaseへのユーザー登録に成功しました！");

            // DBへの保存処理
            User newUser = SaveUserToDatabase(name, email, AuthenticationManager.Instance.CurrentUser.UserId);

            if (newUser != null)
            {
                // DB保存成功後、手動で初期化と画面遷移を行う
                InitializeAllComponents(newUser);
                StartCoroutine(TransitionToMainMenu());
            }
            else
            {
                // DB保存に失敗した場合
                OnAuthError("データベースへのユーザー保存に失敗しました。");
            }
        }

        // isProcessingは遷移後またはエラー表示後にfalseにする
        isHandlingRegistration = false; // 登録処理の終了を通知
    }

    // ========== AuthenticationManager イベントハンドラー ==========

    // ★修正点3: OnUserLoggedIn メソッドをシンプルにする
    private void OnUserLoggedIn(FirebaseUser user)
    {
        // 新規登録処理中は、このイベントを無視する
        if (isHandlingRegistration)
        {
            Debug.Log("新規登録処理中のため、自動ログインイベントはスキップします。");
            return;
        }

        Debug.Log($"通常のログインフローを実行します: {user.Email}");

        User appUser = databaseManager.GetUser(user.UserId);
        InitializeAllComponents(appUser);
        StartCoroutine(TransitionToMainMenu());
    }

    private void OnUserLoggedOut()
    {
        Debug.Log("ユーザーがログアウトしました");
        ShowPanel(loginUIHandler.gameObject);
        loginUIHandler?.ClearInputFields();
        HideLoading();
    }

    private void OnSessionExpired()
    {
        Debug.Log("セッションが期限切れです");
        ShowPanel(loginUIHandler.gameObject);
        loginUIHandler.ShowLoginError("セッションが期限切れです。再度ログインしてください。");
        HideLoading();
    }

    private void OnAuthError(string errorMessage)
    {
        Debug.LogError($"認証エラー: {errorMessage}");
        string parsedMessage = AuthUtils.ParseErrorMessage(errorMessage);

        if (registrationUIHandler != null && registrationUIHandler.gameObject.activeSelf)
        {
            registrationUIHandler.ShowRegistrationError(parsedMessage);
        }
        else
        {
            loginUIHandler?.ShowLoginError(parsedMessage);
        }
        isProcessing = false;
        HideLoading();
    }

    // 不要になったDelayedInitializationコルーチンは削除

    private void InitializeAllComponents(User user)
    {
        if (user == null)
        {
            Debug.LogError("初期化しようとしましたが、ユーザー情報がnullです。");
            return;
        }

        this.loggedInUser = user;

        ToggleSwitch[] toggleSwitches = FindObjectsByType<ToggleSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ToggleSwitch toggle in toggleSwitches) { toggle.Initialize(this.loggedInUser, databaseManager); }

        AppSettingsUIController[] settingsControllers = FindObjectsByType<AppSettingsUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (AppSettingsUIController controller in settingsControllers) { controller.Initialize(this.loggedInUser, databaseManager); }

        ProfileEditor[] profileEditors = FindObjectsByType<ProfileEditor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ProfileEditor editor in profileEditors) { editor.Initialize(this.loggedInUser, databaseManager); }
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
        ShowLoading("処理完了。画面を切り替えています...");
        yield return new WaitForSeconds(1f);

        if (uiManager != null && panelToShowOnSuccess != null) { uiManager.ShowPanel(panelToShowOnSuccess); }
        else if (!string.IsNullOrEmpty(mainMenuSceneName)) { SceneManager.LoadScene(mainMenuSceneName); }
        else { loginUIHandler.ShowLoginSuccess("ログインが完了しました。"); }

        isProcessing = false; // 遷移または成功表示が完了した時点でfalseに
        HideLoading();
    }

    // ★修正点4: SaveUserToDatabaseがUserオブジェクトを返すように変更
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
            Debug.Log($"ユーザー({name})をデータベースに保存しました。");
            return newUser;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"データベースへの保存中に例外が発生しました: {e.Message}");
            return null;
        }
    }

    void OnDestroy()
    {
        // ... (この部分に変更はありません) ...
        if (loginUIHandler != null)
        {
            loginUIHandler.OnLoginRequested -= HandleLoginRequested;
            loginUIHandler.OnForgotPasswordRequested -= HandleForgotPasswordRequested;
        }
        if (registrationUIHandler != null)
        {
            registrationUIHandler.OnRegistrationRequested -= HandleRegistrationRequested;
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