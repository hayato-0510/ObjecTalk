using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlexibleUIManager : MonoBehaviour
{
    [Tooltip("管理したい全てのパネルをここに登録します")]
    public List<GameObject> allPanels;
    [Tooltip("最初に表示するパネルを指定します")]
    public GameObject initialPanel;

    [Header("デバッグ情報")]
    [Tooltip("現在アクティブなパネル")]
    public GameObject currentActivePanel;

    void Start()
    {
        if (!string.IsNullOrEmpty(SceneTransitionData.TargetPanelName))
        {
            // 名前でパネルをリストから検索
            GameObject panelToShow = allPanels.Find(p => p.name == SceneTransitionData.TargetPanelName);
            if (panelToShow != null)
            {
                ShowPanel(panelToShow);
                // 情報を使い終わったので、運び屋のメモを空にする
                SceneTransitionData.TargetPanelName = null;
                return; // ここで初期化処理を終了
            }
            else
            {
                Debug.LogWarning($"指定されたパネル '{SceneTransitionData.TargetPanelName}' が見つかりません");
                SceneTransitionData.TargetPanelName = null; // 見つからなかったのでクリア
            }
        }
        // 念のため、全てのパネルを一度非表示にする
        foreach (GameObject panel in allPanels)
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        // 最初に表示したいパネルだけを表示する
        if (initialPanel != null)
        {
            ShowPanel(initialPanel);
        }
    }

    /// <summary>
    /// 指定されたパネルを表示し、他のパネルを非表示にします
    /// </summary>
    /// <param name="panelToShow">表示したいパネルのGameObject</param>
    public void ShowPanel(GameObject panelToShow)
    {
        if (panelToShow == null)
        {
            Debug.LogWarning("ShowPanel: 表示しようとしたパネルがnullです");
            return;
        }

        // 管理しているパネルかどうかをチェック
        if (!allPanels.Contains(panelToShow))
        {
            Debug.LogWarning($"ShowPanel: パネル '{panelToShow.name}' は管理対象に含まれていません");
            // 管理対象に追加
            allPanels.Add(panelToShow);
        }

        // 管理している全てのパネルをループして…
        foreach (GameObject panel in allPanels)
        {
            if (panel != null)
            {
                // 表示したいパネルかどうかを判定
                bool isActive = (panel == panelToShow);
                // 表示する/しないを設定
                panel.SetActive(isActive);
            }
        }

        // 現在のアクティブパネルを更新
        currentActivePanel = panelToShow;

        Debug.Log($"UIManager: パネル '{panelToShow.name}' を表示しました");
    }

    /// <summary>
    /// 現在アクティブなパネルを取得します
    /// </summary>
    /// <returns>現在アクティブなパネル</returns>
    public GameObject GetCurrentActivePanel()
    {
        return currentActivePanel;
    }

    /// <summary>
    /// 指定されたパネルが現在アクティブかどうかを確認します
    /// </summary>
    /// <param name="panel">確認したいパネル</param>
    /// <returns>アクティブな場合true</returns>
    public bool IsPanelActive(GameObject panel)
    {
        return currentActivePanel == panel;
    }

    /// <summary>
    /// 全てのパネルを非表示にします
    /// </summary>
    public void HideAllPanels()
    {
        foreach (GameObject panel in allPanels)
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }
        currentActivePanel = null;
        Debug.Log("UIManager: 全てのパネルを非表示にしました");
    }

    /// <summary>
    /// パネルを管理対象に追加します
    /// </summary>
    /// <param name="panel">追加したいパネル</param>
    public void AddPanel(GameObject panel)
    {
        if (panel != null && !allPanels.Contains(panel))
        {
            allPanels.Add(panel);
            Debug.Log($"UIManager: パネル '{panel.name}' を管理対象に追加しました");
        }
    }

    /// <summary>
    /// パネルを管理対象から削除します
    /// </summary>
    /// <param name="panel">削除したいパネル</param>
    public void RemovePanel(GameObject panel)
    {
        if (panel != null && allPanels.Contains(panel))
        {
            allPanels.Remove(panel);
            if (currentActivePanel == panel)
            {
                currentActivePanel = null;
            }
            Debug.Log($"UIManager: パネル '{panel.name}' を管理対象から削除しました");
        }
    }
}