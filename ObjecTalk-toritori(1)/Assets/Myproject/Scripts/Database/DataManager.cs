using SQLite;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[Table("users")]
public class User
{
    [PrimaryKey]
    public string user_id { get; set; }
    [Column("name")]
    public string Name { get; set; }
    [Column("date_of_birth")]
    public string DateOfBirth { get; set; }
    [Column("gmail_address")]
    public string GmailAddress { get; set; }
    [Column("is_calendar_sync_enabled")]
    public bool IsCalendarSyncEnabled { get; set; }
    [Column("is_weather_sync_enabled")]
    public bool IsWeatherSyncEnabled { get; set; }
}

[Table("objects")]
public class ObjectItem
{
    [PrimaryKey]
    public string object_id { get; set; }
    [Indexed]
    public string user_id { get; set; }
    [Column("name")]
    public string Name { get; set; }
    [Column("category")]
    public string Category { get; set; }
    [Column("ai_description")]
    public string AiDescription { get; set; }
    [Column("image_url")]
    public string ImageURL { get; set; }
    [Ignore]
    public List<Feature> Features { get; set; } = new List<Feature>();
}

[Table("features")]
public class Feature
{
    [PrimaryKey, AutoIncrement]
    public int feature_id { get; set; }
    [Unique]
    public string name { get; set; }
}

[Table("object_features")]
public class ObjectFeature
{
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
        _connection.CreateTable<User>();
        _connection.CreateTable<ObjectItem>();
        _connection.CreateTable<Feature>();
        _connection.CreateTable<ObjectFeature>();
        Debug.Log($"データベースのパス: {dbPath}");
    }

    // ----- 基本的なデータ操作メソッド -----

    public void SaveUser(User user)
    {
        _connection.InsertOrReplace(user);
    }

    public User GetUser(string userId)
    {
        return _connection.Find<User>(userId);
    }

    public void SaveObjectWithFeatures(ObjectItem item, List<string> featureNames)
    {
        _connection.BeginTransaction();
        try
        {
            _connection.InsertOrReplace(item);
            _connection.Table<ObjectFeature>().Delete(of => of.object_id == item.object_id);

            foreach (var tagName in featureNames)
            {
                if (string.IsNullOrWhiteSpace(tagName)) continue;
                var feature = GetOrCreateFeature(tagName);
                var objectFeature = new ObjectFeature
                {
                    object_id = item.object_id,
                    feature_id = feature.feature_id
                };
                _connection.Insert(objectFeature);
            }
            _connection.Commit();
        }
        catch (System.Exception ex)
        {
            _connection.Rollback();
            Debug.LogError($"モノの保存中にエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    public ObjectItem GetObjectWithDetails(string objectId)
    {
        var item = _connection.Find<ObjectItem>(objectId);
        if (item == null) return null;

        var featureIds = _connection.Table<ObjectFeature>()
                                    .Where(of => of.object_id == objectId)
                                    .Select(of => of.feature_id)
                                    .ToList();
        if (featureIds.Any())
        {
            item.Features = _connection.Table<Feature>()
                                       .Where(f => featureIds.Contains(f.feature_id))
                                       .ToList();
        }
        return item;
    }

    public List<ObjectItem> GetAllUserObjectsWithDetails(string userId)
    {
        var items = _connection.Table<ObjectItem>().Where(o => o.user_id == userId).ToList();
        foreach (var item in items)
        {
            var itemWithDetails = GetObjectWithDetails(item.object_id);
            if (itemWithDetails != null)
            {
                item.Features = itemWithDetails.Features;
            }
        }
        return items;
    }

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

    // ===== ここから下が不足しているメソッド群です =====

    public List<User> GetAllUsers()
    {
        return _connection.Table<User>().ToList();
    }

    public List<Feature> GetAllFeatures()
    {
        return _connection.Table<Feature>().ToList();
    }

    public List<ObjectItem> GetAllObjects()
    {
        return _connection.Table<ObjectItem>().ToList();
    }

    public List<ObjectFeature> GetAllObjectFeatures()
    {
        return _connection.Table<ObjectFeature>().ToList();
    }

    public string GetDatabaseStats()
    {
        var userCount = _connection.Table<User>().Count();
        var objectCount = _connection.Table<ObjectItem>().Count();
        var featureCount = _connection.Table<Feature>().Count();
        var relationCount = _connection.Table<ObjectFeature>().Count();
        return $"ユーザー: {userCount}, オブジェクト: {objectCount}, 特徴: {featureCount}, 関連付け: {relationCount}";
    }

    public void ClearAllTables()
    {
        _connection.BeginTransaction();
        try
        {
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
}