using UnityEngine;
using System.Collections.Generic;

public class SlotGrid : MonoBehaviour
{
    [SerializeField]
    private GameObject slotPrefab;

    private CollectionPanelManager panelManager;

    private void Awake()
    {
        panelManager = GetComponentInParent<CollectionPanelManager>();

        // ★追加: Managerが見つかったかログで確認
        if (panelManager == null)
        {
            Debug.LogError("SlotGridがCollectionPanelManagerを見つけられませんでした！Hierarchyの親子関係を確認してください。");
        }
        else
        {
            Debug.Log("SlotGridがCollectionPanelManagerを正常に見つけました。");
        }
    }

    /// <summary>
    /// ユーザーのオブジェクトリストを受け取り、図鑑のUIを初期化（再生成）します。
    /// </summary>
    public void InitializeCollection(List<ObjectItem> userObjects)
    {
        // 1. 既に表示されている古いスロットがあれば、一度すべて削除する
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // 2. ユーザーのデータがなければ、処理を終了する
        if (userObjects == null || userObjects.Count == 0)
        {
            Debug.Log("このユーザーの図鑑データはありません。");
            return;
        }

        // 3. ユーザーのオブジェクトリストを元に、新しいスロットを生成していく
        foreach (var obj in userObjects)
        {
            GameObject slotObj = Instantiate(slotPrefab, this.transform);
            Slot slot = slotObj.GetComponent<Slot>();

            // ★修正: SetItem の代わりに Initialize を呼び出し、managerを渡す
            if (slot != null)
            {
                slot.Initialize(obj);
            }
        }
    }
}