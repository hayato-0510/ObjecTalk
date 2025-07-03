using SQLite; // sqlite-netライブラリを使用するためのusingディレクティブ
using System.IO;
using UnityEngine;
using System.Collections.Generic; // Listを使うために追加
using System.Linq; // LINQを使うために追加

[Table("users")] // ★変更: SQLの慣習に合わせてテーブル名を小文字複数形に
public class User
{
    // ★変更: Firebase UID に合わせるため、IDをstring型に変更
    [PrimaryKey]
    public string user_id { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("age")]
    public int Age { get; set; }

    [Column("gmail_address")]
    public string GmailAddress { get; set; }

    // ★変更: JSON形式ではなく、独立したカラムとして設定を管理
    [Column("is_calendar_sync_enabled")]
    public bool IsCalendarSyncEnabled { get; set; }

    [Column("is_weather_sync_enabled")]
    public bool IsWeatherSyncEnabled { get; set; }
}

[Table("objects")] // ★変更: テーブル名を小文字複数形に
public class ObjectItem
{
    // ★変更: UUIDを想定し、IDをstring型に変更
    [PrimaryKey]
    public string object_id { get; set; }

    [Indexed] // 検索を高速化するためのインデックス
    public string user_id { get; set; }

    [Column("name")]
    public string Name { get; set; }

    // ★変更: "kind"から"category"に名称変更
    [Column("category")]
    public string Category { get; set; }

    // ★追加: AIが生成した生の説明文を保存するカラム
    [Column("ai_description")]
    public string AiDescription { get; set; }

    [Column("image_url")]
    public string ImageURL { get; set; }

    // ★削除: featuresカラムは不要。下の関連テーブルで管理する。

    // ★追加: 実際のプログラムでオブジェクトと特徴を一緒に扱うためのプロパティ
    [Ignore]
    public List<Feature> Features { get; set; } = new List<Feature>();
}

// ★追加: 特徴タグのマスターリストを管理するテーブル
[Table("features")]
public class Feature
{
    [PrimaryKey, AutoIncrement]
    public int feature_id { get; set; }

    [Unique] // 特徴名は重複しない
    public string name { get; set; }
}

// ★追加: ObjectとFeatureの多対多関係を実現するための中間テーブル
[Table("object_features")]
public class ObjectFeature
{
    // 複合主キーの代わりに、両方のカラムにインデックスを付けて高速化
    [Indexed]
    public string object_id { get; set; }

    [Indexed]
    public int feature_id { get; set; }
}

public class DatabaseManager
{
    private SQLiteConnection _connection;

    public DatabaseManager()
    {
        string dbPath = Path.Combine(Application.persistentDataPath, "objecTalk.sqlite");
        _connection = new SQLiteConnection(dbPath);

        // ★変更: 新しいテーブルをすべて作成
        _connection.CreateTable<User>();
        _connection.CreateTable<ObjectItem>();
        _connection.CreateTable<Feature>();
        _connection.CreateTable<ObjectFeature>();

        Debug.Log($"データベースのパス: {dbPath}");
    }

    // ----- ユーザー関連メソッド -----

    public void SaveUser(User user)
    {
        _connection.InsertOrReplace(user);
        Debug.Log($"ユーザー(ID: {user.user_id})の情報を保存しました。");
    }

    // ★変更: IDの型をstringに変更
    public User GetUser(string userId)
    {
        return _connection.Find<User>(userId);
    }

    // ----- モノと特徴の関連メソッド（最重要） -----

    /// <summary>
    /// モノとその特徴タグをまとめて保存します。
    /// </summary>
    /// <param name="item">保存するモノのデータ</param>
    /// <param name="featureNames">関連付ける特徴タグ名のリスト</param>
    public void SaveObjectWithFeatures(ObjectItem item, List<string> featureNames)
    {
        // トランザクションを開始して、処理の原子性を保証
        _connection.BeginTransaction();
        try
        {
            // 1. モノ自体の情報を保存
            _connection.InsertOrReplace(item);

            // 2. 既存の関連付けを一旦削除（更新のため）
            _connection.Table<ObjectFeature>().Delete(of => of.object_id == item.object_id);

            // 3. 特徴タグを処理し、関連付けを保存
            foreach (var tagName in featureNames)
            {
                if (string.IsNullOrWhiteSpace(tagName)) continue;

                // 特徴タグがfeaturesテーブルになければ作成
                var feature = GetOrCreateFeature(tagName);

                // object_features中間テーブルに新しい関連付けを保存
                var objectFeature = new ObjectFeature
                {
                    object_id = item.object_id,
                    feature_id = feature.feature_id
                };
                _connection.Insert(objectFeature);
            }

            // すべての処理が成功したらコミット
            _connection.Commit();
            Debug.Log($"モノ(ID: {item.object_id})と特徴({featureNames.Count}件)を保存しました。");
        }
        catch (System.Exception ex)
        {
            // エラーが発生したらロールバック
            _connection.Rollback();
            Debug.LogError($"モノの保存中にエラーが発生しました: {ex.Message}");
            throw; // エラーを呼び出し元に伝える
        }
    }

