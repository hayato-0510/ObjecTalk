using UnityEngine;
using Firebase.Auth;
using Firebase;
using System;
using System.Threading.Tasks;

public class AuthenticationManager : MonoBehaviour
{
    public static AuthenticationManager Instance { get; private set; }

    [Header("設定")]
    public bool maintainLoginState = true;
    public float sessionTimeoutMinutes = 60f;

    // イベント
    public static event Action<FirebaseUser> OnUserLoggedIn;
    public static event Action OnUserLoggedOut;
    public static event Action OnSessionExpired;
    public static event Action<string> OnAuthError;

    // 定数
    private const string LOGIN_STATE_KEY = "LOGIN_STATE";
    private const string LAST_LOGIN_TIME_KEY = "LAST_LOGIN_TIME";
    private const string USER_ID_KEY = "USER_ID";
    private const string REMEMBER_EMAIL_KEY = "REMEMBER_EMAIL_KEY";

    // プロパティ
    public FirebaseAuth Auth { get; private set; }
    public FirebaseUser CurrentUser => Auth?.CurrentUser;
    public bool IsLoggedIn => CurrentUser != null && currentLoginState == LoginState.LoggedIn;
    public LoginState CurrentLoginState => currentLoginState;

    public enum LoginState
    {
        NotLoggedIn,
        LoggedIn,
        SessionExpired,
        RememberedUser,
        Initializing
    }

    private LoginState currentLoginState = LoginState.Initializing;
    private bool isInitialized = false;

