using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Slot : MonoBehaviour
{
    [SerializeField]
    private Image itemImage;
    [SerializeField]
    private TextMeshProUGUI itemNameText;

    /// <summary>
    /// ObjectItemデータを受け取り、スロットのUIに名前などを設定します。
    /// </summary>
    public void SetItem(ObjectItem item)
    {
        if (item != null)
        {
            // 現在は画像の情報がないため非表示
            itemImage.enabled = false;

            // テキストを表示し、受け取ったアイテムの名前を設定
            itemNameText.enabled = true;
            itemNameText.text = item.Name;
        }
        else
        {
            itemImage.enabled = false;
            itemNameText.enabled = false;
            itemNameText.text = "";
        }
    }
}