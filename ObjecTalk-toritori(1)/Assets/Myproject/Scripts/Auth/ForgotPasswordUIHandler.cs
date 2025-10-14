using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class ForgotPasswordUIHandler : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField emailInputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text statusText;

    public event Action<string> OnSendResetEmailRequested;
    public event Action OnCancel;

    private Coroutine hideStatusCoroutine;

    private void Awake()
    {
        sendButton?.onClick.AddListener(() => OnSendResetEmailRequested?.Invoke(emailInputField.text.Trim()));
        cancelButton?.onClick.AddListener(() => OnCancel?.Invoke());
    }

    private void OnEnable()
    {
        // パネルが表示されたら入力欄とステータステキストをクリアする
        emailInputField.text = "";
        statusText.text = "";
    }

    public void ShowStatus(string message, bool isSuccess)
    {
        if (statusText == null) return;

        statusText.text = message;
        statusText.color = isSuccess ? Color.green : Color.red;
        statusText.gameObject.SetActive(true);

        if (hideStatusCoroutine != null)
        {
            StopCoroutine(hideStatusCoroutine);
        }
        hideStatusCoroutine = StartCoroutine(HideStatusAfterDelay(5f));
    }

    private IEnumerator HideStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (statusText != null)
        {
            statusText.gameObject.SetActive(false);
        }
    }
}