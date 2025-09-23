using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public class ToggleVisuals : MonoBehaviour
{
    [Tooltip("オンの時に表示するスプライト")]
    public Sprite onSprite;

    [Tooltip("オフの時に表示するスプライト")]
    public Sprite offSprite;

    [Tooltip("スプライトを切り替える対象のImage")]
    public Image targetImage;

    private Toggle toggle;

    void Awake()
    {
        // Toggleコンポーネントを取得し、イベントを登録
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(OnToggleValueChanged);
    }

    void OnEnable()
    {
        // オブジェクトが有効になった時に、現在の状態で見た目を更新
        OnToggleValueChanged(toggle.isOn);
    }

    // トグルの値が変更されたときに呼ばれる
    private void OnToggleValueChanged(bool isOn)
    {
        if (targetImage == null) return;

        // オンならonSpriteを、オフならoffSpriteを設定
        targetImage.sprite = isOn ? onSprite : offSprite;
    }
}