using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class Slot : MonoBehaviour
{
    private Item item;

    [SerializeField]
    private Image itemImage;
    [SerializeField]
    private TextMeshProUGUI itemNameText;

    public Item MyItem { get => item; private set => item = value; }

    public void SetItem(Item item)
    {
        MyItem = item;

        if (item != null)
        {
            itemImage.enabled = true; //Colorではなくenabledで表示/非表示を切り替え
            itemImage.sprite = item.MyItemImage;

            itemNameText.enabled = true; //テキストを表示する
            itemNameText.text = item.MyItemName; // テキストにアイテム名を設定
        }
        else
        {
            itemImage.enabled = false; // 画像を非表示にする

            itemNameText.enabled = false; // テキストを非表示にする
            itemNameText.text = ""; // 念のためテキストを空にする
        }
        
    }
}
