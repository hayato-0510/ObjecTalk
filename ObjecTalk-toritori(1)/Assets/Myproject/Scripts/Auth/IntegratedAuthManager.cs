using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine.SceneManagement;
using System.Collections;

public class IntegratedAuthManager : MonoBehaviour
{
    [Header("新規登録UI")]
    public GameObject registrationPanel;
    public TMP_InputField nameInputField;
    public TMP_InputField regEmailInputField;
    public TMP_InputField regPasswordInputField;
    public Button registerButton;
    public TMP_Text nameHintText;
    public TMP_Text regPasswordHintText;
    public TMP_Text registrationErrorText; // 新規登録専用エラーテキスト

    [Header("ログインUI")]
    public GameObject loginPanel;
    public TMP_InputField loginEmailInputField;
    public TMP_InputField loginPasswordInputField;
    public Button loginButton;
    public Button forgotPasswordButton;
    public TMP_Text loginErrorText; // ログイン専用エラーテキスト

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
    public bool isDebugMode = true;
    public bool sendVerificationEmail = false;

    private FirebaseAuth auth;
    private bool isFirebaseInitialized = false;
    private bool isProcessing = false;
    private DatabaseManager databaseManager;

    void Start()
    {
        databaseManager = new DatabaseManager();
        InitializeUI();
        SetupEventListeners();
        InitializeFirebase();
    }

    private void InitializeUI()
    {
        if (uiManager == null)
        {
            ShowAllPanels(false);
            if (loginPanel != null)
                loginPanel.SetActive(true);
        }
        else
        {
            if (loginPanel != null)
                uiManager.ShowPanel(loginPanel);
        }

        // 両方のエラーテキストを非表示
        HideAllErrors();

        if (nameHintText != null)
            nameHintText.gameObject.SetActive(false);
        if (regPasswordHintText != null)
            regPasswordHintText.gameObject.SetActive(false);

        if (!isDebugMode)
        {
            RestoreRememberedEmail();
        }
    }

    private void SetupEventListeners()
    {
        switchToRegisterButton?.onClick.AddListener(OnSwitchToRegisterClicked);
        switchToLoginButton?.onClick.AddListener(OnSwitchToLoginClicked);
        registerButton?.onClick.AddListener(HandleRegisterButtonClicked);
        loginButton?.onClick.AddListener(HandleLoginButtonClicked);
        forgotPasswordButton?.onClick.AddListener(HandleForgotPasswordClicked);

        nameInputField?.onValueChanged.AddListener(OnNameChanged);
        regEmailInputField?.onValueChanged.AddListener(OnRegEmailChanged);
        regPasswordInputField?.onValueChanged.AddListener(OnRegPasswordChanged);
        loginEmailInputField?.onValueChanged.AddListener(OnLoginEmailChanged);
        loginPasswordInputField?.onValueChanged.AddListener(OnLoginPasswordChanged);
    }

    private void RestoreRememberedEmail()
    {
        if (isDebugMode) return;

        string rememberedEmail = PlayerPrefs.GetString("REMEMBER_EMAIL_KEY", "");
        if (!string.IsNullOrEmpty(rememberedEmail) && loginEmailInputField != null)
        {
            loginEmailInputField.text = rememberedEmail;
        }
    }

