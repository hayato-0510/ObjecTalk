using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

public class Slot : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("UI参照")]
    [SerializeField]
    private Image itemImage;
    [SerializeField]
    private TextMeshProUGUI itemNameText;
    [SerializeField]
    private GameObject deleteButton;
    [SerializeField]
    private Image eyeImageUI;
    [SerializeField]
    private Image mouthImageUI;

    private string currentObjectId;
    private Coroutine longPressCoroutine;
    private const float longPressDuration = 0.5f;

    private ObjectItem currentItemData;

    public void Initialize(ObjectItem item)
    {
        currentItemData = item;
        currentObjectId = item.object_id;
        itemNameText.enabled = true;
        itemNameText.text = item.Name;
        deleteButton.SetActive(false);

        // --- 写真の表示 ---
        bool imageLoaded = false;
        if (item.ImageData != null && item.ImageData.Length > 0)
        {
            Texture2D texture = new Texture2D(2, 2);
            try
            {
                if (texture.LoadImage(item.ImageData))
                {
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    itemImage.sprite = sprite;
                    itemImage.enabled = true;
                    imageLoaded = true;
                }
                else
                {
                    Debug.LogError($"Slot '{item.Name}': Failed to load image data.");
                    itemImage.enabled = false;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Slot '{item.Name}': Exception during LoadImage - {ex.Message}");
                itemImage.enabled = false;
                if (texture != null) Object.Destroy(texture);
            }
        }
        else
        {
            itemImage.enabled = false;
        }

        // --- 目と口のスプライト設定 & 位置設定 ---
        if (imageLoaded && CollectionPanelManager.Instance != null)
        {
            SetupFaceParts(item);
        }
        else
        {
            HideFaceParts();
            if (!imageLoaded)
            {
                Debug.LogWarning($"Slot '{item.Name}': Image not loaded, hiding face parts.");
            }
            if (CollectionPanelManager.Instance == null)
            {
                Debug.LogError("CollectionPanelManager.Instance is NULL!");
            }
        }
    }

    /// <summary>
    /// 目と口のパーツをセットアップ(即座に表示)
    /// </summary>
    private void SetupFaceParts(ObjectItem item)
    {
        // 目のセットアップ
        if (eyeImageUI != null && item.EyeStyleId >= 0 &&
            item.EyeStyleId < CollectionPanelManager.Instance.eyeStyles.Count)
        {
            Sprite eyeSprite = CollectionPanelManager.Instance.eyeStyles[item.EyeStyleId];
            if (eyeSprite != null)
            {
                eyeImageUI.sprite = eyeSprite;

                // 重要: アルファ値を確実に1にする
                Color eyeColor = eyeImageUI.color;
                eyeColor.a = 1f;
                eyeImageUI.color = eyeColor;

                // レイアウト確定を待ってから位置設定と表示
                StartCoroutine(PositionAndShowPart(eyeImageUI, item.EyePositionX, item.EyePositionY, "Eye"));
            }
            else
            {
                eyeImageUI.gameObject.SetActive(false);
                Debug.LogWarning($"Slot '{item.Name}': Eye sprite is null for styleId {item.EyeStyleId}");
            }
        }
        else
        {
            if (eyeImageUI != null) eyeImageUI.gameObject.SetActive(false);
        }

        // 口のセットアップ
        if (mouthImageUI != null && item.MouthStyleId >= 0 &&
            item.MouthStyleId < CollectionPanelManager.Instance.mouthStyles.Count)
        {
            Sprite mouthSprite = CollectionPanelManager.Instance.mouthStyles[item.MouthStyleId];
            if (mouthSprite != null)
            {
                mouthImageUI.sprite = mouthSprite;

                // 重要: アルファ値を確実に1にする
                Color mouthColor = mouthImageUI.color;
                mouthColor.a = 1f;
                mouthImageUI.color = mouthColor;

                // レイアウト確定を待ってから位置設定と表示
                StartCoroutine(PositionAndShowPart(mouthImageUI, item.MouthPositionX, item.MouthPositionY, "Mouth"));
            }
            else
            {
                mouthImageUI.gameObject.SetActive(false);
                Debug.LogWarning($"Slot '{item.Name}': Mouth sprite is null for styleId {item.MouthStyleId}");
            }
        }
        else
        {
            if (mouthImageUI != null) mouthImageUI.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// レイアウト確定後にパーツを配置して表示するコルーチン
    /// </summary>
    private IEnumerator PositionAndShowPart(Image partImage, float relativeX, float relativeY, string partName)
    {
        // 2フレーム待つ(より確実にレイアウトを確定させる)
        yield return null;
        yield return new WaitForEndOfFrame();

        if (partImage != null && itemImage != null && currentItemData != null)
        {
            PositionPart(partImage.rectTransform, relativeX, relativeY);

            // パーツを最前面に持ってくる
            partImage.transform.SetAsLastSibling();

            partImage.gameObject.SetActive(true);

            Debug.Log($"Slot '{currentItemData.Name}': {partName} positioned at ({relativeX}, {relativeY}) and activated. " +
                      $"AnchoredPosition: {partImage.rectTransform.anchoredPosition}, Color.a: {partImage.color.a}");
        }
        else
        {
            Debug.LogError($"Slot: Failed to position {partName} - null reference detected");
        }
    }

    /// <summary>
    /// 目や口のパーツを指定された相対位置に配置する
    /// </summary>
    private void PositionPart(RectTransform partRectTransform, float relativeX, float relativeY)
    {
        if (itemImage == null || itemImage.rectTransform == null || partRectTransform == null)
        {
            Debug.LogError("PositionPart: Null reference detected");
            return;
        }

        Rect itemRect = itemImage.rectTransform.rect;

        if (itemRect.width <= 0 || itemRect.height <= 0)
        {
            Debug.LogWarning($"itemImage rect size is zero or invalid on Slot {itemNameText?.text}. " +
                           $"Cannot position {partRectTransform.name}. Rect: {itemRect}");
            return;
        }

        // 相対座標(0~1)を写真Imageのローカル座標に変換
        float centerX = (relativeX - 0.5f) * itemRect.width;
        float centerY = (relativeY - 0.5f) * itemRect.height;

        partRectTransform.anchoredPosition = new Vector2(centerX, centerY);

        Debug.Log($"PositionPart '{itemNameText?.text}' - {partRectTransform.name}: " +
                  $"Relative({relativeX}, {relativeY}) -> Anchored({centerX}, {centerY}), " +
                  $"ItemRect: {itemRect.width}x{itemRect.height}");
    }

    /// <summary>
    /// 目と口のパーツを非表示にするヘルパーメソッド
    /// </summary>
    private void HideFaceParts()
    {
        if (eyeImageUI != null) eyeImageUI.gameObject.SetActive(false);
        if (mouthImageUI != null) mouthImageUI.gameObject.SetActive(false);
    }

    // --- 長押しと削除の処理 ---
    public void OnPointerDown(PointerEventData eventData)
    {
        if (longPressCoroutine != null) StopCoroutine(longPressCoroutine);
        longPressCoroutine = StartCoroutine(LongPressCheck());
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (longPressCoroutine != null)
        {
            StopCoroutine(longPressCoroutine);
        }
    }

    private IEnumerator LongPressCheck()
    {
        yield return new WaitForSeconds(longPressDuration);
        if (CollectionPanelManager.Instance != null)
        {
            CollectionPanelManager.Instance.OnSlotLongPressed(this);
        }
        else
        {
            Debug.LogError("CollectionPanelManager.Instance が見つかりません!");
        }
    }

    public void SetDeleteButtonActive(bool isActive)
    {
        if (deleteButton != null)
        {
            deleteButton.SetActive(isActive);
        }
    }

    public void OnDeleteButtonClicked()
    {
        DatabaseManager dbManager = new DatabaseManager();
        dbManager.DeleteObject(currentObjectId);
        if (IntegratedAuthManager.Instance != null)
        {
            IntegratedAuthManager.Instance.RefreshCollectionDisplay();
        }
    }
}