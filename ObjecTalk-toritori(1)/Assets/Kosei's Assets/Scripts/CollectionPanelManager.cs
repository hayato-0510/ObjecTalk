using UnityEngine;
using UnityEngine.UI;

public class CollectionPanelManager : MonoBehaviour
{
    public static CollectionPanelManager Instance { get; private set; }

    [Header("必要なコンポーネント")]
    public FlexibleUIManager uiManager;

    [Header("削除機能")]
    public Button dismissButton;

    private Slot activeSlot; // 現在削除ボタンが表示されているスロット

    private void Awake()
    {
        // シングルトンの設定
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (dismissButton != null)
        {
            // dismissButtonがクリックされたら、OnDismissButtonPressedメソッドを呼び出す
            dismissButton.onClick.AddListener(OnDismissButtonPressed);
            dismissButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Slotから長押しを通知され、UIの状態を管理するメソッド
    /// </summary>
    public void OnSlotLongPressed(Slot slot)
    {
        Debug.Log($"ManagerがSlot '{slot.name}'からの長押し通知を受け取りました。");

        // もし既に他のスロットがアクティブなら、それを先に非表示にする
        if (activeSlot != null && activeSlot != slot)
        {
            activeSlot.SetDeleteButtonActive(false);
        }

        // 新しいスロットをアクティブとして記憶
        activeSlot = slot;

        // 記憶したスロットの削除ボタンを表示する
        activeSlot.SetDeleteButtonActive(true);

        // キャンセル用のボタンを表示する
        if (dismissButton != null)
        {
            dismissButton.gameObject.SetActive(true);
            Debug.Log("DismissButtonをアクティブにしました。");
        }
    }

    /// <summary>
    /// キャンセル用ボタンが押されたときに呼び出されるメソッド
    /// </summary>
    private void OnDismissButtonPressed()
    {
        Debug.Log("DismissButtonが押されました。");
        // アクティブなスロットがあれば、その削除ボタンを隠す
        if (activeSlot != null)
        {
            activeSlot.SetDeleteButtonActive(false);
            activeSlot = null; // アクティブなスロットをリセット
        }

        // キャンセル用のボタン自身も隠す
        if (dismissButton != null)
        {
            dismissButton.gameObject.SetActive(false);
        }
    }

    public void ShowPanelAndRefresh()
    {
        if (uiManager != null)
        {
            uiManager.ShowPanel(this.gameObject);
        }
        else
        {
            this.gameObject.SetActive(true);
        }

        if (IntegratedAuthManager.Instance != null)
        {
            Debug.Log("図鑑パネルの表示を更新します。");
            IntegratedAuthManager.Instance.RefreshCollectionDisplay();
        }
        else
        {
            Debug.LogWarning("IntegratedAuthManagerが見つからないため、図鑑を更新できませんでした。");
        }
    }

    public void ResetActiveSlotState()
    {
        Debug.Log("CollectionPanelManagerの状態をリセットします。");
        activeSlot = null;
        if (dismissButton != null)
        {
            dismissButton.gameObject.SetActive(false);
        }
    }
}