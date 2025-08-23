using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ProfileEditor : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private TMP_InputField dateOfBirthInputField;
    [SerializeField] private Button saveButton;
    [SerializeField] private TMP_Text statusText;

    private DatabaseManager dbManager;
    private User currentUser;

    // ★修正点: Start()メソッドの処理を削除
    void Start()
    {
        // 外部からのInitialize呼び出しを待ちます
    }

    /// <summary>
    /// IntegratedAuthManagerから呼び出され、このコンポーネントを初期化します。
    /// </summary>
    public void Initialize(User user, DatabaseManager databaseManager)
    {
        if (user == null)
        {
            statusText.text = "エラー: ユーザー情報が渡されていません。";
            SetUIInteractable(false);
            return;
        }

        currentUser = user;
        dbManager = databaseManager;

        // UIにユーザー情報を反映
        LoadUserData();

        // ボタンにクリックイベントを登録
        saveButton.onClick.AddListener(OnSaveButtonClicked);
    }

    /// <summary>
    /// 現在のユーザーの情報をUIに反映させます。
    /// </summary>
    private void LoadUserData()
    {
        if (currentUser != null)
        {
            nameInputField.text = currentUser.Name;
            dateOfBirthInputField.text = currentUser.DateOfBirth;
            statusText.text = "プロフィール情報を編集してください。";
            SetUIInteractable(true);
        }
        else
        {
            statusText.text = "エラー: ユーザー情報が見つかりません。";
            SetUIInteractable(false);
        }
    }

    /// <summary>
    // 保存ボタンがクリックされたときに呼び出されます。
    /// </summary>
    private void OnSaveButtonClicked()
    {
        if (currentUser == null)
        {
            statusText.text = "エラー: ユーザー情報が読み込めていません。";
            return;
        }

        if (string.IsNullOrWhiteSpace(nameInputField.text))
        {
            statusText.text = "エラー: 名前を入力してください。";
            return;
        }

        currentUser.Name = nameInputField.text;
        currentUser.DateOfBirth = dateOfBirthInputField.text;

        try
        {
            dbManager.SaveUser(currentUser);
            statusText.text = "プロフィールを保存しました！";
            Debug.Log("ユーザー情報の更新に成功しました。");
        }
        catch (System.Exception ex)
        {
            statusText.text = "エラー: 保存に失敗しました。";
            Debug.LogError($"ユーザー情報の保存に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// UI要素の有効/無効をまとめて切り替えます。
    /// </summary>
    private void SetUIInteractable(bool isInteractable)
    {
        nameInputField.interactable = isInteractable;
        dateOfBirthInputField.interactable = isInteractable;
        saveButton.interactable = isInteractable;
    }
}