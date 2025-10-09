using UnityEngine;
using System.Collections.Generic;

public class SlotGrid : MonoBehaviour
{
    [SerializeField]
    private GameObject slotPrefab;

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
            slot.SetItem(obj);
        }
    }
}