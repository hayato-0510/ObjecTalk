using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ObjecTalk.Domain.Conversation;
using ObjecTalk.Infrastructure.Conversation;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class ConversationObjectRepositoryTests
    {
        private string _databasePath;

        [SetUp]
        public void SetUp()
        {
            _databasePath = Path.Combine(Path.GetTempPath(), $"objectalk-conversation-{Guid.NewGuid():N}.sqlite");
        }

        [TearDown]
        public void TearDown()
        {
            DeleteIfPresent(_databasePath);
            DeleteIfPresent(_databasePath + "-shm");
            DeleteIfPresent(_databasePath + "-wal");
        }

        [Test]
        public void GetContext_OwnedPersistedObject_ReturnsJapaneseConversationContext()
        {
            using (var database = CreateDatabase())
            {
                SaveObject(database, "object-1", "user-1", "りんご", "明るく親しみやすい口調で話す");

                var repository = new ConversationObjectRepository(database, () => "user-1");

                ConversationContext context = repository.GetContext("object-1");

                Assert.That(context, Is.Not.Null);
                Assert.That(context.ObjectId, Is.EqualTo("object-1"));
                Assert.That(context.ObjectName, Is.EqualTo("りんご"));
                Assert.That(context.PersonalityPrompt, Is.EqualTo("明るく親しみやすい口調で話す"));
                Assert.That(context.UserId, Is.EqualTo("user-1"));
                Assert.That(context.Language, Is.EqualTo("ja"));
            }
        }

        [Test]
        public void GetContext_MissingObject_ReturnsNull()
        {
            using (var database = CreateDatabase())
            {
                var repository = new ConversationObjectRepository(database, () => "user-1");

                Assert.That(repository.GetContext("missing"), Is.Null);
            }
        }

        [Test]
        public void GetContext_ObjectOwnedByAnotherUser_ReturnsNull()
        {
            using (var database = CreateDatabase())
            {
                SaveObject(database, "object-1", "user-2", "本", "静かに説明する");
                var repository = new ConversationObjectRepository(database, () => "user-1");

                Assert.That(repository.GetContext("object-1"), Is.Null);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void GetContext_AbsentOrBlankCurrentUser_ReturnsNull(string userId)
        {
            using (var database = CreateDatabase())
            {
                SaveObject(database, "object-1", "user-1", "本", "静かに説明する");
                var repository = new ConversationObjectRepository(database, () => userId);

                Assert.That(repository.GetContext("object-1"), Is.Null);
            }
        }

        [Test]
        public void GetContext_NullPersonality_NormalizesToEmptyString()
        {
            using (var database = CreateDatabase())
            {
                SaveObject(database, "object-1", "user-1", "本", null);
                var repository = new ConversationObjectRepository(database, () => "user-1");

                ConversationContext context = repository.GetContext("object-1");

                Assert.That(context, Is.Not.Null);
                Assert.That(context.PersonalityPrompt, Is.Empty);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void GetContext_BlankObjectId_ReturnsNull(string objectId)
        {
            using (var database = CreateDatabase())
            {
                var repository = new ConversationObjectRepository(database, () => "user-1");

                Assert.That(repository.GetContext(objectId), Is.Null);
            }
        }

        private DatabaseManager CreateDatabase()
        {
            return new DatabaseManager(_databasePath);
        }

        private static void SaveObject(
            DatabaseManager database,
            string objectId,
            string userId,
            string objectName,
            string personalityPrompt)
        {
            database.SaveObjectWithFeatures(
                new ObjectItem
                {
                    object_id = objectId,
                    user_id = userId,
                    Name = objectName,
                    PersonalityPrompt = personalityPrompt
                },
                new List<string>());
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
