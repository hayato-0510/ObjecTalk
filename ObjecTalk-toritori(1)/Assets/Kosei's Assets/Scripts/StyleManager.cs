using UnityEngine;
using System.Collections.Generic;

public class StyleManager : MonoBehaviour
{
    public static StyleManager Instance { get; private set; }

    public List<Sprite> eyeStyles;
    public List<Sprite> mouthStyles;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // 必要であればシーンをまたいで永続化
        }
        else
        {
            Destroy(gameObject);
        }
    }
}