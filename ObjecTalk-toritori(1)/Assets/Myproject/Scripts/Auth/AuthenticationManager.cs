using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using Firebase.Auth;

public class SimplifiedIntegratedAuthManager : MonoBehaviour
{
    [Header("新規登録UI")]
    public GameObject registrationPanel;
    public TMP_InputField nameInputField;
    public TMP_InputField regEmailInputField;
    public TMP_InputField regPasswordInputField;
    public Button registerButton;
    public TMP_Text nameHintText;
    public TMP_Text regPasswordHintText;
    public TMP_Text registrationErrorText;

    [Header("ログインUI")]
    public GameObject loginPanel;
    public TMP_InputField loginEmailInputField;
    public TMP_InputField loginPasswordInputField;
    public Button loginButton;
    public Button forgotPasswordButton;
    public TMP_Text loginErrorText;

    [Header("共通UI")]
    public GameObject loadingPanel;
    public TMP_Text loadingText;

    [Header("パネル切り替え")]
    public Button switchToRegisterButton;
    public Button switchToLoginButton;

    [Header("Manager References")]
    public FlexibleUIManager uiManager;

    [Header("成功時の遷移")]
    public GameObject panelToShowOnSuccess;
    public string mainMenuSceneName = "MainMenu";

    [Header("開発設定")]
    public bool sendVerificationEmail = false;

    private bool isProcessing = false;
    private DatabaseManager databaseManager;

    void Start()
    {
        databaseManager = new DatabaseManager();
        InitializeUI();
        SetupEventListeners();
        SubscribeToAuthEvents();
    }

    private void SubscribeToAuthEvents()
    {
        // AuthenticationManagerのイベントに登録
        AuthenticationManager.OnUserLoggedIn += OnUserLoggedIn;
        AuthenticationManager.OnUserLoggedOut += OnUserLoggedOut;
        AuthenticationManager.OnSessionExpired += OnSessionExpired;
        AuthenticationManager.OnAuthError += OnAuthError;
    }

    private void InitializeUI()
    {
        // 初期画面の設定
        if (uiManager == null)
        {
            ShowAllPanels(false);
            loginPanel?.SetActive(true);
        }
        else
        {
            if (loginPanel != null)
                uiManager.ShowPanel(loginPanel);
        }

        // エラーテキストとヒントテキストを初期化
        HideAllErrors();

        if (nameHintText != null)
            nameHintText.gameObject.SetActive(false);
        if (regPasswordHintText != null)
            regPasswordHintText.gameObject.SetActive(false);

        // 記憶されたメールアドレスを復元
        RestoreRememberedEmail();

        // 初期状態でボタンの状態を更新
        UpdateAllButtons();
    }

    private void RestoreRememberedEmail()
    {
        if (AuthenticationManager.Instance != null)
        {
            string rememberedEmail = AuthenticationManager.Instance.GetRememberedEmail();
            if (!string.IsNullOrEmpty(rememberedEmail) && loginEmailInputField != null)
            {
                loginEmailInputField.text = rememberedEmail;
            }
        }
    }

    private void SetupEventListeners()
    {
        // ボタンイベント
        switchToRegisterButton?.onClick.AddListener(OnSwitchToRegisterClicked);
        switchToLoginButton?.onClick.AddListener(OnSwitchToLoginClicked);
        registerButton?.onClick.AddListener(HandleRegisterButtonClicked);
        loginButton?.onClick.AddListener(HandleLoginButtonClicked);
        forgotPasswordButton?.onClick.AddListener(HandleForgotPasswordClicked);

        // 入力フィールドイベント
        nameInputField?.onValueChanged.AddListener(OnNameChanged);
        regEmailInputField?.onValueChanged.AddListener(OnRegEmailChanged);
        regPasswordInputField?.onValueChanged.AddListener(OnRegPasswordChanged);
        loginEmailInputField?.onValueChanged.AddListener(OnLoginEmailChanged);
        loginPasswordInputField?.onValueChanged.AddListener(OnLoginPasswordChanged);
    }

