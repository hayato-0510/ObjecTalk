using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Slot : MonoBehaviour
{
    [SerializeField]
    private Image itemImage;
    [SerializeField]
    private TextMeshProUGUI itemNameText;

    public void SetItem(ObjectItem item)
    {
        if (item != null)
        {
            // --- ★ここからが修正点★ ---
            // ImageDataにデータがあり、長さが0より大きいかチェック
            if (item.ImageData != null && item.ImageData.Length > 0)
            {
                // 1. 空のTexture2Dを作成
                Texture2D texture = new Texture2D(2, 2);

                // 2. バイト配列からテクスチャに画像を読み込む
                if (texture.LoadImage(item.ImageData))
                {
                    // 3. Texture2DからSpriteを作成してImageに設定
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    itemImage.sprite = sprite;
                    itemImage.enabled = true;
                }
                else
                {
                    Debug.LogError("データベースからの画像データの読み込みに失敗しました。");
                    itemImage.enabled = false;
                }
            }
            else
            {
                // 画像データがない場合は非表示
                itemImage.enabled = false;
            }
            // --- ★ここまでが修正点★ ---

            // テキストの表示（変更なし）
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