    void Awake()
    {
        // Singleton パターン
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeAsync();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async void InitializeAsync()
    {
        try
        {
            Debug.Log("AuthenticationManager: Firebase初期化を開始します...");

            var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (dependencyStatus == DependencyStatus.Available)
            {
                Auth = FirebaseAuth.DefaultInstance;
                Auth.StateChanged += OnAuthStateChanged;

                Debug.Log("AuthenticationManager: Firebase Auth初期化完了");

                isInitialized = true;

                if (maintainLoginState)
                {
                    InvokeRepeating(nameof(CheckSessionValidity), 60f, 60f);
                }

                Debug.Log("AuthenticationManager initialized successfully");
            }
            else
            {
                string errorMsg = $"Firebase dependency error: {dependencyStatus}";
                Debug.LogError(errorMsg);
                OnAuthError?.Invoke(errorMsg);
            }
        }
        catch (Exception e)
        {
            string errorMsg = $"Authentication initialization failed: {e.Message}";
            Debug.LogError(errorMsg);
            OnAuthError?.Invoke(errorMsg);
        }
    }

    private void OnAuthStateChanged(object sender, EventArgs eventArgs)
    {
        if (Auth.CurrentUser != null)
        {
            Debug.Log($"User signed in: {Auth.CurrentUser.Email}");
            if (currentLoginState != LoginState.LoggedIn)
            {
                currentLoginState = LoginState.LoggedIn;
                OnUserLoggedIn?.Invoke(Auth.CurrentUser);
            }
        }
        else
        {
            Debug.Log("User signed out");
            if (currentLoginState == LoginState.LoggedIn)
            {
                currentLoginState = LoginState.NotLoggedIn;
                OnUserLoggedOut?.Invoke();
            }
        }
    }

    // ログイン処理
    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            Debug.Log($"ログイン試行: {email}");
            var result = await Auth.SignInWithEmailAndPasswordAsync(email, password);
            SaveLoginState(result.User.UserId, email);
            Debug.Log("ログイン成功");
            return true;
        }
        catch (FirebaseException firebaseEx)
        {
            string errorMsg = HandleFirebaseAuthError(firebaseEx, "Login failed");
            OnAuthError?.Invoke(errorMsg);
            return false;
        }
        catch (Exception e)
        {
            string errorMsg = $"Login failed: {e.Message}";
            Debug.LogError(errorMsg);
            OnAuthError?.Invoke(errorMsg);
            return false;
        }
    }

    // 新規登録処理
    public async Task<bool> RegisterAsync(string email, string password, string displayName = "")
    {
        try
        {
            Debug.Log($"新規登録試行: {email}");
            var result = await Auth.CreateUserWithEmailAndPasswordAsync(email, password);

            if (!string.IsNullOrEmpty(displayName))
            {
                var profile = new UserProfile { DisplayName = displayName };
                await result.User.UpdateUserProfileAsync(profile);
            }

            SaveLoginState(result.User.UserId, email);
            Debug.Log("新規登録成功");
            return true;
        }
        catch (FirebaseException firebaseEx)
        {
            string errorMsg = HandleFirebaseAuthError(firebaseEx, "Registration failed");
            OnAuthError?.Invoke(errorMsg);
            return false;
        }
        catch (Exception e)
        {
            string errorMsg = $"Registration failed: {e.Message}";
            Debug.LogError(errorMsg);
            OnAuthError?.Invoke(errorMsg);
            return false;
        }
    }

    // サインアウト
    public void SignOut()
    {
        try
        {
            Auth?.SignOut();
            ClearLoginState();
            Debug.Log("サインアウト完了");
        }
        catch (Exception e)
        {
            Debug.LogError($"サインアウトエラー: {e.Message}");
        }
    }

    // パスワードリセット
    public async Task<bool> SendPasswordResetEmailAsync(string email)
    {
        try
        {
            await Auth.SendPasswordResetEmailAsync(email);
            Debug.Log($"パスワードリセットメール送信完了: {email}");
            return true;
        }
        catch (FirebaseException firebaseEx)
        {
            string errorMsg = HandleFirebaseAuthError(firebaseEx, "Password reset failed");
            OnAuthError?.Invoke(errorMsg);
            return false;
        }
        catch (Exception e)
        {
            string errorMsg = $"Password reset failed: {e.Message}";
            Debug.LogError(errorMsg);
            OnAuthError?.Invoke(errorMsg);
            return false;
        }
    }

    // ログイン状態の復元
    private void RestoreLoginState()
    {
        if (!maintainLoginState)
        {
            currentLoginState = LoginState.NotLoggedIn;
            Debug.Log("ログイン状態の維持が無効です");
            return;
        }

        if (IsSessionExpired())
        {
            currentLoginState = LoginState.SessionExpired;
            ClearLoginState();
            OnSessionExpired?.Invoke();
            Debug.Log("セッションが期限切れです");
            return;
        }

        if (Auth.CurrentUser != null)
        {
            string savedUserId = PlayerPrefs.GetString(USER_ID_KEY, "");
            if (savedUserId == Auth.CurrentUser.UserId)
            {
                currentLoginState = LoginState.LoggedIn;
                UpdateLastLoginTime();
                OnUserLoggedIn?.Invoke(Auth.CurrentUser);
                Debug.Log($"ログイン状態を復元しました: {Auth.CurrentUser.Email}");
                return;
            }
        }

        string rememberedEmail = PlayerPrefs.GetString(REMEMBER_EMAIL_KEY, "");
        if (!string.IsNullOrEmpty(rememberedEmail))
        {
            currentLoginState = LoginState.RememberedUser;
            Debug.Log($"記憶されたユーザー: {rememberedEmail}");
        }
        else
        {
            currentLoginState = LoginState.NotLoggedIn;
            Debug.Log("ログインしていません");
        }
    }

    private void SaveLoginState(string userId, string email)
    {
        if (!maintainLoginState) return;

        PlayerPrefs.SetInt(LOGIN_STATE_KEY, (int)LoginState.LoggedIn);
        PlayerPrefs.SetString(USER_ID_KEY, userId);
        PlayerPrefs.SetString(REMEMBER_EMAIL_KEY, email);
        UpdateLastLoginTime();
        PlayerPrefs.Save();

        currentLoginState = LoginState.LoggedIn;
        Debug.Log("ログイン状態を保存しました");
    }

    private void ClearLoginState()
    {
        PlayerPrefs.DeleteKey(LOGIN_STATE_KEY);
        PlayerPrefs.DeleteKey(LAST_LOGIN_TIME_KEY);
        PlayerPrefs.DeleteKey(USER_ID_KEY);
        PlayerPrefs.DeleteKey(REMEMBER_EMAIL_KEY);
        PlayerPrefs.Save();

        currentLoginState = LoginState.NotLoggedIn;
        Debug.Log("ログイン状態をクリアしました");
    }

    private void UpdateLastLoginTime()
    {
        PlayerPrefs.SetString(LAST_LOGIN_TIME_KEY, DateTime.Now.ToString());
        PlayerPrefs.Save();
    }

    private bool IsSessionExpired()
    {
        if (!maintainLoginState) return false;

        string lastLoginTimeStr = PlayerPrefs.GetString(LAST_LOGIN_TIME_KEY, "");
        if (string.IsNullOrEmpty(lastLoginTimeStr)) return true;

        if (DateTime.TryParse(lastLoginTimeStr, out DateTime lastLoginTime))
        {
            var timeDifference = DateTime.Now - lastLoginTime;
            return timeDifference.TotalMinutes > sessionTimeoutMinutes;
        }

        return true;
    }

    private void CheckSessionValidity()
    {
        if (currentLoginState == LoginState.LoggedIn && IsSessionExpired())
        {
            Debug.Log("セッションが期限切れになりました");
            SignOut();
            OnSessionExpired?.Invoke();
        }
    }

    public void ExtendSession()
    {
        if (IsLoggedIn)
        {
            UpdateLastLoginTime();
            Debug.Log("セッションを延長しました");
        }
    }

    public string GetRememberedEmail()
    {
        return PlayerPrefs.GetString(REMEMBER_EMAIL_KEY, "");
    }

    // Firebase認証エラーの処理
    private string HandleFirebaseAuthError(FirebaseException firebaseEx, string prefix)
    {
        AuthError errorCode = (AuthError)firebaseEx.ErrorCode;
        string errorMsg = prefix + ": ";

        switch (errorCode)
        {
            case AuthError.WeakPassword:
                errorMsg += "weak-password";
                break;
            case AuthError.InvalidEmail:
                errorMsg += "invalid-email";
                break;
            case AuthError.EmailAlreadyInUse:
                errorMsg += "email-already-in-use";
                break;
            case AuthError.WrongPassword:
                errorMsg += "wrong-password";
                break;
            case AuthError.InvalidCredential:
                errorMsg += "invalid-credential";
                break;
            case AuthError.UserNotFound:
                errorMsg += "user-not-found";
                break;
            case AuthError.TooManyRequests:
                errorMsg += "too-many-requests";
                break;
            case AuthError.MissingEmail:
                errorMsg += "missing-email";
                break;
            default:
                errorMsg += $"firebase-error-{errorCode}";
                break;
        }

        Debug.LogError($"Firebase Auth Error: {errorMsg}");
        return errorMsg;
    }

    // 初期化完了チェック
    public bool IsInitialized()
    {
        return isInitialized && Auth != null;
    }

    // デバッグ用メソッド
    [ContextMenu("Force Sign Out")]
    public void ForceSignOut()
    {
        SignOut();
    }

    [ContextMenu("Clear All Data")]
    public void ClearAllData()
    {
        ClearLoginState();
        Debug.Log("全てのログインデータをクリアしました");
    }

    [ContextMenu("Show Current State")]
    public void ShowCurrentState()
    {
        Debug.Log($"Current Login State: {currentLoginState}");
        Debug.Log($"Is Logged In: {IsLoggedIn}");
        Debug.Log($"Current User: {CurrentUser?.Email ?? "None"}");
        Debug.Log($"Remembered Email: {GetRememberedEmail()}");
        Debug.Log($"Is Initialized: {isInitialized}");
    }

    void OnDestroy()
    {
        if (Auth != null)
        {
            Auth.StateChanged -= OnAuthStateChanged;
        }
    }
}