    // ============ AuthenticationManager イベントハンドラー ============
    private void OnUserLoggedIn(FirebaseUser user)
    {
        Debug.Log($"ユーザーがログインしました: {user.Email}");
        isProcessing = false;
        ShowLoading(false);
        StartCoroutine(TransitionToMainMenu());
    }

    private void OnUserLoggedOut()
    {
        Debug.Log("ユーザーがログアウトしました");
        ShowAllPanels(false);

        if (uiManager != null && loginPanel != null)
        {
            uiManager.ShowPanel(loginPanel);
        }
        else
        {
            loginPanel?.SetActive(true);
        }

        ClearAllInputFields();
        HideAllErrors();
        isProcessing = false;
        ShowLoading(false);
    }

    private void OnSessionExpired()
    {
        Debug.Log("セッションが期限切れです");
        ShowLoginError("セッションが期限切れです。再度ログインしてください。");
        isProcessing = false;
        ShowLoading(false);
    }

    private void OnAuthError(string errorMessage)
    {
        Debug.LogError($"認証エラー: {errorMessage}");

        // エラーメッセージを適切なテキストフィールドに表示
        if (uiManager != null && registrationPanel != null && uiManager.IsPanelActive(registrationPanel))
        {
            ShowRegistrationError(ParseErrorMessage(errorMessage));
        }
        else
        {
            ShowLoginError(ParseErrorMessage(errorMessage));
        }

        isProcessing = false;
        ShowLoading(false);
        UpdateAllButtons();
    }

    // ============ パネル切り替え処理 ============
    public void OnSwitchToRegisterClicked()
    {
        Debug.Log("新規登録画面に切り替えます");
        if (uiManager != null && registrationPanel != null)
        {
            uiManager.ShowPanel(registrationPanel);
        }
        else
        {
            ShowAllPanels(false);
            registrationPanel?.SetActive(true);
        }
        HideAllErrors();
        UpdateAllButtons();
    }

    public void OnSwitchToLoginClicked()
    {
        Debug.Log("ログイン画面に切り替えます");
        if (uiManager != null && loginPanel != null)
        {
            uiManager.ShowPanel(loginPanel);
        }
        else
        {
            ShowAllPanels(false);
            loginPanel?.SetActive(true);
        }
        HideAllErrors();
        UpdateAllButtons();
    }

    // ============ 新規登録関連の処理 ============
    private void OnNameChanged(string value)
    {
        HideRegistrationError();
        ValidateNameRealtime();
        UpdateRegisterButton();
    }

    private void OnRegEmailChanged(string value)
    {
        HideRegistrationError();
        UpdateRegisterButton();
    }

    private void OnRegPasswordChanged(string value)
    {
        HideRegistrationError();
        ValidateRegPasswordRealtime();
        UpdateRegisterButton();
    }

    private void ValidateNameRealtime()
    {
        if (nameHintText == null || nameInputField == null) return;

        string name = nameInputField.text;
        int length = name.Length;

        if (length == 0)
        {
            nameHintText.gameObject.SetActive(false);
        }
        else if (length < 2)
        {
            nameHintText.gameObject.SetActive(true);
            nameHintText.text = "名前は2文字以上で入力してください。";
            nameHintText.color = Color.red;
        }
        else if (length > 20)
        {
            nameHintText.gameObject.SetActive(true);
            nameHintText.text = "名前は20文字以内で入力してください。";
            nameHintText.color = Color.red;
        }
        else
        {
            nameHintText.gameObject.SetActive(false);
        }
    }

    private void ValidateRegPasswordRealtime()
    {
        if (regPasswordHintText == null || regPasswordInputField == null) return;

        string password = regPasswordInputField.text;
        int length = password.Length;

        if (length == 0)
        {
            regPasswordHintText.gameObject.SetActive(false);
        }
        else if (length < 6)
        {
            regPasswordHintText.gameObject.SetActive(true);
            regPasswordHintText.text = "パスワードが弱すぎます。\n6文字以上で設定してください。";
            regPasswordHintText.color = Color.red;
        }
        else
        {
            regPasswordHintText.gameObject.SetActive(false);
        }
    }

