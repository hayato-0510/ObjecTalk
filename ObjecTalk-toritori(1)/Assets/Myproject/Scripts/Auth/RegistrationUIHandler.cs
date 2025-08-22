using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class RegistrationUIHandler : MonoBehaviour
{
    [Header("新規登録UI")]
    public TMP_InputField nameInputField;
    public TMP_InputField regEmailInputField;
    public TMP_InputField regPasswordInputField;
    public Button registerButton;
    public Button switchToLoginButton;
    public TMP_Text nameHintText;
    public TMP_Text regPasswordHintText;
    public TMP_Text registrationErrorText;

    // イベントデリゲートを定義
    public event Action<string, string, string> OnRegistrationRequested;
    public event Action OnSwitchToLoginClicked;

    private Coroutine hideErrorCoroutine;

    void Awake()
    {
        SetupEventListeners();
        HideRegistrationError();
        UpdateRegisterButton();
    }

    private void SetupEventListeners()
    {
        registerButton?.onClick.AddListener(() => OnRegistrationRequested?.Invoke(nameInputField.text.Trim(), regEmailInputField.text.Trim(), regPasswordInputField.text));
        switchToLoginButton?.onClick.AddListener(() => OnSwitchToLoginClicked?.Invoke());

        nameInputField?.onValueChanged.AddListener(OnNameChanged);
        regEmailInputField?.onValueChanged.AddListener(OnRegEmailChanged);
        regPasswordInputField?.onValueChanged.AddListener(OnRegPasswordChanged);
    }

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

        if (length == 0) nameHintText.gameObject.SetActive(false);
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
        else nameHintText.gameObject.SetActive(false);
    }

    private void ValidateRegPasswordRealtime()
    {
        if (regPasswordHintText == null || regPasswordInputField == null) return;
        string password = regPasswordInputField.text;
        if (password.Length == 0) regPasswordHintText.gameObject.SetActive(false);
        else if (password.Length < 6)
        {
            regPasswordHintText.gameObject.SetActive(true);
            regPasswordHintText.text = "パスワードが弱すぎます。\n6文字以上で設定してください。";
            regPasswordHintText.color = Color.red;
        }
        else regPasswordHintText.gameObject.SetActive(false);
    }

    public void UpdateRegisterButton()
    {
        if (registerButton == null) return;
        bool isNameValid = nameInputField != null && nameInputField.text.Length >= 2 && nameInputField.text.Length <= 20;
        bool isEmailValid = regEmailInputField != null && AuthUtils.IsValidEmail(regEmailInputField.text);
        bool isPasswordValid = regPasswordInputField != null && regPasswordInputField.text.Length >= 6;
        registerButton.interactable = isNameValid && isEmailValid && isPasswordValid;
    }

    public void ShowRegistrationError(string message)
    {
        if (registrationErrorText != null)
        {
            registrationErrorText.text = message;
            registrationErrorText.color = Color.red;
            registrationErrorText.gameObject.SetActive(true);
            if (hideErrorCoroutine != null) StopCoroutine(hideErrorCoroutine);
            hideErrorCoroutine = StartCoroutine(HideErrorAfterDelay(5f));
        }
    }

    public void HideRegistrationError()
    {
        if (registrationErrorText != null) registrationErrorText.gameObject.SetActive(false);
        if (hideErrorCoroutine != null) StopCoroutine(hideErrorCoroutine);
    }

    private IEnumerator HideErrorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideRegistrationError();
    }
}