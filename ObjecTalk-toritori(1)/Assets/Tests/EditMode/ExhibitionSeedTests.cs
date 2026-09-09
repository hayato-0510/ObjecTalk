using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ObjecTalk.Infrastructure.Database;
using SQLite;

namespace ObjecTalk.Tests.EditMode
{
    [TestFixture]
    public sealed class ExhibitionSeedTests
    {
        private string _databasePath;

        [SetUp]
        public void SetUp()
        {
            _databasePath = Path.Combine(Path.GetTempPath(), $"objectalk-seed-{Guid.NewGuid():N}.sqlite");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = _databasePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void IsSeed_DefaultsToFalse_AndRoundTrips()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
                database.SaveObjectWithFeatures(CreateObject("visitor-1", isSeed: false), new List<string>());
                database.SaveObjectWithFeatures(CreateObject("seed-1", isSeed: true), new List<string>());
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<ObjectItem>("visitor-1").IsSeed, Is.False);
                Assert.That(connection.Find<ObjectItem>("seed-1").IsSeed, Is.True);
            }
        }

        [Test]
        public void MarkAllObjectsAsSeed_SetsEveryExistingRow()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
                database.SaveObjectWithFeatures(CreateObject("a", isSeed: false), new List<string>());
                database.SaveObjectWithFeatures(CreateObject("b", isSeed: false), new List<string>());

                database.MarkAllObjectsAsSeed();
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<ObjectItem>("a").IsSeed, Is.True);
                Assert.That(connection.Find<ObjectItem>("b").IsSeed, Is.True);
            }
        }

        [Test]
        public void ClearVisitorRegistrations_RemovesNonSeedOnly_AndKeepsUser()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
                database.SaveObjectWithFeatures(CreateObject("seed-1", isSeed: true), new List<string> { "電子機器" });
                database.SaveObjectWithFeatures(CreateObject("visitor-1", isSeed: false), new List<string> { "文房具" });
                database.RecognitionSamples.SaveSamples("seed-1", CreateSamples());
                database.RecognitionSamples.SaveSamples("visitor-1", CreateSamples());

                database.ClearVisitorRegistrations();
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<User>("user-1"), Is.Not.Null);
                Assert.That(connection.Find<ObjectItem>("seed-1"), Is.Not.Null);
                Assert.That(connection.Find<ObjectItem>("visitor-1"), Is.Null);
                Assert.That(
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recognition_samples WHERE object_id = ?", "visitor-1"),
                    Is.Zero);
                Assert.That(
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recognition_samples WHERE object_id = ?", "seed-1"),
                    Is.EqualTo(3));
                Assert.That(
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM object_features WHERE object_id = ?", "visitor-1"),
                    Is.Zero);
            }
        }

        [Test]
        public void ClearVisitorRegistrations_TreatsLegacyNullSeedAsVisitor()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
                database.SaveObjectWithFeatures(CreateObject("legacy-1", isSeed: false), new List<string>());
            }

            // Simulate a row written before the is_seed column existed.
            using (var connection = new SQLiteConnection(_databasePath))
            {
                connection.Execute("UPDATE objects SET is_seed = NULL WHERE object_id = ?", "legacy-1");
            }

            using (var database = new DatabaseManager(_databasePath))
            {
                database.ClearVisitorRegistrations();
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<ObjectItem>("legacy-1"), Is.Null);
            }
        }

        private static User CreateUser()
        {
            return new User
            {
                user_id = "user-1",
                Name = "exhibition",
                DateOfBirth = "2000-01-01",
                GmailAddress = "exhibition@example.invalid",
                IsCalendarSyncEnabled = false,
                IsWeatherSyncEnabled = false
            };
        }

        private static ObjectItem CreateObject(string objectId, bool isSeed)
        {
            return new ObjectItem
            {
                object_id = objectId,
                user_id = "user-1",
                Name = objectId,
                ImageData = new byte[] { 1, 2, 3 },
                ImageWidth = 320,
                ImageHeight = 240,
                EyeStyleId = -1,
                MouthStyleId = -1,
                IsSeed = isSeed
            };
        }

        private static RecognitionSampleRecord[] CreateSamples()
        {
            RecognitionSampleRecord Make(int index) => new RecognitionSampleRecord(
                index,
                new RecognitionDataVersion(1, "model-v2", "rgb-224-v1"),
                new[] { 1f, 2f, 3f, 4f },
                new byte[] { 1, 2, 3, 4 },
                2,
                3,
                0,
                new byte[] { 1, 2, 3, 4, 5, 6 },
                320,
                240);

            return new[] { Make(0), Make(1), Make(2) };
        }
    }
}
