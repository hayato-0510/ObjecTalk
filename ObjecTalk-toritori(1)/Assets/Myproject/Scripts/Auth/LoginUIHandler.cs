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

    private Coroutine hideErrorCoroutine;

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
        bool isEmailValid = loginEmailInputField != null && AuthUtils.IsValidEmail(loginEmailInputField.text);
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

    public void ShowLoginError(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.color = Color.red;
            loginErrorText.gameObject.SetActive(true);
            if (hideErrorCoroutine != null) StopCoroutine(hideErrorCoroutine);
            hideErrorCoroutine = StartCoroutine(HideErrorAfterDelay(5f));
        }
    }

    public void ShowLoginSuccess(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.color = Color.green;
            loginErrorText.gameObject.SetActive(true);
            if (hideErrorCoroutine != null) StopCoroutine(hideErrorCoroutine);
            hideErrorCoroutine = StartCoroutine(HideErrorAfterDelay(3f));
        }
    }

    public void HideLoginError()
    {
        if (loginErrorText != null) loginErrorText.gameObject.SetActive(false);
        if (hideErrorCoroutine != null) StopCoroutine(hideErrorCoroutine);
    }

    private IEnumerator HideErrorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideLoginError();
    }
}