    async void InitializeFirebase()
    {
        ShowLoading(true, "Firebaseを初期化中...");

        try
        {
            var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (dependencyStatus == DependencyStatus.Available)
            {
                auth = FirebaseAuth.DefaultInstance;
                isFirebaseInitialized = true;
                auth.StateChanged += OnAuthStateChanged;
                Debug.Log("Firebase Auth Initialized and ready!");

                if (!isDebugMode && auth.CurrentUser != null)
                {
                    Debug.Log("既にログイン済みです");
                    OnLoginSuccess();
                    return;
                }
            }
            else
            {
                isFirebaseInitialized = false;
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
                ShowLoginError("Firebaseの初期化に失敗しました。");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Firebase初期化エラー: {e.Message}");
            ShowLoginError("Firebase初期化エラーが発生しました。");
        }
        finally
        {
            ShowLoading(false);
            ValidateAllInputs();
        }
    }

    private void OnAuthStateChanged(object sender, System.EventArgs eventArgs)
    {
        if (!isDebugMode && auth.CurrentUser != null && !isProcessing)
        {
            Debug.Log($"ユーザーがサインインしました: {auth.CurrentUser.Email}");
            OnLoginSuccess();
        }
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

        registerButton.interactable = isNameValid && isEmailValid && isPasswordValid &&
                                    isFirebaseInitialized && !isProcessing;

        UpdateButtonVisual(registerButton);
    }

    private async void HandleRegisterButtonClicked()
    {
        if (isProcessing) return;

        isProcessing = true;
        ShowLoading(true, "アカウントを作成中...");
        HideRegistrationError();

        string name = nameInputField.text.Trim();
        string email = regEmailInputField.text.Trim();
        string password = regPasswordInputField.text;

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
            AuthResult result = await auth.CreateUserWithEmailAndPasswordAsync(email, password);
            Debug.Log($"ユーザー登録に成功しました！ UserID: {result.User.UserId}");

            var profile = new UserProfile { DisplayName = name };
            await result.User.UpdateUserProfileAsync(profile);

            if (sendVerificationEmail)
            {
                await result.User.SendEmailVerificationAsync();
                Debug.Log("確認メールを送信しました。");
            }

            SaveUserToDatabase(name, email, result.User.UserId);

            if (!isDebugMode)
            {
                PlayerPrefs.SetString("REMEMBER_EMAIL_KEY", email);
                PlayerPrefs.Save();
            }

            Debug.Log("新規登録が完了しました。次の画面に遷移します。");
            OnRegistrationSuccess();
        }
        catch (System.Exception e)
        {
            Debug.Log("Firebase認証でエラーが発生しました。");
            HandleRegistrationError(e);
        }
        finally
        {
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

        loginButton.interactable = isEmailValid && isPasswordValid && isFirebaseInitialized && !isProcessing;

        UpdateButtonVisual(loginButton);
    }

    private async void HandleLoginButtonClicked()
    {
        if (isProcessing) return;

        isProcessing = true;
        ShowLoading(true, "ログイン中...");
        HideLoginError();

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
            AuthResult result = await auth.SignInWithEmailAndPasswordAsync(email, password);
            Debug.Log($"ログイン成功: {result.User.Email}");

            if (!isDebugMode)
            {
                PlayerPrefs.SetString("REMEMBER_EMAIL_KEY", email);
                PlayerPrefs.Save();
            }

            Debug.Log("ログインが完了しました。次の画面に遷移します。");
            OnLoginSuccess();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ログインエラー: {e.Message}");
            HandleLoginError(e);
        }
        finally
        {
            isProcessing = false;
            ShowLoading(false);
            UpdateLoginButton();
        }
    }

    private async void HandleForgotPasswordClicked()
    {
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
            await auth.SendPasswordResetEmailAsync(email);
            ShowLoginSuccess("パスワードリセット用のメールを送信しました。");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"パスワードリセットエラー: {e.Message}");
            ShowLoginError("パスワードリセットメールの送信に失敗しました。");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    // ============ 成功時の遷移処理 ============
    private void OnRegistrationSuccess()
    {
        ShowLoading(true, "新規登録完了。画面を切り替えています...");
        StartCoroutine(TransitionToMainMenu());
    }

    private void OnLoginSuccess()
    {
        ShowLoading(true, "ログイン処理中...");
        StartCoroutine(TransitionToMainMenu());
    }

    private IEnumerator TransitionToMainMenu()
    {
        yield return new WaitForSeconds(1f);

        if (uiManager != null && panelToShowOnSuccess != null)
        {
            Debug.Log("パネル遷移を実行します。");
            uiManager.ShowPanel(panelToShowOnSuccess);
        }
        else if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.Log("シーン遷移を実行します。");
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            ShowLoading(false);
            ShowRegistrationSuccess("処理が完了しました。");
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
    private void ValidateAllInputs()
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

        if (isDebugMode && loginEmailInputField != null)
            loginEmailInputField.text = "";
    }

