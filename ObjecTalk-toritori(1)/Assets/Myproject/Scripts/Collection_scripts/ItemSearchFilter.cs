using UnityEngine;
using TMPro; // TextMeshProを扱うために必要

public class ItemSearchFilter : MonoBehaviour
{
    // 1. 検索用のUIをInspectorから設定するための変数
    public TMP_InputField searchInputField;

    // 2. 検索対象となるアイテムの親オブジェクトをInspectorから設定
    public Transform itemContent;

    // このスクリプトが有効になった時に一度だけ呼ばれる
    void Start()
    {
        // 3. InputFieldの入力値が変更された時に関数を呼び出すように登録
        if (searchInputField != null)
        {
            searchInputField.onValueChanged.AddListener(FilterItems);
        }
    }

    // 4. 実際にフィルタリング処理を行う関数
    private void FilterItems(string searchText)
    {
        // 入力されたテキストを小文字に変換（大文字・小文字を区別しないため）
        string searchLower = searchText.ToLower();

        // itemContentの子オブジェクト（各アイテム）を一つずつチェックする
        foreach (Transform item in itemContent)
        {
            // 子オブジェクトからTMP_Textコンポーネントを取得
            TMP_Text itemTextComponent = item.GetComponentInChildren<TMP_Text>();

            // テキストコンポーネントが見つかった場合のみ処理
            if (itemTextComponent != null)
            {
                // テキストコンポーネントの表示内容を取得し、小文字に変換
                string itemNameLower = itemTextComponent.text.ToLower();

                // アイテム名に検索テキストが含まれているかチェック
                if (string.IsNullOrEmpty(searchLower) || itemNameLower.Contains(searchLower))
                {
                    // 含まれていれば表示する
                    item.gameObject.SetActive(true);
                }
                else
                {
                    // 含まれていなければ非表示にする
                    item.gameObject.SetActive(false);
                }
            }
        }
    }
}