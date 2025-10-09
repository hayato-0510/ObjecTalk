using UnityEngine;

public class CollectionPanelManager : MonoBehaviour
{
    // ★追加: UIマネージャーへの参照をインスペクターから設定できるようにする
    [Header("必要なコンポーネント")]
    public FlexibleUIManager uiManager;

    // メソッド名をより分かりやすく変更し、処理を追加
    public void ShowPanelAndRefresh()
    {
        // 1. まず、このパネル自身を表示するようにUIマネージャーに命令する
        if (uiManager != null)
        {
            uiManager.ShowPanel(this.gameObject);
        }
        else
        {
            // Fallback: もしUIマネージャーがなければ、直接アクティブにする
            this.gameObject.SetActive(true);
        }

        // 2. 次に、中身を最新の状態に更新する
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
}