    private void UpdateButtonVisual(Button button)
    {
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
        return email.Contains("@") && email.Contains(".");
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

    // HandleRegistrationError メソッドの該当部分を以下のように修正

    private void HandleRegistrationError(System.Exception e)
    {
        string errorMessage = "予期せぬエラーが発生しました。";

        if (e.GetBaseException() is FirebaseException firebaseEx)
        {
            AuthError errorCode = (AuthError)firebaseEx.ErrorCode;
            switch (errorCode)
            {
                case AuthError.WeakPassword:
                    errorMessage = "パスワードが弱すぎます。6文字以上で設定してください。";
                    break;
                case AuthError.InvalidEmail:
                    errorMessage = "メールアドレスの形式が正しくありません。";
                    break;
                case AuthError.EmailAlreadyInUse:
                    errorMessage = "このメールアドレスは既に使用されています。";
                    break;
                case AuthError.MissingEmail:
                    errorMessage = "メールアドレスを入力してください。";
                    break;
                default:
                    errorMessage = "ユーザー登録に失敗しました。エラーコード: " + errorCode;
                    break;
            }
        }

        if (errorMessage == "このメールアドレスは既に使用されています。")
        {
            ShowRegistrationError(errorMessage);
            // 2秒後にログインパネルに遷移するコルーチンを開始
            StartCoroutine(TransitionToLoginPanelAfterDelay(2f));
        }
        else
        {
            ShowRegistrationError(errorMessage);
        }
    }

    // 新しく追加するコルーチンメソッド
    private IEnumerator TransitionToLoginPanelAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (uiManager != null && loginPanel != null)
        {
            uiManager.ShowPanel(loginPanel);
        }
        else
        {
            ShowAllPanels(false);
            loginPanel?.SetActive(true);
        }
    }

    private void HandleLoginError(System.Exception e)
    {
        string errorMessage = "ログインに失敗しました。";

        if (e.GetBaseException() is FirebaseException firebaseEx)
        {
            // AuthError.Failure のエラーコードは 1 です
            AuthError errorCode = (AuthError)firebaseEx.ErrorCode;
            switch (errorCode)
            {
                // Failure または InvalidCredential をキャッチ
                case AuthError.Failure:
                case AuthError.InvalidCredential: // 新しいSDKではこちらが返ることも
                    errorMessage = "メールアドレス\nまたは\nパスワードが正しくありません。";
                    break;

                case AuthError.InvalidEmail:
                    errorMessage = "メールアドレスの形式が正しくありません。";
                    break;

                case AuthError.TooManyRequests:
                    errorMessage = "リクエストが多すぎます。しばらくお待ちください。";
                    break;

                default:
                    errorMessage = "予期せぬエラーが発生しました。";
                    break;
            }
        }
        else
        {
            errorMessage = "予期せぬエラーが発生しました。";
        }

        ShowLoginError(errorMessage);
    }


    // ============ デバッグ用メソッド ============
    [ContextMenu("Sign Out")]
    public void SignOut()
    {
        if (auth != null)
        {
            auth.SignOut();
            Debug.Log("サインアウトしました");

            if (uiManager != null && loginPanel != null)
            {
                uiManager.ShowPanel(loginPanel);
            }
            else
            {
                ShowAllPanels(false);
                loginPanel?.SetActive(true);
            }
            ClearAllInputFields();
        }
    }

    [ContextMenu("Reset Login Settings")]
    public void ResetLoginSettings()
    {
        PlayerPrefs.DeleteKey("REMEMBER_EMAIL_KEY");
        PlayerPrefs.Save();
        Debug.Log("ログイン設定がリセットされました");
    }

    void OnDestroy()
    {
        if (auth != null)
        {
            auth.StateChanged -= OnAuthStateChanged;
        }
    }
}