using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class LoginUIHandler : MonoBehaviour
{
    [Header("ログインUI")]
    public TMP_InputField loginUsernameInputField;
    public TMP_InputField loginPasswordInputField;
    public Button loginButton;
    public Button forgotPasswordButton;
    public Button switchToRegisterButton;
    public TMP_Text loginErrorText;

    // イベントデリゲートを定義
    public event Action<string, string> OnLoginRequested;
    public event Action OnForgotPasswordClicked; // ★変更: 引数なしのイベントに変更
    public event Action OnSwitchToRegisterClicked;

    void Awake()
    {
        SetupEventListeners();
        HideLoginError();
        UpdateLoginButton();
    }

    private void SetupEventListeners()
    {
        loginButton?.onClick.AddListener(() => OnLoginRequested?.Invoke(loginUsernameInputField.text.Trim(), loginPasswordInputField.text));
        // ★変更: パスワード忘れボタンはクリックイベントを通知するだけ
        forgotPasswordButton?.onClick.AddListener(() => OnForgotPasswordClicked?.Invoke());
        switchToRegisterButton?.onClick.AddListener(() => OnSwitchToRegisterClicked?.Invoke());

        loginUsernameInputField?.onValueChanged.AddListener(OnUsernameChanged);
        loginPasswordInputField?.onValueChanged.AddListener(OnPasswordChanged);
    }

    private void OnUsernameChanged(string value)
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

        bool isUsernameValid = loginUsernameInputField != null && !string.IsNullOrEmpty(loginUsernameInputField.text);
        bool isPasswordValid = loginPasswordInputField != null && !string.IsNullOrEmpty(loginPasswordInputField.text);
        loginButton.interactable = isUsernameValid && isPasswordValid;
    }

    public void ClearInputFields()
    {
        if (loginUsernameInputField != null) loginUsernameInputField.text = "";
        if (loginPasswordInputField != null) loginPasswordInputField.text = "";
    }

    public void ShowLoginError(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.color = Color.red;
            loginErrorText.gameObject.SetActive(true);
        }
    }

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

    public IEnumerator HideErrorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideLoginError();
    }
}