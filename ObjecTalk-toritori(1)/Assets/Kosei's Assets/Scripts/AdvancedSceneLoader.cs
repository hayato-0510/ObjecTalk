using UnityEngine;
using UnityEngine.SceneManagement;

public class AdvancedSceneLoader : MonoBehaviour
{
    // Inspectorから "シーン名/パネル名" の形式で文字列を受け取る
    public void LoadSceneAndSetPanel(string sceneAndPanel)
    {
        // 文字列を "/" で分割
        string[] parts = sceneAndPanel.Split('/');
        if (parts.Length != 2)
        {
            Debug.LogError("引数の形式が間違っています。'シーン名/パネル名' の形式で指定してください。");
            return;
        }

        string sceneName = parts[0];
        string panelName = parts[1];

        // 運び屋に情報をセット
        SceneTransitionData.TargetPanelName = panelName;

        // シーンをロード
        SceneManager.LoadScene(sceneName);


    }
}