    private void UpdateRegisterButton()
    {
        if (registerButton == null) return;

        bool isNameValid = nameInputField != null && !string.IsNullOrEmpty(nameInputField.text) &&
                          nameInputField.text.Length >= 2 && nameInputField.text.Length <= 20;
        bool isEmailValid = regEmailInputField != null && !string.IsNullOrEmpty(regEmailInputField.text) &&
                           IsValidEmail(regEmailInputField.text);
        bool isPasswordValid = regPasswordInputField != null && regPasswordInputField.text.Length >= 6;
        bool isAuthManagerReady = AuthenticationManager.Instance != null && AuthenticationManager.Instance.Auth != null;

        registerButton.interactable = isNameValid && isEmailValid && isPasswordValid && isAuthManagerReady && !isProcessing;
        UpdateButtonVisual(registerButton);
    }

    private async void HandleRegisterButtonClicked()
    {
        if (isProcessing || AuthenticationManager.Instance == null) return;

        isProcessing = true;
        ShowLoading(true, "アカウントを作成中...");
        HideRegistrationError();
        UpdateRegisterButton();

        string name = nameInputField.text.Trim();
        string email = regEmailInputField.text.Trim();
        string password = regPasswordInputField.text;

        // 入力値の事前検証
        if (!PreValidateRegistrationInput(name, email, password))
        {
            isProcessing = false;
            ShowLoading(false);
            UpdateRegisterButton();
            return;
        }

        try
        {
            Debug.Log("Firebase認証処理を開始します...");
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
        }
        catch (System.Exception e)
        {
            Debug.LogError($"予期しないエラーが発生しました: {e.Message}");
            ShowRegistrationError("予期しないエラーが発生しました。しばらく後でもう一度お試しください。");
            isProcessing = false;
            ShowLoading(false);
            UpdateRegisterButton();
        }
    }

    // ============ ログイン関連の処理 ============
    private void OnLoginEmailChanged(string value)
    {
        HideLoginError();
        UpdateLoginButton();
    }

    private void OnLoginPasswordChanged(string value)
    {
        HideLoginError();
        UpdateLoginButton();
    }

    private void UpdateLoginButton()
    {
        if (loginButton == null) return;

        bool isEmailValid = loginEmailInputField != null && !string.IsNullOrEmpty(loginEmailInputField.text) &&
                           IsValidEmail(loginEmailInputField.text);
        bool isPasswordValid = loginPasswordInputField != null && !string.IsNullOrEmpty(loginPasswordInputField.text);
        bool isAuthManagerReady = AuthenticationManager.Instance != null && AuthenticationManager.Instance.Auth != null;

        loginButton.interactable = isEmailValid && isPasswordValid && isAuthManagerReady && !isProcessing;
        UpdateButtonVisual(loginButton);
    }

    private async void HandleLoginButtonClicked()
    {
        if (isProcessing || AuthenticationManager.Instance == null) return;

        isProcessing = true;
        ShowLoading(true, "ログイン中...");
        HideLoginError();
        UpdateLoginButton();

        string email = loginEmailInputField.text.Trim();
        string password = loginPasswordInputField.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowLoginError("メールアドレスとパスワードを入力してください。");
            isProcessing = false;
            ShowLoading(false);
            UpdateLoginButton();
            return;
        }

