using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq; // LINQを使用するために追加

public class DatabaseDebugger : MonoBehaviour
{
    [Header("UI References")]
    public Button checkDatabaseButton;
    public Button clearDatabaseButton;
    public Button showEmailsButton; // メールアドレス表示ボタン
    public Button showFeaturesButton; // ★新規追加：特徴タグ表示ボタン
    public Button showObjectsWithFeaturesButton; // ★新規追加：オブジェクトと特徴の関連表示ボタン
    public Button insertTestDataButton; // ★新規追加：テストデータ挿入ボタン
    public Button backToLoginButton; // ★新規追加：ログイン画面に戻るボタン
    public TMP_Text debugOutputText;
    public ScrollRect scrollRect;

    private DatabaseManager databaseManager;

    void Start()
    {
        databaseManager = new DatabaseManager();

        // ボタンにリスナーを登録
        if (checkDatabaseButton != null)
        {
            checkDatabaseButton.onClick.AddListener(CheckDatabase);
        }

        if (clearDatabaseButton != null)
        {
            clearDatabaseButton.onClick.AddListener(ClearDatabase);
        }

        if (showEmailsButton != null)
        {
            showEmailsButton.onClick.AddListener(ShowAllEmailAddresses);
        }

        // ★新規追加：新しいボタンのリスナー登録
        if (showFeaturesButton != null)
        {
            showFeaturesButton.onClick.AddListener(ShowAllFeatures);
        }

        if (showObjectsWithFeaturesButton != null)
        {
            showObjectsWithFeaturesButton.onClick.AddListener(ShowObjectsWithFeatures);
        }

        if (backToLoginButton != null)
        {
            backToLoginButton.onClick.AddListener(BackToLogin);
        }

        UpdateDebugText("データベースデバッガーが初期化されました。");
    }

    /// <summary>
    /// データベース内の全ユーザーのメールアドレスのみを一覧表示する
    /// </summary>
    [ContextMenu("全メールアドレス表示")]
    public void ShowAllEmailAddresses()
    {
        try
        {
            string output = "=== 登録メールアドレス一覧 ===\n\n";

            // DatabaseManagerのメソッドを使用
            List<User> allUsers = databaseManager.GetAllUsers();

            if (allUsers.Count == 0)
            {
                output += "データベースにユーザーが登録されていません。\n";
            }
            else
            {
                // LINQのSelectを使ってメールアドレスのリストを効率的に取得
                List<string> emailList = allUsers.Select(user => user.GmailAddress).ToList();

                // 取得したメールアドレスを番号付きで表示
                for (int i = 0; i < emailList.Count; i++)
                {
                    output += $"{i + 1}: {emailList[i]}\n";
                }
            }

            output += $"\n({allUsers.Count}件のメールアドレスが見つかりました)";
            UpdateDebugText(output);
            Debug.Log("全メールアドレスの表示完了");
        }
        catch (System.Exception e)
        {
            string errorOutput = $"メールアドレス一覧の取得中にエラーが発生しました:\n{e.Message}\n\nスタックトレース:\n{e.StackTrace}";
            UpdateDebugText(errorOutput);
            Debug.LogError($"メールアドレス一覧取得エラー: {e}");
        }
    }

    /// <summary>
    /// ★新規追加：データベース内のすべての特徴タグを表示する
    /// </summary>
    [ContextMenu("全特徴タグ表示")]
    public void ShowAllFeatures()
    {
        try
        {
            string output = "=== 登録特徴タグ一覧 ===\n\n";

            List<Feature> allFeatures = databaseManager.GetAllFeatures();

            if (allFeatures.Count == 0)
            {
                output += "データベースに特徴タグが登録されていません。\n";
            }
            else
            {
                for (int i = 0; i < allFeatures.Count; i++)
                {
                    Feature feature = allFeatures[i];
                    output += $"{i + 1}: ID={feature.feature_id}, 名前='{feature.name}'\n";
                }
            }

            output += $"\n({allFeatures.Count}件の特徴タグが見つかりました)";
            UpdateDebugText(output);
            Debug.Log("全特徴タグの表示完了");
        }
        catch (System.Exception e)
        {
            string errorOutput = $"特徴タグ一覧の取得中にエラーが発生しました:\n{e.Message}\n\nスタックトレース:\n{e.StackTrace}";
            UpdateDebugText(errorOutput);
            Debug.LogError($"特徴タグ一覧取得エラー: {e}");
        }
    }

