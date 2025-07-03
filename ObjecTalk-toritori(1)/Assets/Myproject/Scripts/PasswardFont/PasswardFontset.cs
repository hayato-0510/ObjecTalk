using UnityEngine;
using TMPro; // TextMesh Proを扱うために必要

// このコンポーネントがアタッチされると、自動でTMP_InputFieldも必須になる
[RequireComponent(typeof(TMP_InputField))]
public class PasswordCharSetter : MonoBehaviour
{
    void Awake()
    {
        // 自分にアタッチされているTMP_InputFieldコンポーネントを取得
        TMP_InputField inputField = GetComponent<TMP_InputField>();

        // パスワードのマスク文字を '●' に設定
        // ※文字なのでシングルクォーテーション(')で囲みます
        inputField.asteriskChar = '●';
    }
}