using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Firebase.Auth;

public class IntegratedAuthManager : MonoBehaviour
{
    // シングルトン化するためのインスタンス
    public static IntegratedAuthManager Instance { get; private set; }

    // 外部から参照できる現在のユーザー情報
    public User CurrentUser { get; private set; }

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

    [Header("Collection Panel")]
    public SlotGrid collectionSlotGrid;

    [Header("開発設定")]
    public bool sendVerificationEmail = false;

    private bool isProcessing = false;
    private DatabaseManager databaseManager;

    void Awake()
    {
        // シングルトンの設定
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // シーンをまたいで永続化させたい場合はこの行を有効化
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
        // CollectionPanelManagerのインスタンスが存在すれば、スロットの状態をリセット
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

        // データベースからユーザーのオブジェクトリストを取得
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
            loginUIHandler.ShowLoginError("ログインに失敗しました。");
            // ★変更点: IntegratedAuthManagerがコルーチンを開始
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
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
            if (sendVerificationEmail && AuthenticationManager.Instance.CurrentUser != null)
            {
                await AuthenticationManager.Instance.CurrentUser.SendEmailVerificationAsync();
                Debug.Log("確認メールを送信しました。");
            }
            SaveUserToDatabase(name, email, AuthenticationManager.Instance.CurrentUser.UserId);
        }
        else
        {
            registrationUIHandler.ShowRegistrationError("登録に失敗しました。");
            // こちらも必要であれば同様にコルーチンを開始する修正を追加
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
            // ★変更点: 成功メッセージも一定時間後に消す
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(3f));
        }
        else
        {
            loginUIHandler.ShowLoginError("パスワードリセットメールの送信に失敗しました。");
            // ★変更点: IntegratedAuthManagerがコルーチンを開始
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
        }
    }

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
            // 他のUI要素の初期化処理...

            RefreshCollectionDisplay();
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
        CurrentUser = null; // ユーザー情報をクリア
    }

    private void OnSessionExpired()
    {
        Debug.Log("セッションが期限切れです");
        ShowPanel(loginUIHandler.gameObject);
        loginUIHandler.ShowLoginError("セッションが期限切れです。再度ログインしてください。");
        StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
        HideLoading();
        CurrentUser = null; // ユーザー情報をクリア
    }

    private void OnAuthError(string errorMessage)
    {
        Debug.LogError($"認証エラー: {errorMessage}");
        string parsedMessage = AuthUtils.ParseErrorMessage(errorMessage);
        if (registrationUIHandler != null && registrationUIHandler.gameObject.activeSelf)
        {
            registrationUIHandler.ShowRegistrationError(parsedMessage);
            // こちらも必要であれば同様にコルーチンを開始する修正を追加
        }
        else if (loginUIHandler != null)
        {
            loginUIHandler.ShowLoginError(parsedMessage);
            // ★変更点: IntegratedAuthManagerがコルーチンを開始
            StartCoroutine(loginUIHandler.HideErrorAfterDelay(5f));
        }
        HideLoading();
    }

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
        // ★改善点: 登録したすべてのイベントを解除する
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