    /// <summary>
    /// ★新規追加：オブジェクトと特徴の関連付けを詳細表示する
    /// </summary>
    [ContextMenu("オブジェクトと特徴の関連表示")]
    public void ShowObjectsWithFeatures()
    {
        try
        {
            string output = "=== オブジェクトと特徴の関連 ===\n\n";

            List<ObjectItem> allObjects = databaseManager.GetAllObjects();

            if (allObjects.Count == 0)
            {
                output += "データベースにオブジェクトが登録されていません。\n";
            }
            else
            {
                for (int i = 0; i < allObjects.Count; i++)
                {
                    ObjectItem obj = allObjects[i];
                    // 特徴付きでオブジェクトを取得
                    ObjectItem objWithFeatures = databaseManager.GetObjectWithDetails(obj.object_id);

                    output += $"--- オブジェクト {i + 1} ---\n";
                    output += $"ID: {obj.object_id}\n";
                    output += $"名前: '{obj.Name}'\n";
                    output += $"カテゴリ: '{obj.Category}'\n";
                    output += $"ユーザーID: {obj.user_id}\n";

                    if (objWithFeatures?.Features != null && objWithFeatures.Features.Count > 0)
                    {
                        output += $"特徴タグ ({objWithFeatures.Features.Count}件): ";
                        output += string.Join(", ", objWithFeatures.Features.Select(f => f.name));
                        output += "\n";
                    }
                    else
                    {
                        output += "特徴タグ: なし\n";
                    }
                    output += "\n";
                }
            }

            output += $"({allObjects.Count}件のオブジェクトが見つかりました)";
            UpdateDebugText(output);
            Debug.Log("オブジェクトと特徴の関連表示完了");
        }
        catch (System.Exception e)
        {
            string errorOutput = $"オブジェクト関連情報の取得中にエラーが発生しました:\n{e.Message}\n\nスタックトレース:\n{e.StackTrace}";
            UpdateDebugText(errorOutput);
            Debug.LogError($"オブジェクト関連情報取得エラー: {e}");
        }
    }

    /// <summary>
    /// データベース内の全ユーザー情報を確認する（★設定情報の表示を新構造に更新）
    /// </summary>
    [ContextMenu("データベース確認")]
    public void CheckDatabase()
    {
        try
        {
            string output = "=== データベース確認結果 ===\n\n";

            // DatabaseManagerの統計情報を使用
            output += $"統計: {databaseManager.GetDatabaseStats()}\n\n";

            List<User> allUsers = databaseManager.GetAllUsers();
            if (allUsers.Count == 0)
            {
                output += "データベースにユーザーが登録されていません。\n";
            }
            else
            {
                output += $"登録されているユーザー数: {allUsers.Count}\n\n";
                for (int i = 0; i < allUsers.Count; i++)
                {
                    User user = allUsers[i];
                    output += $"--- ユーザー {i + 1} ---\n";
                    output += $"ID: {user.user_id}\n";
                    output += $"名前: '{user.Name}'\n";
                    output += $"年齢: {user.Age}\n";
                    output += $"メールアドレス: '{user.GmailAddress}'\n";
                    // ★変更：新しい設定項目を表示
                    output += $"カレンダー連携: {(user.IsCalendarSyncEnabled ? "有効" : "無効")}\n";
                    output += $"天気連携: {(user.IsWeatherSyncEnabled ? "有効" : "無効")}\n";
                    output += "\n";
                }
            }

            List<ObjectItem> allObjects = databaseManager.GetAllObjects();
            output += $"登録されているオブジェクト数: {allObjects.Count}\n";

            List<Feature> allFeatures = databaseManager.GetAllFeatures();
            output += $"登録されている特徴タグ数: {allFeatures.Count}\n";

            List<ObjectFeature> allRelations = databaseManager.GetAllObjectFeatures();
            output += $"オブジェクト-特徴関連数: {allRelations.Count}\n";

            output += "\n=== 確認完了 ===";
            UpdateDebugText(output);
            Debug.Log("データベース確認完了");
        }
        catch (System.Exception e)
        {
            string errorOutput = $"データベース確認中にエラーが発生しました:\n{e.Message}\n\nスタックトレース:\n{e.StackTrace}";
            UpdateDebugText(errorOutput);
            Debug.LogError($"データベース確認エラー: {e}");
        }
    }

    /// <summary>
    /// データベースをクリアする（★新しいテーブル構造に対応）
    /// </summary>
    [ContextMenu("データベースクリア")]
    public void ClearDatabase()
    {
        try
        {
            // 削除前の統計を取得
            string beforeStats = databaseManager.GetDatabaseStats();

            // DatabaseManagerのメソッドを使用してクリア
            databaseManager.ClearAllTables();

            string output = $"=== データベースクリア完了 ===\n\n";
            output += $"削除前の統計: {beforeStats}\n";
            output += "\nすべてのデータが削除されました。";

            UpdateDebugText(output);
            Debug.Log("データベースをクリアしました");
        }
        catch (System.Exception e)
        {
            string errorOutput = $"データベースクリア中にエラーが発生しました:\n{e.Message}";
            UpdateDebugText(errorOutput);
            Debug.LogError($"データベースクリアエラー: {e}");
        }
    }

    /// <summary>
    /// ★新規追加：ログイン画面に戻る
    /// </summary>
    [ContextMenu("ログイン画面に戻る")]
    public void BackToLogin()
    {
        try
        {
            // シーン遷移を実行
            UnityEngine.SceneManagement.SceneManager.LoadScene("LoginScene");
            Debug.Log("ログイン画面に遷移します");
        }
        catch (System.Exception e)
        {
            string errorOutput = $"ログイン画面への遷移中にエラーが発生しました:\n{e.Message}";
            UpdateDebugText(errorOutput);
            Debug.LogError($"画面遷移エラー: {e}");
        }
    }

    private void UpdateDebugText(string text)
    {
        if (debugOutputText != null)
        {
            debugOutputText.text = text;
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
        Debug.Log($"[DatabaseDebugger] {text}");
    }
}