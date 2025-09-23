using UnityEngine;
using UnityEngine.EventSystems;

public class UIDrag : MonoBehaviour, IDragHandler
{
    public void OnDrag(PointerEventData eventData)
    {
        // ドラッグされた分だけ、UIを動かす
        transform.position = eventData.position;
    }
}