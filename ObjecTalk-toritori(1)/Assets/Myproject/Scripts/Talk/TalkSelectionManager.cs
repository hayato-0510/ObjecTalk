using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement; // ※シーンをまたぐ場合は必要

public class TalkSelectionManager : MonoBehaviour
{
    public Button registerNewButton; // 「新しいモノと」ボタンをInspectorで設定
    public Button talkAgainButton;   // 「もう一度」ボタンをInspectorで設定

    // (もし同じシーン内のパネル切り替えなら)
    public GameObject talkSelectionPanel;
    public GameObject objectMatcherPanel; // EmbeddingObjectMatcherがいるパネル

    void Start()
    {
        registerNewButton.onClick.AddListener(OnRegisterNew);
        talkAgainButton.onClick.AddListener(OnTalkAgain);
    }

    private void OnRegisterNew()
    {
        // 1. 起動モードを「登録」に設定
        EmbeddingObjectMatcher.DesiredMode = EmbeddingObjectMatcher.StartupMode.Register;

        // 2. EmbeddingObjectMatcher の画面に遷移
        LoadObjectMatcherScene();
    }

    private void OnTalkAgain()
    {
        // 1. 起動モードを「会話」に設定
        EmbeddingObjectMatcher.DesiredMode = EmbeddingObjectMatcher.StartupMode.Talk;

        // 2. EmbeddingObjectMatcher の画面に遷移
        LoadObjectMatcherScene();
    }

    private void LoadObjectMatcherScene()
    {
        // B: もし EmbeddingObjectMatcher が同じシーンの別パネルなら
        // (こちらを使う場合は A の SceneManager の行をコメントアウト)
        talkSelectionPanel.SetActive(false);
        objectMatcherPanel.SetActive(true);
    }
}