    /// <summary>
    /// 指定されたIDのモノの情報を、関連する特徴リスト付きで取得します。
    /// </summary>
    public ObjectItem GetObjectWithDetails(string objectId)
    {
        var item = _connection.Find<ObjectItem>(objectId);
        if (item == null) return null;

        // 関連するfeature_idを中間テーブルから取得
        var featureIds = _connection.Table<ObjectFeature>()
                                    .Where(of => of.object_id == objectId)
                                    .Select(of => of.feature_id)
                                    .ToList();

        if (featureIds.Any())
        {
            // feature_idのリストを使って、featuresテーブルから特徴情報を一括取得
            item.Features = _connection.Table<Feature>()
                                       .Where(f => featureIds.Contains(f.feature_id))
                                       .ToList();
        }

        return item;
    }

    /// <summary>
    /// 指定されたユーザーが所有するすべてのモノの情報を、特徴リスト付きで取得します。
    /// </summary>
    public List<ObjectItem> GetAllUserObjectsWithDetails(string userId)
    {
        var items = _connection.Table<ObjectItem>().Where(o => o.user_id == userId).ToList();
        foreach (var item in items)
        {
            // 上のメソッドを再利用して各アイテムに特徴をセット
            var itemWithDetails = GetObjectWithDetails(item.object_id);
            if (itemWithDetails != null)
            {
                item.Features = itemWithDetails.Features;
            }
        }
        return items;
    }


    // ----- ヘルパーメソッド -----

    /// <summary>
    /// 特徴タグ名からFeatureオブジェクトを取得します。存在しない場合は作成します。
    /// </summary>
    private Feature GetOrCreateFeature(string tagName)
    {
        var feature = _connection.Table<Feature>().FirstOrDefault(f => f.name == tagName);
        if (feature == null)
        {
            feature = new Feature { name = tagName };
            _connection.Insert(feature);
        }
        return feature;
    }

    // DatabaseManagerクラスに以下のメソッドを追加してください

    /// <summary>
    /// すべてのユーザーを取得します
    /// </summary>
    public List<User> GetAllUsers()
    {
        return _connection.Table<User>().ToList();
    }

    /// <summary>
    /// すべての特徴タグを取得します
    /// </summary>
    public List<Feature> GetAllFeatures()
    {
        return _connection.Table<Feature>().ToList();
    }

    /// <summary>
    /// すべてのオブジェクトを取得します
    /// </summary>
    public List<ObjectItem> GetAllObjects()
    {
        return _connection.Table<ObjectItem>().ToList();
    }

    /// <summary>
    /// すべてのオブジェクト-特徴関連付けを取得します
    /// </summary>
    public List<ObjectFeature> GetAllObjectFeatures()
    {
        return _connection.Table<ObjectFeature>().ToList();
    }

    /// <summary>
    /// データベースの統計情報を取得します
    /// </summary>
    public string GetDatabaseStats()
    {
        var userCount = _connection.Table<User>().Count();
        var objectCount = _connection.Table<ObjectItem>().Count();
        var featureCount = _connection.Table<Feature>().Count();
        var relationCount = _connection.Table<ObjectFeature>().Count();

        return $"ユーザー: {userCount}, オブジェクト: {objectCount}, 特徴: {featureCount}, 関連付け: {relationCount}";
    }

    /// <summary>
    /// すべてのテーブルのデータを削除します（危険な操作）
    /// </summary>
    public void ClearAllTables()
    {
        _connection.BeginTransaction();
        try
        {
            // 関連テーブルから先に削除（外部キー制約を考慮）
            _connection.DeleteAll<ObjectFeature>();
            _connection.DeleteAll<ObjectItem>();
            _connection.DeleteAll<Feature>();
            _connection.DeleteAll<User>();

            _connection.Commit();
            Debug.Log("全テーブルのデータが削除されました。");
        }
        catch (System.Exception ex)
        {
            _connection.Rollback();
            Debug.LogError($"データベースクリア中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 特定のユーザーのデータをすべて削除します
    /// </summary>
    public void ClearUserData(string userId)
    {
        _connection.BeginTransaction();
        try
        {
            // ユーザーのオブジェクトIDを取得
            var userObjectIds = _connection.Table<ObjectItem>()
                                          .Where(o => o.user_id == userId)
                                          .Select(o => o.object_id)
                                          .ToList();

            // オブジェクト-特徴関連を削除
            foreach (var objectId in userObjectIds)
            {
                _connection.Table<ObjectFeature>().Delete(of => of.object_id == objectId);
            }

            // ユーザーのオブジェクトを削除
            _connection.Table<ObjectItem>().Delete(o => o.user_id == userId);

            // ユーザー自体を削除
            _connection.Delete<User>(userId);

            _connection.Commit();
            Debug.Log($"ユーザー {userId} のデータがすべて削除されました。");
        }
        catch (System.Exception ex)
        {
            _connection.Rollback();
            Debug.LogError($"ユーザーデータ削除中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }
}