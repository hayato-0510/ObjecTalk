using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class LoginUIHandler : MonoBehaviour
{
    [Header("ログインUI")]
    public TMP_InputField loginEmailInputField;
    public TMP_InputField loginPasswordInputField;
    public Button loginButton;
    public Button forgotPasswordButton;
    public Button switchToRegisterButton;
    public TMP_Text loginErrorText;

    // イベントデリゲートを定義
    public event Action<string, string> OnLoginRequested;
    public event Action<string> OnForgotPasswordRequested;
    public event Action OnSwitchToRegisterClicked;

    // ★変更点: コルーチンの管理はIntegratedAuthManagerに任せるため不要に
    // private Coroutine hideErrorCoroutine;

    void Awake()
    {
        SetupEventListeners();
        HideLoginError();
        UpdateLoginButton();
    }

    private void OnEnable()
    {
        RestoreRememberedEmail();
    }

    private void SetupEventListeners()
    {
        loginButton?.onClick.AddListener(() => OnLoginRequested?.Invoke(loginEmailInputField.text.Trim(), loginPasswordInputField.text));
        forgotPasswordButton?.onClick.AddListener(() => OnForgotPasswordRequested?.Invoke(loginEmailInputField.text.Trim()));
        switchToRegisterButton?.onClick.AddListener(() => OnSwitchToRegisterClicked?.Invoke());

        loginEmailInputField?.onValueChanged.AddListener(OnEmailChanged);
        loginPasswordInputField?.onValueChanged.AddListener(OnPasswordChanged);
    }

    private void OnEmailChanged(string value)
    {
        HideLoginError();
        UpdateLoginButton();
    }

    private void OnPasswordChanged(string value)
    {
        HideLoginError();
        UpdateLoginButton();
    }

    public void UpdateLoginButton()
    {
        if (loginButton == null) return;
        // AuthUtils.IsValidEmailを使用するには、AuthUtils.csがプロジェクトに存在する必要があります
        bool isEmailValid = loginEmailInputField != null && loginEmailInputField.text.Contains("@");
        bool isPasswordValid = loginPasswordInputField != null && !string.IsNullOrEmpty(loginPasswordInputField.text);
        loginButton.interactable = isEmailValid && isPasswordValid;
    }

    public void RestoreRememberedEmail()
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

    public void ClearInputFields()
    {
        if (loginEmailInputField != null) loginEmailInputField.text = "";
        if (loginPasswordInputField != null) loginPasswordInputField.text = "";
    }

    // ★変更点: コルーチン開始のコードを削除
    public void ShowLoginError(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.color = Color.red;
            loginErrorText.gameObject.SetActive(true);
        }
    }

    // ★変更点: コルーチン開始のコードを削除
    public void ShowLoginSuccess(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.color = Color.green;
            loginErrorText.gameObject.SetActive(true);
        }
    }

    public void HideLoginError()
    {
        if (loginErrorText != null) loginErrorText.gameObject.SetActive(false);
    }

    // ★変更点: IntegratedAuthManagerから呼び出せるようにpublicに変更
    public IEnumerator HideErrorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideLoginError();
    }
}