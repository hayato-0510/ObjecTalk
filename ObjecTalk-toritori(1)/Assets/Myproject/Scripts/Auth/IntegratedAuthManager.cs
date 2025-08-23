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
        // UIハンドラーからのイベントを購読
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

        // AuthenticationManagerのイベントに登録
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

    private async void HandleLoginRequested(string email, string password)
    {
        if (isProcessing) return;
        isProcessing = true;
        ShowLoading("ログイン中...");

        bool success = await AuthenticationManager.Instance.LoginAsync(email, password);

        isProcessing = false;
        HideLoading();
        if (!success)
        {
            // エラーはOnAuthErrorイベントで処理される
            loginUIHandler.ShowLoginError("ログインに失敗しました。");
        }
    }

    private async void HandleRegistrationRequested(string name, string email, string password)
    {
        if (isProcessing) return;
        isProcessing = true;
        ShowLoading("アカウントを作成中...");

        bool success = await AuthenticationManager.Instance.RegisterAsync(email, password, name);

        if (success)
        {
            Debug.Log($"ユーザー登録に成功しました！");

            // メール認証の送信
            if (sendVerificationEmail && AuthenticationManager.Instance.CurrentUser != null)
            {
                await AuthenticationManager.Instance.CurrentUser.SendEmailVerificationAsync();
                Debug.Log("確認メールを送信しました。");
            }

            // データベースにユーザー情報を保存
            SaveUserToDatabase(name, email, AuthenticationManager.Instance.CurrentUser.UserId);

            Debug.Log("新規登録が完了しました。次の画面に遷移します。");
            // 成功時の処理はOnUserLoggedInイベントで実行される
        }
        else
        {
            // エラーはOnAuthErrorイベントで処理される
            registrationUIHandler.ShowRegistrationError("登録に失敗しました。");
        }

        isProcessing = false;
        HideLoading();
    }

    private async void HandleForgotPasswordRequested(string email)
    {
        ShowLoading("パスワードリセットメールを送信中...");

        bool success = await AuthenticationManager.Instance.SendPasswordResetEmailAsync(email);

        HideLoading();
        if (success)
        {
            loginUIHandler.ShowLoginSuccess("パスワードリセット用のメールを送信しました。");
        }
        else
        {
            loginUIHandler.ShowLoginError("パスワードリセットメールの送信に失敗しました。");
        }
    }

    // ========== AuthenticationManager イベントハンドラー ==========

    private void OnUserLoggedIn(FirebaseUser user)
    {
        Debug.Log($"ユーザーがログインしました: {user.Email}");

        User loggedInUser = databaseManager.GetUser(user.UserId);

        if (loggedInUser != null)
        {
            // ------------------------------------------------------------------
            // ★修正点: シーン内のすべての初期化対象コンポーネントを見つけてループする ★
            // ------------------------------------------------------------------

            // ToggleSwitchを初期化
            ToggleSwitch[] toggleSwitches = FindObjectsByType<ToggleSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ToggleSwitch toggle in toggleSwitches)
            {
                toggle.Initialize(loggedInUser, databaseManager);
            }

            // AppSettingsUIControllerを初期化
            AppSettingsUIController[] settingsControllers = FindObjectsByType<AppSettingsUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (AppSettingsUIController controller in settingsControllers)
            {
                controller.Initialize(loggedInUser, databaseManager);
            }

            // ★新規追加: ProfileEditorを初期化
            ProfileEditor[] profileEditors = FindObjectsByType<ProfileEditor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ProfileEditor editor in profileEditors)
            {
                editor.Initialize(loggedInUser, databaseManager);
            }
        }
        else
        {
            Debug.LogError("データベースにログインしたユーザーが見つかりません。");
        }

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

        if (registrationUIHandler.gameObject.activeSelf)
        {
            registrationUIHandler.ShowRegistrationError(parsedMessage);
        }
        else
        {
            loginUIHandler.ShowLoginError(parsedMessage);
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
            // 1. まずパネルをアクティブにする
            uiManager.ShowPanel(panelToShowOnSuccess);

            // 2. アクティブになったパネルの中からトグルスイッチを探す
            // GetComponentsInChildren<ToggleSwitch>(true) は非アクティブな子要素も検索対象に含める
            ToggleSwitch[] toggleSwitches = panelToShowOnSuccess.GetComponentsInChildren<ToggleSwitch>(true);
            Debug.Log($"遷移先パネルで見つかったToggleSwitchの数: {toggleSwitches.Length}");

            // 3. 見つかった各トグルを初期化する
            if (loggedInUser != null)
            {
                foreach (ToggleSwitch toggle in toggleSwitches)
                {
                    toggle.Initialize(loggedInUser, databaseManager);
                }
            }
        }
        else if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            // シーンをまたぐ場合は、DontDestroyOnLoadを使うなど別のデータ永続化手法が必要です
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            loginUIHandler.ShowLoginSuccess("ログインが完了しました。");
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
                DateOfBirth = "", // 登録時は空文字列としておく
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
            loginUIHandler.OnForgotPasswordRequested -= HandleForgotPasswordRequested;
            loginUIHandler.OnSwitchToRegisterClicked -= () => ShowPanel(registrationUIHandler.gameObject);
        }
        if (registrationUIHandler != null)
        {
            registrationUIHandler.OnRegistrationRequested -= HandleRegistrationRequested;
            registrationUIHandler.OnSwitchToLoginClicked -= () => ShowPanel(loginUIHandler.gameObject);
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