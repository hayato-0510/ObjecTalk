using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class Slot : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("UI参照")]
    [SerializeField]
    private Image itemImage;
    [SerializeField]
    private TextMeshProUGUI itemNameText;
    [SerializeField]
    private GameObject deleteButton;

    private string currentObjectId;
    private Coroutine longPressCoroutine;
    private const float longPressDuration = 0.5f;

    public void Initialize(ObjectItem item)
    {
        currentObjectId = item.object_id;

        if (item.ImageData != null && item.ImageData.Length > 0)
        {
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(item.ImageData))
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                itemImage.sprite = sprite;
                itemImage.enabled = true;
            }
        }
        else
        {
            itemImage.enabled = false;
        }

        itemNameText.enabled = true;
        itemNameText.text = item.Name;

        // 初期状態では必ず削除ボタンを非表示にする
        deleteButton.SetActive(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
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

        // 長押し成功！Managerに自分（this）を通知する
        if (CollectionPanelManager.Instance != null)
        {
            CollectionPanelManager.Instance.OnSlotLongPressed(this);
        }
    }

    /// <summary>
    /// Managerからの指示で、削除ボタンの表示/非表示を切り替える
    /// </summary>
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