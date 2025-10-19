using UnityEngine;

public class CollectionButtonHandler : MonoBehaviour
{
    public FlexibleUIManager uiManager;
    public GameObject collectionPanel; // 表示したい図鑑パネル

    // CollectionButtonHandler.cs

    public void ShowCollectionAndRefresh()
    {
        // 1. IntegratedAuthManager の存在を確認
        if (IntegratedAuthManager.Instance == null)
        {
            Debug.LogError("IntegratedAuthManager.Instance が見つかりません！");
            return;
        }

        // --- ▼▼▼ この行を追加 ▼▼▼ ---
        // 2. SlotGrid への参照を確実に取得/更新する
        IntegratedAuthManager.Instance.FindAndAssignSlotGrid();
        // --- ▲▲▲ ---

        // 3. データ更新
        Debug.Log("図鑑表示前に RefreshCollectionDisplay を呼び出します。");
        IntegratedAuthManager.Instance.RefreshCollectionDisplay(); // この中で collectionSlotGrid が使われる

        // 4. パネル表示
        if (uiManager != null && collectionPanel != null)
        {
            uiManager.ShowPanel(collectionPanel);
        }
        else { /* エラーログ */ }
    }
}