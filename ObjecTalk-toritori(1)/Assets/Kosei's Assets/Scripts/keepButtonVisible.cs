using UnityEngine;

public class KeepButtonVisible : MonoBehaviour
{
    // LateUpdateは、全てのUpdate処理が終わった後、フレームの最後に呼び出されます
    void LateUpdate()
    {
        // もしこのGameObjectが非アクティブにされていたら、強制的にアクティブに戻す
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }
}