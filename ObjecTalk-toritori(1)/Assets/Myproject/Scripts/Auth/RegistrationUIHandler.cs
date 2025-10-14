using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class RegistrationUIHandler : MonoBehaviour
{
    [Header("新規登録UI")]
    // ★変更: nameInputFieldからusernameInputFieldに名称変更
    public TMP_InputField usernameInputField;
    public TMP_InputField regEmailInputField;
    public TMP_InputField regPasswordInputField;
    public Button registerButton;
    public Button switchToLoginButton;
    // ★変更: nameHintTextからusernameHintTextに名称変更
    public TMP_Text usernameHintText;
    public TMP_Text regPasswordHintText;
    public TMP_Text registrationErrorText;

    // イベントデリゲートを定義 (引数は username, email, password)
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
        // ★変更: usernameInputFieldの値を使用
        registerButton?.onClick.AddListener(() => OnRegistrationRequested?.Invoke(usernameInputField.text.Trim(), regEmailInputField.text.Trim(), regPasswordInputField.text));
        switchToLoginButton?.onClick.AddListener(() => OnSwitchToLoginClicked?.Invoke());

        // ★変更: NameからUsernameのリスナーに変更
        usernameInputField?.onValueChanged.AddListener(OnUsernameChanged);
        regEmailInputField?.onValueChanged.AddListener(OnRegEmailChanged);
        regPasswordInputField?.onValueChanged.AddListener(OnRegPasswordChanged);
    }

    // ★変更: OnNameChangedからOnUsernameChangedに名称変更
    private void OnUsernameChanged(string value)
    {
        HideRegistrationError();
        ValidateUsernameRealtime();
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

    // ★変更: ValidateNameRealtimeからValidateUsernameRealtimeに名称変更し、メッセージを修正
    private void ValidateUsernameRealtime()
    {
        if (usernameHintText == null || usernameInputField == null) return;
        string username = usernameInputField.text;
        int length = username.Length;

        if (length == 0) usernameHintText.gameObject.SetActive(false);
        else if (length < 2)
        {
            usernameHintText.gameObject.SetActive(true);
            usernameHintText.text = "ユーザー名は2文字以上で入力してください。";
            usernameHintText.color = Color.red;
        }
        else if (length > 20)
        {
            usernameHintText.gameObject.SetActive(true);
            usernameHintText.text = "ユーザー名は20文字以内で入力してください。";
            usernameHintText.color = Color.red;
        }
        else usernameHintText.gameObject.SetActive(false);
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
        // ★変更: isNameValidからisUsernameValidに名称変更
        bool isUsernameValid = usernameInputField != null && usernameInputField.text.Length >= 2 && usernameInputField.text.Length <= 20;
        // AuthUtils.IsValidEmailを使用するには、AuthUtils.csがプロジェクトに存在する必要があります
        bool isEmailValid = regEmailInputField != null && regEmailInputField.text.Contains("@");
        bool isPasswordValid = regPasswordInputField != null && regPasswordInputField.text.Length >= 6;
        registerButton.interactable = isUsernameValid && isEmailValid && isPasswordValid;
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

    // ★変更: publicに変更して外部から呼び出せるようにする
    public IEnumerator HideErrorAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideRegistrationError();
    }
}