        try
        {
            bool success = await AuthenticationManager.Instance.LoginAsync(email, password);

            if (success)
            {
                Debug.Log($"ログイン成功: {email}");
                // 成功時の処理はOnUserLoggedInイベントで実行される
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"予期しないエラーが発生しました: {e.Message}");
            ShowLoginError("予期しないエラーが発生しました。しばらく後でもう一度お試しください。");
            isProcessing = false;
            ShowLoading(false);
            UpdateLoginButton();
        }
    }

    private async void HandleForgotPasswordClicked()
    {
        if (AuthenticationManager.Instance == null) return;

        if (loginEmailInputField == null || string.IsNullOrEmpty(loginEmailInputField.text))
        {
            ShowLoginError("パスワードリセット用のメールアドレスを入力してください。");
            return;
        }

        string email = loginEmailInputField.text.Trim();
        if (!IsValidEmail(email))
        {
            ShowLoginError("有効なメールアドレスを入力してください。");
            return;
        }

        ShowLoading(true, "パスワードリセットメールを送信中...");

        try
        {
            bool success = await AuthenticationManager.Instance.SendPasswordResetEmailAsync(email);

            if (success)
            {
                ShowLoginSuccess("パスワードリセット用のメールを送信しました。");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"予期しないエラーが発生しました: {e.Message}");
            ShowLoginError("パスワードリセットメールの送信に失敗しました。");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    // ============ 成功時の遷移処理 ============
    private IEnumerator TransitionToMainMenu()
    {
        ShowLoading(true, "ログイン処理完了。画面を切り替えています...");
        yield return new WaitForSeconds(1f);

        if (uiManager != null && panelToShowOnSuccess != null)
        {
            Debug.Log("パネル遷移を実行します。");
            uiManager.ShowPanel(panelToShowOnSuccess);
            ShowLoading(false);
        }
        else if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.Log("シーン遷移を実行します。");
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            ShowLoading(false);
            ShowLoginSuccess("ログインが完了しました。");
        }
    }

    // ============ エラー表示関連 ============
    private void ShowLoginError(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.color = Color.red;
            loginErrorText.gameObject.SetActive(true);
            StartCoroutine(HideLoginErrorAfterDelay(5f));
        }
        Debug.LogError($"[LOGIN ERROR] {message}");
    }

    private void ShowLoginSuccess(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.color = Color.green;
            loginErrorText.gameObject.SetActive(true);
            StartCoroutine(HideLoginErrorAfterDelay(3f));
        }
        Debug.Log($"[LOGIN SUCCESS] {message}");
    }

    private void HideLoginError()
    {
        if (loginErrorText != null)
        {
            loginErrorText.gameObject.SetActive(false);
        }
    }

    private void ShowRegistrationError(string message)
    {
        if (registrationErrorText != null)
        {
            registrationErrorText.text = message;
            registrationErrorText.color = Color.red;
            registrationErrorText.gameObject.SetActive(true);
            StartCoroutine(HideRegistrationErrorAfterDelay(5f));
        }
        Debug.LogError($"[REGISTRATION ERROR] {message}");
    }

    private void ShowRegistrationSuccess(string message)
    {
        if (registrationErrorText != null)
        {
            registrationErrorText.text = message;
            registrationErrorText.color = Color.green;
            registrationErrorText.gameObject.SetActive(true);
            StartCoroutine(HideRegistrationErrorAfterDelay(3f));
        }
        Debug.Log($"[REGISTRATION SUCCESS] {message}");
    }

    private void HideRegistrationError()
    {
        if (registrationErrorText != null)
        {
            registrationErrorText.gameObject.SetActive(false);
        }
    }

    private void HideAllErrors()
    {
        HideLoginError();
        HideRegistrationError();
    }

    private IEnumerator HideLoginErrorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideLoginError();
    }

    private IEnumerator HideRegistrationErrorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideRegistrationError();
    }

    // ============ 共通処理 ============
    private void UpdateAllButtons()
    {
        UpdateRegisterButton();
        UpdateLoginButton();
    }

    private void ShowAllPanels(bool show)
    {
        registrationPanel?.SetActive(show);
        loginPanel?.SetActive(show);
    }

    private void ShowLoading(bool show, string message = "")
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(show);
        }

        if (loadingText != null && show && !string.IsNullOrEmpty(message))
        {
            loadingText.text = message;
        }
    }

    private void ClearAllInputFields()
    {
        if (nameInputField != null) nameInputField.text = "";
        if (regEmailInputField != null) regEmailInputField.text = "";
        if (regPasswordInputField != null) regPasswordInputField.text = "";
        if (loginPasswordInputField != null) loginPasswordInputField.text = "";

        // ログインメールアドレスは記憶されている場合があるので条件付きでクリア
        if (loginEmailInputField != null && AuthenticationManager.Instance != null)
        {
            string rememberedEmail = AuthenticationManager.Instance.GetRememberedEmail();
            if (string.IsNullOrEmpty(rememberedEmail))
            {
                loginEmailInputField.text = "";
            }
        }
    }

    private void UpdateButtonVisual(Button button)
    {
        if (button == null) return;

        Image buttonImage = button.GetComponent<Image>();
        if (buttonImage != null)
        {
            if (button.interactable)
            {
                buttonImage.color = Color.white;
            }
            else
            {
                buttonImage.color = new Color(0.8f, 0.8f, 0.8f, 0.5f);
            }
        }
    }

    private bool IsValidEmail(string email)
    {
        return !string.IsNullOrEmpty(email) && email.Contains("@") && email.Contains(".");
    }

    private bool PreValidateRegistrationInput(string name, string email, string password)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 20)
        {
            ShowRegistrationError("名前は2文字以上20文字以内で入力してください。");
            return false;
        }

        if (string.IsNullOrEmpty(email) || !IsValidEmail(email))
        {
            ShowRegistrationError("有効なメールアドレスを入力してください。");
            return false;
        }

        if (string.IsNullOrEmpty(password) || password.Length < 6)
        {
            ShowRegistrationError("パスワードは6文字以上で設定してください。");
            return false;
        }

        return true;
    }

    private void SaveUserToDatabase(string name, string email, string firebaseUserId)
    {
        try
        {
            User newUser = new User
            {
                user_id = firebaseUserId,
                Name = name,
                Age = 0,
                GmailAddress = email,
                IsCalendarSyncEnabled = false,
                IsWeatherSyncEnabled = false
            };

            databaseManager.SaveUser(newUser);
            Debug.Log($"ユーザー情報をデータベースに保存しました。Name: {name}, Email: {email}, Firebase UID: {firebaseUserId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"データベースへの保存でエラーが発生しました: {e.Message}");
        }
    }

    private string ParseErrorMessage(string originalMessage)
    {
        // AuthenticationManagerから受け取ったエラーメッセージを解析
        if (originalMessage.Contains("Login failed:") || originalMessage.Contains("Registration failed:"))
        {
            // "Login failed: " や "Registration failed: " を除去
            string[] parts = originalMessage.Split(new string[] { ": " }, 2, System.StringSplitOptions.None);
            if (parts.Length > 1)
            {
                string firebaseError = parts[1];

                // Firebase固有のエラーメッセージを日本語に変換
                if (firebaseError.Contains("weak-password"))
                    return "パスワードが弱すぎます。6文字以上で設定してください。";
                else if (firebaseError.Contains("invalid-email"))
                    return "メールアドレスの形式が正しくありません。";
                else if (firebaseError.Contains("email-already-in-use"))
                    return "このメールアドレスは既に使用されています。";
                else if (firebaseError.Contains("wrong-password") || firebaseError.Contains("invalid-credential"))
                    return "メールアドレスまたはパスワードが正しくありません。";
                else if (firebaseError.Contains("user-not-found"))
                    return "このメールアドレスのアカウントが見つかりません。";
                else if (firebaseError.Contains("too-many-requests"))
                    return "リクエストが多すぎます。しばらくお待ちください。";
                else
                    return "メールアドレス\nまたは\nパスワードが違います。";
            }
        }

        return "予期しないエラーが発生しました。";
    }

    // ============ デバッグ用メソッド ============
    [ContextMenu("Manual Sign Out")]
    public void ManualSignOut()
    {
        if (AuthenticationManager.Instance != null)
        {
            AuthenticationManager.Instance.SignOut();
        }
    }

    [ContextMenu("Check Auth State")]
    public void CheckAuthState()
    {
        if (AuthenticationManager.Instance != null)
        {
            Debug.Log($"Auth State: {AuthenticationManager.Instance.CurrentLoginState}");
            Debug.Log($"Is Logged In: {AuthenticationManager.Instance.IsLoggedIn}");
            Debug.Log($"Current User: {AuthenticationManager.Instance.CurrentUser?.Email ?? "None"}");
        }
        else
        {
            Debug.Log("AuthenticationManager instance not found");
        }
    }

    void OnDestroy()
    {
        // イベント購読を解除
        if (AuthenticationManager.Instance != null)
        {
            AuthenticationManager.OnUserLoggedIn -= OnUserLoggedIn;
            AuthenticationManager.OnUserLoggedOut -= OnUserLoggedOut;
            AuthenticationManager.OnSessionExpired -= OnSessionExpired;
            AuthenticationManager.OnAuthError -= OnAuthError;
        }
    }
}